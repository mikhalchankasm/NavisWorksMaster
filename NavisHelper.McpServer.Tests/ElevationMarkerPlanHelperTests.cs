using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ElevationMarkerPlanHelperTests
{
    [Fact]
    public void Build_UsesBoundingBoxTopCenterAndTargetZ()
    {
        var plan = ElevationMarkerPlanHelper.Build(
            new[]
            {
                new ElevationBounds
                {
                    MinX = 10,
                    MinY = 20,
                    MinZ = 30,
                    MaxX = 14,
                    MaxY = 28,
                    MaxZ = 45,
                },
            },
            targetZ: 5);

        var item = Assert.Single(plan);
        Assert.Equal(12, item.TopX);
        Assert.Equal(24, item.TopY);
        Assert.Equal(45, item.TopZ);
        Assert.Equal(5, item.TargetZ);
        Assert.Equal(40, item.Difference);
    }

    [Theory]
    [InlineData(12345.4, "Z=+12345 MM")]
    [InlineData(12345.5, "Z=+12346 MM")]
    [InlineData(-1250.5, "Z=-1251 MM")]
    [InlineData(0, "Z=0 MM")]
    public void FormatElevationLabel_UsesSignedWholeMillimeters(double millimeters, string expected)
    {
        Assert.Equal(expected, ElevationMarkerPlanHelper.FormatElevationLabel(millimeters));
    }

    [Fact]
    public void Build_AcceptsObjectBelowTargetLevel()
    {
        var item = Assert.Single(ElevationMarkerPlanHelper.Build(
            new[] { Bounds(2) },
            targetZ: 10));

        Assert.Equal(-8, item.Difference);
    }

    [Fact]
    public void Build_RejectsEmptyInput()
    {
        Assert.Throws<ArgumentException>(() =>
            ElevationMarkerPlanHelper.Build(Array.Empty<ElevationBounds>(), 0));
    }

    [Fact]
    public void Build_RejectsInvertedBounds()
    {
        var bounds = Bounds(2);
        bounds.MaxZ = -1;

        Assert.Throws<ArgumentException>(() =>
            ElevationMarkerPlanHelper.Build(new[] { bounds }, 0));
    }

    [Fact]
    public void Build_RejectsBatchAboveSafetyLimit()
    {
        var bounds = Enumerable.Range(0, 101).Select(index => Bounds(index)).ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            ElevationMarkerPlanHelper.Build(bounds, 0));
    }

    private static ElevationBounds Bounds(double maxZ)
    {
        return new ElevationBounds
        {
            MinX = 0,
            MinY = 0,
            MinZ = 0,
            MaxX = 2,
            MaxY = 2,
            MaxZ = maxZ,
        };
    }
}
