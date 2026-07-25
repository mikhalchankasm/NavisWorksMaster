using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NavisHelper.Agent.Contracts
{
    public static class PairNameTemplateFormatter
    {
        private const int MaxTemplateLength = 1000;
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
        private static readonly Regex TokenRegex = new Regex(@"\{([^{}]+)\}", RegexOptions.CultureInvariant, RegexTimeout);

        public static string Format(string template, int index, string aName, string bName)
        {
            if (string.IsNullOrWhiteSpace(template))
                throw new ArgumentException("pairNameTemplate must not be empty.", nameof(template));
            if (template.Length > MaxTemplateLength)
                throw new ArgumentException("pairNameTemplate cannot exceed 1000 characters.", nameof(template));

            try
            {
                return TokenRegex.Replace(template, match =>
                {
                    var parts = match.Groups[1].Value.Split('|');
                    var value = ResolveToken(parts[0], index, aName, bName);
                    for (var i = 1; i < parts.Length; i++)
                        value = ApplyTransform(value, parts[i]);
                    return value;
                });
            }
            catch (RegexMatchTimeoutException ex)
            {
                throw new ArgumentException("A pair-name regex transform exceeded the 250 ms safety timeout.", nameof(template), ex);
            }
        }

        private static string ResolveToken(string token, int index, string aName, string bName)
        {
            switch ((token ?? string.Empty).Trim())
            {
                case "index":
                    return index.ToString(CultureInfo.InvariantCulture);
                case "aName":
                    return aName ?? string.Empty;
                case "bName":
                    return bName ?? string.Empty;
                case "aCode":
                    return ExtractCode(aName);
                case "bCode":
                    return ExtractCode(bName);
                default:
                    throw new ArgumentException("Unsupported pair-name token: " + token);
            }
        }

        private static string ApplyTransform(string value, string transform)
        {
            transform = (transform ?? string.Empty).Trim();
            if (string.Equals(transform, "upper", StringComparison.OrdinalIgnoreCase))
                return value.ToUpperInvariant();
            if (string.Equals(transform, "lower", StringComparison.OrdinalIgnoreCase))
                return value.ToLowerInvariant();
            if (transform.StartsWith("zeroPad:", StringComparison.OrdinalIgnoreCase))
            {
                int width;
                if (!int.TryParse(transform.Substring("zeroPad:".Length), NumberStyles.None, CultureInfo.InvariantCulture, out width) ||
                    width < 1 || width > 12)
                {
                    throw new ArgumentException("zeroPad width must be between 1 and 12.");
                }
                long number;
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
                    throw new ArgumentException("zeroPad can be applied only to an integer token.");
                return number.ToString(new string('0', width), CultureInfo.InvariantCulture);
            }
            if (transform.StartsWith("strip:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = ParseDelimited(transform.Substring("strip:".Length), 1);
                return Regex.Replace(value, parts[0], string.Empty, RegexOptions.CultureInvariant, RegexTimeout);
            }
            if (transform.StartsWith("replace:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = ParseDelimited(transform.Substring("replace:".Length), 2);
                return Regex.Replace(value, parts[0], parts[1], RegexOptions.CultureInvariant, RegexTimeout);
            }

            throw new ArgumentException("Unsupported pair-name transform: " + transform);
        }

        private static string[] ParseDelimited(string expression, int valueCount)
        {
            if (string.IsNullOrEmpty(expression))
                throw new ArgumentException("Regex transform requires a delimiter and expression.");
            var delimiter = expression[0];
            var result = new string[valueCount];
            var start = 1;
            for (var i = 0; i < valueCount; i++)
            {
                var end = expression.IndexOf(delimiter, start);
                if (end < 0)
                    throw new ArgumentException("Regex transform is missing a closing delimiter.");
                result[i] = expression.Substring(start, end - start);
                start = end + 1;
            }
            if (start != expression.Length)
                throw new ArgumentException("Regex transform contains unexpected trailing characters.");
            return result;
        }

        private static string ExtractCode(string value)
        {
            var input = (value ?? string.Empty).Trim();
            var match = Regex.Match(input, @"(?:^|[-_])([^/\\_-]+)$", RegexOptions.CultureInvariant, RegexTimeout);
            return match.Success ? match.Groups[1].Value : input;
        }
    }
}
