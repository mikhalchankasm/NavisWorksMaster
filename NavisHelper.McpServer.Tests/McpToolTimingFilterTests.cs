using System.Text.Json;
using System.Runtime.CompilerServices;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class McpToolTimingFilterTests
{
    [Fact]
    public async Task StructuredRefusal_ReportsToolOkFalseAndErrorCodeInBothCopies()
    {
        var result = await InvokeAsync(new CallToolResult
        {
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = """{"ok":false,"applied":false,"errorCode":"scenario_id_invalid","message":"no such scenario"}""" },
            },
        });

        var timing = MetaTiming(result);
        Assert.Equal("ok", timing.GetProperty("status").GetString());
        Assert.False(timing.GetProperty("tool_ok").GetBoolean());
        Assert.Equal("scenario_id_invalid", timing.GetProperty("tool_error_code").GetString());

        var userMessage = timing.GetProperty("user_message").GetString();
        Assert.Contains("refused", userMessage);
        Assert.Contains("scenario_id_invalid", userMessage);
        Assert.DoesNotContain("completed", userMessage);

        var payload = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement;
        Assert.False(payload.GetProperty("ok").GetBoolean());
        Assert.Equal("scenario_id_invalid", payload.GetProperty("errorCode").GetString());

        var inlineTiming = payload.GetProperty("navishelper_timing");
        Assert.False(inlineTiming.GetProperty("tool_ok").GetBoolean());
        Assert.Equal("scenario_id_invalid", inlineTiming.GetProperty("tool_error_code").GetString());
        Assert.Equal(timing.GetProperty("user_message").GetString(), inlineTiming.GetProperty("user_message").GetString());
    }

    [Fact]
    public async Task PayloadOkTrue_ReportsToolOkTrueWithoutErrorCode()
    {
        var result = await InvokeAsync(new CallToolResult
        {
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = """{"ok":true,"scenarioId":"abc"}""" },
            },
        });

        var timing = MetaTiming(result);
        Assert.Equal("ok", timing.GetProperty("status").GetString());
        Assert.True(timing.GetProperty("tool_ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, timing.GetProperty("tool_error_code").ValueKind);
        Assert.Contains("completed", timing.GetProperty("user_message").GetString());
    }

    [Fact]
    public async Task PayloadWithoutOk_LeavesToolVerdictNull()
    {
        var result = await InvokeAsync(new CallToolResult
        {
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = """{"capabilities":["search","clash"]}""" },
            },
        });

        var timing = MetaTiming(result);
        Assert.Equal(JsonValueKind.Null, timing.GetProperty("tool_ok").ValueKind);
        Assert.Equal(JsonValueKind.Null, timing.GetProperty("tool_error_code").ValueKind);
        Assert.Contains("completed", timing.GetProperty("user_message").GetString());

        var payload = JsonDocument.Parse(((TextContentBlock)result.Content[0]).Text).RootElement;
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("navishelper_timing").GetProperty("tool_ok").ValueKind);
    }

    [Fact]
    public async Task NonJsonText_AppendsSeparateTimingBlockAndLeavesToolVerdictNull()
    {
        var result = await InvokeAsync(new CallToolResult
        {
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = "host log excerpt, not json" },
            },
        });

        Assert.Equal("host log excerpt, not json", ((TextContentBlock)result.Content[0]).Text);
        var appended = Assert.IsType<TextContentBlock>(result.Content[1]);
        Assert.StartsWith("NavisHelper timing: ", appended.Text);

        var timing = MetaTiming(result);
        Assert.Equal(JsonValueKind.Null, timing.GetProperty("tool_ok").ValueKind);
        Assert.Equal(JsonValueKind.Null, timing.GetProperty("tool_error_code").ValueKind);
    }

    [Fact]
    public async Task ExceptionFromTool_ReturnsErrorTimingWithNullToolVerdict()
    {
        var handler = McpToolTimingFilter.Create(
            (context, cancellationToken) => ValueTask.FromException<CallToolResult>(new InvalidOperationException("boom")));

        var result = await handler(Context("failing_tool"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("boom", ((TextContentBlock)result.Content[0]).Text);

        var timing = MetaTiming(result);
        Assert.Equal("error", timing.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, timing.GetProperty("tool_ok").ValueKind);
        Assert.Equal(JsonValueKind.Null, timing.GetProperty("tool_error_code").ValueKind);
        Assert.Equal("InvalidOperationException", timing.GetProperty("error_type").GetString());
        Assert.Contains("failed", timing.GetProperty("user_message").GetString());
    }

    [Fact]
    public async Task ErrorCodeOfNonStringType_IsNotReported()
    {
        var result = await InvokeAsync(new CallToolResult
        {
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = """{"ok":false,"errorCode":42}""" },
            },
        });

        var timing = MetaTiming(result);
        Assert.False(timing.GetProperty("tool_ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, timing.GetProperty("tool_error_code").ValueKind);
        var userMessage = timing.GetProperty("user_message").GetString();
        Assert.Contains("ok: false", userMessage);
        Assert.DoesNotContain("refused", userMessage);
        Assert.DoesNotContain("42", userMessage);
    }

    [Fact]
    public async Task BareOkFalseWithoutErrorCode_IsNotCalledARefusal()
    {
        // mcp_health_check answers ok=false with verdict=degraded when a check fails or the
        // server and plugin versions differ. That is a diagnostic verdict, not a refusal.
        var result = await InvokeAsync(new CallToolResult
        {
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = """{"ok":false,"verdict":"degraded","checks":[]}""" },
            },
        });

        var timing = MetaTiming(result);
        Assert.Equal("ok", timing.GetProperty("status").GetString());
        Assert.False(timing.GetProperty("tool_ok").GetBoolean());
        Assert.Equal(JsonValueKind.Null, timing.GetProperty("tool_error_code").ValueKind);
        var userMessage = timing.GetProperty("user_message").GetString();
        Assert.DoesNotContain("refused", userMessage);
        Assert.Contains("ok: false", userMessage);
    }

    [Fact]
    public async Task NullToolResult_StillGetsTimingEnvelope()
    {
        var result = await InvokeAsync(null);

        Assert.NotNull(result.Content);
        var timing = MetaTiming(result);
        Assert.Equal("ok", timing.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, timing.GetProperty("tool_ok").ValueKind);
        Assert.Equal("sample_tool", timing.GetProperty("tool_name").GetString());
    }

    // #427: the default encoder wrote every Cyrillic letter of a model name as a six-byte \uXXXX escape.
    [Fact]
    public async Task NonAsciiText_StaysReadableAndItsBytesAreReported()
    {
        const string payload = """{"items":[{"name":"ЗОНА 1 /6501.5-S.АМ","note":"a + b's"}]}""";
        var result = await InvokeAsync(new CallToolResult
        {
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = payload },
            },
        });

        var text = ((TextContentBlock)result.Content[0]).Text;
        Assert.Contains("ЗОНА 1 /6501.5-S.АМ", text, StringComparison.Ordinal);
        Assert.Contains("a + b's", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", text, StringComparison.Ordinal);

        var bytes = System.Text.Encoding.UTF8.GetByteCount(payload);
        Assert.Equal(bytes, MetaTiming(result).GetProperty("response_bytes").GetInt32());
        var inline = JsonDocument.Parse(text).RootElement.GetProperty("navishelper_timing");
        Assert.Equal(bytes, inline.GetProperty("response_bytes").GetInt32());
    }

    // The host pages by counting every non-ASCII character as a six-byte escape. That holds
    // only while this filter never writes a string longer than that (#427 review).
    [Fact]
    public async Task NoStringIsWrittenLongerThanItsAsciiEscapedForm()
    {
        const string value = "ЗОНА\u3000东京 😀 \u2028 tab\t del\u007f \"q\" \\ <a&b>";
        var payload = JsonSerializer.Serialize(new { name = value });
        var result = await InvokeAsync(new CallToolResult
        {
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = payload },
            },
        });

        var escapedBound = 0;
        foreach (var c in value)
            escapedBound += c == '"' || c == '\\' ? 2 : c >= 0x20 && c < 0x7f ? 1 : 6;
        var written = MetaTiming(result).GetProperty("response_bytes").GetInt32();
        Assert.True(written <= escapedBound + """{"name":""}""".Length,
            $"The filter wrote {written} bytes; the host's bound is {escapedBound} for the string.");
    }

    [Fact]
    public async Task NonJsonText_ReportsItsBytesToo()
    {
        var result = await InvokeAsync(new CallToolResult
        {
            Content = new List<ContentBlock>
            {
                new TextContentBlock { Text = "журнал" },
            },
        });

        Assert.Equal(12, MetaTiming(result).GetProperty("response_bytes").GetInt32());
    }

    private static async Task<CallToolResult> InvokeAsync(CallToolResult inner)
    {
        var handler = McpToolTimingFilter.Create(
            (context, cancellationToken) => ValueTask.FromResult(inner));
        return await handler(Context("sample_tool"), CancellationToken.None);
    }

    private static RequestContext<CallToolRequestParams> Context(string toolName)
    {
        var context = (RequestContext<CallToolRequestParams>)RuntimeHelpers.GetUninitializedObject(
            typeof(RequestContext<CallToolRequestParams>));
        context.Params = new CallToolRequestParams { Name = toolName };
        return context;
    }

    private static JsonElement MetaTiming(CallToolResult result)
    {
        Assert.NotNull(result.Meta);
        return JsonSerializer.Deserialize<JsonElement>(result.Meta["navishelper_timing"]);
    }
}
