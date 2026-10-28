using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #385: a top-down viewpoint per instrument needs the model cut by one plane 100 mm above the
// instrument. MCP could only make a section box, so a plane was emulated with far-away helper items in
// the selection. section_planes sets, reads and clears Navisworks section planes through the same
// View.Get/SetClippingPlanes JSON the box uses: ClipPlaneSet { Planes[{Normal, Distance, Enabled}],
// Linked, Enabled } (key names read from the JSON serialiser in lcodyplugin.dll).
// A plane's normal points at the side it cuts away; Distance = normal . point (calibrated in L3).
public sealed class SectionPlanesTests
{
    private static BoxVector3 V(double x, double y, double z) => new BoxVector3 { X = x, Y = y, Z = z };

    [Theory]
    [InlineData("top", 0, 0, 1)]
    [InlineData("bottom", 0, 0, -1)]
    [InlineData("north", 0, 1, 0)]
    [InlineData("south", 0, -1, 0)]
    [InlineData("east", 1, 0, 0)]
    [InlineData("west", -1, 0, 0)]
    [InlineData("TOP", 0, 0, 1)]
    public void DirectionsAreWorldAxes(string direction, double x, double y, double z)
    {
        Assert.True(SectionPlaneRules.TryDirectionNormal(direction, out var normal));
        Assert.Equal((x, y, z), (normal.X, normal.Y, normal.Z));
        Assert.Equal(direction.ToLowerInvariant(), SectionPlaneRules.DirectionName(normal));
    }

    [Fact]
    public void UnknownDirectionsAndTiltedNormalsHaveNoName()
    {
        Assert.False(SectionPlaneRules.TryDirectionNormal("front", out _));
        Assert.Null(SectionPlaneRules.DirectionName(V(0, 0.6, 0.8)));
    }

    [Fact]
    public void APlaneAboveTheSelectionTopIsOffsetAlongItsNormal()
    {
        var spec = new SectionPlaneSpec { Index = 1, Direction = "top", FromSelection = "max", OffsetMm = 100 };
        // Model in metres: 100 mm = 0.1 document units.
        var plane = SectionPlaneRules.Resolve(spec, null, V(0, 0, 2), V(1, 1, 3), 0.001, out var error);
        Assert.Null(error);
        Assert.Equal(1, plane.Index);
        Assert.True(plane.Enabled);
        Assert.Equal(1, plane.Normal.Z);
        Assert.Equal(3.1, plane.Distance, 9);
    }

    [Fact]
    public void ABottomPlaneUsesTheLowestCornerAndAnExplicitPoint()
    {
        var below = SectionPlaneRules.Resolve(
            new SectionPlaneSpec { Index = 2, Direction = "bottom", FromSelection = "max", OffsetMm = 0 },
            null, V(0, 0, 2), V(1, 1, 3), 1, out var error);
        Assert.Null(error);
        Assert.Equal(-2, below.Distance, 9); // normal (0,0,-1) . (x, y, 2)

        var custom = SectionPlaneRules.Resolve(
            new SectionPlaneSpec { Index = 3, Normal = V(0, 0, 2), Point = V(5, 5, 7) },
            null, null, null, 1, out error);
        Assert.Null(error);
        Assert.Equal(1, custom.Normal.Z, 9); // normalised
        Assert.Equal(7, custom.Distance, 9);
    }

    [Fact]
    public void AnExistingPlaneMovesAlongItsNormalOrToggles()
    {
        var current = new SectionPlaneState { Index = 1, Enabled = true, Normal = V(0, 0, 1), Distance = 3 };
        var moved = SectionPlaneRules.Resolve(new SectionPlaneSpec { Index = 1, MoveByMm = -500 }, current, null, null, 0.001, out var error);
        Assert.Null(error);
        Assert.Equal(2.5, moved.Distance, 9);
        Assert.True(moved.Enabled);

        var off = SectionPlaneRules.Resolve(new SectionPlaneSpec { Index = 1, Enabled = false }, current, null, null, 0.001, out error);
        Assert.Null(error);
        Assert.False(off.Enabled);
        Assert.Equal(3, off.Distance, 9);
    }

