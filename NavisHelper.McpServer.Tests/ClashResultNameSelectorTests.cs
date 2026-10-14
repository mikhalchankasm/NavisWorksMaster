using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashResultNameSelectorTests
{
    [Fact]
    public void Resolve_ReturnsTreePositionsInRequestOrder()
    {
        var selection = ClashResultNameSelector.Resolve(
            new[] { "Clash1", "Clash2", "Clash3" },
            new[] { " clash3 ", "Clash1", "CLASH3" });

        Assert.True(selection.IsComplete);
        Assert.Equal(new[] { 2, 0 }, selection.Positions);
    }

    [Fact]
    public void Resolve_ReportsMissingAndAmbiguousNamesInsteadOfGuessing()
    {
        var selection = ClashResultNameSelector.Resolve(
            new[] { "Clash1", "Clash2", "clash2", "", null },
            new[] { "Clash2", "Clash9", "Clash1", "" });

        Assert.False(selection.IsComplete);
        Assert.Equal(new[] { 0 }, selection.Positions);
        Assert.Equal(new[] { "Clash9" }, selection.MissingNames);
        Assert.Equal(new[] { "Clash2" }, selection.AmbiguousNames);
        var message = ClashResultNameSelector.DescribeProblems(selection);
        Assert.Contains("not found: Clash9", message);
        Assert.Contains("more than one result", message);
    }

    [Fact]
    public void DescribeProblems_IsEmptyForACompleteSelection()
    {
        var selection = ClashResultNameSelector.Resolve(new[] { "Clash1" }, new[] { "Clash1" });

        Assert.Equal(string.Empty, ClashResultNameSelector.DescribeProblems(selection));
    }

    /// <summary>
    /// The live finding on the 12470 model: three clash_group_custom calls in one test,
    /// all planned from one clash_list_results listing. The simulated tree moves results
    /// the way ClashGroupMutationService does (new group inserted at index 0, results
    /// moved into it at index 0), so positional handles from the listing drift while
    /// names resolved against the current tree land in the intended groups.
    /// </summary>
    [Fact]
    public void SeveralGroupingsFromOneListing_NamesStayCorrectWhilePositionalHandlesDrift()
    {
        var plan = new[]
        {
            ("vs 01-1400 IN", new[] { "Clash7", "Clash8" }),
            ("vs 14-1400 CS", new[] { "Clash2", "Clash3" }),
            ("vs 11-1400 CS", new[] { "Clash5", "Clash9" }),
        };

        var byName = SimulatedTest.WithResults(9);
        foreach (var (group, names) in plan)
        {
            var selection = ClashResultNameSelector.Resolve(byName.FlattenedNames(), names);
            Assert.True(selection.IsComplete);
            byName.GroupCustom(group, selection.Positions);
        }

        foreach (var (group, names) in plan)
            Assert.Equal(names, byName.GroupContents(group));
        Assert.Equal(new[] { "Clash1", "Clash4", "Clash6" }, byName.UngroupedNames());

        var listing = SimulatedTest.WithResults(9).FlattenedNames();
        var byHandle = SimulatedTest.WithResults(9);
        foreach (var (group, names) in plan)
        {
            var listedPositions = names.Select(name => listing.IndexOf(name)).ToList();
            byHandle.GroupCustom(group, listedPositions);
        }

        // Each later call takes results by stale positions, and even pulls Clash8 out of the first group.
        Assert.Equal(new[] { "Clash7" }, byHandle.GroupContents("vs 01-1400 IN"));
        Assert.Equal(new[] { "Clash1", "Clash8" }, byHandle.GroupContents("vs 14-1400 CS"));
        Assert.Equal(new[] { "Clash3", "Clash9" }, byHandle.GroupContents("vs 11-1400 CS"));
    }

    private sealed class SimulatedTest
    {
        private readonly List<Node> _children = new List<Node>();

        public static SimulatedTest WithResults(int count)
        {
            var test = new SimulatedTest();
            for (var index = 1; index <= count; index++)
                test._children.Add(new Node("Clash" + index, isGroup: false));
            return test;
        }

        public List<string> FlattenedNames()
        {
            return Locations(_children).Select(location => location.Parent[location.Index].Name).ToList();
        }

        public IEnumerable<string> UngroupedNames()
        {
            return _children.Where(node => !node.IsGroup).Select(node => node.Name);
        }

        public IEnumerable<string> GroupContents(string group)
        {
            return _children.Single(node => node.IsGroup && node.Name == group).Children.Select(node => node.Name);
        }

        /// <summary>Mirrors FindOrCreateGroup + RebuildGroup for a new group.</summary>
        public void GroupCustom(string groupName, IEnumerable<int> flattenedPositions)
        {
            var all = Locations(_children).ToList();
            var targets = flattenedPositions.Select(position => all[position].Parent[all[position].Index]).ToList();
            var group = new Node(groupName, isGroup: true);
            _children.Insert(0, group);
            var moves = Locations(_children)
                .Where(location => location.Parent != group.Children && targets.Contains(location.Parent[location.Index]))
                .GroupBy(location => location.Parent)
                .ToList();
            foreach (var parent in moves)
            {
                foreach (var location in parent.OrderByDescending(item => item.Index))
                {
                    var node = location.Parent[location.Index];
                    location.Parent.RemoveAt(location.Index);
                    group.Children.Insert(0, node);
                }
            }
        }

        private static IEnumerable<(List<Node> Parent, int Index)> Locations(List<Node> parent)
        {
            for (var index = 0; index < parent.Count; index++)
            {
                if (!parent[index].IsGroup)
                {
                    yield return (parent, index);
                    continue;
                }
                foreach (var nested in Locations(parent[index].Children))
                    yield return nested;
            }
        }

        private sealed class Node
        {
            public Node(string name, bool isGroup)
            {
                Name = name;
                IsGroup = isGroup;
            }

            public string Name { get; }
            public bool IsGroup { get; }
            public List<Node> Children { get; } = new List<Node>();
        }
    }
}
