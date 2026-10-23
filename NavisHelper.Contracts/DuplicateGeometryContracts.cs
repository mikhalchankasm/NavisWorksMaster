using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class DuplicateGeometryCandidate
    {
        public int Index { get; set; }
        public string RootKey { get; set; }
        public string ParentKey { get; set; }
        public string DisplayName { get; set; }
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }
        public int PrimitiveCount { get; set; }
    }

    public sealed class DuplicateGeometryGroup
    {
        public string Key { get; set; }
        public List<int> ItemIndexes { get; set; } = new List<int>();
        public int RootCount { get; set; }
        public bool SameParentAndName { get; set; }
    }

    public static class DuplicateGeometryModes
    {
        public const string All = "all";
        public const string WithinRoot = "within_root";
        public const string AcrossRoots = "across_roots";

        public static bool TryNormalize(string value, out string mode)
        {
            mode = string.IsNullOrWhiteSpace(value) ? All : value.Trim().ToLowerInvariant();
            return mode == All || mode == WithinRoot || mode == AcrossRoots;
        }
    }

    public static class DuplicateGeometryRules
    {
        public static double ClampGrid(double? value)
        {
            var grid = value ?? 0.01;
            return double.IsNaN(grid) ? 0.01 : Math.Max(0.0001, Math.Min(1.0, grid));
        }

        public static string Key(DuplicateGeometryCandidate c, double grid)
        {
            var coordinates = new[] { c.MinX, c.MinY, c.MinZ, c.MaxX, c.MaxY, c.MaxZ };
            return string.Join(":", coordinates.Select(value =>
                ((long)Math.Round(value / grid, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture)))
                + ":" + c.PrimitiveCount.ToString(CultureInfo.InvariantCulture);
        }

        public static List<DuplicateGeometryGroup> Group(IEnumerable<DuplicateGeometryCandidate> items,
            double grid, string mode, int maxGroups)
        {
            string normalizedMode;
            if (!DuplicateGeometryModes.TryNormalize(mode, out normalizedMode))
                throw new ArgumentException("Mode must be all, within_root or across_roots.", nameof(mode));

            var result = new List<DuplicateGeometryGroup>();
            foreach (var geometryGroup in items.GroupBy(item => Key(item, grid), StringComparer.Ordinal))
            {
                if (normalizedMode == DuplicateGeometryModes.WithinRoot)
                {
                    foreach (var rootGroup in geometryGroup.GroupBy(item => item.RootKey, StringComparer.Ordinal))
                        AddGroup(result, geometryGroup.Key, rootGroup.ToList(), false);
                }
                else
                    AddGroup(result, geometryGroup.Key, geometryGroup.ToList(),
                        normalizedMode == DuplicateGeometryModes.AcrossRoots);
            }
            return result.OrderByDescending(group => group.ItemIndexes.Count)
                .ThenBy(group => group.Key, StringComparer.Ordinal).Take(maxGroups).ToList();
        }

        private static void AddGroup(List<DuplicateGeometryGroup> result, string key,
            List<DuplicateGeometryCandidate> items, bool requireMultipleRoots)
        {
            if (items.Count < 2)
                return;
            var rootCount = items.Select(item => item.RootKey).Distinct(StringComparer.Ordinal).Count();
            if (requireMultipleRoots && rootCount < 2)
                return;
            var first = items[0];
            result.Add(new DuplicateGeometryGroup
            {
                Key = key,
                ItemIndexes = items.Select(item => item.Index).OrderBy(index => index).ToList(),
                RootCount = rootCount,
                SameParentAndName = items.All(item =>
                    string.Equals(item.ParentKey, first.ParentKey, StringComparison.Ordinal) &&
                    string.Equals(item.DisplayName, first.DisplayName, StringComparison.OrdinalIgnoreCase)),
            });
        }
    }

    public sealed class FindDuplicateGeometryRequest
    {
        public List<string> MatchHandles { get; set; } = new List<string>();
        public bool? UseSelection { get; set; }
        public double? GridSize { get; set; }
        public string Mode { get; set; }
        public int? MaxGroups { get; set; }
        public int? PreviewPerGroup { get; set; }
    }

    public sealed class DuplicateGeometryGroupInfo
    {
        public int ItemCount { get; set; }
        public int RootCount { get; set; }
        public bool SameParentAndName { get; set; }
        public string MatchHandle { get; set; }
        public List<string> PreviewPaths { get; set; } = new List<string>();
    }

    public sealed class FindDuplicateGeometryResponse
    {
        public double GridSize { get; set; }
        public string Mode { get; set; }
        public int ScannedItemCount { get; set; }
        public bool ScanTruncated { get; set; }
        public int GroupCount { get; set; }
        public bool GroupsTruncated { get; set; }
        public int DuplicateItemCount { get; set; }
        public List<DuplicateGeometryGroupInfo> Groups { get; set; } = new List<DuplicateGeometryGroupInfo>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
