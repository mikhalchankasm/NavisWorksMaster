using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksMatchHandleTools : NavisworksToolBase
{
    public NavisworksMatchHandleTools(NavisworksToolContext context)
        : base(context)
    {
    }

    [McpServerTool]
    [ToolCapabilities(ToolEffects.None, RequiresHost = true, RequiresDocument = true)]
    [Description("Pages through the items behind one match handle from find_items or find_items_by_bbox without searching again and without changing the selection, visibility, or the camera. Use it when a match holds more items than the 20-row preview shows: totalItemCount and hasMore say how much is left, nextOffset is the offset the next page continues from, and an offset past the end returns an empty page. Handles are runtime-only and expire, so a stale one fails with the reason instead of an empty list.")]
    public Task<MatchHandleItemsResponse> MatchHandleItems(
        [Description("Required opaque match handle returned by find_items or find_items_by_bbox. Runtime-only; never persist it in a scenario.")] string matchHandle,
        [Description("Zero-based index of the first item to return. Default is 0; negative values are treated as 0.")] int offset = 0,
        [Description("Maximum items to return. Default is 500, minimum 1, maximum 5000; out-of-range values are clamped and the applied limit is echoed back.")] int limit = MatchHandleItemsPaging.DefaultLimit,
        [Description("Include the full root-to-item path of each item, joined with ' / '. Default is true. Set false for names only on a large page.")] bool includePaths = true,
        [Description("Include each item's source file. Default is false, because reading it walks the item's ancestors.")] bool includeSourceFiles = false,
        [Description("Optional explicit Navisworks host instance_id from list_navisworks_hosts.")] string instanceId = "",
        [Description("Optional Navisworks version, for example 2027. Use only when exactly one host of that version is running.")] string navisworksVersion = "",
        CancellationToken cancellationToken = default)
    {
        return _hostBridgeClient.MatchHandleItemsAsync(
            new MatchHandleItemsRequest
            {
                MatchHandle = matchHandle,
                Offset = offset,
                Limit = limit,
                IncludePaths = includePaths,
                IncludeSourceFiles = includeSourceFiles,
            },
            cancellationToken,
            CreateTarget(instanceId, navisworksVersion));
    }
}  
