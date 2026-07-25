using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed class ClashBatchRunService
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, Operation> _operations = new Dictionary<string, Operation>(StringComparer.OrdinalIgnoreCase);
        private readonly Action<Action> _postToUi;
        private const int MaximumRetainedOperations = 128;

        public ClashBatchRunService(Action<Action> postToUi)
        {
            _postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));
        }

        public ClashRunBatchResponse Start(Document document, ClashRunBatchRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            request = request ?? new ClashRunBatchRequest();
            var tests = ClashApiCompat.GetClashTests(document.GetClash()).ToList();
            var selected = ResolveTests(tests, request);
            if (request.FirstN.HasValue)
                selected = selected.Take(Math.Max(0, request.FirstN.Value)).ToList();
            if (selected.Count == 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "No Clash Detective tests matched the requested run scope.");

            var batchSize = Math.Max(1, Math.Min(500, request.BatchSize.GetValueOrDefault(10)));
            var timeout = Math.Max(1, Math.Min(86400, request.PerTestTimeoutSeconds.GetValueOrDefault(300)));
            if (request.Apply != true)
            {
                return new ClashRunBatchResponse
                {
                    Applied = false,
                    State = "planned",
                    TotalTestCount = selected.Count,
                    RemainingTestCount = selected.Count,
                    BatchSize = batchSize,
                    PerTestTimeoutSeconds = timeout,
                    Message = "Dry-run only. Pass apply=true to start the asynchronous clash run.",
                    Tests = selected.Select((test, index) => new ClashRunTestResult
                    {
                        Index = index + 1,
                        TestName = test.DisplayName,
                        TestHandle = ClashHandleHelper.BuildTestHandle(tests.IndexOf(test) + 1),
                        Status = "pending",
                    }).ToList(),
                };
            }
            return StartNames(selected.Select(test => test.DisplayName), batchSize, timeout, document);
        }

        public ClashRunBatchResponse StartNames(IEnumerable<string> names, int batchSize, int timeoutSeconds, Document document = null)
        {
            document = document ?? Autodesk.Navisworks.Api.Application.ActiveDocument;
            var operation = new Operation
            {
                Id = "clash-run-" + Guid.NewGuid().ToString("N"),
                State = "running",
                BatchSize = Math.Max(1, Math.Min(500, batchSize <= 0 ? 10 : batchSize)),
                TimeoutSeconds = Math.Max(1, Math.Min(86400, timeoutSeconds <= 0 ? 300 : timeoutSeconds)),
                StartedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                DocumentKey = BuildDocumentKey(document),
            };
            var index = 0;
            foreach (var name in (names ?? Enumerable.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                operation.Tests.Add(new ClashRunTestResult { Index = ++index, TestName = name.Trim(), Status = "pending" });
            }
            if (operation.Tests.Count == 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "No created tests are available for runAfterCreate.");
            lock (_sync)
            {
                PruneOperations();
                _operations[operation.Id] = operation;
            }
            Schedule(operation);
            return Snapshot(operation);
        }

        public ClashRunBatchResponse Resume(ClashRunResumeRequest request)
        {
            var operation = Get(request == null ? null : request.OperationId);
            lock (_sync)
            {
                if (operation.State != "paused")
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Only a paused clash run can be resumed.");
                if (request.BatchSize.HasValue)
                    operation.BatchSize = Math.Max(1, Math.Min(500, request.BatchSize.Value));
                operation.BatchProcessed = 0;
                operation.State = "running";
                operation.UpdatedAtUtc = DateTime.UtcNow;
            }
            Schedule(operation);
            return Snapshot(operation);
        }

        public ClashRunBatchResponse Status(ClashRunStatusRequest request)
        {
            return Snapshot(Get(request == null ? null : request.OperationId));
        }

        public ClashRunBatchResponse Cancel(CancelClashRunRequest request)
        {
            var operation = Get(request == null ? null : request.OperationId);
            lock (_sync)
            {
                operation.CancelRequested = true;
                operation.UpdatedAtUtc = DateTime.UtcNow;
                if (operation.State == "paused")
                {
                    operation.State = "cancelled";
                    operation.CompletedAtUtc = DateTime.UtcNow;
                }
            }
            var response = Snapshot(operation);
            response.CancelAccepted = true;
            response.Message = response.IsRunning
                ? "Cancellation requested. The current synchronous Navisworks test cannot be interrupted; cancellation takes effect before the next test."
                : "Clash run cancelled.";
            return response;
        }

        private void ProcessNext(string id)
        {
            var operation = Get(id);
            ClashRunTestResult row;
            lock (_sync)
            {
                if (operation.CancelRequested)
                {
                    Complete(operation, "cancelled");
                    return;
                }
                row = operation.Tests.FirstOrDefault(test => test.Status == "pending");
                if (row == null)
                {
                    Complete(operation, "completed");
                    return;
                }
                if (operation.BatchProcessed >= operation.BatchSize)
                {
                    operation.State = "paused";
                    operation.UpdatedAtUtc = DateTime.UtcNow;
                    return;
                }
                row.Status = "running";
                operation.Current = row.TestName;
                operation.CurrentStartedAtUtc = DateTime.UtcNow;
                operation.UpdatedAtUtc = DateTime.UtcNow;
            }

            var stopwatch = Stopwatch.StartNew();
            var finalStatus = "completed";
            string finalError = null;
            try
            {
                var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (!string.Equals(BuildDocumentKey(document), operation.DocumentKey, StringComparison.Ordinal))
                    throw new DocumentChangedException("The active Navisworks document changed; the clash run was stopped without touching the new document.");
                var clash = document == null ? null : document.GetClash();
                var test = clash == null ? null : ClashApiCompat.GetClashTests(clash)
                    .FirstOrDefault(candidate => string.Equals(candidate.DisplayName, row.TestName, StringComparison.OrdinalIgnoreCase));
                if (test == null)
                    throw new InvalidOperationException("Clash test is no longer available.");
                ClashRunPreservationService.RunTestPreservingReviewState(clash.TestsData, test);
            }
            catch (Exception ex)
            {
                finalStatus = "failed";
                finalError = ex.Message;
                Logger.Error(row.TestName + ": asynchronous clash run failed: " + ex, "ClashMcp");
            }
            finally
            {
                stopwatch.Stop();
                lock (_sync)
                {
                    row.Status = finalStatus;
                    row.ErrorMessage = finalError;
                    row.ElapsedMs = stopwatch.ElapsedMilliseconds;
                    row.ExceededTimebox = stopwatch.Elapsed.TotalSeconds > operation.TimeoutSeconds;
                    operation.BatchProcessed++;
                    operation.Current = null;
                    operation.CurrentStartedAtUtc = null;
                    operation.UpdatedAtUtc = DateTime.UtcNow;
                    if (finalError != null && finalError.IndexOf("active Navisworks document changed", StringComparison.OrdinalIgnoreCase) >= 0)
                        Complete(operation, "failed");
                }
            }
            if (operation.State == "running")
                Schedule(operation);
        }

        private static List<ClashTest> ResolveTests(IList<ClashTest> all, ClashRunBatchRequest request)
        {
            var result = new List<ClashTest>();
            foreach (var handle in request.TestHandles ?? new List<string>())
            {
                int index;
                if (!ClashHandleHelper.TryParseTestHandle(handle, out index) || index < 1 || index > all.Count)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Invalid or stale clash test handle: " + handle);
                if (!result.Contains(all[index - 1])) result.Add(all[index - 1]);
            }
            foreach (var name in request.TestNames ?? new List<string>())
            {
                var test = all.FirstOrDefault(candidate => string.Equals(candidate.DisplayName, name, StringComparison.OrdinalIgnoreCase));
                if (test == null)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Clash test name was not found: " + name);
                if (!result.Contains(test)) result.Add(test);
            }
            if (!string.IsNullOrWhiteSpace(request.NamePrefix))
                foreach (var test in all.Where(candidate => (candidate.DisplayName ?? string.Empty).StartsWith(request.NamePrefix.Trim(), StringComparison.OrdinalIgnoreCase)))
                    if (!result.Contains(test)) result.Add(test);
            return result;
        }

        private Operation Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "operationId is required.");
            lock (_sync)
            {
                Operation operation;
                if (!_operations.TryGetValue(id.Trim(), out operation))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Clash run operation was not found: " + id);
                return operation;
            }
        }

        private static void Complete(Operation operation, string state)
        {
            operation.State = state;
            operation.Current = null;
            operation.CurrentStartedAtUtc = null;
            operation.UpdatedAtUtc = DateTime.UtcNow;
            operation.CompletedAtUtc = operation.UpdatedAtUtc;
        }

        private void Schedule(Operation operation)
        {
            try
            {
                _postToUi(() => ProcessNext(operation.Id));
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    Complete(operation, "failed");
                    operation.ScheduleError = ex.Message;
                }
            }
        }

        private void PruneOperations()
        {
            if (_operations.Count < MaximumRetainedOperations)
                return;
            foreach (var id in _operations.Values
                .Where(item => item.State != "running" && item.State != "paused")
                .OrderBy(item => item.UpdatedAtUtc)
                .Take(_operations.Count - MaximumRetainedOperations + 1)
                .Select(item => item.Id)
                .ToList())
                _operations.Remove(id);
        }

        private static string BuildDocumentKey(Document document)
        {
            if (document == null)
                return "none";
            string fileName;
            try { fileName = document.FileName ?? string.Empty; }
            catch { fileName = string.Empty; }
            return RuntimeHelpers.GetHashCode(document).ToString(CultureInfo.InvariantCulture) + "|" + fileName;
        }

        private ClashRunBatchResponse Snapshot(Operation operation)
        {
            lock (_sync)
            {
                var now = DateTime.UtcNow;
                var processed = operation.Tests.Count(test => test.Status == "completed" || test.Status == "failed");
                return new ClashRunBatchResponse
                {
                    Applied = true,
                    OperationId = operation.Id,
                    State = operation.State,
                    IsRunning = operation.State == "running",
                    CancelRequested = operation.CancelRequested,
                    TotalTestCount = operation.Tests.Count,
                    ProcessedTestCount = processed,
                    SucceededTestCount = operation.Tests.Count(test => test.Status == "completed"),
                    FailedTestCount = operation.Tests.Count(test => test.Status == "failed"),
                    TimedOutTestCount = operation.Tests.Count(test => test.ExceededTimebox),
                    RemainingTestCount = operation.Tests.Count - processed,
                    BatchSize = operation.BatchSize,
                    BatchProcessedCount = operation.BatchProcessed,
                    PerTestTimeoutSeconds = operation.TimeoutSeconds,
                    CurrentTestName = operation.Current,
                    CurrentTestElapsedMs = operation.CurrentStartedAtUtc.HasValue ? (long)(now - operation.CurrentStartedAtUtc.Value).TotalMilliseconds : 0,
                    StartedAtUtc = operation.StartedAtUtc,
                    UpdatedAtUtc = operation.UpdatedAtUtc,
                    CompletedAtUtc = operation.CompletedAtUtc,
                    ElapsedMs = (long)((operation.CompletedAtUtc ?? now) - operation.StartedAtUtc).TotalMilliseconds,
                    Message = !string.IsNullOrWhiteSpace(operation.ScheduleError)
                        ? "Clash run scheduling failed: " + operation.ScheduleError
                        : operation.State == "paused" ? "Batch completed. Call clash_run_resume to continue." : "Clash run state: " + operation.State + ".",
                    Tests = operation.Tests.Select(test => new ClashRunTestResult
                    {
                        Index = test.Index, TestName = test.TestName, TestHandle = test.TestHandle, Status = test.Status,
                        ElapsedMs = test.ElapsedMs, ExceededTimebox = test.ExceededTimebox, ErrorMessage = test.ErrorMessage,
                    }).ToList(),
                };
            }
        }

        private sealed class Operation
        {
            public string Id;
            public string State;
            public int BatchSize;
            public int BatchProcessed;
            public int TimeoutSeconds;
            public bool CancelRequested;
            public string Current;
            public DateTime? CurrentStartedAtUtc;
            public DateTime StartedAtUtc;
            public DateTime UpdatedAtUtc;
            public DateTime? CompletedAtUtc;
            public string DocumentKey;
            public string ScheduleError;
            public readonly List<ClashRunTestResult> Tests = new List<ClashRunTestResult>();
        }

        private sealed class DocumentChangedException : InvalidOperationException
        {
            public DocumentChangedException(string message) : base(message) { }
        }
    }
}
