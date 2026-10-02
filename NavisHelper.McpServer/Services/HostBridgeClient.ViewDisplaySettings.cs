using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed partial class HostBridgeClient
{
    public Task<ViewDisplaySettingsState> GetViewDisplaySettingsAsync(CancellationToken cancellationToken, HostTargetOptions target = null) =>
        CallHostAsync<ViewDisplaySettingsState>(HostCommandNames.GetViewDisplaySettings, new HostStatusRequest(), cancellationToken, target);

    public Task<ViewDisplaySettingsResponse> SetViewDisplaySettingsAsync(ViewDisplaySettingsRequest request,
        CancellationToken cancellationToken, HostTargetOptions target = null) =>
        CallHostAsync<ViewDisplaySettingsResponse>(HostCommandNames.SetViewDisplaySettings, request, cancellationToken, target);
}
