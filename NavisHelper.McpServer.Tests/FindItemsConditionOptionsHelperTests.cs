using System;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class FindItemsConditionOptionsHelperTests
{
    [Theory]
    [InlineData(null, "and")]
    [InlineData(" AND ", "and")]
    [InlineData("or", "or")]
    public void NormalizeLogicalOperator_UsesConditionLevelOperators(string value, string expected)
    {
        Assert.Equal(expected, FindItemsConditionOptionsHelper.NormalizeLogicalOperator(value));
    }

    [Fact]
    public void NormalizeLogicalOperator_RejectsUnknownValue()
    {
        Assert.Throws<ArgumentException>(() => FindItemsConditionOptionsHelper.NormalizeLogicalOperator("any"));
    }
}
