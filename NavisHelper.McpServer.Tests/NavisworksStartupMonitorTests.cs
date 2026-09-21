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
            (_, _) => Task.FromResult<NavisworksHostInfo>(null),
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
            (_, _) => Task.FromResult<NavisworksHostInfo>(null),
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
            (_, _) => Task.FromResult<NavisworksHostInfo>(null),
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
            (_, _) => Task.FromResult(++probes >= 2 ? expectedHost : null),
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
            (_, _) => Task.FromResult(new NavisworksHostInfo { InstanceId = "stale-record" }),
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
            (_, _) => Task.FromResult(handedOffHost),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(StartNavisworksOutcomes.HostReady, result.Outcome);
        Assert.True(result.ProcessExited);
        Assert.Equal(0, result.ExitCode);
        Assert.Same(handedOffHost, result.Host);
    }

    [Fact]
    public async Task WaitForHostAsync_CleanExitPollsUntilDelayedHandoffAppears()
    {
        using var process = new FakeProcess { HasExitedValue = true, ExitCodeValue = 0 };
        var handedOffHost = new NavisworksHostInfo { InstanceId = "delayed-handoff", Pid = process.Id + 1 };
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(5));
        var probes = 0;
        var excludedProcessIds = new List<int?>();

        var result = await monitor.WaitForHostAsync(
            process,
            (excludedProcessId, _) =>
            {
                excludedProcessIds.Add(excludedProcessId);
                return Task.FromResult(++probes >= 3 ? handedOffHost : null);
            },
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.True(probes >= 3);
        Assert.All(excludedProcessIds, excludedProcessId => Assert.Equal(process.Id, excludedProcessId));
        Assert.Equal(StartNavisworksOutcomes.HostReady, result.Outcome);
        Assert.True(result.ProcessExited);
        Assert.Equal(0, result.ExitCode);
        Assert.Same(handedOffHost, result.Host);
    }

    [Fact]
    public async Task WaitForHostAsync_CleanExitWithoutHandoffReturnsHostTimeoutAfterRemainingWait()
    {
        using var process = new FakeProcess { HasExitedValue = true, ExitCodeValue = 0 };
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(5));
        var probes = 0;

        var result = await monitor.WaitForHostAsync(
            process,
            (_, _) => { probes++; return Task.FromResult<NavisworksHostInfo>(null); },
            TimeSpan.FromMilliseconds(35),
            CancellationToken.None);

        Assert.True(probes >= 2);
        Assert.Equal(StartNavisworksOutcomes.HostTimeout, result.Outcome);
        Assert.True(result.ProcessExited);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Host);
    }

    [Fact]
    public async Task WaitForHostAsync_CleanExitIgnoresSamePidStaleRecordUntilTimeout()
    {
        using var process = new FakeProcess { HasExitedValue = true, ExitCodeValue = 0 };
        var staleHost = new NavisworksHostInfo { InstanceId = "stale", Pid = process.Id };
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(5));

        var result = await monitor.WaitForHostAsync(
            process,
            (_, _) => Task.FromResult(staleHost),
            TimeSpan.FromMilliseconds(25),
            CancellationToken.None);

        Assert.Equal(StartNavisworksOutcomes.HostTimeout, result.Outcome);
        Assert.True(result.ProcessExited);
        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Host);
    }

    [Fact]
    public async Task WaitForHostAsync_NonzeroExitDoesNotEnterHandoffPolling()
    {
        using var process = new FakeProcess { HasExitedValue = true, ExitCodeValue = 5 };
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(5));
        var probes = 0;
        var stopwatch = Stopwatch.StartNew();

        var result = await monitor.WaitForHostAsync(
            process,
            (_, _) => { probes++; return Task.FromResult<NavisworksHostInfo>(null); },
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(0, probes);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(250));
        Assert.Equal(StartNavisworksOutcomes.ProcessExited, result.Outcome);
        Assert.Equal(5, result.ExitCode);
    }

    [Fact]
    public async Task WaitForHostAsync_UnavailableExitCodeStillReturnsProcessExited()
    {
        using var process = new FakeProcess { HasExitedValue = true, ExitCodeAvailable = false };
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(5));

        var result = await monitor.WaitForHostAsync(
            process,
            (_, _) => Task.FromResult<NavisworksHostInfo>(null),
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.Equal(StartNavisworksOutcomes.ProcessExited, result.Outcome);
        Assert.True(result.ProcessExited);
        Assert.Null(result.ExitCode);
    }

    [Fact]
    public async Task WaitForHostAsync_CancellationStopsCleanExitHandoffPolling()
    {
        using var process = new FakeProcess { HasExitedValue = true, ExitCodeValue = 0 };
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(5));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => monitor.WaitForHostAsync(
            process,
            (_, _) => Task.FromResult<NavisworksHostInfo>(null),
            TimeSpan.FromSeconds(5),
            cancellation.Token));
    }

    [Fact]
    public void SelectHost_ExcludingExitedLauncherPidAllowsDifferentPidHandoff()
    {
        var staleLauncherHost = new NavisworksHostInfo
        {
            InstanceId = "launcher",
            Pid = 100,
            DocumentTitle = "model.nwd",
            StartedAtUtc = DateTime.UtcNow,
        };
        var handedOffHost = new NavisworksHostInfo
        {
            InstanceId = "handoff",
            Pid = 200,
            DocumentTitle = "model.nwd",
            StartedAtUtc = DateTime.UtcNow.AddSeconds(1),
        };

        var selected = NavisworksLaunchService.SelectHost(
            new[] { staleLauncherHost, handedOffHost },
            expectedTitle: "model.nwd",
            processId: 100,
            hostsBefore: Array.Empty<NavisworksHostInfo>(),
            excludedProcessId: 100);

        Assert.Same(handedOffHost, selected);
    }

    [Fact]
    public void SelectHost_ActiveLauncherPidKeepsExactPidPriority()
    {
        var launcherHost = new NavisworksHostInfo
        {
            InstanceId = "launcher",
            Pid = 100,
            DocumentTitle = "model.nwd",
        };
        var otherHost = new NavisworksHostInfo
        {
            InstanceId = "other",
            Pid = 200,
            DocumentTitle = "model.nwd",
        };

        var selected = NavisworksLaunchService.SelectHost(
            new[] { otherHost, launcherHost },
            expectedTitle: "model.nwd",
            processId: 100,
            hostsBefore: Array.Empty<NavisworksHostInfo>(),
            excludedProcessId: null);

        Assert.Same(launcherHost, selected);
    }

    [Fact]
    public void SelectHandoffCandidates_APreExistingSameNamedHostIsNotOfferedForALaunchStillStarting()
    {
        // Measured on the live rig: two instances held same-named models from different
        // directories, a request for a third such path launched its own process, and
        // start_navisworks returned in 140 ms naming the instance that held D:\nh-l3-b,
        // because discovery compares document *titles* -- file names -- and the launched
        // pid had not registered yet. It registered seconds later holding the requested
        // file. Reporting the stranger makes every later tool address the wrong model.
        var strangerHoldingASameNamedFile = new NavisworksHostInfo
        {
            InstanceId = "stranger",
            Pid = 28760,
            DocumentTitle = "6501.5.nwd",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
        };
        var hosts = new[] { strangerHoldingASameNamedFile };

        // Neither tier identifies it, and it is not even offered for proof, so no round
        // trip is spent on it: the poll simply runs on until the launched pid registers.
        Assert.Null(NavisworksLaunchService.SelectHost(
            hosts,
            expectedTitle: "6501.5.nwd",
            processId: 72976,
            hostsBefore: hosts,
            excludedProcessId: null));

        Assert.Empty(NavisworksLaunchService.SelectHandoffCandidates(
            hosts,
            expectedTitle: "6501.5.nwd",
            hostsBefore: hosts,
            excludedProcessId: null));
    }

    [Fact]
    public void SelectHandoffCandidates_AHostThatPickedUpTheRequestedTitleIsOfferedForProof()
    {
        // The case the fallback exists for, and the reason it is narrowed rather than
        // removed: Roamer can hand the file to an instance that is already running, which
        // registers no new host, so without this a launch would time out on a document
        // that is open on screen. The hand-off shows as a title this host did not carry
        // before the launch -- which makes it a candidate, not an answer. The caller still
        // has to confirm the full path.
        var beforeLaunch = new NavisworksHostInfo
        {
            InstanceId = "existing",
            Pid = 28760,
            DocumentTitle = "something-else.nwd",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
        };
        var afterHandoff = new NavisworksHostInfo
        {
            InstanceId = "existing",
            Pid = 28760,
            DocumentTitle = "6501.5.nwd",
            StartedAtUtc = beforeLaunch.StartedAtUtc,
        };

        var candidates = NavisworksLaunchService.SelectHandoffCandidates(
            new[] { afterHandoff },
            expectedTitle: "6501.5.nwd",
            hostsBefore: new[] { beforeLaunch },
            excludedProcessId: null);

        Assert.Same(afterHandoff, Assert.Single(candidates));
    }

    [Fact]
    public void SelectHandoffCandidates_OnlyTheHostThatAcquiredTheTitleIsOfferedEvenWhenAStrangerIsNewer()
    {
        // Both pre-existing hosts report the requested title and the stranger is the
        // newer of the two, so ordering alone would offer the wrong one first and spend
        // the probe on it. Eligibility is not about recency.
        var strangerBefore = new NavisworksHostInfo
        {
            InstanceId = "stranger",
            Pid = 28760,
            DocumentTitle = "6501.5.nwd",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
        };
        var handoffBefore = new NavisworksHostInfo
        {
            InstanceId = "handoff",
            Pid = 70360,
            DocumentTitle = "something-else.nwd",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-9),
        };
        var handoffAfter = new NavisworksHostInfo
        {
            InstanceId = "handoff",
            Pid = 70360,
            DocumentTitle = "6501.5.nwd",
            StartedAtUtc = handoffBefore.StartedAtUtc,
        };

        var candidates = NavisworksLaunchService.SelectHandoffCandidates(
            new[] { strangerBefore, handoffAfter },
            expectedTitle: "6501.5.nwd",
            hostsBefore: new[] { strangerBefore, handoffBefore },
            excludedProcessId: null);

        Assert.Same(handoffAfter, Assert.Single(candidates));
    }

    [Fact]
    public void SelectHandoffCandidates_APidReusedByANewHostIsNotMistakenForThePreLaunchRecord()
    {
        // The pre-launch snapshot is matched by instance id, because a pid freed by a
        // closed instance can be handed to the next process. Matching on the pid alone
        // would read this host's title from a record belonging to a different host and
        // rule out a host that really did acquire the document.
        var closedHostThatOwnedThePid = new NavisworksHostInfo
        {
            InstanceId = "closed",
            Pid = 28760,
            DocumentTitle = "6501.5.nwd",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-9),
        };
        var newHostReusingThePid = new NavisworksHostInfo
        {
            InstanceId = "fresh",
            Pid = 28760,
            DocumentTitle = "6501.5.nwd",
            StartedAtUtc = DateTime.UtcNow,
        };

        var candidates = NavisworksLaunchService.SelectHandoffCandidates(
            new[] { newHostReusingThePid },
            expectedTitle: "6501.5.nwd",
            hostsBefore: new[] { closedHostThatOwnedThePid },
            excludedProcessId: null);

        Assert.Same(newHostReusingThePid, Assert.Single(candidates));
    }

    [Fact]
    public void SelectHandoffCandidates_WithoutInstanceIdsAPidReuseIsResolvedTowardsTheSaferAnswer()
    {
        // Falling back to pids cannot tell a pid-reusing host from its predecessor, so
        // this host is not offered and the launch ends in host_timeout. That direction is
        // deliberate: not offering one costs a truthful timeout, while offering it spends
        // a probe on an instance that cannot be addressed afterwards anyway -- the tools
        // that follow target hosts by instance id.
        var closedHostThatOwnedThePid = new NavisworksHostInfo
        {
            InstanceId = string.Empty,
            Pid = 28760,
            DocumentTitle = "6501.5.nwd",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-9),
        };
        var newHostReusingThePid = new NavisworksHostInfo
        {
            InstanceId = string.Empty,
            Pid = 28760,
            DocumentTitle = "6501.5.nwd",
            StartedAtUtc = DateTime.UtcNow,
        };

        Assert.Empty(NavisworksLaunchService.SelectHandoffCandidates(
            new[] { newHostReusingThePid },
            expectedTitle: "6501.5.nwd",
            hostsBefore: new[] { closedHostThatOwnedThePid },
            excludedProcessId: null));
    }

    [Fact]
    public void SelectHandoffCandidates_ARequestNamingNoFileOffersNothing()
    {
        // `start_navisworks` with no file may legitimately want another instance, and
        // with no document named there is nothing to check a running host against.
        // Offering one would turn "start Navisworks" into "give me whatever is running".
        var running = new NavisworksHostInfo
        {
            InstanceId = "running",
            Pid = 28760,
            DocumentTitle = "6501.5.nwd",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
        };

        Assert.Empty(NavisworksLaunchService.SelectHandoffCandidates(
            new[] { running },
            expectedTitle: string.Empty,
            hostsBefore: Array.Empty<NavisworksHostInfo>(),
            excludedProcessId: null));
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

    [Fact]
    public void ApplyStartupResult_CleanHandoffTimeoutUsesTimeoutOutcomeAndExitFacts()
    {
        var response = new StartNavisworksResponse { Started = true, ProcessCreated = true };

        NavisworksLaunchService.ApplyStartupResult(
            response,
            NavisworksStartupMonitorResult.HostTimeout(processExited: true, exitCode: 0),
            waitedForHost: true);

        Assert.False(response.Started);
        Assert.True(response.ProcessExited);
        Assert.Equal(0, response.ExitCode);
        Assert.False(response.HostReady);
        Assert.Equal(StartNavisworksOutcomes.HostTimeout, response.Outcome);
        Assert.Contains("exited cleanly", response.FailureReason, StringComparison.OrdinalIgnoreCase);
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
