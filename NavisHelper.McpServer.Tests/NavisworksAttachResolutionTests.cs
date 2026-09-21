using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// The fix these pin is that every candidate is probed, not only the first.
///
/// `NavisworksAttachPolicyTests` proves that `SelectAttachCandidates` returns the
/// matching hosts newest first. That is the input to the decision, not the decision:
/// the loop that keeps going after a candidate fails its path proof lives in
/// `ResolveProvenAttachTargetAsync`, and a test that reproduces the ordering instead of
/// calling it would stay green if that loop were reverted to `FirstOrDefault`.
///
/// Attaching to a ready host costs about 123 ms against roughly 15 500 ms for a launch,
/// so stopping at the first candidate is two orders of magnitude and a redundant
/// Navisworks window.
/// </summary>
public sealed class NavisworksAttachResolutionTests
{
    [Fact]
    public async Task OlderHostHoldingTheRequestedFileIsAttachedToPastANewerMismatch()
    {
        var newer = Host("newer-instance", "D:\\B\\model.nwd");
        var older = Host("older-instance", RequestedFilePath);
        var probed = new List<string>();
        var response = new StartNavisworksResponse();

        var target = await NavisworksLaunchService.ResolveProvenAttachTargetAsync(
            new[] { newer, older },
            RequestedFilePath,
            response,
            (candidate, _) =>
            {
                probed.Add(candidate.InstanceId);
                return Task.FromResult(new HostStatusResponse
                {
                    DocumentFileName = DocumentOf(candidate),
                });
            },
            CancellationToken.None);

        Assert.Same(older, target);
        Assert.Equal(new[] { "newer-instance", "older-instance" }, probed);
        Assert.Empty(response.Warnings);
    }

    [Fact]
    public async Task ProbingStopsAtTheFirstCandidateThatProves()
    {
        var match = Host("first-instance", RequestedFilePath);
        var other = Host("second-instance", "D:\\B\\model.nwd");
        var probed = new List<string>();

        var target = await NavisworksLaunchService.ResolveProvenAttachTargetAsync(
            new[] { match, other },
            RequestedFilePath,
            new StartNavisworksResponse(),
            (candidate, _) =>
            {
                probed.Add(candidate.InstanceId);
                return Task.FromResult(new HostStatusResponse
                {
                    DocumentFileName = DocumentOf(candidate),
                });
            },
            CancellationToken.None);

        // Each extra probe is a round trip; proving one is enough to stop.
        Assert.Same(match, target);
        Assert.Equal(new[] { "first-instance" }, probed);
    }

    [Fact]
    public async Task AnUnresponsiveCandidateDoesNotHideALaterMatch()
    {
        var unresponsive = Host("unresponsive", RequestedFilePath);
        var match = Host("responsive", RequestedFilePath);
        var response = new StartNavisworksResponse();

        var target = await NavisworksLaunchService.ResolveProvenAttachTargetAsync(
            new[] { unresponsive, match },
            RequestedFilePath,
            response,
            (candidate, _) => candidate.InstanceId == "unresponsive"
                ? throw new InvalidOperationException("this host stopped answering")
                : Task.FromResult(new HostStatusResponse { DocumentFileName = DocumentOf(candidate) }),
            CancellationToken.None);

        Assert.Same(match, target);
        Assert.Empty(response.Warnings);
    }

    [Fact]
    public async Task NoCandidateProvingTheRequestedPathLaunchesAndSaysHowManyWereRuledOut()
    {
        var response = new StartNavisworksResponse();

        var target = await NavisworksLaunchService.ResolveProvenAttachTargetAsync(
            new[] { Host("a", "D:\\A\\other.nwd"), Host("b", "D:\\B\\other.nwd") },
            RequestedFilePath,
            response,
            (candidate, _) => Task.FromResult(new HostStatusResponse
            {
                DocumentFileName = DocumentOf(candidate),
            }),
            CancellationToken.None);

        // Null means the caller launches, which is the safe outcome: a wrong attach
        // would point every later write tool at a different model.
        Assert.Null(target);
        Assert.Single(response.Warnings);
        Assert.Contains("2", response.Warnings[0]);
    }

