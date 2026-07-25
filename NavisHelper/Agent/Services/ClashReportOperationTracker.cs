using System;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    // This SDK-independent file is source-linked into NavisHelper.McpServer.Tests.
    // Keep it free of Autodesk/Navisworks and other host-only source dependencies.
    internal sealed class ClashReportOperationState
    {
        public string OperationId { get; set; }
        public string State { get; set; }
        public bool CancelRequested { get; set; }
        public string OutputDirectory { get; set; }
        public string ReportPath { get; set; }
        public string ManifestPath { get; set; }
        public string CurrentTestName { get; set; }
        public string CurrentResultName { get; set; }
        public int CurrentGlobalIndex { get; set; }
        public int ResultOffset { get; set; }
        public int TotalBatchCount { get; set; }
        public int ProcessedResultCount { get; set; }
        public int CreatedViewpointCount { get; set; }
        public int ScreenshotCount { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string Message { get; set; }
    }

    internal sealed class ClashReportOperationTracker
    {
        public const string Running = "running";
        public const string RunningTests = "running_tests";
        public const string Done = "done";
        public const string Cancelled = "cancelled";
        public const string Failed = "failed";

        private readonly object _sync = new object();
        private readonly Func<DateTime> _utcNow;
        private ClashReportOperationState _activeOperation;
        private ClashReportOperationState _lastOperation;

        public ClashReportOperationTracker()
            : this(() => DateTime.UtcNow)
        {
        }

        public ClashReportOperationTracker(Func<DateTime> utcNow)
        {
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public ClashReportOperationState Start(string outputDirectory, string reportPath, string manifestPath, int resultOffset, int totalBatchCount)
        {
            lock (_sync)
            {
                if (_activeOperation != null && IsRunningState(_activeOperation.State))
                    throw new InvalidOperationException("A clash report is already running. Call clash_report_status or cancel_clash_report.");

                var now = _utcNow();
                var operation = new ClashReportOperationState
                {
                    OperationId = "clash-report-" + Guid.NewGuid().ToString("N"),
                    State = Running,
                    OutputDirectory = outputDirectory ?? string.Empty,
                    ReportPath = reportPath ?? string.Empty,
                    ManifestPath = manifestPath ?? string.Empty,
                    ResultOffset = resultOffset,
                    TotalBatchCount = Math.Max(0, totalBatchCount),
                    StartedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                _activeOperation = operation;
                _lastOperation = operation;
                return operation;
            }
        }

        public ClashReportStatusResponse GetStatus(string operationId)
        {
            lock (_sync)
                return BuildStatusResponse(ResolveLocked(operationId), false, false);
        }

        public ClashReportStatusResponse Cancel(string operationId)
        {
            lock (_sync)
            {
                var operation = ResolveLocked(operationId);
                if (operation == null)
                    return BuildStatusResponse(null, false, true);

                var accepted = IsRunningState(operation.State);
                if (accepted)
                {
                    operation.CancelRequested = true;
                    operation.UpdatedAtUtc = _utcNow();
                }

                return BuildStatusResponse(operation, accepted, true);
            }
        }

        public void FailRunning(string reason)
        {
            lock (_sync)
            {
                var operation = _activeOperation;
                if (operation == null || !IsRunningState(operation.State))
                    return;

                SetFinishedLocked(operation, Failed, reason, true);
            }
        }

        public bool IsCancellationRequested(ClashReportOperationState operation)
        {
            if (operation == null)
                return false;

            lock (_sync)
                return operation.CancelRequested;
        }

        public void UpdateState(ClashReportOperationState operation, string state, string message)
        {
            if (operation == null)
                return;

            lock (_sync)
            {
                if (!IsRunningState(operation.State))
                    return;

                operation.State = string.IsNullOrWhiteSpace(state) ? Running : state;
                operation.Message = message ?? string.Empty;
                operation.UpdatedAtUtc = _utcNow();
            }
        }

        public void UpdateBatchScope(ClashReportOperationState operation, int totalBatchCount)
        {
            if (operation == null)
                return;

            lock (_sync)
            {
                operation.TotalBatchCount = Math.Max(0, totalBatchCount);
                operation.UpdatedAtUtc = _utcNow();
            }
        }

        public void UpdateCurrent(ClashReportOperationState operation, string testName, string resultName, int globalIndex)
        {
            if (operation == null)
                return;

            lock (_sync)
            {
                operation.CurrentTestName = testName ?? string.Empty;
                operation.CurrentResultName = resultName ?? string.Empty;
                operation.CurrentGlobalIndex = globalIndex;
                operation.UpdatedAtUtc = _utcNow();
            }
        }

        public void UpdateProgress(ClashReportOperationState operation, int processedResultCount, int createdViewpointCount, int screenshotCount)
        {
            if (operation == null)
                return;

            lock (_sync)
            {
                operation.ProcessedResultCount = Math.Max(0, processedResultCount);
                operation.CreatedViewpointCount = Math.Max(0, createdViewpointCount);
                operation.ScreenshotCount = Math.Max(0, screenshotCount);
                operation.UpdatedAtUtc = _utcNow();
            }
        }

        public void Complete(ClashReportOperationState operation, bool cancelled, string message)
        {
            if (operation == null)
                return;

            lock (_sync)
                SetFinishedLocked(operation, cancelled ? Cancelled : Done, message, false);
        }

        public void Fail(ClashReportOperationState operation, string message)
        {
            if (operation == null)
                return;

            lock (_sync)
            {
                if (string.Equals(operation.State, Done, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(operation.State, Cancelled, StringComparison.OrdinalIgnoreCase))
                    return;

                SetFinishedLocked(operation, Failed, message, false);
            }
        }

        public static bool IsRunningState(string state)
        {
            return string.Equals(state, Running, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(state, RunningTests, StringComparison.OrdinalIgnoreCase);
        }

        private void SetFinishedLocked(ClashReportOperationState operation, string state, string message, bool cancelRequested)
        {
            operation.State = state;
            operation.CancelRequested = operation.CancelRequested || cancelRequested;
            operation.CompletedAtUtc = _utcNow();
            operation.UpdatedAtUtc = operation.CompletedAtUtc.Value;
            operation.Message = message ?? string.Empty;
            if (ReferenceEquals(_activeOperation, operation))
                _activeOperation = null;
            _lastOperation = operation;
        }

        private ClashReportOperationState ResolveLocked(string operationId)
        {
            if (!string.IsNullOrWhiteSpace(operationId))
            {
                var normalized = operationId.Trim();
                if (_activeOperation != null &&
                    string.Equals(_activeOperation.OperationId, normalized, StringComparison.OrdinalIgnoreCase))
                    return _activeOperation;
                if (_lastOperation != null &&
                    string.Equals(_lastOperation.OperationId, normalized, StringComparison.OrdinalIgnoreCase))
                    return _lastOperation;
                return null;
            }

            return _activeOperation ?? _lastOperation;
        }

        private ClashReportStatusResponse BuildStatusResponse(ClashReportOperationState operation, bool cancelAccepted, bool cancelCommand)
        {
            if (operation == null)
            {
                return new ClashReportStatusResponse
                {
                    State = string.Empty,
                    IsRunning = false,
                    CancelRequested = false,
                    CancelAccepted = false,
                    Message = cancelCommand ? "No active clash report operation." : "No clash report operation was found.",
                };
            }

            var nowUtc = _utcNow();
            var completed = operation.CompletedAtUtc;
            var running = IsRunningState(operation.State);
            return new ClashReportStatusResponse
            {
                OperationId = operation.OperationId,
                State = operation.State,
                IsRunning = running,
                CancelRequested = operation.CancelRequested,
                CancelAccepted = cancelAccepted,
                OutputDirectory = operation.OutputDirectory,
                ReportPath = operation.ReportPath,
                ManifestPath = operation.ManifestPath,
                CurrentTestName = operation.CurrentTestName,
                CurrentResultName = operation.CurrentResultName,
                ResultOffset = operation.ResultOffset,
                TotalBatchCount = operation.TotalBatchCount,
                ProcessedResultCount = operation.ProcessedResultCount,
                CreatedViewpointCount = operation.CreatedViewpointCount,
                ScreenshotCount = operation.ScreenshotCount,
                StartedAtUtc = operation.StartedAtUtc,
                UpdatedAtUtc = operation.UpdatedAtUtc,
                CompletedAtUtc = completed,
                ElapsedMs = operation.StartedAtUtc.HasValue ? (long)((completed ?? nowUtc) - operation.StartedAtUtc.Value).TotalMilliseconds : 0,
                Message = string.IsNullOrWhiteSpace(operation.Message)
                    ? running
                        ? "Clash report is running."
                        : "Clash report is " + operation.State + "."
                    : operation.Message,
            };
        }
    }
}
