using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public static class FindItemsConditionHandles
    {
        public const int MaxConditions = 1000;

        public static string Describe(bool handlePerCondition, int searchCount, string combineOperator,
            int conditionCount, bool countOnly, string scope)
        {
            if (!handlePerCondition) return null;
            if (searchCount != 1) return "handlePerCondition needs exactly one search in searches.";
            if (!string.Equals(combineOperator, FindItemsCombineOperators.Any, StringComparison.OrdinalIgnoreCase))
                return "handlePerCondition needs combineOperator any.";
            if (conditionCount < 2 || conditionCount > MaxConditions)
                return "handlePerCondition needs 2 to 1000 conditions.";
            if (countOnly) return "handlePerCondition cannot be combined with countOnly.";
            if (!string.IsNullOrEmpty(scope) && !string.Equals(scope, FindItemsScopes.WholeModel, StringComparison.OrdinalIgnoreCase))
                return "handlePerCondition works only with scope whole_model.";
            return null;
        }

        public static List<List<int>> Group(int conditionCount, IList<IList<int>> matchingConditionsPerItem,
            out int unassignedItemCount)
        {
            if (conditionCount < 0) throw new ArgumentOutOfRangeException(nameof(conditionCount));
            if (matchingConditionsPerItem == null) throw new ArgumentNullException(nameof(matchingConditionsPerItem));
            var groups = new List<List<int>>(conditionCount);
            for (var i = 0; i < conditionCount; i++) groups.Add(new List<int>());
            unassignedItemCount = 0;
            for (var item = 0; item < matchingConditionsPerItem.Count; item++)
            {
                var assigned = new HashSet<int>(matchingConditionsPerItem[item] ?? Array.Empty<int>());
                if (assigned.Count == 0) unassignedItemCount++;
                foreach (var condition in assigned)
                {
                    if (condition < 0 || condition >= conditionCount)
                        throw new ArgumentOutOfRangeException(nameof(matchingConditionsPerItem));
                    groups[condition].Add(item);
                }
            }
            return groups;
        }
    }

    public static class MatchHandleLimits
    {
        public const int MaxHandles = 2000;
        public const int MaxItems = 1000000;
    }
}
