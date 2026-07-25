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

        private void StartListener()
        {
            lock (_listenerSync)
            {
                if (_shutdownCts == null || _shutdownCts.IsCancellationRequested)
                    return;
                if (_listeners.Count >= ListenerSlots)
                    return;
            }

            var server = CreatePipeServer();
            CancellationToken listenerToken;
            lock (_listenerSync)
            {
                if (_shutdownCts == null || _shutdownCts.IsCancellationRequested || _listeners.Count >= ListenerSlots)
                {
                    server.Dispose();
                    return;
                }

                _listeners.Add(server);
                listenerToken = _shutdownCts.Token;
            }

            Task.Run(() => ListenAsync(server, listenerToken));
        }

        private void ListenAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
        {
            try
            {
                server.WaitForConnection();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Error("Pipe wait failed: " + ex.Message, "AgentHost");
                RemoveListener(server);
                server.Dispose();
                StartListener();
                return;
            }

            RemoveListener(server);
            try
            {
                StartListener();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to restart pipe listener after accept: " + ex.Message, "AgentHost");
            }

            try
            {
                HandleConnection(server, cancellationToken);
            }
            finally
            {
                try
                {
                    server.Dispose();
                }
                catch
                {
                }

                try
                {
                    StartListener();
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to restart pipe listener after dispose: " + ex.Message, "AgentHost");
                }
            }
        }

        private void HandleConnection(NamedPipeServerStream server, CancellationToken cancellationToken)
        {
            var connectionStartedAt = Stopwatch.StartNew();
            JObject requestObject = null;
            string requestId = null;
            string command = null;
            RequestGateLease requestGateLease = null;

            try
            {
                requestObject = ReadRequestObject(server, cancellationToken);
                requestId = requestObject.Value<string>("request_id");
                command = requestObject.Value<string>("command");
                ValidateProtocolVersion(requestObject.Value<string>("protocol_version"));

                Logger.Info("request_id=" + (requestId ?? "<null>") + " command=" + (command ?? "<null>") + " received", "AgentHost");
                RecordOperationStarted(requestId, command);

                if (HostRequestPolicy.IsRequestGateBypassCommand(command))
                {
                    HandleRequestGateBypass(server, requestObject, requestId);
                    return;
                }

                if (AgentRuntime.IsInteractiveBusy)
                {
                    var reason = AgentRuntime.InteractiveBusyReason;
                    RecordOperationFailed(requestId, command, ErrorCodes.InteractiveBusy, "Navisworks UI is busy with an interactive operation" + (string.IsNullOrWhiteSpace(reason) ? "." : ": " + reason));
                    Logger.Info("request_id=" + (requestId ?? "<null>") + " command=" + (command ?? "<null>") + " rejected error_code=" + ErrorCodes.InteractiveBusy + " reason=interactive_busy", "AgentHost");
                    WriteError(server, requestId, ErrorCodes.InteractiveBusy, "Navisworks UI is busy with an interactive operation" + (string.IsNullOrWhiteSpace(reason) ? "." : ": " + reason), connectionStartedAt.ElapsedMilliseconds);
                    return;
                }

                if (!_requestGate.Wait(0))
                {
                    RecordOperationFailed(requestId, command, ErrorCodes.HostBusy, "AgentHost is already processing another request.");
                    Logger.Info("request_id=" + (requestId ?? "<null>") + " command=" + (command ?? "<null>") + " rejected error_code=" + ErrorCodes.HostBusy, "AgentHost");
                    WriteError(server, requestId, ErrorCodes.HostBusy, "AgentHost is already processing another request.", connectionStartedAt.ElapsedMilliseconds);
                    return;
                }

                requestGateLease = new RequestGateLease(_requestGate);

                HandleRequest(server, requestObject, requestId, requestGateLease);
            }
            catch (AgentCommandException ex)
            {
                RecordOperationFailed(requestId, command, ex.ErrorCode, ex.Message);
                var logMessage = "request_id=" + (requestId ?? "<null>") + " command=" + (command ?? "<null>") + " failed error_code=" + ex.ErrorCode + " exception=" + ex;
                if (ex.LogAsWarning)
                    Logger.Warn(logMessage, "AgentHost");
                else
                    Logger.Error(logMessage, "AgentHost");
                WriteError(server, requestId, ex.ErrorCode, ex.Message, connectionStartedAt.ElapsedMilliseconds);
            }
            catch (JsonException ex)
            {
                RecordOperationFailed(requestId, command, ErrorCodes.SchemaViolation, ex.Message);
                Logger.Error("request_id=" + (requestId ?? "<null>") + " command=" + (command ?? "<null>") + " failed error_code=" + ErrorCodes.SchemaViolation + " message=" + ex.Message, "AgentHost");
                WriteError(server, requestId, ErrorCodes.SchemaViolation, ex.Message, connectionStartedAt.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                RecordOperationFailed(requestId, command, ErrorCodes.RequestTimeout, "Timed out while reading request frame.");
                Logger.Error("request_id=" + (requestId ?? "<null>") + " command=" + (command ?? "<null>") + " failed error_code=" + ErrorCodes.RequestTimeout + " message=request frame read timed out", "AgentHost");
                WriteError(server, requestId, ErrorCodes.RequestTimeout, "Timed out while reading request frame.", connectionStartedAt.ElapsedMilliseconds);
            }
            catch (AggregateException ex) when (ex.InnerException is AgentCommandException)
            {
                var commandException = (AgentCommandException)ex.InnerException;
                RecordOperationFailed(requestId, command, commandException.ErrorCode, commandException.Message);
                var logMessage = "request_id=" + (requestId ?? "<null>") + " command=" + (command ?? "<null>") + " failed error_code=" + commandException.ErrorCode + " exception=" + commandException;
                if (commandException.LogAsWarning)
                    Logger.Warn(logMessage, "AgentHost");
                else
                    Logger.Error(logMessage, "AgentHost");
                WriteError(server, requestId, commandException.ErrorCode, commandException.Message, connectionStartedAt.ElapsedMilliseconds);
            }
            catch (AggregateException ex)
            {
                var inner = ex.Flatten().InnerExceptions.FirstOrDefault() ?? ex;
                RecordOperationFailed(requestId, command, ErrorCodes.CommandFailed, inner.Message);
                Logger.Error("request_id=" + (requestId ?? "<null>") + " command=" + (command ?? "<null>") + " failed error_code=" + ErrorCodes.CommandFailed + " message=" + inner.Message, "AgentHost");
                WriteError(server, requestId, ErrorCodes.CommandFailed, inner.Message, connectionStartedAt.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                RecordOperationFailed(requestId, command, ErrorCodes.CommandFailed, ex.Message);
                Logger.Error("Unhandled AgentHost error: " + ex, "AgentHost");
                WriteError(server, requestId, ErrorCodes.CommandFailed, ex.Message, connectionStartedAt.ElapsedMilliseconds);
            }
            finally
            {
                if (requestGateLease != null)
                    requestGateLease.Dispose();
            }
        }

        private NamedPipeServerStream CreatePipeServer()
        {
            SecurityIdentifier user;
            using (var identity = WindowsIdentity.GetCurrent())
            {
                user = identity.User;
            }
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                user,
                PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
                AccessControlType.Allow));

            return new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                4096,
                4096,
                security);
        }

        private static JObject ReadRequestObject(Stream stream, CancellationToken cancellationToken)
        {
            var json = ReadFrameAsync(stream, cancellationToken).GetAwaiter().GetResult();
            return JObject.Parse(json);
        }

        private static async Task<string> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
        {
            var lengthBuffer = await ReadExactlyAsync(stream, 4, cancellationToken).ConfigureAwait(false);
            var length = BitConverter.ToInt32(lengthBuffer, 0);
            if (length <= 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Frame length must be positive.");
            if (length > MaxFrameLengthBytes)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Frame length exceeds the maximum of " + MaxFrameLengthBytes + " bytes.");

            var payload = await ReadExactlyAsync(stream, length, cancellationToken).ConfigureAwait(false);
            return Encoding.UTF8.GetString(payload);
        }

        private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken cancellationToken)
        {
            var buffer = new byte[count];
            var offset = 0;
            using (var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                readCts.CancelAfter(ConnectionIdleTimeoutMs);

                while (offset < count)
                {
                    var read = await stream.ReadAsync(buffer, offset, count - offset, readCts.Token).ConfigureAwait(false);
                    if (read <= 0)
                        throw new EndOfStreamException("Pipe closed while reading frame.");

                    offset += read;
                }
            }

            return buffer;
        }

        private static void WriteFrame(Stream stream, string json)
        {
            var payload = Encoding.UTF8.GetBytes(json ?? string.Empty);
            if (payload.Length > MaxFrameLengthBytes)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Frame length exceeds the maximum of " + MaxFrameLengthBytes + " bytes.");
            var length = BitConverter.GetBytes(payload.Length);

            stream.Write(length, 0, length.Length);
            stream.Write(payload, 0, payload.Length);
            stream.Flush();
        }

        private static void TryDelete(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch
            {
            }
        }

        private void RemoveListener(NamedPipeServerStream server)
        {
            lock (_listenerSync)
            {
                _listeners.Remove(server);
            }
        }
    }
}
