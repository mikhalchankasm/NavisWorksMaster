using System;
using System.Collections.Generic;
using System.Linq;
using NavisHelper.Agent.Contracts;
using Newtonsoft.Json;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance test for assembling one clash-zone run (docs/CLASH_ZONES_PLAN.md, sections 4-6, PR 6a).
public sealed class ClashZoneRunPlannerTests
{
    [Fact]
    public void Plan_FirstRun_BuildsZonesPerObjectPairAndSpatialChain()
    {
        var clashes = new List<ClashZoneRawClash>
        {
            Clash("g1", "PIPE-1", "BEAM-7", 0, 0, 0),
            Clash("g2", "PIPE-1", "BEAM-7", 200, 0, 0),
            Clash("g3", "PIPE-1", "BEAM-7", 9000, 0, 0),
            Clash("g4", "DUCT-2", "WALL-1", 100, 0, 0),
            Clash("g5", "DUCT-2", "WALL-1", 150, 0, 0, status: "Approved"),
        };

        var plan = ClashZoneRunPlanner.Plan(clashes, Run("run-1"), null, null);
        var record = plan.Record;

        Assert.Equal(1, record.SchemaVersion);
        Assert.Equal(5, record.RawClashCount);
        Assert.Equal(4, record.DrawnClashCount);
        Assert.Equal(1, record.ExcludedByStatusCount);
        Assert.Equal(3, record.Zones.Count);
        Assert.Equal(new[] { "Z-0001", "Z-0002", "Z-0003" }, record.Zones.Select(z => z.ZoneId));
        Assert.Equal(new[] { "DUCT-2|WALL-1", "PIPE-1|BEAM-7", "PIPE-1|BEAM-7" }, record.Zones.Select(z => z.PartitionKey));
        Assert.Equal(new[] { 1, 2, 1 }, record.Zones.Select(z => z.ClashCount));
        Assert.Equal(new[] { "T-0001", "T-0002", "T-0003" }, record.Zones.Select(z => z.TrackId));
        Assert.All(record.Zones, z => Assert.Equal(ClashZoneCompareStates.New, z.CompareState));
        Assert.Equal(4, record.NextTrackNumber);

        var pair = record.Zones[1];
        Assert.Equal(100, pair.CenterX, 6);
        Assert.Equal(200, pair.RadiusMm, 6);
        Assert.Equal(new[] { "g1", "g2" }, record.Clashes.Where(c => c.ZoneId == "Z-0002").Select(c => c.ResultGuid));
        Assert.Equal(new[] { "id-BEAM-7|id-PIPE-1" }, pair.ItemPairs);
        Assert.Equal("PIPE-1", pair.AssociationNameA);
        Assert.Equal("BEAM-7", pair.AssociationNameB);
        Assert.Equal(-1.5, pair.MinDistanceMm.Value, 6);
        Assert.Null(record.Clashes.SingleOrDefault(c => c.ResultGuid == "g5"));
        Assert.Empty(record.Disappeared);
    }

    [Fact]
    public void Plan_PointsModeDrawsEveryClashWithThePointRadius()
    {
        var options = new ClashZoneRunOptions { Mode = "points" };
        var clashes = new List<ClashZoneRawClash>
        {
            Clash("g1", "A", "B", 0, 0, 0),
            Clash("g2", "A", "B", 10, 0, 0),
        };

        var plan = ClashZoneRunPlanner.Plan(clashes, Run("run-1"), options, null);

        Assert.Equal(2, plan.Record.Zones.Count);
        Assert.All(plan.Record.Zones, z => Assert.Equal(50, z.RadiusMm, 6));
        Assert.Equal(5, plan.Markers.LatitudeBands);
        Assert.Equal(10, plan.Markers.LongitudeBands);
    }

    [Fact]
    public void Plan_MarkersCarryOneSphereAndThePropertiesPerZone()
    {
        var plan = ClashZoneRunPlanner.Plan(new List<ClashZoneRawClash> { Clash("g1", "PIPE-1", "BEAM-7", 1200, -300, 45) }, Run("run-1"), null, null);

        var marker = Assert.Single(plan.Markers.Markers);
        Assert.Equal("Z-0001", marker.Name);
        Assert.Equal(1200, marker.X, 6);
        Assert.Equal(150, marker.RadiusMm, 6);
        Assert.Equal(ClashZoneCompareStates.New, marker.StyleKey);
        var properties = marker.Properties.ToDictionary(p => p.Name, p => p.Value);
        Assert.Equal("Z-0001", properties["ZoneId"]);
        Assert.Equal("T-0001", properties["TrackId"]);
        Assert.Equal("run-1", properties["RunId"]);
        Assert.Equal(1, properties["ClashCount"]);
        Assert.Equal(150.0, properties["RadiusMm"]);
        Assert.Equal("new", properties["CompareState"]);
        Assert.Equal("PIPE-1", properties["SideA"]);
        Assert.Equal("BEAM-7", properties["SideB"]);
        Assert.Equal("run-1", plan.Markers.RunId);
        Assert.Equal(new[] { "new", "persisting", "moved", "disappeared" }, plan.Markers.Styles.Select(s => s.Key));
        Assert.Contains("IFCBUILDINGELEMENTPROXY", ClashZoneIfcWriter.Write(plan.Markers));
    }

