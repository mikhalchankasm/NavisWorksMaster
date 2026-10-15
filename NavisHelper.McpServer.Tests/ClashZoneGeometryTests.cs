using System;
using System.Collections.Generic;
using System.Linq;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance test for clash-zone geometry (docs/CLASH_ZONES_PLAN.md, section 4, PR 2).
public sealed class ClashZoneGeometryTests
{
    private const int Precision = 6;

    [Fact]
    public void MinimumEnclosingSphere_TwoPoints_IsTheirDiameterSphere()
    {
        var sphere = ClashZoneGeometry.MinimumEnclosingSphere(Points((0, 0, 0), (1000, 0, 0)));

        AssertSphere(sphere, 500, 0, 0, 500);
    }

    [Fact]
    public void MinimumEnclosingSphere_ObtuseTriangle_IsSpannedByTheLongestSide()
    {
        var sphere = ClashZoneGeometry.MinimumEnclosingSphere(Points((0, 0, 0), (1000, 0, 0), (500, 100, 0)));

        AssertSphere(sphere, 500, 0, 0, 500);
    }

    [Fact]
    public void MinimumEnclosingSphere_EquilateralTriangle_IsItsCircumcircle()
    {
        var h = 1000 * Math.Sqrt(3) / 2;
        var sphere = ClashZoneGeometry.MinimumEnclosingSphere(Points((1000, 0, 0), (-500, h, 0), (-500, -h, 0)));

        AssertSphere(sphere, 0, 0, 0, 1000);
    }

    [Fact]
    public void MinimumEnclosingSphere_RegularTetrahedron_IsItsCircumsphere()
    {
        var sphere = ClashZoneGeometry.MinimumEnclosingSphere(Points(
            (1000, 1000, 1000), (1000, -1000, -1000), (-1000, 1000, -1000), (-1000, -1000, 1000)));

        AssertSphere(sphere, 0, 0, 0, 1000 * Math.Sqrt(3));
    }

    [Fact]
    public void MinimumEnclosingSphere_CubeWithInteriorPoints_IgnoresTheInterior()
    {
        var points = new List<(double, double, double)>();
        foreach (var x in new[] { -1000.0, 1000.0 })
            foreach (var y in new[] { -1000.0, 1000.0 })
                foreach (var z in new[] { -1000.0, 1000.0 })
                    points.Add((x + 7000, y - 3000, z + 250));
        points.Add((7000, -3000, 250));
        points.Add((7500, -3100, 0));

        var sphere = ClashZoneGeometry.MinimumEnclosingSphere(Points(points.ToArray()));

        AssertSphere(sphere, 7000, -3000, 250, 1000 * Math.Sqrt(3));
    }

    [Fact]
    public void MinimumEnclosingSphere_RandomSets_MatchTheBruteForceOptimum()
    {
        var random = new Random(20261015);
        for (var trial = 0; trial < 40; trial++)
        {
            var count = 2 + random.Next(9);
            var points = Enumerable.Range(0, count)
                .Select(i => Point("p" + i, random.NextDouble() * 4000 - 2000, random.NextDouble() * 4000 - 2000, random.NextDouble() * 1000))
                .ToList();

            var sphere = ClashZoneGeometry.MinimumEnclosingSphere(points);
            var expected = BruteForceMinimumSphere(points);

            Assert.Equal(expected.Radius, sphere.Radius, 3);
            Assert.Equal(expected.X, sphere.X, 3);
            Assert.Equal(expected.Y, sphere.Y, 3);
            Assert.Equal(expected.Z, sphere.Z, 3);
        }
    }

    [Fact]
    public void MinimumEnclosingSphere_DoesNotDependOnInputOrder()
    {
        var random = new Random(7);
        var points = Enumerable.Range(0, 300)
            .Select(i => Point("c" + i.ToString("D3"), random.NextDouble() * 9000, random.NextDouble() * 9000, random.NextDouble() * 3000))
            .ToList();
        var shuffled = points.OrderBy(_ => random.Next()).ToList();

        var a = ClashZoneGeometry.MinimumEnclosingSphere(points);
        var b = ClashZoneGeometry.MinimumEnclosingSphere(shuffled);

        Assert.Equal(a.X, b.X);
        Assert.Equal(a.Y, b.Y);
        Assert.Equal(a.Z, b.Z);
        Assert.Equal(a.Radius, b.Radius);
        foreach (var p in points)
            Assert.True(Distance(p, a.X, a.Y, a.Z) <= a.Radius + 1e-6, "point " + p.Id + " lies outside the sphere");
    }

