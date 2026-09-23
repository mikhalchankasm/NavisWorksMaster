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
    [Description("Generates a NavisHelper Clash Report workflow from existing Clash Detective results. Defaults to dry-run; pass apply=true to create section-box viewpoints, screenshots when available, and HTML/JSON artifacts.")]
    public Task<ClashGenerateReportResponse> ClashGenerateReport(
        [Description("False previews counts and output paths, true creates viewpoints/screenshots/report artifacts. Default is false/dry-run.")] bool apply = false,
        [Description("Optional test name. Empty means all tests. Exact match is preferred; otherwise contains-match is used.")] string testName = "",
        [Description("Optional list of test names. When provided, exact match is preferred per name, otherwise contains-match is used. Empty with testName empty means all tests.")] List<string> testNames = null,
        [Description("Optional clash status filters. Default when omitted is New and Active. Examples: New, Active, Reviewed, Approved, Resolved.")] List<string> statusFilters = null,
        [Description("Include all clash statuses instead of the default New/Active filter. Equivalent to passing all known statuses. Default is false.")] bool includeAllStatuses = false,
        [Description("Maximum clash results to process in this call. Default is 100, maximum is 5000. For screenshots/viewpoints prefer batches of 500-1000.")] int limit = 100,
        [Description("Zero-based offset into the sorted, filtered clash result set. Use with nextResultOffset/hasMoreResults for paged full reports. Default is 0.")] int resultOffset = 0,
        [Description("Optional output folder. If empty, a timestamped folder is created next to the active model or under Documents/NavisHelper/ClashReports.")] string outputDirectory = "",
        [Description("Allow writing into a non-empty output folder. Default is false.")] bool overwrite = false,
        [Description("Append this batch to existing report artifacts in outputDirectory. Use first call with overwrite=true, then subsequent calls with append=true and resultOffset=nextResultOffset. Default is false.")] bool append = false,
        [Description("Required for apply=true when the filtered report scope exceeds 10000 clashes. The agent should ask the user before setting this true. Default is false.")] bool confirmLargeReport = false,
        [Description("Run Clash Detective tests before reading results. Requires apply=true. With testName/testNames it runs only matched tests; otherwise it runs all tests. Default is false.")] bool runTests = false,
        [Description("Section box distance around each clash, in millimeters. With boxMode=point this is the half-size from the clash point. Default is 1500.")] double boxOffsetMm = 1500,
        [Description("Clash box mode: point creates a box centered on the clash point; items creates a box from both clashing item bounds plus padding. Default is point.")] string boxMode = "point",
        [Description("Transparency for nearby non-clashing context items, 0..1. Default is 0.5.")] double contextTransparency = 0.5,
        [Description("Deprecated name kept for compatibility. When true, uses safe owner-level context transparency for report screenshots; it no longer scans all objects inside the clash box. Default is false.")] bool useFullBoxTransparency = false,
        [Description("Optional report clustering mode: none (default), hybrid, object_pair, or spatial. A cluster mode is required when artifactGranularity=cluster.")] string groupMode = "none",
        [Description("Visual artifact granularity: result (default) creates one viewpoint/image per raw clash; cluster creates one shared viewpoint/image per cluster. Cluster mode requires the complete filtered scope in one call and does not support append or a non-zero resultOffset.")] string artifactGranularity = "result",
        [Description("Response verbosity: full (default) returns all raw paths and cluster preview rows; compact omits duplicated long paths, descriptions, association keys, and cluster preview rows from the MCP response only. manifest.json and report.html always remain full.")] string verbosity = "full",
        [Description("Maximum distance in millimeters for spatial cluster splitting/grouping when groupMode is spatial or hybrid. Default is 300.")] double clusterDistanceMm = 300,
        [Description("When true, HTML cluster summaries include bounded member rows. Default is true.")] bool includeClusterMembers = true,
        [Description("Maximum member rows shown per cluster in HTML. Default is 25, maximum is 200.")] int maxMembersPerClusterInHtml = 25,
        [Description("Optional side A color as #RRGGBB. Default is red.")] string colorAHex = "",
        [Description("Optional side B color as #RRGGBB. Default is blue.")] string colorBHex = "",
        [Description("Create one saved viewpoint per clash when apply=true. Default is true.")] bool createViewpoints = true,
        [Description("Capture one image per clash when apply=true and Navisworks image export is available. Default is true.")] bool captureScreenshots = true,
        [Description("Draw a red redline target marker at the clash point before saving viewpoints/screenshots. Default is false.")] bool includeClashPointMarker = false,
        [Description("Capture an additional orthographic top-view image per clash when screenshots are enabled. Default is false.")] bool captureTopViewScreenshots = false,
        [Description("Screenshot output profile: compact (1280x720 JPEG, default), fullhd/standard (1920x1080 JPEG), large (2560x1440 JPEG), or source (legacy BMP).")] string screenshotProfile = "compact",
        [Description("Optional screenshot format override: jpg/jpeg, png, or bmp. Empty uses the selected profile default.")] string screenshotFormat = "",
        [Description("Optional max screenshot width in pixels. 0 uses profile default. Images are never upscaled.")] int screenshotMaxWidth = 0,
        [Description("Optional max screenshot height in pixels. 0 uses profile default. Images are never upscaled.")] int screenshotMaxHeight = 0,
        [Description("Optional JPEG quality 1..100. 0 uses profile default. Lower values reduce report size.")] int screenshotJpegQuality = 0,
        [Description("Optional list of text filters. If either clashing item name/path contains any text, that clash is excluded from the final report but remains in Clash Detective. Example: Weld.")] List<string> excludeItemNameContains = null,
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
    [Description("Creates Saved Viewpoints from existing Clash Detective results only. Defaults to dry-run; pass apply=true to save viewpoints. This does not run tests, generate reports, write files, or capture screenshots.")]
    public Task<ClashSaveViewpointsResponse> ClashSaveViewpoints(
        [Description("False previews matched clash results and planned viewpoint names, true creates Saved Viewpoints. Default is false/dry-run.")] bool apply = false,
        [Description("Optional test name. Empty means all tests. Exact match is preferred; otherwise contains-match is used.")] string testName = "",
        [Description("Optional list of test names. Empty with testName empty means all tests. Exact match is preferred per name, otherwise contains-match is used.")] List<string> testNames = null,
        [Description("Optional clash status filters. Default when omitted is New and Active. Examples: New, Active, Reviewed, Approved, Resolved.")] List<string> statusFilters = null,
        [Description("Include all clash statuses instead of the default New/Active filter. Default is false.")] bool includeAllStatuses = false,
        [Description("Maximum clash results/viewpoints to process in this call. Default is 100, maximum is 5000. Use resultOffset/nextResultOffset for batches.")] int limit = 100,
        [Description("Zero-based offset into the sorted, filtered clash result set. Use with nextResultOffset/hasMoreResults for paged viewpoint creation. Default is 0.")] int resultOffset = 0,
        [Description("Required for apply=true when the filtered scope exceeds 10000 clashes. The agent should ask the user before setting this true. Default is false.")] bool confirmLargeViewpoints = false,
        [Description("Saved Viewpoints target folder path. Empty creates a timestamped folder named 'NavisHelper Clash Viewpoints ...'. Nested folders use '/'.")] string folderPath = "",
        [Description("Legacy input kept for compatibility. The host always creates '0000 Базовый вид' at the start of the folder.")] bool createResetViewpoint = true,
        [Description("Section box distance around each clash, in millimeters. With boxMode=point this is the half-size from the clash point. Default is 1500.")] double boxOffsetMm = 1500,
        [Description("Clash box mode: point creates a box centered on the clash point; items creates a box from both clashing item bounds plus padding. Default is point.")] string boxMode = "point",
        [Description("Transparency for nearby non-clashing context items, 0..1. Default is 0.5.")] double contextTransparency = 0.5,
        [Description("Deprecated for saved viewpoints. Ignored because Saved Viewpoints are saved without transparency. Default is false.")] bool useFullBoxTransparency = false,
        [Description("Deprecated for saved viewpoints. Ignored because Saved Viewpoints are saved without transparency. Default is false.")] bool useRootContextTransparency = false,
        [Description("When true, saves two viewpoints per clash: '(1)' from the standard diagonal top ISO angle and '(2)' from the opposite diagonal top ISO angle. Default is false.")] bool createOppositeViewpoints = false,
        [Description("Optional side A color as #RRGGBB. Default is red.")] string colorAHex = "",
        [Description("Optional side B color as #RRGGBB. Default is blue.")] string colorBHex = "",
        [Description("Draw a red redline target marker at the clash point before saving each viewpoint. Default is false.")] bool includeClashPointMarker = false,
        [Description("Optional list of text filters. If either clashing item name/path contains any text, that clash is excluded from viewpoint creation. Example: Weld.")] List<string> excludeItemNameContains = null,
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
    [Description("Returns status for the active or last clash_generate_report operation. This can be called while a large report is running.")]
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
    [Description("Requests cooperative cancellation of the active clash_generate_report operation. The current screenshot/viewpoint step may finish, then the report writes partial artifacts and stops before the next clash.")]
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
