using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashGroupNameHelper
    {
        public const string SideTagA = " [NH:A]";
        public const string SideTagB = " [NH:B]";
        public const string GroupNameModeOwnerName = "owner_name";
        public const string GroupNameModeOwnerPath = "owner_path";

        public static string ExtractSideTag(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.TrimEnd();
            if (trimmed.EndsWith(SideTagA, StringComparison.OrdinalIgnoreCase))
                return SideTagA;
            if (trimmed.EndsWith(SideTagB, StringComparison.OrdinalIgnoreCase))
                return SideTagB;
            return string.Empty;
        }

        public static string RemoveSideTag(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.TrimEnd();
            var tag = ExtractSideTag(trimmed);
            if (string.IsNullOrEmpty(tag))
                return value.Trim();

            return trimmed.Substring(0, trimmed.Length - tag.Length).Trim();
        }

        public static string StripLeadingNumber(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return Regex.Replace(value.Trim(), @"^\s*(?:[\[\(]?\d+[\]\)]?)(?:\s*[-_.:]\s*|\s+)", string.Empty).Trim();
        }

        public static string BuildMatchKey(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                return string.Empty;

            var tag = ExtractSideTag(groupName);
            var cleanName = StripLeadingNumber(RemoveSideTag(groupName));
            return NormalizeKey(cleanName) + "|" + NormalizeKey(tag);
        }

        public static string FindMatchingGroupName(IEnumerable<string> existingGroupNames, string requestedGroupName)
        {
            if (existingGroupNames == null || string.IsNullOrWhiteSpace(requestedGroupName))
                return null;

            var names = existingGroupNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
            var exact = names.FirstOrDefault(name => string.Equals(name, requestedGroupName, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;

            var expectedKey = BuildMatchKey(requestedGroupName);
            if (string.IsNullOrWhiteSpace(expectedKey))
                return null;

            return names.FirstOrDefault(name => string.Equals(BuildMatchKey(name), expectedKey, StringComparison.OrdinalIgnoreCase));
        }

        public static bool MatchesPrefix(string groupName, string groupNamePrefix)
        {
            if (string.IsNullOrWhiteSpace(groupNamePrefix))
                return true;
            if (string.IsNullOrWhiteSpace(groupName))
                return false;

            var cleanName = StripLeadingNumber(RemoveSideTag(groupName));
            return cleanName.StartsWith(groupNamePrefix.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static string BuildGroupKey(string groupNameMode, string ownerName, string ownerPath)
        {
            return NormalizeKey(SelectGroupSource(groupNameMode, ownerName, ownerPath, string.Empty));
        }

        public static string BuildCleanName(string groupNameMode, string prefix, string ownerName, string ownerPath)
        {
            var source = SelectGroupSource(groupNameMode, ownerName, ownerPath, "Group");
            var name = ClashRenumberNameHelper.SanitizeNamePart(source, 180);
            if (!string.IsNullOrWhiteSpace(prefix))
                name = ClashRenumberNameHelper.SanitizeNamePart(prefix, 40) + " " + name;

            return ClashRenumberNameHelper.SanitizeNamePart(name, 220);
        }

        public static string BuildGroupName(string side, string cleanName, bool includeNavisHelperSideTag)
        {
            var value = ClashRenumberNameHelper.SanitizeNamePart(cleanName, 220);
            if (!includeNavisHelperSideTag)
                return value;

            return value + GetSideTag(side);
        }

        public static string GetSideTag(string side)
        {
            return string.Equals(side, "A", StringComparison.OrdinalIgnoreCase) ? SideTagA : SideTagB;
        }

        public static bool IsTaggedSideGroup(string groupName, string side)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                return false;

            return groupName.EndsWith(GetSideTag(side), StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldRemoveExistingGroup(string groupName, string side, string groupNamePrefix)
        {
            return IsTaggedSideGroup(groupName, side) && MatchesPrefix(groupName, groupNamePrefix);
        }

        public static HashSet<string> BuildMatchKeySet(IEnumerable<string> groupNames)
        {
            return new HashSet<string>(
                (groupNames ?? Enumerable.Empty<string>())
                    .Select(BuildMatchKey)
                    .Where(key => !string.IsNullOrWhiteSpace(key)),
                StringComparer.OrdinalIgnoreCase);
        }

        public static bool ShouldRemoveEmptyGroup(string groupName, string side, ISet<string> plannedMatchKeys)
        {
            if (!IsTaggedSideGroup(groupName, side))
                return false;

            var key = BuildMatchKey(groupName);
            if (string.IsNullOrWhiteSpace(key))
                return false;

            return plannedMatchKeys == null || !plannedMatchKeys.Contains(key);
        }

        public static string NormalizeKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return string.Join(" ", value.Trim().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        }

        private static string SelectGroupSource(string groupNameMode, string ownerName, string ownerPath, string fallback)
        {
            var source = string.Equals(groupNameMode, GroupNameModeOwnerPath, StringComparison.OrdinalIgnoreCase)
                ? ownerPath
                : ownerName;
            if (string.IsNullOrWhiteSpace(source))
                source = ownerPath;
            if (string.IsNullOrWhiteSpace(source))
                source = fallback;

            return source ?? string.Empty;
        }
    }
}
