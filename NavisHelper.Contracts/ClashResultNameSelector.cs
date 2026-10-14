using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashResultNameSelection
    {
        /// <summary>Zero-based positions in the test's flattened result list, in request order.</summary>
        public List<int> Positions { get; set; } = new List<int>();
        public List<string> MissingNames { get; set; } = new List<string>();
        public List<string> AmbiguousNames { get; set; } = new List<string>();

        public bool IsComplete
        {
            get { return MissingNames.Count == 0 && AmbiguousNames.Count == 0; }
        }
    }

    /// <summary>
    /// Selects clash results by display name instead of by <c>clash-result:T:N</c>.
    /// N is the result's position in the test's flattened tree, so it changes
    /// whenever a grouping tool moves results; a result's name does not.
    /// </summary>
    public static class ClashResultNameSelector
    {
        /// <param name="namesInTreeOrder">Display names of every result of one test, in flattened tree order.</param>
        /// <param name="requestedNames">Names to select. Matching is exact after trimming and ignores case;
        /// a name held by more than one result is reported as ambiguous rather than resolved to the first.</param>
        public static ClashResultNameSelection Resolve(IList<string> namesInTreeOrder, IEnumerable<string> requestedNames)
        {
            var selection = new ClashResultNameSelection();
            var positionsByName = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var names = namesInTreeOrder ?? new List<string>();
            for (var index = 0; index < names.Count; index++)
            {
                var key = (names[index] ?? string.Empty).Trim();
                if (key.Length == 0)
                    continue;
                List<int> positions;
                if (!positionsByName.TryGetValue(key, out positions))
                {
                    positions = new List<int>();
                    positionsByName.Add(key, positions);
                }
                positions.Add(index);
            }

            foreach (var requested in NormalizeNames(requestedNames))
            {
                List<int> positions;
                if (!positionsByName.TryGetValue(requested, out positions))
                    selection.MissingNames.Add(requested);
                else if (positions.Count > 1)
                    selection.AmbiguousNames.Add(requested);
                else
                    selection.Positions.Add(positions[0]);
            }

            return selection;
        }

        public static List<string> NormalizeNames(IEnumerable<string> names)
        {
            return (names ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string DescribeProblems(ClashResultNameSelection selection)
        {
            if (selection == null || selection.IsComplete)
                return string.Empty;
            var parts = new List<string>();
            if (selection.MissingNames.Count > 0)
                parts.Add("not found: " + string.Join(", ", selection.MissingNames));
            if (selection.AmbiguousNames.Count > 0)
                parts.Add("held by more than one result (use resultHandles): " + string.Join(", ", selection.AmbiguousNames));
            return "Clash result names " + string.Join("; ", parts) + ".";
        }
    }
}
