using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SelectionClusterPlanningHelperTests
{
    [Fact]
    public void CountMode_Splits666ItemsIntoThreeTarget300Clusters()
    {
        Assert.Equal(3, SelectionClusterPlanningHelper.GetCountClusterCount(666, 300));
    }

    [Fact]
    public void CountMode_UsesTwoBalancedClustersFor301Items()
    {
        Assert.Equal(2, SelectionClusterPlanningHelper.GetCountClusterCount(301, 300));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(49_999, 0)]
    [InlineData(50_000, 1)]
    [InlineData(-1, -1)]
    public void GridMode_UsesStableGlobalFloorCells(double coordinateMm, long expectedCell)
    {
        Assert.Equal(expectedCell, SelectionClusterPlanningHelper.GetGridIndex(coordinateMm, 50_000));
    }
}
