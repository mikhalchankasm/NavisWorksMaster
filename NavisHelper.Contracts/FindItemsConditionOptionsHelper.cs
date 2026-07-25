using System;

namespace NavisHelper.Agent.Contracts
{
    public static class FindItemsConditionOptionsHelper
    {
        public const string And = "and";
        public const string Or = "or";

        public static string NormalizeLogicalOperator(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? And : value.Trim().ToLowerInvariant();
            if (normalized != And && normalized != Or)
                throw new ArgumentException("logicalOperator must be and or or.", nameof(value));
            return normalized;
        }
    }
}
