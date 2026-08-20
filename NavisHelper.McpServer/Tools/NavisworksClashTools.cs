using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashTools : NavisworksToolBase
{
    public NavisworksClashTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Lists Clash Detective tests in the active Navisworks document and returns per-test clash counts. Read-only.")]
    public Task<ClashListTestsResponse> ClashListTests(
        [Description("Maximum number of tests to return. Default is 200, maximum is 10000.")] int limit = 200,
        [Description("Zero-based offset into the filtered test list. Default is 0.")] int offset = 0,
        [Description("Optional case-insensitive test-name prefix filter.")] string namePrefix = "",
        [Description("Optional case-insensitive test-name contains filter.")] string nameContains = "",
        [Description("Include detailed status counts by Navisworks clash result status. Default is true.")] bool includeStatusCounts = true,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashListTestsAsync(new ClashListTestsRequest
        {
            Limit = limit,
            Offset = offset,
            NamePrefix = namePrefix,
            NameContains = nameContains,
            IncludeStatusCounts = includeStatusCounts,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Lists Clash Detective results from all tests or a named test. Read-only. Use after clash_list_tests to inspect statuses, assignees, and clashing item names.")]
    public Task<ClashListResultsResponse> ClashListResults(
        [Description("Optional test name. Empty means all tests. Exact match is preferred; otherwise contains-match is used.")] string testName = "",
        [Description("Maximum number of result rows to return. Default is 500, maximum is 50000.")] int limit = 500,
        [Description("Optional clash status filters, for example New, Active, Reviewed, Approved, Resolved. Exact case-insensitive match.")] List<string> statusFilters = null,
        [Description("Include all clash statuses and ignore statusFilters. Default is false; with empty statusFilters, all statuses are returned for backward compatibility.")] bool includeAllStatuses = false,
        [Description("Zero-based offset into the sorted, filtered clash result set. Use with nextResultOffset/hasMoreResults for paging. Default is 0.")] int resultOffset = 0,
        [Description("Include Item1/Item2 display names. Default is true.")] bool includeItemNames = true,
        [Description("Include AssignedTo. Default is true.")] bool includeAssignedTo = true,
        [Description("Include results marked by NavisHelper ignore rules. Default is false.")] bool includeIgnored = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashListResultsAsync(new ClashListResultsRequest
        {
            TestName = testName,
            Limit = limit,
            StatusFilters = statusFilters ?? new List<string>(),
            IncludeAllStatuses = includeAllStatuses,
            ResultOffset = resultOffset,
            IncludeItemNames = includeItemNames,
            IncludeAssignedTo = includeAssignedTo,
            IncludeIgnored = includeIgnored,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Groups existing Clash Detective results into read-only clusters. Default groupMode=hybrid first groups by associated object pair, then splits by clash-point proximity; this does not require reliable discipline/architecture classification. Use to collapse many raw clashes into practical problem zones such as pump building vs pump pipelines. Does not mutate the document.")]
    public Task<ClashListClustersResponse> ClashListClusters(
        [Description("Optional test name. Empty means all tests. Exact match is preferred; otherwise contains-match is used.")] string testName = "",
        [Description("Optional list of test names. Empty with testName empty means all tests.")] List<string> testNames = null,
        [Description("Optional clash status filters. Default when omitted is New and Active. Use includeAllStatuses=true for all statuses.")] List<string> statusFilters = null,
        [Description("Include all clash statuses instead of the default New/Active filter. Default is false.")] bool includeAllStatuses = false,
        [Description("Grouping mode: hybrid (default), object_pair, or spatial. hybrid groups by associated object pair and then splits by distance.")] string groupMode = "hybrid",
        [Description("Maximum distance in millimeters for spatial splitting/grouping. Default is 300.")] double clusterDistanceMm = 300,
        [Description("Maximum clusters to return. Default is 100, maximum is 5000.")] int limit = 100,
        [Description("Zero-based offset into the sorted cluster list. Use with nextResultOffset/hasMoreClusters for paging. Default is 0.")] int resultOffset = 0,
        [Description("Maximum raw clash preview rows returned per cluster. Default is 5, maximum is 50.")] int previewRowsPerCluster = 5,
        [Description("Maximum filtered raw clash results to analyze before truncating. Default is 500, maximum is 50000.")] int maxResults = 500,
        [Description("Optional text filters. If either clashing side name/path contains any text, that raw clash is excluded before clustering. Example: Weld.")] List<string> excludeItemNameContains = null,
        [Description("Response detail: compact (default) omits long item paths from previewRows; full includes them.")] string verbosity = "compact",
        [Description("Include results marked by NavisHelper ignore rules. Default is false.")] bool includeIgnored = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashListClustersAsync(new ClashListClustersRequest
        {
            TestName = testName,
            TestNames = testNames ?? new List<string>(),
            StatusFilters = statusFilters ?? new List<string>(),
            IncludeAllStatuses = includeAllStatuses,
            GroupMode = groupMode,
            ClusterDistanceMm = clusterDistanceMm,
            Limit = limit,
            ResultOffset = resultOffset,
            PreviewRowsPerCluster = previewRowsPerCluster,
            MaxResults = maxResults,
            ExcludeItemNameContains = excludeItemNameContains ?? new List<string>(),
            Verbosity = verbosity,
            IncludeIgnored = includeIgnored,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Creates real Clash Detective result groups from existing clash results by formula. The groups array is paged and reports plannedGroupCount, returnedGroupCount, groupsTruncated, and nextGroupOffset. Defaults to dry-run.")]
    public Task<ClashGroupResultsResponse> ClashGroupResults(
        [Description("False previews planned ClashResultGroup folders, true creates/updates real groups in Clash Detective. Default is false/dry-run.")] bool apply = false,
        [Description("Required test name. Exact match is preferred; otherwise contains-match is used.")] string testName = "",
        [Description("Optional list of test names. Use when grouping several explicit tests.")] List<string> testNames = null,
        [Description("Optional test handles from clash_list_tests, for example clash-test:3.")] List<string> testHandles = null,
        [Description("Optional clash status filters. Default when omitted is New and Active. Use includeAllStatuses=true for all statuses.")] List<string> statusFilters = null,
        [Description("Include all clash statuses instead of the default New/Active filter. Default is false.")] bool includeAllStatuses = false,
        [Description("Clash side used for grouping: A or B. Default is B.")] string groupBySide = "B",
        [Description("Grouping boundary: ancestor (default), root (the appended model root), or source_file. root is independent of model-tree depth.")] string groupBy = "ancestor",
        [Description("How many model-tree parent levels to move upward from the clashing side item before grouping. Default is 1. Use 0 for the exact clashing item.")] int ancestorLevelsUp = 1,
        [Description("Group naming/grouping mode: owner_name (default) or owner_path. owner_path avoids merging owners with the same visible name.")] string groupNameMode = "owner_name",
        [Description("Optional prefix added to each generated ClashResultGroup name.")] string groupNamePrefix = "",
        [Description("Append NavisHelper side tag [NH:A]/[NH:B] so the UI can detect the grouping side. Default is true.")] bool includeNavisHelperSideTag = true,
        [Description("When false, existing group-name conflicts are skipped. When true, matching existing groups are rebuilt to the new target results. Default is false.")] bool overwriteExisting = false,
        [Description("Before applying, ungroup existing NavisHelper groups for the selected side and optional prefix. Use carefully. Default is false.")] bool ungroupExistingFirst = false,
        [Description("Minimum clash result count required to create a group. Default is 2.")] int minGroupSize = 2,
        [Description("Maximum filtered raw results to analyze before truncating. Default is 500, maximum is 50000.")] int maxResults = 500,
        [Description("Preview rows per planned group. Default is 5, maximum is 50.")] int previewRowsPerGroup = 5,
        [Description("Zero-based offset in the planned groups array. Default is 0.")] int groupOffset = 0,
        [Description("Maximum groups returned in this page. Default is 500, maximum is 5000.")] int groupLimit = 500,
        [Description("Return group aggregates without preview rows. Recommended for large matrices.")] bool aggregateOnly = false,
        [Description("Required for apply=true when the grouping scope exceeds 1000 results. The agent should ask the user before setting this true. Default is false.")] bool confirmLargeGrouping = false,
        [Description("Optional text filters. If either clashing item name/path contains any text, that clash is excluded before grouping.")] List<string> excludeItemNameContains = null,
        [Description("Response detail: compact (default) omits long item paths from previewRows; full includes them.")] string verbosity = "compact",
        [Description("Include results marked by NavisHelper ignore rules. Default is false.")] bool includeIgnored = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashGroupResultsAsync(new ClashGroupResultsRequest
        {
            Apply = apply,
            TestName = testName,
            TestNames = testNames ?? new List<string>(),
            TestHandles = testHandles ?? new List<string>(),
            StatusFilters = statusFilters ?? new List<string>(),
            IncludeAllStatuses = includeAllStatuses,
            GroupBySide = groupBySide,
            GroupBy = groupBy,
            AncestorLevelsUp = ancestorLevelsUp,
            GroupNameMode = groupNameMode,
            GroupNamePrefix = groupNamePrefix,
            IncludeNavisHelperSideTag = includeNavisHelperSideTag,
            OverwriteExisting = overwriteExisting,
            UngroupExistingFirst = ungroupExistingFirst,
            MinGroupSize = minGroupSize,
            MaxResults = maxResults,
            PreviewRowsPerGroup = previewRowsPerGroup,
            GroupOffset = groupOffset,
            GroupLimit = groupLimit,
            AggregateOnly = aggregateOnly,
            ConfirmLargeGrouping = confirmLargeGrouping,
            ExcludeItemNameContains = excludeItemNameContains ?? new List<string>(),
            Verbosity = verbosity,
            IncludeIgnored = includeIgnored,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Creates or rebuilds one real ClashResultGroup from explicit result handles. All handles are validated before mutation. Dry-run by default.")]
    public Task<ClashGroupCustomResponse> ClashGroupCustom(
        [Description("Required test handle from clash_list_tests.")] string testHandle,
        [Description("Required result handles from clash_list_results.")] List<string> resultHandles,
        [Description("Name of the target ClashResultGroup.")] string groupName,
        [Description("False previews, true mutates the document. Default is false.")] bool apply = false,
        [Description("Rebuild an existing same-name group. Default is false.")] bool overwriteExisting = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashGroupCustomAsync(new ClashGroupCustomRequest
        {
            TestHandle = testHandle,
            ResultHandles = resultHandles ?? new List<string>(),
            GroupName = groupName,
            Apply = apply,
            OverwriteExisting = overwriteExisting,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Ungroups explicit ClashResultGroup handles or groups matching a name prefix within one test. Dry-run by default.")]
    public Task<ClashUngroupResponse> ClashUngroup(
        [Description("Required test handle from clash_list_tests.")] string testHandle,
        [Description("Optional explicit group handles returned by clash_list_results/grouping tools.")] List<string> groupHandles = null,
        [Description("Optional group-name prefix used when groupHandles is empty.")] string groupNamePrefix = "",
        [Description("False previews, true mutates the document. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashUngroupAsync(new ClashUngroupRequest
        {
            TestHandle = testHandle,
            GroupHandles = groupHandles ?? new List<string>(),
            GroupNamePrefix = groupNamePrefix,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Sets status on explicit results, all results in groups, or whole tests. Group/test scopes cascade to individual results. Dry-run by default.")]
    public Task<ClashSetStatusResponse> ClashSetStatus(
        [Description("Required scope: results, group, or test.")] string scope,
        [Description("Target status: New, Active, Reviewed, Approved, or Resolved.")] string status,
        [Description("Test handle required for results/group scope; optional additional test handle for test scope.")] string testHandle = "",
        [Description("Result handles for scope=results.")] List<string> resultHandles = null,
        [Description("Group handles for scope=group.")] List<string> groupHandles = null,
        [Description("Test handles for scope=test.")] List<string> testHandles = null,
        [Description("Optional assignee applied to every affected result.")] string assignedTo = "",
        [Description("Optional comment appended to every affected result.")] string comment = "",
        [Description("False previews, true mutates the document. Default is false.")] bool apply = false,
        [Description("Required for apply=true above 500 results.")] bool confirmLargeStatusChange = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashSetStatusAsync(new ClashSetStatusRequest
        {
            Scope = scope,
            Status = status,
            TestHandle = testHandle,
            ResultHandles = resultHandles ?? new List<string>(),
            GroupHandles = groupHandles ?? new List<string>(),
            TestHandles = testHandles ?? new List<string>(),
            AssignedTo = assignedTo,
            Comment = comment,
            Apply = apply,
            ConfirmLargeStatusChange = confirmLargeStatusChange,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Clusters clash points and writes real ClashResultGroup folders per test. Supports spatial, hybrid, and object_pair modes. Dry-run by default.")]
    public Task<ClashGroupByProximityResponse> ClashGroupByProximity(
        [Description("Optional single test name.")] string testName = "",
        [Description("Optional test names.")] List<string> testNames = null,
        [Description("Optional test handles.")] List<string> testHandles = null,
        [Description("Grouping mode: spatial, hybrid, or object_pair. Default is hybrid.")] string groupMode = "hybrid",
        [Description("Maximum spatial distance in millimeters. Default is 500.")] double clusterDistanceMm = 500,
        [Description("Minimum results per created group. Default is 2.")] int minGroupSize = 2,
        [Description("Template placeholders: index, count, x, y, z, ownerA, ownerB.")] string groupNameTemplate = "Зона {index:D2} ({count} колл.)",
        [Description("Optional group-name prefix.")] string groupNamePrefix = "",
        [Description("Ungroup matching existing prefix groups before apply. Default is false.")] bool ungroupExistingFirst = false,
        [Description("Status filters; default is New and Active.")] List<string> statusFilters = null,
        [Description("Include all statuses. Default is false.")] bool includeAllStatuses = false,
        [Description("Exclude results whose item name/path contains any value.")] List<string> excludeItemNameContains = null,
        [Description("Maximum raw results to analyze. Default is 500.")] int maxResults = 500,
        [Description("Include ignored results. Default is false.")] bool includeIgnored = false,
        [Description("False previews, true mutates the document. Default is false.")] bool apply = false,
        [Description("Required for apply=true above 1000 analyzed results.")] bool confirmLargeGrouping = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashGroupByProximityAsync(new ClashGroupByProximityRequest
        {
            TestName = testName,
            TestNames = testNames ?? new List<string>(),
            TestHandles = testHandles ?? new List<string>(),
            GroupMode = groupMode,
            ClusterDistanceMm = clusterDistanceMm,
            MinGroupSize = minGroupSize,
            GroupNameTemplate = groupNameTemplate,
            GroupNamePrefix = groupNamePrefix,
            UngroupExistingFirst = ungroupExistingFirst,
            StatusFilters = statusFilters ?? new List<string>(),
            IncludeAllStatuses = includeAllStatuses,
            ExcludeItemNameContains = excludeItemNameContains ?? new List<string>(),
            MaxResults = maxResults,
            IncludeIgnored = includeIgnored,
            Apply = apply,
            ConfirmLargeGrouping = confirmLargeGrouping,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Lists, adds, or removes document-persistent clash ignore rules. Added rules approve matching results with a reason comment and are re-applied after test runs. Dry-run for add/remove by default.")]
    public Task<ClashIgnoreRulesResponse> ClashIgnoreRules(
        [Description("Action: list, add, or remove.")] string action,
        [Description("Rule for action=add; remove may also use ruleName.")] ClashIgnoreRule rule = null,
        [Description("Rule name for action=remove.")] string ruleName = "",
        [Description("False previews add/remove; true persists in the Navisworks document. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashIgnoreRulesAsync(new ClashIgnoreRulesRequest
        {
            Action = action,
            Rule = rule,
            RuleName = ruleName,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Exports clash points to CSV or XLSX with global/local coordinates, level assignment, grid cells, and XLSX summary sheets. Dry-run by default.")]
    public Task<ClashExportPointsResponse> ClashExportPoints(
        [Description("Output .csv or .xlsx path.")] string outputPath,
        [Description("Optional test names.")] List<string> testNames = null,
        [Description("Optional test handles.")] List<string> testHandles = null,
        [Description("Building origin in current Navisworks document coordinates.")] Point3Info origin = null,
        [Description("Local coordinate-system rotation relative to global, degrees. Default is 0.")] double rotationDeg = 0,
        [Description("Optional levels with zFrom/zTo in meters relative to origin Z.")] List<ClashExportLevel> levels = null,
        [Description("Plan grid size in meters. Default is 6.")] double gridSizeM = 6,
        [Description("Include ignored clash results. Default is false.")] bool includeIgnored = false,
        [Description("False previews counts/path, true writes the file. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashExportPointsAsync(new ClashExportPointsRequest
        {
            OutputPath = outputPath,
            TestNames = testNames ?? new List<string>(),
            TestHandles = testHandles ?? new List<string>(),
            Origin = origin,
            RotationDeg = rotationDeg,
            Levels = levels ?? new List<ClashExportLevel>(),
            GridSizeM = gridSizeM,
            IncludeIgnored = includeIgnored,
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Renumbers Clash Detective groups and/or individual clash results inside selected tests. Defaults to dry-run and top-level scope, so existing groups and ungrouped results are numbered as the user sees them in the standard form. Pass apply=true and confirmRename=true only after reviewing the plan.")]
    public Task<ClashRenumberResultsResponse> ClashRenumberResults(
        [Description("False previews renames, true renames Clash Detective groups/results. Default is false/dry-run.")] bool apply = false,
        [Description("Required test name unless testNames/testHandles are provided. Exact match is preferred; otherwise contains-match is used.")] string testName = "",
        [Description("Optional list of test names.")] List<string> testNames = null,
        [Description("Optional test handles from clash_list_tests, for example clash-test:3.")] List<string> testHandles = null,
        [Description("Renumber scope: top_level (default) numbers only direct test children: groups and ungrouped results; recursive also numbers nested results/groups inside groups.")] string scope = "top_level",
        [Description("Order for assigning numbers: current (default, Clash Detective tree order) or name (natural display-name order).")] string orderBy = "current",
        [Description("First number. Default is 1.")] int startNumber = 1,
        [Description("Minimum number width with leading zeroes. Default is 4, so 1 becomes 0001. Larger numbers expand naturally.")] int numberWidth = 4,
        [Description("Text between the generated number and the preserved old name. Default is ' - '.")] string separator = " - ",
        [Description("Optional text before the number, for example 'C-'.")] string prefix = "",
        [Description("Optional text after the number, before the old name.")] string suffix = "",
        [Description("When true, keep the old clean name after the generated number. Default is true.")] bool preserveExistingName = true,
        [Description("When true, remove an existing leading number from the old name before adding the new number. Default is true.")] bool stripExistingNumber = true,
        [Description("Include ClashResultGroup folders. Default is true.")] bool includeGroups = true,
        [Description("Include individual ClashResult rows. Default is true.")] bool includeResults = true,
        [Description("Include empty groups. Default is false.")] bool includeEmptyGroups = false,
        [Description("Required with apply=true because renaming is a document mutation. Default is false.")] bool confirmRename = false,
        [Description("Maximum items to plan/rename. Default is 5000, maximum is 50000.")] int limit = 5000,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashRenumberResultsAsync(new ClashRenumberResultsRequest
        {
            Apply = apply,
            TestName = testName,
            TestNames = testNames ?? new List<string>(),
            TestHandles = testHandles ?? new List<string>(),
            Scope = scope,
            OrderBy = orderBy,
            StartNumber = startNumber,
            NumberWidth = numberWidth,
            Separator = separator,
            Prefix = prefix,
            Suffix = suffix,
            PreserveExistingName = preserveExistingName,
            StripExistingNumber = stripExistingNumber,
            IncludeGroups = includeGroups,
            IncludeResults = includeResults,
            IncludeEmptyGroups = includeEmptyGroups,
            ConfirmRename = confirmRename,
            Limit = limit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Plans candidate Clash Detective group pairs by intersecting bounding boxes from top-level roots or the current arbitrary selection. Does not mutate the document. Dry-run never writes outputPath and returns outputWritten=false plus requested/matched/unmatched rootNames. apply=true requires an absolute outputPath and returns only after verified atomic write. Never creates or runs tests.")]
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

    [McpServerTool]
    [Description("Runs, deletes, renames, reorders, sorts, or edits selected Clash Detective tests by name, handle, prefix, or first-N scope. Defaults to dry-run; pass apply=true for run/reset/compact/rename/delete/move/sort/set_settings. operation=run only executes tests; it does not save the model, create reports, screenshots, or viewpoints. Use clash_list_tests first to get testHandles.")]
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
}
