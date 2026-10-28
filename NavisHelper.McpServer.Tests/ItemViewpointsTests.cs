using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #387: one viewpoint per instrument (a few hundred) took about six calls each: select, section,
// camera, markup, save. viewpoints_per_item does it for every item of the selection or of match
// handles in one call: a section plane above the item (#385), a preset camera (#386), a mark on the
// item, and a viewpoint named after the item, then it restores the user's selection, clipping and camera.
public sealed class ItemViewpointsTests
{
    [Fact]
    public void NamesFollowTheTemplateAndRepeatsAreNumbered()
    {
        var names = ItemViewpointRules.PlanNames(new[] { "PT-101", "PT/102", "pt-101", "  ", "PT-101" }, "{name}");
        Assert.Equal(new[] { "PT-101", "PT 102", "pt-101 (2)", "item", "PT-101 (3)" }, names);
        Assert.Equal(new[] { "2 - TI-7" }, ItemViewpointRules.PlanNames(new[] { "TI-7" }, "{index} - {name}", firstIndex: 2));
    }

    [Fact]
    public void ItemNamesAreNotReadAsPlaceholders()
    {
        Assert.Equal(new[] { "Pump {index}" }, ItemViewpointRules.PlanNames(new[] { "Pump {index}" }, "{name}"));
        Assert.Equal(new[] { "3 - A{name}" }, ItemViewpointRules.PlanNames(new[] { "A{name}" }, "{index} - {name}", firstIndex: 3));
    }

    [Fact]
    public void BlankMatchHandlesAreRefusedInsteadOfFallingBackToTheSelection()
    {
        var blank = new ItemViewpointsRequest { MatchHandles = new List<string> { "h1", " " } };
        Assert.Equal("matchHandles must not contain blank handles.", ItemViewpointRules.Describe(blank));
        Assert.Null(ItemViewpointRules.Describe(new ItemViewpointsRequest { MatchHandles = new List<string> { "h1" } }));
        Assert.Null(ItemViewpointRules.Describe(new ItemViewpointsRequest { MatchHandles = null }));
    }

    [Theory]
    [InlineData("plane", "{name}", 100.0, "top", null)]
    [InlineData("box", "{name}", 100.0, "iso", null)]
    [InlineData("none", "{name}", 100.0, "north", null)]
    [InlineData("", "{name}", 100.0, "top", null)]
    [InlineData("sphere", "{name}", 100.0, "top", "section must be none, plane or box.")]
    [InlineData("plane", "View", 100.0, "top", "nameTemplate must contain {name}.")]
    [InlineData("plane", "{name}", -1.0, "top", "planeOffsetMm, boxOffsetMm and contextMm must not be negative.")]
    [InlineData("plane", "{name}", 100.0, "front", "camera must be iso, top, north, south, east or west.")]
    public void RequestsAreValidated(string section, string template, double offset, string camera, string expected)
    {
        var request = new ItemViewpointsRequest { Section = section, NameTemplate = template, PlaneOffsetMm = offset, Camera = camera };
        Assert.Equal(expected, ItemViewpointRules.Describe(request));
    }

    [Theory]
    [InlineData(null, 500)]
    [InlineData(0, 1)]
    [InlineData(40, 40)]
    [InlineData(5000, 1000)]
    public void TheItemCountIsBounded(int? requested, int expected)
    {
        Assert.Equal(expected, ItemViewpointRules.ClampMaxItems(requested));
    }

    [Fact]
    public void ContractsReportEveryItem()
    {
        var response = new ItemViewpointsResponse { ItemCount = 2, CreatedCount = 1 };
        response.Items.Add(new ItemViewpointResult { ItemName = "PT-101", ViewpointName = "PT-101", MarkCount = 1 });
        response.Items.Add(new ItemViewpointResult { ItemName = "PT-102", Error = "no bounding box" });
        Assert.Equal(2, response.Items.Count);
        Assert.Empty(response.Warnings);
        Assert.Empty(new ItemViewpointsRequest().MatchHandles);
        Assert.Equal("viewpoints_per_item", HostCommandNames.ViewpointsPerItem);
        Assert.True(HostCommandEffects.IsKnown(HostCommandNames.ViewpointsPerItem));
        Assert.Equal(new[] { "none", "plane", "box" }, ItemViewpointRules.Sections);
    }

    [Fact]
    public void TheToolIsADryRunFirstViewpointWriter()
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == "ViewpointsPerItem");
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
        Assert.Equal("viewpoints_per_item", tool.ProtocolTool.Name);
        Assert.Equal(ToolEffects.View | ToolEffects.Document, method.GetCustomAttribute<ToolCapabilitiesAttribute>()!.Effects);
        var properties = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!["properties"]!;
        Assert.False(properties["apply"]!["default"]!.GetValue<bool>());
        Assert.Equal("none", properties["section"]!["default"]!.GetValue<string>());
        Assert.Equal("top", properties["camera"]!["default"]!.GetValue<string>());
        Assert.Equal(100, properties["planeOffsetMm"]!["default"]!.GetValue<double>());
        Assert.Equal("target", properties["markStyle"]!["default"]!.GetValue<string>());
        Assert.Equal("{name}", properties["nameTemplate"]!["default"]!.GetValue<string>());
    }

    [Fact]
    public void ThePluginCombinesPlaneCameraMarkupAndSaveThenRestores()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "ItemViewpointsService.cs"));
        Assert.Contains("ItemViewpointRules.Describe(", service, StringComparison.Ordinal);
        Assert.Contains("ItemViewpointRules.PlanNames(", service, StringComparison.Ordinal);
        Assert.Contains("SectionPlaneRules.Resolve(", service, StringComparison.Ordinal);
        Assert.Contains("SectionPlaneClippingJson.Write(", service, StringComparison.Ordinal);
        Assert.Contains("ViewpointCameraHelper.ApplyPresetViewToBox(", service, StringComparison.Ordinal);
        Assert.Contains("SelectionMarkupViewpointService.BuildGeometry(", service, StringComparison.Ordinal);
        Assert.Contains("SaveCurrentViewpointUsingCreateViewpointPath(", service, StringComparison.Ordinal);
        Assert.Contains("GetClippingPlanes()", service, StringComparison.Ordinal);
        Assert.Contains("finally", service, StringComparison.Ordinal);
    }
}