    [Fact]
    public void MinimumEnclosingSphere_RejectsCoordinatesThatWouldOverflow()
    {
        Assert.Throws<ArgumentException>(() => ClashZoneGeometry.MinimumEnclosingSphere(Points((0, 0, 0), (1e200, 0, 0))));
        Assert.Throws<ArgumentException>(() => ClashZoneGeometry.MinimumEnclosingSphere(Points((0, double.NaN, 0))));
    }

    [Fact]
    public void MinimumEnclosingSphere_LongOrderedChainIsExact()
    {
        // Sequential ids along a line: the input order that makes an unshuffled Welzl quadratic.
        var points = Enumerable.Range(0, 50000).Select(i => Point("c" + i.ToString("D6"), i * 10.0, 0, 0)).ToList();

        var sphere = ClashZoneGeometry.MinimumEnclosingSphere(points);

        AssertSphere(sphere, 249995, 0, 0, 249995);
    }

    [Fact]
    public void BuildZones_SingleClash_GetsTheMinimumRadius()
    {
        var zones = ClashZoneGeometry.BuildZones(new[] { Cluster("A|B", (1200, 3400, 560)) }, null);

        var zone = Assert.Single(zones);
        Assert.Equal("Z-0001", zone.ZoneId);
        Assert.Equal("A|B", zone.PartitionKey);
        Assert.Equal(0, zone.EnclosingRadiusMm, Precision);
        Assert.Equal(150, zone.RadiusMm, Precision);
        Assert.Equal(1200, zone.CenterX, Precision);
        Assert.Equal(3400, zone.CenterY, Precision);
        Assert.Equal(560, zone.CenterZ, Precision);
    }

    [Theory]
    [InlineData(1000.0, 500.0, 600.0)]
    [InlineData(200.0, 100.0, 200.0)]
    [InlineData(80.0, 40.0, 150.0)]
    public void BuildZones_RadiusIsEnclosingPlusPadButNotBelowTheMinimum(double span, double enclosing, double drawn)
    {
        var zones = ClashZoneGeometry.BuildZones(new[] { Cluster("A|B", (0, 0, 0), (span, 0, 0)) }, new ClashZoneGeometryOptions());

        var zone = Assert.Single(zones);
        Assert.Equal(enclosing, zone.EnclosingRadiusMm, Precision);
        Assert.Equal(drawn, zone.RadiusMm, Precision);
        Assert.Equal(span / 2, zone.CenterX, Precision);
    }

    [Fact]
    public void BuildZones_DefaultOptionsAreTheOwnerAcceptedNumbers()
    {
        var options = new ClashZoneGeometryOptions();

        Assert.Equal(3000, options.MaxZoneRadiusMm);
        Assert.Equal(150, options.MinRadiusMm);
        Assert.Equal(100, options.PadMm);
    }

