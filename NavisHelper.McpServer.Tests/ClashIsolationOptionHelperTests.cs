using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashIsolationOptionHelperTests
{
    [Theory]
    [InlineData(null, "point")]
    [InlineData("", "point")]
    [InlineData("POINT", "point")]
    [InlineData("items", "items")]
    public void NormalizeBoxModeAcceptsSupportedValues(string value, string expected)
    {
        Assert.Equal(expected, ClashIsolationOptionHelper.NormalizeBoxMode(value));
    }

    [Fact]
    public void NormalizeBoxModeRejectsUnknownValue()
    {
        Assert.Null(ClashIsolationOptionHelper.NormalizeBoxMode("root"));
    }

    [Theory]
    [InlineData(null, "current")]
    [InlineData("ISO", "iso")]
    [InlineData("iso-opposite", "iso_opposite")]
    [InlineData("opposite", "iso_opposite")]
    [InlineData("top", "top")]
    [InlineData("front", "front")]
    [InlineData("back", "back")]
    [InlineData("left", "left")]
    [InlineData("right", "right")]
    [InlineData("custom", "custom")]
    public void NormalizeCameraModeAcceptsSupportedValues(string value, string expected)
    {
        Assert.Equal(expected, ClashIsolationOptionHelper.NormalizeCameraMode(value));
    }

    [Fact]
    public void NormalizeCameraModeRejectsUnknownValue()
    {
        Assert.Null(ClashIsolationOptionHelper.NormalizeCameraMode("orbit"));
    }

    [Theory]
    [InlineData(null, "current")]
    [InlineData("ORTHOGRAPHIC", "orthographic")]
    [InlineData("perspective", "perspective")]
    public void NormalizeProjectionAcceptsSupportedValues(string value, string expected)
    {
        Assert.Equal(expected, ClashIsolationOptionHelper.NormalizeProjection(value));
    }

    [Fact]
    public void IsFinitePointRejectsInvalidCoordinates()
    {
        Assert.True(ClashIsolationOptionHelper.IsFinitePoint(new Point3Info { X = 1, Y = 2, Z = 3 }));
        Assert.False(ClashIsolationOptionHelper.IsFinitePoint(null));
        Assert.False(ClashIsolationOptionHelper.IsFinitePoint(new Point3Info { X = double.NaN, Y = 2, Z = 3 }));
        Assert.False(ClashIsolationOptionHelper.IsFinitePoint(new Point3Info { X = 1, Y = double.PositiveInfinity, Z = 3 }));
    }

    [Theory]
    [InlineData("items", 0, true)]
    [InlineData("items", 500, true)]
    [InlineData("point", 0, false)]
    [InlineData("point", 0.001, true)]
    [InlineData("items", -1, false)]
    public void BoxOffsetValidationDependsOnBoxMode(string boxMode, double value, bool expected)
    {
        Assert.Equal(expected, ClashIsolationOptionHelper.IsValidBoxOffset(boxMode, value));
    }
}
