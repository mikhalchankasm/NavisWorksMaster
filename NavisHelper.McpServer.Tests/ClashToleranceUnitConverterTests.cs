using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashToleranceUnitConverterTests
{
    [Theory]
    [InlineData("Centimeters", 10.0)]
    [InlineData("Meters", 1000.0)]
    [InlineData("Kilometers", 1000000.0)]
    [InlineData("Inches", 25.4)]
    [InlineData("Feet", 304.8)]
    [InlineData("Yards", 914.4)]
    [InlineData("Miles", 1609344.0)]
    [InlineData("Millimeters", 1.0)]
    public void ToMillimeters_PreservesExistingNavisworksUnitConversions(string unitName, double expected)
    {
        Assert.Equal(expected, ClashToleranceUnitConverter.ToMillimeters(1.0, unitName), 12);
    }

    [Theory]
    [InlineData("Micrometers", 0.001)]
    [InlineData("Mils", 0.0254)]
    [InlineData("Microinches", 0.0000254)]
    public void ToMillimeters_ConvertsSmallNavisworksUnitsWithoutMeterFallback(string unitName, double expected)
    {
        var actual = ClashToleranceUnitConverter.ToMillimeters(1.0, unitName);

        Assert.Equal(expected, actual, 12);
        Assert.True(actual < 1.0);
        Assert.NotEqual(1000.0, actual);
    }

    [Fact]
    public void ToMillimeters_RejectsUnknownUnitsInsteadOfAssumingMeters()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ClashToleranceUnitConverter.ToMillimeters(1.0, "Unknown"));
    }
}
