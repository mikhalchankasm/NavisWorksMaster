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
    [Description("Runs, deletes, renames, reorders, sorts, or edits selected Clash Detective tests by name, handle, prefix, or first-N scope. Defaults to dry-run; pass apply=true for run/reset/compact/rename/delete/move/sort/set_settings. operation=run only executes tests; it does not save the model, create reports, screenshots, or viewpoints. Use clash_list_tests first to get testHandles.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashManageTestsResponse> ClashManageTests(
        [Description("Required operation: run, reset, compact, rename, rename_batch, delete, move, sort, or set_settings. There is no default because an omitted or misspelled operation must never run tests.")] string operation,
        [Description("False previews matched tests and operation, true applies the operation. Default is false/dry-run.")] bool apply = false,
        [Description("Optional single test name. Exact match is preferred; otherwise contains-match is used.")] string testName = "",
        [Description("Optional list of test names. Exact match is preferred per name, otherwise contains-match is used.")] List<string> testNames = null,
        [Description("Optional list of handles from clash_list_tests, for example clash-test:1.")] List<string> testHandles = null,
        [Description("Optional test name prefix scope, useful for generated tests such as NH-BBOX.")] string namePrefix = "",
        [Description("Optional first N tests from the matched scope; if no other scope is provided, selects the first N tests globally for dry-run/run/compact/settings. delete/reset apply=true require another scope such as namePrefix.")] int firstN = 0,
        [Description("New test name for operation=rename. Rename requires exactly one matched test.")] string newName = "",
        [Description("Batch rename plan for operation=rename_batch. Every entry requires testHandle and newName; the full plan is validated before mutation.")] List<ClashTestRenameRequest> renames = null,
        [Description("Handle-free rename_batch template for the matched scope. Tokens: {index}, {name}, {aName}, {bName}, {aCode}, {bCode}; pair tokens support zeroPad:N, strip:#regex#, replace:#regex#replacement#, upper, lower.")] string namePattern = "",
        [Description("First number used by namePattern {index}. Default is 1.")] int renameStartIndex = 1,
        [Description("Number the matched scope from its end while retaining current test order. Useful after deleting empty tests. Default is false.")] bool renameFromEnd = false,
        [Description("1-based destination index for operation=move. Move requires exactly one matched test and moves it within its current Clash Detective folder/root.")] int targetIndex = 0,
        [Description("Sort direction for operation=sort: asc/name/natural or desc. Default is asc. Sorting reorders only the matched tests within each current Clash Detective folder/root.")] string sortDirection = "asc",
        [Description("New Clash Detective tolerance in millimeters for operation=set_settings. Use -1 to leave unchanged. Example: 1 means 1 mm.")] double toleranceMm = -1,
        [Description("New Clash Detective test type for operation=set_settings: hard/intersection, conservative/hard_conservative, clearance, or duplicate. Empty leaves unchanged.")] string testType = "",
        [Description("Optional result-count scope. Example onlyWithTotal=0 with namePrefix selects only empty tests without storing handles.")] int onlyWithTotal = -1,
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
    [Description("Plans candidate Clash Detective group pairs by intersecting bounding boxes from top-level roots or the current arbitrary selection. Does not mutate the document. Dry-run never writes outputPath and returns outputWritten=false plus requested/matched/unmatched rootNames. apply=true requires an absolute outputPath and returns only after verified atomic write. Never creates or runs tests.")]
    [ToolCapabilities(ToolEffects.Files, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashBboxPairPlanResponse> ClashBboxPairPlan(
        [Description("False previews only and never writes a file. True requires outputPath and writes/verifies the full artifact. Default false.")] bool apply = false,
        [Description("Root mode. Currently top_level_files uses each model root and its direct children, matching list_root_items.")] string rootMode = "top_level_files",
        [Description("Candidate source: top_level_files (default) or selection. selection evaluates the current selected groups/items and supports nested discipline groups.")] string sourceMode = "top_level_files",
        [Description("Optional exact root names/paths/source files to include. Empty means all matched roots.")] List<string> rootNames = null,
        [Description("Optional contains filter applied to root name, path, or source file.")] string nameContains = "",
        [Description("Optional contains filters to exclude roots by name, path, or source file.")] List<string> excludeNameContains = null,
        [Description("Optional exact target root name/path/source file. When set, returns only pairs containing that root while still comparing it against all included roots.")] string targetRootName = "",
        [Description("BBox refinement depth: 0=root only, 1=children, 2=grandchildren for intersecting child pairs. Default is 1.")] int refineDepth = 1,
        [Description("BBox tolerance/expansion in millimeters. Default is 0.")] double bboxToleranceMm = 0,
        [Description("Maximum root items to evaluate. Default is 500, maximum is 2000.")] int maxRootItems = 500,
        [Description("Maximum candidate pairs before stopping. Default is 50000.")] int maxCandidatePairs = 50000,
        [Description("Maximum roots/candidates/rejected pairs returned inline. Full output can be written with outputPath. Default is 200.")] int previewLimit = 200,
        [Description("Include skipped/rejected pair preview rows with reasons. Default is false.")] bool includeRejected = false,
        [Description("Optional exact absolute JSON or CSV output path for the full plan. In dry-run it is returned only as calculatedOutputPath. JSON can feed clash_pair_tests_create.planOutputPath.")] string outputPath = "",
        [Description("Replace an existing outputPath when apply=true. Default is false. Store only in an explicitly reviewed exactReplay scenario.")] bool overwriteExisting = false,
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
    [Description("Creates root/BBox-oriented Clash Detective tests from bbox candidate pairs. Each side resolves by exact full path, then unique exact root display name, then unique exact source-file identity; ambiguity is never resolved by choosing the first match. Use clash_tests_from_sets or clash_batchtest_import for Selection Set/Search Set sides. Dry-run by default and never runs tests.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashPairTestsCreateResponse> ClashPairTestsCreate(
        [Description("False previews test creation, true creates Clash Detective tests. Default is false/dry-run.")] bool apply = false,
        [Description("Candidate pairs returned by clash_bbox_pair_plan. Usually pass planOutputPath instead for large plans.")] List<ClashBboxCandidatePair> pairs = null,
        [Description("Path to a JSON output from clash_bbox_pair_plan.")] string planOutputPath = "",
        [Description("Stable test name prefix. Default is NH-BBOX.")] string testNamePrefix = "NH-BBOX",
        [Description("Create or preview only the first N candidate pairs. Default is 200.")] int limit = 200,
        [Description("Clash Detective tolerance in millimeters. Use -1 to use settingsFromTestName or Navisworks default.")] double toleranceMm = -1,
        [Description("Clash Detective test type: hard/intersection, conservative/hard_conservative, clearance, or duplicate. Empty uses settingsFromTestName or Navisworks default.")] string testType = "",
        [Description("Optional existing Clash Test name whose type and tolerance are copied when the corresponding explicit arguments are omitted.")] string settingsFromTestName = "",
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
    [Description("Creates Clash Detective matrix tests from the current Navisworks selection or explicit matrix items: every item against every other item (i<j), with no self-clash. Defaults to dry-run and no generated name prefix unless useGeneratedPrefix=true.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashCreateMatrixFromSelectionResponse> ClashCreateMatrixFromSelection(
        [Description("False previews matrix test creation, true creates Clash Detective tests. Default is false/dry-run.")] bool apply = false,
        [Description("Test name prefix. Empty string means no prefix unless useGeneratedPrefix=true. The yyyyMMdd_HHmmss token is replaced by the current timestamp.")] string namePrefix = "",
        [Description("When true and namePrefix is empty, use the generated '[NH-MATRIX] yyyyMMdd_HHmmss ' prefix. Default is false so clean names are created by default.")] bool useGeneratedPrefix = false,
        [Description("Optional Clash Detective tolerance in millimeters. Use -1 to leave Navisworks default. Example: 10 means 10 mm.")] double toleranceMm = -1,
        [Description("Clash Detective test type: hard/intersection, conservative/hard_conservative, clearance, or duplicate. Default is hard.")] string testType = "hard",
        [Description("Run only the newly created tests after creation. Default is false.")] bool runAfterCreate = false,
        [Description("Delete previous generated tests before creating the new matrix. Without pairNameTemplate, names must start with the generated/custom prefix. With pairNameTemplate, custom namePrefix is the reviewed containment key because numbering may precede it. Requires apply=true; dry-run never deletes. Default is false.")] bool removePreviousGenerated = false,
        [Description("Optional exact item names, paths, or source filenames to use as matrix items instead of the current Navisworks selection. Matches can be any model objects, not only currently selected items. Empty means use current selection.")] List<string> matrixItemNames = null,
        [Description("Optional contains filter for item name, path, or source file. When set, matching model-tree items are used instead of current selection.")] string matrixNameContains = "",
        [Description("Optional contains filters to exclude matrix items by name, path, or source file.")] List<string> matrixExcludeNameContains = null,
        [Description("Maximum selected items allowed. Default is 100, maximum is 1000.")] int maxSelectedItems = 100,
        [Description("Required when the matrix exceeds the large threshold, currently 300 pairs. Default is false.")] bool confirmLargeMatrix = false,
        [Description("Include selected item names and planned/created pair names in the response. Default is true.")] bool includePairNames = true,
        [Description("Optional pair-name template. Tokens: {index}, {aName}, {bName}, {aCode}, {bCode}. Transforms: zeroPad:N, strip:#regex#, replace:#regex#replacement#, upper, lower. Example: {index|zeroPad:3} PROJECT {aCode}-{bCode}.")] string pairNameTemplate = "",
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
