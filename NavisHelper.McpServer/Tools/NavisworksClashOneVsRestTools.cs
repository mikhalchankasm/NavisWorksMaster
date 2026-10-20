using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashOneVsRestTools : NavisworksToolBase
{
    public NavisworksClashOneVsRestTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Creates tests: one node vs siblings (pairs) or all siblings together (single), at any tree level. Reads target/direct siblings only. Creates explicit Selection Sets in one folder, names without /, for comparable runs. Dry-run; apply=true creates sets/tests, never runs. Skips include reasons.")]
    [ToolCapabilities(ToolEffects.Document | ToolEffects.LocalState, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashOneVsRestResponse> ClashOneVsRest(
        [Description("find_items/list_item_children handle for exactly one node; preferred for deep nodes.")] string targetMatchHandle = "",
        [Description("Exact list_item_children path, traversed by segment; used if targetMatchHandle empty.")] string targetPath = "",
        [Description("pairs: test per sibling; single: one test vs set of all included siblings.")] string mode = ClashOneVsRestPlanner.ModePairs,
        [Description("False previews sets, tests, conflicts and skipped pairs. True creates them. Default false.")] bool apply = false,
        [Description("Sibling-name masks (*, ?, case-insensitive); empty=all siblings.")] List<string> includeNameMasks = null,
        [Description("Optional wildcard masks on sibling names to leave out; an exclude always wins.")] List<string> excludeNameMasks = null,
        [Description("Include hidden siblings. Default true.")] bool includeHidden = true,
        [Description("Selection Sets folder path, '/'-separated. Default: 'Clash one vs rest/<target>'.")] string setFolder = "",
        [Description("{a}, {b}, {index}; display names without /.")] string pairNameTemplate = ClashOneVsRestPlanner.DefaultPairNameTemplate,
        [Description("Clash type: hard, hard_conservative, clearance, or duplicate. Default hard.")] string testType = "hard",
        [Description("Tolerance in millimeters. Default 1.")] double toleranceMm = ClashOneVsRestPlanner.DefaultToleranceMm,
        [Description("[{nameMask,testType,toleranceMm}]; first mask matching B name wins.")] List<ClashOneVsRestOverride> pairOverrides = null,
        [Description("Skip/report siblings whose BBox misses target within tolerance.")] bool bboxPrefilter = false,
        [Description("Replace same-name sets/tests; false reports conflicts.")] bool overwriteExisting = false,
        [Description("Maximum planned test count. Default 200, maximum 500.")] int limit = 200,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashOneVsRestAsync(new ClashOneVsRestRequest
        {
            Apply = apply,
            TargetMatchHandle = targetMatchHandle,
            TargetPath = targetPath,
            Mode = mode,
            IncludeNameMasks = includeNameMasks ?? new List<string>(),
            ExcludeNameMasks = excludeNameMasks ?? new List<string>(),
            IncludeHidden = includeHidden,
            SetFolder = setFolder,
            PairNameTemplate = pairNameTemplate,
            TestType = testType,
            ToleranceMm = toleranceMm,
            PairOverrides = pairOverrides ?? new List<ClashOneVsRestOverride>(),
            BboxPrefilter = bboxPrefilter,
            OverwriteExisting = overwriteExisting,
            Limit = limit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
