using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace NavisHelper.McpServer.Services;

internal static class McpToolTimingFilter
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Create(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        return async (context, cancellationToken) =>
        {
            var toolName = context?.Params?.Name ?? string.Empty;
            var startedAtUtc = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result = await next(context, cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();
                return AddTiming(result, toolName, startedAtUtc, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                var result = new CallToolResult
                {
                    IsError = true,
                    Content = new List<ContentBlock>
                    {
                        new TextContentBlock { Text = ex.Message },
                    },
                };
                return AddTiming(result, toolName, startedAtUtc, stopwatch.ElapsedMilliseconds, ex);
            }
        };
    }

    private static CallToolResult AddTiming(
        CallToolResult result,
        string toolName,
        DateTime startedAtUtc,
        long elapsedMs,
        Exception exception = null)
    {
        result ??= new CallToolResult();

        result.Content ??= new List<ContentBlock>();
        var primaryPayload = TryParsePrimaryJsonContent(result.Content, out var primaryTextBlock);
        var toolOk = ReadPayloadBoolean(primaryPayload, "ok");
        var toolErrorCode = ReadPayloadString(primaryPayload, "errorCode");

        var completedAtUtc = DateTime.UtcNow;
        var elapsedHuman = ElapsedTimeFormatter.Format(elapsedMs);
        var isError = result.IsError == true || exception != null;
        var shouldReportToUser = elapsedMs >= ElapsedTimeFormatter.ReportThresholdMs;
        var userMessage = BuildUserMessage(toolName, elapsedMs, isError, toolOk, toolErrorCode);
        var timing = new
        {
            tool_name = toolName,
            status = isError ? "error" : "ok",
            tool_ok = toolOk,
            tool_error_code = toolErrorCode,
            started_at_utc = startedAtUtc,
            completed_at_utc = completedAtUtc,
            elapsed_ms = elapsedMs,
            elapsed_human = elapsedHuman,
            should_report_to_user = shouldReportToUser,
            user_message = userMessage,
            agent_instruction = shouldReportToUser
                ? "Report this MCP command duration to the user: " + userMessage
                : string.Empty,
            error_type = exception?.GetType().Name,
        };

        result.Meta ??= new JsonObject();
        result.Meta["navishelper_timing"] = JsonSerializer.SerializeToNode(timing, JsonOptions);

        if (primaryPayload != null && primaryTextBlock != null)
        {
            primaryPayload["navishelper_timing"] = JsonSerializer.SerializeToNode(timing, JsonOptions);
            primaryTextBlock.Text = primaryPayload.ToJsonString(JsonOptions);
        }
        else
        {
            result.Content.Add(new TextContentBlock
            {
                Text = "NavisHelper timing: " + JsonSerializer.Serialize(timing, JsonOptions),
            });
        }

        return result;
    }

    private static JsonObject TryParsePrimaryJsonContent(IList<ContentBlock> content, out TextContentBlock textBlock)
    {
        textBlock = null;
        var candidate = content.OfType<TextContentBlock>().FirstOrDefault();
        if (candidate == null || string.IsNullOrWhiteSpace(candidate.Text))
            return null;

        try
        {
            if (JsonNode.Parse(candidate.Text) is not JsonObject obj)
                return null;

            textBlock = candidate;
            return obj;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool? ReadPayloadBoolean(JsonObject payload, string propertyName)
    {
        if (payload == null
            || !payload.TryGetPropertyValue(propertyName, out var node)
            || node is not JsonValue value
            || !value.TryGetValue<bool>(out var boolean))
        {
            return null;
        }

        return boolean;
    }

    private static string ReadPayloadString(JsonObject payload, string propertyName)
    {
        if (payload == null
            || !payload.TryGetPropertyValue(propertyName, out var node)
            || node is not JsonValue value
            || !value.TryGetValue<string>(out var text))
        {
            return null;
        }

        return text;
    }

    private static string BuildUserMessage(string toolName, long elapsedMs, bool isError, bool? toolOk, string toolErrorCode)
    {
        string action;
        if (isError)
            action = "failed";
        // Refusal wording needs an error code. A bare ok=false is a verdict, not a refusal:
        // mcp_health_check answers ok=false with verdict=degraded as a successful diagnosis.
        else if (toolOk == false && !string.IsNullOrWhiteSpace(toolErrorCode))
            action = "was refused with error code " + toolErrorCode;
        else if (toolOk == false)
            action = "completed with ok: false";
        else
            action = "completed";

        if (string.IsNullOrWhiteSpace(toolName))
            return "MCP command " + action + ". " + ElapsedTimeFormatter.BuildUserMessage(elapsedMs);

        return "MCP command '" + toolName + "' " + action + ". " + ElapsedTimeFormatter.BuildUserMessage(elapsedMs);
    }
}
