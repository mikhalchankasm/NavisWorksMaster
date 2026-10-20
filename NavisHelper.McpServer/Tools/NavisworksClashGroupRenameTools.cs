using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksClashGroupRenameTools : NavisworksToolBase
{
    public NavisworksClashGroupRenameTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [Description("Renames one/batch groups in a test; exact name or groupHandle (required for duplicate names). Only names change; membership/statuses stay. All-or-nothing validation includes sibling name conflicts. No moves: handles stay valid for whole batch. Dry-run.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashGroupRenameResponse> ClashGroupRename(
        [Description("Required test handle from clash_list_tests.")] string testHandle,
        [Description("Single rename: clash-group:T:N from clash_list_results/grouping tools.")] string groupHandle = "",
        [Description("Exact group name if groupHandle empty; otherwise verifies handle target.")] string groupName = "",
        [Description("New group name. Single-rename mode.")] string newName = "",
        [Description("Batch replaces single inputs: [{groupHandle or groupName,newName}].")] List<ClashGroupRenameItem> renames = null,
        [Description("False previews, true renames the groups. Default is false.")] bool apply = false,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.ClashGroupRenameAsync(new ClashGroupRenameRequest
        {
            TestHandle = testHandle,
            GroupHandle = groupHandle,
            GroupName = groupName,
            NewName = newName,
            Renames = renames ?? new List<ClashGroupRenameItem>(),
            Apply = apply,
        }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
    }
}
