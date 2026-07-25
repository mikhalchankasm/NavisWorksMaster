using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashReportOptionHelperTests
{
    [Theory]
    [InlineData(null, 25)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(300, 200)]
    public void ClampClusterMembersInHtml_AppliesDefaultMinimumAndMaximum(int? value, int expected)
    {
        Assert.Equal(expected, ClashReportOptionHelper.ClampClusterMembersInHtml(value));
    }

    [Theory]
    [InlineData(null, 100)]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(250, 250)]
    [InlineData(6000, 5000)]
    public void ClampReportLimit_AppliesDefaultMinimumAndMaximum(int? value, int expected)
    {
        Assert.Equal(expected, ClashReportOptionHelper.ClampReportLimit(value));
    }

    [Theory]
    [InlineData(null, "point")]
    [InlineData("", "point")]
    [InlineData(" point ", "point")]
    [InlineData("ITEMS", "items")]
    [InlineData("bad", null)]
    public void NormalizeBoxMode_ReturnsCanonicalModes(string value, string expected)
    {
        Assert.Equal(expected, ClashReportOptionHelper.NormalizeBoxMode(value));
    }

    [Theory]
    [InlineData(null, "result")]
    [InlineData("", "result")]
    [InlineData(" raw ", "result")]
    [InlineData("CLASH", "result")]
    [InlineData("cluster", "cluster")]
    [InlineData("group", "cluster")]
    [InlineData("bad", null)]
    public void NormalizeArtifactGranularity_ReturnsCanonicalModes(string value, string expected)
    {
        Assert.Equal(expected, ClashReportOptionHelper.NormalizeArtifactGranularity(value));
    }

    [Theory]
    [InlineData(null, "full")]
    [InlineData("", "full")]
    [InlineData(" standard ", "full")]
    [InlineData("COMPACT", "compact")]
    [InlineData("short", "compact")]
    [InlineData("bad", null)]
    public void NormalizeReportVerbosity_ReturnsCanonicalModes(string value, string expected)
    {
        Assert.Equal(expected, ClashReportOptionHelper.NormalizeReportVerbosity(value));
    }

    [Theory]
    [InlineData(null, "compact", "jpg", ".jpg", 1280, 720, 72, true)]
    [InlineData("", "compact", "jpg", ".jpg", 1280, 720, 72, true)]
    [InlineData("small", "compact", "jpg", ".jpg", 1280, 720, 72, true)]
    [InlineData("full_hd", "fullhd", "jpg", ".jpg", 1920, 1080, 82, true)]
    [InlineData("fhd", "fullhd", "jpg", ".jpg", 1920, 1080, 82, true)]
    [InlineData("high", "large", "jpg", ".jpg", 2560, 1440, 88, true)]
    [InlineData("legacy", "source", "bmp", ".bmp", 0, 0, 100, false)]
    public void NormalizeScreenshotOptions_AppliesProfileDefaults(
        string profile,
        string expectedProfile,
        string expectedFormat,
        string expectedExtension,
        int expectedWidth,
        int expectedHeight,
        int expectedQuality,
        bool expectedPostProcess)
    {
        var options = Normalize(profile);

        Assert.Equal(expectedProfile, options.Profile);
        Assert.Equal(expectedFormat, options.Format);
        Assert.Equal(expectedExtension, options.FileExtension);
        Assert.Equal(expectedWidth, options.MaxWidth);
        Assert.Equal(expectedHeight, options.MaxHeight);
        Assert.Equal(expectedQuality, options.JpegQuality);
        Assert.Equal(expectedPostProcess, options.RequiresPostProcess);
    }

    [Theory]
    [InlineData("jpeg", "jpg", ".jpg")]
    [InlineData("png", "png", ".png")]
    [InlineData("bmp", "bmp", ".bmp")]
    public void NormalizeScreenshotOptions_AppliesFormatOverride(string format, string expectedFormat, string expectedExtension)
    {
        var options = Normalize(profile: "compact", format: format);

        Assert.Equal(expectedFormat, options.Format);
        Assert.Equal(expectedExtension, options.FileExtension);
    }

    [Fact]
    public void NormalizeScreenshotOptions_AppliesDimensionAndQualityOverrides()
    {
        var options = Normalize(profile: "large", maxWidth: 640, maxHeight: 0, jpegQuality: 55);

        Assert.Equal("large", options.Profile);
        Assert.Equal(640, options.MaxWidth);
        Assert.Equal(0, options.MaxHeight);
        Assert.Equal(55, options.JpegQuality);
    }

    [Theory]
    [InlineData("unknown", null, null, null, null, "Unsupported screenshotProfile 'unknown'. Use compact, fullhd, large, or source.")]
    [InlineData("compact", "gif", null, null, null, "Unsupported screenshotFormat 'gif'. Use jpg, png, or bmp.")]
    [InlineData("compact", null, -1, null, null, "screenshotMaxWidth must be 0 or greater.")]
    [InlineData("compact", null, 319, null, null, "screenshotMaxWidth must be between 320 and 10000 pixels, or 0 to disable that bound.")]
    [InlineData("compact", null, 10001, null, null, "screenshotMaxWidth must be between 320 and 10000 pixels, or 0 to disable that bound.")]
    [InlineData("compact", null, null, 319, null, "screenshotMaxHeight must be between 320 and 10000 pixels, or 0 to disable that bound.")]
    [InlineData("compact", null, null, null, 0, "screenshotJpegQuality must be between 1 and 100.")]
    [InlineData("compact", null, null, null, 101, "screenshotJpegQuality must be between 1 and 100.")]
    public void NormalizeScreenshotOptions_ReturnsErrorForInvalidValues(
        string profile,
        string format,
        int? maxWidth,
        int? maxHeight,
        int? jpegQuality,
        string expectedError)
    {
        string errorMessage;
        var options = ClashReportOptionHelper.NormalizeScreenshotOptions(profile, format, maxWidth, maxHeight, jpegQuality, out errorMessage);

        Assert.Null(options);
        Assert.Equal(expectedError, errorMessage);
    }

    [Theory]
    [InlineData(1920, 1080, 0, 0, 1920, 1080)]
    [InlineData(1920, 1080, 2560, 1440, 1920, 1080)]
    [InlineData(1920, 1080, 1280, 0, 1280, 720)]
    [InlineData(1920, 1080, 0, 720, 1280, 720)]
    [InlineData(4000, 1000, 1000, 1000, 1000, 250)]
    [InlineData(1000, 4000, 1000, 1000, 250, 1000)]
    [InlineData(3, 2, 1, 1, 1, 1)]
    [InlineData(0, 1080, 1280, 720, 0, 1080)]
    public void CalculateImageTargetSize_PreservesAspectRatioWithoutUpscaling(
        int sourceWidth,
        int sourceHeight,
        int maxWidth,
        int maxHeight,
        int expectedWidth,
        int expectedHeight)
    {
        ClashReportOptionHelper.CalculateImageTargetSize(sourceWidth, sourceHeight, maxWidth, maxHeight, out var targetWidth, out var targetHeight);

        Assert.Equal(expectedWidth, targetWidth);
        Assert.Equal(expectedHeight, targetHeight);
    }

    private static ClashReportScreenshotOptions Normalize(
        string profile,
        string format = null,
        int? maxWidth = null,
        int? maxHeight = null,
        int? jpegQuality = null)
    {
        string errorMessage;
        var options = ClashReportOptionHelper.NormalizeScreenshotOptions(profile, format, maxWidth, maxHeight, jpegQuality, out errorMessage);
        Assert.True(options != null, errorMessage);
        Assert.Equal(string.Empty, errorMessage);
        return options;
    }
}
