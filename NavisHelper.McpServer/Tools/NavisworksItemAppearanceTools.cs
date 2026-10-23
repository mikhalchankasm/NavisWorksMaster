using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksItemAppearanceTools : NavisworksToolBase
{
    public NavisworksItemAppearanceTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Reads each item's original and current colour and transparency and whether they are overrides (Override Color/Transparency, model_color_scheme, appearance profiles). Read-only.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<ItemAppearanceResponse> ItemAppearance(
        [Description("Match handles from find_items or other queries; pass these or useSelection=true, not both.")] List<string> matchHandles = null,
        [Description("Read the current selection instead of matchHandles. Default false.")] bool useSelection = false,
        [Description("Maximum returned items, clamped to 1-500; counts cover all geometry items. Default 50.")] int maxItems = 50,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version, when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default) =>
        _hostBridgeClient.ItemAppearanceAsync(new ItemAppearanceRequest
        { MatchHandles = matchHandles, UseSelection = useSelection, MaxItems = maxItems }, cancellationToken, CreateTarget(instanceId, navisworksVersion));

    [McpServerTool]
    [Description("Resets colour and transparency overrides of given items to their original materials, like Reset Appearance in Navisworks, as one undo step. Unlike model_color_scheme reset it works on any items. Dry-run by default.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ResetAppearanceResponse> ResetAppearance(
        [Description("Match handles from find_items or other queries; pass these or useSelection=true, not both.")] List<string> matchHandles = null,
        [Description("Reset the current selection instead of matchHandles. Default false.")] bool useSelection = false,
        [Description("False previews counts without changing the document; true resets overrides. Default false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version, when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default) =>
        _hostBridgeClient.ResetAppearanceAsync(new ResetAppearanceRequest
        { MatchHandles = matchHandles, UseSelection = useSelection, Apply = apply }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
}
