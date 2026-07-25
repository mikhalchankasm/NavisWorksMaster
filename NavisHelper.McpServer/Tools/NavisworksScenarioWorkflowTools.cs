using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksScenarioWorkflowTools : NavisworksToolBase
{
    public NavisworksScenarioWorkflowTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Selects model items by Navisworks property conditions in the whole model, among one parent's direct children, or among all descendants. The search and selection happen in one call, so no runtime match handle is persisted. This changes only the current selection, not the model.")]
    public Task<SelectBySearchResponse> SelectBySearch(
        [Description("Navisworks property conditions. Conditions are combined with AND unless logical_operator is supplied on later conditions.")] List<FindItemsCondition> conditions,
        [Description("Search scope: whole_model, direct_children_of, or descendants_of.")] string scope = SelectBySearchScopes.WholeModel,
        [Description("Conditions that must resolve to exactly one parent for direct_children_of or descendants_of.")] List<FindItemsCondition> parentConditions = null,
        [Description("Replace the current selection. False adds matches to it. Default is true.")] bool replaceSelection = true,
        [Description("Fail closed if more items match. Default 5000, maximum 100000.")] int maxMatchedItems = 5000,
        [Description("Maximum preview rows in the response. Default 10, maximum 50.")] int previewLimit = 10,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.SelectBySearchAsync(new SelectBySearchRequest
        {
            Conditions = conditions ?? new List<FindItemsCondition>(),
            Scope = scope,
            ParentConditions = parentConditions ?? new List<FindItemsCondition>(),
            ReplaceSelection = replaceSelection,
            MaxMatchedItems = maxMatchedItems,
            PreviewLimit = previewLimit,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
