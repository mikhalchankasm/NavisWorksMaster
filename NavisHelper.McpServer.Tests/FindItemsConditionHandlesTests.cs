using System.Reflection;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #390 item 1: a list of a few hundred instrument names took a few hundred find_items calls, because one
// OR search returns one handle for the whole set. handlePerCondition returns one handle per condition from
// one call. The match store kept only 100 handles, so those calls also evicted each other's handles, which
// is most likely the "stale handles" of item 3; it now keeps 2000 handles holding at most 1000000 items.
public sealed class FindItemsConditionHandlesTests
{
    private const string Whole = FindItemsScopes.WholeModel;

    [Fact]
    public void AValidRequestOrTheFlagOffIsAccepted()
    {
        Assert.Null(FindItemsConditionHandles.Describe(true, 1, "any", 2, false, Whole));
        Assert.Null(FindItemsConditionHandles.Describe(true, 1, "ANY", 1000, false, ""));
        Assert.Null(FindItemsConditionHandles.Describe(true, 1, "any", 300, false, null));
        Assert.Null(FindItemsConditionHandles.Describe(false, 0, "all", 0, true, FindItemsScopes.UnderHandle));
        Assert.Equal(1000, FindItemsConditionHandles.MaxConditions);
    }

    [Theory]
    [InlineData(0, "any", 3, false, Whole, "handlePerCondition needs exactly one search in searches.")]
    [InlineData(2, "any", 3, false, Whole, "handlePerCondition needs exactly one search in searches.")]
    [InlineData(1, "all", 3, false, Whole, "handlePerCondition needs combineOperator any.")]
    [InlineData(1, "", 3, false, Whole, "handlePerCondition needs combineOperator any.")]
    [InlineData(1, "any", 1, false, Whole, "handlePerCondition needs 2 to 1000 conditions.")]
    [InlineData(1, "any", 1001, false, Whole, "handlePerCondition needs 2 to 1000 conditions.")]
    [InlineData(1, "any", 3, true, Whole, "handlePerCondition cannot be combined with countOnly.")]
    [InlineData(1, "any", 3, false, FindItemsScopes.CurrentSelection, "handlePerCondition works only with scope whole_model.")]
    public void AnUnsupportedRequestIsRefusedWithAReason(int searches, string combine, int conditions, bool countOnly, string scope, string message)
    {
        Assert.Equal(message, FindItemsConditionHandles.Describe(true, searches, combine, conditions, countOnly, scope));
    }

    [Fact]
    public void ItemsGoToEveryConditionTheyMatchInItemOrder()
    {
        var perItem = new List<IList<int>> { new[] { 0 }, new[] { 1 }, new[] { 0, 2 }, Array.Empty<int>(), new[] { 2 } };
        var groups = FindItemsConditionHandles.Group(4, perItem, out var unassigned);
        Assert.Equal(4, groups.Count);
        Assert.Equal(new[] { 0, 2 }, groups[0]);
        Assert.Equal(new[] { 1 }, groups[1]);
        Assert.Equal(new[] { 2, 4 }, groups[2]);
        Assert.Empty(groups[3]);
        Assert.Equal(1, unassigned);
    }

    [Fact]
    public void TheResultListsOneMatchPerCondition()
    {
        var property = typeof(FindItemsResult).GetProperty("ConditionMatches");
        Assert.NotNull(property);
        Assert.Equal(typeof(List<FindItemsConditionMatch>), property!.PropertyType);
        Assert.NotNull(new FindItemsResult().ConditionMatches);
        foreach (var name in new[] { "ConditionIndex", "Value", "ItemCount", "MatchHandle" })
            Assert.NotNull(typeof(FindItemsConditionMatch).GetProperty(name));
        Assert.NotNull(typeof(FindItemsResult).GetProperty("UnassignedItemCount"));
        Assert.NotNull(typeof(FindItemsRequest).GetProperty("HandlePerCondition"));
    }

    [Fact]
    public void TheToolTakesTheFlagAndForwardsIt()
    {
        var method = typeof(NavisworksModelTools).GetMethod("FindItems", BindingFlags.Public | BindingFlags.Instance);
        var parameter = method!.GetParameters().SingleOrDefault(item => item.Name == "handlePerCondition");
        Assert.NotNull(parameter);
        Assert.Equal(typeof(bool), parameter!.ParameterType);
        Assert.Equal(false, parameter.DefaultValue);
        var tools = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper.McpServer", "Tools", "NavisworksModelTools.cs"));
        Assert.Contains("HandlePerCondition = handlePerCondition", tools, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStoreKeepsMoreHandlesAndSaysSoWhenOneIsEvicted()
    {
        Assert.Equal(2000, MatchHandleLimits.MaxHandles);
        Assert.Equal(1000000, MatchHandleLimits.MaxItems);
        var ledger = new MatchHandleLedger();
        ledger.RecordEvicted("mh_000002");
        Assert.Equal("This match handle was evicted because only the 2000 most recently used handles, holding at most 1000000 items, are kept.",
            ledger.Explain("mh_000002", 2));
        var store = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Session", "MatchSessionStore.cs"));
        Assert.Contains("MatchHandleLimits.MaxHandles", store, StringComparison.Ordinal);
        Assert.Contains("MatchHandleLimits.MaxItems", store, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePluginValidatesAndGroupsThroughTheContract()
    {
        var root = Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services");
        var search = string.Concat(Directory.GetFiles(root, "SearchService*.cs").Select(File.ReadAllText));
        Assert.Contains("FindItemsConditionHandles.Describe(", search, StringComparison.Ordinal);
        Assert.Contains("FindItemsConditionHandles.Group(", search, StringComparison.Ordinal);
    }
}
