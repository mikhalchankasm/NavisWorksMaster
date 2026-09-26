using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed partial class HostBridgeClient
{
    public Task<ViewpointSetCameraResponse> ViewpointSetCameraAsync(
        ViewpointSetCameraRequest request,
        CancellationToken cancellationToken,
        HostTargetOptions target = null)
    {
        return CallHostAsync<ViewpointSetCameraResponse>(
            HostCommandNames.ViewpointSetCamera,
            request,
            cancellationToken,
            target);
    }
}
