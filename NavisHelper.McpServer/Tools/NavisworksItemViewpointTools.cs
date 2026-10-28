using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksItemViewpointTools : NavisworksToolBase
{
    public NavisworksItemViewpointTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Saves one viewpoint per item of the selection or match handles, named after the item, with an optional section, preset camera fitted to the item plus context, and mark. section=plane cuts planeOffsetMm above each item top. Restores selection, clipping and camera afterwards. Dry-run by default.")]
    [ToolCapabilities(ToolEffects.View | ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ItemViewpointsResponse> ViewpointsPerItem(
        [Description("Optional match handles; omitted uses current selection. Items are deduplicated without expanding descendants.")] List<string> matchHandles = null,
        [Description("Saved viewpoint folder; empty means root.")] string folderPath = "",
        [Description("Must contain {name}; optional {index}. Repeated names receive numbered suffixes.")] string nameTemplate = "{name}",
        [Description("Section mode: none, plane or box.")] string section = "none",
        [Description("Plane height above the item top, in millimetres.")] double planeOffsetMm = 100,
        [Description("Section box padding in millimetres.")] double boxOffsetMm = 1000,
        [Description("Camera preset: iso, top, north, south, east or west.")] string camera = "top",
        [Description("Camera fitting padding in millimetres; surrounding items remain visible.")] double contextMm = 2000,
        [Description("Markup style; empty string saves without a mark.")] string markStyle = "target",
        [Description("Optional three RGB values from 0 to 1; default red.")] List<double> ellipseColor = null,
        [Description("Mark thickness, from 1 to 20.")] int thickness = 3,
        [Description("Update existing viewpoints with matching names.")] bool overwrite = false,
        [Description("Maximum items, clamped to 1-1000.")] int maxItems = 500,
        [Description("False previews names and conflicts; true saves the viewpoints.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version, when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default) =>
        _hostBridgeClient.ViewpointsPerItemAsync(new ItemViewpointsRequest
        {
            MatchHandles = matchHandles, FolderPath = folderPath, NameTemplate = nameTemplate,
            Section = section, PlaneOffsetMm = planeOffsetMm, BoxOffsetMm = boxOffsetMm,
            Camera = camera, ContextMm = contextMm, MarkStyle = markStyle, EllipseColor = ellipseColor,
            Thickness = thickness, Overwrite = overwrite, MaxItems = maxItems, Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
}