    [Theory]
    [InlineData(0, "top", null, "max", null, "index")]
    [InlineData(7, "top", null, "max", null, "index")]
    [InlineData(1, "top", true, "max", null, "direction or normal")]
    [InlineData(1, "up", null, "max", null, "direction")]
    [InlineData(1, "top", null, "middle", null, "fromSelection")]
    [InlineData(1, "top", null, null, null, "point, fromSelection or moveByMm")]
    [InlineData(1, null, null, null, 100.0, "existing plane")]
    public void BadSpecsAreRefused(int index, string direction, bool? withNormal, string fromSelection, double? moveByMm, string expected)
    {
        var spec = new SectionPlaneSpec
        {
            Index = index,
            Direction = direction,
            Normal = withNormal == true ? V(0, 0, 1) : null,
            FromSelection = fromSelection,
            MoveByMm = moveByMm,
        };
        SectionPlaneRules.Resolve(spec, null, V(0, 0, 0), V(1, 1, 1), 1, out var error);
        Assert.Contains(expected, error, StringComparison.Ordinal);
    }

    [Fact]
    public void FromSelectionNeedsASelectionAndZeroNormalsAreRefused()
    {
        SectionPlaneRules.Resolve(new SectionPlaneSpec { Index = 1, Direction = "top", FromSelection = "max" }, null, null, null, 1, out var error);
        Assert.Contains("selection", error, StringComparison.Ordinal);
        SectionPlaneRules.Resolve(new SectionPlaneSpec { Index = 1, Normal = V(0, 0, 0), Point = V(0, 0, 0) }, null, null, null, 1, out error);
        Assert.Contains("normal", error, StringComparison.Ordinal);
        Assert.Equal(6, SectionPlaneRules.MaxPlanes);
    }

    [Fact]
    public void PlaneModeJsonIsReadPlaneByPlane()
    {
        var parsed = SectionPlaneClippingJson.Read(
            "{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"Planes\":[" +
            "{\"Type\":\"ClipPlane\",\"Version\":1,\"Normal\":[0,0,1],\"Distance\":12.5,\"Enabled\":true}," +
            "{\"Type\":\"ClipPlane\",\"Version\":1,\"Normal\":[1,0,0],\"Distance\":-3,\"Enabled\":false}]," +
            "\"Linked\":true,\"Enabled\":true}");
        Assert.Equal("planes", parsed.Mode);
        Assert.True(parsed.Enabled);
        Assert.True(parsed.Linked);
        Assert.Equal(2, parsed.Planes.Count);
        Assert.Equal(1, parsed.Planes[0].Index);
        Assert.Equal(12.5, parsed.Planes[0].Distance);
        Assert.Equal(1, parsed.Planes[0].Normal.Z);
        Assert.False(parsed.Planes[1].Enabled);
        Assert.Equal(2, parsed.Planes[1].Index);
    }

    [Theory]
    [InlineData("{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"OrientedBox\":{\"Type\":\"OrientedBox3D\",\"Version\":1,\"Box\":[[0,0,0],[1,1,1]],\"Rotation\":[0,0,0]},\"Enabled\":true}", "box", true)]
    [InlineData("{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"Planes\":[],\"Enabled\":false}", "off", false)]
    [InlineData("", "off", false)]
    public void BoxAndDisabledSectioningAreReported(string json, string mode, bool enabled)
    {
        var parsed = SectionPlaneClippingJson.Read(json);
        Assert.Equal(mode, parsed.Mode);
        Assert.Equal(enabled, parsed.Enabled);
    }