    [Fact]
    public void BuildZones_ChainedClusterIsSplitAtTheMedianOfItsLongestAxis()
    {
        // 161 points 250 mm apart along X: one 40 m chain. Halving (first half = floor(n/2) points
        // in axis order) until every enclosing radius is <= 3000 gives 80/81 -> 40/40/40/41 -> eight zones.
        var points = Enumerable.Range(0, 161).Select(i => (i * 250.0, 10.0, 20.0)).ToArray();

        var zones = ClashZoneGeometry.BuildZones(new[] { Cluster("PIPE|BEAM", points) }, new ClashZoneGeometryOptions());

        Assert.Equal(8, zones.Count);
        Assert.All(zones, zone => Assert.True(zone.EnclosingRadiusMm <= 3000, zone.ZoneId + " encloses " + zone.EnclosingRadiusMm));
        var memberIds = zones.SelectMany(zone => zone.Points.Select(p => p.Id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
        var inputIds = Enumerable.Range(0, 161).Select(i => "PIPE|BEAM#" + i.ToString("D4")).OrderBy(id => id, StringComparer.Ordinal).ToList();
        Assert.Equal(inputIds, memberIds);
        Assert.Equal(new[] { 20, 20, 20, 20, 20, 20, 20, 21 }, zones.Select(zone => zone.Points.Count).OrderBy(n => n).ToArray());
        Assert.Equal(Enumerable.Range(1, 8).Select(i => "Z-" + i.ToString("D4")), zones.Select(zone => zone.ZoneId));
    }

    [Fact]
    public void BuildZones_NumbersByPartitionKeyThenCentroid()
    {
        var clusters = new[]
        {
            Cluster("b|x", (0, 0, 0)),
            Cluster("a|x", (5000, 0, 0)),
            Cluster("a|x", (100, 0, 0), (300, 0, 0)),
        };

        var zones = ClashZoneGeometry.BuildZones(clusters, null);

        Assert.Equal(new[] { "Z-0001", "Z-0002", "Z-0003" }, zones.Select(zone => zone.ZoneId));
        Assert.Equal(new[] { "a|x", "a|x", "b|x" }, zones.Select(zone => zone.PartitionKey));
        Assert.Equal(new[] { 200.0, 5000.0, 0.0 }, zones.Select(zone => zone.CentroidX));
    }

    [Fact]
    public void BuildZones_DoesNotDependOnClusterOrPointOrder()
    {
        var random = new Random(11);
        var clusters = Enumerable.Range(0, 6)
            .Select(c => new ClashZoneInputCluster
            {
                PartitionKey = "k" + (c % 3),
                Points = Enumerable.Range(0, 40)
                    .Select(i => Point("k" + c + "#" + i, c * 20000 + random.NextDouble() * 12000, random.NextDouble() * 500, 0))
                    .ToList(),
            })
            .ToList();
        var reordered = clusters
            .AsEnumerable()
            .Reverse()
            .Select(c => new ClashZoneInputCluster { PartitionKey = c.PartitionKey, Points = c.Points.Reverse().ToList() })
            .ToList();

        var a = ClashZoneGeometry.BuildZones(clusters, null);
        var b = ClashZoneGeometry.BuildZones(reordered, null);

        Assert.Equal(Describe(a), Describe(b));
    }

    [Fact]
    public void BuildZones_SkipsEmptyClustersAndRejectsNullInput()
    {
        var zones = ClashZoneGeometry.BuildZones(new[] { new ClashZoneInputCluster { PartitionKey = "A|B", Points = new List<ClashZonePoint>() } }, null);

        Assert.Empty(zones);
        Assert.Throws<ArgumentNullException>(() => ClashZoneGeometry.BuildZones(null, null));
        Assert.Throws<ArgumentException>(() => ClashZoneGeometry.MinimumEnclosingSphere(new List<ClashZonePoint>()));
    }

    [Theory]
    [InlineData(0.0, 150.0, 100.0)]
    [InlineData(3000.0, -1.0, 100.0)]
    [InlineData(3000.0, 150.0, -1.0)]
    public void BuildZones_RejectsInvalidOptions(double maxRadius, double minRadius, double pad)
    {
        var options = new ClashZoneGeometryOptions { MaxZoneRadiusMm = maxRadius, MinRadiusMm = minRadius, PadMm = pad };

        Assert.Throws<ArgumentOutOfRangeException>(() => ClashZoneGeometry.BuildZones(new[] { Cluster("A|B", (0, 0, 0)) }, options));
    }

    private static string Describe(IList<ClashZone> zones)
    {
        return string.Join(";", zones.Select(zone => string.Join(",",
            zone.ZoneId,
            zone.PartitionKey,
            zone.Points.Count,
            zone.CenterX.ToString("R"),
            zone.CenterY.ToString("R"),
            zone.CenterZ.ToString("R"),
            zone.RadiusMm.ToString("R"))));
    }

    private static ClashZoneInputCluster Cluster(string key, params (double X, double Y, double Z)[] points)
    {
        return new ClashZoneInputCluster
        {
            PartitionKey = key,
            Points = points.Select((p, i) => Point(key + "#" + i.ToString("D4"), p.X, p.Y, p.Z)).ToList(),
        };
    }

    private static List<ClashZonePoint> Points(params (double X, double Y, double Z)[] points)
    {
        return points.Select((p, i) => Point("p" + i, p.X, p.Y, p.Z)).ToList();
    }

    private static ClashZonePoint Point(string id, double x, double y, double z)
    {
        return new ClashZonePoint { Id = id, X = x, Y = y, Z = z };
    }

    private static void AssertSphere(ClashZoneSphere sphere, double x, double y, double z, double radius)
    {
        Assert.Equal(x, sphere.X, Precision);
        Assert.Equal(y, sphere.Y, Precision);
        Assert.Equal(z, sphere.Z, Precision);
        Assert.Equal(radius, sphere.Radius, Precision);
    }

    private static double Distance(ClashZonePoint p, double x, double y, double z)
    {
        var dx = p.X - x;
        var dy = p.Y - y;
        var dz = p.Z - z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    // Reference: the smallest sphere through 2, 3 or 4 of the points that contains all of them.
    private static ClashZoneSphere BruteForceMinimumSphere(IList<ClashZonePoint> points)
    {
        ClashZoneSphere best = null;
        var n = points.Count;
        void Consider(ClashZoneSphere candidate)
        {
            if (candidate == null || (best != null && candidate.Radius >= best.Radius))
                return;
            if (points.All(p => Distance(p, candidate.X, candidate.Y, candidate.Z) <= candidate.Radius + 1e-7))
                best = candidate;
        }

        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                Consider(Diameter(points[i], points[j]));
                for (var k = j + 1; k < n; k++)
                {
                    Consider(Circumcircle(points[i], points[j], points[k]));
                    for (var l = k + 1; l < n; l++)
                        Consider(Circumsphere(points[i], points[j], points[k], points[l]));
                }
            }

        return best;
    }

    private static ClashZoneSphere Diameter(ClashZonePoint a, ClashZonePoint b)
    {
        var x = (a.X + b.X) / 2;
        var y = (a.Y + b.Y) / 2;
        var z = (a.Z + b.Z) / 2;
        return new ClashZoneSphere { X = x, Y = y, Z = z, Radius = Distance(a, x, y, z) };
    }

    private static ClashZoneSphere Circumcircle(ClashZonePoint a, ClashZonePoint b, ClashZonePoint c)
    {
        double[] ab = { b.X - a.X, b.Y - a.Y, b.Z - a.Z };
        double[] ac = { c.X - a.X, c.Y - a.Y, c.Z - a.Z };
        var normal = Cross(ab, ac);
        var normal2 = Dot(normal, normal);
        if (normal2 < 1e-9)
            return null;

        var t1 = Cross(normal, ab);
        var t2 = Cross(ac, normal);
        var ac2 = Dot(ac, ac);
        var ab2 = Dot(ab, ab);
        var x = a.X + (ac2 * t1[0] + ab2 * t2[0]) / (2 * normal2);
        var y = a.Y + (ac2 * t1[1] + ab2 * t2[1]) / (2 * normal2);
        var z = a.Z + (ac2 * t1[2] + ab2 * t2[2]) / (2 * normal2);
        return new ClashZoneSphere { X = x, Y = y, Z = z, Radius = Distance(a, x, y, z) };
    }

    private static ClashZoneSphere Circumsphere(ClashZonePoint a, ClashZonePoint b, ClashZonePoint c, ClashZonePoint d)
    {
        double[][] m =
        {
            new[] { b.X - a.X, b.Y - a.Y, b.Z - a.Z },
            new[] { c.X - a.X, c.Y - a.Y, c.Z - a.Z },
            new[] { d.X - a.X, d.Y - a.Y, d.Z - a.Z },
        };
        double[] rhs = { Dot(m[0], m[0]) / 2, Dot(m[1], m[1]) / 2, Dot(m[2], m[2]) / 2 };
        var det = Determinant(m[0], m[1], m[2]);
        if (Math.Abs(det) < 1e-6)
            return null;

        var columns = new double[3];
        for (var col = 0; col < 3; col++)
        {
            var replaced = m.Select((row, r) => row.Select((v, cc) => cc == col ? rhs[r] : v).ToArray()).ToArray();
            columns[col] = Determinant(replaced[0], replaced[1], replaced[2]) / det;
        }

        var x = a.X + columns[0];
        var y = a.Y + columns[1];
        var z = a.Z + columns[2];
        return new ClashZoneSphere { X = x, Y = y, Z = z, Radius = Distance(a, x, y, z) };
    }

    private static double[] Cross(double[] u, double[] v)
    {
        return new[] { u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0] };
    }

    private static double Dot(double[] u, double[] v)
    {
        return u[0] * v[0] + u[1] * v[1] + u[2] * v[2];
    }

    private static double Determinant(double[] r0, double[] r1, double[] r2)
    {
        return Dot(r0, Cross(r1, r2));
    }
}