    [Fact]
    public async Task TheSharedBudgetStopsTheProbesAndSaysWhatWasNotExamined()
    {
        // Each host_status call carries the transport's own 60 s timeout, so without a
        // shared deadline three unresponsive candidates would hold start_navisworks for
        // three minutes where one held it for one. The budget is the whole resolution's.
        var probed = new List<string>();
        var response = new StartNavisworksResponse();

        var target = await NavisworksLaunchService.ResolveProvenAttachTargetAsync(
            new[] { Host("slow-1", RequestedFilePath), Host("slow-2", RequestedFilePath),
                    Host("slow-3", RequestedFilePath) },
            RequestedFilePath,
            response,
            async (candidate, token) =>
            {
                probed.Add(candidate.InstanceId);
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return new HostStatusResponse { DocumentFileName = DocumentOf(candidate) };
            },
            CancellationToken.None,
            probeBudget: TimeSpan.FromMilliseconds(120));

        // Launching is the safe outcome, exactly as when no candidate proves its path.
        Assert.Null(target);
        Assert.True(probed.Count < 3, "the budget must stop the probes, probed " + probed.Count);
        Assert.Single(response.Warnings);
        Assert.Contains("were not examined", response.Warnings[0]);
    }

    [Fact]
    public async Task BudgetExpiryIsNotReportedAsCallerCancellation()
    {
        // The linked token trips while the caller's does not, so the rethrow guarded by
        // cancellationToken.IsCancellationRequested must not fire: an expired budget is a
        // reason to launch, not an error to hand back.
        var response = new StartNavisworksResponse();

        var target = await NavisworksLaunchService.ResolveProvenAttachTargetAsync(
            new[] { Host("slow", RequestedFilePath), Host("also-slow", RequestedFilePath) },
            RequestedFilePath,
            response,
            async (candidate, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return new HostStatusResponse { DocumentFileName = DocumentOf(candidate) };
            },
            CancellationToken.None,
            probeBudget: TimeSpan.FromMilliseconds(80));

        Assert.Null(target);
    }

