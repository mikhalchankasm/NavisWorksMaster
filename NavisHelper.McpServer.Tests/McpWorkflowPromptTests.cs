using System.Text.RegularExpressions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

[Collection("MCP stdio")]
public sealed class McpWorkflowPromptTests
{
    [Theory]
    [InlineData("review_clashes", "core,clash")]
    [InlineData("audit_selection_properties", "core,reports")]
    public async Task PromptsAreDiscoverableAndReferenceOnlyAvailableReadOnlyTools(string name, string profile)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpCatalogResourceTests.ConnectAsync(profile, true, timeout.Token);
        var prompts = await client.ListPromptsAsync(cancellationToken: timeout.Token);
        Assert.Equal(new[] { "audit_selection_properties", "review_clashes" },
            prompts.Select(prompt => prompt.Name).OrderBy(value => value, StringComparer.Ordinal));
        var result = await client.GetPromptAsync(name, cancellationToken: timeout.Token);
        var message = Assert.Single(result.Messages);
        Assert.Equal(Role.User, message.Role);
        var text = Assert.IsType<TextContentBlock>(message.Content).Text;
        Assert.Contains("not executed analysis", text);
        Assert.Contains("truncation", text);
        Assert.Contains("instanceId", text);
        Assert.Contains("document changed", text);
        Assert.DoesNotContain("Workflow unavailable", text);
        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token))
            .Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        var readOnly = new McpReadOnlyMode(true, McpReadOnlyMode.RegisteredToolMethods());
        var referenced = Regex.Matches(text, "`([a-z][a-z0-9_]*)`").Select(match => match.Groups[1].Value).Distinct().ToArray();
        Assert.True(referenced.Length >= 6);
        Assert.All(referenced, tool =>
        {
            Assert.Contains(tool, tools);
            Assert.True(readOnly.IsAllowed(tool), "Workflow references a state-changing tool: " + tool);
        });
    }

    [Theory]
    [InlineData("review_clashes")]
    [InlineData("audit_selection_properties")]
    public async Task MissingProfileToolsReturnConfigurationGuidanceWithoutAnExecutionPlan(string name)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpCatalogResourceTests.ConnectAsync("meta", true, timeout.Token);
        var result = await client.GetPromptAsync(name, cancellationToken: timeout.Token);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Messages).Content).Text;
        Assert.Contains("Workflow unavailable", text);
        Assert.Contains("preserving the current read-only setting", text);
        Assert.DoesNotContain("1.", text);
    }

    [Fact]
    public async Task UnknownPromptIsRefused()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpCatalogResourceTests.ConnectAsync("all", false, timeout.Token);
        await Assert.ThrowsAsync<McpProtocolException>(() => client.GetPromptAsync(
            "unknown_workflow", cancellationToken: timeout.Token).AsTask());
    }
}
