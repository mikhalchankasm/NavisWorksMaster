using System;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class WorldMarkerOverlayGeometryTests
{
    [Theory]
    [InlineData(WorldMarkerStyles.Target, 34)]
    [InlineData(WorldMarkerStyles.Cross, 3)]
    [InlineData(WorldMarkerStyles.Circle, 32)]
    [InlineData(WorldMarkerStyles.Pin, 1)]
    [InlineData(WorldMarkerStyles.Box, 12)]
    [InlineData(WorldMarkerStyles.Pole, 1)]
    public void Build_DrawsTheExpectedSegmentCountPerStyle(string style, int expectedCount)
    {
        var figure = WorldMarkerOverlayGeometry.Build(Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 10,
            Y = 20,
            Z = 30,
            Size = 4,
            Style = style,
        }));

        Assert.Equal(expectedCount, figure.Segments.Count);
    }

    [Fact]
    public void Build_Box_DrawsTheTwelveCubeEdgesAtExactCoordinates()
    {
        var figure = WorldMarkerOverlayGeometry.Build(Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 10,
            Y = 20,
            Z = 30,
            Size = 4,
            Style = "box",
        }));

        AssertSegments(figure,
            (8, 18, 28, 12, 18, 28),
            (12, 18, 28, 12, 22, 28),
            (12, 22, 28, 8, 22, 28),
            (8, 22, 28, 8, 18, 28),
            (8, 18, 32, 12, 18, 32),
            (12, 18, 32, 12, 22, 32),
            (12, 22, 32, 8, 22, 32),
            (8, 22, 32, 8, 18, 32),
            (8, 18, 28, 8, 18, 32),
            (12, 18, 28, 12, 18, 32),
            (12, 22, 28, 12, 22, 32),
            (8, 22, 28, 8, 22, 32));
        AssertPoint(figure.HeadAnchor, 10, 20, 30);
        AssertPoint(figure.LabelAnchor, 10, 20, 32);
    }

    [Fact]
    public void Build_Cross_DrawsThreeAxisArmsOfTheWorldSizeCentredOnTheAnchor()
    {
        var figure = WorldMarkerOverlayGeometry.Build(Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 10,
            Y = 20,
            Z = 30,
            Size = 4,
            Style = "cross",
        }));

        AssertSegments(figure,
            (8, 20, 30, 12, 20, 30),
            (10, 18, 30, 10, 22, 30),
            (10, 20, 28, 10, 20, 32));
        AssertPoint(figure.HeadAnchor, 10, 20, 30);
        AssertPoint(figure.LabelAnchor, 10, 20, 32);
    }

    [Fact]
    public void Build_Target_DrawsTheHorizontalRingAndTwoDiameters()
    {
        var figure = WorldMarkerOverlayGeometry.Build(Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 10,
            Y = 20,
            Z = 30,
            Size = 4,
            Style = "target",
        }));

        Assert.Equal(WorldMarkerOverlayGeometry.RingSegmentCount + 2, figure.Segments.Count);
        AssertPoint(figure.Segments[0].Start, 12, 20, 30);
        AssertPoint(figure.Segments[0].End, 11.96157056080646, 20.390180644032256, 30);
        AssertPoint(figure.Segments[8].Start, 10, 22, 30);
        AssertPoint(figure.Segments[31].Start, 11.96157056080646, 19.609819355967744, 30);
        AssertPoint(figure.Segments[31].End, 12, 20, 30);
        AssertSegment(figure.Segments[32], (8, 20, 30, 12, 20, 30));
        AssertSegment(figure.Segments[33], (10, 18, 30, 10, 22, 30));
        AssertPoint(figure.HeadAnchor, 10, 20, 30);
        AssertPoint(figure.LabelAnchor, 10, 20, 30);
    }

    [Fact]
    public void Build_Circle_DrawsTheRingWithoutDiameters()
    {
        var figure = WorldMarkerOverlayGeometry.Build(Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 10,
            Y = 20,
            Z = 30,
            Size = 4,
            Style = "circle",
        }));

        Assert.Equal(WorldMarkerOverlayGeometry.RingSegmentCount, figure.Segments.Count);
        AssertPoint(figure.Segments[0].Start, 12, 20, 30);
        AssertPoint(figure.Segments[0].End, 11.96157056080646, 20.390180644032256, 30);
        AssertPoint(figure.LabelAnchor, 10, 20, 30);
    }

    [Fact]
    public void Build_RingLiesInTheHorizontalPlaneAndClosesOnItself()
    {
        var figure = WorldMarkerOverlayGeometry.Build(Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 10,
            Y = 20,
            Z = 30,
            Size = 4,
            Style = "circle",
        }));

        for (var index = 0; index < figure.Segments.Count; index++)
        {
            var segment = figure.Segments[index];
            Assert.Equal(30, segment.Start.Z, 12);
            Assert.Equal(30, segment.End.Z, 12);
            Assert.Equal(2, Radius(segment.Start), 12);
            Assert.Equal(2, Radius(segment.End), 12);
            AssertPoint(segment.End, figure.Segments[(index + 1) % figure.Segments.Count].Start);
        }
    }

    [Fact]
    public void Build_Pin_DrawsAVerticalStemAndAnchorsTheHeadAtItsTop()
    {
        var figure = WorldMarkerOverlayGeometry.Build(Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 10,
            Y = 20,
            Z = 30,
            Size = 4,
            Style = "pin",
        }));

        AssertSegments(figure, (10, 20, 30, 10, 20, 34));
        AssertPoint(figure.HeadAnchor, 10, 20, 34);
        AssertPoint(figure.LabelAnchor, 10, 20, 34);
    }

    [Fact]
    public void Build_Pole_DrawsOnlyThePoleFromBaseToTop()
    {
        var figure = WorldMarkerOverlayGeometry.Build(Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 10,
            Y = 20,
            Z = 30,
            Size = 4,
            Style = "pole",
            Pole = new WorldMarkerPole { BaseZ = 5, TopZ = 42 },
        }));

        AssertSegments(figure, (10, 20, 5, 10, 20, 42));
        AssertPoint(figure.HeadAnchor, 10, 20, 30);
        AssertPoint(figure.LabelAnchor, 10, 20, 42);
    }

    [Fact]
    public void Build_AppendsThePoleAfterTheWorldFigure()
    {
        var figure = WorldMarkerOverlayGeometry.Build(Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 10,
            Y = 20,
            Z = 30,
            Size = 4,
            Style = "box",
            Pole = new WorldMarkerPole { Enabled = true, BaseZ = 0, TopZ = 40 },
        }));

        Assert.Equal(13, figure.Segments.Count);
        AssertPoint(figure.Segments[12].Start, 10, 20, 0);
        AssertPoint(figure.Segments[12].End, 10, 20, 40);
        AssertPoint(figure.LabelAnchor, 10, 20, 40);
    }

    [Fact]
    public void Build_WithoutAWorldSize_LeavesOnlyThePoleWhenItIsEnabled()
    {
        var marker = Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 10,
            Y = 20,
            Z = 30,
            Size = 4,
            Style = "box",
            Pole = new WorldMarkerPole { Enabled = true, BaseZ = 0, TopZ = 40 },
        });

        var withPole = WorldMarkerOverlayGeometry.Build(marker, null);

        AssertSegments(withPole, (10, 20, 0, 10, 20, 40));
        AssertPoint(withPole.HeadAnchor, 10, 20, 30);
        AssertPoint(withPole.LabelAnchor, 10, 20, 40);

        var withoutPole = WorldMarkerOverlayGeometry.Build(Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 10,
            Y = 20,
            Z = 30,
            Size = 4,
            Style = "target",
        }), null);

        Assert.Empty(withoutPole.Segments);
        AssertPoint(withoutPole.HeadAnchor, 10, 20, 30);
        AssertPoint(withoutPole.LabelAnchor, 10, 20, 30);
    }

    [Fact]
    public void Build_IsDeterministicAndLeavesTheMarkerUntouched()
    {
        var marker = Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 10,
            Y = 20,
            Z = 30,
            Size = 4,
            Style = "target",
            Pole = new WorldMarkerPole { Enabled = true, BaseZ = 0, TopZ = 40 },
        });

        var first = WorldMarkerOverlayGeometry.Build(marker);
        var second = WorldMarkerOverlayGeometry.Build(marker);

        Assert.Equal(first.Segments.Count, second.Segments.Count);
        for (var index = 0; index < first.Segments.Count; index++)
        {
            Assert.Equal(first.Segments[index].Start.X, second.Segments[index].Start.X);
            Assert.Equal(first.Segments[index].Start.Y, second.Segments[index].Start.Y);
            Assert.Equal(first.Segments[index].Start.Z, second.Segments[index].Start.Z);
            Assert.Equal(first.Segments[index].End.X, second.Segments[index].End.X);
            Assert.Equal(first.Segments[index].End.Y, second.Segments[index].End.Y);
            Assert.Equal(first.Segments[index].End.Z, second.Segments[index].End.Z);
        }

        Assert.Equal(WorldMarkerStyles.Target, marker.Style);
        Assert.Equal(4, marker.Size);
        Assert.Equal(10, marker.X);
        Assert.Equal(20, marker.Y);
        Assert.Equal(30, marker.Z);
        Assert.True(marker.PoleEnabled);
        Assert.Equal(0, marker.PoleBaseZ);
        Assert.Equal(40, marker.PoleTopZ);
    }

    [Fact]
    public void Build_RejectsANullMarkerAndAnUnsupportedStyle()
    {
        Assert.Throws<ArgumentNullException>(() => WorldMarkerOverlayGeometry.Build(null));

        var unsupported = Normalize(new WorldMarkerSpec { Name = "M", X = 10, Y = 20, Z = 30, Style = "target" });
        unsupported.Style = "sphere";

        var sized = Assert.Throws<ArgumentException>(() => WorldMarkerOverlayGeometry.Build(unsupported));
        Assert.Contains("sphere", sized.Message);
        Assert.Throws<ArgumentException>(() => WorldMarkerOverlayGeometry.Build(unsupported, null));
    }

    [Fact]
    public void Build_RejectsAWorldSizeOutsideThePolicyBounds()
    {
        var marker = Normalize(new WorldMarkerSpec { Name = "M", X = 10, Y = 20, Z = 30, Style = "box" });

        foreach (var size in new[] { WorldMarkerInputPolicy.MinSize / 10, WorldMarkerInputPolicy.MaxSize * 10, double.NaN, double.PositiveInfinity })
        {
            var error = Assert.Throws<ArgumentException>(() => WorldMarkerOverlayGeometry.Build(marker, size));
            Assert.Contains("size must be between", error.Message);
        }
    }

    [Fact]
    public void Build_RejectsAnOverrideWorldSizeWhoseEnvelopeExceedsTheCoordinateLimit()
    {
        var marker = Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 1e12 - 1,
            Size = 1,
            Style = "box",
        });

        var error = Assert.Throws<ArgumentException>(() => WorldMarkerOverlayGeometry.Build(marker, 1e9));

        Assert.Contains("derived x max", error.Message);
        Assert.Equal(1, marker.Size);
    }

    [Fact]
    public void Build_AcceptsALargerOverrideWorldSizeInsideTheCoordinateLimit()
    {
        var marker = Normalize(new WorldMarkerSpec
        {
            Name = "M",
            X = 10,
            Y = 20,
            Z = 30,
            Size = 4,
            Style = "box",
        });

        var figure = WorldMarkerOverlayGeometry.Build(marker, 8);

        Assert.Equal(12, figure.Segments.Count);
        AssertSegment(figure.Segments[0], (6, 16, 26, 14, 16, 26));
        AssertPoint(figure.LabelAnchor, 10, 20, 34);
        Assert.Equal(4, marker.Size);
    }

    private static WorldMarkerPlanItem Normalize(WorldMarkerSpec marker)
    {
        return WorldMarkerInputPolicy.NormalizeMarker(marker);
    }

    private static double Radius(WorldMarkerPoint point)
    {
        var dx = point.X - 10;
        var dy = point.Y - 20;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static void AssertSegments(
        WorldMarkerOverlayFigure figure,
        params (double StartX, double StartY, double StartZ, double EndX, double EndY, double EndZ)[] expected)
    {
        Assert.Equal(expected.Length, figure.Segments.Count);
        for (var index = 0; index < expected.Length; index++)
            AssertSegment(figure.Segments[index], expected[index]);
    }

    private static void AssertSegment(
        WorldMarkerSegment segment,
        (double StartX, double StartY, double StartZ, double EndX, double EndY, double EndZ) expected)
    {
        AssertPoint(segment.Start, expected.StartX, expected.StartY, expected.StartZ);
        AssertPoint(segment.End, expected.EndX, expected.EndY, expected.EndZ);
    }

    private static void AssertPoint(WorldMarkerPoint point, double x, double y, double z)
    {
        Assert.Equal(x, point.X, 12);
        Assert.Equal(y, point.Y, 12);
        Assert.Equal(z, point.Z, 12);
    }

    private static void AssertPoint(WorldMarkerPoint point, WorldMarkerPoint expected)
    {
        AssertPoint(point, expected.X, expected.Y, expected.Z);
    }
}
