using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashListingTools : NavisworksToolBase
{
    public NavisworksClashListingTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Lists Clash Detective tests in the active Navisworks document and returns per-test clash counts. Read-only.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
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
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
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
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
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

}