    [Fact]
    public void Plan_SecondRunTracksZonesAndDrawsDisappearedOnesGrey()
    {
        var first = ClashZoneRunPlanner.Plan(new List<ClashZoneRawClash>
        {
            Clash("a1", "PIPE-1", "BEAM-7", 0, 0, 0),
            Clash("b1", "DUCT-2", "WALL-1", 20000, 0, 0),
        }, Run("run-1"), null, null);
        var previous = RoundTrip(first.Record);

        var second = ClashZoneRunPlanner.Plan(new List<ClashZoneRawClash>
        {
            Clash("a2", "PIPE-1", "BEAM-7", 40, 0, 0),
            Clash("c2", "CABLE-3", "SLAB-9", -20000, 0, 0),
        }, Run("run-2"), null, previous);

        var zones = second.Record.Zones.ToDictionary(z => z.PartitionKey);
        Assert.Equal(first.Record.Zones.Single(z => z.PartitionKey == "PIPE-1|BEAM-7").TrackId, zones["PIPE-1|BEAM-7"].TrackId);
        Assert.Equal(ClashZoneCompareStates.Persisting, zones["PIPE-1|BEAM-7"].CompareState);
        Assert.Equal(ClashZoneCompareStates.New, zones["CABLE-3|SLAB-9"].CompareState);
        Assert.Equal("T-0003", zones["CABLE-3|SLAB-9"].TrackId);
        Assert.Equal(4, second.Record.NextTrackNumber);

        var gone = Assert.Single(second.Record.Disappeared);
        Assert.Equal(ClashZoneCompareStates.Disappeared, gone.CompareState);
        Assert.Equal(gone.ZoneId, gone.PreviousZoneId);
        Assert.Equal(0, gone.ShiftMm);
        Assert.Empty(gone.MergedFrom);
        Assert.Equal(20000, gone.CenterX, 6);
        var greyMarker = second.Markers.Markers.Single(m => m.StyleKey == ClashZoneCompareStates.Disappeared);
        Assert.Equal("D" + gone.ZoneId, greyMarker.Name);
        Assert.Equal(3, second.Markers.Markers.Count);

        Assert.NotNull(second.ComparisonWorkbook);
        Assert.Equal(new[] { "Summary", "Matches", "New", "Disappeared" }, second.ComparisonWorkbook.Select(s => s.Name));
        Assert.Null(first.ComparisonWorkbook);
    }

    [Fact]
    public void Plan_RefusesToCompareRunsMadeWithDifferentParametersUnlessAllowed()
    {
        var previous = RoundTrip(ClashZoneRunPlanner.Plan(new List<ClashZoneRawClash> { Clash("a1", "A", "B", 0, 0, 0) }, Run("run-1"), null, null).Record);
        var other = new ClashZoneRunOptions { EpsMm = 500 };

        var error = Assert.Throws<ArgumentException>(() => ClashZoneRunPlanner.Plan(new List<ClashZoneRawClash> { Clash("a2", "A", "B", 0, 0, 0) }, Run("run-2"), other, previous));
        Assert.Contains("epsMm", error.Message);

        other.AllowParameterMismatch = true;
        var plan = ClashZoneRunPlanner.Plan(new List<ClashZoneRawClash> { Clash("a2", "A", "B", 0, 0, 0) }, Run("run-2"), other, previous);
        Assert.Contains(plan.Record.Warnings, w => w.Contains("epsMm"));
    }

    [Fact]
    public void Plan_WorkbookHasRunZonesAndClashesWithNumericCells()
    {
        var plan = ClashZoneRunPlanner.Plan(new List<ClashZoneRawClash>
        {
            Clash("g1", "PIPE-1", "BEAM-7", 0, 0, 0),
            Clash("g2", "PIPE-1", "BEAM-7", 200, 0, 0),
        }, Run("run-1"), null, null);

        Assert.Equal(new[] { "Run", "Zones", "Clashes" }, plan.Workbook.Select(s => s.Name));
        var zones = plan.Workbook[1];
        Assert.Equal("zoneId", zones.Columns[0]);
        var radiusColumn = zones.Columns.IndexOf("radiusMm");
        Assert.True(radiusColumn > 0);
        Assert.Equal(200.0, zones.Rows[0][radiusColumn]);
        var clashes = plan.Workbook[2];
        Assert.Equal(2, clashes.Rows.Count);
        Assert.Contains("resultGuid", clashes.Columns);
        var run = plan.Workbook[0].Rows.ToDictionary(r => (string)r[0], r => r[1]);
        Assert.Equal("run-1", run["runId"]);
        Assert.Equal(300.0, run["epsMm"]);
        Assert.Equal(1, run["zoneCount"]);
        Assert.Equal("zones.ifc", run["markerFile"]);

        using (var stream = new System.IO.MemoryStream())
            XlsxWorkbookWriter.Write(stream, plan.Workbook);
    }

