using System.Collections.Generic;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class FindItemsConditionGroupsTests
{
    [Fact]
    public void Split_JoinsAllConditionsIntoOneGroup_WhenNoConditionIsOr()
    {
        var conditions = new List<FindItemsCondition>
        {
            Condition("wall"),
            Condition("door", FindItemsConditionOptionsHelper.And),
            Condition("slab", FindItemsConditionOptionsHelper.And),
        };

        var groups = FindItemsConditionGroups.Split(conditions);

        var group = Assert.Single(groups);
        Assert.Equal(new List<int> { 0, 1, 2 }, group);
    }

    [Fact]
    public void Split_SeparatesEveryCondition_WhenEveryConditionIsOr()
    {
        var conditions = new List<FindItemsCondition>
        {
            Condition("wall"),
            Condition("door", FindItemsConditionOptionsHelper.Or),
            Condition("slab", FindItemsConditionOptionsHelper.Or),
        };

        var groups = FindItemsConditionGroups.Split(conditions);

        Assert.Equal(3, groups.Count);
        Assert.Equal(new List<int> { 0 }, groups[0]);
        Assert.Equal(new List<int> { 1 }, groups[1]);
        Assert.Equal(new List<int> { 2 }, groups[2]);
    }

    [Fact]
    public void Split_SplitsBeforeEachOrCondition_InMixedConditions()
    {
        var conditions = new List<FindItemsCondition>
        {
            Condition("wall"),
            Condition("door", FindItemsConditionOptionsHelper.And),
            Condition("slab", FindItemsConditionOptionsHelper.Or),
            Condition("column", FindItemsConditionOptionsHelper.And),
        };

        var groups = FindItemsConditionGroups.Split(conditions);

        Assert.Equal(2, groups.Count);
        Assert.Equal(new List<int> { 0, 1 }, groups[0]);
        Assert.Equal(new List<int> { 2, 3 }, groups[1]);
    }

    [Fact]
    public void Split_ReturnsNoGroups_ForNullOrEmptyConditions()
    {
        Assert.Empty(FindItemsConditionGroups.Split(null));
        Assert.Empty(FindItemsConditionGroups.Split(new List<FindItemsCondition>()));
    }

    [Fact]
    public void Split_IgnoresTheFirstConditionLogicalOperator()
    {
        var conditions = new List<FindItemsCondition>
        {
            Condition("wall", FindItemsConditionOptionsHelper.Or),
            Condition("door", FindItemsConditionOptionsHelper.And),
        };

        var groups = FindItemsConditionGroups.Split(conditions);

        var group = Assert.Single(groups);
        Assert.Equal(new List<int> { 0, 1 }, group);
    }

    [Fact]
    public void Split_TreatsNullOrNonOrOperatorsAsGroupContinuations()
    {
        var conditions = new List<FindItemsCondition>
        {
            Condition("wall"),
            null,
            Condition("door", "OR"),
            Condition("slab", "xor"),
        };

        var groups = FindItemsConditionGroups.Split(conditions);

        Assert.Equal(2, groups.Count);
        Assert.Equal(new List<int> { 0, 1 }, groups[0]);
        Assert.Equal(new List<int> { 2, 3 }, groups[1]);
    }

    private static FindItemsCondition Condition(string value, string logicalOperator = null)
    {
        return new FindItemsCondition
        {
            Property = "Name",
            Operator = FindItemsComparisons.Equal,
            Value = value,
            LogicalOperator = logicalOperator,
        };
    }
}
