using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashZoneRunZone
    {
        public string ZoneId { get; set; }
        public string TrackId { get; set; }
        public string PartitionKey { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double RadiusMm { get; set; }
        public int ClashCount { get; set; }
        public IList<string> ItemPairs { get; set; } = new List<string>();
    }

    public sealed class ClashZoneCompareOptions
    {
        public double MaxShiftMm { get; set; } = 2000;
        public double StillMinMm { get; set; } = 100;
        public double StillRadiusFraction { get; set; } = 0.25;
        public double ChangeFraction { get; set; } = 0.2;
        public int MinimumNextTrackNumber { get; set; } = 1;
    }

    public static class ClashZoneCompareStates
    {
        public const string New = "new";
        public const string Persisting = "persisting";
        public const string Moved = "moved";
        public const string Disappeared = "disappeared";
    }

    public static class ClashZoneCompareEvidence
    {
        public const string Items = "items";
        public const string Geometry = "geometry";
    }

    public sealed class ClashZoneComparisonEntry
    {
        public string ZoneId { get; set; }
        public string PreviousZoneId { get; set; }
        public string TrackId { get; set; }
        public string CompareState { get; set; }
        public string Evidence { get; set; }
        public double ShiftMm { get; set; }
        public double Score { get; set; }
        public bool Shrunk { get; set; }
        public bool Grew { get; set; }
        public string SplitFrom { get; set; }
        public IList<string> MergedFrom { get; set; } = new List<string>();
    }

    public sealed class ClashZoneComparison
    {
        public IList<ClashZoneComparisonEntry> Current { get; set; } = new List<ClashZoneComparisonEntry>();
        public IList<ClashZoneComparisonEntry> Disappeared { get; set; } = new List<ClashZoneComparisonEntry>();
        public int NextTrackNumber { get; set; }
    }

    /// <summary>Compares snapshots without modifying either run or accessing external state.</summary>
    public static class ClashZoneRunComparer
    {
        public static ClashZoneComparison Compare(IList<ClashZoneRunZone> previous,
            IList<ClashZoneRunZone> current, ClashZoneCompareOptions options)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            options = options ?? new ClashZoneCompareOptions();
            ValidateOptions(options);
            Validate(current, false, nameof(current));
            long next = Math.Max(1, options.MinimumNextTrackNumber);
            if (previous != null)
                next = Math.Max(next, Validate(previous, true, nameof(previous)) + 1);

            var before = (previous ?? new ClashZoneRunZone[0])
                .OrderBy(z => z.ZoneId, StringComparer.Ordinal).ToArray();
            var after = current.OrderBy(z => z.ZoneId, StringComparer.Ordinal).ToArray();
            var previousItems = before.Select(ItemSet).ToArray();
            var currentItems = after.Select(ItemSet).ToArray();
            var candidates = new List<Candidate>();
            for (int p = 0; p < before.Length; p++)
            {
                for (int c = 0; c < after.Length; c++)
                {
                    var a = before[p];
                    var b = after[c];
                    double distance = Distance(a, b);
                    if (!Finite(distance) || distance > a.RadiusMm + b.RadiusMm ||
                        distance > options.MaxShiftMm) continue;

                    int shared = previousItems[p].Count(item => currentItems[c].Contains(item));
                    bool items = shared > 0;
                    if (!items && !string.Equals(a.PartitionKey, b.PartitionKey, StringComparison.Ordinal))
                        continue;
                    double score = items
                        ? (double)shared / (previousItems[p].Count + currentItems[c].Count - shared)
                        : 1 - distance / (a.RadiusMm + b.RadiusMm);
                    candidates.Add(new Candidate { Previous = p, Current = c,
                        Distance = distance, Items = items, Score = score });
                }
            }

            // Indices follow ordinal zone order and therefore supply both final tie-breakers.
            candidates.Sort((a, b) =>
            {
                int order = b.Items.CompareTo(a.Items);
                if (order == 0) order = b.Score.CompareTo(a.Score);
                if (order == 0) order = a.Previous.CompareTo(b.Previous);
                if (order == 0) order = a.Current.CompareTo(b.Current);
                return order;
            });
            var assignedPrevious = new bool[before.Length];
            var matches = new Candidate[after.Length];
            var bestPrevious = new Candidate[before.Length];
            var bestCurrent = new Candidate[after.Length];
            foreach (var candidate in candidates)
            {
                int p = candidate.Previous, c = candidate.Current;
                if (bestPrevious[p] == null) bestPrevious[p] = candidate;
                if (bestCurrent[c] == null) bestCurrent[c] = candidate;
                if (assignedPrevious[p] || matches[c] != null) continue;
                assignedPrevious[p] = true;
                matches[c] = candidate;
            }

            var result = new ClashZoneComparison();
            for (int c = 0; c < after.Length; c++)
            {
                var zone = after[c];
                var entry = new ClashZoneComparisonEntry { ZoneId = zone.ZoneId };
                var match = matches[c];
                if (match == null)
                {
                    // Leave room for the next unused number in the public int property.
                    if (next >= int.MaxValue) throw new ArgumentException("Track numbers exceed the supported range.");
                    entry.TrackId = "T-" + next.ToString("D4", CultureInfo.InvariantCulture);
                    next++;
                    entry.CompareState = ClashZoneCompareStates.New;
                    entry.SplitFrom = bestCurrent[c] == null ? null : before[bestCurrent[c].Previous].ZoneId;
                }
                else
                {
                    var old = before[match.Previous];
                    entry.TrackId = old.TrackId;
                    entry.PreviousZoneId = old.ZoneId;
                    entry.Evidence = match.Items ? ClashZoneCompareEvidence.Items : ClashZoneCompareEvidence.Geometry;
                    entry.Score = match.Score;
                    entry.ShiftMm = match.Distance;
                    entry.CompareState = match.Distance <= Math.Max(options.StillMinMm,
                        options.StillRadiusFraction * old.RadiusMm)
                        ? ClashZoneCompareStates.Persisting : ClashZoneCompareStates.Moved;
                    entry.Shrunk = zone.ClashCount < old.ClashCount * (1 - options.ChangeFraction) ||
                        zone.RadiusMm < old.RadiusMm * (1 - options.ChangeFraction);
                    entry.Grew = zone.ClashCount > old.ClashCount * (1 + options.ChangeFraction) ||
                        zone.RadiusMm > old.RadiusMm * (1 + options.ChangeFraction);
                }
                result.Current.Add(entry);
            }

            for (int p = 0; p < before.Length; p++)
            {
                if (assignedPrevious[p]) continue;
                var old = before[p];
                result.Disappeared.Add(new ClashZoneComparisonEntry { PreviousZoneId = old.ZoneId,
                    TrackId = old.TrackId, CompareState = ClashZoneCompareStates.Disappeared });
                if (bestPrevious[p] != null)
                    result.Current[bestPrevious[p].Current].MergedFrom.Add(old.ZoneId);
            }
            // Previous zones are visited in ordinal order, so every MergedFrom list is sorted.
            if (next > int.MaxValue) throw new ArgumentException("Track numbers exceed the supported range.");
            result.NextTrackNumber = (int)next;
            return result;
        }

        private static void ValidateOptions(ClashZoneCompareOptions options)
        {
            // A NaN threshold would silently disable its comparison instead of failing.
            if (!Finite(options.MaxShiftMm) || options.MaxShiftMm < 0)
                throw new ArgumentOutOfRangeException(nameof(options.MaxShiftMm));
            if (!Finite(options.StillMinMm) || options.StillMinMm < 0)
                throw new ArgumentOutOfRangeException(nameof(options.StillMinMm));
            if (!Finite(options.StillRadiusFraction) || options.StillRadiusFraction < 0)
                throw new ArgumentOutOfRangeException(nameof(options.StillRadiusFraction));
            if (!Finite(options.ChangeFraction) || options.ChangeFraction < 0 || options.ChangeFraction >= 1)
                throw new ArgumentOutOfRangeException(nameof(options.ChangeFraction));
        }

        private static long Validate(IList<ClashZoneRunZone> zones, bool requireTrack, string parameter)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var tracks = new HashSet<string>(StringComparer.Ordinal);
            long maximum = 0;
            foreach (var zone in zones)
            {
                if (zone == null || zone.ZoneId == null || !ids.Add(zone.ZoneId))
                    throw new ArgumentException("Zones must have unique non-null identifiers.", parameter);
                if (!Finite(zone.X) || !Finite(zone.Y) || !Finite(zone.Z) ||
                    !Finite(zone.RadiusMm) || zone.RadiusMm <= 0)
                    throw new ArgumentException("Zone coordinates must be finite and radii positive and finite.", parameter);
                if (!requireTrack) continue;
                int number;
                if (zone.TrackId == null || !Regex.IsMatch(zone.TrackId, @"\AT-(\d+)\z") ||
                    !int.TryParse(zone.TrackId.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out number))
                    throw new ArgumentException("Previous zones must have a numeric T- track identifier in range.", parameter);
                if (!tracks.Add(zone.TrackId))
                    throw new ArgumentException("Previous zones must not share a track identifier: " + zone.TrackId, parameter);
                maximum = Math.Max(maximum, number);
            }
            return maximum;
        }

        private static HashSet<string> ItemSet(ClashZoneRunZone zone)
        {
            return new HashSet<string>(zone.ItemPairs ?? new string[0], StringComparer.Ordinal);
        }

        private static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double Distance(ClashZoneRunZone a, ClashZoneRunZone b)
        {
            double x = Math.Abs(a.X - b.X), y = Math.Abs(a.Y - b.Y), z = Math.Abs(a.Z - b.Z);
            double scale = Math.Max(x, Math.Max(y, z));
            if (scale == 0 || double.IsInfinity(scale)) return scale;
            x /= scale;
            y /= scale;
            z /= scale;
            return scale * Math.Sqrt(x * x + y * y + z * z);
        }

        private sealed class Candidate
        {
            public int Previous;
            public int Current;
            public double Distance;
            public bool Items;
            public double Score;
        }
    }
}
