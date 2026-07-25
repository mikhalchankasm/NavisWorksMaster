using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashRenumberNameHelper
    {
        public static string FormatNumber(int number, int width)
        {
            return number.ToString("D" + Math.Max(1, width).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        public static string BuildName(
            string oldName,
            string numberText,
            string prefix,
            string suffix,
            string separator,
            bool preserveExistingName,
            bool stripExistingNumber)
        {
            var sideTag = ClashGroupNameHelper.ExtractSideTag(oldName);
            var clean = ClashGroupNameHelper.RemoveSideTag(oldName);
            if (stripExistingNumber)
                clean = ClashGroupNameHelper.StripLeadingNumber(clean);

            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(prefix))
                builder.Append(prefix);
            builder.Append(numberText ?? string.Empty);
            if (!string.IsNullOrEmpty(suffix))
                builder.Append(suffix);

            if (preserveExistingName && !string.IsNullOrWhiteSpace(clean))
            {
                builder.Append(separator ?? " - ");
                builder.Append(clean.Trim());
            }

            var value = SanitizeNamePart(builder.ToString(), 220);
            if (!string.IsNullOrEmpty(sideTag))
                value = SanitizeNamePart(value, 220 - sideTag.Length) + sideTag;
            return value;
        }

        public static string SanitizeNamePart(string value, int maxLength)
        {
            value = string.IsNullOrWhiteSpace(value) ? "item" : value.Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
                value = value.Replace(c, '_');
            value = value.Replace("\r", " ").Replace("\n", " ");
            while (value.Contains("  "))
                value = value.Replace("  ", " ");
            if (value.Length > maxLength)
                value = value.Substring(0, maxLength).Trim();
            return string.IsNullOrWhiteSpace(value) ? "item" : value;
        }
    }
}
