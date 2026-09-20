using System;
using System.Collections.Generic;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// Observed live on 2026-09-20, Navisworks 2027: `start_navisworks` produced a ready
/// host with no document, `open_latest_navisworks_file` then produced a **second**
/// process, and every following call failed with `multiple_hosts_detected` until one
/// was closed. A second session reproduced it with the requested file already open in
/// the running host — the case where launching another process is provably wrong.
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
    public void AReadyHostWithTheRequestedDocumentIsAttachedTo()
    {
        var hosts = new List<NavisworksHostInfo> { Host(31924, "2027", "6501.5.nwd") };

        var target = NavisworksAttachPolicy.SelectAttachTarget(hosts, "2027", @"D:\Downloads\6501.5.nwd");

        Assert.NotNull(target);
        Assert.Equal(31924, target.Pid);
    }

    [Fact]
    public void AHostWithADifferentDocumentIsNotAttachedTo()
    {
        // This is the reported sequence: the running host has no document, and nothing
        // can open one in it, so a process still has to start. The defect there is that
        // the caller was not told what it had just created -- see the warning tests.
        var hosts = new List<NavisworksHostInfo> { Host(50424, "2027", string.Empty) };

        Assert.Null(NavisworksAttachPolicy.SelectAttachTarget(hosts, "2027", @"D:\Downloads\6501.5.nwd"));
    }

    [Fact]
    public void AHostOfAnotherVersionIsNotAttachedTo()
    {
        // The case the task asked to preserve: a different version must still launch.
        var hosts = new List<NavisworksHostInfo> { Host(1000, "2026", "6501.5.nwd") };

        Assert.Null(NavisworksAttachPolicy.SelectAttachTarget(hosts, "2027", @"D:\Downloads\6501.5.nwd"));
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

        Assert.Null(NavisworksAttachPolicy.SelectAttachTarget(hosts, "2027", requestedFilePath));
    }

    [Fact]
    public void AnInvisibleHostStillLaunches()
    {
        // A host appears in the discovery list only once registered, so a
        // running-but-not-yet-ready instance is invisible here. That is the other case
        // the task asked to preserve, and an empty list is exactly how it presents.
        Assert.Null(NavisworksAttachPolicy.SelectAttachTarget(new List<NavisworksHostInfo>(), "2027", @"D:\x\6501.5.nwd"));
        Assert.Null(NavisworksAttachPolicy.SelectAttachTarget(null, "2027", @"D:\x\6501.5.nwd"));
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

        var target = NavisworksAttachPolicy.SelectAttachTarget(hosts, "2027", @"D:\Downloads\6501.5.nwd");

        Assert.Equal(2, target.Pid);
    }

    [Fact]
    public void OnlyTheFileNameHasToMatch_NotTheDirectory()
    {
        // A host reports its document title, not its path, so two different files with
        // the same name are indistinguishable here. Stated rather than hidden: this
        // policy can attach to a host holding a same-named file from another directory.
        var hosts = new List<NavisworksHostInfo> { Host(7, "2027", "6501.5.nwd") };

        Assert.NotNull(NavisworksAttachPolicy.SelectAttachTarget(hosts, "2027", @"C:\elsewhere\6501.5.nwd"));
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
    public void LaunchingBesideAnotherHostOfTheSameVersionIsWarnedAbout()
    {
        var warning = NavisworksAttachPolicy.BuildAdditionalHostWarning(1, "2027");

        Assert.NotNull(warning);
        Assert.Contains("multiple_hosts_detected", warning, StringComparison.Ordinal);
        Assert.Contains("instanceId", warning, StringComparison.Ordinal);
        Assert.Contains("close_navisworks", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchingAsTheOnlyHostIsNotWarnedAbout()
    {
        Assert.Null(NavisworksAttachPolicy.BuildAdditionalHostWarning(0, "2027"));
        Assert.Null(NavisworksAttachPolicy.BuildAdditionalHostWarning(-1, "2027"));
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
