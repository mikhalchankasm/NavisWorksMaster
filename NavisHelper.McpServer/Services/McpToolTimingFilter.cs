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

        var completedAtUtc = DateTime.UtcNow;
        var elapsedHuman = ElapsedTimeFormatter.Format(elapsedMs);
        var isError = result.IsError == true || exception != null;
        var shouldReportToUser = elapsedMs >= ElapsedTimeFormatter.ReportThresholdMs;
        var userMessage = BuildUserMessage(toolName, elapsedMs, isError);
        var timing = new
        {
            tool_name = toolName,
            status = isError ? "error" : "ok",
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

        result.Content ??= new List<ContentBlock>();
        if (!TryInjectTimingIntoPrimaryJsonContent(result.Content, timing))
        {
            result.Content.Add(new TextContentBlock
            {
                Text = "NavisHelper timing: " + JsonSerializer.Serialize(timing, JsonOptions),
            });
        }

        return result;
    }

    private static bool TryInjectTimingIntoPrimaryJsonContent(IList<ContentBlock> content, object timing)
    {
        var textBlock = content.OfType<TextContentBlock>().FirstOrDefault();
        if (textBlock == null || string.IsNullOrWhiteSpace(textBlock.Text))
            return false;

        try
        {
            var node = JsonNode.Parse(textBlock.Text);
            if (node is not JsonObject obj)
                return false;

            obj["navishelper_timing"] = JsonSerializer.SerializeToNode(timing, JsonOptions);
            textBlock.Text = obj.ToJsonString(JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildUserMessage(string toolName, long elapsedMs, bool isError)
    {
        var action = isError ? "failed" : "completed";
        if (string.IsNullOrWhiteSpace(toolName))
            return "MCP command " + action + ". " + ElapsedTimeFormatter.BuildUserMessage(elapsedMs);

        return "MCP command '" + toolName + "' " + action + ". " + ElapsedTimeFormatter.BuildUserMessage(elapsedMs);
    }
}
