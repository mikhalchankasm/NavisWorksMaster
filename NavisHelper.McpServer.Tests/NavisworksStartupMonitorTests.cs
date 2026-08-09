using System.Diagnostics;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class NavisworksStartupMonitorTests
{
    [Fact]
    public async Task WaitForHostAsync_ImmediateExit_ReturnsExitWithoutWaitingForTimeout()
    {
        using var process = new FakeProcess { HasExitedValue = true, ExitCodeValue = 42 };
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(10));
        var stopwatch = Stopwatch.StartNew();

        var result = await monitor.WaitForHostAsync(
            process,
            () => null,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(StartNavisworksOutcomes.ProcessExited, result.Outcome);
        Assert.True(result.ProcessExited);
        Assert.Equal(42, result.ExitCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task WaitForHostAsync_RealImmediateExitFixture_ReturnsWithinSeconds()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("exit 37");

        using var process = new SystemNavisworksProcessLauncher().Start(startInfo);
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(25));
        var stopwatch = Stopwatch.StartNew();

        var result = await monitor.WaitForHostAsync(
            process,
            () => null,
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        Assert.Equal(StartNavisworksOutcomes.ProcessExited, result.Outcome);
        Assert.Equal(37, result.ExitCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task WaitForHostAsync_AliveWithoutHost_ReturnsHostTimeout()
    {
        using var process = new FakeProcess();
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(5));

        var result = await monitor.WaitForHostAsync(
            process,
            () => null,
            TimeSpan.FromMilliseconds(30),
            CancellationToken.None);

        Assert.Equal(StartNavisworksOutcomes.HostTimeout, result.Outcome);
        Assert.False(result.ProcessExited);
        Assert.Null(result.ExitCode);
        Assert.Null(result.Host);
    }

    [Fact]
    public async Task WaitForHostAsync_HostAppears_ReturnsHostReady()
    {
        using var process = new FakeProcess();
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(5));
        var probes = 0;
        var expectedHost = new NavisworksHostInfo { InstanceId = "test-instance", Pid = process.Id };

        var result = await monitor.WaitForHostAsync(
            process,
            () => ++probes >= 2 ? expectedHost : null,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(StartNavisworksOutcomes.HostReady, result.Outcome);
        Assert.Same(expectedHost, result.Host);
        Assert.False(result.ProcessExited);
    }

    [Fact]
    public async Task WaitForHostAsync_StaleHostRacePrefersConfirmedProcessExit()
    {
        using var process = new FakeProcess
        {
            HasExitedSequence = new Queue<bool>(new[] { false, true }),
            ExitCodeValue = 9,
        };
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(5));

        var result = await monitor.WaitForHostAsync(
            process,
            () => new NavisworksHostInfo { InstanceId = "stale-record" },
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(StartNavisworksOutcomes.ProcessExited, result.Outcome);
        Assert.Equal(9, result.ExitCode);
        Assert.Null(result.Host);
    }

    [Fact]
    public async Task WaitForHostAsync_ZeroExitHandoffToDifferentPid_ReturnsReadyHost()
    {
        using var process = new FakeProcess { HasExitedValue = true, ExitCodeValue = 0 };
        var handedOffHost = new NavisworksHostInfo { InstanceId = "handoff", Pid = process.Id + 1 };
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(5));

        var result = await monitor.WaitForHostAsync(
            process,
            () => handedOffHost,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(StartNavisworksOutcomes.HostReady, result.Outcome);
        Assert.True(result.ProcessExited);
        Assert.Equal(0, result.ExitCode);
        Assert.Same(handedOffHost, result.Host);
    }

    [Fact]
    public async Task WaitForHostAsync_ZeroExitWithSamePidRejectsStaleHostRecord()
    {
        using var process = new FakeProcess { HasExitedValue = true, ExitCodeValue = 0 };
        var staleHost = new NavisworksHostInfo { InstanceId = "stale", Pid = process.Id };
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(5));

        var result = await monitor.WaitForHostAsync(
            process,
            () => staleHost,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(StartNavisworksOutcomes.ProcessExited, result.Outcome);
        Assert.Null(result.Host);
    }

    [Fact]
    public async Task WaitForHostAsync_UnavailableExitCodeStillReturnsProcessExited()
    {
        using var process = new FakeProcess { HasExitedValue = true, ExitCodeAvailable = false };
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(5));

        var result = await monitor.WaitForHostAsync(
            process,
            () => null,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(StartNavisworksOutcomes.ProcessExited, result.Outcome);
        Assert.True(result.ProcessExited);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public void ObserveWithoutWait_AliveProcess_ReturnsProcessCreatedWithoutDelay()
    {
        using var process = new FakeProcess();
        var monitor = new NavisworksStartupMonitor();

        var result = monitor.ObserveWithoutWait(process);

        Assert.Equal(StartNavisworksOutcomes.ProcessCreated, result.Outcome);
        Assert.False(result.ProcessExited);
    }

    [Fact]
    public void ObserveWithoutWait_AlreadyExitedProcess_ReturnsFailureSnapshot()
    {
        using var process = new FakeProcess { HasExitedValue = true, ExitCodeValue = -1 };
        var monitor = new NavisworksStartupMonitor();

        var result = monitor.ObserveWithoutWait(process);

        Assert.Equal(StartNavisworksOutcomes.ProcessExited, result.Outcome);
        Assert.True(result.ProcessExited);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public void ApplyStartupResult_ConfirmedEarlyExit_ClearsLegacyStartedSuccess()
    {
        var response = new StartNavisworksResponse { Started = true, ProcessCreated = true };
        var result = NavisworksStartupMonitorResult.Exited(unchecked((int)0xe0434352));

        NavisworksLaunchService.ApplyStartupResult(response, result, waitedForHost: true);

        Assert.True(response.ProcessCreated);
        Assert.False(response.Started);
        Assert.True(response.ProcessExited);
        Assert.Equal(unchecked((int)0xe0434352), response.ExitCode);
        Assert.Equal(StartNavisworksOutcomes.ProcessExited, response.Outcome);
        Assert.False(response.HostReady);
        Assert.NotEmpty(response.FailureReason);
    }

    [Fact]
    public void ApplyStartupResult_AliveHostTimeout_PreservesStartedAndExplainsFailure()
    {
        var response = new StartNavisworksResponse { Started = true, ProcessCreated = true };

        NavisworksLaunchService.ApplyStartupResult(
            response,
            NavisworksStartupMonitorResult.HostTimeout(),
            waitedForHost: true);

        Assert.True(response.Started);
        Assert.False(response.ProcessExited);
        Assert.Equal(StartNavisworksOutcomes.HostTimeout, response.Outcome);
        Assert.NotEmpty(response.FailureReason);
    }

    [Fact]
    public void ApplyStartupResult_NoWaitProcessCreated_IsHonestSnapshotWithoutFailure()
    {
        var response = new StartNavisworksResponse { Started = true, ProcessCreated = true };

        NavisworksLaunchService.ApplyStartupResult(
            response,
            NavisworksStartupMonitorResult.ProcessCreated(),
            waitedForHost: false);

        Assert.True(response.Started);
        Assert.Equal(StartNavisworksOutcomes.ProcessCreated, response.Outcome);
        Assert.False(response.HostReady);
        Assert.Null(response.FailureReason);
    }

    [Fact]
    public void ApplyStartupResult_NormalHandoffKeepsLegacyStartedSuccessAndExitFacts()
    {
        var response = new StartNavisworksResponse { Started = true, ProcessCreated = true };
        var host = new NavisworksHostInfo { InstanceId = "handoff", Pid = 456 };

        NavisworksLaunchService.ApplyStartupResult(
            response,
            NavisworksStartupMonitorResult.HostReady(host, processExited: true, exitCode: 0),
            waitedForHost: true);

        Assert.True(response.Started);
        Assert.True(response.ProcessExited);
        Assert.Equal(0, response.ExitCode);
        Assert.True(response.HostReady);
        Assert.Same(host, response.Host);
        Assert.Equal(StartNavisworksOutcomes.HostReady, response.Outcome);
        Assert.Null(response.FailureReason);
    }

    private sealed class FakeProcess : INavisworksProcess
    {
        public int Id { get; } = 12345;
        public bool HasExitedValue { get; set; }
        public Queue<bool> HasExitedSequence { get; set; }
        public int ExitCodeValue { get; set; }
        public bool ExitCodeAvailable { get; set; } = true;
        public bool HasExited => HasExitedSequence != null && HasExitedSequence.Count > 0
            ? HasExitedSequence.Dequeue()
            : HasExitedValue;
        public int? TryGetExitCode() => ExitCodeAvailable ? ExitCodeValue : null;
        public void Dispose() { }
    }
}
