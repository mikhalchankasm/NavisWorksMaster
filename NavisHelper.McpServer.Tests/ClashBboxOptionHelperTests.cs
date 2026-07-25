using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashBboxOptionHelperTests
{
    [Theory]
    [InlineData(null, "top_level_files")]
    [InlineData("", "top_level_files")]
    [InlineData(" top_level_files ", "top_level_files")]
    [InlineData("TOP_LEVEL", "top_level_files")]
    [InlineData("roots", "top_level_files")]
    [InlineData("all", null)]
    public void NormalizeRootMode_ReturnsCanonicalModeOrNull(string value, string expected)
    {
        Assert.Equal(expected, ClashBboxOptionHelper.NormalizeRootMode(value));
    }

    [Theory]
    [InlineData(null, true, 1)]
    [InlineData(0, true, 0)]
    [InlineData(1, true, 1)]
    [InlineData(2, true, 2)]
    [InlineData(-1, false, -1)]
    [InlineData(3, false, 3)]
    public void TryNormalizeRefineDepth_ValidatesRange(int? value, bool expectedOk, int expectedDepth)
    {
        var ok = ClashBboxOptionHelper.TryNormalizeRefineDepth(value, out var depth);

        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedDepth, depth);
    }

    [Theory]
    [InlineData(null, 500)]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(42, 42)]
    [InlineData(3000, 2000)]
    public void ClampRootItems_AppliesDefaultMinimumAndMaximum(int? value, int expected)
    {
        Assert.Equal(expected, ClashBboxOptionHelper.ClampRootItems(value));
    }

    [Theory]
    [InlineData(null, 50000)]
    [InlineData(0, 1)]
    [InlineData(12, 12)]
    [InlineData(300000, 250000)]
    public void ClampCandidatePairs_AppliesDefaultMinimumAndMaximum(int? value, int expected)
    {
        Assert.Equal(expected, ClashBboxOptionHelper.ClampCandidatePairs(value));
    }

    [Theory]
    [InlineData(null, 200)]
    [InlineData(0, 1)]
    [InlineData(25, 25)]
    [InlineData(3000, 2000)]
    public void ClampPreviewLimit_AppliesDefaultMinimumAndMaximum(int? value, int expected)
    {
        Assert.Equal(expected, ClashBboxOptionHelper.ClampPreviewLimit(value));
    }

    [Theory]
    [InlineData(null, 200)]
    [InlineData(0, 1)]
    [InlineData(25, 25)]
    [InlineData(300000, 250000)]
    public void ClampPairTestsCreateLimit_UsesPreviewDefaultAndCandidateMaximum(int? value, int expected)
    {
        Assert.Equal(expected, ClashBboxOptionHelper.ClampPairTestsCreateLimit(value));
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(25, 25)]
    [InlineData(2000, 1000)]
    public void ClampMatrixSelectedItems_AppliesDefaultMinimumAndMaximum(int? value, int expected)
    {
        Assert.Equal(expected, ClashBboxOptionHelper.ClampMatrixSelectedItems(value));
    }
}
