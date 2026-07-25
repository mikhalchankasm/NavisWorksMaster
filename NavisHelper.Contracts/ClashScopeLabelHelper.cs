using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashScopeLabelHelper
    {
        public static string FormatRequestedTestNames(
            string testName,
            IEnumerable<string> testNames,
            IEnumerable<string> testHandles,
            string namePrefix = null,
            int? firstN = null)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(testName))
                parts.Add(testName.Trim());
            if (testNames != null)
                parts.AddRange(testNames.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()));
            if (testHandles != null)
                parts.AddRange(testHandles.Where(handle => !string.IsNullOrWhiteSpace(handle)).Select(handle => handle.Trim()));
            if (!string.IsNullOrWhiteSpace(namePrefix))
                parts.Add("prefix:" + namePrefix.Trim());
            if (firstN.HasValue)
                parts.Add("firstN:" + firstN.Value.ToString(CultureInfo.InvariantCulture));

            return string.Join(", ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
        }
    }
}
