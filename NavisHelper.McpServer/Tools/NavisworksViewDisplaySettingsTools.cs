using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksViewDisplaySettingsTools : NavisworksToolBase
{
    public NavisworksViewDisplaySettingsTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    [Description("Reads current Navisworks lighting/render style and horizon support without changing the view. Full background mode/colors cannot be read through the public SDK; backgroundReadbackAvailable is false, never a cached guess.")]
    public Task<ViewDisplaySettingsState> GetViewDisplaySettings(
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version, when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default) =>
        _hostBridgeClient.GetViewDisplaySettingsAsync(cancellationToken, CreateTarget(instanceId, navisworksVersion));

    [McpServerTool]
    [ToolCapabilities(ToolEffects.View | ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    [Description("Previews or applies scene background, lighting and render style together or independently. Omitted settings are preserved. No camera, selection, geometry or file save changes. Background setter completion is not readback; on native failure background may be partially changed and cannot be restored automatically. Inspect the view before retrying.")]
    public Task<ViewDisplaySettingsResponse> SetViewDisplaySettings(
        [Description("Optional background: mode plain with one #RRGGBB color; graduated with colors [top,bottom]; horizon with [skyTop,skyBottom,groundTop,groundBottom]. Horizon requires perspective 3D. Supply exactly the required number of colors; no alpha.")] ViewBackgroundSettings background = null,
        [Description("Optional lighting: none, scene_lights, headlight, full_lights.")] string lighting = null,
        [Description("Optional render style: full_render, preview, shaded, wireframe, hidden_line.")] string renderStyle = null,
        [Description("False validates/previews only; true applies supplied settings. Default false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version, when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default) =>
        _hostBridgeClient.SetViewDisplaySettingsAsync(new ViewDisplaySettingsRequest
        { Background = background, Lighting = lighting, RenderStyle = renderStyle, Apply = apply },
            cancellationToken, CreateTarget(instanceId, navisworksVersion));
}
