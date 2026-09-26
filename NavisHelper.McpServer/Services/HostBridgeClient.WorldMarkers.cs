using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed partial class HostBridgeClient
{
    public Task<WorldMarkerOverlaySetResponse> WorldMarkersSetAsync(
        WorldMarkerOverlaySetRequest request,
        CancellationToken cancellationToken,
        HostTargetOptions target = null)
    {
        return CallHostAsync<WorldMarkerOverlaySetResponse>(
            HostCommandNames.WorldMarkersSet,
            request,
            cancellationToken,
            target);
    }

    public Task<WorldMarkerOverlayManageResponse> WorldMarkersManageAsync(
        WorldMarkerOverlayManageRequest request,
        CancellationToken cancellationToken,
        HostTargetOptions target = null)
    {
        return CallHostAsync<WorldMarkerOverlayManageResponse>(
            HostCommandNames.WorldMarkersManage,
            request,
            cancellationToken,
            target);
    }

    public Task<WorldMarkerOverlayListResponse> WorldMarkersListAsync(
        WorldMarkerOverlayListRequest request,
        CancellationToken cancellationToken,
        HostTargetOptions target = null)
    {
        return CallHostAsync<WorldMarkerOverlayListResponse>(
            HostCommandNames.WorldMarkersList,
            request,
            cancellationToken,
            target);
    }
}
