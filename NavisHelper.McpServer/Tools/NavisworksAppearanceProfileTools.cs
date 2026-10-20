using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksAppearanceProfileTools : NavisworksToolBase
{
    public NavisworksAppearanceProfileTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Applies a saved Appearance Profiler profile (.xml/.dat) with Navisworks' own loader as one undo step (Ctrl+Z in Navisworks). Unmatched items are greyed as in the profiler. Navisworks silently ignores a profile it cannot read, so check errors in the dry-run, which lists the rules and blocking errors; apply refuses profiles with errors. The overrides are permanent and model_color_scheme reset does not undo them; use Ctrl+Z, or reopen without saving.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<AppearanceProfileApplyResponse> AppearanceProfileApply(
        [Description("Rooted path to a saved Appearance Profiler .xml or .dat file on the Navisworks host; maximum 10 MB.")] string profilePath,
        [Description("False lists profile rules without changing the document; true invokes the native loader. Default false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version, when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default) =>
        _hostBridgeClient.AppearanceProfileApplyAsync(new AppearanceProfileApplyRequest
        { ProfilePath = profilePath, Apply = apply }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
}
