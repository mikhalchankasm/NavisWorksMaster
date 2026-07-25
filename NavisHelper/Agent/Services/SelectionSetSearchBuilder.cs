using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal static class SelectionSetSearchBuilder
    {
        private const int RuntimePropertyBindingTimeoutMilliseconds = 10000;

        internal static Search Build(
            Document document,
            IEnumerable<FindItemsCondition> conditions,
            ICollection<string> bindingWarnings,
            ref int runtimeResolvedConditionCount)
        {
            var search = new Search();
            search.Selection.SelectAll();
            search.Locations = SearchLocations.DescendantsAndSelf;

            var index = 0;
            foreach (var condition in conditions)
            {
                var searchCondition = BuildSearchCondition(document, condition, bindingWarnings, ref runtimeResolvedConditionCount);
                search.SearchConditions.Add(ApplyConditionOptions(searchCondition, condition, index));
                index++;
            }

            return search;
        }

        private static SearchCondition BuildSearchCondition(
            Document document,
            FindItemsCondition condition,
            ICollection<string> bindingWarnings,
            ref int runtimeResolvedConditionCount)
        {
            if (condition == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Search condition cannot be null.");

            var baseCondition = CreateBaseSearchCondition(document, condition, bindingWarnings, ref runtimeResolvedConditionCount);
            var comparison = NormalizeComparison(SearchSetConditionRulesHelper.GetConditionComparison(condition));
            if (string.Equals(comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase))
            {
                var variant = CreateVariantData(condition);
                return baseCondition.EqualValue(variant);
            }

            if (string.Equals(comparison, FindItemsComparisons.Contains, StringComparison.OrdinalIgnoreCase))
                return baseCondition.DisplayStringContains(condition.Value ?? string.Empty);

            if (string.Equals(comparison, FindItemsComparisons.Wildcard, StringComparison.OrdinalIgnoreCase))
                return baseCondition.DisplayStringWildcard(condition.Value ?? string.Empty);

            if (string.Equals(comparison, FindItemsComparisons.Defined, StringComparison.OrdinalIgnoreCase))
                return baseCondition;

            throw new AgentCommandException(ErrorCodes.SchemaViolation, "Unsupported search set comparison: " + comparison + ". Use equals, contains, wildcard, or defined.");
        }

        private static SearchCondition ApplyConditionOptions(SearchCondition searchCondition, FindItemsCondition condition, int index)
        {
            if (searchCondition == null || condition == null)
                return searchCondition;

            var comparison = NormalizeComparison(SearchSetConditionRulesHelper.GetConditionComparison(condition));
            var isStringValue = string.Equals(comparison, FindItemsComparisons.Contains, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(comparison, FindItemsComparisons.Wildcard, StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase) &&
                 ShouldIgnoreStringValueCase(CreateVariantData(condition)));
            if (isStringValue && condition.IgnoreCase.GetValueOrDefault(true))
                searchCondition = searchCondition.IgnoreStringValueCase();
            if (isStringValue && condition.IgnoreDiacritics.GetValueOrDefault(false))
                searchCondition = searchCondition.IgnoreStringValueAccents();
            if (isStringValue && condition.IgnoreCharWidth.GetValueOrDefault(false))
                searchCondition = searchCondition.IgnoreStringValueCharWidths();
            if (condition.Negate.GetValueOrDefault(false))
                searchCondition = searchCondition.Negate();

            // In the native saved-search format StartGroup is Navisworks' UI
            // flag for “OR with the preceding condition”. It is deliberately a
            // property of this condition, not a global set combine operator.
            if (index > 0 && string.Equals(condition.LogicalOperator, FindItemsConditionOptionsHelper.Or, StringComparison.OrdinalIgnoreCase))
            {
                var options = searchCondition.Options | SearchConditionOptions.StartGroup;
                searchCondition = new SearchCondition(
                    searchCondition.CategoryCombinedName,
                    searchCondition.PropertyCombinedName,
                    options,
                    searchCondition.Comparison,
                    searchCondition.Value);
            }

            return searchCondition;
        }

        private static SearchCondition CreateBaseSearchCondition(
            Document document,
            FindItemsCondition condition,
            ICollection<string> bindingWarnings,
            ref int runtimeResolvedConditionCount)
        {
            if (SearchSetConditionRulesHelper.IsDefaultItemNameTarget(condition))
            {
                return SearchCondition.HasPropertyByName(
                    SearchSetConditionRulesHelper.ItemInternalCategory,
                    SearchSetConditionRulesHelper.ItemNameInternalProperty);
            }

            if (!string.IsNullOrWhiteSpace(condition.Category) && !string.IsNullOrWhiteSpace(condition.Property))
            {
                PersistedPropertyBinding binding;
                if (!TryResolvePersistedPropertyBinding(document, condition.Category, condition.Property, out binding))
                {
                    throw new AgentCommandException(
                        ErrorCodes.CommandFailed,
                        "Unable to resolve a real runtime property for " + condition.Category + "/" + condition.Property + ". The Search Set was not created because persisting a display-only condition can create a phantom category and return an incomplete selection.");
                }

                if (binding.HasRoundTrippableInternalNames)
                {
                    if (bindingWarnings != null)
                        bindingWarnings.Add(condition.Category + "/" + condition.Property + " was bound to existing internal property names before persistence.");
                    return SearchCondition.HasPropertyByName(binding.CategoryInternalName, binding.PropertyInternalName);
                }

                runtimeResolvedConditionCount++;
                if (bindingWarnings != null)
                    bindingWarnings.Add(condition.Category + "/" + condition.Property + " was bound through runtime property IDs because its internal name is not round-trippable.");
                return SearchCondition.HasPropertyByCombinedName(binding.CategoryCombinedName, binding.PropertyCombinedName);
            }

            if (!string.IsNullOrWhiteSpace(condition.CategoryInternal) && !string.IsNullOrWhiteSpace(condition.PropertyInternal))
                return SearchCondition.HasPropertyByName(condition.CategoryInternal.Trim(), condition.PropertyInternal.Trim());

            throw new AgentCommandException(ErrorCodes.SchemaViolation, "Search condition must specify display category/property or internal category/property names.");
        }

        private static bool TryResolvePersistedPropertyBinding(
            Document document,
            string categoryDisplayName,
            string propertyDisplayName,
            out PersistedPropertyBinding binding)
        {
            binding = null;
            if (document == null || document.Models == null)
                return false;

            var stopwatch = Stopwatch.StartNew();
            foreach (ModelItem item in document.Models.RootItemDescendantsAndSelf)
            {
                if (stopwatch.ElapsedMilliseconds > RuntimePropertyBindingTimeoutMilliseconds)
                {
                    throw new AgentCommandException(
                        ErrorCodes.CommandFailed,
                        "Timed out while resolving a real property for the persisted Search Set. Narrow the loaded model or retry after it finishes loading.");
                }

                if (item == null || item.PropertyCategories == null)
                    continue;

                foreach (PropertyCategory category in item.PropertyCategories)
                {
                    if (category == null || category.Properties == null ||
                        !string.Equals(category.DisplayName ?? string.Empty, categoryDisplayName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (DataProperty property in category.Properties)
                    {
                        if (property == null ||
                            !string.Equals(property.DisplayName ?? string.Empty, propertyDisplayName ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                            category.CombinedName == null || property.CombinedName == null)
                        {
                            continue;
                        }

                        binding = new PersistedPropertyBinding(
                            category.Name,
                            property.Name,
                            category.CombinedName,
                            property.CombinedName);
                        return true;
                    }
                }
            }

            return false;
        }

        private static VariantData CreateVariantData(FindItemsCondition condition)
        {
            VariantData typedVariant;
            if (TryCreateTypedVariantData(condition, out typedVariant))
                return typedVariant;

            return VariantData.FromDisplayString(condition == null ? string.Empty : condition.Value ?? string.Empty);
        }

        private static bool TryCreateTypedVariantData(FindItemsCondition condition, out VariantData variant)
        {
            variant = null;
            var dataType = FindItemsSearchRulesHelper.NormalizeDataType(condition == null ? null : condition.DataType);
            if (string.IsNullOrEmpty(dataType) || string.Equals(dataType, "wstring", StringComparison.Ordinal))
                return false;

            int intValue;
            if (string.Equals(dataType, "int32", StringComparison.Ordinal) &&
                int.TryParse(condition == null ? null : condition.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
            {
                variant = VariantData.FromInt32(intValue);
                return true;
            }

            double doubleValue;
            if (string.Equals(dataType, "double", StringComparison.Ordinal) &&
                double.TryParse(NormalizeInvariantDecimal(condition == null ? null : condition.Value), NumberStyles.Float, CultureInfo.InvariantCulture, out doubleValue))
            {
                variant = VariantData.FromDouble(doubleValue);
                return true;
            }

            bool boolValue;
            if (string.Equals(dataType, "bool", StringComparison.Ordinal) &&
                bool.TryParse(condition == null ? null : condition.Value, out boolValue))
            {
                variant = VariantData.FromBoolean(boolValue);
                return true;
            }

            DateTime dateTimeValue;
            if (string.Equals(dataType, "datetime", StringComparison.Ordinal) &&
                DateTime.TryParse(condition == null ? null : condition.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dateTimeValue))
            {
                variant = VariantData.FromDateTime(dateTimeValue);
                return true;
            }

            return false;
        }

        private static string NormalizeInvariantDecimal(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;
            var normalized = value.Trim();
            return normalized.IndexOf('.') < 0 ? normalized.Replace(',', '.') : normalized;
        }

        private static bool ShouldIgnoreStringValueCase(VariantData variant)
        {
            return variant != null && (variant.IsDisplayString || variant.IsIdentifierString);
        }

        private static string NormalizeComparison(string comparison)
        {
            try
            {
                return SearchSetConditionRulesHelper.NormalizeComparison(comparison);
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }
        }

        private sealed class PersistedPropertyBinding
        {
            internal PersistedPropertyBinding(
                string categoryInternalName,
                string propertyInternalName,
                NamedConstant categoryCombinedName,
                NamedConstant propertyCombinedName)
            {
                CategoryInternalName = categoryInternalName;
                PropertyInternalName = propertyInternalName;
                CategoryCombinedName = categoryCombinedName;
                PropertyCombinedName = propertyCombinedName;
            }

            internal string CategoryInternalName { get; private set; }
            internal string PropertyInternalName { get; private set; }
            internal NamedConstant CategoryCombinedName { get; private set; }
            internal NamedConstant PropertyCombinedName { get; private set; }

            internal bool HasRoundTrippableInternalNames =>
                SearchSetRuntimeBindingHelper.IsRoundTrippableInternalName(CategoryInternalName) &&
                SearchSetRuntimeBindingHelper.IsRoundTrippableInternalName(PropertyInternalName);
        }
    }
}
