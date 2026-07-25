using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashHandleHelperTests
{
    [Theory]
    [InlineData(1, "clash-test:1")]
    [InlineData(42, "clash-test:42")]
    [InlineData(0, "")]
    [InlineData(-1, "")]
    public void BuildTestHandle_ReturnsCanonicalHandleOrEmpty(int testIndex, string expected)
    {
        Assert.Equal(expected, ClashHandleHelper.BuildTestHandle(testIndex));
    }

    [Theory]
    [InlineData(1, 1, "clash-result:1:1")]
    [InlineData(2, 99, "clash-result:2:99")]
    [InlineData(0, 1, "")]
    [InlineData(1, 0, "")]
    [InlineData(-1, 1, "")]
    [InlineData(1, -1, "")]
    public void BuildResultHandle_ReturnsCanonicalHandleOrEmpty(int testIndex, int resultIndex, string expected)
    {
        Assert.Equal(expected, ClashHandleHelper.BuildResultHandle(testIndex, resultIndex));
    }

    [Theory]
    [InlineData("clash-test:1", true, 1)]
    [InlineData(" CLASH-TEST:42 ", true, 42)]
    [InlineData("7", true, 7)]
    [InlineData("-2", true, -2)]
    [InlineData("clash-test:", false, 0)]
    [InlineData("clash-result:1:2", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void TryParseTestHandle_PreservesExistingParsingRules(string value, bool expectedOk, int expectedIndex)
    {
        var ok = ClashHandleHelper.TryParseTestHandle(value, out var testIndex);

        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedIndex, testIndex);
    }

    [Theory]
    [InlineData("clash-result:1:2", true, 1, 2)]
    [InlineData(" CLASH-RESULT:42:7 ", true, 42, 7)]
    [InlineData("clash-result:0:1", false, 0, 1)]
    [InlineData("clash-result:1:0", false, 1, 0)]
    [InlineData("clash-test:1", false, 0, 0)]
    [InlineData("", false, 0, 0)]
    [InlineData(null, false, 0, 0)]
    public void TryParseResultHandle_RequiresCanonicalPositiveIndexes(string value, bool expectedOk, int expectedTest, int expectedResult)
    {
        var ok = ClashHandleHelper.TryParseResultHandle(value, out var testIndex, out var resultIndex);

        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedTest, testIndex);
        Assert.Equal(expectedResult, resultIndex);
    }
}
