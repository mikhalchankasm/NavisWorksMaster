using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashStatusFilterPlan
    {
        public List<string> StatusFilters { get; set; } = new List<string>();
        public bool IncludeAllStatuses { get; set; }
        public int ExplicitStatusFilterCount { get; set; }

        public bool ShouldWarnIncludeAllOverrides
        {
            get { return IncludeAllStatuses && ExplicitStatusFilterCount > 0; }
        }
    }

    public static class ClashStatusFilterHelper
    {
        public const string NewStatus = "New";
        public const string ActiveStatus = "Active";

        public static ClashStatusFilterPlan Normalize(IEnumerable<string> filters, bool includeAllStatuses, bool defaultToNewActive)
        {
            var normalized = NormalizeRawFilters(filters);
            var effectiveIncludeAll = includeAllStatuses || HasAllStatusesFilter(normalized);
            var explicitFilters = normalized
                .Where(filter => !IsAllStatusesFilterValue(filter))
                .ToList();

            if (!effectiveIncludeAll && explicitFilters.Count == 0 && defaultToNewActive)
            {
                explicitFilters.Add(NewStatus);
                explicitFilters.Add(ActiveStatus);
            }

            return new ClashStatusFilterPlan
            {
                StatusFilters = explicitFilters,
                IncludeAllStatuses = effectiveIncludeAll,
                ExplicitStatusFilterCount = explicitFilters.Count,
            };
        }

        public static bool Matches(IEnumerable<string> statusFilters, string status)
        {
            if (statusFilters == null)
                return true;

            var filters = statusFilters as IList<string> ?? statusFilters.ToList();
            if (filters.Count == 0)
                return true;

            return filters.Any(filter => string.Equals(filter, status, StringComparison.OrdinalIgnoreCase));
        }

        public static bool HasAllStatusesFilter(IEnumerable<string> statusFilters)
        {
            return statusFilters != null && statusFilters.Any(IsAllStatusesFilterValue);
        }

        public static bool IsAllStatusesFilterValue(string statusFilter)
        {
            if (string.IsNullOrWhiteSpace(statusFilter))
                return false;

            var value = statusFilter.Trim();
            return value == "*" ||
                string.Equals(value, "All", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "Any", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> NormalizeRawFilters(IEnumerable<string> filters)
        {
            if (filters == null)
                return new List<string>();

            return filters
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
