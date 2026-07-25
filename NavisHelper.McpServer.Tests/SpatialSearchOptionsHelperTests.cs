using System;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SpatialSearchOptionsHelperTests
{
    [Theory]
    [InlineData(null, SpatialSearchOptionsHelper.Intersects)]
    [InlineData(" overlaps ", SpatialSearchOptionsHelper.Intersects)]
    [InlineData("inside", SpatialSearchOptionsHelper.Contains)]
    [InlineData("centre", SpatialSearchOptionsHelper.Center)]
    public void NormalizeMatchMode_UsesSupportedAliases(string value, string expected)
    {
        Assert.Equal(expected, SpatialSearchOptionsHelper.NormalizeMatchMode(value));
    }

    [Fact]
    public void ValidateBounds_RejectsReversedAxis()
    {
        var exception = Assert.Throws<ArgumentException>(() => SpatialSearchOptionsHelper.ValidateBounds(
            new SpatialPoint { X = 2, Y = 0, Z = 0 },
            new SpatialPoint { X = 1, Y = 1, Z = 1 }));

        Assert.Equal("min coordinates must not exceed max coordinates.", exception.Message);
    }

    [Fact]
    public void ClampLimits_KeepRequestsWithinSafeBounds()
    {
        Assert.Equal(SpatialSearchOptionsHelper.DefaultMaxScannedItems, SpatialSearchOptionsHelper.ClampMaxScannedItems(null));
        Assert.Equal(SpatialSearchOptionsHelper.MaxMaxScannedItems, SpatialSearchOptionsHelper.ClampMaxScannedItems(int.MaxValue));
        Assert.Equal(1, SpatialSearchOptionsHelper.ClampMaxResults(0));
        Assert.Equal(SpatialSearchOptionsHelper.MaxPreviewLimit, SpatialSearchOptionsHelper.ClampPreviewLimit(int.MaxValue));
    }
}
