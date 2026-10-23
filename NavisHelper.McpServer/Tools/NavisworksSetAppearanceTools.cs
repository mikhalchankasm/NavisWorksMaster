using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksSetAppearanceTools : NavisworksToolBase
{
    public NavisworksSetAppearanceTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Applies ordered appearance steps to saved selection or search sets in one call and one undo step: each step sets colorHex, transparency (0-100) and/or hide for a whole set; later steps win. Dry-run by default. Undo with Ctrl+Z or reset_appearance (color/transparency only; hidden items need unhiding).")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<SelectionSetsApplyAppearanceResponse> SelectionSetsApplyAppearance(
        [Description("Ordered steps (1-200). Each object has required setPath (saved selection/search set path or unique name), optional colorHex (#RRGGBB), transparency (whole percent 0-100), hide (true hides; false does not unhide). Each step needs colorHex, transparency or hide=true. Folders are refused; invalid or unresolved steps report errors and other steps continue.")] List<SetAppearanceStep> steps,
        [Description("False previews each set's item count without changing the document; true applies all valid steps in one transaction. Default false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version, when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default) =>
        _hostBridgeClient.SelectionSetsApplyAppearanceAsync(new SelectionSetsApplyAppearanceRequest
        { Steps = steps, Apply = apply }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
}
