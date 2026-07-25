using System;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    public enum ClashVirtualGroupSide
    {
        None,
        ItemA,
        ItemB,
    }

    public static class ClashVirtualGroupIdentityHelper
    {
        private const int MaxSavedItemNameLength = 120;

        public const string SideTagA = " [NH:A]";
        public const string SideTagB = " [NH:B]";

        public static string BuildPersistentName(ClashVirtualGroupSide side, string label)
        {
            var tag = side == ClashVirtualGroupSide.ItemA
                ? SideTagA
                : side == ClashVirtualGroupSide.ItemB ? SideTagB : string.Empty;
            var fallback = side == ClashVirtualGroupSide.ItemA
                ? "Group A"
                : side == ClashVirtualGroupSide.ItemB ? "Group B" : "Group";
            var value = GetUserName(label);
            if (string.IsNullOrWhiteSpace(value))
                value = fallback;

            var baseNameLimit = MaxSavedItemNameLength - tag.Length;
            value = NormalizeSavedItemName(value, fallback, baseNameLimit);
            return value + tag;
        }

        public static ClashVirtualGroupSide InferSide(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                return ClashVirtualGroupSide.None;

            var value = groupName.Trim();
            if (value.StartsWith("Clash-A", StringComparison.OrdinalIgnoreCase))
                return ClashVirtualGroupSide.ItemA;
            if (value.StartsWith("Clash-B", StringComparison.OrdinalIgnoreCase))
                return ClashVirtualGroupSide.ItemB;
            if (value.EndsWith(SideTagA, StringComparison.OrdinalIgnoreCase))
                return ClashVirtualGroupSide.ItemA;
            if (value.EndsWith(SideTagB, StringComparison.OrdinalIgnoreCase))
                return ClashVirtualGroupSide.ItemB;

            return ClashVirtualGroupSide.None;
        }

        public static string GetUserName(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                return string.Empty;

            var value = groupName.Trim();
            if (value.StartsWith("Clash-A:", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("Clash-A:".Length).Trim();
            else if (value.StartsWith("Clash-B:", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("Clash-B:".Length).Trim();
            else if (value.StartsWith("Clash-A", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("Clash-A".Length).TrimStart(':', ' ', '-').Trim();
            else if (value.StartsWith("Clash-B", StringComparison.OrdinalIgnoreCase))
                value = value.Substring("Clash-B".Length).TrimStart(':', ' ', '-').Trim();

            if (value.EndsWith(SideTagA, StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - SideTagA.Length).Trim();
            else if (value.EndsWith(SideTagB, StringComparison.OrdinalIgnoreCase))
                value = value.Substring(0, value.Length - SideTagB.Length).Trim();

            return value;
        }

        public static bool AreSame(
            ClashVirtualGroupSide leftSide,
            string leftPath,
            string leftLabel,
            ClashVirtualGroupSide rightSide,
            string rightPath,
            string rightLabel)
        {
            return leftSide == rightSide &&
                string.Equals(leftPath ?? string.Empty, rightPath ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(GetUserName(leftLabel), GetUserName(rightLabel), StringComparison.OrdinalIgnoreCase);
        }

        public static string BuildTestCacheKey(Guid testGuid, string displayName)
        {
            if (testGuid != Guid.Empty)
                return "guid:" + testGuid.ToString("D");

            return string.IsNullOrWhiteSpace(displayName)
                ? string.Empty
                : "name:" + displayName.Trim();
        }

        private static string NormalizeSavedItemName(string value, string fallback, int maxLength)
        {
            var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            var builder = new StringBuilder(text.Length);
            foreach (var ch in text)
            {
                if (!char.IsControl(ch))
                    builder.Append(ch);
            }

            var name = builder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = fallback;

            if (name.Length <= maxLength)
                return name;

            var safeLength = maxLength;
            if (safeLength > 0 &&
                safeLength < name.Length &&
                char.IsHighSurrogate(name[safeLength - 1]) &&
                char.IsLowSurrogate(name[safeLength]))
            {
                safeLength--;
            }

            return name.Substring(0, safeLength).Trim();
        }
    }
}
