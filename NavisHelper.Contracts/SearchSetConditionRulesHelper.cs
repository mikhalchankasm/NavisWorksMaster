using System;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Pure normalization and target-classification rules for persisted Navisworks Search Sets.
    /// </summary>
    public static class SearchSetConditionRulesHelper
    {
        public const string ItemInternalCategory = "LcOaNode";
        public const string ItemNameInternalProperty = "LcOaSceneBaseUserName";

        public static FindItemsCondition NormalizeCondition(FindItemsCondition condition)
        {
            if (condition == null)
                throw new ArgumentException("Search condition cannot be null.");

            var comparison = NormalizeComparison(GetConditionComparison(condition));
            var requiresValue = !string.Equals(comparison, FindItemsComparisons.Defined, StringComparison.OrdinalIgnoreCase);
            var value = condition.Value == null ? null : condition.Value.Trim();
            if (requiresValue && string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("The operator \"" + comparison + "\" requires a non-empty value.");

            var normalized = new FindItemsCondition
            {
                Category = TrimOrNull(condition.Category),
                Property = TrimOrNull(condition.Property),
                CategoryInternal = TrimOrNull(condition.CategoryInternal),
                PropertyInternal = TrimOrNull(condition.PropertyInternal),
                DataType = FindItemsSearchRulesHelper.NormalizeDataType(condition.DataType),
                InheritFromAncestor = condition.InheritFromAncestor,
                Operator = comparison,
                Comparison = comparison,
                Value = requiresValue ? value : null,
                LogicalOperator = FindItemsConditionOptionsHelper.NormalizeLogicalOperator(condition.LogicalOperator),
                Negate = condition.Negate.GetValueOrDefault(false),
                IgnoreCase = condition.IgnoreCase.GetValueOrDefault(true),
                IgnoreDiacritics = condition.IgnoreDiacritics.GetValueOrDefault(false),
                IgnoreCharWidth = condition.IgnoreCharWidth.GetValueOrDefault(false),
            };

            if (string.IsNullOrWhiteSpace(normalized.Property) && string.IsNullOrWhiteSpace(normalized.PropertyInternal))
            {
                normalized.Category = "Item";
                normalized.Property = "Name";
            }

            if (IsDefaultItemNameTarget(normalized))
            {
                normalized.CategoryInternal = ItemInternalCategory;
                normalized.PropertyInternal = ItemNameInternalProperty;
            }

            return normalized;
        }

        public static string NormalizeComparison(string comparison)
        {
            var value = FindItemsSearchRulesHelper.NormalizeComparableText(comparison);
            if (string.IsNullOrWhiteSpace(value))
                return FindItemsComparisons.Contains;
            if (value == "equal")
                return FindItemsComparisons.Equal;
            if (value == "contains" || value == "contain")
                return FindItemsComparisons.Contains;
            if (value == "wildcard" || value == "wildcards")
                return FindItemsComparisons.Wildcard;
            if (value == "defined" || value == "exists")
                return FindItemsComparisons.Defined;
            if (value == FindItemsComparisons.NotEquals || value == FindItemsComparisons.NotDefined)
                throw new ArgumentException("Search sets cannot persist negative/manual-only operators: " + value);
            if (value == FindItemsComparisons.Equal || value == FindItemsComparisons.Contains || value == FindItemsComparisons.Wildcard || value == FindItemsComparisons.Defined)
                return value;

            throw new ArgumentException("Unsupported search set comparison: " + comparison);
        }

        public static string GetConditionComparison(FindItemsCondition condition)
        {
            if (condition == null)
                return null;

            return string.IsNullOrWhiteSpace(condition.Operator)
                ? condition.Comparison
                : condition.Operator;
        }

        public static string NormalizeCombineOperator(string combineOperator)
        {
            var value = FindItemsSearchRulesHelper.NormalizeComparableText(combineOperator);
            if (string.IsNullOrWhiteSpace(value))
                return FindItemsCombineOperators.All;
            if (value == FindItemsCombineOperators.All || value == "and")
                return FindItemsCombineOperators.All;
            if (value == FindItemsCombineOperators.Any || value == "or")
                return FindItemsCombineOperators.Any;

            throw new ArgumentException("Unsupported search set combine_operator: " + combineOperator);
        }

        public static bool IsDefaultItemNameTarget(FindItemsCondition condition)
        {
            if (condition == null)
                return false;

            var internalCategory = FindItemsSearchRulesHelper.NormalizeComparableText(condition.CategoryInternal);
            var internalProperty = FindItemsSearchRulesHelper.NormalizeComparableText(condition.PropertyInternal);
            if (!string.IsNullOrEmpty(internalProperty))
            {
                if (!string.IsNullOrEmpty(internalCategory) &&
                    internalCategory != FindItemsSearchRulesHelper.NormalizeComparableText(ItemInternalCategory))
                {
                    return false;
                }

                return internalProperty == FindItemsSearchRulesHelper.NormalizeComparableText(ItemNameInternalProperty) ||
                       internalProperty == "name";
            }

            var category = FindItemsSearchRulesHelper.NormalizeComparableText(condition.Category);
            var property = FindItemsSearchRulesHelper.NormalizeComparableText(condition.Property);
            if (property != "name" && property != "имя")
                return false;

            return string.IsNullOrEmpty(category) ||
                   category == "item" ||
                   category == "элемент";
        }

        public static bool UsesInternalPropertyOnly(FindItemsCondition condition)
        {
            return condition != null &&
                !IsDefaultItemNameTarget(condition) &&
                string.IsNullOrWhiteSpace(condition.Category) &&
                string.IsNullOrWhiteSpace(condition.Property) &&
                !string.IsNullOrWhiteSpace(condition.CategoryInternal) &&
                !string.IsNullOrWhiteSpace(condition.PropertyInternal);
        }

        public static bool HasDisplayAndInternalPropertyNames(FindItemsCondition condition)
        {
            return condition != null &&
                !IsDefaultItemNameTarget(condition) &&
                !string.IsNullOrWhiteSpace(condition.Category) &&
                !string.IsNullOrWhiteSpace(condition.Property) &&
                !string.IsNullOrWhiteSpace(condition.CategoryInternal) &&
                !string.IsNullOrWhiteSpace(condition.PropertyInternal);
        }

        private static string TrimOrNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
