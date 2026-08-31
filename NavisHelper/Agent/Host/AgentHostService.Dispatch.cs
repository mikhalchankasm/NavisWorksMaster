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

        private void HandleRequest(NamedPipeServerStream server, JObject requestObject, string requestId, RequestGateLease requestGateLease)
        {
            var command = requestObject.Value<string>("command");
            var instanceId = requestObject.Value<string>("instance_id");
            var payloadToken = requestObject["payload"];
            var timeoutMs = ClampTimeoutMs(requestObject.Value<int?>("timeout_ms"));

            if (string.IsNullOrWhiteSpace(requestId))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "request_id is required.");
            if (string.IsNullOrWhiteSpace(command))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "command is required.");
            if (!string.IsNullOrWhiteSpace(instanceId) && !string.Equals(instanceId, _instanceId, StringComparison.OrdinalIgnoreCase))
                throw new AgentCommandException(ErrorCodes.InstanceNotFound, "The request targets another Navisworks instance.");
            if (_uiContext == null && _uiControl == null)
                throw new AgentCommandException(ErrorCodes.HostUiContextUnavailable, "UI synchronization context is not available.");

            var startedAt = Stopwatch.StartNew();
            Logger.Info(
                "request_id=" + requestId + " command=" + command + " ui_dispatch_start timeout_ms=" + timeoutMs + " dispatcher=" + GetUiDispatcherLabel(),
                "AgentHost");

            object payload = InvokeOnUiThread<object>(() =>
            {
                Logger.Info(
                    "request_id=" + requestId + " command=" + command + " ui_callback_start elapsed_ms=" + startedAt.ElapsedMilliseconds,
                    "AgentHost");

                var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
                Logger.Info(
                    "request_id=" + requestId + " command=" + command + " active_document_resolved document=\"" + GetDocumentTitleForLog(document) + "\" elapsed_ms=" + startedAt.ElapsedMilliseconds,
                    "AgentHost");

                RefreshDiscoveryFile(document);
                Logger.Info(
                    "request_id=" + requestId + " command=" + command + " discovery_refreshed elapsed_ms=" + startedAt.ElapsedMilliseconds,
                    "AgentHost");
                Logger.Info(
                    "request_id=" + requestId + " command=" + command + " operation_start selected_item_count=" + GetSelectedItemCountForLog(document) +
                    " parameters=" + BuildPayloadSummaryForLog(payloadToken) + " elapsed_ms=" + startedAt.ElapsedMilliseconds,
                    "AgentHost");

                if (string.Equals(command, HostCommandNames.FindItems, StringComparison.OrdinalIgnoreCase))
                {
                    EnsureDocument(document);
                    var request = DeserializePayload<FindItemsRequest>(payloadToken);
                    Logger.Info(
                        "request_id=" + requestId + " command=" + command + " find_items_search_start elapsed_ms=" + startedAt.ElapsedMilliseconds,
                        "AgentHost");
                    return _searchService.FindItems(document, request, _matchSessionStore, timeoutMs);
                }

                object routedPayload;
                // Every router handler, including close preparation/save/discard,
                // executes inside this UI-dispatched callback.
                if (_commandRouter.TryDispatch(command, document, payloadToken, EnsureDocument, out routedPayload))
                    return routedPayload;

                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Unsupported command: " + command);
            }, timeoutMs, requestGateLease, requestId, command, startedAt);

            startedAt.Stop();
            Logger.Info(
                "request_id=" + requestId + " command=" + command + " ui_dispatch_done elapsed_ms=" + startedAt.ElapsedMilliseconds,
                "AgentHost");

            bool responseTruncated;
            var responseJson = BuildSuccessResponseJson(requestId, startedAt.ElapsedMilliseconds, payload, out responseTruncated);
            RecordOperationCompleted(requestId, command, startedAt.ElapsedMilliseconds, payload, responseTruncated);

            Logger.Info("request_id=" + requestId + " command=" + command + " elapsed_ms=" + startedAt.ElapsedMilliseconds, "AgentHost");
            WriteFrame(server, responseJson);
            if (payload is CloseNavisworksResponse closeResponse &&
                closeResponse.ExitScheduled)
            {
                WaitForCloseResponseDrain(server);
                ScheduleNavisworksExit();
            }
        }

        private static void WaitForCloseResponseDrain(NamedPipeServerStream server)
        {
            try
            {
                server.WaitForPipeDrain();
            }
            catch (Exception ex)
            {
                Logger.Error(
                    "Could not confirm close_navisworks response drain; keeping the delayed exit fallback: " + ex.Message,
                    "CloseNavisworks");
            }
        }

        private void ScheduleNavisworksExit()
        {
            var uiContext = _uiContext;
            var uiControl = uiContext == null ? GetAttachedControl() : null;
            Task.Run(async () =>
            {
                await Task.Delay(250).ConfigureAwait(false);
                try
                {
                    if (uiContext != null)
                    {
                        uiContext.Post(_ => NavisworksApplicationCloseService.ExecuteScheduledExit(), null);
                    }
                    else if (uiControl != null && !uiControl.IsDisposed && uiControl.IsHandleCreated)
                    {
                        uiControl.BeginInvoke(new Action(NavisworksApplicationCloseService.ExecuteScheduledExit));
                    }
                    else
                    {
                        Logger.Error("Scheduled Navisworks exit lost its UI dispatcher.", "CloseNavisworks");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Could not dispatch scheduled Navisworks exit: " + ex, "CloseNavisworks");
                }
            });
        }

        private void PostToUi(Action action)
        {
            if (action == null)
                return;
            var context = _uiContext;
            var control = context == null ? GetAttachedControl() : null;
            if (context != null)
                context.Post(_ => action(), null);
            else if (control != null && !control.IsDisposed && control.IsHandleCreated)
                control.BeginInvoke(action);
            else
                throw new AgentCommandException(ErrorCodes.HostUiContextUnavailable, "Could not schedule asynchronous clash work: UI dispatcher is unavailable.");
        }

        private static void ValidateProtocolVersion(string protocolVersion)
        {
            if (string.IsNullOrWhiteSpace(protocolVersion))
                return;

            if (!string.Equals(protocolVersion.Trim(), ProtocolConstants.CurrentProtocolVersion, StringComparison.Ordinal))
            {
                throw new AgentCommandException(
                    ErrorCodes.SchemaViolation,
                    "protocol_version mismatch. Client sent " + protocolVersion.Trim() + ", host expects " + ProtocolConstants.CurrentProtocolVersion + ".");
            }
        }

        private void HandleRequestGateBypass(NamedPipeServerStream server, JObject requestObject, string requestId)
        {
            var command = requestObject.Value<string>("command");
            var instanceId = requestObject.Value<string>("instance_id");
            var payloadToken = requestObject["payload"];
            var startedAt = Stopwatch.StartNew();

            if (string.IsNullOrWhiteSpace(requestId))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "request_id is required.");
            if (string.IsNullOrWhiteSpace(command))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "command is required.");
            if (!string.IsNullOrWhiteSpace(instanceId) && !string.Equals(instanceId, _instanceId, StringComparison.OrdinalIgnoreCase))
                throw new AgentCommandException(ErrorCodes.InstanceNotFound, "The request targets another Navisworks instance.");

            object payload;
            switch (HostRequestPolicy.GetRequestGateBypassKind(command))
            {
                case HostRequestGateBypassKind.ClashReportStatus:
                    payload = _commandService.ClashReportStatus(DeserializePayload<ClashReportStatusRequest>(payloadToken));
                    break;
                case HostRequestGateBypassKind.LastOperationStatus:
                    payload = GetLastOperationStatus(DeserializePayload<LastOperationStatusRequest>(payloadToken));
                    break;
                case HostRequestGateBypassKind.CancelClashReport:
                    payload = _commandService.CancelClashReport(DeserializePayload<CancelClashReportRequest>(payloadToken));
                    break;
                case HostRequestGateBypassKind.CancelSubtreeNamesDump:
                    payload = AttachInstanceId(_commandService.CancelSubtreeNamesDump(DeserializePayload<CancelSubtreeNamesDumpRequest>(payloadToken)));
                    break;
                case HostRequestGateBypassKind.ClashRunStatus:
                    payload = _clashBatchRunService.Status(DeserializePayload<ClashRunStatusRequest>(payloadToken));
                    break;
                case HostRequestGateBypassKind.CancelClashRun:
                    payload = _clashBatchRunService.Cancel(DeserializePayload<CancelClashRunRequest>(payloadToken));
                    break;
                default:
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Unsupported request gate bypass command: " + command);
            }

            startedAt.Stop();
            bool responseTruncated;
            var responseJson = BuildSuccessResponseJson(requestId, startedAt.ElapsedMilliseconds, payload, out responseTruncated);
            RecordOperationCompleted(requestId, command, startedAt.ElapsedMilliseconds, payload, responseTruncated);

            Logger.Info("request_id=" + requestId + " command=" + command + " bypass elapsed_ms=" + startedAt.ElapsedMilliseconds, "AgentHost");
            WriteFrame(server, responseJson);
        }

        private T InvokeOnUiThread<T>(Func<T> callback, int timeoutMs, RequestGateLease requestGateLease, string requestId, string command, Stopwatch startedAt)
        {
            var uiContext = _uiContext;
            // A WPF dispatcher captured by RibbonLoader is the actual Navisworks
            // UI context. The fallback control exists only for headless startup.
            var uiControl = uiContext == null ? GetAttachedControl() : null;

            if (uiContext == null && uiControl == null)
                throw new AgentCommandException(ErrorCodes.HostUiContextUnavailable, "UI synchronization context is not available.");

            if (uiContext != null && SynchronizationContext.Current == uiContext)
                return callback();

            if (uiControl != null)
            {
                if (!uiControl.IsHandleCreated)
                    throw new AgentCommandException(ErrorCodes.HostUiContextUnavailable, "UI control dispatcher handle is not available.");

                if (!uiControl.InvokeRequired)
                    return callback();

                T resultFromControl = default(T);
                var controlCompletion = new TaskCompletionSource<T>();
                try
                {
                    uiControl.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            resultFromControl = callback();
                            controlCompletion.TrySetResult(resultFromControl);
                        }
                        catch (Exception ex)
                        {
                            controlCompletion.TrySetException(ex);
                        }
                    }));
                }
                catch (ObjectDisposedException)
                {
                    ClearDisposedControl(uiControl);
                    uiControl = null;
                }
                catch (InvalidOperationException)
                {
                    ClearDisposedControl(uiControl);
                    uiControl = null;
                }

                if (uiControl != null)
                {
                    try
                    {
                        if (!controlCompletion.Task.Wait(timeoutMs))
                        {
                            var dispatcherControl = uiControl;
                            RecordDeferredOperationCompletion(controlCompletion.Task, requestId, command, startedAt);
                            requestGateLease.DeferRelease(
                                controlCompletion.Task,
                                timeoutMs + DeferredGateReleaseGraceMs,
                                requestId,
                                command,
                                () => IsDispatcherControlUnavailable(dispatcherControl),
                                () => RecordOperationFailed(
                                    requestId,
                                    command,
                                    ErrorCodes.HostUiContextUnavailable,
                                    "UI callback did not complete because the dispatcher control is no longer available."));
                            throw new AgentCommandException(ErrorCodes.RequestTimeout, "The request exceeded the timeout of " + timeoutMs + " ms.");
                        }

                        return controlCompletion.Task.GetAwaiter().GetResult();
                    }
                    catch (AggregateException ex) when (ex.InnerException is AgentCommandException)
                    {
                        throw ex.InnerException;
                    }
                }
            }

            if (uiContext == null)
                throw new AgentCommandException(ErrorCodes.HostUiContextUnavailable, "UI synchronization context is not available.");

            T result = default(T);
            var completion = new TaskCompletionSource<T>();

            uiContext.Post(_ =>
            {
                try
                {
                    result = callback();
                    completion.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }, null);

            try
            {
                if (!completion.Task.Wait(timeoutMs))
                {
                    RecordDeferredOperationCompletion(completion.Task, requestId, command, startedAt);
                    requestGateLease.DeferRelease(completion.Task, timeoutMs + DeferredGateReleaseGraceMs, requestId, command);
                    throw new AgentCommandException(ErrorCodes.RequestTimeout, "The request exceeded the timeout of " + timeoutMs + " ms.");
                }

                return completion.Task.GetAwaiter().GetResult();
            }
            catch (AggregateException ex) when (ex.InnerException is AgentCommandException)
            {
                throw ex.InnerException;
            }
        }

        private DumpSubtreeNamesJobStatusResponse AttachInstanceId(DumpSubtreeNamesJobStatusResponse response)
        {
            if (response != null)
                response.InstanceId = _instanceId ?? string.Empty;
            return response;
        }

        private Control GetAttachedControl()
        {
            var uiControl = _uiControl;
            if (uiControl == null)
                return null;

            if (uiControl.IsDisposed)
            {
                ClearDisposedControl(uiControl);
                return null;
            }

            if (uiControl.IsHandleCreated)
                return uiControl;

            return null;
        }

        private static bool IsDispatcherControlUnavailable(Control uiControl)
        {
            if (uiControl == null)
                return true;

            try
            {
                return uiControl.IsDisposed || !uiControl.IsHandleCreated;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        private void ClearDisposedControl(Control disposedControl)
        {
            if (disposedControl == null)
                return;

            lock (_listenerSync)
            {
                if (ReferenceEquals(_uiControl, disposedControl) && _uiControl.IsDisposed)
                {
                    _uiControl = null;
                    Logger.Info("Disposed UI control dispatcher cleared.", "AgentHost");
                }
            }
        }

        private static T DeserializePayload<T>(JToken payloadToken)
        {
            if (payloadToken == null || payloadToken.Type == JTokenType.Null)
                return Activator.CreateInstance<T>();

            return payloadToken.ToObject<T>(JsonDeserializer);
        }

        private void EnsureDocument(Document document)
        {
            if (document == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "There is no active document.");
        }

        private static int ClampTimeoutMs(int? timeoutMs)
        {
            var value = timeoutMs.GetValueOrDefault(60000);
            if (value < 1)
                return 1;
            if (value > ProtocolConstants.MaximumHostRequestTimeoutMilliseconds)
                return ProtocolConstants.MaximumHostRequestTimeoutMilliseconds;
            return value;
        }
    }
}