    [Fact]
    public void WritingKeepsOtherPlanesAndSwitchesToPlaneMode()
    {
        var current = "{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"Planes\":[" +
            "{\"Type\":\"ClipPlane\",\"Version\":1,\"Normal\":[0,0,1],\"Distance\":1,\"Enabled\":false}," +
            "{\"Type\":\"ClipPlane\",\"Version\":1,\"Normal\":[1,0,0],\"Distance\":4,\"Enabled\":true}]," +
            "\"OrientedBox\":{\"Type\":\"OrientedBox3D\",\"Version\":1,\"Box\":[[0,0,0],[1,1,1]],\"Rotation\":[0,0,0]},\"Linked\":false,\"Enabled\":true}";
        var json = SectionPlaneClippingJson.Write(current,
            new[] { new SectionPlaneState { Index = 1, Enabled = true, Normal = V(0, 0, 1), Distance = 3.1 } },
            linked: false, enabled: true);
        var root = JsonNode.Parse(json)!;
        Assert.Equal("ClipPlaneSet", root["Type"]!.GetValue<string>());
        Assert.Equal(1, root["Version"]!.GetValue<int>());
        Assert.Null(root["OrientedBox"]);
        var planes = root["Planes"]!.AsArray();
        Assert.Equal(SectionPlaneRules.MaxPlanes, planes.Count);
        Assert.Equal(3.1, planes[0]!["Distance"]!.GetValue<double>(), 9);
        Assert.True(planes[0]!["Enabled"]!.GetValue<bool>());
        Assert.Equal("ClipPlane", planes[0]!["Type"]!.GetValue<string>());
        Assert.Equal(4, planes[1]!["Distance"]!.GetValue<double>(), 9);
        Assert.True(planes[1]!["Enabled"]!.GetValue<bool>());
        Assert.False(planes[5]!["Enabled"]!.GetValue<bool>());

        var back = SectionPlaneClippingJson.Read(json);
        Assert.Equal("planes", back.Mode);
        Assert.Equal(3.1, back.Planes[0].Distance, 9);
        Assert.Equal("off", SectionPlaneClippingJson.Read(SectionPlaneClippingJson.Write(json, Array.Empty<SectionPlaneState>(), false, enabled: false)).Mode);
    }

    // Review of #385: clear must switch sectioning off without discarding a section box, and JSON the
    // reader cannot understand must not look like "sectioning off" (or be overwritten as empty).
    [Fact]
    public void ClearingOnlySwitchesSectioningOffAndKeepsTheBox()
    {
        var box = "{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"OrientedBox\":{\"Type\":\"OrientedBox3D\",\"Version\":1,\"Box\":[[0,0,0],[1,1,1]],\"Rotation\":[0,0,0]},\"Enabled\":true}";
        var root = JsonNode.Parse(SectionPlaneClippingJson.Disable(box))!;
        Assert.False(root["Enabled"]!.GetValue<bool>());
        Assert.Equal(1, root["OrientedBox"]!["Box"]![1]![2]!.GetValue<double>());
        Assert.Null(root["Planes"]);
    }

    [Theory]
    [InlineData("{\"Type\":\"ClipPlaneSet\",\"Version\":1,\"Planes\":[{\"Normal\":[0,0,1],\"Enabled\":true}],\"Enabled\":true}")]
    [InlineData("{not json")]
    public void UnreadableClippingIsReportedAsSuch(string json)
    {
        var parsed = SectionPlaneClippingJson.Read(json);
        Assert.False(parsed.Readable);
        Assert.Equal("unreadable", parsed.Mode);
        Assert.True(SectionPlaneClippingJson.Read("").Readable);
    }

    [Fact]
    public void TheToolReadsByDefaultAndWritesOnlyOnApply()
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == "SectionPlanes");
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
        Assert.Equal("section_planes", tool.ProtocolTool.Name);
        Assert.Equal(ToolEffects.View, method.GetCustomAttribute<ToolCapabilitiesAttribute>()!.Effects);
        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!;
        Assert.Equal("get", schema["properties"]!["operation"]!["default"]!.GetValue<string>());
        Assert.False(schema["properties"]!["apply"]!["default"]!.GetValue<bool>());
        Assert.Equal("section_planes", HostCommandNames.SectionPlanes);
        Assert.True(HostCommandEffects.IsKnown(HostCommandNames.SectionPlanes));
        Assert.Empty(new SectionPlanesRequest().Planes);
        Assert.Empty(new SectionPlanesResponse().Planes);
    }

    [Fact]
    public void ThePluginWritesThroughTheViewAndReadsBack()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "SectionPlanesService.cs"));
        Assert.Contains("GetClippingPlanes()", service, StringComparison.Ordinal);
        Assert.Contains("TrySetClippingPlanes(", service, StringComparison.Ordinal);
        Assert.Contains("SectionPlaneClippingJson.Write(", service, StringComparison.Ordinal);
        Assert.Contains("SectionPlaneClippingJson.Read(", service, StringComparison.Ordinal);
        Assert.Contains("SectionPlaneRules.Resolve(", service, StringComparison.Ordinal);
        Assert.Contains("ReadbackMatches", service, StringComparison.Ordinal);
        Assert.Contains("SectionPlaneClippingJson.Disable(", service, StringComparison.Ordinal);
        Assert.Contains(".Readable", service, StringComparison.Ordinal);
    }
}
