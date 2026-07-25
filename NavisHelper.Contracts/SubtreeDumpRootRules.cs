using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class SubtreeDumpRootRules
    {
        public static bool IsCandidate(string ownSourceFile, string displayName)
        {
            return !string.IsNullOrWhiteSpace(ownSourceFile) || IsModelFileName(displayName);
        }

        public static List<string> BuildAliases(string displayName, string ownSourceFile)
        {
            var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddAlias(aliases, displayName);
            AddAlias(aliases, ownSourceFile);
            foreach (var alias in aliases.ToList())
                AddAlias(aliases, GetContentFileName(alias));

            return aliases.ToList();
        }

        public static bool Matches(IEnumerable<string> aliases, string value)
        {
            if (aliases == null || string.IsNullOrWhiteSpace(value))
                return false;

            return aliases.Any(alias => string.Equals(alias, value, StringComparison.OrdinalIgnoreCase));
        }

        public static string GetContentFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.Trim();
            var lastSeparator = trimmed.LastIndexOfAny(new[] { '\\', '/' });
            return lastSeparator >= 0 && lastSeparator + 1 < trimmed.Length
                ? trimmed.Substring(lastSeparator + 1)
                : trimmed;
        }

        public static bool IsModelFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim();
            return trimmed.EndsWith(".rvm", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.EndsWith(".nwd", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.EndsWith(".nwf", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddAlias(ICollection<string> aliases, string value)
        {
            if (aliases == null || string.IsNullOrWhiteSpace(value))
                return;

            aliases.Add(value.Trim());
        }
    }
}
