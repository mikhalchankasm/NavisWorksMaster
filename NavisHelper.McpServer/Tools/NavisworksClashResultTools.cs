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
    [Description("Creates real groups by formula; dry-run. Paged groups: plannedGroupCount, returnedGroupCount, groupsTruncated, nextGroupOffset.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashGroupResultsResponse> ClashGroupResults(
        [Description("False previews; true creates/updates groups.")] bool apply = false,
        [Description("Required test name. Exact match is preferred; otherwise contains-match is used.")] string testName = "",
        [Description("Optional list of test names. Use when grouping several explicit tests.")] List<string> testNames = null,
        [Description("Test handles from clash_list_tests.")] List<string> testHandles = null,
        [Description("Statuses; omitted=New/Active; all: includeAllStatuses=true.")] List<string> statusFilters = null,
        [Description("Include all statuses instead of New/Active.")] bool includeAllStatuses = false,
        [Description("Clash side used for grouping: A or B. Default is B.")] string groupBySide = "B",
        [Description("ancestor, root (appended model, depth-independent), source_file.")] string groupBy = "ancestor",
        [Description("Parent levels above clash item; 0=item itself.")] int ancestorLevelsUp = 1,
        [Description("owner_name or owner_path (separates same-name owners).")] string groupNameMode = "owner_name",
        [Description("Optional prefix added to each generated ClashResultGroup name.")] string groupNamePrefix = "",
        [Description("Append [NH:A]/[NH:B] for UI side detection.")] bool includeNavisHelperSideTag = true,
        [Description("False skips name conflicts; true rebuilds groups with target results.")] bool overwriteExisting = false,
        [Description("Ungroup NavisHelper groups for side/prefix before apply. Use carefully.")] bool ungroupExistingFirst = false,
        [Description("Minimum clash result count required to create a group. Default is 2.")] int minGroupSize = 2,
        [Description("Maximum filtered raw results to analyze before truncating. Default is 500, maximum is 50000.")] int maxResults = 500,
        [Description("Preview rows per planned group. Default is 5, maximum is 50.")] int previewRowsPerGroup = 5,
        [Description("Zero-based offset in the planned groups array. Default is 0.")] int groupOffset = 0,
        [Description("Maximum groups returned in this page. Default is 500, maximum is 5000.")] int groupLimit = 500,
        [Description("Return group aggregates without preview rows. Recommended for large matrices.")] bool aggregateOnly = false,
        [Description("On apply above 1000 results: required; ask user first.")] bool confirmLargeGrouping = false,
        [Description("Exclude if either item name/path contains any value.")] List<string> excludeItemNameContains = null,
        [Description("compact omits long previewRows paths; full includes them.")] string verbosity = "compact",
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
    [Description("Groups explicit results into one real group; validates all selectors before mutation. Grouping prepends group and renumbers result/group handles: use stable resultNames or relist. Dry-run.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashGroupCustomResponse> ClashGroupCustom(
        [Description("Required test handle from clash_list_tests.")] string testHandle,
        [Description("Name of the target ClashResultGroup.")] string groupName,
        [Description("clash_list_results: clash-result:T:N; stale after grouping in test.")] List<string> resultHandles = null,
        [Description("clash_list_results names: exact, case-insensitive, unique in test; grouping-stable.")] List<string> resultNames = null,
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
            ResultNames = resultNames ?? new List<string>(),
            GroupName = groupName,
            Apply = apply,
            OverwriteExisting = overwriteExisting,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Ungroups handles/name-prefix groups in one test. Handles are tree positions; relist after any grouping. Dry-run.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
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
    [Description("Sets result status by results/groups/tests; group/test scopes cascade to results. Grouping invalidates result/group handles; resultNames stable. Dry-run.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashSetStatusResponse> ClashSetStatus(
        [Description("Required scope: results, group, or test.")] string scope,
        [Description("Target status: New, Active, Reviewed, Approved, or Resolved.")] string status,
        [Description("Required test handle for results/group; additional handle for test scope.")] string testHandle = "",
        [Description("Result handles for scope=results (positional).")] List<string> resultHandles = null,
        [Description("scope=results: exact case-insensitive names, unique in test.")] List<string> resultNames = null,
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
            ResultNames = resultNames ?? new List<string>(),
            GroupHandles = groupHandles ?? new List<string>(),
            TestHandles = testHandles ?? new List<string>(),
            AssignedTo = assignedTo,
            Comment = comment,
            Apply = apply,
            ConfirmLargeStatusChange = confirmLargeStatusChange,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Creates per-test groups by clash-point clustering: spatial, hybrid, object_pair. Dry-run.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
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
    [Description("Lists/adds/removes document-persistent ignore rules. Added rules approve matches with reason comment; reapplied after test runs. Add/remove: dry-run.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
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
    [Description("Exports CSV/XLSX clash points: global/local coordinates, levels, grid cells, XLSX summaries. Dry-run.")]
    [ToolCapabilities(ToolEffects.Files, RequiresHost = true, RequiresDocument = true)]
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
    [Description("Renumbers selected test groups/results; dry-run. top_level numbers visible groups/ungrouped results. Review plan before apply=true + confirmRename=true.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashRenumberResultsResponse> ClashRenumberResults(
        [Description("False previews; true renames groups/results.")] bool apply = false,
        [Description("Test name: exact, else contains; required unless testNames/testHandles supplied.")] string testName = "",
        [Description("Optional list of test names.")] List<string> testNames = null,
        [Description("Test handles from clash_list_tests.")] List<string> testHandles = null,
        [Description("top_level: direct groups/ungrouped results; recursive: includes nested groups/results.")] string scope = "top_level",
        [Description("current: tree order; name: natural name order.")] string orderBy = "current",
        [Description("First number. Default is 1.")] int startNumber = 1,
        [Description("Minimum zero-padded width; expands for larger numbers.")] int numberWidth = 4,
        [Description("Between number and preserved old name.")] string separator = " - ",
        [Description("Optional text before the number, for example 'C-'.")] string prefix = "",
        [Description("Optional text after the number, before the old name.")] string suffix = "",
        [Description("Keep old clean name after number.")] bool preserveExistingName = true,
        [Description("Strip old leading number before adding new one.")] bool stripExistingNumber = true,
        [Description("Include ClashResultGroup folders. Default is true.")] bool includeGroups = true,
        [Description("Include individual ClashResult rows. Default is true.")] bool includeResults = true,
        [Description("Include empty groups. Default is false.")] bool includeEmptyGroups = false,
        [Description("Required with apply=true to confirm document renames.")] bool confirmRename = false,
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
