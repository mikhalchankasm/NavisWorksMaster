using System;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashTestNamePrefixHelperTests
{
    [Theory]
    [InlineData(null, "NH-BBOX")]
    [InlineData("", "NH-BBOX")]
    [InlineData(" Custom ", "Custom")]
    [InlineData("A\r\nB", "A__B")]
    public void NormalizePairTestPrefix_AppliesDefaultTrimAndSanitize(string value, string expected)
    {
        Assert.Equal(expected, ClashTestNamePrefixHelper.NormalizePairTestPrefix(value));
    }

    [Fact]
    public void NormalizePairTestPrefix_TruncatesToFortyCharacters()
    {
        var result = ClashTestNamePrefixHelper.NormalizePairTestPrefix("1234567890123456789012345678901234567890XYZ");

        Assert.Equal("1234567890123456789012345678901234567890", result);
    }

    [Theory]
    [InlineData(null, false, "")]
    [InlineData("", false, "")]
    [InlineData(null, true, "[NH-MATRIX] 20260707_080910 ")]
    [InlineData("", true, "[NH-MATRIX] 20260707_080910 ")]
    [InlineData("Matrix", false, "Matrix ")]
    [InlineData(" Matrix ", true, "Matrix ")]
    [InlineData("Matrix yyyyMMdd_HHmmss", false, "Matrix 20260707_080910 ")]
    [InlineData("A\r\nB", false, "A B ")]
    [InlineData("A  B", false, "A B ")]
    public void NormalizeMatrixPrefix_AppliesExistingRuntimeRules(string value, bool useGeneratedPrefix, string expected)
    {
        var timestamp = new DateTime(2026, 7, 7, 8, 9, 10);

        Assert.Equal(expected, ClashTestNamePrefixHelper.NormalizeMatrixPrefix(value, useGeneratedPrefix, timestamp));
    }

    [Fact]
    public void NormalizeMatrixPrefix_PreservesExistingTrailingSpace()
    {
        var timestamp = new DateTime(2026, 7, 7, 8, 9, 10);

        var result = ClashTestNamePrefixHelper.NormalizeMatrixPrefix("Already ", false, timestamp);

        Assert.Equal("Already ", result);
    }
}
