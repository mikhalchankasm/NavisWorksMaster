using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashOneVsRestTools : NavisworksToolBase
{
    public NavisworksClashOneVsRestTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Creates Clash Detective tests for one model tree node against each of its siblings (mode=pairs) or against all of them at once (mode=single). Works on any tree level, e.g. marks inside one RVM, and reads only the target and its parent's direct children. Each side is bound to an explicit Selection Set the tool creates in one folder (names without '/'), so successive runs stay comparable. Dry-run by default; apply=true creates sets and tests; it never runs tests. Skipped pairs carry a reason.")]
    [ToolCapabilities(ToolEffects.Document | ToolEffects.LocalState, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashOneVsRestResponse> ClashOneVsRest(
        [Description("Match handle from find_items/list_item_children resolving to exactly one target node. Preferred for deep nodes.")] string targetMatchHandle = "",
        [Description("Exact printed target path as list_item_children reports it, traversed segment by segment. Used when targetMatchHandle is empty.")] string targetPath = "",
        [Description("pairs (default): one test per sibling. single: one test, target against a set holding every included sibling.")] string mode = ClashOneVsRestPlanner.ModePairs,
        [Description("False previews sets, tests, conflicts and skipped pairs. True creates them. Default false.")] bool apply = false,
        [Description("Optional wildcard masks (* and ?, case-insensitive) on sibling names; empty includes every sibling.")] List<string> includeNameMasks = null,
        [Description("Optional wildcard masks on sibling names to leave out; an exclude always wins.")] List<string> excludeNameMasks = null,
        [Description("Include hidden siblings. Default true.")] bool includeHidden = true,
        [Description("Selection Sets folder path, '/'-separated. Default: 'Clash one vs rest/<target>'.")] string setFolder = "",
        [Description("Test name template with {a}, {b} and {index}. Names are display names without '/'. Default '{a} vs {b}'.")] string pairNameTemplate = ClashOneVsRestPlanner.DefaultPairNameTemplate,
        [Description("Clash type: hard, hard_conservative, clearance, or duplicate. Default hard.")] string testType = "hard",
        [Description("Tolerance in millimeters. Default 1.")] double toleranceMm = ClashOneVsRestPlanner.DefaultToleranceMm,
        [Description("Optional per-pair profile: [{nameMask, testType, toleranceMm}]; the first mask matching side B's name wins.")] List<ClashOneVsRestOverride> pairOverrides = null,
        [Description("Skip siblings whose bounding box does not reach the target's (within the tolerance). Off by default; skipped names are listed.")] bool bboxPrefilter = false,
        [Description("Replace same-name Selection Sets and tests. Default false reports them as conflicts.")] bool overwriteExisting = false,
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