    // A fixed clock: these tests are about what the ledger records, not about elapsed
    // time. The one test that needs a refusal to expire moves it forward explicitly.
    private static readonly DateTimeOffset Now =
        new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);

    private static string RequestedFilePath =>
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "navishelper-attach-resolution", "model.nwd"));

    private static readonly Dictionary<string, string> Documents = new();

    private static NavisworksHostInfo Host(string instanceId, string documentPath)
    {
        Documents[instanceId] = documentPath;
        return new NavisworksHostInfo
        {
            InstanceId = instanceId,
            Pid = 4242,
            NavisworksVersion = "2026",
            DocumentTitle = Path.GetFileName(documentPath),
        };
    }

    private static string DocumentOf(NavisworksHostInfo host) => Documents[host.InstanceId];

    // ---- the hand-off proof, which runs inside the startup wait rather than before it --

    [Fact]
    public async Task AHandoffCandidateIsAcceptedOnlyWhenItsFullPathIsProven()
    {
        // The candidate reports the requested file *name*, which is what made it a
        // candidate. The proof is the path: this one holds a same-named model from
        // another directory, so the launch must keep waiting for its own process rather
        // than answer with this host.
        var wrongDirectory = Host("stranger", "D:\\nh-l3-b\\6501.5.nwd");
        var ledger = new NavisworksLaunchService.HandoffProofLedger();

        var target = await NavisworksLaunchService.ResolveProvenHandoffAsync(
            new[] { wrongDirectory },
            RequestedFilePath,
            ledger,
            (candidate, _) => Task.FromResult(new HostStatusResponse
            {
                DocumentFileName = DocumentOf(candidate),
            }),
            () => Now,
            CancellationToken.None);

        Assert.Null(target);
        // Recorded as refused, so the next poll within the retry interval skips it.
        Assert.False(ledger.IsProven("stranger"));
        Assert.False(ledger.ShouldProbe("stranger", Now));
    }

    [Fact]
    public async Task AProvenHandoffIsReturnedAndRemembered()
    {
        var handoff = Host("handoff", RequestedFilePath);
        var ledger = new NavisworksLaunchService.HandoffProofLedger();
        var probes = 0;

        for (var poll = 0; poll < 3; poll++)
        {
            var target = await NavisworksLaunchService.ResolveProvenHandoffAsync(
                new[] { handoff },
                RequestedFilePath,
                ledger,
                (candidate, _) =>
                {
                    probes++;
                    return Task.FromResult(new HostStatusResponse
                    {
                        DocumentFileName = DocumentOf(candidate),
                    });
                },
                () => Now,
                CancellationToken.None);

            Assert.Same(handoff, target);
        }

        // The wait polls every 250 ms for up to five minutes. One verdict per instance
        // is the difference between one round trip into somebody else's instance and
        // hundreds of them.
        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task ARuledOutCandidateIsNotProbedAgainOnEveryPoll()
    {
        var wrongDirectory = Host("stranger", "D:\\nh-l3-b\\6501.5.nwd");
        var ledger = new NavisworksLaunchService.HandoffProofLedger();
        var probes = 0;

        for (var poll = 0; poll < 4; poll++)
        {
            Assert.Null(await NavisworksLaunchService.ResolveProvenHandoffAsync(
                new[] { wrongDirectory },
                RequestedFilePath,
                ledger,
                (candidate, _) =>
                {
                    probes++;
                    return Task.FromResult(new HostStatusResponse
                    {
                        DocumentFileName = DocumentOf(candidate),
                    });
                },
                () => Now,
                CancellationToken.None));
        }

        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task ACandidateThatBlocksDoesNotConsumeTheWholeWaitOrHideTheRealHost()
    {
        // Each candidate gets its own short deadline. Sharing one -- the time left in the
        // startup wait -- let the first candidate that blocks swallow the entire budget,
        // so the candidates behind it were never asked and discovery was never polled
        // again. The host that actually holds the file is second here, which is the point.
        var blocks = Host("blocks", RequestedFilePath);
        var holdsTheFile = Host("holds-the-file", RequestedFilePath);
        var probed = new List<string>();

        var target = await NavisworksLaunchService.ResolveProvenHandoffAsync(
            new[] { blocks, holdsTheFile },
            RequestedFilePath,
            new NavisworksLaunchService.HandoffProofLedger(),
            async (candidate, token) =>
            {
                probed.Add(candidate.InstanceId);
                if (candidate.InstanceId == "blocks")
                    await Task.Delay(TimeSpan.FromMinutes(5), token);

                return new HostStatusResponse { DocumentFileName = DocumentOf(candidate) };
            },
            () => Now,
            CancellationToken.None,
            perProbeTimeout: TimeSpan.FromMilliseconds(120));

        Assert.Same(holdsTheFile, target);
        Assert.Equal(new[] { "blocks", "holds-the-file" }, probed);
    }

    [Fact]
    public async Task AShortWaitIsSharedBetweenCandidatesRatherThanSpentOnTheFirst()
    {
        // The per-candidate cap alone was not enough. With a five-second cap and a wait
        // shorter than that -- `waitTimeoutSeconds: 3`, say -- the first candidate that
        // blocks outlives the entire wait, so the candidate behind it is never asked and a
        // host already holding the requested file yields `host_timeout`. The deadline has
        // to be a share of what is left, not a constant.
        var blocks = Host("blocks", RequestedFilePath);
        var holdsTheFile = Host("holds-the-file", RequestedFilePath);
        var probed = new List<string>();
        var wait = TimeSpan.FromMilliseconds(600);

        // The wait's own deadline, which is what the startup monitor supplies. Without it
        // an over-long per-probe timeout only makes this slower; with it, the blocked
        // candidate takes the whole wait and the second is never reached.
        using var waitDeadline = new CancellationTokenSource(wait);

        var target = await NavisworksLaunchService.ResolveProvenHandoffAsync(
            new[] { blocks, holdsTheFile },
            RequestedFilePath,
            new NavisworksLaunchService.HandoffProofLedger(),
            async (candidate, token) =>
            {
                probed.Add(candidate.InstanceId);
                if (candidate.InstanceId == "blocks")
                    await Task.Delay(TimeSpan.FromMinutes(5), token);

                return new HostStatusResponse { DocumentFileName = DocumentOf(candidate) };
            },
            () => Now,
            waitDeadline.Token,
            // Far below the five-second default, so only a shared budget reaches the
            // second candidate before the deadline above fires.
            remainingWait: wait);

        Assert.Same(holdsTheFile, target);
        Assert.Equal(new[] { "blocks", "holds-the-file" }, probed);
    }

    [Fact]
    public async Task ARefusalExpiresSoAHostStillBeingHandedTheFileIsNotWrittenOff()
    {
        // Why a refusal is not final. Roamer changes an existing instance's document while
        // that instance keeps the same title, so a candidate can answer with its previous
        // path and hold the requested one moments later. Caching the refusal for the whole
        // wait would turn that into a host_timeout over a document that did finish loading.
        var transitioning = Host("transitioning", "D:\\nh-l3-b\\6501.5.nwd");
        var ledger = new NavisworksLaunchService.HandoffProofLedger(
            retryRefusalsAfter: TimeSpan.FromSeconds(2));

        var duringTransition = await NavisworksLaunchService.ResolveProvenHandoffAsync(
            new[] { transitioning },
            RequestedFilePath,
            ledger,
            (candidate, _) => Task.FromResult(new HostStatusResponse
            {
                DocumentFileName = DocumentOf(candidate),
            }),
            () => Now,
            CancellationToken.None);

        Assert.Null(duringTransition);

        // The hand-off completes: this instance now holds the requested file.
        Documents["transitioning"] = RequestedFilePath;

        // Still inside the retry interval, so it is not asked again yet.
        Assert.Null(await NavisworksLaunchService.ResolveProvenHandoffAsync(
            new[] { transitioning },
            RequestedFilePath,
            ledger,
            (candidate, _) => Task.FromResult(new HostStatusResponse
            {
                DocumentFileName = DocumentOf(candidate),
            }),
            () => Now.AddSeconds(1),
            CancellationToken.None));

        // Past it, the question is asked again and the host is found.
        var afterTransition = await NavisworksLaunchService.ResolveProvenHandoffAsync(
            new[] { transitioning },
            RequestedFilePath,
            ledger,
            (candidate, _) => Task.FromResult(new HostStatusResponse
            {
                DocumentFileName = DocumentOf(candidate),
            }),
            () => Now.AddSeconds(3),
            CancellationToken.None);

        Assert.Same(transitioning, afterTransition);
    }

    [Fact]
    public async Task ACandidateThatBurnsItsWholeDeadlineIsNotAskedAgainNextPoll()
    {
        // A probe that used its entire deadline without answering is the expensive kind of
        // failure, and it is recorded so the next poll moves past it. Left unrecorded, the
        // same blocked instance was re-probed every 250 ms for the length of the wait,
        // spending most of each poll on a host that had already failed to answer.
        var blocks = Host("blocks", RequestedFilePath);
        var ledger = new NavisworksLaunchService.HandoffProofLedger(
            retryRefusalsAfter: TimeSpan.FromSeconds(2));
        var probes = 0;

        for (var poll = 0; poll < 3; poll++)
        {
            Assert.Null(await NavisworksLaunchService.ResolveProvenHandoffAsync(
                new[] { blocks },
                RequestedFilePath,
                ledger,
                async (_, token) =>
                {
                    probes++;
                    await Task.Delay(TimeSpan.FromMinutes(5), token);
                    return new HostStatusResponse { DocumentFileName = RequestedFilePath };
                },
                () => Now,
                CancellationToken.None,
                perProbeTimeout: TimeSpan.FromMilliseconds(80)));
        }

        Assert.Equal(1, probes);

        // And still only a throttle, not a verdict: past the interval it is asked again,
        // because an instance that was busy may since have answered.
        Assert.True(ledger.ShouldProbe("blocks", Now.AddSeconds(3)));
    }

    [Fact]
    public async Task ARefusalIsStampedWhenTheProbeEndsNotWhenItStarted()
    {
        // The probe itself takes time -- up to its whole deadline, five seconds by default,
        // against a two-second retry interval. Stamping the refusal with the time the probe
        // *began* records it as already expired, so the next poll re-probes the same blocked
        // instance immediately and the throttle does nothing. The clock therefore has to be
        // read when the verdict is recorded, which is why this takes a clock and not a
        // timestamp.
        var blocks = Host("blocks", RequestedFilePath);
        var ledger = new NavisworksLaunchService.HandoffProofLedger(
            retryRefusalsAfter: TimeSpan.FromSeconds(2));
        var clock = Now;
        var probes = 0;

        for (var poll = 0; poll < 2; poll++)
        {
            Assert.Null(await NavisworksLaunchService.ResolveProvenHandoffAsync(
                new[] { blocks },
                RequestedFilePath,
                ledger,
                async (_, token) =>
                {
                    probes++;
                    // The probe burns more than the retry interval before failing, which is
                    // the situation a fixed clock cannot reproduce.
                    clock = clock.AddSeconds(5);
                    await Task.Delay(TimeSpan.FromMinutes(5), token);
                    return new HostStatusResponse { DocumentFileName = RequestedFilePath };
                },
                () => clock,
                CancellationToken.None,
                perProbeTimeout: TimeSpan.FromMilliseconds(80)));
        }

        Assert.Equal(1, probes);
    }

    [Fact]
    public async Task AnUnresponsiveCandidateIsAskedAgainOnTheNextPoll()
    {
        // Not the same as a candidate that answered and failed. An instance still loading
        // a large model may not answer at all, and recording that as a verdict would rule
        // out the very host the launch is waiting for.
        var loading = Host("loading", RequestedFilePath);
        var ledger = new NavisworksLaunchService.HandoffProofLedger();
        var probes = 0;

        var first = await NavisworksLaunchService.ResolveProvenHandoffAsync(
            new[] { loading },
            RequestedFilePath,
            ledger,
            (_, _) =>
            {
                probes++;
                throw new InvalidOperationException("still loading");
            },
            () => Now,
            CancellationToken.None);

        Assert.Null(first);
        // Nothing recorded at all, so the very next poll asks again rather than waiting
        // out a refusal interval it never earned.
        Assert.True(ledger.ShouldProbe("loading", Now));

        var second = await NavisworksLaunchService.ResolveProvenHandoffAsync(
            new[] { loading },
            RequestedFilePath,
            ledger,
            (candidate, _) =>
            {
                probes++;
                return Task.FromResult(new HostStatusResponse { DocumentFileName = DocumentOf(candidate) });
            },
            () => Now,
            CancellationToken.None);

        Assert.Same(loading, second);
        Assert.Equal(2, probes);
    }

    [Fact]
    public async Task CallerCancellationDuringTheHandoffProbeIsNotSwallowed()
    {
        // The catch-all that tolerates an unresponsive host must not also swallow the
        // caller giving up, or start_navisworks would keep polling for a request nobody
        // is waiting on.
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            NavisworksLaunchService.ResolveProvenHandoffAsync(
                new[] { Host("candidate", RequestedFilePath) },
                RequestedFilePath,
                new NavisworksLaunchService.HandoffProofLedger(),
                (_, token) =>
                {
                    cts.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.FromResult<HostStatusResponse>(null);
                },
                () => Now,
                cts.Token));
    }

    [Fact]
    public async Task TheFirstProvenCandidateWinsAndTheRestAreLeftAlone()
    {
        var proven = Host("proven", RequestedFilePath);
        var other = Host("other", RequestedFilePath);
        var probed = new List<string>();

        var target = await NavisworksLaunchService.ResolveProvenHandoffAsync(
            new[] { proven, other },
            RequestedFilePath,
            new NavisworksLaunchService.HandoffProofLedger(),
            (candidate, _) =>
            {
                probed.Add(candidate.InstanceId);
                return Task.FromResult(new HostStatusResponse { DocumentFileName = DocumentOf(candidate) });
            },
            () => Now,
            CancellationToken.None);

        Assert.Same(proven, target);
        Assert.Equal(new[] { "proven" }, probed);
    }
}
