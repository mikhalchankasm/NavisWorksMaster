using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class SearchService
    {
        private static int ClampItemChildrenLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultItemChildrenLimit);
            if (value < 1)
                return 1;
            if (value > MaxItemChildrenLimit)
                return MaxItemChildrenLimit;
            return value;
        }

        private static List<FindItemsSearch> NormalizeSearches(FindItemsRequest request)
        {
            if (request.Searches != null && request.Searches.Count > 0)
                return request.Searches.Select(NormalizeStructuredSearch).ToList();

            var comparison = NormalizeComparison(request.Comparison);
            var category = NormalizeCategory(request.Category);
            var property = NormalizeProperty(request.Property);
            var queries = request.Queries == null
                ? new List<string>()
                : request.Queries
                    .Where(q => !string.IsNullOrWhiteSpace(q))
                    .Select(q => q.Trim())
                    .ToList();
            if (!string.IsNullOrWhiteSpace(request.Query))
                queries.Insert(0, request.Query.Trim());

            var searches = new List<FindItemsSearch>();
            foreach (var query in queries)
            {
                searches.Add(new FindItemsSearch
                {
                    Query = query,
                    CombineOperator = FindItemsCombineOperators.All,
                    Conditions = new List<FindItemsCondition>
                    {
                        new FindItemsCondition
                        {
                            Category = category,
                            Property = property,
                            Operator = comparison,
                            Value = query,
                            IgnoreCase = request.IgnoreCase,
                            IgnoreDiacritics = request.IgnoreDiacritics,
                            IgnoreCharWidth = request.IgnoreCharWidth,
                        }
                    }
                });
            }

            if (searches.Count == 0 &&
                (string.Equals(comparison, FindItemsComparisons.Defined, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(comparison, FindItemsComparisons.NotDefined, StringComparison.OrdinalIgnoreCase)))
            {
                searches.Add(new FindItemsSearch
                {
                    Query = BuildConditionLabel(category, property, comparison, null),
                    CombineOperator = FindItemsCombineOperators.All,
                    Conditions = new List<FindItemsCondition>
                    {
                        new FindItemsCondition
                        {
                            Category = category,
                            Property = property,
                            Operator = comparison,
                            IgnoreCase = request.IgnoreCase,
                            IgnoreDiacritics = request.IgnoreDiacritics,
                            IgnoreCharWidth = request.IgnoreCharWidth,
                        }
                    }
                });
            }

            return searches;
        }

        private static FindItemsSearch NormalizeStructuredSearch(FindItemsSearch search)
        {
            if (search == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "A search entry cannot be null.");

            var conditions = new List<FindItemsCondition>();
            if (search.Conditions != null)
            {
                foreach (var condition in search.Conditions)
                    conditions.Add(NormalizeCondition(condition));
            }

            if (conditions.Count == 0 && !string.IsNullOrWhiteSpace(search.Query))
            {
                conditions.Add(new FindItemsCondition
                {
                    Category = DefaultCategory,
                    Property = DefaultProperty,
                    Operator = FindItemsComparisons.Contains,
                    Value = search.Query.Trim(),
                });
            }

            if (conditions.Count == 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Each search must include at least one condition.");

            var combineOperator = NormalizeCombineOperator(search.CombineOperator);
            return new FindItemsSearch
            {
                Query = string.IsNullOrWhiteSpace(search.Query)
                    ? BuildSearchLabel(conditions, combineOperator)
                    : search.Query.Trim(),
                CombineOperator = combineOperator,
                Conditions = conditions,
            };
        }

        private static FindItemsCondition NormalizeCondition(FindItemsCondition condition)
        {
            if (condition == null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "A search condition cannot be null.");

            var comparison = NormalizeComparison(condition.Operator);
            var value = condition.Value == null ? null : condition.Value.Trim();
            var requiresValue =
                !string.Equals(comparison, FindItemsComparisons.Defined, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(comparison, FindItemsComparisons.NotDefined, StringComparison.OrdinalIgnoreCase);

            if (requiresValue && string.IsNullOrWhiteSpace(value))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "The operator \"" + comparison + "\" requires a non-empty value.");

            var categoryInternal = TrimOrNull(condition.CategoryInternal);
            var propertyInternal = TrimOrNull(condition.PropertyInternal);
            var category = TrimOrNull(condition.Category);
            var property = TrimOrNull(condition.Property);

            if (string.IsNullOrWhiteSpace(property) && string.IsNullOrWhiteSpace(propertyInternal))
                property = DefaultProperty;

            return new FindItemsCondition
            {
                Category = category,
                Property = property,
                CategoryInternal = categoryInternal,
                PropertyInternal = propertyInternal,
                DataType = NormalizeDataType(condition.DataType),
                InheritFromAncestor = condition.InheritFromAncestor,
                Operator = comparison,
                Comparison = comparison,
                Value = requiresValue ? value : null,
                LogicalOperator = NormalizeConditionLogicalOperator(condition.LogicalOperator),
                Negate = condition.Negate.GetValueOrDefault(false),
                IgnoreCase = condition.IgnoreCase.GetValueOrDefault(true),
                IgnoreDiacritics = condition.IgnoreDiacritics.GetValueOrDefault(false),
                IgnoreCharWidth = condition.IgnoreCharWidth.GetValueOrDefault(false),
            };
        }

        private static string NormalizeConditionLogicalOperator(string value)
        {
            try
            {
                return FindItemsConditionOptionsHelper.NormalizeLogicalOperator(value);
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }
        }

        private static bool CanUseNativeAndFastPathCondition(FindItemsCondition condition, ResolvedProperty resolved)
        {
            if (condition == null || resolved == null)
                return false;

            var comparison = NormalizeComparison(GetConditionComparison(condition));
            if (!string.Equals(comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(comparison, FindItemsComparisons.Contains, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(comparison, FindItemsComparisons.Wildcard, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(comparison, FindItemsComparisons.Defined, StringComparison.OrdinalIgnoreCase))
                return false;

            if (resolved.IsDefaultItemNameTarget &&
                string.Equals(comparison, FindItemsComparisons.Defined, StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static bool CanFilterAccumulator(FindItemsCondition condition)
        {
            return CanEvaluateConditionManually(condition);
        }

        private static void EnsureSearchIsSafeToExecute(FindItemsSearch search)
        {
            if (search == null || search.Conditions == null || search.Conditions.Count == 0)
                return;

            var unsafeBroadConditionCount = search.Conditions.Count(IsUnsafeBroadPositiveCondition);
            if (unsafeBroadConditionCount == 0)
                return;

            var combineAll = string.Equals(search.CombineOperator, FindItemsCombineOperators.All, StringComparison.OrdinalIgnoreCase);
            if (!combineAll)
            {
                throw new TooAmbiguousSearchException(
                    "The find_items request contains a broad short contains/wildcard condition in an OR search. Split it into narrower searches or remove the short term.");
            }

            var anchorConditionCount = search.Conditions.Count(IsPositiveAnchorCondition);
            if (anchorConditionCount == 0)
            {
                throw new TooAmbiguousSearchException(
                    "The find_items request contains only broad short contains/wildcard conditions. Use a longer value or add a selective equals/source-file condition.");
            }
        }

        private static List<FindItemsCondition> OrderConditionsForExecution(IEnumerable<FindItemsCondition> conditions, bool combineAll)
        {
            var ordered = conditions == null
                ? new List<FindItemsCondition>()
                : conditions.ToList();

            if (!combineAll || ordered.Count < 2)
                return ordered;

            return ordered
                .Select((condition, index) => new
                {
                    Condition = condition,
                    Index = index,
                    Resolved = ResolveProperty(condition),
                })
                .OrderBy(entry => GetConditionExecutionPriority(entry.Condition, entry.Resolved))
                .ThenByDescending(entry => GetConditionExecutionSelectivityScore(entry.Condition, entry.Resolved))
                .ThenBy(entry => entry.Index)
                .Select(entry => entry.Condition)
                .ToList();
        }

        private static int GetConditionExecutionPriority(FindItemsCondition condition, ResolvedProperty resolved)
        {
            var comparison = NormalizeComparison(condition.Operator);
            if (IsUnsafeBroadPositiveCondition(condition))
                return 4;

            if (resolved != null &&
                resolved.InheritFromAncestor &&
                string.Equals(comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase))
                return 0;

            if (string.Equals(comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase))
                return 1;

            if (ShouldEvaluateConditionManually(condition, resolved))
                return 2;

            return 3;
        }

        private static int GetConditionExecutionSelectivityScore(FindItemsCondition condition, ResolvedProperty resolved)
        {
            if (condition == null)
                return 0;

            var comparison = NormalizeComparison(condition.Operator);
            var literalScore = CountSearchLiteralCharacters(comparison, condition.Value);
            var isInherited = resolved != null && resolved.InheritFromAncestor;

            if (string.Equals(comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase))
                return (isInherited ? 1000 : 0) + 100 + literalScore;

            if (string.Equals(comparison, FindItemsComparisons.Contains, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(comparison, FindItemsComparisons.Wildcard, StringComparison.OrdinalIgnoreCase))
                return literalScore;

            return isInherited ? 1000 : 0;
        }

        private static string NormalizeConditionString(string value, FindItemsCondition condition)
        {
            return FindItemsSearchRulesHelper.NormalizeConditionString(
                value,
                condition != null && condition.IgnoreCharWidth.GetValueOrDefault(false),
                condition != null && condition.IgnoreDiacritics.GetValueOrDefault(false));
        }

        private static SearchCondition BuildPositiveSearchCondition(
            ResolvedProperty resolved,
            FindItemsCondition condition,
            VariantData variantOverride = null)
        {
            var baseCondition = CreateSearchCondition(resolved, condition);
            var comparison = NormalizeComparison(condition.Operator);

            if (string.Equals(comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase))
            {
                var variant = variantOverride ?? CreateVariantData(condition);
                var equalCondition = baseCondition.EqualValue(variant);
                return ApplySearchConditionStringOptions(equalCondition, condition, ShouldIgnoreStringValueCase(variant));
            }

            if (string.Equals(comparison, FindItemsComparisons.Contains, StringComparison.OrdinalIgnoreCase))
            {
                return ApplySearchConditionStringOptions(baseCondition.DisplayStringContains(condition.Value), condition, true);
            }

            if (string.Equals(comparison, FindItemsComparisons.StartsWith, StringComparison.OrdinalIgnoreCase))
            {
                return ApplySearchConditionStringOptions(baseCondition.DisplayStringWildcard((condition.Value ?? string.Empty) + "*"), condition, true);
            }

            if (string.Equals(comparison, FindItemsComparisons.EndsWith, StringComparison.OrdinalIgnoreCase))
            {
                return ApplySearchConditionStringOptions(baseCondition.DisplayStringWildcard("*" + (condition.Value ?? string.Empty)), condition, true);
            }

            if (string.Equals(comparison, FindItemsComparisons.Wildcard, StringComparison.OrdinalIgnoreCase))
            {
                return ApplySearchConditionStringOptions(baseCondition.DisplayStringWildcard(condition.Value), condition, true);
            }

            if (string.Equals(comparison, FindItemsComparisons.Defined, StringComparison.OrdinalIgnoreCase))
                return baseCondition;

            throw new AgentCommandException(ErrorCodes.SchemaViolation, "Unsupported positive find_items operator: " + comparison);
        }

        private static SearchCondition CreateSearchCondition(ResolvedProperty resolved, FindItemsCondition condition)
        {
            var candidate = resolved.DisplayCandidates.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(candidate.Property))
            {
                return SearchCondition.HasPropertyByDisplayName(
                    string.IsNullOrWhiteSpace(candidate.Category) ? string.Empty : candidate.Category,
                    candidate.Property);
            }

            if (!string.IsNullOrWhiteSpace(resolved.InternalCategory) &&
                !string.IsNullOrWhiteSpace(resolved.InternalProperty))
            {
                return SearchCondition.HasPropertyByName(resolved.InternalCategory, resolved.InternalProperty);
            }

            return SearchCondition.HasPropertyByDisplayName(
                NormalizeCategory(condition.Category),
                NormalizeProperty(condition.Property));
        }

        private static VariantData CreateVariantData(FindItemsCondition condition)
        {
            VariantData typedVariant;
            if (TryCreateTypedVariantData(condition, out typedVariant))
                return typedVariant;

            return VariantData.FromDisplayString(condition.Value ?? string.Empty);
        }

        private static bool TryCreateTypedVariantData(FindItemsCondition condition, out VariantData variant)
        {
            variant = null;
            var dataType = NormalizeDataType(condition == null ? null : condition.DataType);

            int intValue;
            if ((string.Equals(dataType, "int32", StringComparison.Ordinal) || string.IsNullOrEmpty(dataType)) &&
                TryParseInt32(condition == null ? null : condition.Value, out intValue))
            {
                variant = VariantData.FromInt32(intValue);
                return true;
            }

            double doubleValue;
            if ((string.Equals(dataType, "double", StringComparison.Ordinal) || string.IsNullOrEmpty(dataType)) &&
                TryParseDouble(condition == null ? null : condition.Value, out doubleValue))
            {
                variant = VariantData.FromDouble(doubleValue);
                return true;
            }

            bool boolValue;
            if ((string.Equals(dataType, "bool", StringComparison.Ordinal) || string.IsNullOrEmpty(dataType)) &&
                TryParseBoolean(condition == null ? null : condition.Value, out boolValue))
            {
                variant = VariantData.FromBoolean(boolValue);
                return true;
            }

            DateTime dateTimeValue;
            if ((string.Equals(dataType, "datetime", StringComparison.Ordinal) || string.IsNullOrEmpty(dataType)) &&
                TryParseDateTime(condition == null ? null : condition.Value, out dateTimeValue))
            {
                variant = VariantData.FromDateTime(dateTimeValue);
                return true;
            }

            return false;
        }

        private static bool ShouldIgnoreStringValueCase(VariantData variant)
        {
            return variant != null && (variant.IsDisplayString || variant.IsIdentifierString);
        }

        private static bool ShouldEvaluateConditionManually(FindItemsCondition condition, ResolvedProperty resolved)
        {
            var comparison = NormalizeComparison(condition.Operator);

            if (string.Equals(comparison, FindItemsComparisons.NotEquals, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(comparison, FindItemsComparisons.Defined, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(comparison, FindItemsComparisons.NotDefined, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        private static SearchCondition ApplySearchConditionStringOptions(SearchCondition searchCondition, FindItemsCondition condition, bool isStringValue)
        {
            if (searchCondition == null)
                return null;
            if (isStringValue && condition != null && condition.IgnoreCase.GetValueOrDefault(true))
                searchCondition = searchCondition.IgnoreStringValueCase();
            if (isStringValue && condition != null && condition.IgnoreDiacritics.GetValueOrDefault(false))
                searchCondition = searchCondition.IgnoreStringValueAccents();
            if (isStringValue && condition != null && condition.IgnoreCharWidth.GetValueOrDefault(false))
                searchCondition = searchCondition.IgnoreStringValueCharWidths();
            return searchCondition;
        }

        private static bool IsPositiveAnchorCondition(FindItemsCondition condition)
        {
            if (condition == null || IsUnsafeBroadPositiveCondition(condition))
                return false;

            var comparison = NormalizeComparison(condition.Operator);
            return IsPositiveComparison(comparison);
        }

        private static bool IsPositiveComparison(string comparison)
        {
            return FindItemsSearchRulesHelper.IsPositiveComparison(comparison);
        }

        private static bool CanEvaluateConditionManually(FindItemsCondition condition)
        {
            if (condition == null)
                return false;

            var comparison = NormalizeComparison(condition.Operator);
            return IsPositiveComparison(comparison) ||
                   string.Equals(comparison, FindItemsComparisons.NotEquals, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(comparison, FindItemsComparisons.Defined, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(comparison, FindItemsComparisons.NotDefined, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBroadInheritedPositiveCondition(FindItemsCondition condition, ResolvedProperty resolved, string comparison)
        {
            if (condition == null || resolved == null || !resolved.InheritFromAncestor)
                return false;

            return string.Equals(comparison, FindItemsComparisons.Defined, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(comparison, FindItemsComparisons.Contains, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(comparison, FindItemsComparisons.Wildcard, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsUnsafeBroadPositiveCondition(FindItemsCondition condition)
        {
            if (condition == null)
                return false;

            var comparison = NormalizeComparison(condition.Operator);
            if (!string.Equals(comparison, FindItemsComparisons.Contains, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(comparison, FindItemsComparisons.Wildcard, StringComparison.OrdinalIgnoreCase))
                return false;

            return CountSearchLiteralCharacters(comparison, condition.Value) < MinBroadPositiveLiteralLength;
        }

        private static int CountSearchLiteralCharacters(string comparison, string value)
        {
            return FindItemsSearchRulesHelper.CountSearchLiteralCharacters(comparison, value);
        }

        private static bool ShouldTryDisplayStringFallback(FindItemsCondition condition)
        {
            VariantData variant;
            return TryCreateTypedVariantData(condition, out variant) && !ShouldIgnoreStringValueCase(variant);
        }

        private static ResolvedProperty ResolveProperty(FindItemsCondition condition)
        {
            var resolved = new ResolvedProperty
            {
                IsDefaultItemNameTarget = IsDefaultItemNameTarget(condition),
            };

            if (resolved.IsDefaultItemNameTarget)
            {
                if (IsDefaultItemNameInternalTarget(condition))
                {
                    resolved.InternalCategory = TrimOrNull(condition.CategoryInternal);
                    resolved.InternalProperty = TrimOrNull(condition.PropertyInternal);
                }

                foreach (var alias in DisplayNameProperties)
                    AddDisplayCandidate(resolved.DisplayCandidates, alias.Category, alias.Name);

                return resolved;
            }

            var knownProperty = FindKnownProperty(condition);
            if (!string.IsNullOrWhiteSpace(condition.Property))
            {
                resolved.InternalCategory = knownProperty == null ? null : knownProperty.InternalCategory;
                resolved.InternalProperty = knownProperty == null ? null : knownProperty.InternalProperty;
            }
            else
            {
                resolved.InternalCategory = FirstNonEmpty(TrimOrNull(condition.CategoryInternal), knownProperty == null ? null : knownProperty.InternalCategory);
                resolved.InternalProperty = FirstNonEmpty(TrimOrNull(condition.PropertyInternal), knownProperty == null ? null : knownProperty.InternalProperty);
            }
            resolved.InheritFromAncestor = condition.InheritFromAncestor ?? (knownProperty != null && knownProperty.InheritFromAncestor);

            AddDisplayCandidate(resolved.DisplayCandidates, condition.Category, condition.Property);

            if (knownProperty != null)
            {
                foreach (var categoryAlias in knownProperty.CategoryAliases)
                {
                    foreach (var propertyAlias in knownProperty.PropertyAliases)
                        AddDisplayCandidate(resolved.DisplayCandidates, categoryAlias, propertyAlias);
                }
            }

            if (resolved.DisplayCandidates.Count == 0 && string.IsNullOrWhiteSpace(resolved.InternalProperty))
                AddDisplayCandidate(resolved.DisplayCandidates, NormalizeCategory(condition.Category), NormalizeProperty(condition.Property));

            return resolved;
        }

        private static ResolvedProperty CreateResolvedInternalProperty(string categoryInternal, string propertyInternal)
        {
            return new ResolvedProperty
            {
                InternalCategory = TrimOrNull(categoryInternal),
                InternalProperty = TrimOrNull(propertyInternal),
            };
        }

        private static ResolvedProperty CreateResolvedDisplayProperty(string category, string property)
        {
            var resolved = new ResolvedProperty();
            AddDisplayCandidate(resolved.DisplayCandidates, category, property);
            return resolved;
        }

        private static KnownPropertyDefinition FindKnownProperty(FindItemsCondition condition)
        {
            var displayCategory = NormalizeComparableText(condition.Category);
            var displayProperty = NormalizeComparableText(condition.Property);
            var internalCategory = NormalizeComparableText(condition.CategoryInternal);
            var internalProperty = NormalizeComparableText(condition.PropertyInternal);

            if (!string.IsNullOrWhiteSpace(condition.Property))
            {
                return KnownProperties.FirstOrDefault(property =>
                    property.MatchesDisplayAlias(displayCategory, displayProperty));
            }

            return KnownProperties.FirstOrDefault(property =>
                property.MatchesInternalAlias(internalCategory, internalProperty));
        }

        private static void AddDisplayCandidate(ICollection<(string Category, string Property)> candidates, string category, string property)
        {
            if (candidates == null || string.IsNullOrWhiteSpace(property))
                return;

            var candidate = (
                Category: string.IsNullOrWhiteSpace(category) ? string.Empty : category.Trim(),
                Property: property.Trim());

            if (!candidates.Any(existing =>
                string.Equals(existing.Category, candidate.Category, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.Property, candidate.Property, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(candidate);
            }
        }

        private static bool IsDefaultItemNameTarget(FindItemsCondition condition)
        {
            if (condition == null)
                return false;

            var normalizedCategory = NormalizeComparableText(condition.Category);
            var normalizedProperty = NormalizeComparableText(condition.Property);

            if (normalizedProperty != "name" && normalizedProperty != "имя")
                return false;

            return string.IsNullOrEmpty(normalizedCategory) ||
                   normalizedCategory == "item" ||
                   normalizedCategory == "элемент";
        }

        private static bool IsDefaultItemNameInternalTarget(FindItemsCondition condition)
        {
            if (condition == null)
                return false;

            var internalCategory = NormalizeComparableText(condition.CategoryInternal);
            var internalProperty = NormalizeComparableText(condition.PropertyInternal);

            if (!string.IsNullOrEmpty(internalCategory) &&
                internalCategory != NormalizeComparableText(ItemInternalCategory))
                return false;

            return internalProperty == NormalizeComparableText(ItemUserNameInternalProperty) ||
                   internalProperty == "name";
        }

        private static int ClampPreviewLimit(int? previewLimit)
        {
            var value = previewLimit.GetValueOrDefault(DefaultPreviewLimit);
            if (value < 1)
                return 1;
            if (value > MaxPreviewLimit)
                return MaxPreviewLimit;
            return value;
        }

        private static string NormalizeComparison(string comparison)
        {
            try
            {
                return FindItemsSearchRulesHelper.NormalizeComparison(comparison);
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }
        }

        private static string GetConditionComparison(FindItemsCondition condition)
        {
            if (condition == null)
                return null;
            return string.IsNullOrWhiteSpace(condition.Operator)
                ? condition.Comparison
                : condition.Operator;
        }

        private static string NormalizeCombineOperator(string combineOperator)
        {
            try
            {
                return FindItemsSearchRulesHelper.NormalizeCombineOperator(combineOperator);
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }
        }

        private static string NormalizeCategory(string category)
        {
            return string.IsNullOrWhiteSpace(category)
                ? DefaultCategory
                : category.Trim();
        }

        private static string NormalizeProperty(string property)
        {
            return string.IsNullOrWhiteSpace(property)
                ? DefaultProperty
                : property.Trim();
        }

        private static string NormalizeDataType(string dataType)
        {
            return FindItemsSearchRulesHelper.NormalizeDataType(dataType);
        }

        private static string NormalizeComparableText(string value)
        {
            return FindItemsSearchRulesHelper.NormalizeComparableText(value);
        }

        private static string TrimOrNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string FirstNonEmpty(string preferred, string fallback)
        {
            return string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
        }

        private static string BuildSearchLabel(IReadOnlyCollection<FindItemsCondition> conditions, string combineOperator)
        {
            var separator = string.Equals(combineOperator, FindItemsCombineOperators.Any, StringComparison.OrdinalIgnoreCase)
                ? " OR "
                : " AND ";

            return string.Join(separator, conditions.Select(condition => BuildConditionLabel(
                condition.Category,
                condition.Property,
                condition.Operator,
                condition.Value)));
        }

        private static string BuildConditionLabel(FindItemsCondition condition)
        {
            if (condition == null)
                return string.Empty;

            return BuildConditionLabel(
                condition.Category,
                condition.Property,
                condition.Operator,
                condition.Value);
        }

        private static string BuildConditionLabel(string category, string property, string comparison, string value)
        {
            var prefix = category + "/" + property + " " + NormalizeComparison(comparison);
            if (string.Equals(comparison, FindItemsComparisons.Defined, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(comparison, FindItemsComparisons.NotDefined, StringComparison.OrdinalIgnoreCase))
                return prefix;

            return prefix + " \"" + value + "\"";
        }

        private static long GetElapsedMilliseconds(Stopwatch stopwatch)
        {
            return stopwatch == null ? 0 : stopwatch.ElapsedMilliseconds;
        }
    }
}
