using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #386: a top-down viewpoint with the model cut above an item needs the clipping to survive the
// top view, but markup_selection with autoTopView=true switched sectioning off. preserveSectioning
// keeps it. section_box_viewpoint always used an ISO camera; camera picks iso, top or an elevation
// fitted to the box, which removes the separate viewpoint_set_camera call.
public sealed class ViewpointCameraPresetTests
{
    [Theory]
    [InlineData("iso", 1, -1, 1)]
    [InlineData("top", 0, 0, 1)]
    [InlineData("north", 0, 1, 0)]
    [InlineData("south", 0, -1, 0)]
    [InlineData("east", 1, 0, 0)]
    [InlineData("west", -1, 0, 0)]
    [InlineData("TOP", 0, 0, 1)]
    [InlineData("", 1, -1, 1)]
    [InlineData(null, 1, -1, 1)]
    public void PresetsPlaceTheCameraOnTheirSideOfTheTarget(string camera, double x, double y, double z)
    {
        Assert.Null(ViewpointCameraRules.Describe(camera));
        var offset = ViewpointCameraRules.Offset(camera);
        Assert.Equal((x, y, z), (offset.X, offset.Y, offset.Z));
    }

    [Theory]
    [InlineData("front")]
    [InlineData("plan")]
    public void UnknownPresetsAreRefused(string camera)
    {
        Assert.Equal("camera must be iso, top, north, south, east or west.", ViewpointCameraRules.Describe(camera));
    }

    [Fact]
    public void OnlyTheTopPresetLooksStraightDown()
    {
        Assert.True(ViewpointCameraRules.IsTopDown("top"));
        Assert.False(ViewpointCameraRules.IsTopDown("iso"));
        Assert.False(ViewpointCameraRules.IsTopDown("north"));
        Assert.Equal("iso", ViewpointCameraRules.Normalize(null));
        Assert.Equal("east", ViewpointCameraRules.Normalize(" East "));
    }

    [Fact]
    public void ToolsExposeCameraAndPreserveSectioning()
    {
        var box = JsonNode.Parse(Tool("SectionBoxViewpoint").ProtocolTool.InputSchema.GetRawText())!;
        Assert.Equal("iso", box["properties"]!["camera"]!["default"]!.GetValue<string>());
        var markup = JsonNode.Parse(Tool("MarkupSelection").ProtocolTool.InputSchema.GetRawText())!;
        Assert.False(markup["properties"]!["preserveSectioning"]!["default"]!.GetValue<bool>());
        Assert.Null(new SectionBoxViewpointRequest().Camera);
        Assert.Null(new MarkupSelectionRequest().PreserveSectioning);
    }

    [Fact]
    public void ThePluginKeepsSectioningOnRequestAndUsesThePresetCamera()
    {
        var markup = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "DocumentCommandService.Markup.cs"));
        Assert.Contains("PreserveSectioning", markup, StringComparison.Ordinal);
        Assert.Contains("ViewpointCameraRules.Describe(", markup, StringComparison.Ordinal);
        Assert.Contains("ApplyPresetViewToBox(", markup, StringComparison.Ordinal);
        var camera = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Core", "ViewpointCameraHelper.cs"));
        Assert.Contains("ViewpointCameraRules.Offset(", camera, StringComparison.Ordinal);
        Assert.Contains("ViewpointCameraRules.IsTopDown(", camera, StringComparison.Ordinal);
    }

    private static McpServerTool Tool(string methodName)
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == methodName);
        return McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
    }
}
