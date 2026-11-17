using System.ComponentModel;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;

namespace NavisHelper.McpServer.Tools;

internal sealed class NavisworksBackgroundJobTools
{
    private readonly HostBackgroundJobStore _jobs;

    public NavisworksBackgroundJobTools(HostBackgroundJobStore jobs) => _jobs = jobs;

    [McpServerTool]
    [Description("Status of a background=true call: running, succeeded with the tool's own response in result, or failed with errorCode. waitSeconds waits up to 55 s for completion. Server-side; jobs live while this MCP server runs.")]
    [ToolCapabilities(ToolEffects.None, RequiresHost = false, RequiresDocument = false)]
    public Task<HostBackgroundJobStatusResponse> BackgroundJobStatus(
        [Description("operationId from a background=true call.")] string operationId,
        [Description("Seconds to wait for completion, 0-55. Default 0.")] int waitSeconds = 0)
    {
        var seconds = Math.Clamp(waitSeconds, 0, 55);
        return seconds == 0 ? Task.FromResult(_jobs.Get(operationId))
            : _jobs.WaitAsync(operationId, TimeSpan.FromSeconds(seconds));
    }
}
