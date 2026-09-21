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
    public void SelectProvableCandidates_ExcludingExitedLauncherPidStillOffersTheDifferentPidHandoff()
    {
        // The launcher exited and a different pid serves the document. That host is a
        // candidate rather than an answer: it is identified here by a file name, and only
        // the path proof can say whether it holds the requested file or a same-named one.
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
        var hosts = new[] { staleLauncherHost, handedOffHost };

        Assert.Null(NavisworksLaunchService.SelectHost(
            hosts,
            expectedTitle: "model.nwd",
            processId: 100,
            excludedProcessId: 100));

        var candidates = NavisworksLaunchService.SelectProvableCandidates(
            hosts,
            expectedTitle: "model.nwd",
            hostsBefore: Array.Empty<NavisworksHostInfo>(),
            excludedProcessId: 100);

        Assert.Same(handedOffHost, Assert.Single(candidates));
    }

    [Fact]
    public void SelectProvableCandidates_AHostThatRegisteredSinceTheLaunchIsAskedFirst()
    {
        // Ordering only, and it is the one thing `hostsBefore` still decides here. A host
        // that appeared since the launch is the likelier answer, so it is worth the first
        // round trip -- but it is proven like any other, because another instance opening
        // a same-named file after the snapshot would otherwise win on its name alone.
        var alreadyRunning = new NavisworksHostInfo
        {
            InstanceId = "already-running",
            Pid = 28760,
            DocumentTitle = "model.nwd",
            StartedAtUtc = DateTime.UtcNow,
        };
        var appearedSince = new NavisworksHostInfo
        {
            InstanceId = "appeared-since",
            Pid = 70360,
            DocumentTitle = "model.nwd",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-9),
        };

        var candidates = NavisworksLaunchService.SelectProvableCandidates(
            new[] { alreadyRunning, appearedSince },
            expectedTitle: "model.nwd",
            hostsBefore: new[] { alreadyRunning },
            excludedProcessId: null);

        // Ahead of `alreadyRunning` despite being the older record of the two.
        Assert.Equal(
            new[] { "appeared-since", "already-running" },
            candidates.Select(host => host.InstanceId));
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
            excludedProcessId: null);

        Assert.Same(launcherHost, selected);
    }

    [Fact]
    public void SelectHost_NeverAnswersWithAHostThatWasAlreadyRunning()
    {
        // Measured on the live rig: two instances held same-named models from different
        // directories, a request for a third such path launched its own process, and
        // start_navisworks returned in 140 ms naming the instance that held D:\nh-l3-b,
        // because discovery compares document *titles* -- file names. Neither tier here
        // identifies a host by a name alone, so this returns null and the caller either
        // waits for its own pid or proves a candidate's full path.
        var strangerHoldingASameNamedFile = new NavisworksHostInfo
        {
            InstanceId = "stranger",
            Pid = 28760,
            DocumentTitle = "6501.5.nwd",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
        };
        var hosts = new[] { strangerHoldingASameNamedFile };

        Assert.Null(NavisworksLaunchService.SelectHost(
            hosts,
            expectedTitle: "6501.5.nwd",
            processId: 72976,
            excludedProcessId: null));
    }

    [Fact]
    public void SelectHandoffCandidates_AHostHandedASameNamedFileIsStillOffered()
    {
        // The case an earlier version of this code got wrong, which is why it has a test
        // of its own. Roamer hands D:\C\6501.5.nwd to an instance already showing
        // D:\B\6501.5.nwd: the document changes, the *title* does not. Filtering on
        // "acquired the expected title since the launch" dropped exactly this host -- the
        // one that really did take the file -- and the launch reported host_timeout over a
        // document open on screen. Nothing here may exclude a host on the strength of its
        // name; that is the path proof's job.
        var handedTheRequestedFile = new NavisworksHostInfo
        {
            InstanceId = "existing",
            Pid = 28760,
            DocumentTitle = "6501.5.nwd",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
        };

        var candidates = NavisworksLaunchService.SelectProvableCandidates(
            new[] { handedTheRequestedFile },
            expectedTitle: "6501.5.nwd",
            hostsBefore: Array.Empty<NavisworksHostInfo>(),
            excludedProcessId: null);

        Assert.Same(handedTheRequestedFile, Assert.Single(candidates));
    }

    [Fact]
    public void SelectHandoffCandidates_EveryNameMatchingHostIsOfferedNewestFirst()
    {
        // All of them, because the newest host holding a same-named file need not be the
        // one holding this file -- the same reason the attach path probes every candidate
        // rather than only the newest. Newest first is a preference between candidates
        // that are otherwise equal, not a decision.
        var older = new NavisworksHostInfo
        {
            InstanceId = "older",
            Pid = 70360,
            DocumentTitle = "6501.5.nwd",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-9),
        };
        var newer = new NavisworksHostInfo
        {
            InstanceId = "newer",
            Pid = 28760,
            DocumentTitle = "6501.5.nwd",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
        };
        var differentDocument = new NavisworksHostInfo
        {
            InstanceId = "unrelated",
            Pid = 11111,
            DocumentTitle = "something-else.nwd",
            StartedAtUtc = DateTime.UtcNow,
        };

        var candidates = NavisworksLaunchService.SelectProvableCandidates(
            new[] { older, differentDocument, newer },
            expectedTitle: "6501.5.nwd",
            hostsBefore: Array.Empty<NavisworksHostInfo>(),
            excludedProcessId: null);

        Assert.Equal(new[] { "newer", "older" }, candidates.Select(host => host.InstanceId));
    }

    [Fact]
    public void SelectHandoffCandidates_TheExcludedProcessIsNotOffered()
    {
        // The launcher's own pid is excluded once it is known to have exited, so a stale
        // record for it cannot be re-offered as somebody else's hand-off.
        var exitedLauncher = new NavisworksHostInfo
        {
            InstanceId = "launcher",
            Pid = 72976,
            DocumentTitle = "6501.5.nwd",
            StartedAtUtc = DateTime.UtcNow,
        };

        Assert.Empty(NavisworksLaunchService.SelectProvableCandidates(
            new[] { exitedLauncher },
            expectedTitle: "6501.5.nwd",
            hostsBefore: Array.Empty<NavisworksHostInfo>(),
            excludedProcessId: 72976));
    }

    [Fact]
    public void SelectHandoffCandidates_ARequestNamingNoFileOffersNothing()
    {
        // `start_navisworks` with no file may legitimately want another instance, and with
        // no document named there is nothing to check a running host against. Offering one
        // would turn "start Navisworks" into "give me whatever is running".
        var running = new NavisworksHostInfo
        {
            InstanceId = "running",
            Pid = 28760,
            DocumentTitle = "6501.5.nwd",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
        };

        Assert.Empty(NavisworksLaunchService.SelectProvableCandidates(
            new[] { running },
            expectedTitle: string.Empty,
            hostsBefore: Array.Empty<NavisworksHostInfo>(),
            excludedProcessId: null));
    }

    [Fact]
    public async Task WaitForHostAsync_AHostLookupThatNeverAnswersStillEndsAtTheTimeout()
    {
        // The lookup used to be a local discovery read. It can now include a host_status
        // round trip into an instance somebody else is using, and a busy instance may leave
        // it outstanding -- so the wait has to bound it by its own remaining time. Without
        // that, one unresponsive candidate holds start_navisworks open past any
        // waitTimeoutSeconds and no further poll ever happens.
        using var process = new FakeProcess();
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(5));
        var stopwatch = Stopwatch.StartNew();

        var result = await monitor.WaitForHostAsync(
            process,
            async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromMinutes(5), token);
                return new NavisworksHostInfo { InstanceId = "never-answers" };
            },
            TimeSpan.FromMilliseconds(200),
            CancellationToken.None);

        Assert.Equal(StartNavisworksOutcomes.HostTimeout, result.Outcome);
        Assert.Null(result.Host);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(20),
            "the wait must end near its timeout, took " + stopwatch.Elapsed);
    }

    [Fact]
    public async Task WaitForHostAsync_ALookupBudgetExpiringIsNotReportedAsCallerCancellation()
    {
        // The budget that bounds the lookup is linked to the caller's token, so the
        // distinction has to be kept: an expired budget means "no host this poll", while
        // the caller cancelling must surface as cancellation.
        using var process = new FakeProcess();
        var monitor = new NavisworksStartupMonitor(TimeSpan.FromMilliseconds(5));
        using var cts = new CancellationTokenSource();

        var timedOut = await monitor.WaitForHostAsync(
            process,
            async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromMinutes(5), token);
                return new NavisworksHostInfo { InstanceId = "never-answers" };
            },
            TimeSpan.FromMilliseconds(150),
            cts.Token);

        Assert.Equal(StartNavisworksOutcomes.HostTimeout, timedOut.Outcome);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => monitor.WaitForHostAsync(
            process,
            async (_, token) =>
            {
                cts.Cancel();
                await Task.Delay(TimeSpan.FromMinutes(5), token);
                return new NavisworksHostInfo { InstanceId = "never-answers" };
            },
            TimeSpan.FromSeconds(30),
            cts.Token));
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
