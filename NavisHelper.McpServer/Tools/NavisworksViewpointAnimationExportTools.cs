using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksViewpointAnimationExportTools : NavisworksToolBase
{
    public NavisworksViewpointAnimationExportTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Reports the Navisworks animation exporter's options and validates an export request; exports nothing yet.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<ViewpointAnimationExportResponse> ViewpointAnimationExport(
        [Description("Saved viewpoint animation path or unique name.")] string animation = "",
        [Description("png, jpg or avi; validated now for a later export.")] string format = "png",
        [Description("Absolute output folder; empty uses viewpoint_animations/<name> beside the document. No folder is created.")] string outputDirectory = "",
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version when exactly one host matches.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default) =>
        _hostBridgeClient.ViewpointAnimationExportAsync(new ViewpointAnimationExportRequest
        {
            Animation = animation, Format = format, OutputDirectory = outputDirectory,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
}
