using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksDuplicateGeometryTools : NavisworksToolBase
{
    public NavisworksDuplicateGeometryTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Finds items with identical geometry (same rounded bounding box and primitive count), e.g. copied pipelines or E3D fitting pairs that cause false self-clashes. The first 25 groups have a matchHandle for select_items; narrow the scope for handles to the rest. Read-only.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<FindDuplicateGeometryResponse> FindDuplicateGeometry(
        [Description("Match handles from find_items or other queries; pass these or useSelection=true, not both.")] List<string> matchHandles = null,
        [Description("Scan the current selection instead of matchHandles. Default false.")] bool useSelection = false,
        [Description("Rounding grid for bounding boxes in model units (0.01 = 1 cm in a metre model); 0.0001-1.")] double gridSize = 0.01,
        [Description("Grouping mode: all, within_root or across_roots. Default all.")] string mode = "all",
        [Description("Maximum returned groups, clamped to 1-100; handles for the first 25. Default 50.")] int maxGroups = 50,
        [Description("Preview paths per group, clamped to 1-20. Default 5.")] int previewPerGroup = 5,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version, when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        if (!DuplicateGeometryModes.TryNormalize(mode, out var normalizedMode))
            throw new InvalidOperationException(ErrorCodes.SchemaViolation + ": mode must be all, within_root or across_roots.");
        return _hostBridgeClient.FindDuplicateGeometryAsync(new FindDuplicateGeometryRequest
        {
            MatchHandles = matchHandles,
            UseSelection = useSelection,
            GridSize = gridSize,
            Mode = normalizedMode,
            MaxGroups = maxGroups,
            PreviewPerGroup = previewPerGroup,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