    [Fact]
    public void Plan_RecordRoundTripsThroughJsonWithStableFieldNames()
    {
        var plan = ClashZoneRunPlanner.Plan(new List<ClashZoneRawClash> { Clash("g1", "PIPE-1", "BEAM-7", 0, 0, 0) }, Run("run-1"), null, null);

        var json = JsonConvert.SerializeObject(plan.Record);

        foreach (var field in new[] { "\"SchemaVersion\"", "\"RunId\"", "\"Parameters\"", "\"Zones\"", "\"Clashes\"", "\"Disappeared\"", "\"NextTrackNumber\"", "\"TrackId\"", "\"ItemPairs\"" })
            Assert.Contains(field, json);
        var back = JsonConvert.DeserializeObject<ClashZoneRunRecord>(json);
        Assert.Equal(plan.Record.Zones[0].TrackId, back.Zones[0].TrackId);
        Assert.Equal(plan.Record.Parameters.EpsMm, back.Parameters.EpsMm);
    }

    [Fact]
    public void Plan_IsDeterministicAndRejectsBadInput()
    {
        var clashes = Enumerable.Range(0, 60)
            .Select(i => Clash("g" + i.ToString("D2"), "P" + (i % 4), "B" + (i % 3), (i * 37) % 900 * 10.0, (i * 53) % 700 * 10.0, 0))
            .ToList();

        var a = JsonConvert.SerializeObject(ClashZoneRunPlanner.Plan(clashes, Run("run-1"), null, null).Record);
        var b = JsonConvert.SerializeObject(ClashZoneRunPlanner.Plan(Enumerable.Reverse(clashes).ToList(), Run("run-1"), null, null).Record);
        Assert.Equal(a, b);

        Assert.Throws<ArgumentNullException>(() => ClashZoneRunPlanner.Plan(null, Run("run-1"), null, null));
        Assert.Throws<ArgumentNullException>(() => ClashZoneRunPlanner.Plan(clashes, null, null, null));
        Assert.Throws<ArgumentException>(() => ClashZoneRunPlanner.Plan(clashes, Run("run-1"), new ClashZoneRunOptions { Mode = "cubes" }, null));
        Assert.Throws<ArgumentException>(() => ClashZoneRunPlanner.Plan(new List<ClashZoneRawClash> { Clash("g1", "A", "B", double.NaN, 0, 0) }, Run("run-1"), null, null));
    }

    [Fact]
    public void Plan_KeepsAssociationPairsApartWhenTheSeparatorOccursInAKey()
    {
        var plan = ClashZoneRunPlanner.Plan(new List<ClashZoneRawClash>
        {
            Clash("g1", "A|B", "C", 0, 0, 0),
            Clash("g2", "A", "B|C", 10, 0, 0),
        }, Run("run-1"), null, null);

        Assert.Equal(2, plan.Record.Zones.Count);
    }

    [Fact]
    public void Plan_WithoutResultGuidsDoesNotDependOnInputOrder()
    {
        var first = Clash("", "A", "B", 0, 0, 0);
        first.ClashName = "Clash 1";
        var second = Clash("", "A", "B", 0, 0, 0);
        second.ClashName = "Clash 2";

        var a = JsonConvert.SerializeObject(ClashZoneRunPlanner.Plan(new List<ClashZoneRawClash> { first, second }, Run("run-1"), null, null).Record);
        var b = JsonConvert.SerializeObject(ClashZoneRunPlanner.Plan(new List<ClashZoneRawClash> { second, first }, Run("run-1"), null, null).Record);

        Assert.Equal(a, b);
    }

    private static ClashZoneRunRecord RoundTrip(ClashZoneRunRecord record)
    {
        return JsonConvert.DeserializeObject<ClashZoneRunRecord>(JsonConvert.SerializeObject(record));
    }

    private static ClashZoneRunInfo Run(string id)
    {
        return new ClashZoneRunInfo
        {
            RunId = id,
            TimeStamp = "2026-10-15T12:00:00",
            DocumentPath = "C:\\models\\demo.nwd",
            DocumentUnits = "Meters",
            PluginVersion = "2.13.0.0",
            Tests = new List<string> { "PI vs CS" },
        };
    }

    private static ClashZoneRawClash Clash(string guid, string a, string b, double x, double y, double z, string status = "New")
    {
        return new ClashZoneRawClash
        {
            ResultGuid = guid,
            ClashName = "Clash " + guid,
            TestName = "PI vs CS",
            Status = status,
            X = x,
            Y = y,
            Z = z,
            DistanceMm = -1.5,
            AssociationKeyA = a,
            AssociationKeyB = b,
            AssociationNameA = a,
            AssociationNameB = b,
            ItemPathA = "/site/" + a,
            ItemPathB = "/site/" + b,
            StableIdA = "id-" + a,
            StableIdB = "id-" + b,
            SourceFileA = "pipes.rvm",
            SourceFileB = "steel.rvm",
        };
    }
}
