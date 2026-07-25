using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashGroupDisplayPolicy
    {
        public const string MissingItemName = "(нет)";

        public static string FormatDistinctNames(IEnumerable<string> names)
        {
            // Keep two enumeration passes on the non-empty path. The WPF adapter
            // supplies deferred Navisworks name access, and this is legacy parity.
            var preview = names
                .Where(IsUsableName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            if (preview.Count == 0)
                return "?";

            var suffix = names
                .Where(IsUsableName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > preview.Count
                    ? ", ..."
                    : string.Empty;

            return string.Join(", ", preview) + suffix;
        }

        public static int CountDistinctNames(IEnumerable<string> names)
        {
            return names
                .Where(IsUsableName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        private static bool IsUsableName(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && name != MissingItemName;
        }
    }
}
