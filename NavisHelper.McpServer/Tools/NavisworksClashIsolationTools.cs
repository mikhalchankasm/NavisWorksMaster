using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashIsolationTools : NavisworksToolBase
{
    public NavisworksClashIsolationTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Isolates existing result in transient view: A/B highlighting, clipping, visibility, camera, screenshot. Dry-run. Select by positional resultHandle or stable testHandle + resultName.")]
    [ToolCapabilities(ToolEffects.View | ToolEffects.Files | ToolEffects.LocalState, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashIsolateResultResponse> ClashIsolateResult(
        [Description("clash_list_results: clash-result:1:1; stale after grouping. If resultName supplied, check handle against name.")] string resultHandle = "",
        [Description("Test handle from clash_list_tests; required with resultName when resultHandle is empty.")] string testHandle = "",
        [Description("Result name within the test, for example Clash3. Exact, case-insensitive, unique within the test.")] string resultName = "",
        [Description("point: clash-centered cube; items: A/B bounds plus padding.")] string boxMode = "point",
        [Description("mm: point positive half-size; items non-negative padding (0=exact A/B bounds).")] double boxOffsetMm = 1000,
        [Description("Enable a temporary section box around the clash. Default is true.")] bool useSectionBox = true,
        [Description("Hide branches outside A/B; clash_reset_isolation restores hidden state.")] bool isolatePair = false,
        [Description("Apply transparency to nearby context while keeping A/B opaque. Default is false.")] bool useContextTransparency = false,
        [Description("Context transparency from 0 to 1. Default is 0.7.")] double contextTransparency = 0.7,
        [Description("Optional A-side color in #RRGGBB, RAL, or R,G,B form. Default is red #FF2626.")] string colorAHex = "",
        [Description("Optional B-side color in #RRGGBB, RAL, or R,G,B form. Default is blue #2666FF.")] string colorBHex = "",
        [Description("current (keep orientation, zoom to box), iso, iso_opposite, top, front, back, left, right, custom.")] string cameraMode = "current",
        [Description("Required for cameraMode=custom. Exact camera position in document coordinates.")] Point3Info cameraPosition = null,
        [Description("Camera target in document coordinates; empty=clash point.")] Point3Info cameraTarget = null,
        [Description("Optional custom camera up vector. Defaults to global +Z.")] Point3Info cameraUp = null,
        [Description("current, orthographic, perspective. current: iso/custom keep projection; top/front/back/left/right=orthographic.")] string projection = "current",
        [Description("Absolute .png/.jpg/.jpeg/.bmp path; apply=true captures after camera setup.")] string screenshotPath = "",
        [Description("Screenshot profile: compact, fullhd, large, or source. Default is compact.")] string screenshotProfile = "compact",
        [Description("Optional screenshot format override: jpg, png, or bmp. Empty infers from screenshotPath.")] string screenshotFormat = "",
        [Description("Optional maximum screenshot width. 0 keeps source width.")] int? screenshotMaxWidth = null,
        [Description("Optional maximum screenshot height. 0 keeps source height.")] int? screenshotMaxHeight = null,
        [Description("JPEG quality from 1 to 100.")] int? screenshotJpegQuality = null,
        [Description("Allow replacing an existing screenshot file. Default is false.")] bool overwriteScreenshot = false,
        [Description("False previews; true changes transient view.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashIsolateResultAsync(new ClashIsolateResultRequest
        {
            ResultHandle = resultHandle,
            TestHandle = testHandle,
            ResultName = resultName,
            BoxMode = boxMode,
            BoxOffsetMm = boxOffsetMm,
            UseSectionBox = useSectionBox,
            IsolatePair = isolatePair,
            UseContextTransparency = useContextTransparency,
            ContextTransparency = contextTransparency,
            ColorAHex = colorAHex,
            ColorBHex = colorBHex,
            CameraMode = cameraMode,
            CameraPosition = cameraPosition,
            CameraTarget = cameraTarget,
            CameraUp = cameraUp,
            Projection = projection,
            ScreenshotPath = screenshotPath,
            ScreenshotProfile = screenshotProfile,
            ScreenshotFormat = screenshotFormat,
            ScreenshotMaxWidth = screenshotMaxWidth,
            ScreenshotMaxHeight = screenshotMaxHeight,
            ScreenshotJpegQuality = screenshotJpegQuality,
            OverwriteScreenshot = overwriteScreenshot,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Resets clash_isolate_result viewpoint, section box, appearance overrides and temporary visibility in active document. Dry-run.")]
    [ToolCapabilities(ToolEffects.View | ToolEffects.LocalState, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashResetIsolationResponse> ClashResetIsolation(
        [Description("False reports whether an MCP isolation can be reset; true restores it. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashResetIsolationAsync(new ClashResetIsolationRequest
        {
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Captures view as displayed after isolation or manual camera setup. Dry-run.")]
    [ToolCapabilities(ToolEffects.Files, RequiresHost = true, RequiresDocument = true)]
    public Task<CaptureCurrentViewResponse> CaptureCurrentView(
        [Description("Required absolute output path ending in .png, .jpg, .jpeg, or .bmp.")] string outputPath,
        [Description("Screenshot profile: compact, fullhd, large, or source. Default is compact.")] string screenshotProfile = "compact",
        [Description("Optional format override: jpg, png, or bmp. Empty infers from outputPath.")] string screenshotFormat = "",
        [Description("Optional maximum image width. 0 keeps source width.")] int? screenshotMaxWidth = null,
        [Description("Optional maximum image height. 0 keeps source height.")] int? screenshotMaxHeight = null,
        [Description("JPEG quality from 1 to 100.")] int? screenshotJpegQuality = null,
        [Description("Allow replacing an existing output file. Default is false.")] bool overwrite = false,
        [Description("False validates and previews the output; true writes the screenshot. Default is false.")] bool apply = false,
        [Description("True clears the selection highlight for the capture and restores the selection afterwards. Default is false.")] bool hideSelectionHighlight = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.CaptureCurrentViewAsync(new CaptureCurrentViewRequest
        {
            OutputPath = outputPath,
            HideSelectionHighlight = hideSelectionHighlight,
            ScreenshotProfile = screenshotProfile,
            ScreenshotFormat = screenshotFormat,
            ScreenshotMaxWidth = screenshotMaxWidth,
            ScreenshotMaxHeight = screenshotMaxHeight,
            ScreenshotJpegQuality = screenshotJpegQuality,
            Overwrite = overwrite,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
