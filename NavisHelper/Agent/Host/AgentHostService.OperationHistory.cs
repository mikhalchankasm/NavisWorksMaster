using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ApplicationParts;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using NavisHelper.Agent.Session;
using NavisHelper.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace NavisHelper.Agent.Host
{
    internal sealed partial class AgentHostService : IDisposable
    {

        private sealed class RequestGateLease : IDisposable
        {
            private readonly SemaphoreSlim _requestGate;
            private int _releaseDeferred;
            private int _released;

            public RequestGateLease(SemaphoreSlim requestGate)
            {
                _requestGate = requestGate ?? throw new ArgumentNullException(nameof(requestGate));
            }

            public void DeferRelease(Task completionTask, int releaseAfterMs, string requestId, string command, Func<bool> isAbandoned = null, Action recordAbandonedFailure = null)
            {
                if (completionTask == null)
                    throw new ArgumentNullException(nameof(completionTask));

                if (Interlocked.Exchange(ref _releaseDeferred, 1) != 0)
                    return;

                completionTask.ContinueWith(t =>
                {
                    var ignored = t.Exception;
                }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

                Task.Run(async () =>
                {
                    try
                    {
                        await completionTask.ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                    finally
                    {
                        Release();
                    }
                });

                if (releaseAfterMs > 0)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(releaseAfterMs).ConfigureAwait(false);
                            if (!completionTask.IsCompleted && Interlocked.CompareExchange(ref _released, 0, 0) == 0)
                            {
                                var abandoned = false;
                                if (isAbandoned != null)
                                {
                                    try
                                    {
                                        abandoned = isAbandoned();
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Error(
                                            "request_id=" + (requestId ?? "<null>") + " command=" + (command ?? "<null>") + " deferred request gate abandonment probe failed: " + ex.Message,
                                            "AgentHost");
                                    }
                                }

                                if (abandoned)
                                {
                                    try
                                    {
                                        if (recordAbandonedFailure != null)
                                            recordAbandonedFailure();
                                    }
                                    catch (Exception ex)
                                    {
                                        Logger.Error(
                                            "request_id=" + (requestId ?? "<null>") + " command=" + (command ?? "<null>") + " failed to record abandoned deferred operation: " + ex.Message,
                                            "AgentHost");
                                    }

                                    Logger.Error(
                                        "request_id=" + (requestId ?? "<null>") + " command=" + (command ?? "<null>") + " deferred request gate released because the UI dispatcher control is no longer available after " + releaseAfterMs + " ms.",
                                        "AgentHost");
                                    Release();
                                    return;
                                }

                                Logger.Error(
                                    "request_id=" + (requestId ?? "<null>") + " command=" + (command ?? "<null>") + " deferred request gate is still held after " + releaseAfterMs + " ms.",
                                    "AgentHost");
                            }
                        }
                        catch
                        {
                        }
                    });
                }
            }

            public void Dispose()
            {
                if (Interlocked.CompareExchange(ref _releaseDeferred, 0, 0) == 0)
                    Release();
            }

            private void Release()
            {
                if (Interlocked.Exchange(ref _released, 1) != 0)
                    return;

                _requestGate.Release();
            }
        }

        private void RecordOperationStarted(string requestId, string command)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return;

            if (HostRequestPolicy.IsOperationStatusPollCommand(command))
                return;

            lock (_operationHistorySync)
            {
                OperationRecord existing;
                if (_operationHistory.TryGetValue(requestId, out existing))
                {
                    if (string.Equals(existing.State, "completed", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(existing.State, "failed", StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
                else
                {
                    _operationHistoryOrder.Enqueue(requestId);
                }

                _operationHistory[requestId] = new OperationRecord
                {
                    RequestId = requestId,
                    Command = command ?? string.Empty,
                    State = "running",
                    StartedAtUtc = DateTime.UtcNow,
                };

                TrimOperationHistoryLocked();
            }
        }

        private void RecordOperationCompleted(string requestId, string command, long elapsedMs, object payload, bool responseTruncated)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return;

            if (HostRequestPolicy.IsOperationStatusPollCommand(command))
                return;

            lock (_operationHistorySync)
            {
                OperationRecord record;
                if (!_operationHistory.TryGetValue(requestId, out record))
                {
                    record = new OperationRecord
                    {
                        RequestId = requestId,
                        StartedAtUtc = DateTime.UtcNow,
                    };
                    _operationHistory[requestId] = record;
                    _operationHistoryOrder.Enqueue(requestId);
                }

                if (string.Equals(record.State, "failed", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(record.ErrorCode, ErrorCodes.HostUiContextUnavailable, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                record.Command = command ?? record.Command ?? string.Empty;
                record.State = "completed";
                record.Ok = true;
                record.ErrorCode = null;
                record.ErrorMessage = null;
                record.ResponseTruncated = responseTruncated;
                record.ResponseType = payload == null ? string.Empty : payload.GetType().FullName;
                record.CompletedAtUtc = DateTime.UtcNow;
                record.ElapsedMs = elapsedMs;
                record.Message = responseTruncated
                    ? "Command completed, but the response was truncated to fit the named-pipe frame limit."
                    : "Command completed.";

                TrimOperationHistoryLocked();
            }
        }

        private void RecordOperationFailed(string requestId, string command, string errorCode, string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(requestId))
                return;

            if (HostRequestPolicy.IsOperationStatusPollCommand(command))
                return;

            lock (_operationHistorySync)
            {
                OperationRecord record;
                if (!_operationHistory.TryGetValue(requestId, out record))
                {
                    record = new OperationRecord
                    {
                        RequestId = requestId,
                        StartedAtUtc = DateTime.UtcNow,
                    };
                    _operationHistory[requestId] = record;
                    _operationHistoryOrder.Enqueue(requestId);
                }

                // Completion is authoritative. A later pipe-write/disconnect failure must not
                // rewrite a successfully completed (and possibly mutating) UI callback as failed.
                if (OperationHistoryPolicy.IsAuthoritativeSuccessfulCompletion(record.State, record.Ok))
                {
                    return;
                }

                record.Command = command ?? record.Command ?? string.Empty;
                record.State = "failed";
                record.Ok = false;
                record.ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? ErrorCodes.CommandFailed : errorCode;
                record.ErrorMessage = errorMessage ?? string.Empty;
                record.CompletedAtUtc = DateTime.UtcNow;
                record.ElapsedMs = record.StartedAtUtc.HasValue
                    ? (long)Math.Max(0, (record.CompletedAtUtc.Value - record.StartedAtUtc.Value).TotalMilliseconds)
                    : 0;
                record.Message = "Command failed before completion.";

                TrimOperationHistoryLocked();
            }
        }

        private void RecordDeferredOperationCompletion<T>(Task<T> completionTask, string requestId, string command, Stopwatch startedAt)
        {
            if (completionTask == null || string.IsNullOrWhiteSpace(requestId))
                return;

            completionTask.ContinueWith(task =>
            {
                if (task.IsCanceled)
                {
                    RecordOperationFailed(requestId, command, ErrorCodes.RequestTimeout, "UI callback was cancelled after the client timed out.");
                    return;
                }

                if (task.IsFaulted)
                {
                    var exception = task.Exception == null
                        ? null
                        : task.Exception.Flatten().InnerExceptions.FirstOrDefault();
                    var commandException = exception as AgentCommandException;
                    RecordOperationFailed(
                        requestId,
                        command,
                        commandException == null ? ErrorCodes.CommandFailed : commandException.ErrorCode,
                        exception == null ? "UI callback failed after the client timed out." : exception.Message);
                    return;
                }

                var elapsedMs = startedAt == null ? 0 : startedAt.ElapsedMilliseconds;
                RecordOperationCompleted(requestId, command, elapsedMs, task.Result, false);
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        private LastOperationStatusResponse GetLastOperationStatus(LastOperationStatusRequest request)
        {
            var requestId = request == null ? string.Empty : (request.RequestId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(requestId))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "requestId is required.");

            lock (_operationHistorySync)
            {
                OperationRecord record;
                if (!_operationHistory.TryGetValue(requestId, out record))
                {
                    return new LastOperationStatusResponse
                    {
                        RequestId = requestId,
                        Found = false,
                        State = "not_found",
                        Message = "No recent host operation was found for this requestId. The in-memory history is bounded to recent requests and is reset when Navisworks exits.",
                    };
                }

                return new LastOperationStatusResponse
                {
                    RequestId = record.RequestId,
                    Found = true,
                    Command = record.Command,
                    State = record.State,
                    Ok = record.Ok,
                    ErrorCode = record.ErrorCode,
                    ErrorMessage = record.ErrorMessage,
                    ResponseTruncated = record.ResponseTruncated,
                    ResponseType = record.ResponseType,
                    StartedAtUtc = record.StartedAtUtc,
                    CompletedAtUtc = record.CompletedAtUtc,
                    ElapsedMs = record.ElapsedMs,
                    Message = record.Message,
                };
            }
        }

        private void TrimOperationHistoryLocked()
        {
            var scanned = 0;
            var scanLimit = Math.Max(_operationHistoryOrder.Count, 1);
            while (_operationHistory.Count > MaxOperationHistoryCount && _operationHistoryOrder.Count > 0 && scanned < scanLimit)
            {
                var oldRequestId = _operationHistoryOrder.Dequeue();
                scanned++;

                OperationRecord record;
                if (_operationHistory.TryGetValue(oldRequestId, out record) &&
                    string.Equals(record.State, "running", StringComparison.OrdinalIgnoreCase))
                {
                    _operationHistoryOrder.Enqueue(oldRequestId);
                    continue;
                }

                _operationHistory.Remove(oldRequestId);
            }
        }

        private sealed class PluginAssemblyFileInfo
        {
            public string Version { get; set; }

            public string Path { get; set; }

            public DateTime? LastWriteUtc { get; set; }

            public long? Length { get; set; }
        }

        private sealed class OperationRecord
        {
            public string RequestId { get; set; }

            public string Command { get; set; }

            public string State { get; set; }

            public bool? Ok { get; set; }

            public string ErrorCode { get; set; }

            public string ErrorMessage { get; set; }

            public bool ResponseTruncated { get; set; }

            public string ResponseType { get; set; }

            public DateTime? StartedAtUtc { get; set; }

            public DateTime? CompletedAtUtc { get; set; }

            public long ElapsedMs { get; set; }

            public string Message { get; set; }
        }
    }
}
