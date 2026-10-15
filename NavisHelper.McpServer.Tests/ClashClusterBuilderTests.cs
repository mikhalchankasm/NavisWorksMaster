using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned characterization test for moving clash clustering into Contracts
// (docs/CLASH_ZONES_PLAN.md, section 6, PR 1). It pins what ClashClusterConstructionService did on main 115fb2b.
public sealed class ClashClusterBuilderTests
{
    [Fact]
    public void Build_ObjectPair_GroupsByKeyIgnoringCaseInFirstAppearanceOrder()
    {
        var rows = new[] { Row("r1", "B|C"), Row("r2", "a|b"), Row("r3", "b|c"), Row("r4", "A|B"), Row("r5", "x|y") };

        var clusters = ClashClusterBuilder.Build(rows, "object_pair", 100, r => r.Key, r => r.Point);

        Assert.Equal(new[] { "r1 r3", "r2 r4", "r5" }, Names(clusters));
    }

    [Fact]
    public void Build_Spatial_LinksPointsWithinTheDistanceInclusive()
    {
        var rows = new[]
        {
            Row("a", "k", 0, 0, 0),
            Row("b", "k", 100, 0, 0),
            Row("c", "k", 200, 0, 0),
            Row("d", "k", 300.0001, 0, 0),
        };

        var clusters = ClashClusterBuilder.Build(rows, "spatial", 100, r => r.Key, r => r.Point);

        Assert.Equal(new[] { "a b c", "d" }, Names(clusters));
    }

    [Fact]
    public void Build_Spatial_FindsNeighboursAcrossCellsAndNegativeCoordinates()
    {
        var rows = new[]
        {
            Row("left", "k", 99, 0, 0),
            Row("right", "k", 101, 0, 0),
            Row("diag", "k", 300, 300, 0),
            Row("diag2", "k", 370, 370, 0),
            Row("neg", "k", -50, -1000, 0),
            Row("pos", "k", 30, -1000, 0),
            Row("far", "k", 0, 0, 5000),
        };

        var clusters = ClashClusterBuilder.Build(rows, "spatial", 100, r => r.Key, r => r.Point);

        Assert.Equal(new[] { "left right", "diag diag2", "neg pos", "far" }, Names(clusters));
    }

    [Fact]
    public void Build_Spatial_ClustersAppearInFirstMemberOrderWithMembersInInputOrder()
    {
        var rows = new[] { Row("A", "k", 0, 0, 0), Row("B", "k", 1000, 0, 0), Row("C", "k", 50, 0, 0), Row("D", "k", 1050, 0, 0) };

        var clusters = ClashClusterBuilder.Build(rows, "SPATIAL", 100, r => r.Key, r => r.Point);

        Assert.Equal(new[] { "A C", "B D" }, Names(clusters));
    }

    [Fact]
    public void Build_Spatial_RowWithoutAPointIsItsOwnCluster()
    {
        var rows = new[] { Row("a", "k", 0, 0, 0), Row("none", "k"), Row("b", "k", 10, 0, 0) };

        var clusters = ClashClusterBuilder.Build(rows, "spatial", 100, r => r.Key, r => r.Point);

        Assert.Equal(new[] { "a b", "none" }, Names(clusters));
    }

    [Theory]
    [InlineData("hybrid")]
    [InlineData("none")]
    [InlineData(null)]
    [InlineData("anything-else")]
    public void Build_AnyOtherModeIsHybrid_KeyGroupsThenSpatialSplit(string mode)
    {
        var rows = new[]
        {
            Row("p1", "pipe|beam", 0, 0, 0),
            Row("d1", "duct|wall", 10, 0, 0),
            Row("p2", "PIPE|BEAM", 5000, 0, 0),
            Row("p3", "pipe|beam", 60, 0, 0),
            Row("d2", "duct|wall", 5010, 0, 0),
        };

        var clusters = ClashClusterBuilder.Build(rows, mode, 100, r => r.Key, r => r.Point);

        Assert.Equal(new[] { "p1 p3", "p2", "d1", "d2" }, Names(clusters));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void Build_NonPositiveDistanceOnlyLinksCoincidentPoints(double distance)
    {
        var rows = new[] { Row("a", "k", 1, 2, 3), Row("b", "k", 1, 2, 3), Row("c", "k", 1, 2, 3.01) };

        var clusters = ClashClusterBuilder.Build(rows, "spatial", distance, r => r.Key, r => r.Point);

        Assert.Equal(new[] { "a b", "c" }, Names(clusters));
    }

    [Fact]
    public void Build_NullOrEmptyRowsGiveNoClusters()
    {
        Assert.Empty(ClashClusterBuilder.Build<TestRow>(null, "spatial", 100, r => r.Key, r => r.Point));
        Assert.Empty(ClashClusterBuilder.Build(new List<TestRow>(), "hybrid", 100, r => r.Key, r => r.Point));
    }

    [Fact]
    public void Build_Spatial_MatchesNaiveSingleLinkageOnRandomPoints()
    {
        var random = new Random(115);
        var rows = Enumerable.Range(0, 3000)
            .Select(i => Row("r" + i, "k", random.NextDouble() * 20000 - 10000, random.NextDouble() * 20000, random.NextDouble() * 2000))
            .ToList();

        var clusters = ClashClusterBuilder.Build(rows, "spatial", 300, r => r.Key, r => r.Point);

        Assert.Equal(NaiveSingleLinkage(rows, 300), Names(clusters));
        Assert.True(clusters.Count > 100 && clusters.Count < 2900, "the sample should mix singletons and chains: " + clusters.Count);
    }

    [Fact]
    public void PluginService_DelegatesToTheContractsBuilder()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "NavisHelper", "Agent", "Services", "ClashClusterConstructionService.cs"));

        Assert.Contains("ClashClusterBuilder.Build", source);
        Assert.DoesNotContain("UnionFind", source);
        Assert.DoesNotContain("BuildSpatialCellKey", source);
        Assert.DoesNotContain("Math.Floor", source);
    }

    private static List<string> NaiveSingleLinkage(IList<TestRow> rows, double distance)
    {
        var parent = Enumerable.Range(0, rows.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        for (var i = 0; i < rows.Count; i++)
            for (var j = i + 1; j < rows.Count; j++)
            {
                var a = rows[i].Point;
                var b = rows[j].Point;
                var dx = a.X - b.X;
                var dy = a.Y - b.Y;
                var dz = a.Z - b.Z;
                if (Math.Sqrt(dx * dx + dy * dy + dz * dz) <= distance)
                    parent[Find(i)] = Find(j);
            }

        return rows
            .Select((row, index) => new { row.Name, Root = Find(index) })
            .GroupBy(x => x.Root)
            .Select(g => string.Join(" ", g.Select(x => x.Name)))
            .ToList();
    }

    private static List<string> Names(IEnumerable<List<TestRow>> clusters)
    {
        return clusters.Select(c => string.Join(" ", c.Select(r => r.Name))).ToList();
    }

    private static TestRow Row(string name, string key)
    {
        return new TestRow { Name = name, Key = key };
    }

    private static TestRow Row(string name, string key, double x, double y, double z)
    {
        return new TestRow { Name = name, Key = key, Point = new ClashClusterPoint(x, y, z) };
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }

    private sealed class TestRow
    {
        public string Name;
        public string Key;
        public ClashClusterPoint Point;
    }
}
