using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed partial class HostBridgeClient
{
    public Task<GetCurrentSectionBoxResponse> GetCurrentSectionBoxAsync(
        GetCurrentSectionBoxRequest request,
        CancellationToken cancellationToken,
        HostTargetOptions target = null)
    {
        return CallHostAsync<GetCurrentSectionBoxResponse>(
            HostCommandNames.GetCurrentSectionBox,
            request,
            cancellationToken,
            target);
    }

    public Task<IsolateByBoxResponse> IsolateByBoxAsync(
        IsolateByBoxRequest request,
        CancellationToken cancellationToken,
        HostTargetOptions target = null)
    {
        return CallHostAsync<IsolateByBoxResponse>(
            HostCommandNames.IsolateByBox,
            request,
            cancellationToken,
            target);
    }
}
