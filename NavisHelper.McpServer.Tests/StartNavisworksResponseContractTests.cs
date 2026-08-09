using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class StartNavisworksResponseContractTests
{
    [Fact]
    public void Response_PreservesLegacyMembersAndAddsStartupDiagnostics()
    {
        var propertyNames = typeof(StartNavisworksResponse)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        var legacyNames = new[]
        {
            "Started", "ProcessId", "NavisworksVersion", "RoamerPath", "FilePath",
            "OpenedRecentFile", "RecentFile", "WaitedForHost", "HostReady", "Host",
            "ElapsedMs", "ElapsedHuman", "Message", "Warnings",
        };
        var addedNames = new[]
        {
            "ProcessCreated", "ProcessExited", "ExitCode", "Outcome",
            "FailureReason", "StartupElapsedMs",
        };

        Assert.All(legacyNames, name => Assert.Contains(name, propertyNames));
        Assert.All(addedNames, name => Assert.Contains(name, propertyNames));
    }
}
