using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed partial class HostBridgeClient
{
    private async Task<TResponse> CallHostAsync<TResponse>(string command, object payload, CancellationToken cancellationToken, HostTargetOptions target = null, int timeoutMs = DefaultTimeoutMs)
    {
        var stopwatch = Stopwatch.StartNew();
        InstanceDiscoveryRecord record = null;
        var hostBusyRetryCount = 0;
        var effectiveTimeoutMs = Math.Max(HostTimeoutMarginMs + 1, timeoutMs);

        while (true)
        {
            var requestId = "req-" + Guid.NewGuid().ToString("N");

            try
            {
                record = ResolveTargetHost(target);
                var remainingTimeoutMs = effectiveTimeoutMs - (int)stopwatch.ElapsedMilliseconds;
                if (remainingTimeoutMs <= HostTimeoutMarginMs)
                    throw new HostCallException(ErrorCodes.RequestTimeout, "Navisworks host did not become available within " + effectiveTimeoutMs + " ms.");

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(remainingTimeoutMs));

                using var client = new NamedPipeClientStream(
                    ".",
                    record.PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                try
                {
                    await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
                    VerifyConnectedPipeServer(client, record);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    TryDeleteStaleRecord(record);
                    throw new HostCallException(ErrorCodes.TransportConnectFailed, "Timed out while connecting to Navisworks host.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    TryDeleteStaleRecord(record);
                    throw new HostCallException(ErrorCodes.TransportConnectFailed, "Unable to connect to Navisworks host.");
                }

                var hostTimeoutMs = Math.Max(1, remainingTimeoutMs - HostTimeoutMarginMs);
                var requestJson = JsonSerializer.Serialize(new
                {
                    protocol_version = ProtocolConstants.CurrentProtocolVersion,
                    request_id = requestId,
                    instance_id = record.InstanceId,
                    command = command,
                    timeout_ms = hostTimeoutMs,
                    payload = payload,
                }, JsonOptions);

                string responseJson;
                try
                {
                    await WriteFrameAsync(client, requestJson, timeoutCts.Token).ConfigureAwait(false);
                    responseJson = await ReadFrameAsync(client, timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new HostCallException(ErrorCodes.RequestTimeout, "Navisworks host did not respond within " + effectiveTimeoutMs + " ms.");
                }
                catch (EndOfStreamException ex)
                {
                    throw new HostCallException(ErrorCodes.TransportConnectFailed, ex.Message, ex);
                }
                catch (IOException ex)
                {
                    throw new HostCallException(ErrorCodes.TransportConnectFailed, ex.Message, ex);
                }

                using var responseDocument = JsonDocument.Parse(responseJson);
                var root = responseDocument.RootElement;
                ValidateResponseProtocolVersion(root);

                var ok = root.GetProperty("ok").GetBoolean();
                if (!ok)
                {
                    var errorCode = GetOptionalString(root, "error_code") ?? ErrorCodes.SchemaViolation;
                    var errorMessage = GetOptionalString(root, "error_message") ?? "Unknown host error.";
                    if (string.Equals(errorCode, ErrorCodes.HostBusy, StringComparison.OrdinalIgnoreCase))
                    {
                        var retryDelayMs = GetHostBusyRetryDelayMs(hostBusyRetryCount);
                        if (stopwatch.ElapsedMilliseconds + retryDelayMs < effectiveTimeoutMs - HostTimeoutMarginMs)
                        {
                            hostBusyRetryCount++;
                            _callLogger.Log(new
                            {
                                event_name = "host_busy_retry",
                                timestamp_utc = DateTime.UtcNow,
                                command = command,
                                request_id = requestId,
                                retry_count = hostBusyRetryCount,
                                delay_ms = retryDelayMs,
                                elapsed_ms = stopwatch.ElapsedMilliseconds,
                                instance_id = record.InstanceId,
                                pipe_name = record.PipeName,
                                pid = record.Pid,
                                navisworks_version = record.NavisworksVersion,
                                document_title = record.DocumentTitle,
                                plugin_version = record.PluginVersion,
                                plugin_assembly_path = record.PluginAssemblyPath,
                                plugin_assembly_last_write_utc = record.PluginAssemblyLastWriteUtc,
                                plugin_assembly_length = record.PluginAssemblyLength,
                            });

                            await Task.Delay(retryDelayMs, cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                    }

                    throw new HostCallException(errorCode, errorMessage);
                }

                var responseRequestId = GetRequiredString(root, "request_id");
                if (!string.Equals(requestId, responseRequestId, StringComparison.Ordinal))
                    throw new HostCallException(ErrorCodes.SchemaViolation, "request_id mismatch.");

                if (!root.TryGetProperty("payload", out var payloadElement) || payloadElement.ValueKind == JsonValueKind.Null)
                {
                    var emptyResponse = Activator.CreateInstance<TResponse>();
                    stopwatch.Stop();
                    _callLogger.LogHostCall(command, requestId, record, stopwatch.ElapsedMilliseconds, "ok", null, null, ResponseSummaryFormatter.Build(emptyResponse));
                    return emptyResponse;
                }

                var response = payloadElement.Deserialize<TResponse>(JsonOptions);
                if (response == null)
                    throw new HostCallException(ErrorCodes.SchemaViolation, "Empty payload.");

                stopwatch.Stop();
                _callLogger.LogHostCall(command, requestId, record, stopwatch.ElapsedMilliseconds, "ok", null, null, ResponseSummaryFormatter.Build(response));
                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _callLogger.LogHostCall(command, requestId, record, stopwatch.ElapsedMilliseconds, cancellationToken.IsCancellationRequested ? "cancelled" : "error", ExtractErrorCode(ex), ex.Message, null);
                throw;
            }
        }
    }

    private static int GetHostBusyRetryDelayMs(int retryCount)
    {
        var delay = HostBusyRetryDelayBaseMs * Math.Max(1, retryCount + 1);
        return Math.Min(delay, HostBusyRetryDelayMaxMs);
    }

    private static void VerifyConnectedPipeServer(NamedPipeClientStream client, InstanceDiscoveryRecord record)
    {
        if (client == null || record == null)
            return;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        try
        {
            uint serverPid;
            if (!GetNamedPipeServerProcessId(client.SafePipeHandle, out serverPid))
                throw new HostCallException(ErrorCodes.TransportConnectFailed, "Unable to verify named-pipe server process id.");

            if (serverPid != (uint)record.Pid)
            {
                TryDeleteStaleRecord(record);
                throw new HostCallException(
                    ErrorCodes.TransportConnectFailed,
                    "Named-pipe server pid " + serverPid.ToString(CultureInfo.InvariantCulture) +
                    " does not match discovery pid " + record.Pid.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }
        catch (HostCallException)
        {
            throw;
        }
        catch (ObjectDisposedException)
        {
            throw new HostCallException(ErrorCodes.TransportConnectFailed, "Named pipe closed before server pid verification.");
        }
    }

    private static async Task<string> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var lengthBuffer = await ReadExactlyAsync(stream, 4, cancellationToken).ConfigureAwait(false);
        var length = BitConverter.ToInt32(lengthBuffer, 0);
        if (length <= 0)
            throw new HostCallException(ErrorCodes.SchemaViolation, "Invalid frame length.");
        if (length > MaxFrameLengthBytes)
            throw new HostCallException(ErrorCodes.SchemaViolation, "Frame length exceeds the maximum of " + MaxFrameLengthBytes + " bytes.");

        var payload = await ReadExactlyAsync(stream, length, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(payload);
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        var buffer = new byte[count];
        var offset = 0;

        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                throw new EndOfStreamException("Pipe closed while reading frame.");

            offset += read;
        }

        return buffer;
    }

    private static async Task WriteFrameAsync(Stream stream, string json, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        if (payload.Length > MaxFrameLengthBytes)
            throw new HostCallException(ErrorCodes.SchemaViolation, "Frame length exceeds the maximum of " + MaxFrameLengthBytes + " bytes.");
        var length = BitConverter.GetBytes(payload.Length);

        await stream.WriteAsync(length.AsMemory(0, length.Length), cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload.AsMemory(0, payload.Length), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint serverProcessId);
}
