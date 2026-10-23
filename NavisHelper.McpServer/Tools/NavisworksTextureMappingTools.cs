using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksTextureMappingTools : NavisworksToolBase
{
    public NavisworksTextureMappingTools(NavisworksToolContext context) : base(context) { }

    [McpServerTool]
    [Description("Reads texture mapping, transforms, domain, box face directions, cylinder cap and texture-coordinate availability one item at a time through the current selection, then restores the selection. Read-only.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    public Task<GetItemTextureMappingResponse> GetItemTextureMapping(
        [Description("Match handles from find_items or other queries. Reads matched items without expanding descendants.")] List<string> matchHandles,
        [Description("Maximum items to read, clamped to 1-100. Default 20.")] int maxItems = 20,
        [Description("Optional explicit Navisworks host instance_id.")] string instanceId = "",
        [Description("Optional Navisworks version, when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default) =>
        _hostBridgeClient.GetItemTextureMappingAsync(new GetItemTextureMappingRequest
        { MatchHandles = matchHandles, MaxItems = maxItems }, cancellationToken, CreateTarget(instanceId, navisworksVersion));
}
