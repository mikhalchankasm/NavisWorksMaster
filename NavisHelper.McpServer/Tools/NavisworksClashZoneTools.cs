using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashZoneTools : NavisworksToolBase
{
    public NavisworksClashZoneTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Appends the zones.ifc marker file written by clash_zones_export to the open document (operation=append), swaps the loaded run for another (replace), removes it (remove) or sets its transparency (style). Only models named zones.ifc are touched; the first model is never removed. Never saves the document. Dry-run by default.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashZoneMarkersResponse> ClashZonesMarkers(
        [Description("Marker operation: append, replace, remove or style.")] string operation,
        [Description("Path to zones.ifc or its run folder. Required for append/replace; omitted for remove/style targets all loaded marker models.")] string markerPath = "",
        [Description("Required for style: finite transparency from 0 (opaque) to 1 (transparent).")] double? transparency = null,
        [Description("False previews the plan; true changes the document's marker models. Never saves the document.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashZonesMarkersAsync(new ClashZoneMarkersRequest {
            Operation = operation, MarkerPath = markerPath, Transparency = transparency, Apply = apply
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Builds clash zones from Clash Detective results and writes one run folder: zones.json, zones.xlsx and zones.ifc with one semi-transparent sphere per zone (per clash with mode=points). With previousRunPath it tracks zones across iterations (new, persisting, moved, disappeared) and adds a comparison workbook. Reads results only; never saves the document. Dry-run by default.")]
    [ToolCapabilities(ToolEffects.Files, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashZoneExportResponse> ClashZonesExport(
        [Description("Optional test names. Empty with testHandles empty means all tests.")] List<string> testNames = null,
        [Description("Optional test handles from clash_list_tests.")] List<string> testHandles = null,
        [Description("Clash statuses to draw. Empty defaults to New, Active and Reviewed.")] List<string> statuses = null,
        [Description("zones clusters clashes; points draws one sphere per clash.")] string mode = "zones",
        [Description("Clustering distance in millimetres. Default 300; finite and positive.")] double? epsMm = null,
        [Description("Maximum enclosing zone radius in millimetres. Default 3000; finite and positive.")] double? maxZoneRadiusMm = null,
        [Description("Minimum displayed zone radius in millimetres. Default 150; finite and positive.")] double? minRadiusMm = null,
        [Description("Zone padding in millimetres. Default 100; finite and non-negative.")] double? padMm = null,
        [Description("Sphere radius for points mode in millimetres. Default 50; finite and positive.")] double? pointRadiusMm = null,
        [Description("Include results marked by NavisHelper ignore rules. Default false.")] bool includeIgnored = false,
        [Description("Maximum raw results to analyze. Default and maximum 50000.")] int maxResults = 50000,
        [Description("Parent folder for the run folder. Default is clash_zones beside the document; required for an unsaved document.")] string outputDirectory = "",
        [Description("Run folder identifier, 1-64 ASCII letters, digits, underscores or hyphens. Default local timestamp yyyyMMdd-HHmmss.")] string runId = "",
        [Description("Previous run folder or zones.json path for iteration comparison.")] string previousRunPath = "",
        [Description("Allow comparison when run parameters differ, with a warning. Default false.")] bool allowParameterMismatch = false,
        [Description("False previews paths and summary; true writes the run folder. Never saves the document.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashZonesExportAsync(new ClashZoneExportRequest {
            TestNames = testNames ?? new List<string>(), TestHandles = testHandles ?? new List<string>(),
            Statuses = statuses ?? new List<string>(), Mode = mode, EpsMm = epsMm,
            MaxZoneRadiusMm = maxZoneRadiusMm, MinRadiusMm = minRadiusMm, PadMm = padMm, PointRadiusMm = pointRadiusMm,
            IncludeIgnored = includeIgnored, MaxResults = maxResults, OutputDirectory = outputDirectory,
            RunId = runId, PreviousRunPath = previousRunPath, AllowParameterMismatch = allowParameterMismatch, Apply = apply
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
