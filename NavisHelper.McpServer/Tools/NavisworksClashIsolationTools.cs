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
    [Description("Previews and optionally isolates one existing Clash Detective result by resultHandle. Can highlight A/B, clip around the clash point or item bounds, hide everything except the pair, choose a preset or custom camera, and optionally capture a screenshot. Defaults to dry-run.")]
    public Task<ClashIsolateResultResponse> ClashIsolateResult(
        [Description("Required resultHandle from clash_list_results, for example clash-result:1:1.")] string resultHandle,
        [Description("Section-box mode: point creates a cube around the clash point; items uses the combined A/B bounds plus padding. Default is point.")] string boxMode = "point",
        [Description("For point mode, positive half-size of the box in millimeters. For items mode, non-negative padding around combined bounds; 0 uses exact A/B bounds. Default is 1000.")] double boxOffsetMm = 1000,
        [Description("Enable a temporary section box around the clash. Default is true.")] bool useSectionBox = true,
        [Description("Temporarily hide model branches outside clash sides A and B. Original hidden state is restored by clash_reset_isolation. Default is false.")] bool isolatePair = false,
        [Description("Apply transparency to nearby context while keeping A/B opaque. Default is false.")] bool useContextTransparency = false,
        [Description("Context transparency from 0 to 1. Default is 0.7.")] double contextTransparency = 0.7,
        [Description("Optional A-side color in #RRGGBB, RAL, or R,G,B form. Default is red #FF2626.")] string colorAHex = "",
        [Description("Optional B-side color in #RRGGBB, RAL, or R,G,B form. Default is blue #2666FF.")] string colorBHex = "",
        [Description("Camera: current, iso, iso_opposite, top, front, back, left, right, or custom. Current preserves orientation and zooms to the box. Default is current.")] string cameraMode = "current",
        [Description("Required for cameraMode=custom. Exact camera position in document coordinates.")] Point3Info cameraPosition = null,
        [Description("Optional camera target in document coordinates. Defaults to the clash point.")] Point3Info cameraTarget = null,
        [Description("Optional custom camera up vector. Defaults to global +Z.")] Point3Info cameraUp = null,
        [Description("Projection: current, orthographic, or perspective. With current, iso/custom preserve the active projection while top/front/back/left/right use orthographic.")] string projection = "current",
        [Description("Optional absolute .png/.jpg/.jpeg/.bmp path. When supplied with apply=true, captures the isolated view after the camera is applied.")] string screenshotPath = "",
        [Description("Screenshot profile: compact, fullhd, large, or source. Default is compact.")] string screenshotProfile = "compact",
        [Description("Optional screenshot format override: jpg, png, or bmp. Empty infers from screenshotPath.")] string screenshotFormat = "",
        [Description("Optional maximum screenshot width. 0 keeps source width.")] int? screenshotMaxWidth = null,
        [Description("Optional maximum screenshot height. 0 keeps source height.")] int? screenshotMaxHeight = null,
        [Description("JPEG quality from 1 to 100.")] int? screenshotJpegQuality = null,
        [Description("Allow replacing an existing screenshot file. Default is false.")] bool overwriteScreenshot = false,
        [Description("False previews the resolved result and settings; true changes the transient Navisworks view. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashIsolateResultAsync(new ClashIsolateResultRequest
        {
            ResultHandle = resultHandle,
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
    [Description("Restores the viewpoint, section box, appearance overrides, and temporary visibility changed by clash_isolate_result in the active document. Defaults to dry-run.")]
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
    [Description("Captures the current Navisworks view exactly as displayed. Use after clash_isolate_result or after manually choosing any camera angle. Defaults to dry-run.")]
    public Task<CaptureCurrentViewResponse> CaptureCurrentView(
        [Description("Required absolute output path ending in .png, .jpg, .jpeg, or .bmp.")] string outputPath,
        [Description("Screenshot profile: compact, fullhd, large, or source. Default is compact.")] string screenshotProfile = "compact",
        [Description("Optional format override: jpg, png, or bmp. Empty infers from outputPath.")] string screenshotFormat = "",
        [Description("Optional maximum image width. 0 keeps source width.")] int? screenshotMaxWidth = null,
        [Description("Optional maximum image height. 0 keeps source height.")] int? screenshotMaxHeight = null,
        [Description("JPEG quality from 1 to 100.")] int? screenshotJpegQuality = null,
        [Description("Allow replacing an existing output file. Default is false.")] bool overwrite = false,
        [Description("False validates and previews the output; true writes the screenshot. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.CaptureCurrentViewAsync(new CaptureCurrentViewRequest
        {
            OutputPath = outputPath,
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
