using System.Reflection;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #390 item 2: list_item_children's childrenMatchHandle covered only the returned rows, so limit=1 gave a
// one-item handle and a handle over every child cost 60 000+ characters of rows. allChildren=true makes the
// handle cover every listed child (hidden ones too when includeHidden) while rows stay bounded by limit, and
// limit=0 is accepted with it to return no rows at all. The plugin cannot compile in L0, so the choice lives in
// ItemChildrenHandleScope (Contracts) and the plugin's use of it is pinned by source.
public sealed class ItemChildrenAllChildrenTests
{
    private static readonly List<string> Children = Enumerable.Range(1, 5).Select(i => "C" + i).ToList();

    [Fact]
    public void WithoutTheFlagTheHandleCoversTheReturnedRows()
    {
        var scope = ItemChildrenHandleScope.Select(Children, 2, allChildren: false);
        Assert.Equal(new[] { "C1", "C2" }, scope.Rows);
        Assert.Equal(new[] { "C1", "C2" }, scope.HandleItems);
    }

    [Fact]
    public void WithTheFlagTheHandleCoversEveryChildAndRowsStayBounded()
    {
        var scope = ItemChildrenHandleScope.Select(Children, 1, allChildren: true);
        Assert.Equal(new[] { "C1" }, scope.Rows);
        Assert.Equal(Children, scope.HandleItems);
    }

    [Fact]
    public void LimitZeroReturnsNoRowsOnlyWithTheFlag()
    {
        var scope = ItemChildrenHandleScope.Select(Children, 0, allChildren: true);
        Assert.Empty(scope.Rows);
        Assert.Equal(Children, scope.HandleItems);

        Assert.Equal(1, ItemChildrenHandleScope.ClampLimit(0, allChildren: false));
        Assert.Equal(0, ItemChildrenHandleScope.ClampLimit(0, allChildren: true));
        Assert.Equal(200, ItemChildrenHandleScope.ClampLimit(null, allChildren: true));
        Assert.Equal(2000, ItemChildrenHandleScope.ClampLimit(5000, allChildren: true));
        Assert.Equal(1, ItemChildrenHandleScope.ClampLimit(-3, allChildren: false));
    }

    [Fact]
    public void NoChildrenGiveNoHandleItems()
    {
        var scope = ItemChildrenHandleScope.Select(new List<string>(), 10, allChildren: true);
        Assert.Empty(scope.Rows);
        Assert.Empty(scope.HandleItems);
    }

    [Fact]
    public void TheRequestTheResponseAndTheToolCarryTheFlag()
    {
        Assert.True(new ListItemChildrenRequest { AllChildren = true }.AllChildren);
        Assert.Equal(5, new ListItemChildrenResponse { ChildrenMatchHandleItemCount = 5 }.ChildrenMatchHandleItemCount);

        var tool = typeof(NavisworksModelTools).GetMethod("ListItemChildren", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(tool);
        var flag = Assert.Single(tool!.GetParameters(), parameter => parameter.Name == "allChildren");
        Assert.Equal(typeof(bool), flag.ParameterType);
        Assert.Equal(false, flag.DefaultValue);
    }

    [Fact]
    public void ThePluginUsesTheScope()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.Root,
            "NavisHelper", "Agent", "Services", "SearchService.Commands.cs"));
        Assert.Contains("ItemChildrenHandleScope.Select(", source, StringComparison.Ordinal);
        Assert.Contains("ItemChildrenHandleScope.ClampLimit(", source, StringComparison.Ordinal);
        Assert.Contains("allChildren", File.ReadAllText(Path.Combine(RepositoryPaths.Root, "docs", "MCP_TOOL_CONTRACTS.md")),
            StringComparison.Ordinal);
    }
}
