using System;
using System.Globalization;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashClusterKeyHelper
    {
        public const int DefaultClusterLimit = 100;
        public const int MaxClusterLimit = 5000;
        public const int DefaultPreviewRowsPerCluster = 5;
        public const int MaxPreviewRowsPerCluster = 50;
        public const string ModeSpatial = "spatial";
        public const string ModeObjectPair = "object_pair";
        public const string ModeHybrid = "hybrid";
        public const string ModeNone = "none";
        public const string StableIdentityPrefix = "id";
        public const string NamedPathPrefix = "path";
        public const string SourceFilePrefix = "source";
        public const string LeafPathPrefix = "leaf";

        public static int ClampClusterLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultClusterLimit);
            if (value < 1)
                return 1;
            if (value > MaxClusterLimit)
                return MaxClusterLimit;
            return value;
        }

        public static int ClampPreviewRowsPerCluster(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultPreviewRowsPerCluster);
            if (value < 0)
                return 0;
            if (value > MaxPreviewRowsPerCluster)
                return MaxPreviewRowsPerCluster;
            return value;
        }

        public static string NormalizeMode(string groupMode)
        {
            if (string.IsNullOrWhiteSpace(groupMode))
                return ModeHybrid;

            var value = NormalizeToken(groupMode);
            switch (value)
            {
                case ModeSpatial:
                case "point":
                case "point_cluster":
                case "proximity":
                    return ModeSpatial;
                case ModeObjectPair:
                case "object":
                case "pair":
                case "association":
                case "association_pair":
                case "associated_objects":
                    return ModeObjectPair;
                case ModeHybrid:
                case "mixed":
                case "association_spatial":
                    return ModeHybrid;
                default:
                    return null;
            }
        }

        public static string NormalizeReportMode(string groupMode)
        {
            if (string.IsNullOrWhiteSpace(groupMode))
                return ModeNone;

            var value = NormalizeToken(groupMode);
            if (value == ModeNone || value == "off" || value == "false")
                return ModeNone;

            return NormalizeMode(value);
        }

        public static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return string.Join(" ", value.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        }

        public static string BuildTypedKey(string prefix, string value)
        {
            var normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? string.Empty : prefix.Trim().ToLowerInvariant();
            return normalizedPrefix + ":" + NormalizeKey(value);
        }

        public static string StableHexHash(string value)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                var text = value ?? string.Empty;
                for (var i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 1099511628211UL;
                }

                return hash.ToString("x16", CultureInfo.InvariantCulture);
            }
        }

        public static string BuildSpatialCellKey(int x, int y, int z)
        {
            return x.ToString(CultureInfo.InvariantCulture) + ":" +
                y.ToString(CultureInfo.InvariantCulture) + ":" +
                z.ToString(CultureInfo.InvariantCulture);
        }

        private static string NormalizeToken(string value)
        {
            return value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        }
    }
}
