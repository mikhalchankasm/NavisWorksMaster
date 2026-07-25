using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashReportHtmlFormatHelper
    {
        public static string FormatStatusCounts(IDictionary<string, int> counts)
        {
            if (counts == null || counts.Count == 0)
                return string.Empty;

            return string.Join(", ", counts
                .OrderBy(pair => GetStatusSortOrder(pair.Key))
                .ThenBy(pair => pair.Key)
                .Select(pair => pair.Key + ": " + pair.Value.ToString(CultureInfo.InvariantCulture)));
        }

        public static int GetStatusSortOrder(string status)
        {
            if (string.Equals(status, "New", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (string.Equals(status, "Active", StringComparison.OrdinalIgnoreCase))
                return 1;
            if (string.Equals(status, "Reviewed", StringComparison.OrdinalIgnoreCase))
                return 2;
            if (string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase))
                return 3;
            if (string.Equals(status, "Resolved", StringComparison.OrdinalIgnoreCase))
                return 4;
            return 100;
        }

        public static string HtmlEncode(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        public static string HtmlAttributeEncode(string value)
        {
            return HtmlEncode((value ?? string.Empty).Replace("\\", "/"));
        }
    }
}
