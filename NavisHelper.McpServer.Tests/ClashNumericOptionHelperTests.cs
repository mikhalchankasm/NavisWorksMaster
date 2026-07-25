using System;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashNumericOptionHelperTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(42, 42)]
    public void ClampNonNegative_ClampsNegativeValuesToZero(int? value, int expected)
    {
        Assert.Equal(expected, ClashNumericOptionHelper.ClampNonNegative(value));
    }

    [Theory]
    [InlineData(null, 1500, true, 1500)]
    [InlineData(1.0, 1500, true, 1)]
    [InlineData(0.0, 1500, false, 0)]
    [InlineData(-1.0, 1500, false, -1)]
    [InlineData(double.PositiveInfinity, 1500, true, double.PositiveInfinity)]
    public void TryNormalizePositiveDouble_PreservesExistingGreaterThanZeroSemantics(double? value, double defaultValue, bool expectedOk, double expected)
    {
        var ok = ClashNumericOptionHelper.TryNormalizePositiveDouble(value, defaultValue, out var result);

        Assert.Equal(expectedOk, ok);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryNormalizePositiveDouble_PreservesExistingNaNAcceptance()
    {
        var ok = ClashNumericOptionHelper.TryNormalizePositiveDouble(double.NaN, 1500, out var result);

        Assert.True(ok);
        Assert.True(double.IsNaN(result));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1.25, true)]
    [InlineData(-0.1, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    [InlineData(double.NegativeInfinity, false)]
    public void TryNormalizeNonNegativeDouble_RejectsNaNInfinityAndNegativeValues(double value, bool expectedOk)
    {
        var ok = ClashNumericOptionHelper.TryNormalizeNonNegativeDouble(value, out var result);

        Assert.Equal(expectedOk, ok);
        Assert.Equal(value, result);
    }

    [Theory]
    [InlineData(null, 0.5, true, 0.5)]
    [InlineData(0.0, 0.5, true, 0)]
    [InlineData(1.0, 0.5, true, 1)]
    [InlineData(-0.01, 0.5, false, -0.01)]
    [InlineData(1.01, 0.5, false, 1.01)]
    public void TryNormalizeUnitDouble_PreservesExistingZeroToOneSemantics(double? value, double defaultValue, bool expectedOk, double expected)
    {
        var ok = ClashNumericOptionHelper.TryNormalizeUnitDouble(value, defaultValue, out var result);

        Assert.Equal(expectedOk, ok);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TryNormalizeUnitDouble_PreservesExistingNaNAcceptance()
    {
        var ok = ClashNumericOptionHelper.TryNormalizeUnitDouble(double.NaN, 0.5, out var result);

        Assert.True(ok);
        Assert.True(double.IsNaN(result));
    }
}
