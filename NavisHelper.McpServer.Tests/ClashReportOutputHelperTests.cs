using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashReportOutputHelperTests
{
    [Fact]
    public void BuildArtifactPaths_UsesStandardReportFileNames()
    {
        var paths = ClashReportOutputHelper.BuildArtifactPaths(@"C:\Reports\Clash");

        Assert.Equal(@"C:\Reports\Clash", paths.OutputDirectory);
        Assert.Equal(@"C:\Reports\Clash\report.html", paths.ReportPath);
        Assert.Equal(@"C:\Reports\Clash\manifest.json", paths.ManifestPath);
        Assert.Equal(@"C:\Reports\Clash\clash_boxes.json", paths.ClashBoxesPath);
        Assert.Equal(@"C:\Reports\Clash\images", paths.ImagesDirectory);
    }

    [Theory]
    [InlineData(1, ".jpg", false, "clash_000001.jpg")]
    [InlineData(42, ".png", true, "clash_000042_top.png")]
    [InlineData(1234567, ".bmp", false, "clash_1234567.bmp")]
    [InlineData(5, null, true, "clash_000005_top")]
    public void BuildScreenshotFileName_UsesStablePaddedNames(int index, string extension, bool topView, string expected)
    {
        Assert.Equal(expected, ClashReportOutputHelper.BuildScreenshotFileName(index, extension, topView));
    }

    [Theory]
    [InlineData(1, ".jpg", false, "cluster_000001.jpg")]
    [InlineData(42, ".png", true, "cluster_000042_top.png")]
    public void BuildClusterScreenshotFileName_UsesDistinctStableNames(int index, string extension, bool topView, string expected)
    {
        Assert.Equal(expected, ClashReportOutputHelper.BuildClusterScreenshotFileName(index, extension, topView));
    }

    [Fact]
    public void BuildScreenshotPaths_ReturnRelativeAndAbsoluteImagePaths()
    {
        var fileName = ClashReportOutputHelper.BuildScreenshotFileName(7, ".jpg", false);

        Assert.Equal("images/clash_000007.jpg", ClashReportOutputHelper.BuildScreenshotRelativePath(fileName));
        Assert.Equal(@"C:\Reports\Clash\images\clash_000007.jpg", ClashReportOutputHelper.BuildScreenshotAbsolutePath(@"C:\Reports\Clash\images", fileName));
    }

    [Fact]
    public void RequiresMarkerForOverwrite_RequiresMarkerForImagesDirectory()
    {
        Assert.True(ClashReportOutputHelper.RequiresMarkerForOverwrite(
            imagesDirectoryExists: true,
            existingFileNames: new[] { "notes.txt" }));
    }

    [Theory]
    [InlineData("report.html")]
    [InlineData("manifest.json")]
    [InlineData("clash_boxes.json")]
    [InlineData("REPORT.HTML")]
    public void RequiresMarkerForOverwrite_RequiresMarkerForProtectedReportFiles(string fileName)
    {
        Assert.True(ClashReportOutputHelper.RequiresMarkerForOverwrite(
            imagesDirectoryExists: false,
            existingFileNames: new[] { fileName }));
    }

    [Fact]
    public void RequiresMarkerForOverwrite_AllowsDirectoryWithoutProtectedReportEntries()
    {
        Assert.False(ClashReportOutputHelper.RequiresMarkerForOverwrite(
            imagesDirectoryExists: false,
            existingFileNames: new[] { "notes.txt", "readme.md" }));
    }

    [Fact]
    public void GetProtectedExistingFileNames_DeduplicatesCaseInsensitively()
    {
        var protectedFiles = ClashReportOutputHelper.GetProtectedExistingFileNames(
            new[] { "report.html", "REPORT.HTML", "manifest.json", "notes.txt" });

        Assert.Equal(new[] { "report.html", "manifest.json" }, protectedFiles);
    }
}
