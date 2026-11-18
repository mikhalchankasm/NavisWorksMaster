using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public static class SearchSetConditionGroups
    {
        public const int MaxBranches = 64;

        public static List<FindItemsCondition> Expand(IList<FindItemsCondition> all,
            IList<List<FindItemsCondition>> groups, out string error)
        {
            error = null;
            if (groups == null || groups.Count == 0)
                return all == null ? new List<FindItemsCondition>() : new List<FindItemsCondition>(all);

            error = Validate(all);
            if (error != null)
                return null;

            var branchCount = 1;
            foreach (var group in groups)
            {
                if (group == null || group.Count == 0)
                {
                    error = "conditionGroups must not contain an empty group.";
                    return null;
                }
                error = Validate(group);
                if (error != null)
                    return null;
                if (group.Count > MaxBranches / branchCount)
                {
                    error = "conditionGroups must expand to at most 64 branches.";
                    return null;
                }
                branchCount *= group.Count;
            }

            var result = new List<FindItemsCondition>();
            for (var branch = 0; branch < branchCount; branch++)
            {
                var stride = branchCount;
                for (var index = 0; index < groups.Count; index++)
                {
                    var group = groups[index];
                    stride /= group.Count;
                    result.Add(Copy(group[(branch / stride) % group.Count],
                        branch > 0 && index == 0 ? "or" : "and"));
                }
                if (all != null)
                    foreach (var condition in all)
                        result.Add(Copy(condition, "and"));
            }
            return result;
        }

        private static string Validate(IList<FindItemsCondition> conditions)
        {
            if (conditions == null)
                return null;
            foreach (var condition in conditions)
            {
                if (condition == null)
                    return "Search conditions must not contain a null member.";
                if (!string.IsNullOrWhiteSpace(condition.LogicalOperator) &&
                    !string.Equals(condition.LogicalOperator.Trim(), "and", StringComparison.OrdinalIgnoreCase))
                    return "logicalOperator must be and when conditionGroups are supplied.";
            }
            return null;
        }

        private static FindItemsCondition Copy(FindItemsCondition condition, string logicalOperator)
        {
            return new FindItemsCondition
            {
                Category = condition.Category,
                Property = condition.Property,
                CategoryInternal = condition.CategoryInternal,
                PropertyInternal = condition.PropertyInternal,
                DataType = condition.DataType,
                InheritFromAncestor = condition.InheritFromAncestor,
                Operator = condition.Operator,
                Comparison = condition.Comparison,
                Value = condition.Value,
                LogicalOperator = logicalOperator,
                Negate = condition.Negate,
                IgnoreCase = condition.IgnoreCase,
                IgnoreDiacritics = condition.IgnoreDiacritics,
                IgnoreCharWidth = condition.IgnoreCharWidth,
            };
        }
    }
}
