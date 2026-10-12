using System.Text.RegularExpressions;
using NavisHelper.McpServer.Resources;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// The server hands every client a short guide at initialize. It must name the
// entry points, and every tool it names in backticks must be a real tool, so the
// guide cannot drift from the surface it describes.
[Collection("MCP stdio")]
public sealed class McpServerInstructionsTests
{
    [Fact]
    public async Task InitializeReturnsShortGuideThatNamesOnlyRealTools()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpCatalogResourceTests.ConnectAsync("all", false, timeout.Token);
        var instructions = client.ServerInstructions;

        Assert.False(string.IsNullOrWhiteSpace(instructions));
        Assert.InRange(instructions.Length, 300, 1500);
        foreach (var required in new[] { "`active_model_context`", "`list_navisworks_hosts`", "`save_document`", McpToolCatalogResources.CatalogUri })
            Assert.Contains(required, instructions, StringComparison.Ordinal);

        var tools = (await client.ListToolsAsync(cancellationToken: timeout.Token)).Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        var named = Regex.Matches(instructions, "`([^`]+)`").Select(match => match.Groups[1].Value)
            .Where(token => !token.Contains("://", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(named);
        Assert.All(named, token => Assert.Contains(token, tools));
    }

    [Fact]
    public async Task ReadOnlyServerGivesTheSameGuide()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var full = await McpCatalogResourceTests.ConnectAsync("all", false, timeout.Token);
        await using var readOnly = await McpCatalogResourceTests.ConnectAsync("all", true, timeout.Token);
        Assert.Equal(full.ServerInstructions, readOnly.ServerInstructions);
    }
}
