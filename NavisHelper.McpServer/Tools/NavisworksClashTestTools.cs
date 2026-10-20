using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashTestTools : NavisworksToolBase
{
    public NavisworksClashTestTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Manages tests by name/handle/prefix/first-N. Dry-run; apply=true mutates. run executes only: no model save, reports, screenshots or viewpoints. Handles: clash_list_tests.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashManageTestsResponse> ClashManageTests(
        [Description("Required: run, reset, compact, rename, rename_batch, delete, move, sort, set_settings. Missing/invalid never runs tests.")] string operation,
        [Description("False previews matched tests/operation; true applies.")] bool apply = false,
        [Description("Test name: exact match, else contains.")] string testName = "",
        [Description("Test names: exact match per name, else contains.")] List<string> testNames = null,
        [Description("Optional list of handles from clash_list_tests, for example clash-test:1.")] List<string> testHandles = null,
        [Description("Optional test name prefix scope, useful for generated tests such as NH-BBOX.")] string namePrefix = "",
        [Description("First N matches; unscoped: first N globally for preview/run/compact/settings. Applied delete/reset needs another scope, e.g. namePrefix.")] int firstN = 0,
        [Description("New test name for operation=rename. Rename requires exactly one matched test.")] string newName = "",
        [Description("rename_batch: {testHandle,newName} entries; validates all before mutation.")] List<ClashTestRenameRequest> renames = null,
        [Description("rename_batch template: {index}, {name}, {aName}, {bName}, {aCode}, {bCode}. Pair transforms: zeroPad:N, strip:#regex#, replace:#regex#replacement#, upper, lower.")] string namePattern = "",
        [Description("First number used by namePattern {index}. Default is 1.")] int renameStartIndex = 1,
        [Description("Number from scope end, keeping current test order.")] bool renameFromEnd = false,
        [Description("move: 1-based index in current folder/root; exactly one test.")] int targetIndex = 0,
        [Description("sort: asc/name/natural or desc; matched tests within each folder/root.")] string sortDirection = "asc",
        [Description("set_settings tolerance, mm; -1=unchanged.")] double toleranceMm = -1,
        [Description("set_settings: hard/intersection, conservative/hard_conservative, clearance, duplicate; empty=unchanged.")] string testType = "",
        [Description("Result-count scope; 0 selects empty tests, -1 disables.")] int onlyWithTotal = -1,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashManageTestsAsync(new ClashManageTestsRequest
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
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
    [McpServerTool]
    [Description("Plans BBox-intersecting root/selection pairs; no document mutation or test creation/run. Dry-run: no write, outputWritten=false, requested/matched/unmatched rootNames. apply=true requires absolute outputPath; verifies atomic write.")]
    [ToolCapabilities(ToolEffects.Files, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashBboxPairPlanResponse> ClashBboxPairPlan(
        [Description("False previews, never writes; true writes/verifies required outputPath.")] bool apply = false,
        [Description("top_level_files: roots/direct children, as list_root_items.")] string rootMode = "top_level_files",
        [Description("top_level_files or selection (current items/groups, including nested disciplines).")] string sourceMode = "top_level_files",
        [Description("Optional exact root names/paths/source files to include. Empty means all matched roots.")] List<string> rootNames = null,
        [Description("Root name/path/source-file contains filter.")] string nameContains = "",
        [Description("Exclude roots by name/path/source-file contains filters.")] List<string> excludeNameContains = null,
        [Description("Exact root name/path/source file; only its pairs against included roots.")] string targetRootName = "",
        [Description("BBox depth: 0=root, 1=children, 2=intersecting child-pair grandchildren.")] int refineDepth = 1,
        [Description("BBox tolerance/expansion in millimeters. Default is 0.")] double bboxToleranceMm = 0,
        [Description("Maximum root items to evaluate. Default is 500, maximum is 2000.")] int maxRootItems = 500,
        [Description("Candidate-pair cap before stopping.")] int maxCandidatePairs = 50000,
        [Description("Inline roots/candidates/rejected-pair cap; outputPath=full plan.")] int previewLimit = 200,
        [Description("Include skipped/rejected pair preview rows with reasons. Default is false.")] bool includeRejected = false,
        [Description("Absolute JSON/CSV full plan. Dry-run: calculatedOutputPath only. JSON feeds clash_pair_tests_create.planOutputPath.")] string outputPath = "",
        [Description("Replace outputPath on apply; store only for reviewed exactReplay.")] bool overwriteExisting = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashBboxPairPlanAsync(new ClashBboxPairPlanRequest
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
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Creates BBox-pair tests; dry-run, never runs. Side resolution: exact path, unique exact root name, unique exact source-file identity; reject ambiguity. Set sides: clash_tests_from_sets/clash_batchtest_import.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashPairTestsCreateResponse> ClashPairTestsCreate(
        [Description("False previews; true creates tests.")] bool apply = false,
        [Description("clash_bbox_pair_plan pairs; large plans: planOutputPath.")] List<ClashBboxCandidatePair> pairs = null,
        [Description("Path to a JSON output from clash_bbox_pair_plan.")] string planOutputPath = "",
        [Description("Stable test name prefix. Default is NH-BBOX.")] string testNamePrefix = "NH-BBOX",
        [Description("Create or preview only the first N candidate pairs. Default is 200.")] int limit = 200,
        [Description("Tolerance, mm; -1=settingsFromTestName or host default.")] double toleranceMm = -1,
        [Description("hard/intersection, conservative/hard_conservative, clearance, duplicate; empty=settingsFromTestName or host default.")] string testType = "",
        [Description("Existing test supplying type/tolerance when not explicit.")] string settingsFromTestName = "",
        [Description("Replace existing tests with the same generated names. Default is false.")] bool overwriteExisting = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashPairTestsCreateAsync(new ClashPairTestsCreateRequest
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
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
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
        [Description("Delete previous generated tests on apply; dry-run never deletes. Without pairNameTemplate: names must start with generated/custom prefix. With template: custom namePrefix is reviewed containment key (number may precede it).")] bool removePreviousGenerated = false,
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
