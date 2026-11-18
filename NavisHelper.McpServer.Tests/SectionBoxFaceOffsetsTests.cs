using System.Reflection;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #388, first slice: section_box_viewpoint took one boxOffsetMm for all six faces. boxFaceOffsetsMm now sets
// top, bottom, north, south, east and west separately (document axes: east +X, north +Y, top +Z); a face left
// null keeps boxOffsetMm, and SectionBoxFaceOffsetPlanner.Open (-1) opens the face to the model's extent on that
// side plus boxOffsetMm, so nothing on that side is clipped. The camera still fits the closed box, so an open
// side cannot push it away. The plugin cannot compile in L0: the arithmetic lives in the planner (Contracts).
public sealed class SectionBoxFaceOffsetsTests
{
    private static readonly double[] ClusterMin = { 0, 0, 0 };
    private static readonly double[] ClusterMax = { 10, 20, 30 };
    private static readonly double[] ModelMin = { -500, -600, -700 };
    private static readonly double[] ModelMax = { 800, 900, 1000 };

    private static (double[] Min, double[] Max) Plan(SectionBoxFaceOffsets? offsets, double defaultMm = 1000, double unitsPerMm = 0.001)
    {
        var ok = SectionBoxFaceOffsetPlanner.TryPlan(ClusterMin, ClusterMax, offsets, defaultMm, unitsPerMm,
            ModelMin, ModelMax, out var min, out var max, out var error);
        Assert.True(ok, error);
        Assert.Null(error);
        return (min, max);
    }

    [Fact]
    public void WithoutFaceOffsetsEveryFaceTakesTheDefault()
    {
        var (min, max) = Plan(null);
        Assert.Equal(new[] { -1.0, -1.0, -1.0 }, min);
        Assert.Equal(new[] { 11.0, 21.0, 31.0 }, max);
        var (emptyMin, emptyMax) = Plan(new SectionBoxFaceOffsets());
        Assert.Equal(min, emptyMin);
        Assert.Equal(max, emptyMax);
    }

    [Fact]
    public void EachFaceMovesOnItsOwnAxis()
    {
        var (min, max) = Plan(new SectionBoxFaceOffsets { West = 100, East = 200, South = 300, North = 400, Bottom = 500, Top = 600 });
        Assert.Equal(new[] { -0.1, -0.3, -0.5 }, min.Select(v => Math.Round(v, 9)));
        Assert.Equal(new[] { 10.2, 20.4, 30.6 }, max.Select(v => Math.Round(v, 9)));
    }

    [Fact]
    public void TopPlusOneHundredWithTheOtherSidesOpen()
    {
        var open = SectionBoxFaceOffsetPlanner.Open;
        Assert.Equal(-1, open);
        var (min, max) = Plan(new SectionBoxFaceOffsets { Top = 100, Bottom = open, North = open, South = open, East = open, West = open });
        // Open faces reach the model extent plus the default offset (1000 mm = 1 unit).
        Assert.Equal(new[] { -501.0, -601.0, -701.0 }, min);
        Assert.Equal(801.0, max[0]);
        Assert.Equal(901.0, max[1]);
        Assert.Equal(30.1, Math.Round(max[2], 9));
    }

    [Fact]
    public void AnOpenFaceNeverEndsInsideTheCluster()
    {
        // A cluster that sticks out of a stale model extent still keeps its own default margin.
        var ok = SectionBoxFaceOffsetPlanner.TryPlan(new double[] { -900, 0, 0 }, ClusterMax,
            new SectionBoxFaceOffsets { West = SectionBoxFaceOffsetPlanner.Open }, 1000, 0.001,
            ModelMin, ModelMax, out var min, out _, out var error);
        Assert.True(ok, error);
        Assert.Equal(-901.0, min[0]);
    }

    [Theory]
    [InlineData(-2.0)]
    [InlineData(-0.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void OtherNegativeOrNonFiniteValuesAreRefused(double value)
    {
        var ok = SectionBoxFaceOffsetPlanner.TryPlan(ClusterMin, ClusterMax, new SectionBoxFaceOffsets { North = value }, 1000, 0.001,
            ModelMin, ModelMax, out _, out _, out var error);
        Assert.False(ok);
        Assert.Contains("north", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheRequestAndTheToolCarryTheOffsets()
    {
        var request = new SectionBoxViewpointRequest { BoxFaceOffsetsMm = new SectionBoxFaceOffsets { Top = 100 } };
        Assert.Equal(100, request.BoxFaceOffsetsMm.Top);

        var tool = typeof(NavisworksMarkupTools).GetMethod("SectionBoxViewpoint", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(tool);
        var parameter = Assert.Single(tool!.GetParameters(), p => p.Name == "boxFaceOffsetsMm");
        Assert.Equal(typeof(SectionBoxFaceOffsets), parameter.ParameterType);
        Assert.True(parameter.IsOptional);
    }

    [Fact]
    public void ThePluginClipsWithThePlannedBoxButFitsTheCameraToTheClosedOne()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.Root,
            "NavisHelper", "Agent", "Services", "DocumentCommandService.Markup.cs"));
        Assert.Contains("SectionBoxFaceOffsetPlanner.TryPlan(", source, StringComparison.Ordinal);
        Assert.Contains("ViewpointCameraHelper.ApplyPresetViewToBox(document, box, request.Camera)", source, StringComparison.Ordinal);
        Assert.Contains("SectionBoxHelper.SetSectionBox(clipBox)", source, StringComparison.Ordinal);
        Assert.Contains("boxFaceOffsetsMm", File.ReadAllText(Path.Combine(RepositoryPaths.Root, "docs", "MCP_TOOL_CONTRACTS.md")),
            StringComparison.Ordinal);
    }
}
