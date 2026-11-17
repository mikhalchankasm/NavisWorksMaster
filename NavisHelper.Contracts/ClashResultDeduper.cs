using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashResultDuplicateKey
    {
        public static string For(ClashResultSummary row)
        {
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            var key = new StringBuilder();
            Append(key, row.TestName);
            AppendNames(key, row.Item1Names);
            AppendNames(key, row.Item2Names);
            Append(key, Round(row.ClashPoint == null ? (double?)null : row.ClashPoint.X, 6));
            Append(key, Round(row.ClashPoint == null ? (double?)null : row.ClashPoint.Y, 6));
            Append(key, Round(row.ClashPoint == null ? (double?)null : row.ClashPoint.Z, 6));
            Append(key, Round(row.DistanceMm, 3));
            return key.ToString();
        }

        private static void AppendNames(StringBuilder key, IList<string> names)
        {
            Append(key, (names == null ? 0 : names.Count).ToString(CultureInfo.InvariantCulture));
            if (names != null)
                foreach (var name in names)
                    Append(key, name);
        }

        private static void Append(StringBuilder key, string value)
        {
            // Escape both the separator and the escape character; counts mark side boundaries.
            key.Append((value ?? string.Empty).Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n"));
            key.Append('\n');
        }

        private static string Round(double? value, int decimals)
        {
            if (!value.HasValue)
                return string.Empty;
            var rounded = Math.Round(value.Value, decimals);
            if (rounded == 0)
                rounded = 0; // Normalize negative zero across target runtimes.
            return rounded.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }
    }

    public sealed class ClashResultDedupeResult
    {
        public List<ClashResultSummary> Kept { get; set; } = new List<ClashResultSummary>();
        public int FoldedCount { get; set; }
    }

    public static class ClashResultDeduper
    {
        public static ClashResultDedupeResult Dedupe(IList<ClashResultSummary> rows)
        {
            var result = new ClashResultDedupeResult();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows ?? new List<ClashResultSummary>())
            {
                if (row == null)
                    continue;
                if (keys.Add(ClashResultDuplicateKey.For(row)))
                    result.Kept.Add(row);
                else
                    result.FoldedCount++;
            }
            return result;
        }
    }
}
