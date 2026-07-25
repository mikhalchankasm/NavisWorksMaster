using System;
using System.Globalization;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashTestNamePrefixHelper
    {
        public const string DefaultPairTestPrefix = "NH-BBOX";
        public const string MatrixGeneratedPrefix = "[NH-MATRIX]";
        public const string TimestampToken = "yyyyMMdd_HHmmss";

        public static string NormalizePairTestPrefix(string prefix)
        {
            var value = string.IsNullOrWhiteSpace(prefix) ? DefaultPairTestPrefix : prefix.Trim();
            return ClashRenumberNameHelper.SanitizeNamePart(value, 40);
        }

        public static string NormalizeMatrixPrefix(string prefix, bool useGeneratedPrefix, DateTime timestamp)
        {
            if (string.IsNullOrWhiteSpace(prefix) && !useGeneratedPrefix)
                return string.Empty;

            var value = string.IsNullOrWhiteSpace(prefix)
                ? MatrixGeneratedPrefix + " " + TimestampToken + " "
                : prefix.Trim();

            value = value.Replace(TimestampToken, timestamp.ToString(TimestampToken, CultureInfo.InvariantCulture));
            value = value.Replace("\r", " ").Replace("\n", " ");
            while (value.Contains("  "))
                value = value.Replace("  ", " ");
            if (!value.EndsWith(" ", StringComparison.Ordinal))
                value += " ";
            return value;
        }
    }
}
