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
        int maxDurationSeconds;
        try
        {
            maxDurationSeconds = SectionBoxIsolationLimits.ValidateMaxDurationSeconds(
                request?.MaxDurationSeconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new HostCallException(ErrorCodes.SchemaViolation, exception.Message);
        }
        return CallHostAsync<IsolateByBoxResponse>(
            HostCommandNames.IsolateByBox,
            request,
            cancellationToken,
            target,
            SectionBoxIsolationLimits.GetBridgeRequestTimeoutMilliseconds(maxDurationSeconds));
    }
}
