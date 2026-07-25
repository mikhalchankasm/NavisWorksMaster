using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class RootSearchSuggestionHelperTests
{
    [Fact]
    public void Rank_ReturnsClosestFilenameForTypo()
    {
        var result = RootSearchSuggestionHelper.Rank("KKS.rvmm", new[] { "Other.rvm", "KKS.rvm", "KKS-2.rvm" }, 5);
        Assert.Equal("KKS.rvm", result[0]);
    }

    [Fact]
    public void Rank_NormalizesPathsAndLimitsResults()
    {
        var result = RootSearchSuggestionHelper.Rank("Pump", new[] { @"D:\Models\Pump.rvm", "Pump-2.rvm", "Pump-3.rvm" }, 2);
        Assert.Equal(2, result.Count);
        Assert.Equal(@"D:\Models\Pump.rvm", result[0]);
    }
}
