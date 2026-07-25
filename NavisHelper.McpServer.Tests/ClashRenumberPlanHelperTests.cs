using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashRenumberPlanHelperTests
{
    [Theory]
    [InlineData(null, 5000)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(42, 42)]
    [InlineData(60000, 50000)]
    public void ClampLimit_AppliesDefaultMinimumAndMaximum(int? limit, int expected)
    {
        Assert.Equal(expected, ClashRenumberPlanHelper.ClampLimit(limit));
    }

    [Theory]
    [InlineData(null, true, 1)]
    [InlineData(0, true, 0)]
    [InlineData(10, true, 10)]
    [InlineData(-1, false, -1)]
    public void TryNormalizeStartNumber_RejectsOnlyNegativeValues(int? value, bool expectedResult, int expectedStart)
    {
        var result = ClashRenumberPlanHelper.TryNormalizeStartNumber(value, out var start);

        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedStart, start);
    }

    [Theory]
    [InlineData(null, 4)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(6, 6)]
    [InlineData(99, 12)]
    public void NormalizeNumberWidth_AppliesDefaultMinimumAndMaximum(int? value, int expected)
    {
        Assert.Equal(expected, ClashRenumberPlanHelper.NormalizeNumberWidth(value));
    }

    [Theory]
    [InlineData(null, "top_level")]
    [InlineData("", "top_level")]
    [InlineData("top", "top_level")]
    [InlineData("standard-form", "top_level")]
    [InlineData("all", "recursive")]
    [InlineData("tree", "recursive")]
    [InlineData("bad", null)]
    public void NormalizeScope_RecognizesAliases(string value, string expected)
    {
        Assert.Equal(expected, ClashRenumberPlanHelper.NormalizeScope(value));
    }

    [Theory]
    [InlineData(null, "current")]
    [InlineData("", "current")]
    [InlineData("tree", "current")]
    [InlineData("standard form", "current")]
    [InlineData("natural-name", "name")]
    [InlineData("display_name", "name")]
    [InlineData("bad", null)]
    public void NormalizeOrder_RecognizesAliases(string value, string expected)
    {
        Assert.Equal(expected, ClashRenumberPlanHelper.NormalizeOrder(value));
    }

    [Fact]
    public void BuildPlan_AssignsSequentialNumbersAndMetadata()
    {
        var result = ClashRenumberPlanHelper.BuildPlan(
            new[]
            {
                new ClashRenumberPlanSource
                {
                    TestIndex = 2,
                    TestHandle = "clash-test:2",
                    TestName = "Test A",
                    ItemType = "group",
                    GroupPath = "Parent",
                    OldName = "0007 - Pump Station [NH:B]",
                },
                new ClashRenumberPlanSource
                {
                    TestIndex = 2,
                    TestHandle = "clash-test:2",
                    TestName = "Test A",
                    ItemType = "result",
                    GroupPath = "Parent",
                    OldName = "Valve",
                },
            },
            startNumber: 10,
            numberWidth: 4,
            prefix: "",
            suffix: "",
            separator: " - ",
            preserveExistingName: true,
            stripExistingNumber: true);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("0010", result.Items[0].NumberText);
        Assert.Equal("0010 - Pump Station [NH:B]", result.Items[0].NewName);
        Assert.Equal("0011 - Valve", result.Items[1].NewName);
        Assert.Equal("clash-test:2", result.Items[0].TestHandle);
        Assert.Equal("Parent", result.Items[0].GroupPath);
        Assert.Equal(2, result.PlannedRenameCount);
        Assert.Equal(0, result.SkippedCount);
    }

    [Fact]
    public void BuildPlan_MarksUnchangedItemsAsSkipped()
    {
        var result = ClashRenumberPlanHelper.BuildPlan(
            new[]
            {
                new ClashRenumberPlanSource { OldName = "0001" },
                new ClashRenumberPlanSource { OldName = "Pump" },
            },
            startNumber: 1,
            numberWidth: 4,
            prefix: "",
            suffix: "",
            separator: " - ",
            preserveExistingName: false,
            stripExistingNumber: true);

        Assert.Equal("unchanged", result.Items[0].Status);
        Assert.Equal("planned", result.Items[1].Status);
        Assert.Equal(1, result.PlannedRenameCount);
        Assert.Equal(1, result.SkippedCount);
    }

    [Fact]
    public void BuildPlan_ToleratesNullSources()
    {
        var result = ClashRenumberPlanHelper.BuildPlan(
            new ClashRenumberPlanSource[] { null },
            startNumber: 0,
            numberWidth: 1,
            prefix: "R-",
            suffix: "",
            separator: null,
            preserveExistingName: true,
            stripExistingNumber: true);

        Assert.Single(result.Items);
        Assert.Equal("R-0", result.Items[0].NewName);
        Assert.Equal("", result.Items[0].OldName);
        Assert.Equal("planned", result.Items[0].Status);
    }
}
