using System.Reflection;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #391: Navisworks search has no parentheses and AND binds tighter than OR, so (A OR B) AND D had to be
// written by hand as (A AND D) OR (B AND D). create_search_set now takes conditionGroups: each group is an
// OR of its members, groups are ANDed with each other and with conditions. SearchSetConditionGroups.Expand
// distributes that into the persisted form: one branch per combination (one member from each group, in group
// order, then every condition), branches joined by OR. The plugin cannot compile in L0, so its use is pinned
// by source.
public sealed class SearchSetConditionGroupsTests
{
    private static FindItemsCondition C(string value, string logical = "") =>
        new() { Category = "Item", Property = "Name", Operator = "equals", Value = value, LogicalOperator = logical };

    private static List<FindItemsCondition> L(params FindItemsCondition[] conditions) => conditions.ToList();

    private static List<List<FindItemsCondition>> Groups(params List<FindItemsCondition>[] groups) => groups.ToList();

    private static string Render(IEnumerable<FindItemsCondition> conditions) =>
        string.Join(" ", conditions.Select((c, i) => (i == 0 ? "" : c.LogicalOperator + " ") + c.Value));

    private static List<FindItemsCondition> Expand(List<FindItemsCondition> all, params List<FindItemsCondition>[] groups)
    {
        var expanded = SearchSetConditionGroups.Expand(all, groups.ToList(), out var error);
        Assert.True(error == null, error);
        return expanded;
    }

    [Fact]
    public void OneGroupIsDistributedOverTheCommonConditions()
    {
        var expanded = Expand(L(C("D")), L(C("A"), C("B")));
        Assert.Equal("A and D or B and D", Render(expanded));
        Assert.Equal("and", expanded[0].LogicalOperator);
    }

    [Fact]
    public void TwoGroupsGiveEveryCombinationInGroupOrder()
    {
        var expanded = Expand(L(C("D")), L(C("A"), C("B")), L(C("C"), C("E")));
        Assert.Equal("A and C and D or A and E and D or B and C and D or B and E and D", Render(expanded));
    }

    [Fact]
    public void GroupsWithoutCommonConditionsAndASingleMemberGroupWork()
    {
        Assert.Equal("A or B", Render(Expand(L(), L(C("A"), C("B")))));
        Assert.Equal("A and D", Render(Expand(L(C("D")), L(C("A")))));
    }

    [Fact]
    public void NoGroupsLeavesTheConditionsAsTheyAre()
    {
        var all = new List<FindItemsCondition> { C("A"), C("B", "or"), C("D", "and") };
        var expanded = SearchSetConditionGroups.Expand(all, new List<List<FindItemsCondition>>(), out var error);
        Assert.Null(error);
        Assert.Equal("A or B and D", Render(expanded));
        Assert.Equal("A or B and D", Render(SearchSetConditionGroups.Expand(all, null, out error)));
        Assert.Null(error);
    }

    [Fact]
    public void ExpandedConditionsAreCopiesThatKeepTheirFlags()
    {
        var d = C("D");
        d.Negate = true;
        d.IgnoreCase = false;
        d.InheritFromAncestor = true;
        var expanded = Expand(L(d), L(C("A"), C("B")));

        Assert.NotSame(expanded[1], expanded[3]);
        Assert.All(new[] { expanded[1], expanded[3] }, copy =>
        {
            Assert.NotSame(d, copy);
            Assert.True(copy.Negate);
            Assert.False(copy.IgnoreCase);
            Assert.True(copy.InheritFromAncestor);
            Assert.Equal("Item", copy.Category);
            Assert.Equal("Name", copy.Property);
            Assert.Equal("equals", copy.Operator);
        });
        Assert.Equal("", d.LogicalOperator);
    }

    [Fact]
    public void AmbiguousOrOversizedInputIsRefused()
    {
        string error;
        // In group mode OR is expressed by the groups only.
        Assert.Null(SearchSetConditionGroups.Expand(L(C("D"), C("E", "or")), Groups(L(C("A"))), out error));
        Assert.Contains("logicalOperator", error);
        Assert.Null(SearchSetConditionGroups.Expand(L(C("D")), Groups(L(C("A"), C("B", "or"))), out error));
        Assert.Contains("logicalOperator", error);
        Assert.Null(SearchSetConditionGroups.Expand(L(C("D")), Groups(L()), out error));
        Assert.Contains("empty", error);
        Assert.Null(SearchSetConditionGroups.Expand(L(C("D")), Groups(L(C("A"), null!)), out error));
        Assert.NotNull(error);

        // 3 x 3 x 3 x 3 = 81 branches is past the limit.
        var three = new List<FindItemsCondition> { C("1"), C("2"), C("3") };
        Assert.Null(SearchSetConditionGroups.Expand(L(C("D")),
            Groups(three, three.ToList(), three.ToList(), three.ToList()), out error));
        Assert.Contains(SearchSetConditionGroups.MaxBranches.ToString(), error);
        Assert.Equal(64, SearchSetConditionGroups.MaxBranches);
    }

    [Fact]
    public void TheRequestAndTheToolCarryTheGroups()
    {
        var request = new CreateSearchSetRequest { ConditionGroups = new() { new() { C("A"), C("B") } } };
        Assert.Single(request.ConditionGroups);
        Assert.NotNull(new CreateSearchSetRequest().ConditionGroups);

        var tool = typeof(NavisworksSelectionSetTools).GetMethod("CreateSearchSet", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(tool);
        var groups = Assert.Single(tool!.GetParameters(), parameter => parameter.Name == "conditionGroups");
        Assert.Equal(typeof(List<List<FindItemsCondition>>), groups.ParameterType);
        Assert.True(groups.IsOptional);
    }

    [Fact]
    public void ThePluginExpandsTheGroupsBeforeBuildingTheSearch()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.Root,
            "NavisHelper", "Agent", "Services", "DocumentCommandService.SelectionSets.cs"));
        var expand = source.IndexOf("SearchSetConditionGroups.Expand(", StringComparison.Ordinal);
        var build = source.IndexOf("SelectionSetSearchBuilder.Build(", StringComparison.Ordinal);
        Assert.True(expand >= 0, "the plugin does not call SearchSetConditionGroups.Expand");
        Assert.True(expand < build, "the groups must be expanded before the search is built");
        Assert.Contains("conditionGroups", File.ReadAllText(Path.Combine(RepositoryPaths.Root, "docs", "MCP_TOOL_CONTRACTS.md")),
            StringComparison.Ordinal);
    }
}
