using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashTestTypeHelperTests
{
    [Theory]
    [InlineData("hard", "hard")]
    [InlineData("intersection", "hard")]
    [InlineData("hard intersection", "hard")]
    [InlineData("intersect", "hard")]
    [InlineData("пересечение", "hard")]
    [InlineData("по пересечению", "hard")]
    [InlineData("hard_conservative", "hard_conservative")]
    [InlineData("hard-conservative", "hard_conservative")]
    [InlineData("conservative", "hard_conservative")]
    [InlineData("conservative (hard conservative)", "hard_conservative")]
    [InlineData("hard conservative (conservative)", "hard_conservative")]
    [InlineData("консервативно", "hard_conservative")]
    [InlineData("пересечение консервативно", "hard_conservative")]
    [InlineData("по пересечению консервативно", "hard_conservative")]
    [InlineData("clearance", "clearance")]
    [InlineData("clear", "clearance")]
    [InlineData("gap", "clearance")]
    [InlineData("просвет", "clearance")]
    [InlineData("duplicate", "duplicate")]
    [InlineData("duplicates", "duplicate")]
    [InlineData("duplication", "duplicate")]
    [InlineData("дублирование", "duplicate")]
    [InlineData("дубликаты", "duplicate")]
    public void NormalizeTestType_ReturnsCanonicalTypeForAliases(string value, string expected)
    {
        Assert.Equal(expected, ClashTestTypeHelper.NormalizeTestType(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("unknown")]
    [InlineData("harder")]
    public void NormalizeTestType_ReturnsNullForEmptyOrInvalidValues(string value)
    {
        Assert.Null(ClashTestTypeHelper.NormalizeTestType(value));
    }
}
