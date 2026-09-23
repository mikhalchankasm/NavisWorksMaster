using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashResultTools : NavisworksToolBase
{
    public NavisworksClashResultTools(NavisworksToolContext context)
        : base(context)
    {
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

}
