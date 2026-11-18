using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashTestTools : NavisworksToolBase
{
    private const int BackgroundHostTimeoutMs = 1800000;
    private readonly HostBackgroundJobStore _jobs;

    public NavisworksClashTestTools(NavisworksToolContext context, HostBackgroundJobStore jobs)
        : base(context)
    {
        _jobs = jobs;
    }

    [McpServerTool]
    [Description("Manages tests by name/handle/prefix/first-N. Dry-run; apply=true mutates. run executes only: no model save, reports, screenshots or viewpoints. Handles: clash_list_tests.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public async Task<ClashManageTestsResponse> ClashManageTests(
        [Description("run, reset, compact, rename, rename_batch, delete, move, sort or set_settings; missing/invalid never runs tests.")] string operation,
        [Description("False previews; true applies.")] bool apply = false,
        [Description("Test name: exact match, else contains.")] string testName = "",
        [Description("Test names: exact match per name, else contains.")] List<string> testNames = null,
        [Description("clash_list_tests handles.")] List<string> testHandles = null,
        [Description("Test-name prefix scope.")] string namePrefix = "",
        [Description("First N matches, globally if unscoped. Applied delete/reset requires another scope, e.g. namePrefix.")] int firstN = 0,
        [Description("rename: new name; exactly one matched test.")] string newName = "",
        [Description("rename_batch: {testHandle,newName}; all validated before mutation.")] List<ClashTestRenameRequest> renames = null,
        [Description("rename_batch: {index}, {name}; {aName}/{bName}/{aCode}/{bCode} from the current test name A vs B. Transforms: zeroPad:N, strip:#regex#, replace:#regex#replacement#, upper, lower.")] string namePattern = "",
        [Description("namePattern initial {index}. Default is 1.")] int renameStartIndex = 1,
        [Description("Number from scope end; keep test order.")] bool renameFromEnd = false,
        [Description("move: 1-based index in current folder/root; exactly one test.")] int targetIndex = 0,
        [Description("sort: asc/name/natural/desc within each folder/root.")] string sortDirection = "asc",
        [Description("set_settings tolerance, mm; -1=unchanged.")] double toleranceMm = -1,
        [Description("set_settings: hard/intersection, conservative/hard_conservative, clearance, duplicate; empty=unchanged.")] string testType = "",
        [Description("Result-count scope; 0 selects empty tests, -1 disables.")] int onlyWithTotal = -1,
        [Description("Per-test rows, problems first; 0 = counters only. Default is 25.")] int maxTestRows = ClashTestRowLimit.DefaultMaxRows,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        var response = await _hostBridgeClient.ClashManageTestsAsync(new ClashManageTestsRequest
        {
            Apply = apply,
            Operation = operation,
            TestName = testName,
            TestNames = testNames ?? new List<string>(),
            TestHandles = testHandles ?? new List<string>(),
            NamePrefix = namePrefix,
            FirstN = firstN > 0 ? firstN : null,
            NewName = newName,
            Renames = renames ?? new List<ClashTestRenameRequest>(),
            TargetIndex = targetIndex > 0 ? targetIndex : null,
            SortDirection = sortDirection,
            ToleranceMm = toleranceMm >= 0 ? toleranceMm : null,
            TestType = testType,
            OnlyWithTotal = onlyWithTotal >= 0 ? onlyWithTotal : null,
            NamePattern = namePattern,
            RenameStartIndex = renameStartIndex,
            RenameFromEnd = renameFromEnd,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion)).ConfigureAwait(false);
        ClashTestRowLimit.Apply(response, maxTestRows);
        return response;
    }
    [McpServerTool]
    [Description("Plans BBox-intersecting root/selection pairs; never mutates the document or creates/runs tests. Dry-run reports requested/matched/unmatched rootNames, no write. apply=true needs absolute outputPath; atomic verified write.")]
    [ToolCapabilities(ToolEffects.Files, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashBboxPairPlanResponse> ClashBboxPairPlan(
        [Description("False previews; true writes and verifies outputPath.")] bool apply = false,
        [Description("top_level_files: roots/direct children, as list_root_items.")] string rootMode = "top_level_files",
        [Description("top_level_files or selection (current items/groups, nested disciplines too).")] string sourceMode = "top_level_files",
        [Description("Exact root names/paths/source files to include; empty = all matched roots.")] List<string> rootNames = null,
        [Description("Root name/path/source-file contains filter.")] string nameContains = "",
        [Description("Exclude roots by name/path/source-file contains filters.")] List<string> excludeNameContains = null,
        [Description("Exact root name/path/source file; only its pairs against included roots.")] string targetRootName = "",
        [Description("BBox depth: 0=root, 1=children, 2=intersecting child-pair grandchildren.")] int refineDepth = 1,
        [Description("BBox tolerance/expansion in millimeters. Default is 0.")] double bboxToleranceMm = 0,
        [Description("Maximum root items, up to 2000. Default is 500.")] int maxRootItems = 500,
        [Description("Candidate-pair cap before stopping.")] int maxCandidatePairs = 50000,
        [Description("Inline roots/candidates/rejected-pair cap; outputPath=full plan.")] int previewLimit = 200,
        [Description("Preview rejected pairs with reasons. Default is false.")] bool includeRejected = false,
        [Description("Absolute JSON/CSV full plan. Dry-run: calculatedOutputPath only. JSON feeds clash_pair_tests_create.planOutputPath.")] string outputPath = "",
        [Description("Replace outputPath on apply; store only for reviewed exactReplay.")] bool overwriteExisting = false,
        [Description("Background run; see background_job_status.")] bool background = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        var request = new ClashBboxPairPlanRequest
        {
            Apply = apply,
            RootMode = rootMode,
            SourceMode = sourceMode,
            RootNames = rootNames ?? new List<string>(),
            NameContains = nameContains,
            ExcludeNameContains = excludeNameContains ?? new List<string>(),
            TargetRootName = targetRootName,
            RefineDepth = refineDepth,
            BboxToleranceMm = bboxToleranceMm,
            MaxRootItems = maxRootItems,
            MaxCandidatePairs = maxCandidatePairs,
            PreviewLimit = previewLimit,
            IncludeRejected = includeRejected,
            OutputPath = outputPath,
            OverwriteExisting = overwriteExisting,
        };
        var target = CreateTarget(instanceId, navisworksVersion);
        if (background)
        {
            var started = _jobs.Start("clash_bbox_pair_plan", async token =>
                (object)await _hostBridgeClient.ClashBboxPairPlanAsync(request, token, target, BackgroundHostTimeoutMs));
            return Task.FromResult(new ClashBboxPairPlanResponse
            {
                BackgroundOperationId = started.OperationId, Applied = false, Warnings = { BackgroundStartReply.Message(started.OperationId) },
            });
        }
        return _hostBridgeClient.ClashBboxPairPlanAsync(request, cancellationToken, target);
    }

    [McpServerTool]
    [Description("Creates BBox-pair tests; dry-run, never runs. Side resolution: exact path, unique exact root name, unique exact source-file identity; reject ambiguity. Set sides: clash_tests_from_sets/clash_batchtest_import.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public async Task<ClashPairTestsCreateResponse> ClashPairTestsCreate(
        [Description("False previews; true creates tests.")] bool apply = false,
        [Description("clash_bbox_pair_plan pairs; large plans: planOutputPath.")] List<ClashBboxCandidatePair> pairs = null,
        [Description("clash_bbox_pair_plan JSON path.")] string planOutputPath = "",
        [Description("Stable test name prefix. Default is NH-BBOX.")] string testNamePrefix = "NH-BBOX",
        [Description("First N candidate pairs. Default is 200.")] int limit = 200,
        [Description("Tolerance, mm; -1=settingsFromTestName or host default.")] double toleranceMm = -1,
        [Description("hard/intersection, conservative/hard_conservative, clearance, duplicate; empty=settingsFromTestName or host default.")] string testType = "",
        [Description("Type/tolerance source test when unspecified.")] string settingsFromTestName = "",
        [Description("Replace same-name tests. Default is false.")] bool overwriteExisting = false,
        [Description("Per-test rows, problems first; 0 = counters only. Default is 25.")] int maxTestRows = ClashTestRowLimit.DefaultMaxRows,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        var response = await _hostBridgeClient.ClashPairTestsCreateAsync(new ClashPairTestsCreateRequest
        {
            Apply = apply,
            Pairs = pairs ?? new List<ClashBboxCandidatePair>(),
            PlanOutputPath = planOutputPath,
            TestNamePrefix = testNamePrefix,
            Limit = limit,
            ToleranceMm = toleranceMm >= 0 ? toleranceMm : null,
            TestType = testType,
            OverwriteExisting = overwriteExisting,
            SettingsFromTestName = settingsFromTestName,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion)).ConfigureAwait(false);
        ClashTestRowLimit.Apply(response, maxTestRows);
        return response;
    }

    [McpServerTool]
    [Description("Creates selection/explicit-item matrix tests (i<j, no self-clash). Dry-run. Prefix only if supplied or useGeneratedPrefix=true.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashCreateMatrixFromSelectionResponse> ClashCreateMatrixFromSelection(
        [Description("False previews; true creates matrix tests.")] bool apply = false,
        [Description("Prefix; empty=none unless useGeneratedPrefix=true. yyyyMMdd_HHmmss=current timestamp.")] string namePrefix = "",
        [Description("Use '[NH-MATRIX] yyyyMMdd_HHmmss ' when namePrefix is empty.")] bool useGeneratedPrefix = false,
        [Description("Tolerance, mm; -1=host default.")] double toleranceMm = -1,
        [Description("hard/intersection, conservative/hard_conservative, clearance, duplicate.")] string testType = "hard",
        [Description("Run only the newly created tests after creation. Default is false.")] bool runAfterCreate = false,
        [Description("On apply, delete previously generated tests (never in dry-run). Without pairNameTemplate names must start with the prefix; with it, namePrefix is a containment key (a number may precede it).")] bool removePreviousGenerated = false,
        [Description("Exact names/paths/source filenames of any model objects instead of selection; empty=selection.")] List<string> matrixItemNames = null,
        [Description("Use tree items matching name/path/source-file contains filter instead of selection.")] string matrixNameContains = "",
        [Description("Exclude matrix items by name/path/source-file contains filters.")] List<string> matrixExcludeNameContains = null,
        [Description("Selected-item cap; max 1000.")] int maxSelectedItems = 100,
        [Description("Required above 300 matrix pairs.")] bool confirmLargeMatrix = false,
        [Description("Return selected item names and planned/created pair names.")] bool includePairNames = true,
        [Description("{index}, {aName}, {bName}, {aCode}, {bCode}; transforms: zeroPad:N, strip:#regex#, replace:#regex#replacement#, upper, lower.")] string pairNameTemplate = "",
        [Description("First index used by pairNameTemplate. Default is 1.")] int pairNameStartIndex = 1,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashCreateMatrixFromSelectionAsync(new ClashCreateMatrixFromSelectionRequest
        {
            Apply = apply,
            NamePrefix = namePrefix,
            UseGeneratedPrefix = useGeneratedPrefix,
            ToleranceMm = toleranceMm >= 0 ? toleranceMm : null,
            TestType = testType,
            RunAfterCreate = runAfterCreate,
            RemovePreviousGenerated = removePreviousGenerated,
            MatrixItemNames = matrixItemNames ?? new List<string>(),
            MatrixNameContains = matrixNameContains,
            MatrixExcludeNameContains = matrixExcludeNameContains ?? new List<string>(),
            MaxSelectedItems = maxSelectedItems,
            ConfirmLargeMatrix = confirmLargeMatrix,
            IncludePairNames = includePairNames,
            PairNameTemplate = pairNameTemplate,
            PairNameStartIndex = pairNameStartIndex,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

}
