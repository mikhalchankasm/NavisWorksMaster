using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    public static class HeightMarkGroupNameHelper
    {
        public static string Suggest(IEnumerable<string> itemNames)
        {
            var abbreviations = (itemNames ?? Enumerable.Empty<string>())
                .Select(Abbreviate)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            if (abbreviations.Count == 0)
                return "Группа высот";

            return string.Join(" + ", abbreviations);
        }

        private static string Abbreviate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Trim();
            var words = normalized
                .Split(new[] { ' ', '\t', '/', '\\', '-', '_', '.', ',', ';', ':' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

            if (words.Count > 1)
            {
                var initials = new StringBuilder();
                foreach (var word in words)
                {
                    if (word.Length > 0 && char.IsLetterOrDigit(word[0]))
                        initials.Append(char.ToUpperInvariant(word[0]));
                    if (initials.Length >= 8)
                        break;
                }

                if (initials.Length > 0)
                    return initials.ToString();
            }

            const int maximumLength = 12;
            return normalized.Length <= maximumLength
                ? normalized
                : normalized.Substring(0, maximumLength);
        }
    }
}
