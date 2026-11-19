using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #388 slice 2: move, grow (per face, along the box's own axes) and rotate (about world Z through the centre)
// the current section box. Offsets are millimetres; the caller passes the millimetre-to-document-unit factor.
public sealed class SectionBoxEditTests
{
    private static BoxVector3 V(double x, double y, double z) => new BoxVector3 { X = x, Y = y, Z = z };

    private static SectionBoxGeometry Box() => SectionBoxWriteRules.FromMinMax(V(0, 0, 0), V(10, 20, 4), "meters");

    private static void Near(BoxVector3 expected, BoxVector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 9);
        Assert.Equal(expected.Y, actual.Y, 9);
        Assert.Equal(expected.Z, actual.Z, 9);
    }

    [Fact]
    public void MoveShiftsTheCentreInDocumentUnits()
    {
        var moved = SectionBoxEditRules.Move(Box(), 1000, -500, 250, 0.001);
        Near(V(6, 9.5, 2.25), moved.Center);
        Near(V(5, 10, 2), moved.HalfExtents);
        Near(V(5, 10, 2), Box().HalfExtents);
    }

    [Fact]
    public void GrowMovesEachFaceAlongTheBoxAxes()
    {
        var grown = SectionBoxEditRules.Grow(Box(), new SectionBoxFaceOffsets { Top = 2000, West = 1000 }, 0.001);
        Near(V(4.5, 10, 3), grown.Center);
        Near(V(5.5, 10, 3), grown.HalfExtents);
        var shrunk = SectionBoxEditRules.Grow(Box(), new SectionBoxFaceOffsets { North = -4000, South = -4000 }, 0.001);
        Near(V(5, 10, 2), shrunk.Center);
        Near(V(5, 6, 2), shrunk.HalfExtents);
    }

    [Fact]
    public void GrowFollowsARotatedBox()
    {
        var rotated = SectionBoxEditRules.RotateZ(Box(), 90);
        var grown = SectionBoxEditRules.Grow(rotated, new SectionBoxFaceOffsets { East = 2000 }, 0.001);
        // After 90 degrees the box's own X axis points along world +Y.
        Near(V(5, 11, 2), grown.Center);
        Near(V(6, 10, 2), grown.HalfExtents);
    }

    [Fact]
    public void GrowRefusesToCollapseTheBox()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            SectionBoxEditRules.Grow(Box(), new SectionBoxFaceOffsets { Top = -2000, Bottom = -2000 }, 0.001));
        Assert.Equal("grow would leave the box with no volume along its z axis.", error.Message);
    }

    [Fact]
    public void RotateTurnsTheAxesAboutWorldZThroughTheCentre()
    {
        var rotated = SectionBoxEditRules.RotateZ(Box(), 90);
        SectionBoxGeometryRules.Validate(rotated);
        Near(Box().Center, rotated.Center);
        Near(V(0, 1, 0), rotated.Axes[0]);
        Near(V(-1, 0, 0), rotated.Axes[1]);
        Near(V(0, 0, 1), rotated.Axes[2]);
        var back = SectionBoxEditRules.RotateZ(rotated, -90);
        Near(V(1, 0, 0), back.Axes[0]);
    }

    [Theory]
    [InlineData("spin", "operation must be set, clear, move, grow or rotate.")]
    [InlineData("move", "move needs offsetMm.")]
    [InlineData("grow", "grow needs growMm with at least one face.")]
    [InlineData("rotate", "rotate needs a finite rotateDegrees.")]
    public void EditRequestsAreRefusedWithAReason(string operation, string message)
    {
        Assert.Equal(message, SectionBoxWriteRules.Describe(new SectionBoxRequest { Operation = operation }));
    }

    [Fact]
    public void ValidEditRequestsPassAndTakeNoBox()
    {
        Assert.Null(SectionBoxWriteRules.Describe(new SectionBoxRequest { Operation = "move", OffsetMm = V(1, 0, 0) }));
        Assert.Null(SectionBoxWriteRules.Describe(new SectionBoxRequest { Operation = "GROW", GrowMm = new SectionBoxFaceOffsets { Top = 1 } }));
        Assert.Null(SectionBoxWriteRules.Describe(new SectionBoxRequest { Operation = "rotate", RotateDegrees = 15 }));
        Assert.Equal("move, grow and rotate take no box, min or max.",
            SectionBoxWriteRules.Describe(new SectionBoxRequest { Operation = "move", OffsetMm = V(1, 0, 0), Min = V(0, 0, 0) }));
        Assert.Equal("rotate needs a finite rotateDegrees.",
            SectionBoxWriteRules.Describe(new SectionBoxRequest { Operation = "rotate", RotateDegrees = double.NaN }));
    }

    [Fact]
    public void TheToolAndThePluginCarryTheEdits()
    {
        var parameters = typeof(NavisworksSectionBoxWriteTools).GetMethod("SectionBox")!.GetParameters();
        foreach (var name in new[] { "offsetMm", "growMm", "rotateDegrees" })
            Assert.Contains(parameters, item => item.Name == name);
        var service = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "SectionBoxService.cs"));
        foreach (var expected in new[] { "SectionBoxEditRules.Move(", "SectionBoxEditRules.Grow(", "SectionBoxEditRules.RotateZ(" })
            Assert.Contains(expected, service, StringComparison.Ordinal);
    }
}
