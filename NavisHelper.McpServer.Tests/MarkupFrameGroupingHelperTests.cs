using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class MarkupFrameGroupingHelperTests
{
    [Fact]
    public void LargeItem_GetsOneSoloFrame()
    {
        var groups = MarkupFrameGroupingHelper.Group(
            new[] { Box(0, 0, 1600, 500) },
            soloMinHorizontalSize: 1500,
            mergeGap: 1000);

        var group = Assert.Single(groups);
        Assert.True(group.IsSolo);
        Assert.Equal(new[] { 0 }, group.SourceIndices);
    }

    [Fact]
    public void EightNearbySmallItems_MergeIntoOneFrame()
    {
        var boxes = Enumerable.Range(0, 8)
            .Select(index => Box(index * 600, 0, index * 600 + 100, 100))
            .ToArray();

        var groups = MarkupFrameGroupingHelper.Group(boxes, 1500, 1000);

        var group = Assert.Single(groups);
        Assert.False(group.IsSolo);
        Assert.Equal(8, group.SourceIndices.Count);
        Assert.Equal(4300, group.Bounds.MaxX);
    }

    [Fact]
    public void TwoLargeItemsAndSmallCloud_CreateThreeFrames()
    {
        var groups = MarkupFrameGroupingHelper.Group(
            new[]
            {
                Box(0, 0, 2000, 500),
                Box(5000, 0, 7000, 500),
                Box(9000, 0, 9100, 100),
                Box(9500, 0, 9600, 100),
                Box(10000, 0, 10100, 100),
            },
            1500,
            1000);

        Assert.Equal(3, groups.Count);
        Assert.Equal(2, groups.Count(group => group.IsSolo));
        var cloud = Assert.Single(groups, group => !group.IsSolo);
        Assert.Equal(new[] { 2, 3, 4 }, cloud.SourceIndices);
    }

    [Fact]
    public void DistantSmallItems_RemainSeparateComponents()
    {
        var groups = MarkupFrameGroupingHelper.Group(
            new[] { Box(0, 0, 100, 100), Box(1500, 0, 1600, 100) },
            1500,
            1000);

        Assert.Equal(2, groups.Count);
        Assert.All(groups, group => Assert.False(group.IsSolo));
    }

    [Fact]
    public void LargeSeparatedSelection_UsesSpatialSweepWithoutHittingPairGuard()
    {
        var boxes = Enumerable.Range(0, 1528)
            .Select(index => Box(index * 6000.0, 0, index * 6000.0 + 100, 100))
            .ToArray();

        var groups = MarkupFrameGroupingHelper.Group(boxes, 999999, 5000);

        Assert.Equal(1528, groups.Count);
    }

    [Fact]
    public void DenseSelection_MergesWithoutComparingPairsInsideKnownComponent()
    {
        var boxes = Enumerable.Range(0, 1528)
            .Select(index => Box(index, 0, index + 100, 100))
            .ToArray();

        var group = Assert.Single(MarkupFrameGroupingHelper.Group(boxes, 999999, 100000));

        Assert.Equal(1528, group.SourceIndices.Count);
    }

    [Fact]
    public void CandidateGuard_ReturnsActionableFailureInsteadOfUnboundedWork()
    {
        var boxes = new[]
        {
            Box(0, 0, 100, 100),
            Box(900, 900, 1000, 1000),
            Box(1800, 1800, 1900, 1900),
        };

        var error = Assert.Throws<MarkupFrameGroupingLimitExceededException>(() =>
            MarkupFrameGroupingHelper.Group(boxes, 999999, 1000, maxCandidateComparisons: 1));

        Assert.Contains("Reduce markMergeGapMm", error.Message);
        Assert.True(error.CandidateComparisons > 1);
    }

    [Fact]
    public void CandidateGuard_CountsVisitedPairsRejectedByYAxisFilter()
    {
        var boxes = Enumerable.Range(0, 6)
            .Select(index => Box(0, index * 2000.0, 100, index * 2000.0 + 100))
            .ToArray();

        var error = Assert.Throws<MarkupFrameGroupingLimitExceededException>(() =>
            MarkupFrameGroupingHelper.Group(boxes, 999999, 1000, maxCandidateComparisons: 10));

        Assert.Equal(11, error.CandidateComparisons);
    }

    private static MarkupFrameBounds Box(double minX, double minY, double maxX, double maxY)
    {
        return new MarkupFrameBounds
        {
            MinX = minX,
            MinY = minY,
            MinZ = 0,
            MaxX = maxX,
            MaxY = maxY,
            MaxZ = 100,
        };
    }
}
