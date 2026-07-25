using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashUngroupSelectionCandidate
    {
        public string GroupKey { get; set; }
        public string ContainerKey { get; set; }
        public int GroupIndex { get; set; }
        public int ResultCount { get; set; }
    }

    public sealed class ClashUngroupSelectionPlan
    {
        public List<ClashUngroupSelectionCandidate> Groups { get; set; } = new List<ClashUngroupSelectionCandidate>();
    }

    public static class ClashUngroupSelectionPlanHelper
    {
        public static ClashUngroupSelectionPlan Build(IEnumerable<ClashUngroupSelectionCandidate> persistentGroups)
        {
            var rows = (persistentGroups ?? Enumerable.Empty<ClashUngroupSelectionCandidate>()).ToList();
            if (rows.Count == 0)
                throw new ArgumentException("At least one persistent group is required.", nameof(persistentGroups));
            if (rows.Any(row => string.IsNullOrWhiteSpace(row.GroupKey) ||
                                string.IsNullOrWhiteSpace(row.ContainerKey) ||
                                row.GroupIndex < 0 ||
                                row.ResultCount < 0))
            {
                throw new InvalidOperationException("Every selected group must have a valid persistent location.");
            }

            var uniqueGroups = new Dictionary<string, ClashUngroupSelectionCandidate>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                ClashUngroupSelectionCandidate existing;
                if (uniqueGroups.TryGetValue(row.GroupKey, out existing))
                {
                    if (!string.Equals(existing.ContainerKey, row.ContainerKey, StringComparison.Ordinal) ||
                        existing.GroupIndex != row.GroupIndex ||
                        existing.ResultCount != row.ResultCount)
                    {
                        throw new InvalidOperationException("Duplicate group rows have conflicting persistent locations.");
                    }

                    continue;
                }

                uniqueGroups.Add(row.GroupKey, row);
            }

            var groups = uniqueGroups.Values
                .OrderBy(row => row.ContainerKey, StringComparer.Ordinal)
                .ThenByDescending(row => row.GroupIndex)
                .ToList();

            return new ClashUngroupSelectionPlan
            {
                Groups = groups,
            };
        }
    }
}
