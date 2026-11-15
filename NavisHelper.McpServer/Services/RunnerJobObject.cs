using System.Diagnostics;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed class RunnerJobObject : IDisposable
{
    private readonly KillOnCloseJob _handle = new KillOnCloseJob(@"Local\NavisHelper-Datasmith-");

    public string Name => _handle.Name;

    public void Assign(Process process)
    {
        _handle.Assign(process);
    }

    public void Dispose() => _handle.Dispose();
}
