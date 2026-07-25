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
    private static McpErrorContractItem Error(string errorCode, string meaning, string recommendedAction, bool retryable)
    {
        return new McpErrorContractItem
        {
            ErrorCode = errorCode,
            Meaning = meaning,
            RecommendedAction = recommendedAction,
            Retryable = retryable,
        };
    }

    private static string ExtractErrorCode(Exception ex)
    {
        if (ex is HostCallException hostCallException)
            return hostCallException.ErrorCode;
        if (ex is EndOfStreamException || ex is IOException)
            return ErrorCodes.TransportConnectFailed;
        if (ex is JsonException)
            return ErrorCodes.SchemaViolation;
        if (ex is OperationCanceledException)
            return ErrorCodes.RequestTimeout;

        var message = ex.Message ?? string.Empty;
        var separatorIndex = message.IndexOf(':');
        if (separatorIndex > 0 && separatorIndex < 80)
            return message.Substring(0, separatorIndex);

        return ex.GetType().Name;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
    }
}
