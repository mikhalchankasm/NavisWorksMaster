using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashGroupRenamePlannerTests
{
    [Fact]
    public void SingleRename_ByHandleAndByName_ArePlanned()
    {
        var groups = TopLevel("Зона 01 (3 колл.)", "Зона 02 (2 колл.)");

        var plan = ClashGroupRenamePlanner.Build(groups, new[]
        {
            Rename(handle: "clash-group:4:1", newName: "PI vs IN"),
            Rename(name: "зона 02 (2 колл.)", newName: "PI vs CS"),
        });

        Assert.True(plan.CanApply);
        Assert.Equal(new[] { 0, 1 }, plan.CandidateIndexes);
        Assert.All(plan.Items, item => Assert.Equal(ClashGroupRenamePlanner.StatusPlanned, item.Status));
        Assert.Equal("Зона 01 (3 колл.)", plan.Items[0].OldName);
        Assert.Equal("clash-group:4:2", plan.Items[1].GroupHandle);
        Assert.Equal(3, plan.Items[1].ResultCount);
    }

    [Fact]
    public void BatchOfTwentyTwoZoneRenames_IsPlannedWholly()
    {
        var names = Enumerable.Range(1, 22).Select(index => "Зона " + index.ToString("D2") + " (" + index + " колл.)").ToArray();
        var groups = TopLevel(names);
        var renames = names.Select((name, index) => Rename(name: name, newName: "PI vs mark " + (index + 1))).ToList();

        var plan = ClashGroupRenamePlanner.Build(groups, renames);

        Assert.True(plan.CanApply);
        Assert.Equal(Enumerable.Range(0, 22), plan.CandidateIndexes);
    }

    [Fact]
    public void UnknownHandleAndUnknownName_AreNotFound()
    {
        var plan = ClashGroupRenamePlanner.Build(TopLevel("A"), new[]
        {
            Rename(handle: "clash-group:4:9", newName: "X"),
            Rename(name: "Missing", newName: "Y"),
        });

        Assert.False(plan.CanApply);
        Assert.All(plan.Items, item => Assert.Equal(ClashGroupRenamePlanner.StatusNotFound, item.Status));
        Assert.Equal(new[] { -1, -1 }, plan.CandidateIndexes);
    }

    [Fact]
    public void NameHeldByGroupsInDifferentFolders_IsAmbiguous_ButHandleWorks()
    {
        var groups = new List<ClashGroupRenameCandidate>
        {
            Group("clash-group:1:1", "Level 1", "test"),
            Group("clash-group:1:2", "Pipes", "clash-group:1:1"),
            Group("clash-group:1:3", "Level 2", "test"),
            Group("clash-group:1:4", "Pipes", "clash-group:1:3"),
        };

        var byName = ClashGroupRenamePlanner.Build(groups, new[] { Rename(name: "Pipes", newName: "PI") });
        var byHandle = ClashGroupRenamePlanner.Build(groups, new[] { Rename(handle: "clash-group:1:4", newName: "PI") });

        Assert.Equal(ClashGroupRenamePlanner.StatusAmbiguous, byName.Items[0].Status);
        Assert.Contains("use groupHandle", byName.Items[0].ErrorMessage);
        Assert.True(byHandle.CanApply);
        Assert.Equal(3, byHandle.CandidateIndexes[0]);
    }

    [Fact]
    public void HandleWithNameThatNoLongerMatches_IsRejectedAsStale()
    {
        var plan = ClashGroupRenamePlanner.Build(
            TopLevel("vs 02-1400 EL", "Зона 01 (3 колл.)"),
            new[] { Rename(handle: "clash-group:4:1", name: "Зона 01 (3 колл.)", newName: "PI vs EL") });

        Assert.False(plan.CanApply);
        Assert.Equal(ClashGroupRenamePlanner.StatusInvalid, plan.Items[0].Status);
        Assert.Contains("tree positions", plan.Items[0].ErrorMessage);
    }

    [Fact]
    public void SameNameIsUnchanged_AndDoesNotBlockTheBatch()
    {
        var plan = ClashGroupRenamePlanner.Build(TopLevel("A", "B"), new[]
        {
            Rename(name: "A", newName: "A"),
            Rename(name: "B", newName: "C"),
        });

        Assert.True(plan.CanApply);
        Assert.Equal(ClashGroupRenamePlanner.StatusUnchanged, plan.Items[0].Status);
        Assert.Equal(ClashGroupRenamePlanner.StatusPlanned, plan.Items[1].Status);
    }

    [Fact]
    public void RenamingOneGroupTwice_MarksBothItems()
    {
        var plan = ClashGroupRenamePlanner.Build(TopLevel("A"), new[]
        {
            Rename(handle: "clash-group:4:1", newName: "X"),
            Rename(name: "A", newName: "Y"),
        });

        Assert.False(plan.CanApply);
        Assert.All(plan.Items, item => Assert.Equal(ClashGroupRenamePlanner.StatusDuplicate, item.Status));
    }

    [Fact]
    public void NewNameTakenByAnotherSibling_IsAConflict_ButASwapIsNot()
    {
        var groups = TopLevel("A", "B", "C");

        var conflict = ClashGroupRenamePlanner.Build(groups, new[] { Rename(name: "A", newName: "b") });
        var intoSame = ClashGroupRenamePlanner.Build(groups, new[]
        {
            Rename(name: "A", newName: "Z"),
            Rename(name: "B", newName: "Z"),
        });
        var swap = ClashGroupRenamePlanner.Build(groups, new[]
        {
            Rename(name: "A", newName: "B"),
            Rename(name: "B", newName: "A"),
        });

        Assert.Equal(ClashGroupRenamePlanner.StatusNameConflict, conflict.Items[0].Status);
        Assert.Contains("clash-group:4:2", conflict.Items[0].ErrorMessage);
        Assert.All(intoSame.Items, item => Assert.Equal(ClashGroupRenamePlanner.StatusNameConflict, item.Status));
        Assert.True(swap.CanApply);
    }

    [Fact]
    public void SameNameInAnotherFolder_IsNoConflict()
    {
        var groups = new List<ClashGroupRenameCandidate>
        {
            Group("clash-group:1:1", "Level 1", "test"),
            Group("clash-group:1:2", "Pipes", "clash-group:1:1"),
            Group("clash-group:1:3", "Ducts", "test"),
        };

        var plan = ClashGroupRenamePlanner.Build(groups, new[] { Rename(name: "Ducts", newName: "Pipes") });

        Assert.True(plan.CanApply);
    }

    [Theory]
    [InlineData("clash-group:4:1", "", "")]
    [InlineData("", "", "X")]
    [InlineData(null, null, "  ")]
    public void MissingSelectorOrNewName_IsInvalid(string handle, string name, string newName)
    {
        var plan = ClashGroupRenamePlanner.Build(TopLevel("A"), new[] { Rename(handle, name, newName) });

        Assert.False(plan.CanApply);
        Assert.Equal(ClashGroupRenamePlanner.StatusInvalid, plan.Items[0].Status);
    }

    [Theory]
    [InlineData(3, 7, "clash-group:3:7")]
    [InlineData(0, 1, "")]
    [InlineData(1, 0, "")]
    public void BuildGroupHandle_MatchesTheListingFormat(int test, int group, string expected)
    {
        Assert.Equal(expected, ClashHandleHelper.BuildGroupHandle(test, group));
    }

    [Theory]
    [InlineData(" CLASH-GROUP:3:7 ", true, 3, 7)]
    [InlineData("clash-group:0:7", false, 0, 7)]
    [InlineData("clash-group:3", false, 3, 0)]
    [InlineData("clash-result:3:7", false, 0, 0)]
    [InlineData("clash-group:-3:7", false, 0, 0)]
    [InlineData(null, false, 0, 0)]
    public void TryParseGroupHandle_AcceptsOnlyPositiveIndexes(string value, bool expectedOk, int expectedTest, int expectedGroup)
    {
        var ok = ClashHandleHelper.TryParseGroupHandle(value, out var test, out var group);

        Assert.Equal(expectedOk, ok);
        if (ok)
        {
            Assert.Equal(expectedTest, test);
            Assert.Equal(expectedGroup, group);
        }
    }

    private static List<ClashGroupRenameCandidate> TopLevel(params string[] names)
    {
        return names.Select((name, index) => Group("clash-group:4:" + (index + 1), name, "test", index + 2)).ToList();
    }

    private static ClashGroupRenameCandidate Group(string handle, string name, string parentKey, int resultCount = 1)
    {
        return new ClashGroupRenameCandidate { Handle = handle, Name = name, Path = name, ParentKey = parentKey, ResultCount = resultCount };
    }

    private static ClashGroupRenameItem Rename(string handle = null, string name = null, string newName = null)
    {
        return new ClashGroupRenameItem { GroupHandle = handle, GroupName = name, NewName = newName };
    }
}
