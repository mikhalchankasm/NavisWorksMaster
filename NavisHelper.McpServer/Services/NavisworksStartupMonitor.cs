using System.Diagnostics;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed class NavisworksStartupMonitor
{
    private readonly TimeSpan _pollInterval;

    public NavisworksStartupMonitor()
        : this(TimeSpan.FromMilliseconds(250))
    {
    }

    internal NavisworksStartupMonitor(TimeSpan pollInterval)
    {
        if (pollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval));

        _pollInterval = pollInterval;
    }

    public async Task<NavisworksStartupMonitorResult> WaitForHostAsync(
        INavisworksProcess process,
        Func<NavisworksHostInfo> findHost,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (process == null)
            throw new ArgumentNullException(nameof(process));
        if (findHost == null)
            throw new ArgumentNullException(nameof(findHost));
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
                return ResolveExitedProcess(process, findHost());

            var host = findHost();
            if (host != null)
            {
                if (process.HasExited)
                    return ResolveExitedProcess(process, host);

                return NavisworksStartupMonitorResult.HostReady(host);
            }

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
                break;

            await Task.Delay(remaining < _pollInterval ? remaining : _pollInterval, cancellationToken).ConfigureAwait(false);
        }

        if (process.HasExited)
            return ResolveExitedProcess(process, findHost());

        return NavisworksStartupMonitorResult.HostTimeout();
    }

    public NavisworksStartupMonitorResult ObserveWithoutWait(INavisworksProcess process)
    {
        if (process == null)
            throw new ArgumentNullException(nameof(process));

        return process.HasExited
            ? NavisworksStartupMonitorResult.Exited(process.TryGetExitCode())
            : NavisworksStartupMonitorResult.ProcessCreated();
    }

    private static NavisworksStartupMonitorResult ResolveExitedProcess(
        INavisworksProcess process,
        NavisworksHostInfo host)
    {
        var exitCode = process.TryGetExitCode();
        if (exitCode == 0 && host != null && host.Pid != process.Id)
            return NavisworksStartupMonitorResult.HostReady(host, processExited: true, exitCode);

        return NavisworksStartupMonitorResult.Exited(exitCode);
    }
}

internal sealed record NavisworksStartupMonitorResult(
    string Outcome,
    bool ProcessExited,
    int? ExitCode,
    NavisworksHostInfo Host)
{
    public static NavisworksStartupMonitorResult HostReady(
        NavisworksHostInfo host,
        bool processExited = false,
        int? exitCode = null) =>
        new(StartNavisworksOutcomes.HostReady, processExited, exitCode, host);

    public static NavisworksStartupMonitorResult Exited(int? exitCode) =>
        new(StartNavisworksOutcomes.ProcessExited, true, exitCode, null);

    public static NavisworksStartupMonitorResult HostTimeout() =>
        new(StartNavisworksOutcomes.HostTimeout, false, null, null);

    public static NavisworksStartupMonitorResult ProcessCreated() =>
        new(StartNavisworksOutcomes.ProcessCreated, false, null, null);
}

internal interface INavisworksProcess : IDisposable
{
    int Id { get; }
    bool HasExited { get; }
    int? TryGetExitCode();
}

internal interface INavisworksProcessLauncher
{
    INavisworksProcess Start(ProcessStartInfo startInfo);
}

internal sealed class SystemNavisworksProcessLauncher : INavisworksProcessLauncher
{
    public INavisworksProcess Start(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo);
        if (process == null)
            throw new InvalidOperationException("Navisworks process could not be created.");

        return new SystemNavisworksProcess(process);
    }
}

internal sealed class SystemNavisworksProcess : INavisworksProcess
{
    private readonly Process _process;

    public SystemNavisworksProcess(Process process)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
    }

    public int Id => _process.Id;
    public bool HasExited => _process.HasExited;

    public int? TryGetExitCode()
    {
        try
        {
            return _process.ExitCode;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _process.Dispose();
    }
}
