using System.Diagnostics;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// Starting Navisworks is the one irreversible thing start_navisworks does: about
/// fifteen seconds of work and a window the user did not ask for. These tests pin
/// the rule that a cancelled request never gets that far, and that a *host* failing
/// to answer -- which is not cancellation -- still launches as before.
/// </summary>
public sealed class NavisworksLaunchCancellationTests
{
    [Fact]
    public async Task StartAsync_AlreadyCancelledToken_NeverStartsAProcess()
    {
        var launcher = new RecordingProcessLauncher();
        var service = CreateService(launcher);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StartAsync(
            navisworksVersion: "2026",
            filePath: string.Empty,
            openLatestRecentFile: false,
            waitForHost: false,
            waitTimeoutSeconds: 30,
            cancellationToken: cts.Token));

        Assert.Equal(0, launcher.StartCount);
    }

    [Fact]
    public async Task ResolveProvenAttachTargetAsync_CallerCancelsDuringProbe_DoesNotFallThroughToLaunch()
    {
        var response = new StartNavisworksResponse();
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NavisworksLaunchService.ResolveProvenAttachTargetAsync(
                new[] { Candidate() },
                RequestedFilePath,
                response,
                (_, token) =>
                {
                    cts.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult<HostStatusResponse>(null);
                },
                cts.Token));

        // The launch path is reached by returning null, so a returned value of any
        // kind here would already be the bug. The empty warning list says the caller
        // is not told a host was unproven either: nothing was proven or disproven.
        Assert.Empty(response.Warnings);
    }

    [Fact]
    public async Task ResolveProvenAttachTargetAsync_ProbeTimesOutWhileCallerIsLive_Launches()
    {
        var response = new StartNavisworksResponse();
        using var probeTimeout = new CancellationTokenSource();
        probeTimeout.Cancel();

        var target = await NavisworksLaunchService.ResolveProvenAttachTargetAsync(
            new[] { Candidate() },
            RequestedFilePath,
            response,
            (_, __) => throw new OperationCanceledException(probeTimeout.Token),
            CancellationToken.None);

        Assert.Null(target);
        Assert.Contains(NavisworksAttachPolicy.CandidateDocumentNotProvenMessage, response.Warnings);
    }

    [Fact]
    public async Task ResolveProvenAttachTargetAsync_UnresponsiveHost_Launches()
    {
        var response = new StartNavisworksResponse();

        var target = await NavisworksLaunchService.ResolveProvenAttachTargetAsync(
            new[] { Candidate() },
            RequestedFilePath,
            response,
            (_, __) => throw new InvalidOperationException("host did not answer"),
            CancellationToken.None);

        Assert.Null(target);
        Assert.Contains(NavisworksAttachPolicy.CandidateDocumentNotProvenMessage, response.Warnings);
    }

    [Fact]
    public async Task ResolveProvenAttachTargetAsync_ProvenDocument_Attaches()
    {
        var response = new StartNavisworksResponse();
        var candidate = Candidate();

        var target = await NavisworksLaunchService.ResolveProvenAttachTargetAsync(
            new[] { candidate },
            RequestedFilePath,
            response,
            (_, __) => Task.FromResult(new HostStatusResponse { DocumentFileName = RequestedFilePath }),
            CancellationToken.None);

        Assert.Same(candidate, target);
        Assert.Empty(response.Warnings);
    }

    private static string RequestedFilePath =>
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "navishelper-attach-probe", "model.nwd"));

    private static NavisworksHostInfo Candidate() => new NavisworksHostInfo
    {
        InstanceId = "instance-1",
        Pid = 4242,
        NavisworksVersion = "2026",
        DocumentTitle = "model.nwd",
    };

    private static NavisworksLaunchService CreateService(INavisworksProcessLauncher launcher)
    {
        var callLogger = new McpCallLogger();
        return new NavisworksLaunchService(
            new HostBridgeClient(callLogger),
            callLogger,
            new NavisworksRecentFilesService(),
            launcher);
    }

    private sealed class RecordingProcessLauncher : INavisworksProcessLauncher
    {
        public int StartCount { get; private set; }

        public INavisworksProcess Start(ProcessStartInfo startInfo)
        {
            StartCount++;
            throw new InvalidOperationException(
                "The launcher must not be reached for a cancelled request.");
        }
    }
}
