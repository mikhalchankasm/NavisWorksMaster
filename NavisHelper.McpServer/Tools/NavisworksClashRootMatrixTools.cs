using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashRootMatrixTools : NavisworksToolBase
{
    public NavisworksClashRootMatrixTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Builds a paged {(rootA, rootB): clashCount} coordination matrix from existing Clash Detective results. Root identity comes directly from each ModelItem.Model, so it does not depend on tree depth or parsing .rvm names from owner paths.")]
    public Task<ClashRootMatrixResponse> ClashRootMatrix(
        [Description("Optional exact test name.")] string testName = "",
        [Description("Optional exact test names.")] List<string> testNames = null,
        [Description("Optional test handles from clash_list_tests.")] List<string> testHandles = null,
        [Description("Optional statuses. Omit for all statuses.")] List<string> statusFilters = null,
        [Description("Include all statuses. Default true.")] bool includeAllStatuses = true,
        [Description("Exclude clashes whose two items belong to the same source model. Default true.")] bool ignoreSameFile = true,
        [Description("Zero-based pair offset.")] int offset = 0,
        [Description("Maximum returned pairs. Default 500, maximum 5000.")] int limit = 500,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashRootMatrixAsync(new ClashRootMatrixRequest
        {
            TestName = testName,
            TestNames = testNames ?? new List<string>(),
            TestHandles = testHandles ?? new List<string>(),
            StatusFilters = statusFilters ?? new List<string>(),
            IncludeAllStatuses = includeAllStatuses,
            IgnoreSameFile = ignoreSameFile,
            Offset = offset,
            Limit = limit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
