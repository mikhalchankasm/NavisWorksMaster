using System;
using System.Globalization;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashHandleHelper
    {
        public const string TestPrefix = "clash-test:";
        public const string ResultPrefix = "clash-result:";
        public const string GroupPrefix = "clash-group:";

        public static string BuildTestHandle(int testIndex)
        {
            if (testIndex < 1)
                return string.Empty;

            return TestPrefix + testIndex.ToString(CultureInfo.InvariantCulture);
        }

        public static string BuildResultHandle(int testIndex, int resultIndex)
        {
            if (testIndex < 1 || resultIndex < 1)
                return string.Empty;

            return ResultPrefix
                + testIndex.ToString(CultureInfo.InvariantCulture)
                + ":"
                + resultIndex.ToString(CultureInfo.InvariantCulture);
        }

        public static string BuildGroupHandle(int testIndex, int groupIndex)
        {
            if (testIndex < 1 || groupIndex < 1)
                return string.Empty;

            return GroupPrefix
                + testIndex.ToString(CultureInfo.InvariantCulture)
                + ":"
                + groupIndex.ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryParseGroupHandle(string handle, out int testIndex, out int groupIndex)
        {
            testIndex = 0;
            groupIndex = 0;
            var parts = (handle ?? string.Empty).Trim().Split(':');
            return parts.Length == 3 &&
                   string.Equals(parts[0] + ":", GroupPrefix, StringComparison.OrdinalIgnoreCase) &&
                   int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out testIndex) &&
                   int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out groupIndex) &&
                   testIndex > 0 && groupIndex > 0;
        }

        public static bool TryParseTestHandle(string handle, out int testIndex)
        {
            testIndex = 0;
            if (string.IsNullOrWhiteSpace(handle))
                return false;

            var value = handle.Trim();
            if (value.StartsWith(TestPrefix, StringComparison.OrdinalIgnoreCase))
                value = value.Substring(TestPrefix.Length);

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out testIndex);
        }

        public static bool TryParseResultHandle(string handle, out int testIndex, out int resultIndex)
        {
            testIndex = 0;
            resultIndex = 0;
            if (string.IsNullOrWhiteSpace(handle))
                return false;

            var value = handle.Trim();
            if (!value.StartsWith(ResultPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            var parts = value.Substring(ResultPrefix.Length).Split(':');
            return parts.Length == 2 &&
                   int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out testIndex) &&
                   int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out resultIndex) &&
                   testIndex > 0 && resultIndex > 0;
        }
    }
}
