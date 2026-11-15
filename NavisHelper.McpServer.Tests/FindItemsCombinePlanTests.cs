using System.Collections.Generic;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance for #458. Condition-level logic follows FindItemsConditionGroups.Split:
// AND binds tighter, so the result is the union over the groups of the intersection inside each.
// The whole-model route used to fold the conditions left to right, and with combineOperator=all
// it intersected every condition, so [A, or B] returned A ∩ B.
public sealed class FindItemsCombinePlanTests
{
    // A = {1,2}, B = {3,4}, C = {4,5}, D = {6}, E = {} (no hits).
    private static readonly Dictionary<string, int[]> Hits = new()
    {
        ["A"] = new[] { 1, 2 },
        ["B"] = new[] { 3, 4 },
        ["C"] = new[] { 4, 5 },
        ["D"] = new[] { 6 },
        ["E"] = new int[0],
    };

    [Fact]
    public void AndBindsTighterThanOr()
    {
        // [A, or B, and C] = A ∪ (B ∩ C) = {1,2,4}; the left-to-right fold gives (A ∪ B) ∩ C = {4}.
        Assert.Equal(new[] { 1, 2, 4 }, Evaluate(new List<int>(), C("A"), C("B", "or"), C("C", "and")));
    }

    [Fact]
    public void OrAloneIsAUnionInGroupOrder()
    {
        Assert.Equal(new[] { 1, 2, 3, 4 }, Evaluate(new List<int>(), C("A"), C("B", "or")));
    }

    [Fact]
    public void WithoutOrEveryConditionIsIntersected()
    {
        Assert.Equal(new[] { 4 }, Evaluate(new List<int>(), C("B"), C("C", "and")));
    }

    [Fact]
    public void AGroupStopsAtItsFirstEmptyIntersectionAndTheNextGroupStillRuns()
    {
        var asked = new List<int>();

        var result = Evaluate(asked, C("A"), C("E", "and"), C("C", "and"), C("D", "or"));

        Assert.Equal(new[] { 6 }, result);
        Assert.Equal(new[] { 0, 1, 3 }, asked);
    }

    [Fact]
    public void NoConditionsGiveNoItems()
    {
        Assert.Empty(FindItemsCombinePlan.Evaluate<int>(new List<FindItemsCondition>(), _ => new[] { 1 }, null));
        Assert.Empty(FindItemsCombinePlan.Evaluate<int>(null, _ => new[] { 1 }, null));
    }

    // The plugin cannot compile in L0, so the wiring is pinned by source: the whole-model route
    // combines condition-level logic through the plan.
    [Fact]
    public void TheWholeModelSearchCombinesConditionLevelLogicThroughThePlan()
    {
        var directory = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (directory != null && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(
            directory!.FullName, "NavisHelper", "Agent", "Services", "SearchService.Execution.cs"));
        int start = source.IndexOf("private static List<ModelItem> ExecuteSearch(", System.StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = source.IndexOf("\n        }", start, System.StringComparison.Ordinal);
        var body = source.Substring(start, end - start);

        Assert.Contains("FindItemsCombinePlan.Evaluate", body, System.StringComparison.Ordinal);
    }

    private static int[] Evaluate(List<int> asked, params FindItemsCondition[] conditions)
    {
        List<int> result = FindItemsCombinePlan.Evaluate<int>(
            conditions,
            index =>
            {
                asked.Add(index);
                return Hits[conditions[index].Value];
            },
            null);
        return result.ToArray();
    }

    private static FindItemsCondition C(string value, string logicalOperator = null)
    {
        return new FindItemsCondition { Value = value, LogicalOperator = logicalOperator };
    }
}
