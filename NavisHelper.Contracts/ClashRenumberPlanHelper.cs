using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashRenumberPlanSource
    {
        public int TestIndex { get; set; }
        public string TestHandle { get; set; }
        public string TestName { get; set; }
        public string ItemType { get; set; }
        public string GroupPath { get; set; }
        public string OldName { get; set; }
    }

    public sealed class ClashRenumberPlanBuildResult
    {
        public List<ClashRenumberPlanItem> Items { get; set; } = new List<ClashRenumberPlanItem>();
        public int PlannedRenameCount { get; set; }
        public int SkippedCount { get; set; }
    }

    public static class ClashRenumberPlanHelper
    {
        public const int DefaultLimit = 5000;
        public const int MaxLimit = 50000;
        public const int MaxNumberWidth = 12;
        public const string ScopeTopLevel = "top_level";
        public const string ScopeRecursive = "recursive";
        public const string OrderCurrent = "current";
        public const string OrderName = "name";
        public const string StatusPlanned = "planned";
        public const string StatusUnchanged = "unchanged";

        public static int ClampLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultLimit);
            if (value < 1)
                return 1;
            if (value > MaxLimit)
                return MaxLimit;
            return value;
        }

        public static bool TryNormalizeStartNumber(int? value, out int startNumber)
        {
            startNumber = value.GetValueOrDefault(1);
            return startNumber >= 0;
        }

        public static int NormalizeNumberWidth(int? value)
        {
            var result = value.GetValueOrDefault(4);
            if (result < 1)
                return 1;
            if (result > MaxNumberWidth)
                return MaxNumberWidth;
            return result;
        }

        public static string NormalizeScope(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ScopeTopLevel;

            var normalized = NormalizeToken(value);
            switch (normalized)
            {
                case ScopeTopLevel:
                case "top":
                case "visible":
                case "standard_form":
                    return ScopeTopLevel;
                case ScopeRecursive:
                case "all":
                case "tree":
                    return ScopeRecursive;
                default:
                    return null;
            }
        }

        public static string NormalizeOrder(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return OrderCurrent;

            var normalized = NormalizeToken(value);
            switch (normalized)
            {
                case OrderCurrent:
                case "tree":
                case "standard_form":
                    return OrderCurrent;
                case OrderName:
                case "natural_name":
                case "display_name":
                    return OrderName;
                default:
                    return null;
            }
        }

        public static ClashRenumberPlanBuildResult BuildPlan(
            IEnumerable<ClashRenumberPlanSource> sources,
            int startNumber,
            int numberWidth,
            string prefix,
            string suffix,
            string separator,
            bool preserveExistingName,
            bool stripExistingNumber)
        {
            var result = new ClashRenumberPlanBuildResult();
            var number = startNumber;
            var index = 0;

            foreach (var source in sources ?? Enumerable.Empty<ClashRenumberPlanSource>())
            {
                index++;
                var oldName = source == null ? string.Empty : source.OldName ?? string.Empty;
                var numberText = ClashRenumberNameHelper.FormatNumber(number, numberWidth);
                var newName = ClashRenumberNameHelper.BuildName(oldName, numberText, prefix, suffix, separator, preserveExistingName, stripExistingNumber);
                var status = string.Equals(oldName, newName, StringComparison.Ordinal)
                    ? StatusUnchanged
                    : StatusPlanned;

                result.Items.Add(new ClashRenumberPlanItem
                {
                    Index = index,
                    Number = number,
                    NumberText = numberText,
                    TestIndex = source == null ? 0 : source.TestIndex,
                    TestHandle = source == null ? string.Empty : source.TestHandle ?? string.Empty,
                    TestName = source == null ? string.Empty : source.TestName ?? string.Empty,
                    ItemType = source == null ? string.Empty : source.ItemType ?? string.Empty,
                    GroupPath = source == null ? string.Empty : source.GroupPath ?? string.Empty,
                    OldName = oldName,
                    NewName = newName,
                    Status = status,
                });

                number++;
            }

            result.PlannedRenameCount = result.Items.Count(item => !string.Equals(item.Status, StatusUnchanged, StringComparison.OrdinalIgnoreCase));
            result.SkippedCount = result.Items.Count - result.PlannedRenameCount;
            return result;
        }

        private static string NormalizeToken(string value)
        {
            return value.Trim().ToLowerInvariant().Replace("-", "_").Replace(" ", "_");
        }
    }
}
