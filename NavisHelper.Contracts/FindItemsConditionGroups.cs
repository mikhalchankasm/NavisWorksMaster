using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public static class FindItemsConditionGroups
    {
        public static List<List<int>> Split(IList<FindItemsCondition> conditions)
        {
            var groups = new List<List<int>>();
            if (conditions == null || conditions.Count == 0)
                return groups;

            var current = new List<int> { 0 };
            for (var index = 1; index < conditions.Count; index++)
            {
                if (IsOr(conditions[index]))
                {
                    groups.Add(current);
                    current = new List<int> { index };
                }
                else
                {
                    current.Add(index);
                }
            }

            groups.Add(current);
            return groups;
        }

        private static bool IsOr(FindItemsCondition condition)
        {
            return condition != null && string.Equals(
                condition.LogicalOperator,
                FindItemsConditionOptionsHelper.Or,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
