using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed partial class HostBridgeClient
{
    public Task<StartSaveDocumentResponse> StartSaveDocumentAsync(
        StartSaveDocumentRequest request,
        CancellationToken cancellationToken,
        HostTargetOptions target = null)
    {
        return CallHostAsync<StartSaveDocumentResponse>(
            HostCommandNames.StartSaveDocument,
            request,
            cancellationToken,
            target);
    }

    public Task<SaveDocumentStatusResponse> SaveDocumentStatusAsync(
        SaveDocumentStatusRequest request,
        CancellationToken cancellationToken,
        HostTargetOptions target = null)
    {
        return CallHostAsync<SaveDocumentStatusResponse>(
            HostCommandNames.SaveDocumentStatus,
            request,
            cancellationToken,
            target);
    }
}
