using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksViewpointAnimationTools : NavisworksToolBase
{
    public NavisworksViewpointAnimationTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Lists, creates or deletes saved viewpoint animations built from copies of saved viewpoints, preserving clipping, overrides and redlines. Consecutive repeated cameras become cuts; optional holds pause at each stop. Dry-run by default. Step timing via frame speeds is unverified until L3.")]
    [ToolCapabilities(ToolEffects.View | ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ViewpointAnimationResponse> ViewpointAnimation(
        [Description("Operation: list, create or delete.")] string action = "list",
        [Description("Animation name, required for create and delete.")] string name = "",
        [Description("Target folder; empty means root.")] string folderPath = "",
        [Description("Source folder's direct saved viewpoints in order; excludes animations. Use this or viewpoints.")] string sourceFolder = "",
        [Description("Ordered saved viewpoint paths or unique names, at most 1000. Use this or sourceFolder.")] List<string> viewpoints = null,
        [Description("Pause at each stop in seconds, 0-3600.")] double holdSeconds = 0,
        [Description("Pause before consecutive repeated cameras in seconds, 0-3600.")] double pauseSeconds = 2,
        [Description("Seconds per move, 0-3600; zero keeps saved speeds. Timing requires L3 validation.")] double stepSeconds = 0,
        [Description("Loop playback when supported by this Navisworks version.")] bool loops = false,
        [Description("none, sync or spline; empty leaves the created default.")] string smoothing = "",
        [Description("Replace an item with the same name in the target folder.")] bool overwrite = false,
        [Description("False previews the plan; true creates or deletes.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version when exactly one host matches.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default) =>
        _hostBridgeClient.ViewpointAnimationAsync(new ViewpointAnimationRequest
        {
            Action = action, Name = name, FolderPath = folderPath, SourceFolder = sourceFolder, Viewpoints = viewpoints,
            HoldSeconds = holdSeconds, PauseSeconds = pauseSeconds, StepSeconds = stepSeconds,
            Loops = loops, Smoothing = smoothing, Overwrite = overwrite, Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
}
