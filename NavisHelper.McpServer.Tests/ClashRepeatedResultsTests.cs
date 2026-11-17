using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #509 (Port clash run, 2026-11-17): RVM read from E3D holds most components as two sibling nodes
// with the same parent, name and box, so Clash Detective reports every clash with them twice (52 rows,
// 32 unique). clash_list_results gains dedupe (same items, point and distance within one test, first
// row kept) and find_duplicate_geometry gains excludeSameParentAndName. The plugin cannot compile in
// L0, so its wiring is pinned by source; the rules are tested directly.
public sealed class ClashRepeatedResultsTests
{
    private static ClashResultSummary Row(string test, string name, string a, string b, double x, double? distance = -0.166) => new()
    {
        TestName = test,
        Name = name,
        Item1Names = new List<string> { a },
        Item2Names = new List<string> { b },
        ClashPoint = new Point3Info { X = x, Y = 58.279, Z = 39.581 },
        DistanceMm = distance,
    };

    [Fact]
    public void RepeatedRowsShareAKeyAndTheFirstOneIsKept()
    {
        var rows = new List<ClashResultSummary>
        {
            Row("T1", "Clash2", "A/BOX 2", "B/VALVE 1", 674.680),
            Row("T1", "Clash3", "A/BOX 2", "B/VALVE 1", 674.6800000004),
            Row("T1", "Clash4", "A/BOX 2", "B/VALVE 1", 674.690),
            Row("T2", "Clash2", "A/BOX 2", "B/VALVE 1", 674.680),
            Row("T1", "Clash5", "A/BOX 2", "B/VALVE 1", 674.680, -0.2),
            Row("T1", "Clash6", "B/VALVE 1", "A/BOX 2", 674.680),
        };

        Assert.Equal(ClashResultDuplicateKey.For(rows[0]), ClashResultDuplicateKey.For(rows[1]));
        Assert.NotEqual(ClashResultDuplicateKey.For(rows[0]), ClashResultDuplicateKey.For(rows[2]));

        var deduped = ClashResultDeduper.Dedupe(rows);
        Assert.Equal(1, deduped.FoldedCount);
        Assert.Equal(new[] { "Clash2", "Clash4", "Clash2", "Clash5", "Clash6" }, deduped.Kept.Select(row => row.Name));
        Assert.Equal("T2", deduped.Kept[2].TestName);
    }

    [Fact]
    public void SameParentAndNameGroupsCanBeLeftOutBeforeTheGroupLimit()
    {
        var items = new[]
        {
            Item(0, "p1", "VALVE 1", 5), Item(1, "p1", "VALVE 1", 5), Item(2, "p1", "VALVE 1", 5),
            Item(3, "b1", "TUBE 1", 9), Item(4, "b2", "TUBE 1", 9),
        };

        var all = DuplicateGeometryRules.Group(items, 0.01, DuplicateGeometryModes.All, 1, false, out var none);
        Assert.Equal(0, none);
        Assert.True(Assert.Single(all).SameParentAndName);

        var kept = DuplicateGeometryRules.Group(items, 0.01, DuplicateGeometryModes.All, 1, true, out var excluded);
        Assert.Equal(1, excluded);
        Assert.Equal(new[] { 3, 4 }, Assert.Single(kept).ItemIndexes);
    }

    [Fact]
    public void RequestsResponsesAndWiringCarryTheNewOptions()
    {
        Assert.NotNull(typeof(ClashListResultsRequest).GetProperty("Dedupe"));
        Assert.NotNull(typeof(ClashListResultsResponse).GetProperty("DedupedResultCount"));
        Assert.NotNull(typeof(FindDuplicateGeometryRequest).GetProperty("ExcludeSameParentAndName"));
        Assert.NotNull(typeof(FindDuplicateGeometryResponse).GetProperty("ExcludedGroupCount"));

        var listing = Read("NavisHelper", "Agent", "Services", "DocumentCommandService.Clash.Listing.cs");
        int dedupe = listing.IndexOf("ClashResultDeduper.Dedupe(", StringComparison.Ordinal);
        int sort = listing.IndexOf("SortClashResultSummaries(", StringComparison.Ordinal);
        Assert.True(dedupe > 0 && sort > dedupe, "rows are folded before they are sorted and paged");
        Assert.Contains("\"dedupe requires includeItemNames=true.\"", listing, StringComparison.Ordinal);

        var duplicates = Read("NavisHelper", "Agent", "Services", "DuplicateGeometryService.cs");
        Assert.Contains("request.ExcludeSameParentAndName == true", duplicates, StringComparison.Ordinal);

        Assert.Contains("Dedupe = dedupe,", Read("NavisHelper.McpServer", "Tools", "NavisworksClashListingTools.cs"), StringComparison.Ordinal);
        Assert.Contains("ExcludeSameParentAndName = excludeSameParentAndName,", Read("NavisHelper.McpServer", "Tools", "NavisworksDuplicateGeometryTools.cs"), StringComparison.Ordinal);
    }

    private static DuplicateGeometryCandidate Item(int index, string parent, string name, double x) => new()
    {
        Index = index, RootKey = "R", ParentKey = parent, DisplayName = name,
        MinX = x, MinY = 0, MinZ = 0, MaxX = x + 1, MaxY = 0.2, MaxZ = 0.2, PrimitiveCount = 12,
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
