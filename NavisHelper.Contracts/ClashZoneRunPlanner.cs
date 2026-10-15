using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashZoneRawClash
    {
        public string ResultGuid { get; set; }
        public string ClashName { get; set; }
        public string TestName { get; set; }
        public string Status { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double? DistanceMm { get; set; }
        public string AssociationKeyA { get; set; }
        public string AssociationKeyB { get; set; }
        public string AssociationNameA { get; set; }
        public string AssociationNameB { get; set; }
        public bool WeakAssociation { get; set; }
        public string ItemPathA { get; set; }
        public string ItemPathB { get; set; }
        public string StableIdA { get; set; }
        public string StableIdB { get; set; }
        public string SourceFileA { get; set; }
        public string SourceFileB { get; set; }
        public string CreatedTime { get; set; }
    }

    public sealed class ClashZoneRunInfo
    {
        public string RunId { get; set; }
        public string TimeStamp { get; set; }
        public string DocumentPath { get; set; }
        public string DocumentUnits { get; set; }
        public string PluginVersion { get; set; }
        public string MarkerFileName { get; set; } = "zones.ifc";
        public IList<string> Tests { get; set; }
    }

    public sealed class ClashZoneRunOptions
    {
        public string Mode { get; set; } = "zones";
        public double EpsMm { get; set; } = 300;
        public double MaxZoneRadiusMm { get; set; } = 3000;
        public double MinRadiusMm { get; set; } = 150;
        public double PadMm { get; set; } = 100;
        public double PointRadiusMm { get; set; } = 50;
        public IList<string> Statuses { get; set; }
        public bool AllowParameterMismatch { get; set; }
    }

    public sealed class ClashZoneRecord
    {
        public string ZoneId { get; set; }
        public string TrackId { get; set; }
        public string CompareState { get; set; }
        public string PreviousZoneId { get; set; }
        public string SplitFrom { get; set; }
        public IList<string> MergedFrom { get; set; }
        public string PartitionKey { get; set; }
        public int ClashCount { get; set; }
        public IDictionary<string, int> StatusCounts { get; set; } = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double CenterZ { get; set; }
        public double RadiusMm { get; set; }
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }
        public double? MinDistanceMm { get; set; }
        public IList<string> Tests { get; set; }
        public string AssociationNameA { get; set; }
        public string AssociationNameB { get; set; }
        public string AssociationKeyA { get; set; }
        public string AssociationKeyB { get; set; }
        public bool WeakAssociation { get; set; }
        public IList<string> SourceFilesA { get; set; }
        public IList<string> SourceFilesB { get; set; }
        public string RepresentativeClash { get; set; }
        public IList<string> ItemPairs { get; set; }
        public double ShiftMm { get; set; }
        public bool Shrunk { get; set; }
        public bool Grew { get; set; }
    }

    public sealed class ClashZoneClashRecord
    {
        public string ResultGuid { get; set; }
        public string ClashName { get; set; }
        public string TestName { get; set; }
        public string Status { get; set; }
        public string ZoneId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double? DistanceMm { get; set; }
        public string ItemPathA { get; set; }
        public string ItemPathB { get; set; }
        public string StableIdA { get; set; }
        public string StableIdB { get; set; }
        public string CreatedTime { get; set; }
    }

    public sealed class ClashZoneRunRecord
    {
        public int SchemaVersion { get; set; } = 1;
        public string RunId { get; set; }
        public string TimeStamp { get; set; }
        public string DocumentPath { get; set; }
        public string DocumentUnits { get; set; }
        public string PluginVersion { get; set; }
        public string MarkerFile { get; set; }
        public IList<string> Tests { get; set; }
        public ClashZoneRunOptions Parameters { get; set; }
        public int RawClashCount { get; set; }
        public int DrawnClashCount { get; set; }
        public int ExcludedByStatusCount { get; set; }
        public int NextTrackNumber { get; set; }
        public string PreviousRunId { get; set; }
        public IList<ClashZoneRecord> Zones { get; set; }
        public IList<ClashZoneRecord> Disappeared { get; set; }
        public IList<ClashZoneClashRecord> Clashes { get; set; }
        public IList<string> Warnings { get; set; }
    }

    public sealed class ClashZoneRunPlan
    {
        public ClashZoneRunRecord Record { get; set; }
        public ClashZoneIfcDocument Markers { get; set; }
        public IList<XlsxSheet> Workbook { get; set; }
        public IList<XlsxSheet> ComparisonWorkbook { get; set; }
    }

    /// <summary>Assembles serializable run artifacts without accessing files or external state.</summary>
    public static class ClashZoneRunPlanner
    {
        public static ClashZoneRunPlan Plan(IList<ClashZoneRawClash> clashes, ClashZoneRunInfo run,
            ClashZoneRunOptions options, ClashZoneRunRecord previous)
        {
            if (clashes == null) throw new ArgumentNullException(nameof(clashes));
            if (run == null) throw new ArgumentNullException(nameof(run));
            var effective = Effective(options);
            var warnings = new List<string>();
            if (previous != null)
            {
                var differing = Differences(Effective(previous.Parameters), effective);
                if (differing.Count > 0)
                {
                    var message = "Run parameters differ: " + string.Join(", ", differing);
                    if (!effective.AllowParameterMismatch) throw new ArgumentException(message, nameof(options));
                    warnings.Add(message);
                }
            }
            var statuses = new HashSet<string>(effective.Statuses, StringComparer.OrdinalIgnoreCase);
            var rows = new List<Member>();
            for (var i = 0; i < clashes.Count; i++)
            {
                var row = clashes[i];
                if (row == null || !Finite(row.X) || !Finite(row.Y) || !Finite(row.Z))
                    throw new ArgumentException("Clashes must have finite coordinates.", nameof(clashes));
                if (!statuses.Contains(row.Status)) continue;
                rows.Add(new Member { Row = row, Point = new ClashZonePoint {
                    // Without a GUID the id comes from the row's content, never its position, so the
                    // output cannot depend on input order.
                    Id = string.IsNullOrEmpty(row.ResultGuid) ? "#" + RawTieKey(row) : row.ResultGuid,
                    X = row.X, Y = row.Y, Z = row.Z } });
            }
            // Canonicalize before clustering, including ties in identifiers and coordinates.
            rows = rows.OrderBy(m => m.Point.Id, StringComparer.Ordinal)
                .ThenBy(m => m.Row.X).ThenBy(m => m.Row.Y).ThenBy(m => m.Row.Z)
                .ThenBy(m => RawTieKey(m.Row), StringComparer.Ordinal).ToList();
            bool points = effective.Mode == "points";
            var clusters = points ? rows.Select(m => new List<Member> { m }).ToList()
                : ClashClusterBuilder.Build(rows, "hybrid", effective.EpsMm, m => PairKey(m.Row.AssociationKeyA, m.Row.AssociationKeyB),
                    m => new ClashClusterPoint(m.Row.X, m.Row.Y, m.Row.Z));
            var geometry = ClashZoneGeometry.BuildZones(clusters.Select(c => new ClashZoneInputCluster {
                PartitionKey = Partition(c[0].Row), Points = c.Select(m => m.Point).ToList()
            }), new ClashZoneGeometryOptions {
                MaxZoneRadiusMm = points ? double.MaxValue : effective.MaxZoneRadiusMm,
                MinRadiusMm = points ? effective.PointRadiusMm : effective.MinRadiusMm,
                PadMm = points ? 0 : effective.PadMm });
            var members = rows.ToDictionary(m => m.Point, m => m.Row);
            var zones = new List<ClashZoneRecord>();
            var drawn = new List<ClashZoneClashRecord>();
            foreach (var zone in geometry)
            {
                var group = zone.Points.Select(p => members[p]).ToList();
                zones.Add(ZoneRecord(zone, group));
                drawn.AddRange(group.Select(c => new ClashZoneClashRecord {
                    ResultGuid = c.ResultGuid, ClashName = c.ClashName, TestName = c.TestName, Status = c.Status,
                    ZoneId = zone.ZoneId, X = c.X, Y = c.Y, Z = c.Z, DistanceMm = c.DistanceMm,
                    ItemPathA = c.ItemPathA, ItemPathB = c.ItemPathB, StableIdA = c.StableIdA,
                    StableIdB = c.StableIdB, CreatedTime = c.CreatedTime }));
            }
            var comparison = ClashZoneRunComparer.Compare(previous == null ? null : previous.Zones.Select(CompareZone).ToList(),
                zones.Select(CompareZone).ToList(), new ClashZoneCompareOptions {
                    MinimumNextTrackNumber = previous == null ? 1 : previous.NextTrackNumber });
            var byId = zones.ToDictionary(z => z.ZoneId, StringComparer.Ordinal);
            foreach (var entry in comparison.Current) ApplyComparison(byId[entry.ZoneId], entry);
            var disappeared = new List<ClashZoneRecord>();
            foreach (var entry in comparison.Disappeared)
            {
                var copy = CopyZone(previous.Zones.Single(z => z.ZoneId == entry.PreviousZoneId));
                // The copy describes this comparison, not the one that produced the previous run.
                copy.CompareState = ClashZoneCompareStates.Disappeared;
                copy.PreviousZoneId = entry.PreviousZoneId;
                copy.SplitFrom = null;
                copy.MergedFrom = new List<string>();
                copy.ShiftMm = 0;
                copy.Shrunk = false;
                copy.Grew = false;
                disappeared.Add(copy);
            }
            var record = new ClashZoneRunRecord {
                RunId = run.RunId, TimeStamp = run.TimeStamp, DocumentPath = run.DocumentPath,
                DocumentUnits = run.DocumentUnits, PluginVersion = run.PluginVersion, MarkerFile = run.MarkerFileName,
                Tests = Sorted(run.Tests), Parameters = effective, RawClashCount = clashes.Count,
                DrawnClashCount = rows.Count, ExcludedByStatusCount = clashes.Count - rows.Count,
                NextTrackNumber = comparison.NextTrackNumber, PreviousRunId = previous == null ? null : previous.RunId,
                Zones = zones, Disappeared = disappeared, Warnings = warnings,
                Clashes = drawn.OrderBy(c => c.ZoneId, StringComparer.Ordinal).ThenBy(c => c.ResultGuid, StringComparer.Ordinal).ToList() };
            return new ClashZoneRunPlan { Record = record, Markers = Markers(record, points), Workbook = Workbook(record),
                ComparisonWorkbook = previous == null ? null : ComparisonWorkbook(record, comparison) };
        }

        private sealed class Member
        {
            public ClashZoneRawClash Row;
            public ClashZonePoint Point;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static string Partition(ClashZoneRawClash row) => row.AssociationKeyA + "|" + row.AssociationKeyB;

        // Display keys join with "|", which may also occur inside a key; grouping and matching use
        // a length-prefixed form so ("A|B", "C") and ("A", "B|C") stay apart.
        private static string PairKey(string a, string b) =>
            (a ?? string.Empty).Length.ToString(CultureInfo.InvariantCulture) + ":" + a + "|" + b;
        private static List<string> Sorted(IEnumerable<string> values) => (values ?? new string[0])
            .Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();

        private static string RawTieKey(ClashZoneRawClash c)
        {
            // Length prefixes keep embedded separators unambiguous; null differs from empty.
            var fields = new[] { c.ResultGuid, c.TestName, c.ClashName, c.Status, c.AssociationKeyA, c.AssociationKeyB,
                c.AssociationNameA, c.AssociationNameB, c.ItemPathA, c.ItemPathB, c.StableIdA, c.StableIdB,
                c.SourceFileA, c.SourceFileB, c.CreatedTime, c.DistanceMm?.ToString("R", CultureInfo.InvariantCulture),
                c.WeakAssociation ? "1" : "0" };
            return string.Concat(fields.Select(s => s == null ? "-1:" : s.Length.ToString(CultureInfo.InvariantCulture) + ":" + s));
        }

        private static ClashZoneRunOptions Effective(ClashZoneRunOptions input)
        {
            var o = input ?? new ClashZoneRunOptions();
            if (!string.Equals(o.Mode, "zones", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(o.Mode, "points", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("mode must be zones or points.", nameof(input));
            var numbers = new[] { o.EpsMm, o.MaxZoneRadiusMm, o.MinRadiusMm, o.PointRadiusMm };
            if (numbers.Any(n => !Finite(n) || n <= 0) || !Finite(o.PadMm) || o.PadMm < 0)
                throw new ArgumentException("Run distances must be finite and positive; padMm may be zero.", nameof(input));
            var statuses = o.Statuses == null || o.Statuses.Count == 0 ? new[] { "New", "Active", "Reviewed" } : o.Statuses;
            return new ClashZoneRunOptions { Mode = o.Mode.ToLowerInvariant(), EpsMm = o.EpsMm,
                MaxZoneRadiusMm = o.MaxZoneRadiusMm, MinRadiusMm = o.MinRadiusMm, PadMm = o.PadMm,
                PointRadiusMm = o.PointRadiusMm, AllowParameterMismatch = o.AllowParameterMismatch,
                Statuses = Sorted(statuses).Distinct(StringComparer.OrdinalIgnoreCase).ToList() };
        }

        private static List<string> Differences(ClashZoneRunOptions a, ClashZoneRunOptions b)
        {
            var fields = new List<string>();
            if (a.Mode != b.Mode) fields.Add("mode");
            if (a.EpsMm != b.EpsMm) fields.Add("epsMm");
            if (a.MaxZoneRadiusMm != b.MaxZoneRadiusMm) fields.Add("maxZoneRadiusMm");
            if (a.MinRadiusMm != b.MinRadiusMm) fields.Add("minRadiusMm");
            if (a.PadMm != b.PadMm) fields.Add("padMm");
            if (a.PointRadiusMm != b.PointRadiusMm) fields.Add("pointRadiusMm");
            if (!new HashSet<string>(a.Statuses, StringComparer.OrdinalIgnoreCase).SetEquals(b.Statuses)) fields.Add("statuses");
            return fields;
        }

        private static ClashZoneRecord ZoneRecord(ClashZone z, IList<ClashZoneRawClash> rows)
        {
            var first = rows[0];
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in rows) { int n; counts.TryGetValue(row.Status, out n); counts[row.Status] = n + 1; }
            return new ClashZoneRecord { ZoneId = z.ZoneId, PartitionKey = z.PartitionKey, ClashCount = rows.Count,
                StatusCounts = counts, CenterX = z.CenterX, CenterY = z.CenterY, CenterZ = z.CenterZ, RadiusMm = z.RadiusMm,
                MinX = rows.Min(c => c.X), MinY = rows.Min(c => c.Y), MinZ = rows.Min(c => c.Z),
                MaxX = rows.Max(c => c.X), MaxY = rows.Max(c => c.Y), MaxZ = rows.Max(c => c.Z),
                MinDistanceMm = rows.Min(c => c.DistanceMm), Tests = Sorted(rows.Select(c => c.TestName)),
                AssociationNameA = first.AssociationNameA, AssociationNameB = first.AssociationNameB,
                AssociationKeyA = first.AssociationKeyA, AssociationKeyB = first.AssociationKeyB,
                WeakAssociation = rows.Any(c => c.WeakAssociation), SourceFilesA = Sorted(rows.Select(c => c.SourceFileA)),
                SourceFilesB = Sorted(rows.Select(c => c.SourceFileB)), RepresentativeClash = first.ClashName,
                ItemPairs = Sorted(rows.Where(c => !string.IsNullOrEmpty(c.StableIdA) && !string.IsNullOrEmpty(c.StableIdB))
                    .Select(c => string.CompareOrdinal(c.StableIdA, c.StableIdB) <= 0
                        ? c.StableIdA + "|" + c.StableIdB : c.StableIdB + "|" + c.StableIdA)) };
        }

        private static ClashZoneRunZone CompareZone(ClashZoneRecord z) => new ClashZoneRunZone {
            ZoneId = z.ZoneId, TrackId = z.TrackId, PartitionKey = PairKey(z.AssociationKeyA, z.AssociationKeyB), X = z.CenterX, Y = z.CenterY,
            Z = z.CenterZ, RadiusMm = z.RadiusMm, ClashCount = z.ClashCount, ItemPairs = z.ItemPairs };

        private static void ApplyComparison(ClashZoneRecord zone, ClashZoneComparisonEntry entry)
        {
            zone.TrackId = entry.TrackId; zone.CompareState = entry.CompareState;
            zone.PreviousZoneId = entry.PreviousZoneId; zone.SplitFrom = entry.SplitFrom;
            zone.MergedFrom = entry.MergedFrom.ToList(); zone.ShiftMm = entry.ShiftMm;
            zone.Shrunk = entry.Shrunk; zone.Grew = entry.Grew;
        }

        private static ClashZoneRecord CopyZone(ClashZoneRecord z) => new ClashZoneRecord {
            ZoneId = z.ZoneId, TrackId = z.TrackId, CompareState = z.CompareState, PreviousZoneId = z.PreviousZoneId,
            SplitFrom = z.SplitFrom, MergedFrom = z.MergedFrom == null ? null : z.MergedFrom.ToList(),
            PartitionKey = z.PartitionKey, ClashCount = z.ClashCount,
            StatusCounts = new SortedDictionary<string, int>(z.StatusCounts, StringComparer.Ordinal),
            CenterX = z.CenterX, CenterY = z.CenterY, CenterZ = z.CenterZ, RadiusMm = z.RadiusMm,
            MinX = z.MinX, MinY = z.MinY, MinZ = z.MinZ, MaxX = z.MaxX, MaxY = z.MaxY, MaxZ = z.MaxZ,
            MinDistanceMm = z.MinDistanceMm, Tests = Sorted(z.Tests), AssociationNameA = z.AssociationNameA,
            AssociationNameB = z.AssociationNameB, AssociationKeyA = z.AssociationKeyA, AssociationKeyB = z.AssociationKeyB,
            WeakAssociation = z.WeakAssociation, SourceFilesA = Sorted(z.SourceFilesA), SourceFilesB = Sorted(z.SourceFilesB),
            RepresentativeClash = z.RepresentativeClash, ItemPairs = Sorted(z.ItemPairs), ShiftMm = z.ShiftMm,
            Shrunk = z.Shrunk, Grew = z.Grew };

        private static ClashZoneIfcDocument Markers(ClashZoneRunRecord run, bool points)
        {
            var document = new ClashZoneIfcDocument { ProjectName = "Clash zones", SiteName = "Clash zones " + run.RunId,
                RunId = run.RunId, TimeStamp = run.TimeStamp, LatitudeBands = points ? 5 : 10, LongitudeBands = points ? 10 : 20,
                Styles = new List<ClashZoneMarkerStyle> { Style("new", 0.9, 0.1, 0.1, 0.6),
                    Style("persisting", 1, 0.55, 0, 0.6), Style("moved", 1, 0.85, 0, 0.6), Style("disappeared", 0.5, 0.5, 0.5, 0.8) } };
            foreach (var z in run.Zones.Concat(run.Disappeared))
            {
                var marker = new ClashZoneMarker { Name = (z.CompareState == "disappeared" ? "D" : "") + z.ZoneId,
                    X = z.CenterX, Y = z.CenterY, Z = z.CenterZ, RadiusMm = z.RadiusMm, StyleKey = z.CompareState };
                var names = new[] { "ZoneId", "TrackId", "RunId", "ClashCount", "RadiusMm", "CompareState", "SideA", "SideB", "Tests" };
                var values = new object[] { z.ZoneId, z.TrackId, run.RunId, z.ClashCount, z.RadiusMm, z.CompareState,
                    z.AssociationNameA, z.AssociationNameB, Join(z.Tests) };
                for (var i = 0; i < names.Length; i++)
                    marker.Properties.Add(new ClashZoneMarkerProperty { Name = names[i], Value = values[i] });
                if (z.MinDistanceMm.HasValue)
                    marker.Properties.Add(new ClashZoneMarkerProperty { Name = "MinDistanceMm", Value = z.MinDistanceMm.Value });
                document.Markers.Add(marker);
            }
            return document;
        }

        private static ClashZoneMarkerStyle Style(string key, double r, double g, double b, double t) =>
            new ClashZoneMarkerStyle { Key = key, Red = r, Green = g, Blue = b, Transparency = t };
        private static string Join(IEnumerable<string> values) => string.Join("; ", values ?? new string[0]);
        private static XlsxSheet Sheet(string name, string columns, IEnumerable<IList<object>> rows) =>
            new XlsxSheet { Name = name, Columns = columns.Split(','), Rows = rows.ToList() };

        private static IList<XlsxSheet> Workbook(ClashZoneRunRecord r)
        {
            var p = r.Parameters;
            var keys = "runId,timeStamp,documentPath,documentUnits,pluginVersion,mode,epsMm,maxZoneRadiusMm,minRadiusMm,padMm,pointRadiusMm,statuses,rawClashCount,drawnClashCount,excludedByStatusCount,zoneCount,markerFile,previousRunId".Split(',');
            var values = new object[] { r.RunId, r.TimeStamp, r.DocumentPath, r.DocumentUnits, r.PluginVersion,
                p.Mode, p.EpsMm, p.MaxZoneRadiusMm, p.MinRadiusMm, p.PadMm, p.PointRadiusMm, Join(p.Statuses),
                r.RawClashCount, r.DrawnClashCount, r.ExcludedByStatusCount, r.Zones.Count, r.MarkerFile, r.PreviousRunId };
            return new List<XlsxSheet> {
                Sheet("Run", "key,value", keys.Select((k, i) => (IList<object>)new object[] { k, values[i] })),
                ZoneSheet("Zones", r.Zones),
                Sheet("Clashes", "resultGuid,clashName,testName,status,zoneId,x,y,z,distanceMm,itemPathA,itemPathB,stableIdA,stableIdB,createdTime",
                    r.Clashes.Select(c => (IList<object>)new object[] { c.ResultGuid, c.ClashName, c.TestName, c.Status,
                        c.ZoneId, c.X, c.Y, c.Z, c.DistanceMm, c.ItemPathA, c.ItemPathB, c.StableIdA, c.StableIdB, c.CreatedTime })) };
        }

        private static XlsxSheet ZoneSheet(string name, IEnumerable<ClashZoneRecord> zones) => Sheet(name,
            "zoneId,trackId,compareState,previousZoneId,splitFrom,mergedFrom,partitionKey,clashCount,statusCounts,centerX,centerY,centerZ,radiusMm,minX,minY,minZ,maxX,maxY,maxZ,minDistanceMm,tests,associationNameA,associationNameB,associationKeyA,associationKeyB,weakAssociation,sourceFilesA,sourceFilesB,representativeClash,itemPairs,shiftMm,shrunk,grew",
            zones.Select(z => (IList<object>)new object[] { z.ZoneId, z.TrackId, z.CompareState, z.PreviousZoneId,
                z.SplitFrom, Join(z.MergedFrom), z.PartitionKey, z.ClashCount,
                Join(z.StatusCounts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => c.Key + ": " + c.Value.ToString(CultureInfo.InvariantCulture))),
                z.CenterX, z.CenterY, z.CenterZ, z.RadiusMm, z.MinX, z.MinY, z.MinZ, z.MaxX, z.MaxY, z.MaxZ,
                z.MinDistanceMm, Join(z.Tests), z.AssociationNameA, z.AssociationNameB, z.AssociationKeyA, z.AssociationKeyB,
                z.WeakAssociation, Join(z.SourceFilesA), Join(z.SourceFilesB), z.RepresentativeClash, Join(z.ItemPairs), z.ShiftMm, z.Shrunk, z.Grew }));

        private static IList<XlsxSheet> ComparisonWorkbook(ClashZoneRunRecord r, ClashZoneComparison comparison)
        {
            return new List<XlsxSheet> {
                Sheet("Summary", "key,value", new IList<object>[] {
                    new object[] { "runId", r.RunId }, new object[] { "previousRunId", r.PreviousRunId },
                    new object[] { "new", r.Zones.Count(z => z.CompareState == "new") },
                    new object[] { "persisting", r.Zones.Count(z => z.CompareState == "persisting") },
                    new object[] { "moved", r.Zones.Count(z => z.CompareState == "moved") },
                    new object[] { "disappeared", r.Disappeared.Count } }),
                Sheet("Matches", "zoneId,previousZoneId,trackId,compareState,evidence,shiftMm,score,shrunk,grew,splitFrom,mergedFrom",
                    comparison.Current.Where(e => e.PreviousZoneId != null).Select(e => (IList<object>)new object[] {
                        e.ZoneId, e.PreviousZoneId, e.TrackId, e.CompareState, e.Evidence, e.ShiftMm, e.Score,
                        e.Shrunk, e.Grew, e.SplitFrom, Join(e.MergedFrom) })),
                ZoneSheet("New", r.Zones.Where(z => z.CompareState == "new")), ZoneSheet("Disappeared", r.Disappeared) };
        }
    }
}
