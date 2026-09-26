using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksViewpointCameraTools : NavisworksToolBase
{
    public NavisworksViewpointCameraTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [ToolCapabilities(ToolEffects.View | ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    [Description("Previews and optionally sets the active Navisworks camera from exact document-coordinate values without scanning the model or changing selection, visibility, or section clipping. Supply exactly one of target or direction. Optional orthographic zoomTo uses a bounded point cube or explicit bounding box. Optional saveName stores the resulting current viewpoint through the existing Saved Viewpoints workflow.")]
    public Task<ViewpointSetCameraResponse> ViewpointSetCamera(
        [Description("Required exact camera position in current document units and document-global coordinates.")] Point3Info position,
        [Description("Required camera up vector. It must be finite, non-zero, and non-parallel to the view direction.")] Point3Info up,
        [Description("Required projection: perspective, orthographic, or ortho.")] string projection,
        [Description("Exact look-at point in document coordinates. Supply exactly one of target or direction.")] Point3Info target = null,
        [Description("Camera direction vector. Supply exactly one of direction or target; its magnitude becomes focal distance.")] Point3Info direction = null,
        [Description("Optional camera HeightField. For perspective this is the vertical field-of-view angle in radians (0 < value < pi); for orthographic it is the vertical extent in document units. When supplied, it takes precedence over zoomTo framing.")] double? heightField = null,
        [Description("Optional orthographic framing target. Supply exactly one of point plus positive pointHalfSize, or boundingBox with strict min/max. The point or box center must equal target. All values use document units. Perspective calls must use heightField instead.")] ViewpointCameraZoomTo zoomTo = null,
        [Description("Optional Saved Viewpoint name. Existing-name conflicts follow create_viewpoint semantics and reject apply before the camera changes.")] string saveName = "",
        [Description("Optional Saved Viewpoints folder path. Missing folders are created only with apply=true. Requires saveName.")] string saveFolderPath = "",
        [Description("False validates and previews the exact camera plan; true applies it. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ViewpointSetCameraAsync(
            new ViewpointSetCameraRequest
            {
                Position = position,
                Target = target,
                Direction = direction,
                Up = up,
                Projection = projection,
                HeightField = heightField,
                ZoomTo = zoomTo,
                SaveName = saveName,
                SaveFolderPath = saveFolderPath,
                Apply = apply,
            },
            cancellationToken,
            CreateTarget(instanceId, navisworksVersion));
    }
}
