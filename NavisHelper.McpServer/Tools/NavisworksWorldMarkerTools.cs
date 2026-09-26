using System.Collections.Generic;
using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksWorldMarkerTools : NavisworksToolBase
{
    public NavisworksWorldMarkerTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Plans and optionally stores overlay world markers anchored at exact document coordinates. Markers are a plugin-drawn view overlay, never geometry in the model: they are not saved with the document, are cleared when the document changes, are always drawn on top of the model, respect section clipping, and appear in capture_current_view. The in-memory store holds at most 500 markers. Defaults to dry-run.")]
    [ToolCapabilities(ToolEffects.View | ToolEffects.LocalState, RequiresHost = true, RequiresDocument = true)]
    public Task<WorldMarkerOverlaySetResponse> WorldMarkersSet(
        [Description("Required typed list of markers. name, x, y and z are required; x, y and z are in document units. Optional per marker: style (target, cross, circle, pin, pole, box), size figure in document units, sizePx head size in pixels (5 to 200, default 12), color, alpha (0 to 255), label, pole and group.")] List<WorldMarkerOverlaySpec> markers,
        [Description("upsert keeps untouched markers and replaces same-name ones; replace_all swaps the whole store. Default is upsert.")] string mode = "upsert",
        [Description("False validates and previews the marker plan; true stores it in the overlay. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.WorldMarkersSetAsync(
            new WorldMarkerOverlaySetRequest
            {
                Markers = markers ?? new List<WorldMarkerOverlaySpec>(),
                Mode = mode,
                Apply = apply,
            },
            cancellationToken,
            CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Hides, shows, deletes, or clears stored overlay world markers; markers are a plugin-drawn view overlay, never model geometry, not saved with the document, cleared when the document changes, always drawn on top of the model, and visible in capture_current_view. A blank selector entry (an empty or whitespace-only name, id, or group) is refused, while a request with no selector at all acts on every stored marker; the store holds at most 500 markers. Defaults to dry-run.")]
    [ToolCapabilities(ToolEffects.View | ToolEffects.LocalState, RequiresHost = true, RequiresDocument = true)]
    public Task<WorldMarkerOverlayManageResponse> WorldMarkersManage(
        [Description("Required operation: hide, show, delete, or clear. clear with no selector removes every stored marker.")] string operation,
        [Description("Optional marker-name selectors; blank entries are refused.")] List<string> names = null,
        [Description("Optional marker ids as returned by world_markers_list; blank entries are refused.")] List<string> ids = null,
        [Description("Optional group selector; blank is refused. Omit every selector to act on all stored markers.")] string group = null,
        [Description("False validates and previews the operation; true applies it to the overlay. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.WorldMarkersManageAsync(
            new WorldMarkerOverlayManageRequest
            {
                Operation = operation,
                Names = names ?? new List<string>(),
                Ids = ids ?? new List<string>(),
                Group = group,
                Apply = apply,
            },
            cancellationToken,
            CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Lists the overlay world markers stored for the active document, with visibility flags and overlay diagnostics. Markers are a plugin-drawn view overlay, never model geometry: the store is not saved with the document, is cleared when the document changes, always draws on top of the model, respects section clipping, appears in capture_current_view, and holds at most 500 markers.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<WorldMarkerOverlayListResponse> WorldMarkersList(
        [Description("Optional marker-name filter; blank entries are ignored.")] List<string> names = null,
        [Description("Optional group filter; blank means no group restriction.")] string group = null,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.WorldMarkersListAsync(
            new WorldMarkerOverlayListRequest
            {
                Names = names ?? new List<string>(),
                Group = group,
            },
            cancellationToken,
            CreateTarget(instanceId, navisworksVersion));
    }
}
