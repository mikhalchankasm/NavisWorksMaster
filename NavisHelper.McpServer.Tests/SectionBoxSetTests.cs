using System.Reflection;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #388 slice 1: a section box comes only from the selection today. section_box sets an explicit box, either
// the canonical shape get_current_section_box returns (so a captured box round-trips) or an axis-aligned
// min/max, and clears it. The clipping JSON stores the box as min/max around the centre plus Euler angles,
// which the plugin gets from new Rotation3D(x, y, z, w).ToEulerAngles().
public sealed class SectionBoxSetTests
{
    private static BoxVector3 V(double x, double y, double z) => new BoxVector3 { X = x, Y = y, Z = z };

    public static IEnumerable<object[]> Rotations() => new[]
    {
        new object[] { 0.0, 0.0, 0.0, 1.0 },
        new object[] { 0.0, 0.0, 0.3826834324, 0.9238795325 },
        new object[] { 0.1, -0.7, 0.2, 0.3 },
        new object[] { 1.0, 0.0, 0.0, 0.0 },
        new object[] { 0.0, 0.7071067812, 0.7071067812, 0.0 },
    };

    [Theory]
    [MemberData(nameof(Rotations))]
    public void AxesRoundTripThroughTheQuaternion(double x, double y, double z, double w)
    {
        var axes = SectionBoxGeometryRules.AxesFromQuaternion(x, y, z, w);
        var q = SectionBoxWriteRules.QuaternionFromAxes(axes);
        Assert.Equal(4, q.Length);
        Assert.Equal(1.0, Math.Sqrt(q.Sum(value => value * value)), 9);
        var back = SectionBoxGeometryRules.AxesFromQuaternion(q[0], q[1], q[2], q[3]);
        for (var axis = 0; axis < 3; axis++)
        {
            Assert.Equal(axes[axis].X, back[axis].X, 9);
            Assert.Equal(axes[axis].Y, back[axis].Y, 9);
            Assert.Equal(axes[axis].Z, back[axis].Z, 9);
        }
    }

    [Fact]
    public void MinMaxBecomesAnAxisAlignedCanonicalBox()
    {
        var box = SectionBoxWriteRules.FromMinMax(V(-10, 0, 5), V(30, 8, 9), "meters");
        SectionBoxGeometryRules.Validate(box);
        Assert.Equal("meters", box.DocumentUnits);
        Assert.Equal(10, box.Center.X);
        Assert.Equal(4, box.Center.Y);
        Assert.Equal(7, box.Center.Z);
        Assert.Equal(20, box.HalfExtents.X);
        Assert.Equal(4, box.HalfExtents.Y);
        Assert.Equal(2, box.HalfExtents.Z);
        Assert.Equal(1, box.Axes[0].X);
        Assert.Equal(1, box.Axes[1].Y);
        Assert.Equal(1, box.Axes[2].Z);
        var error = Assert.Throws<ArgumentException>(() => SectionBoxWriteRules.FromMinMax(V(0, 0, 0), V(1, 0, 1), "meters"));
        Assert.Equal("max must be greater than min on every axis.", error.Message);
    }

    [Fact]
    public void TheClippingJsonParsesBackToTheSameBox()
    {
        var box = SectionBoxWriteRules.FromMinMax(V(-10, 0, 5), V(30, 8, 9), "meters");
        var json = SectionBoxWriteRules.BuildClippingJson(box, 0.1, -0.2, 0.3);
        var parsed = SectionBoxClippingParser.Parse(json);
        Assert.Equal(-10, parsed.Minimum.X, 12);
        Assert.Equal(0, parsed.Minimum.Y, 12);
        Assert.Equal(5, parsed.Minimum.Z, 12);
        Assert.Equal(30, parsed.Maximum.X, 12);
        Assert.Equal(8, parsed.Maximum.Y, 12);
        Assert.Equal(9, parsed.Maximum.Z, 12);
        Assert.Equal(0.1, parsed.EulerRadians.X, 12);
        Assert.Equal(-0.2, parsed.EulerRadians.Y, 12);
        Assert.Equal(0.3, parsed.EulerRadians.Z, 12);
    }

    [Theory]
    [InlineData("spin", false, false, false, "operation must be set, clear, move, grow or rotate.")]
    [InlineData("set", false, false, false, "set needs either box or both min and max.")]
    [InlineData("set", true, true, true, "set needs either box or both min and max.")]
    [InlineData("set", false, true, false, "set needs either box or both min and max.")]
    [InlineData("clear", true, false, false, "clear takes no box, min or max.")]
    public void BadRequestsAreRefusedWithAReason(string operation, bool withBox, bool withMin, bool withMax, string message)
    {
        var request = new SectionBoxRequest
        {
            Operation = operation,
            Box = withBox ? SectionBoxWriteRules.FromMinMax(V(0, 0, 0), V(1, 1, 1), "meters") : null,
            Min = withMin ? V(0, 0, 0) : null,
            Max = withMax ? V(1, 1, 1) : null,
        };
        Assert.Equal(message, SectionBoxWriteRules.Describe(request));
        Assert.Null(SectionBoxWriteRules.Describe(new SectionBoxRequest { Operation = "SET", Min = V(0, 0, 0), Max = V(1, 1, 1) }));
        Assert.Null(SectionBoxWriteRules.Describe(new SectionBoxRequest { Operation = "clear" }));
    }

    [Fact]
    public void TheToolIsADryRunByDefaultInTheSectionsSet()
    {
        Assert.Equal("section_box", HostCommandNames.SectionBox);
        var type = typeof(NavisworksSectionPlanesTools).Assembly.GetTypes()
            .Single(item => item.GetMethods().Any(method => method.Name == "SectionBox" && method.GetCustomAttributes().Any(a => a.GetType().Name == "McpServerToolAttribute")));
        var parameters = type.GetMethod("SectionBox")!.GetParameters();
        Assert.Equal(false, parameters.Single(item => item.Name == "apply").DefaultValue);
        foreach (var name in new[] { "operation", "box", "min", "max" })
            Assert.Contains(parameters, item => item.Name == name);
        Assert.NotNull(typeof(SectionBoxResponse).GetProperty("ReadbackMatches"));
        Assert.NotNull(typeof(SectionBoxResponse).GetProperty("Box"));
    }

    [Fact]
    public void ThePluginWritesThroughTheContractAndReadsBack()
    {
        var root = Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services");
        var service = File.ReadAllText(Path.Combine(root, "SectionBoxService.cs"));
        foreach (var expected in new[]
        {
            "SectionBoxWriteRules.Describe(", "SectionBoxWriteRules.QuaternionFromAxes(", "SectionBoxWriteRules.BuildClippingJson(",
            ".ToEulerAngles()", "TrySetClippingPlanes(", "SectionBoxClippingParser.Parse(",
        })
            Assert.Contains(expected, service, StringComparison.Ordinal);
    }
}
