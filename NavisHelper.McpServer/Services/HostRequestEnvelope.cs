#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal static class HostRequestEnvelope
{
    internal static string Build(string requestId, string instanceId, string command,
        int timeoutMs, object? payload, string sessionId, string? clientName)
    {
        return JsonSerializer.Serialize(new Envelope
        {
            RequestId = requestId,
            InstanceId = instanceId,
            Command = command,
            TimeoutMs = timeoutMs,
            Payload = payload,
            SessionId = sessionId,
            ClientName = clientName,
        }, HostBridgeClient.RequestJsonOptions);
    }

    private sealed class Envelope
    {
        public string ProtocolVersion { get; } = ProtocolConstants.CurrentProtocolVersion;
        public string RequestId { get; set; } = string.Empty;
        public string InstanceId { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public int TimeoutMs { get; set; }
        public object? Payload { get; set; }
        public string SessionId { get; set; } = string.Empty;
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? ClientName { get; set; }
    }
}
