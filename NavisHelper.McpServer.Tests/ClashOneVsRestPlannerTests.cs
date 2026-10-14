using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashOneVsRestPlannerTests
{
    private const string Target = "/12470-14-1400/PI";

    [Theory]
    [InlineData("/12470-14-1400/PI", "12470-14-1400 PI")]
    [InlineData("  /A//B/ ", "A B")]
    [InlineData("Plain name", "Plain name")]
    [InlineData("a\\b", "a b")]
    [InlineData("/", "unnamed")]
    [InlineData(null, "unnamed")]
    public void Label_DropsTheFolderSeparator(string displayName, string expected)
    {
        Assert.Equal(expected, ClashOneVsRestPlanner.Label(displayName));
    }

    [Theory]
    [InlineData("/12470-00-1400/PI", "*-00-*", true)]
    [InlineData("/12470-00-1400/PI", "12470-00-1400 PI", true)]
    [InlineData("/12470-00-1400/CI", "*/pi", false)]
    [InlineData("/12470-00-1400/PI", "*/pi", true)]
    [InlineData("/12470-00-1400/PI", "12470-0?-1400*", true)]
    [InlineData("/12470-00-1400/PI", "", false)]
    public void MatchesMask_IsCaseInsensitiveWildcardOnNameOrLabel(string name, string mask, bool expected)
    {
        Assert.Equal(expected, ClashOneVsRestPlanner.MatchesMask(name, mask));
    }

    [Fact]
    public void DefaultPlan_HasNoFiltersAndPairsTheTargetWithEverySibling()
    {
        var plan = ClashOneVsRestPlanner.Plan(Input(Target, "/12470-00-1400/PI", "/12470-12-1400/CI", "/12470-13-1400/PI"));

        Assert.Equal(ClashOneVsRestPlanner.ModePairs, plan.Mode);
        Assert.Equal("Clash one vs rest/12470-14-1400 PI", plan.SetFolder);
        Assert.Equal(0, plan.ExcludedByMaskCount);
        Assert.Empty(plan.BboxSkippedNames);
        Assert.Equal(4, plan.Sets.Count);
        Assert.True(plan.Sets[0].IsTarget);
        Assert.Equal(3, plan.Pairs.Count);
        Assert.Equal("12470-14-1400 PI vs 12470-00-1400 PI", plan.Pairs[0].TestName);
        Assert.Equal("Clash one vs rest/12470-14-1400 PI/12470-14-1400 PI", plan.Pairs[0].SetAPath);
        Assert.Equal("Clash one vs rest/12470-14-1400 PI/12470-12-1400 CI", plan.Pairs[1].SetBPath);
        Assert.All(plan.Pairs, pair =>
        {
            Assert.Equal(ClashTestTypeHelper.Hard, pair.TestType);
            Assert.Equal(1.0, pair.ToleranceMm);
            Assert.Equal(ClashOneVsRestStatuses.Planned, pair.Status);
        });
    }

    [Fact]
    public void TwentyFourSiblings_GiveTwentyFourTests()
    {
        var names = new List<string> { Target };
        for (var i = 0; i < 24; i++)
            names.Add("/12470-" + i.ToString("D2") + "-1500/PI");
        var plan = ClashOneVsRestPlanner.Plan(InputFromNames(names.ToArray()));

        Assert.Equal(24, plan.Pairs.Count);
        Assert.Equal(25, plan.Sets.Count);
        Assert.Equal(24, plan.Pairs.Select(pair => pair.TestName).Distinct().Count());
    }

    [Fact]
    public void Masks_IncludeThenExclude()
    {
        var input = Input(Target, "/12470-00-1400/PI", "/12470-12-1400/CI", "/12470-13-1400/PI");
        input.IncludeNameMasks.Add("*/PI");
        input.ExcludeNameMasks.Add("*-13-*");

        var plan = ClashOneVsRestPlanner.Plan(input);

        Assert.Equal(2, plan.ExcludedByMaskCount);
        Assert.Equal(new[] { "12470-00-1400 PI" }, plan.Pairs.Select(pair => pair.BName));
    }

    [Fact]
    public void BboxPrefilter_SkipsDisjointSiblingsAndKeepsUnknownBoxes()
    {
        var input = Input(Target, "near", "far", "nobox");
        input.BboxPrefilter = true;
        input.BboxMargin = 0.5;
        input.Target.Box = Box(0, 0, 0, 10, 10, 10);
        input.Rest[0].Box = Box(10.4, 0, 0, 20, 10, 10);
        input.Rest[1].Box = Box(11, 0, 0, 20, 10, 10);

        var plan = ClashOneVsRestPlanner.Plan(input);

        Assert.Equal(new[] { "far" }, plan.BboxSkippedNames);
        Assert.Equal(1, plan.UnknownBoxCount);
        Assert.Equal(new[] { "near", "nobox" }, plan.Pairs.Select(pair => pair.BName));
    }

    [Fact]
    public void BboxPrefilter_IsOffByDefault()
    {
        var input = Input(Target, "far");
        input.Target.Box = Box(0, 0, 0, 1, 1, 1);
        input.Rest[0].Box = Box(100, 100, 100, 101, 101, 101);

        Assert.Single(ClashOneVsRestPlanner.Plan(input).Pairs);
    }

    [Fact]
    public void SingleMode_MakesOneTestAgainstAUnionSet()
    {
        var input = Input(Target, "/12470-00-1400/PI", "/12470-12-1400/CI");
        input.Mode = "SINGLE";

        var plan = ClashOneVsRestPlanner.Plan(input);

        var pair = Assert.Single(plan.Pairs);
        Assert.Equal("12470-14-1400 PI vs 12470-14-1400 PI - rest", pair.TestName);
        Assert.Equal(2, plan.Sets.Count);
        Assert.Equal(new[] { 0, 1 }, plan.Sets[1].RestIndexes);
    }

    [Fact]
    public void SingleMode_WithNothingLeftPlansNoTest()
    {
        var input = Input(Target, "other");
        input.Mode = "single";
        input.ExcludeNameMasks.Add("*");

        Assert.Empty(ClashOneVsRestPlanner.Plan(input).Pairs);
    }

    [Fact]
    public void PairOverride_FirstMatchingMaskWins()
    {
        var input = Input(Target, "/12470-00-1400/PI", "/12470-12-1400/CI");
        input.PairOverrides.Add(new ClashOneVsRestOverride { NameMask = "*/CI", TestType = "clearance", ToleranceMm = 50 });
        input.PairOverrides.Add(new ClashOneVsRestOverride { NameMask = "*", ToleranceMm = 5 });

        var plan = ClashOneVsRestPlanner.Plan(input);

        Assert.Equal(ClashTestTypeHelper.Hard, plan.Pairs[0].TestType);
        Assert.Equal(5.0, plan.Pairs[0].ToleranceMm);
        Assert.Equal(ClashTestTypeHelper.Clearance, plan.Pairs[1].TestType);
        Assert.Equal(50.0, plan.Pairs[1].ToleranceMm);
    }

    [Fact]
    public void SameLabelSiblings_GetUniqueSetAndTestNames()
    {
        var plan = ClashOneVsRestPlanner.Plan(Input(Target, "/A/B", "A B", "/a/b"));

        Assert.Equal(new[] { "A B", "A B (2)", "a b (3)" }, plan.Pairs.Select(pair => pair.BName));
        Assert.Equal(3, plan.Pairs.Select(pair => pair.TestName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void CustomTemplateAndFolder()
    {
        var input = Input(Target, "/12470-00-1400/PI");
        input.PairNameTemplate = "{index} {a} x {b}";
        input.SetFolder = " /Coordination\\Marks/ ";

        var plan = ClashOneVsRestPlanner.Plan(input);

        Assert.Equal("Coordination/Marks", plan.SetFolder);
        Assert.Equal("1 12470-14-1400 PI x 12470-00-1400 PI", plan.Pairs[0].TestName);
    }

    [Theory]
    [InlineData("bogus", null, "testType")]
    [InlineData(null, -1.0, "toleranceMm")]
    [InlineData(null, double.NaN, "toleranceMm")]
    public void InvalidSettings_AreRejected(string testType, double? tolerance, string expectedWord)
    {
        var input = Input(Target, "x");
        input.TestType = testType;
        input.ToleranceMm = tolerance;

        var error = Assert.Throws<ArgumentException>(() => ClashOneVsRestPlanner.Plan(input));
        Assert.Contains(expectedWord, error.Message);
    }

    [Fact]
    public void InvalidModeAndOverride_AreRejected()
    {
        var input = Input(Target, "x");
        input.Mode = "matrix";
        Assert.Throws<ArgumentException>(() => ClashOneVsRestPlanner.Plan(input));

        input = Input(Target, "x");
        input.PairOverrides.Add(new ClashOneVsRestOverride { TestType = "hard" });
        Assert.Throws<ArgumentException>(() => ClashOneVsRestPlanner.Plan(input));
    }

    [Fact]
    public void BoxesOverlap_TouchingCountsAndMarginExtends()
    {
        var a = Box(0, 0, 0, 1, 1, 1);
        Assert.True(ClashOneVsRestPlanner.BoxesOverlap(a, Box(1, 0, 0, 2, 1, 1), 0));
        Assert.False(ClashOneVsRestPlanner.BoxesOverlap(a, Box(1.1, 0, 0, 2, 1, 1), 0));
        Assert.True(ClashOneVsRestPlanner.BoxesOverlap(a, Box(1.1, 0, 0, 2, 1, 1), 0.2));
        Assert.False(ClashOneVsRestPlanner.BoxesOverlap(a, Box(0, 0, 5, 1, 1, 6), 0.2));
        Assert.True(ClashOneVsRestPlanner.BoxesOverlap(a, null, 0));
    }

    private static ClashOneVsRestPlanInput Input(string target, params string[] rest)
    {
        return new ClashOneVsRestPlanInput
        {
            Target = new ClashOneVsRestCandidate { DisplayName = target },
            Rest = rest.Select(name => new ClashOneVsRestCandidate { DisplayName = name }).ToList(),
        };
    }

    private static ClashOneVsRestPlanInput InputFromNames(string[] names) => Input(names[0], names.Skip(1).ToArray());

    private static ClashOneVsRestBox Box(double minX, double minY, double minZ, double maxX, double maxY, double maxZ) =>
        new ClashOneVsRestBox { MinX = minX, MinY = minY, MinZ = minZ, MaxX = maxX, MaxY = maxY, MaxZ = maxZ };
}
