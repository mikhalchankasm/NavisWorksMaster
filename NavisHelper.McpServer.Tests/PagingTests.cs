using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #359, found in real modelling work on a large model:
// - selected_items_tree with bounding boxes stopped at the deadline after 1,500-2,700
//   items with truncated=true and no way to ask for the rest;
// - active_model_context with rootItemLimit=300 returned more than 80 KB, because every
//   root carries its display name, source file, file name and path.
public sealed class PagingTests
{
    [Theory]
    [InlineData(0, 100, 250, 100)]
    [InlineData(100, 100, 250, 200)]
    [InlineData(200, 50, 250, null)]
    [InlineData(0, 250, 250, null)]
    [InlineData(0, 0, 0, null)]
    [InlineData(300, 0, 250, null)]
    public void NextOffsetPointsAtTheFirstItemNotReturned(int offset, int returned, int selected, int? expected)
    {
        Assert.Equal(expected, SelectedItemsTreePaging.NextOffset(offset, returned, selected));
    }

    [Fact]
    public void ANegativeOffsetIsRefused()
    {
        Assert.Contains("offset", SelectedItemsTreePaging.DescribeOffsetError(-1), StringComparison.Ordinal);
        Assert.Null(SelectedItemsTreePaging.DescribeOffsetError(null));
        Assert.Null(SelectedItemsTreePaging.DescribeOffsetError(0));
        Assert.Null(SelectedItemsTreePaging.DescribeOffsetError(5000));
    }

    [Fact]
    public void ContractsCarryThePage()
    {
        Assert.Equal(300, new SelectedItemsTreeRequest { Offset = 300 }.Offset);
        var response = new SelectedItemsTreeResponse { Offset = 300, NextOffset = 600 };
        Assert.Equal(300, response.Offset);
        Assert.Equal(600, response.NextOffset);
        Assert.Empty(new ActiveModelContextResponse().RootItemNames);
    }

    [Theory]
    [InlineData("", "full")]
    [InlineData("full", "full")]
    [InlineData("NAMES", "names")]
    [InlineData(" names ", "names")]
    public void RootFormatsNormalise(string value, string expected)
    {
        Assert.True(ActiveModelContextRootFormats.TryNormalize(value, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void AnUnknownRootFormatIsRefused()
    {
        Assert.False(ActiveModelContextRootFormats.TryNormalize("tree", out _));
    }

    [Fact]
    public void NamesFallBackToTheFileName()
    {
        var names = RootItemNames.From(new ListRootItemsResponse
        {
            Items = new List<RootItemInfo>
            {
                new RootItemInfo { DisplayName = "A.rvm", FileName = "A.rvm", SourceFile = @"D:\m\A.rvm", Path = "A.rvm" },
                new RootItemInfo { DisplayName = "", FileName = "B.dwg" },
                new RootItemInfo { DisplayName = "  ", FileName = "" },
            },
        });
        Assert.Equal(new[] { "A.rvm", "B.dwg" }, names);
        Assert.Empty(RootItemNames.From(null));
    }

    [Fact]
    public void TheToolsExposeTheParameters()
    {
        var tree = ToolSchema("SelectedItemsTree");
        Assert.Equal("integer", tree["properties"]!["offset"]!["type"]!.GetValue<string>());
        Assert.Equal(0, tree["properties"]!["offset"]!["default"]!.GetValue<int>());

        var context = ToolSchema("ActiveModelContext");
        Assert.Equal("string", context["properties"]!["rootItemsFormat"]!["type"]!.GetValue<string>());
        Assert.Equal("full", context["properties"]!["rootItemsFormat"]!["default"]!.GetValue<string>());
    }

    [Fact]
    public void TheCodeUsesTheHelpers()
    {
        var queries = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "DocumentCommandService.SelectionQueries.cs"));
        Assert.Contains("SelectedItemsTreePaging.NextOffset(", queries, StringComparison.Ordinal);
        Assert.Contains("SelectedItemsTreePaging.DescribeOffsetError(", queries, StringComparison.Ordinal);

        var host = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper.McpServer", "Tools", "NavisworksHostTools.cs"));
        Assert.Contains("RootItemNames.From(", host, StringComparison.Ordinal);
        Assert.Contains("ActiveModelContextRootFormats.TryNormalize(", host, StringComparison.Ordinal);
    }

    private static JsonNode ToolSchema(string methodName)
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == methodName);
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
        return JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!;
    }
}
