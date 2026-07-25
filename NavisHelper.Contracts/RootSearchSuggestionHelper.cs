using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class RootSearchSuggestionHelper
    {
        public static List<string> Rank(string query, IEnumerable<string> candidates, int maxResults)
        {
            if (string.IsNullOrWhiteSpace(query) || candidates == null || maxResults < 1)
                return new List<string>();

            var normalizedQuery = Normalize(query);
            if (normalizedQuery.Length < 3)
                return new List<string>();

            return candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate))
                .Select(candidate => new { Value = candidate.Trim(), Normalized = Normalize(candidate) })
                .Where(candidate => candidate.Normalized.Length > 0)
                .GroupBy(candidate => candidate.Normalized, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(candidate => new { candidate.Value, Score = Score(normalizedQuery, candidate.Normalized) })
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Value, StringComparer.OrdinalIgnoreCase)
                .Take(maxResults).Select(candidate => candidate.Value).ToList();
        }

        private static int Score(string query, string candidate)
        {
            if (string.Equals(query, candidate, StringComparison.OrdinalIgnoreCase)) return 0;
            if (candidate.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || query.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                return 1000 - Math.Abs(candidate.Length - query.Length);
            var prefixLength = CommonPrefixLength(query, candidate);
            var distance = LevenshteinDistance(query, candidate);
            if (prefixLength < 2 && distance > Math.Max(2, Math.Max(query.Length, candidate.Length) / 3)) return 0;
            return (prefixLength * 100) - distance;
        }

        private static string Normalize(string value) => Path.GetFileName((value ?? string.Empty).Trim().Replace('/', '\\')).Trim().ToLowerInvariant();

        private static int CommonPrefixLength(string left, string right)
        {
            var length = Math.Min(left.Length, right.Length); var index = 0;
            while (index < length && left[index] == right[index]) index++;
            return index;
        }

        private static int LevenshteinDistance(string left, string right)
        {
            var previous = new int[right.Length + 1]; var current = new int[right.Length + 1];
            for (var index = 0; index <= right.Length; index++) previous[index] = index;
            for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
            {
                current[0] = leftIndex;
                for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
                {
                    var cost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                    current[rightIndex] = Math.Min(Math.Min(current[rightIndex - 1] + 1, previous[rightIndex] + 1), previous[rightIndex - 1] + cost);
                }
                var swap = previous; previous = current; current = swap;
            }
            return previous[right.Length];
        }
    }
}
