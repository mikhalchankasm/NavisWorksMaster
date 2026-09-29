using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed partial class HostBridgeClient
{
    public Task<MatchHandleItemsResponse> MatchHandleItemsAsync(
        MatchHandleItemsRequest request,
        CancellationToken cancellationToken,
        HostTargetOptions target = null)
    {
        return CallHostAsync<MatchHandleItemsResponse>(
            HostCommandNames.MatchHandleItems,
            request,
            cancellationToken,
            target);
    }
}  
