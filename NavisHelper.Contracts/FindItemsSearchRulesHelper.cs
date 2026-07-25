using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Pure rules shared by find_items request normalization and tests.
    /// </summary>
    public static class FindItemsSearchRulesHelper
    {
        public static string NormalizeComparison(string comparison)
        {
            var normalized = NormalizeComparableText(comparison);
            if (string.IsNullOrEmpty(normalized))
                return FindItemsComparisons.Contains;

            switch (normalized)
            {
                case "equals":
                    return FindItemsComparisons.Equal;
                case "not_equals":
                    return FindItemsComparisons.NotEquals;
                case "contains":
                    return FindItemsComparisons.Contains;
                case "starts_with":
                case "startswith":
                    return FindItemsComparisons.StartsWith;
                case "ends_with":
                case "endswith":
                    return FindItemsComparisons.EndsWith;
                case "wildcard":
                    return FindItemsComparisons.Wildcard;
                case "defined":
                    return FindItemsComparisons.Defined;
                case "not_defined":
                    return FindItemsComparisons.NotDefined;
                default:
                    throw new ArgumentException("Unsupported find_items comparison: " + comparison);
            }
        }

        public static string NormalizeCombineOperator(string combineOperator)
        {
            var normalized = NormalizeComparableText(combineOperator);
            if (string.IsNullOrEmpty(normalized) || normalized == "all" || normalized == "and")
                return FindItemsCombineOperators.All;

            if (normalized == "any" || normalized == "or")
                return FindItemsCombineOperators.Any;

            throw new ArgumentException("Unsupported find_items combine_operator: " + combineOperator);
        }

        public static string NormalizeDataType(string dataType)
        {
            var normalized = NormalizeComparableText(dataType);
            if (string.IsNullOrEmpty(normalized))
                return null;

            switch (normalized)
            {
                case "int":
                case "int32":
                case "integer":
                case "system.int32":
                    return "int32";
                case "double":
                case "float":
                case "number":
                case "system.double":
                    return "double";
                case "bool":
                case "boolean":
                case "system.boolean":
                    return "bool";
                case "datetime":
                case "date_time":
                case "date":
                case "system.datetime":
                    return "datetime";
                case "wstring":
                case "string":
                case "displaystring":
                    return "wstring";
                default:
                    return normalized;
            }
        }

        public static string NormalizeComparableText(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }

        public static string NormalizeConditionString(string value, bool ignoreCharWidth, bool ignoreDiacritics)
        {
            var normalized = value ?? string.Empty;
            if (ignoreCharWidth)
                normalized = normalized.Normalize(NormalizationForm.FormKC);
            if (ignoreDiacritics)
            {
                var decomposed = normalized.Normalize(NormalizationForm.FormD);
                normalized = new string(decomposed
                    .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                    .ToArray())
                    .Normalize(NormalizationForm.FormC);
            }

            return normalized;
        }

        public static int CountSearchLiteralCharacters(string comparison, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            var text = value;
            if (string.Equals(comparison, FindItemsComparisons.Wildcard, StringComparison.OrdinalIgnoreCase))
                text = text.Replace("*", string.Empty).Replace("?", string.Empty);

            return text.Count(char.IsLetterOrDigit);
        }

        public static bool IsPositiveComparison(string comparison)
        {
            return string.Equals(comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(comparison, FindItemsComparisons.Contains, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(comparison, FindItemsComparisons.StartsWith, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(comparison, FindItemsComparisons.EndsWith, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(comparison, FindItemsComparisons.Wildcard, StringComparison.OrdinalIgnoreCase);
        }

        public static bool MatchesAnchoredText(
            string value,
            string expected,
            string comparison,
            bool ignoreCase,
            bool ignoreCharWidth,
            bool ignoreDiacritics)
        {
            var normalizedValue = NormalizeConditionString(value, ignoreCharWidth, ignoreDiacritics);
            var normalizedExpected = NormalizeConditionString(expected, ignoreCharWidth, ignoreDiacritics);
            var stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            if (string.Equals(comparison, FindItemsComparisons.StartsWith, StringComparison.OrdinalIgnoreCase))
                return normalizedValue.StartsWith(normalizedExpected, stringComparison);
            if (string.Equals(comparison, FindItemsComparisons.EndsWith, StringComparison.OrdinalIgnoreCase))
                return normalizedValue.EndsWith(normalizedExpected, stringComparison);

            throw new ArgumentException("Unsupported anchored find_items comparison: " + comparison);
        }
    }
}
