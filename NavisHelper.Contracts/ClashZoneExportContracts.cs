using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashZoneExportRequest
    {
        public List<string> TestNames { get; set; } = new List<string>();
        public List<string> TestHandles { get; set; } = new List<string>();
        public List<string> Statuses { get; set; } = new List<string>();
        public string Mode { get; set; }
        public double? EpsMm { get; set; }
        public double? MaxZoneRadiusMm { get; set; }
        public double? MinRadiusMm { get; set; }
        public double? PadMm { get; set; }
        public double? PointRadiusMm { get; set; }
        public bool? IncludeIgnored { get; set; }
        public int? MaxResults { get; set; }
        public string OutputDirectory { get; set; }
        public string RunId { get; set; }
        public string PreviousRunPath { get; set; }
        public bool? AllowParameterMismatch { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class ClashZoneExportZoneSummary
    {
        public string ZoneId { get; set; }
        public string TrackId { get; set; }
        public string CompareState { get; set; }
        public int ClashCount { get; set; }
        public double RadiusMm { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public double CenterZ { get; set; }
        public string AssociationNameA { get; set; }
        public string AssociationNameB { get; set; }
    }

    public sealed class ClashZoneExportResponse
    {
        public string RunId { get; set; }
        public string PreviousRunId { get; set; }
        public string RunDirectory { get; set; }
        public string Mode { get; set; }
        public bool Applied { get; set; }
        public int MatchedTestCount { get; set; }
        public int RawClashCount { get; set; }
        public int DrawnClashCount { get; set; }
        public int ExcludedByStatusCount { get; set; }
        public int ZoneCount { get; set; }
        public int DisappearedCount { get; set; }
        public int WeakAssociationCount { get; set; }
        public bool ResultsTruncated { get; set; }
        public Dictionary<string, int> StateCounts { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
        public List<ClashZoneExportZoneSummary> LargestZones { get; set; } = new List<ClashZoneExportZoneSummary>();
        public List<string> Files { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public string Message { get; set; }
    }

    public static class ClashZoneExportPolicy
    {
        public const string RecordFileName = "zones.json";
        public const string WorkbookFileName = "zones.xlsx";
        public const string MarkerFileName = "zones.ifc";

        public static string ComparisonFileName(string previous, string current) =>
            "clash_zones_compare_" + previous + "_" + current + ".xlsx";

        public static string ResolveRunId(string requested, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var value = requested.Trim();
            if (!Regex.IsMatch(value, @"\A[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant))
                throw new ArgumentException("runId must contain 1-64 ASCII letters, digits, underscores or hyphens.");
            return value;
        }

        public static string ResolveRunDirectory(string documentPath, string outputDirectory, string runId)
        {
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                return Path.Combine(Path.GetFullPath(outputDirectory), runId);
            if (string.IsNullOrWhiteSpace(documentPath))
                throw new ArgumentException("Specify outputDirectory when the document has no file path.");
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(documentPath)), "clash_zones", runId);
        }

        public static string ResolvePreviousRecordPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var full = Path.GetFullPath(path);
            return full.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? full : Path.Combine(full, RecordFileName);
        }

        public static ClashZoneRunOptions ToRunOptions(ClashZoneExportRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var options = new ClashZoneRunOptions();
            options.Mode = (request.Mode ?? "zones").ToLowerInvariant();
            if (options.Mode != "zones" && options.Mode != "points")
                throw new ArgumentException("mode must be zones or points.");
            options.EpsMm = Number(request.EpsMm, options.EpsMm, "epsMm");
            options.MaxZoneRadiusMm = Number(request.MaxZoneRadiusMm, options.MaxZoneRadiusMm, "maxZoneRadiusMm");
            options.MinRadiusMm = Number(request.MinRadiusMm, options.MinRadiusMm, "minRadiusMm");
            options.PadMm = Number(request.PadMm, options.PadMm, "padMm", true);
            options.PointRadiusMm = Number(request.PointRadiusMm, options.PointRadiusMm, "pointRadiusMm");
            options.Statuses = request.Statuses == null || request.Statuses.Count == 0 ? null : request.Statuses.ToList();
            options.AllowParameterMismatch = request.AllowParameterMismatch == true;
            return options;
        }

        private static double Number(double? requested, double fallback, string name, bool allowZero = false)
        {
            if (!requested.HasValue) return fallback;
            var value = requested.Value;
            if (double.IsNaN(value) || double.IsInfinity(value) || (allowZero ? value < 0 : value <= 0))
                throw new ArgumentException(name + (allowZero ? " must be finite and non-negative." : " must be finite and positive."));
            return value;
        }

        public static int ClampMaxResults(int? requested) =>
            !requested.HasValue || requested.Value <= 0 ? 50000 : Math.Min(requested.Value, 50000);

        public static void Summarize(ClashZoneRunPlan plan, ClashZoneExportResponse response, int top)
        {
            var record = plan.Record;
            response.RunId = record.RunId;
            response.PreviousRunId = record.PreviousRunId;
            response.RawClashCount = record.RawClashCount;
            response.DrawnClashCount = record.DrawnClashCount;
            response.ExcludedByStatusCount = record.ExcludedByStatusCount;
            response.ZoneCount = record.Zones.Count;
            response.DisappearedCount = record.Disappeared.Count;
            response.WeakAssociationCount = record.Zones.Count(z => z.WeakAssociation);
            response.StateCounts = record.Zones.GroupBy(z => z.CompareState, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            if (response.DisappearedCount > 0) response.StateCounts["disappeared"] = response.DisappearedCount;
            response.Warnings.AddRange(record.Warnings);
            response.LargestZones = record.Zones.OrderByDescending(z => z.RadiusMm)
                .ThenBy(z => z.ZoneId, StringComparer.Ordinal).Take(Math.Max(0, top))
                .Select(z => new ClashZoneExportZoneSummary {
                    ZoneId = z.ZoneId, TrackId = z.TrackId, CompareState = z.CompareState, ClashCount = z.ClashCount,
                    RadiusMm = z.RadiusMm, CenterX = z.CenterX, CenterY = z.CenterY, CenterZ = z.CenterZ,
                    AssociationNameA = z.AssociationNameA, AssociationNameB = z.AssociationNameB }).ToList();
        }
    }
}
