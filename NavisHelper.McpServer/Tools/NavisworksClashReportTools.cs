using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashReportTools : NavisworksToolBase
{
    public NavisworksClashReportTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Reports existing results; dry-run. apply=true creates section-box viewpoints, available screenshots and HTML/JSON.")]
    [ToolCapabilities(ToolEffects.View | ToolEffects.Document | ToolEffects.Files | ToolEffects.LocalState, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashGenerateReportResponse> ClashGenerateReport(
        [Description("False previews counts/paths; true creates viewpoints/screenshots/report.")] bool apply = false,
        [Description("Test name: exact, else contains; empty=all.")] string testName = "",
        [Description("Test names: exact, else contains; empty with testName empty=all.")] List<string> testNames = null,
        [Description("New, Active, Reviewed, Approved, Resolved; omitted=New/Active.")] List<string> statusFilters = null,
        [Description("All statuses, overriding New/Active or statusFilters.")] bool includeAllStatuses = false,
        [Description("Results per call, max 5000; screenshots/viewpoints: prefer 500-1000.")] int limit = 100,
        [Description("Sorted filtered result offset, zero-based; page: nextResultOffset/hasMoreResults.")] int resultOffset = 0,
        [Description("Folder; empty=timestamped beside model or in Documents/NavisHelper/ClashReports.")] string outputDirectory = "",
        [Description("Allow writing into a non-empty output folder. Default is false.")] bool overwrite = false,
        [Description("Append to outputDirectory artifacts. First: overwrite=true; later: append=true, resultOffset=nextResultOffset.")] bool append = false,
        [Description("On apply above 10000 filtered clashes: required; ask user first.")] bool confirmLargeReport = false,
        [Description("Run matched tests before reading; empty testName/testNames=all. Requires apply=true.")] bool runTests = false,
        [Description("Box distance, mm: point half-size; items padding.")] double boxOffsetMm = 1500,
        [Description("point: clash-centered box; items: A/B bounds plus padding.")] string boxMode = "point",
        [Description("Nearby non-clashing context transparency, 0..1.")] double contextTransparency = 0.5,
        [Description("Deprecated name; enables safe owner-level screenshot transparency, no full box scan.")] bool useFullBoxTransparency = false,
        [Description("none, hybrid, object_pair, spatial; enable for artifactGranularity=cluster.")] string groupMode = "none",
        [Description("result: viewpoint/image per clash; cluster: shared per cluster, full filtered scope in one call, no append/non-zero resultOffset.")] string artifactGranularity = "result",
        [Description("full: raw paths/cluster previews; compact omits duplicate long paths, descriptions, association keys and cluster previews from response only; manifest.json/report.html stay full.")] string verbosity = "full",
        [Description("Spatial/hybrid cluster distance, mm.")] double clusterDistanceMm = 300,
        [Description("When true, HTML cluster summaries include bounded member rows. Default is true.")] bool includeClusterMembers = true,
        [Description("HTML member rows per cluster; max 200.")] int maxMembersPerClusterInHtml = 25,
        [Description("Optional side A color as #RRGGBB. Default is red.")] string colorAHex = "",
        [Description("Optional side B color as #RRGGBB. Default is blue.")] string colorBHex = "",
        [Description("Create one saved viewpoint per clash when apply=true. Default is true.")] bool createViewpoints = true,
        [Description("Capture image per clash on apply if export available.")] bool captureScreenshots = true,
        [Description("Red redline target at clash point before viewpoints/screenshots.")] bool includeClashPointMarker = false,
        [Description("Extra orthographic top image per clash if screenshots enabled.")] bool captureTopViewScreenshots = false,
        [Description("compact: 1280x720 JPEG; fullhd/standard: 1920x1080 JPEG; large: 2560x1440 JPEG; source: legacy BMP.")] string screenshotProfile = "compact",
        [Description("jpg/jpeg, png, bmp; empty=profile default.")] string screenshotFormat = "",
        [Description("Max width, pixels; 0=profile default. Never upscale.")] int screenshotMaxWidth = 0,
        [Description("Max height, pixels; 0=profile default. Never upscale.")] int screenshotMaxHeight = 0,
        [Description("JPEG quality 1..100; 0=profile default; lower=smaller file.")] int screenshotJpegQuality = 0,
        [Description("Exclude if either item name/path contains any value; keeps Clash Detective results.")] List<string> excludeItemNameContains = null,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashGenerateReportAsync(new ClashGenerateReportRequest
        {
            Apply = apply,
            TestName = testName,
            TestNames = testNames ?? new List<string>(),
            StatusFilters = statusFilters ?? new List<string>(),
            IncludeAllStatuses = includeAllStatuses,
            Limit = limit,
            ResultOffset = resultOffset,
            OutputDirectory = outputDirectory,
            Overwrite = overwrite,
            Append = append,
            ConfirmLargeReport = confirmLargeReport,
            RunTests = runTests,
            BoxOffsetMm = boxOffsetMm,
            BoxMode = boxMode,
            ContextTransparency = contextTransparency,
            UseFullBoxTransparency = useFullBoxTransparency,
            GroupMode = groupMode,
            ArtifactGranularity = artifactGranularity,
            Verbosity = verbosity,
            ClusterDistanceMm = clusterDistanceMm,
            IncludeClusterMembers = includeClusterMembers,
            MaxMembersPerClusterInHtml = maxMembersPerClusterInHtml,
            ColorAHex = colorAHex,
            ColorBHex = colorBHex,
            CreateViewpoints = createViewpoints,
            CaptureScreenshots = captureScreenshots,
            IncludeClashPointMarker = includeClashPointMarker,
            CaptureTopViewScreenshots = captureTopViewScreenshots,
            ScreenshotProfile = screenshotProfile,
            ScreenshotFormat = screenshotFormat,
            ScreenshotMaxWidth = screenshotMaxWidth > 0 ? screenshotMaxWidth : null,
            ScreenshotMaxHeight = screenshotMaxHeight > 0 ? screenshotMaxHeight : null,
            ScreenshotJpegQuality = screenshotJpegQuality > 0 ? screenshotJpegQuality : null,
            ExcludeItemNameContains = excludeItemNameContains ?? new List<string>(),
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Creates Saved Viewpoints from results; dry-run. apply=true saves viewpoints only; no test runs, reports, files or screenshots.")]
    [ToolCapabilities(ToolEffects.View | ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashSaveViewpointsResponse> ClashSaveViewpoints(
        [Description("False previews results/names; true saves viewpoints.")] bool apply = false,
        [Description("Test name: exact, else contains; empty=all.")] string testName = "",
        [Description("Test names: exact, else contains; empty with testName empty=all.")] List<string> testNames = null,
        [Description("New, Active, Reviewed, Approved, Resolved; omitted=New/Active.")] List<string> statusFilters = null,
        [Description("Include all statuses instead of New/Active.")] bool includeAllStatuses = false,
        [Description("Results/viewpoints per call, max 5000; page: resultOffset/nextResultOffset.")] int limit = 100,
        [Description("Sorted filtered result offset, zero-based; page: nextResultOffset/hasMoreResults.")] int resultOffset = 0,
        [Description("On apply above 10000 filtered clashes: required; ask user first.")] bool confirmLargeViewpoints = false,
        [Description("Viewpoint folder; '/' nests. Empty=timestamped 'NavisHelper Clash Viewpoints ...'.")] string folderPath = "",
        [Description("Legacy; always creates '0000 Базовый вид' first in folder.")] bool createResetViewpoint = true,
        [Description("Box distance, mm: point half-size; items padding.")] double boxOffsetMm = 1500,
        [Description("point: clash-centered box; items: A/B bounds plus padding.")] string boxMode = "point",
        [Description("Nearby non-clashing context transparency, 0..1.")] double contextTransparency = 0.5,
        [Description("Deprecated/ignored: Saved Viewpoints have no transparency.")] bool useFullBoxTransparency = false,
        [Description("Deprecated/ignored: Saved Viewpoints have no transparency.")] bool useRootContextTransparency = false,
        [Description("Two views per clash: '(1)' diagonal top ISO, '(2)' opposite diagonal top ISO.")] bool createOppositeViewpoints = false,
        [Description("Optional side A color as #RRGGBB. Default is red.")] string colorAHex = "",
        [Description("Optional side B color as #RRGGBB. Default is blue.")] string colorBHex = "",
        [Description("Draw a red redline target marker at the clash point before saving each viewpoint. Default is false.")] bool includeClashPointMarker = false,
        [Description("Exclude if either item name/path contains any value.")] List<string> excludeItemNameContains = null,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashSaveViewpointsAsync(new ClashSaveViewpointsRequest
        {
            Apply = apply,
            TestName = testName,
            TestNames = testNames ?? new List<string>(),
            StatusFilters = statusFilters ?? new List<string>(),
            IncludeAllStatuses = includeAllStatuses,
            Limit = limit,
            ResultOffset = resultOffset,
            ConfirmLargeViewpoints = confirmLargeViewpoints,
            FolderPath = folderPath,
            CreateResetViewpoint = createResetViewpoint,
            BoxOffsetMm = boxOffsetMm,
            BoxMode = boxMode,
            ContextTransparency = contextTransparency,
            UseFullBoxTransparency = useFullBoxTransparency,
            UseRootContextTransparency = useRootContextTransparency,
            CreateOppositeViewpoints = createOppositeViewpoints,
            ColorAHex = colorAHex,
            ColorBHex = colorBHex,
            IncludeClashPointMarker = includeClashPointMarker,
            ExcludeItemNameContains = excludeItemNameContains ?? new List<string>(),
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Active/last clash_generate_report status; callable during run.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = false)]
    public Task<ClashReportStatusResponse> ClashReportStatus(
        [Description("Optional operation id. Empty means active clash report, or the last report if none is active.")] string operationId = "",
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashReportStatusAsync(new ClashReportStatusRequest
        {
            OperationId = operationId,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Cooperatively cancels clash_generate_report. Current image/viewpoint step may finish; writes partial artifacts, stops before next clash.")]
    [ToolCapabilities(ToolEffects.LocalState, RequiresHost = true, RequiresDocument = false)]
    public Task<ClashReportStatusResponse> CancelClashReport(
        [Description("Optional operation id. Empty means the active clash report.")] string operationId = "",
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.CancelClashReportAsync(new CancelClashReportRequest
        {
            OperationId = operationId,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

}
