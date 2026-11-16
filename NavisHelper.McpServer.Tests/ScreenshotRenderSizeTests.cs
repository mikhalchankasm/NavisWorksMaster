using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #493: screenshots must render at the profile's size, not at the Image Export dialog's stored size.
public sealed class ScreenshotRenderSizeTests
{
    [Theory]
    [InlineData("extra_high")]
    [InlineData("ultra")]
    [InlineData("4k")]
    public void ExtraHighProfile_Is3840x2160Jpeg(string profile)
    {
        var options = ClashReportOptionHelper.NormalizeScreenshotOptions(profile, null, null, null, null, out var error);

        Assert.Equal(string.Empty, error);
        Assert.NotNull(options);
        Assert.Equal("extra_high", options.Profile);
        Assert.Equal(3840, options.MaxWidth);
        Assert.Equal(2160, options.MaxHeight);
        Assert.Equal("jpg", options.Format);
    }

    [Theory]
    [InlineData("compact", 1745, 1144, 1280, 720)]
    [InlineData("fullhd", 1745, 1144, 1920, 1080)]
    [InlineData("high", 256, 256, 2560, 1440)]
    [InlineData("extra_high", 800, 600, 3840, 2160)]
    [InlineData("source", 1745, 1144, 1745, 1144)]
    public void RenderSize_IsTheProfileSize_OrTheViewSizeForSource(
        string profile, int viewWidth, int viewHeight, int expectedWidth, int expectedHeight)
    {
        var options = ClashReportOptionHelper.NormalizeScreenshotOptions(profile, null, null, null, null, out _);

        ClashReportOptionHelper.ResolveRenderSize(options, viewWidth, viewHeight, out var width, out var height);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    [Fact]
    public void ExplicitMaxSize_IsTheRenderSize()
    {
        var options = ClashReportOptionHelper.NormalizeScreenshotOptions("compact", null, 3000, 2000, null, out _);

        ClashReportOptionHelper.ResolveRenderSize(options, 1745, 1144, out var width, out var height);

        Assert.Equal(3000, width);
        Assert.Equal(2000, height);
    }
}
