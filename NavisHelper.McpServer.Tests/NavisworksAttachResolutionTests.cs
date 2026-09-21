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
}
