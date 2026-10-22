using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #357, found in real modelling work:
// - nothing could clear the selection: select_items([]) failed, and the selection
//   highlight then covered the colours in capture_current_view;
// - hide_selected cleared the selection, so the next unhide_selected failed with
//   no_selection, while hide_unselected and unhide_selected keep theirs.
public sealed class SelectionErgonomicsTests
{
    [Fact]
    public void AnEmptyRequestStillFailsButPointsAtClear()
    {
        var error = SelectItemsRules.DescribeError(new SelectItemsRequest());
        Assert.NotNull(error);
        Assert.StartsWith("At least one match handle is required.", error, StringComparison.Ordinal);
        Assert.Contains("clear=true", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearTakesNoHandles()
    {
        var error = SelectItemsRules.DescribeError(new SelectItemsRequest { Clear = true, MatchHandles = new List<string> { "h1" } });
        Assert.NotNull(error);
        Assert.Contains("clear=true", error, StringComparison.Ordinal);
        Assert.Contains("matchHandles", error, StringComparison.Ordinal);

        Assert.Null(SelectItemsRules.DescribeError(new SelectItemsRequest { Clear = true }));
        Assert.Null(SelectItemsRules.DescribeError(new SelectItemsRequest { MatchHandles = new List<string> { "h1" } }));
    }

    [Fact]
    public void ContractsCarryTheNewFields()
    {
        Assert.True(new SelectItemsResponse { Cleared = true }.Cleared);
        Assert.True(new CaptureCurrentViewRequest { HideSelectionHighlight = true }.HideSelectionHighlight);
        Assert.True(new CaptureCurrentViewResponse { SelectionHighlightHidden = true }.SelectionHighlightHidden);
    }

    [Theory]
    [InlineData("SelectItems", "clear")]
    [InlineData("CaptureCurrentView", "hideSelectionHighlight")]
    public void TheToolsExposeAnOptInFlag(string methodName, string parameter)
    {
        var schema = ToolSchema(methodName, out _);
        Assert.Equal("boolean", schema["properties"]![parameter]!["type"]!.GetValue<string>());
        Assert.False(schema["properties"]![parameter]!["default"]!.GetValue<bool>());
    }

    [Fact]
    public void HideSelectedSaysTheSelectionIsKept()
    {
        ToolSchema("HideSelected", out var description);
        Assert.Contains("selection is kept", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThePluginUsesTheRuleAndKeepsTheSelection()
    {
        var services = Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services");
        var queries = File.ReadAllText(Path.Combine(services, "DocumentCommandService.SelectionQueries.cs"));
        Assert.Contains("SelectItemsRules.DescribeError(", queries, StringComparison.Ordinal);

        var visibility = File.ReadAllText(Path.Combine(services, "DocumentCommandService.Visibility.cs"));
        var start = visibility.IndexOf("public HideSelectedResponse HideSelected(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = visibility.IndexOf("\n        public ", start + 1, StringComparison.Ordinal);
        var hideSelected = end > start ? visibility.Substring(start, end - start) : visibility.Substring(start);
        Assert.DoesNotContain("ClearSelection(document)", hideSelected, StringComparison.Ordinal);
        Assert.Contains("RestoreSelection(", hideSelected, StringComparison.Ordinal);

        var capture = File.ReadAllText(Path.Combine(services, "ClashIsolationService.cs"));
        Assert.Contains("HideSelectionHighlight", capture, StringComparison.Ordinal);
    }

    private static JsonNode ToolSchema(string methodName, out string description)
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == methodName);
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
        description = tool.ProtocolTool.Description ?? "";
        return JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!;
    }
}
