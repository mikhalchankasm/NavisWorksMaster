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
    [Description("Lists tests and per-test clash counts. Read-only.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashListTestsResponse> ClashListTests(
        [Description("Tests per page; max 10000.")] int limit = 200,
        [Description("Zero-based offset into the filtered test list. Default is 0.")] int offset = 0,
        [Description("Optional case-insensitive test-name prefix filter.")] string namePrefix = "",
        [Description("Optional case-insensitive test-name contains filter.")] string nameContains = "",
        [Description("Include per-status counts.")] bool includeStatusCounts = true,
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
    [Description("Read-only results: statuses, assignees, item names; use after clash_list_tests. Grouping invalidates positional resultHandle/groupHandle; stable name works with clash_group_custom/clash_set_status/clash_isolate_result.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashListResultsResponse> ClashListResults(
        [Description("Test name: exact, else contains; empty=all.")] string testName = "",
        [Description("Result rows per page; max 50000.")] int limit = 500,
        [Description("Case-insensitive exact statuses: New, Active, Reviewed, Approved, Resolved.")] List<string> statusFilters = null,
        [Description("All statuses, ignoring statusFilters; empty statusFilters also means all.")] bool includeAllStatuses = false,
        [Description("Sorted filtered result offset, zero-based; page: nextResultOffset/hasMoreResults.")] int resultOffset = 0,
        [Description("Include Item1/Item2 display names. Default is true.")] bool includeItemNames = true,
        [Description("Include AssignedTo. Default is true.")] bool includeAssignedTo = true,
        [Description("Include up to 20 newest comments per result (NavisHelper markers left out).")] bool includeComments = false,
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
            IncludeComments = includeComments,
            IncludeIgnored = includeIgnored,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }

    [McpServerTool]
    [Description("Read-only problem-zone clusters. hybrid splits object pairs by clash-point distance without reliable discipline/architecture classification. For real groups: clash_group_by_proximity.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashListClustersResponse> ClashListClusters(
        [Description("Test name: exact, else contains; empty=all.")] string testName = "",
        [Description("Test names; empty with testName empty means all.")] List<string> testNames = null,
        [Description("Statuses; omitted=New/Active; all: includeAllStatuses=true.")] List<string> statusFilters = null,
        [Description("Include all statuses instead of New/Active.")] bool includeAllStatuses = false,
        [Description("hybrid: object pairs split by distance; object_pair; spatial.")] string groupMode = "hybrid",
        [Description("Spatial split/group distance, mm.")] double clusterDistanceMm = 300,
        [Description("Maximum clusters to return. Default is 100, maximum is 5000.")] int limit = 100,
        [Description("Sorted cluster offset, zero-based; page: nextResultOffset/hasMoreClusters.")] int resultOffset = 0,
        [Description("Preview rows per cluster; max 50.")] int previewRowsPerCluster = 5,
        [Description("Filtered raw results before truncation; max 50000.")] int maxResults = 500,
        [Description("Exclude if either side name/path contains any value.")] List<string> excludeItemNameContains = null,
        [Description("compact omits long previewRows paths; full includes them.")] string verbosity = "compact",
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
