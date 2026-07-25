using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashReportValueHelper
    {
        public static List<string> NormalizeTextFilters(IEnumerable<string> filters)
        {
            if (filters == null)
                return new List<string>();

            return filters
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static void IncrementStatusCount(IDictionary<string, int> counts, string status)
        {
            if (counts == null)
                return;

            var key = string.IsNullOrWhiteSpace(status) ? "Unknown" : status.Trim();
            int current;
            counts.TryGetValue(key, out current);
            counts[key] = current + 1;
        }

        public static string FirstOrEmpty(IList<string> values)
        {
            return values == null || values.Count == 0 ? string.Empty : values[0] ?? string.Empty;
        }
    }
}
