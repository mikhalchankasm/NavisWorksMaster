using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed partial class HostBridgeClient
{
    public Task<FbxLevelExportStatusResponse> StartFbxLevelExportAsync(
        StartFbxLevelExportRequest request,
        CancellationToken cancellationToken,
        HostTargetOptions target = null)
    {
        return CallHostAsync<FbxLevelExportStatusResponse>(
            HostCommandNames.StartFbxLevelExport,
            request,
            cancellationToken,
            target);
    }

    public Task<FbxLevelExportStatusResponse> FbxLevelExportStatusAsync(
        FbxLevelExportStatusRequest request,
        CancellationToken cancellationToken,
        HostTargetOptions target = null)
    {
        return CallHostAsync<FbxLevelExportStatusResponse>(
            HostCommandNames.FbxLevelExportStatus,
            request,
            cancellationToken,
            target);
    }

    public Task<FbxLevelExportStatusResponse> CancelFbxLevelExportAsync(
        CancelFbxLevelExportRequest request,
        CancellationToken cancellationToken,
        HostTargetOptions target = null)
    {
        return CallHostAsync<FbxLevelExportStatusResponse>(
            HostCommandNames.CancelFbxLevelExport,
            request,
            cancellationToken,
            target);
    }
}
