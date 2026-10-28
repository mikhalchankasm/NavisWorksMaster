using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksViewpointAnimationExportTools : NavisworksToolBase
{
    public NavisworksViewpointAnimationExportTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Previews exporter options or synchronously exports a saved viewpoint animation; reports produced files and detected formats.")]
    [ToolCapabilities(ToolEffects.Files | ToolEffects.View, RequiresHost = true, RequiresDocument = true)]
    public Task<ViewpointAnimationExportResponse> ViewpointAnimationExport(
        [Description("Saved viewpoint animation path or unique name.")] string animation = "",
        [Description("png, jpg or avi; the output extension requests the format, verified from produced file signatures.")] string format = "png",
        [Description("Absolute output folder; empty uses viewpoint_animations/<name> beside the document.")] string outputDirectory = "",
        [Description("Export now; false only validates and probes exporter options.")] bool apply = false,
        [Description("Allow the exporter to replace existing output for this animation.")] bool overwrite = false,
        [Description("Image width: 16-4096; 0 keeps the exporter value.")] int width = 0,
        [Description("Image height: 16-4096; 0 keeps the exporter value.")] int height = 0,
        [Description("Frames per second: 1-60; 0 keeps the exporter value.")] int fps = 0,
        [Description("Anti-alias level: 1, 2, 4, 8, 16, 32, 64; 0 keeps the exporter value.")] int antiAliasLevel = 0,
        [Description("Exporter plugin to drive: lcodpanim, lcodpavi, lcodpexpng or lcodpexjpg.")] string exporter = "lcodpanim",
        [Description("Extra exporter options as name=value, at most 20; set existing or add new options.")] List<string> exporterOptions = null,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version when exactly one host matches.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default) =>
        _hostBridgeClient.ViewpointAnimationExportAsync(new ViewpointAnimationExportRequest
        {
            Animation = animation, Format = format, OutputDirectory = outputDirectory,
            Apply = apply, Overwrite = overwrite, Width = width, Height = height, Fps = fps, AntiAliasLevel = antiAliasLevel,
            Exporter = exporter, ExporterOptions = exporterOptions,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
}
