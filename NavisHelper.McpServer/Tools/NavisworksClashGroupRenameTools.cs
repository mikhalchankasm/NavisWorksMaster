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
    [Description("Renames ClashResultGroup folders in one test, one group or a batch. Select each group by groupHandle or by its exact name in the test; a name held by several groups must use groupHandle. Only names change: group membership and result statuses stay. Validated all-or-nothing, including name clashes between sibling groups. Renaming moves nothing, so group handles from one listing stay valid for the whole batch. Dry-run by default.")]
    [ToolCapabilities(ToolEffects.Document, RequiresHost = true, RequiresDocument = true)]
    public Task<ClashGroupRenameResponse> ClashGroupRename(
        [Description("Required test handle from clash_list_tests.")] string testHandle,
        [Description("Group handle (clash-group:T:N) from clash_list_results or a grouping tool. Single-rename mode.")] string groupHandle = "",
        [Description("Exact current group name within the test, used when groupHandle is empty. With groupHandle it is a check that the handle still points at this group.")] string groupName = "",
        [Description("New group name. Single-rename mode.")] string newName = "",
        [Description("Batch mode instead of groupHandle/groupName/newName: items {groupHandle or groupName, newName}.")] List<ClashGroupRenameItem> renames = null,
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
