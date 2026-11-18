using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #526 (clash session, Navisworks 2027, 2026-11-18): a hard test reports faces lying in one plane as a clash
// with distanceMm -26.46 although the two items' boxes do not overlap on Z (about 3e-9 m). The report now
// carries the per-axis overlap of the two items' bounding boxes and the smallest of the three, so touches can
// be told from penetrations. Positive = overlap on that axis, negative = gap. The plugin cannot compile in L0,
// so the arithmetic lives in ClashBoundsOverlapHelper (Contracts) and the plugin's use of it is pinned by source.
public sealed class ClashItemBoundsOverlapTests
{
    [Fact]
    public void FacesInOnePlaneGiveZeroOnThatAxis()
    {
        // PANEL 1 (z 42 403..42 413 mm) under ATTACHMENT 4 (z 42 413..42 614 mm), overlapping in plan.
        var overlap = ClashBoundsOverlapHelper.Compute(
            Box(0, 0, 42403, 1000, 1000, 42413),
            Box(400, 400, 42413, 600, 600, 42614));
        Assert.NotNull(overlap);
        Assert.Equal(200, overlap.X, 6);
        Assert.Equal(200, overlap.Y, 6);
        Assert.Equal(0, overlap.Z, 6);
        Assert.Equal(0, overlap.Min, 6);
    }

    [Fact]
    public void APenetrationAndAGapAreSigned()
    {
        var penetration = ClashBoundsOverlapHelper.Compute(Box(0, 0, 0, 100, 100, 10), Box(20, 20, 5, 80, 80, 50));
        Assert.Equal(5, penetration.Z, 6);
        Assert.Equal(5, penetration.Min, 6);

        var gap = ClashBoundsOverlapHelper.Compute(Box(0, 0, 0, 100, 100, 10), Box(20, 20, 12, 80, 80, 50));
        Assert.Equal(-2, gap.Z, 6);
        Assert.Equal(-2, gap.Min, 6);
    }

    [Fact]
    public void TheSmallestAxisWinsAndTheOrderOfTheItemsDoesNotMatter()
    {
        var a = Box(0, 0, 0, 100, 100, 100);
        var b = Box(97, 10, 30, 300, 90, 60);
        var ab = ClashBoundsOverlapHelper.Compute(a, b);
        var ba = ClashBoundsOverlapHelper.Compute(b, a);
        Assert.Equal(3, ab.X, 6);
        Assert.Equal(80, ab.Y, 6);
        Assert.Equal(30, ab.Z, 6);
        Assert.Equal(3, ab.Min, 6);
        Assert.Equal(ab.Min, ba.Min, 6);
        Assert.Equal(ab.X, ba.X, 6);
    }

    [Fact]
    public void AMissingBoxGivesNoOverlap()
    {
        var box = Box(0, 0, 0, 1, 1, 1);
        Assert.Null(ClashBoundsOverlapHelper.Compute(null, box));
        Assert.Null(ClashBoundsOverlapHelper.Compute(box, null));
        Assert.Null(ClashBoundsOverlapHelper.Compute(new BoundingBoxInfo { Min = new Point3Info() }, box));
    }

    [Fact]
    public void TheReportItemCarriesTheOverlapAndAReason()
    {
        var item = new ClashReportItem
        {
            ItemBoundsOverlapMm = new ClashBoundsOverlapInfo { X = 1, Y = 2, Z = 0, Min = 0 },
            ItemBoundsOverlapReason = "",
        };
        Assert.Equal(0, item.ItemBoundsOverlapMm.Min);
    }

    [Fact]
    public void TheReportFillsAndWritesTheOverlap()
    {
        var output = Read("NavisHelper", "Agent", "Services", "DocumentCommandService.Clash.ReportOutputSupport.cs");
        Assert.Contains("ClashBoundsOverlapHelper.Compute(", output, StringComparison.Ordinal);
        Assert.Contains("ItemBoundsOverlapMm", Read("NavisHelper", "Agent", "Services", "ClashReportResponseFactory.cs"), StringComparison.Ordinal);
        var writer = Read("NavisHelper", "Agent", "Services", "ClashReportArtifactWriter.cs");
        Assert.Contains("item.ItemBoundsOverlapMm", writer, StringComparison.Ordinal);
        Assert.Contains("item.ItemBoundsOverlapReason", writer, StringComparison.Ordinal);
        Assert.Contains("itemBoundsOverlapMm", Read("docs", "MCP_TOOL_CONTRACTS.md"), StringComparison.Ordinal);
    }

    private static BoundingBoxInfo Box(double minX, double minY, double minZ, double maxX, double maxY, double maxZ) =>
        new BoundingBoxInfo
        {
            Min = new Point3Info { X = minX, Y = minY, Z = minZ },
            Max = new Point3Info { X = maxX, Y = maxY, Z = maxZ },
        };

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
