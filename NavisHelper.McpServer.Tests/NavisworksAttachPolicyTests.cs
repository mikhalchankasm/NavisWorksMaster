using System;
using System.Collections.Generic;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// Covers the attach-versus-launch decision. The account of why it exists and what it
/// deliberately does not cover lives in `docs/MCP_TOOL_CONTRACTS.md` under *Attaching
/// instead of starting a second process*.
/// </summary>
public sealed class NavisworksAttachPolicyTests
{
    private static NavisworksHostInfo Host(
        int pid,
        string version,
        string documentTitle,
        int startedMinutesAgo = 0)
    {
        return new NavisworksHostInfo
        {
            Pid = pid,
            NavisworksVersion = version,
            DocumentTitle = documentTitle,
            InstanceId = "nw-" + version + "-" + pid,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-startedMinutesAgo),
        };
    }

    [Fact]
    public void AReadyHostWithTheRequestedDocumentIsACandidate()
    {
        var hosts = new List<NavisworksHostInfo> { Host(31924, "2027", "6501.5.nwd") };

        var candidate = NavisworksAttachPolicy.SelectAttachCandidate(hosts, "2027", @"D:\Downloads\6501.5.nwd");

        Assert.NotNull(candidate);
        Assert.Equal(31924, candidate.Pid);
    }

    [Fact]
    public void AHostWithADifferentDocumentIsNotACandidate()
    {
        // This is the reported sequence: the running host has no document, and nothing
        // can open one in it, so a process still has to start. The defect there is that
        // the caller was not told what it had just created -- see the warning tests.
        var hosts = new List<NavisworksHostInfo> { Host(50424, "2027", string.Empty) };

        Assert.Null(NavisworksAttachPolicy.SelectAttachCandidate(hosts, "2027", @"D:\Downloads\6501.5.nwd"));
    }

    [Fact]
    public void AHostOfAnotherVersionIsNotACandidate()
    {
        var hosts = new List<NavisworksHostInfo> { Host(1000, "2026", "6501.5.nwd") };

        Assert.Null(NavisworksAttachPolicy.SelectAttachCandidate(hosts, "2027", @"D:\Downloads\6501.5.nwd"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AStartWithNoFileAlwaysLaunches(string requestedFilePath)
    {
        // "Start Navisworks" is not "give me whatever is running". With no document
        // named there is nothing to check a running host against, and a caller may
        // legitimately want a second instance.
        var hosts = new List<NavisworksHostInfo> { Host(50424, "2027", "6501.5.nwd") };

        Assert.Null(NavisworksAttachPolicy.SelectAttachCandidate(hosts, "2027", requestedFilePath));
    }

    [Fact]
    public void AnInvisibleHostStillLaunches()
    {
        // A host appears in the discovery list only once registered, so a
        // running-but-not-yet-ready instance is invisible here. An empty list is exactly
        // how it presents.
        Assert.Null(NavisworksAttachPolicy.SelectAttachCandidate(new List<NavisworksHostInfo>(), "2027", @"D:\x\6501.5.nwd"));
        Assert.Null(NavisworksAttachPolicy.SelectAttachCandidate(null, "2027", @"D:\x\6501.5.nwd"));
    }

    [Fact]
    public void TheMostRecentlyStartedMatchingHostWins()
    {
        var hosts = new List<NavisworksHostInfo>
        {
            Host(1, "2027", "6501.5.nwd", startedMinutesAgo: 30),
            Host(2, "2027", "6501.5.nwd", startedMinutesAgo: 1),
            Host(3, "2027", "other.nwd", startedMinutesAgo: 0),
        };

        Assert.Equal(2, NavisworksAttachPolicy.SelectAttachCandidate(hosts, "2027", @"D:\Downloads\6501.5.nwd").Pid);
    }

    [Fact]
    public void ASameNamedFileFromAnotherDirectoryIsOnlyACandidate()
    {
        // Discovery reports a document title, so this host may hold a different model
        // with the same file name. It comes back as a candidate and must not be attached
        // to until the path is proven: a review found that attaching here would report
        // success without opening the requested file, and later write tools would then
        // act on the wrong model.
        var hosts = new List<NavisworksHostInfo> { Host(7, "2027", "6501.5.nwd") };

        Assert.NotNull(NavisworksAttachPolicy.SelectAttachCandidate(hosts, "2027", @"C:\elsewhere\6501.5.nwd"));
        Assert.False(NavisworksAttachPolicy.DocumentPathMatches(@"D:\Downloads\6501.5.nwd", @"C:\elsewhere\6501.5.nwd"));
    }

    [Fact]
    public void TheSameFullPathIsProven()
    {
        Assert.True(NavisworksAttachPolicy.DocumentPathMatches(@"D:\Downloads\6501.5.nwd", @"D:\Downloads\6501.5.nwd"));
    }

    [Fact]
    public void PathComparisonIgnoresCaseAndNormalises()
    {
        // Windows paths are case-insensitive, and a caller may pass an unnormalised one.
        Assert.True(NavisworksAttachPolicy.DocumentPathMatches(@"D:\Downloads\6501.5.nwd", @"d:\downloads\6501.5.NWD"));
        Assert.True(NavisworksAttachPolicy.DocumentPathMatches(@"D:\Downloads\6501.5.nwd", @"D:\Downloads\.\6501.5.nwd"));
    }

    [Theory]
    [InlineData(null, @"D:\a.nwd")]
    [InlineData("", @"D:\a.nwd")]
    [InlineData("   ", @"D:\a.nwd")]
    [InlineData(@"D:\a.nwd", null)]
    [InlineData(@"D:\a.nwd", "")]
    public void AnUnknownPathOnEitherSideIsNotProven(string hostDocumentFileName, string requestedFilePath)
    {
        // Proving nothing must mean launching, not attaching. A wrong attach costs a
        // write tool acting on another model; a needless launch costs a warned-about
        // second host.
        Assert.False(NavisworksAttachPolicy.DocumentPathMatches(hostDocumentFileName, requestedFilePath));
    }

    [Fact]
    public void TheUnprovenCandidateMessageSaysWhyItLaunched()
    {
        var text = NavisworksAttachPolicy.CandidateDocumentNotProvenMessage;

        Assert.Contains("same file name", text, StringComparison.Ordinal);
        Assert.Contains("full path", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"D:\Downloads\6501.5.nwd", "6501.5.nwd")]
    [InlineData("6501.5.nwd", "6501.5.nwd")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void TheExpectedTitleIsTheFileName(string requestedFilePath, string expected)
    {
        Assert.Equal(expected, NavisworksAttachPolicy.GetExpectedDocumentTitle(requestedFilePath));
    }

    [Fact]
    public void MoreThanOneHostOfTheVersionIsWarnedAbout()
    {
        var warning = NavisworksAttachPolicy.BuildAdditionalHostWarning(2, "2027");

        Assert.NotNull(warning);
        Assert.Contains("multiple_hosts_detected", warning, StringComparison.Ordinal);
        Assert.Contains("instanceId", warning, StringComparison.Ordinal);
        Assert.Contains("close_navisworks", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-1)]
    public void OneHostOrNoneIsNotWarnedAbout(int readyHostsAfter)
    {
        // Counted after the launch, not predicted before it. Roamer can hand a file off
        // into an instance that is already running, so a pre-launch count of one would
        // have predicted a second host that does not exist and told the caller to close
        // it. A review caught that.
        Assert.Null(NavisworksAttachPolicy.BuildAdditionalHostWarning(readyHostsAfter, "2027"));
    }

    [Fact]
    public void CountingReadyHostsIsScopedToTheVersion()
    {
        var hosts = new List<NavisworksHostInfo>
        {
            Host(1, "2027", "a.nwd"),
            Host(2, "2027", string.Empty),
            Host(3, "2026", "a.nwd"),
        };

        Assert.Equal(2, NavisworksAttachPolicy.CountReadyHostsOfVersion(hosts, "2027"));
        Assert.Equal(1, NavisworksAttachPolicy.CountReadyHostsOfVersion(hosts, "2026"));
        Assert.Equal(0, NavisworksAttachPolicy.CountReadyHostsOfVersion(null, "2027"));
    }

    [Fact]
    public void AHostRunningInAnotherProcessThanTheOneStartedIsCalledOut()
    {
        // The conflated response a peer session found: processId 42284 paired with
        // host.pid 57488 and the requested file as the document title, which reads as
        // correct to exactly the caller it misleads.
        var warning = NavisworksAttachPolicy.BuildHostProcessMismatchWarning(42284, 57488);

        Assert.NotNull(warning);
        Assert.Contains("57488", warning, StringComparison.Ordinal);
        Assert.Contains("42284", warning, StringComparison.Ordinal);
        Assert.Contains("list_navisworks_hosts", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1234, 1234)]
    [InlineData(null, 1234)]
    [InlineData(1234, null)]
    [InlineData(null, null)]
    public void AMatchingOrUnknownProcessIsNotCalledOut(int? startedProcessId, int? hostPid)
    {
        Assert.Null(NavisworksAttachPolicy.BuildHostProcessMismatchWarning(startedProcessId, hostPid));
    }
}
