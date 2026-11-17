using System.Xml.Linq;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #510 (Port clash run, 2026-11-17): saved_viewpoints_export wrote only the tree (csv/json/md), so the
// views themselves could not be moved to another document. format=xml writes Navisworks Saved
// Viewpoints XML (exchange / viewpoints / viewfolder / view) that saved_viewpoints_import, and the
// camera parser it uses, read back. The plugin cannot compile in L0, so the export wiring is pinned by
// source; the writer is tested by round trip through SavedViewpointCameraXmlParser.
public sealed class SavedViewpointsXmlWriterTests
{
    [Fact]
    public void FoldersAndCamerasSurviveARoundTrip()
    {
        var nodes = new List<SavedViewpointsXmlNode>
        {
            Folder("Zone A",
                View("V1", FullCamera()),
                Folder("Sub", View("V2", SimpleCamera(5)))),
            View("V3", SimpleCamera(7)),
        };

        XDocument document = SavedViewpointsXmlWriter.Write(nodes, "m");

        Assert.Equal("exchange", document.Root!.Name.LocalName);
        Assert.Equal("m", (string?)document.Root.Attribute("units"));
        var viewpoints = Assert.Single(document.Root.Elements("viewpoints"));
        var zone = Assert.Single(viewpoints.Elements("viewfolder"));
        Assert.Equal("Zone A", (string?)zone.Attribute("name"));
        Assert.Equal(new[] { "view", "viewfolder" }, zone.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("Sub", (string?)zone.Element("viewfolder")!.Attribute("name"));
        Assert.Equal("V2", (string?)zone.Element("viewfolder")!.Element("view")!.Attribute("name"));
        Assert.Equal("V3", (string?)viewpoints.Element("view")!.Attribute("name"));

        var parsed = SavedViewpointCameraXmlParser.Parse(zone.Element("view")!);
        var expected = FullCamera();
        Assert.True(parsed.Success);
        Assert.Equal((expected.PositionX, expected.PositionY, expected.PositionZ), (parsed.PositionX, parsed.PositionY, parsed.PositionZ));
        Assert.Equal((expected.RotationA, expected.RotationB, expected.RotationC, expected.RotationD), (parsed.RotationA, parsed.RotationB, parsed.RotationC, parsed.RotationD));
        Assert.Equal(SavedViewpointProjectionKind.Orthographic, parsed.Projection);
        Assert.Equal(expected.NearPlaneDistance, parsed.NearPlaneDistance);
        Assert.Equal(expected.FarPlaneDistance, parsed.FarPlaneDistance);
        Assert.Equal(expected.AspectRatio, parsed.AspectRatio);
        Assert.Equal(expected.HeightField, parsed.HeightField);
        Assert.Equal(expected.FocalDistance, parsed.FocalDistance);
        Assert.Equal(expected.LinearSpeed, parsed.LinearSpeed);
        Assert.Equal(expected.AngularSpeed, parsed.AngularSpeed);
        Assert.True(parsed.HasWorldUpVector);
        Assert.Equal((0.0, 0.0, 1.0), (parsed.WorldUpX, parsed.WorldUpY, parsed.WorldUpZ));
        Assert.Equal(SavedViewpointRenderStyleKind.HiddenLine, parsed.RenderStyle);
        Assert.Equal(SavedViewpointLightingKind.FullLights, parsed.Lighting);
        Assert.Equal(SavedViewpointCameraModeKind.ThirdPerson, parsed.ViewerCameraMode);

        var simple = SavedViewpointCameraXmlParser.Parse(viewpoints.Element("view")!);
        Assert.True(simple.Success);
        Assert.Equal(7, simple.PositionX);
        Assert.Equal(SavedViewpointProjectionKind.Perspective, simple.Projection);
        Assert.Null(simple.NearPlaneDistance);
        Assert.False(simple.HasWorldUpVector);
    }

    [Fact]
    public void ExportAcceptsXmlAndUsesTheWriter()
    {
        var export = Read("NavisHelper", "Agent", "Services", "DocumentCommandService.Viewpoints.Export.cs");
        Assert.Contains("SavedViewpointsXmlWriter.Write(", export, StringComparison.Ordinal);
        Assert.Contains("\"Unsupported export format. Use csv, json, md, or xml.\"", export, StringComparison.Ordinal);
    }

    private static SavedViewpointCameraParseResult FullCamera() => new()
    {
        Success = true,
        PositionX = 674.6801234567891, PositionY = -58.279, PositionZ = 39.581,
        RotationA = 0.1, RotationB = 0.2, RotationC = 0.3, RotationD = 0.9273618495495703,
        Projection = SavedViewpointProjectionKind.Orthographic,
        NearPlaneDistance = 0.25, FarPlaneDistance = 1500, AspectRatio = 1.7777777777777777, HeightField = 12.5,
        FocalDistance = 33.3, LinearSpeed = 2, AngularSpeed = 0.7853981633974483,
        HasWorldUpVector = true, WorldUpX = 0, WorldUpY = 0, WorldUpZ = 1,
        RenderStyle = SavedViewpointRenderStyleKind.HiddenLine,
        Lighting = SavedViewpointLightingKind.FullLights,
        ViewerCameraMode = SavedViewpointCameraModeKind.ThirdPerson,
    };

    private static SavedViewpointCameraParseResult SimpleCamera(double x) => new()
    {
        Success = true,
        PositionX = x, RotationA = 1,
        Projection = SavedViewpointProjectionKind.Perspective,
    };

    private static SavedViewpointsXmlNode Folder(string name, params SavedViewpointsXmlNode[] children) =>
        new() { Name = name, IsFolder = true, Children = children.ToList() };

    private static SavedViewpointsXmlNode View(string name, SavedViewpointCameraParseResult camera) =>
        new() { Name = name, Camera = camera };

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
