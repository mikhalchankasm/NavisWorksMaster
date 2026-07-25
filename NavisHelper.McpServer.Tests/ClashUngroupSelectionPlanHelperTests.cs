using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashUngroupSelectionPlanHelperTests
{
    [Fact]
    public void Build_DeduplicatesGroupsAndSortsEachContainerByDescendingIndex()
    {
        var duplicate = Candidate("g2", "p1", 2, 4);
        var plan = ClashUngroupSelectionPlanHelper.Build(
            new[]
            {
                Candidate("g1", "p1", 1, 3),
                duplicate,
                Candidate("g3", "p2", 0, 5),
                Candidate("g2", "p1", 2, 4),
            });

        Assert.Equal(new[] { "g2", "g1", "g3" }, plan.Groups.Select(group => group.GroupKey));
    }

    [Theory]
    [InlineData(null, "p1", 1, 3)]
    [InlineData("g1", null, 1, 3)]
    [InlineData("g1", "p1", -1, 3)]
    [InlineData("g1", "p1", 1, -1)]
    public void Build_RejectsInvalidPersistentLocation(string groupKey, string containerKey, int index, int resultCount)
    {
        var rows = new[] { Candidate(groupKey, containerKey, index, resultCount) };

        Assert.Throws<InvalidOperationException>(() => ClashUngroupSelectionPlanHelper.Build(rows));
    }

    [Fact]
    public void Build_RejectsConflictingDuplicateLocations()
    {
        var rows = new[]
        {
            Candidate("g1", "p1", 1, 3),
            Candidate("g1", "p1", 2, 3),
        };

        Assert.Throws<InvalidOperationException>(() => ClashUngroupSelectionPlanHelper.Build(rows));
    }

    [Fact]
    public void Build_RequiresPersistentGroups()
    {
        Assert.Throws<ArgumentException>(() => ClashUngroupSelectionPlanHelper.Build(Array.Empty<ClashUngroupSelectionCandidate>()));
        Assert.Throws<ArgumentException>(() => ClashUngroupSelectionPlanHelper.Build(null));
    }

    private static ClashUngroupSelectionCandidate Candidate(
        string groupKey,
        string containerKey,
        int index,
        int resultCount)
    {
        return new ClashUngroupSelectionCandidate
        {
            GroupKey = groupKey,
            ContainerKey = containerKey,
            GroupIndex = index,
            ResultCount = resultCount,
        };
    }
}
