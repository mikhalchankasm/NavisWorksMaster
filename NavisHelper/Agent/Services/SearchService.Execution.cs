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
        private FindItemsResult FindSingle(Document document, FindItemsSearch search, int previewLimit, MatchSessionStore sessionStore)
        {
            var started = Stopwatch.StartNew();
            List<ModelItem> matchedItems;
            try
            {
                EnsureSearchIsSafeToExecute(search);
                matchedItems = ExecuteSearch(document, search, started);
            }
            catch (TooAmbiguousSearchException ex)
            {
                started.Stop();
                Logger.Info(
                    "find_items query=\"" + search.Query + "\" combine=" + search.CombineOperator + " conditions=" + search.Conditions.Count + " status=" + FindItemStatuses.QueryTooAmbiguous + " elapsed_ms=" + started.ElapsedMilliseconds + " message=" + ex.Message,
                    "AgentHost");

                return new FindItemsResult
                {
                    Query = search.Query,
                    Status = FindItemStatuses.QueryTooAmbiguous,
                };
            }

            started.Stop();

            Logger.Info(
                "find_items query=\"" + search.Query + "\" combine=" + search.CombineOperator + " conditions=" + search.Conditions.Count + " hits=" + matchedItems.Count + " elapsed_ms=" + started.ElapsedMilliseconds,
                "AgentHost");

            if (matchedItems.Count == 0)
            {
                return new FindItemsResult
                {
                    Query = search.Query,
                    Status = FindItemStatuses.NotFound,
                };
            }

            var handle = sessionStore.Add(matchedItems);
            var result = new FindItemsResult
            {
                Query = search.Query,
                Status = FindItemStatuses.Matched,
            };

            result.Matches.Add(new FindItemsMatch
            {
                MatchHandle = handle,
                ItemCount = matchedItems.Count,
                Preview = matchedItems
                    .Take(previewLimit)
                    .Select(BuildPreviewItem)
                    .ToList(),
                PreviewTruncated = matchedItems.Count > previewLimit,
            });

            return result;
        }

        private static MatchPreviewItem BuildPreviewItem(ModelItem item)
        {
            return new MatchPreviewItem
            {
                DisplayName = item == null ? string.Empty : item.DisplayName,
                Path = BuildItemPath(item),
                SourceFile = TryGetSourceFile(item),
            };
        }

        private static List<ModelItem> ExecuteSearch(Document document, FindItemsSearch search, Stopwatch searchStarted)
        {
            if (search == null || search.Conditions == null || search.Conditions.Count == 0)
                return new List<ModelItem>();

            Dictionary<string, ModelItem> accumulator = null;
            var combineAll = string.Equals(search.CombineOperator, FindItemsCombineOperators.All, StringComparison.OrdinalIgnoreCase);
            var hasConditionLevelLogic = search.Conditions.Any(condition =>
                condition != null && !string.Equals(condition.LogicalOperator, FindItemsConditionOptionsHelper.And, StringComparison.OrdinalIgnoreCase));
            var orderedConditions = hasConditionLevelLogic
                ? search.Conditions.ToList()
                : OrderConditionsForExecution(search.Conditions, combineAll);

            List<ModelItem> nativeAndMatches;
            if (!hasConditionLevelLogic && TryExecuteNativeAndFastPath(document, orderedConditions, combineAll, searchStarted, out nativeAndMatches))
                return nativeAndMatches;

            foreach (var condition in orderedConditions)
            {
                if (combineAll && accumulator != null && CanFilterAccumulator(condition))
                {
                    var beforeCount = accumulator.Count;
                    Logger.Info(
                        "find_items condition_stage_start query=\"" + search.Query + "\" mode=manual_filter accumulator=" + beforeCount + " condition=\"" + BuildConditionLabel(condition) + "\" elapsed_ms=" + GetElapsedMilliseconds(searchStarted),
                        "AgentHost");

                    FilterAccumulator(accumulator, condition, searchStarted);
                    Logger.Info(
                        "find_items condition_stage_done query=\"" + search.Query + "\" mode=manual_filter before=" + beforeCount + " after=" + accumulator.Count + " condition=\"" + BuildConditionLabel(condition) + "\" elapsed_ms=" + GetElapsedMilliseconds(searchStarted),
                        "AgentHost");

                    if (accumulator.Count == 0)
                        break;

                    continue;
                }

                var conditionPathCache = new Dictionary<ModelItem, string>();
                var conditionStarted = Stopwatch.StartNew();
                Logger.Info(
                    "find_items condition_stage_start query=\"" + search.Query + "\" mode=native_condition accumulator=" + (accumulator == null ? -1 : accumulator.Count) + " condition=\"" + BuildConditionLabel(condition) + "\" elapsed_ms=" + GetElapsedMilliseconds(searchStarted),
                    "AgentHost");

                var currentMatches = ExecuteConditionSearch(document, condition, conditionPathCache);
                conditionStarted.Stop();
                Logger.Info(
                    "find_items condition_stage_done query=\"" + search.Query + "\" mode=native_condition hits=" + currentMatches.Count + " condition_elapsed_ms=" + conditionStarted.ElapsedMilliseconds + " condition=\"" + BuildConditionLabel(condition) + "\" elapsed_ms=" + GetElapsedMilliseconds(searchStarted),
                    "AgentHost");

                var currentMap = ToPathMap(currentMatches, conditionPathCache);

                if (accumulator == null)
                {
                    accumulator = currentMap;
                }
                else if (hasConditionLevelLogic
                    ? string.Equals(condition.LogicalOperator, FindItemsConditionOptionsHelper.Or, StringComparison.OrdinalIgnoreCase)
                    : !combineAll)
                {
                    UnionByPath(accumulator, currentMap);
                }
                else
                {
                    IntersectByPath(accumulator, currentMap);
                }

                if (!hasConditionLevelLogic && combineAll && accumulator.Count == 0)
                    break;
            }

            if (accumulator == null || accumulator.Count == 0)
                return new List<ModelItem>();

            var sortPathCache = new Dictionary<ModelItem, string>();
            var matches = accumulator.Values.ToList();
            matches.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(GetCachedPath(left, sortPathCache), GetCachedPath(right, sortPathCache)));
            return matches;
        }

        private static bool TryExecuteNativeAndFastPath(
            Document document,
            IList<FindItemsCondition> orderedConditions,
            bool combineAll,
            Stopwatch searchStarted,
            out List<ModelItem> matches)
        {
            matches = null;

            if (!combineAll || orderedConditions == null || orderedConditions.Count < 2)
                return false;

            var nativeConditionGroups = new List<List<SearchCondition>>();
            var postFilters = new List<FindItemsCondition>();
            var nativeVariantCount = 1;

            foreach (var condition in orderedConditions)
            {
                var resolved = ResolveProperty(condition);
                if (!CanUseNativeAndFastPathCondition(condition, resolved))
                    return false;

                if (resolved.InheritFromAncestor)
                {
                    postFilters.Add(condition);
                    continue;
                }

                var alternatives = BuildNativeAndFastPathConditionAlternatives(resolved, condition);
                if (alternatives.Count == 0)
                    return false;

                if (nativeVariantCount > MaxNativeAndFastPathVariants / alternatives.Count)
                    return false;

                nativeVariantCount *= alternatives.Count;
                nativeConditionGroups.Add(alternatives);
            }

            if (nativeConditionGroups.Count == 0)
                return false;

            var pathCache = new Dictionary<ModelItem, string>();
            var resultMap = new Dictionary<string, ModelItem>(StringComparer.OrdinalIgnoreCase);
            Logger.Info(
                "find_items native_and_fast_path_start native_conditions=" + nativeConditionGroups.Count + " variants=" + nativeVariantCount + " post_filters=" + postFilters.Count + " elapsed_ms=" + GetElapsedMilliseconds(searchStarted),
                "AgentHost");

            var variantIndex = 0;
            foreach (var nativeConditions in BuildNativeAndFastPathConditionSets(nativeConditionGroups))
            {
                variantIndex++;
                Logger.Info(
                    "find_items native_and_fast_path_variant_start variant=" + variantIndex + "/" + nativeVariantCount + " conditions=" + nativeConditions.Count + " elapsed_ms=" + GetElapsedMilliseconds(searchStarted),
                    "AgentHost");

                var nativeMatches = ToPathMap(ExecuteSearchQuery(document, nativeConditions, pathCache), pathCache);
                UnionByPath(resultMap, nativeMatches);
                Logger.Info(
                    "find_items native_and_fast_path_variant_done variant=" + variantIndex + "/" + nativeVariantCount + " hits=" + nativeMatches.Count + " union_hits=" + resultMap.Count + " elapsed_ms=" + GetElapsedMilliseconds(searchStarted),
                    "AgentHost");

                if (postFilters.Count > 0 && resultMap.Count > MaxNativeAndFastPathIntermediateMatches)
                {
                    throw new TooAmbiguousSearchException(
                        "The native find_items fast path produced too many intermediate matches. Add a more selective name/property condition.");
                }
            }

            foreach (var postFilter in postFilters)
            {
                FilterAccumulator(resultMap, postFilter, searchStarted);
                if (resultMap.Count == 0)
                    break;
            }

            var sortPathCache = new Dictionary<ModelItem, string>();
            matches = resultMap.Values.ToList();
            matches.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(GetCachedPath(left, sortPathCache), GetCachedPath(right, sortPathCache)));

            Logger.Info(
                "find_items native_and_fast_path=true native_conditions=" + nativeConditionGroups.Count + " variants=" + nativeVariantCount + " post_filters=" + postFilters.Count + " matches=" + matches.Count + " elapsed_ms=" + (searchStarted == null ? 0 : searchStarted.ElapsedMilliseconds),
                "AgentHost");

            return true;
        }

        private static List<SearchCondition> BuildNativeAndFastPathConditionAlternatives(
            ResolvedProperty resolved,
            FindItemsCondition condition)
        {
            if (resolved.IsDefaultItemNameTarget)
                return BuildDefaultItemNameNativeAlternatives(resolved, condition);

            var alternatives = new List<SearchCondition>
            {
                BuildPositiveSearchCondition(resolved, condition),
            };

            if (string.Equals(NormalizeComparison(condition.Operator), FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase) &&
                ShouldTryDisplayStringFallback(condition))
            {
                alternatives.Add(BuildPositiveSearchCondition(
                    resolved,
                    condition,
                    VariantData.FromDisplayString(condition.Value ?? string.Empty)));
            }

            return alternatives;
        }

        private static List<SearchCondition> BuildDefaultItemNameNativeAlternatives(
            ResolvedProperty resolved,
            FindItemsCondition condition)
        {
            var alternatives = new List<SearchCondition>();
            if (!string.IsNullOrWhiteSpace(resolved.InternalCategory) &&
                !string.IsNullOrWhiteSpace(resolved.InternalProperty))
            {
                AddNativeConditionAlternatives(
                    alternatives,
                    CreateResolvedInternalProperty(resolved.InternalCategory, resolved.InternalProperty),
                    condition);
                return alternatives;
            }

            foreach (var property in DisplayNameProperties)
            {
                AddNativeConditionAlternatives(
                    alternatives,
                    CreateResolvedDisplayProperty(property.Category, property.Name),
                    condition);
            }

            return alternatives;
        }

        private static void AddNativeConditionAlternatives(
            ICollection<SearchCondition> alternatives,
            ResolvedProperty resolved,
            FindItemsCondition condition)
        {
            alternatives.Add(BuildPositiveSearchCondition(resolved, condition));

            if (string.Equals(NormalizeComparison(condition.Operator), FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase) &&
                ShouldTryDisplayStringFallback(condition))
            {
                alternatives.Add(BuildPositiveSearchCondition(
                    resolved,
                    condition,
                    VariantData.FromDisplayString(condition.Value ?? string.Empty)));
            }
        }

        private static IEnumerable<List<SearchCondition>> BuildNativeAndFastPathConditionSets(
            IList<List<SearchCondition>> conditionGroups)
        {
            var sets = new List<List<SearchCondition>>
            {
                new List<SearchCondition>(),
            };

            foreach (var group in conditionGroups)
            {
                var expanded = new List<List<SearchCondition>>();
                foreach (var set in sets)
                {
                    foreach (var condition in group)
                    {
                        var next = new List<SearchCondition>(set);
                        next.Add(condition);
                        expanded.Add(next);
                    }
                }

                sets = expanded;
            }

            return sets;
        }

        private static void FilterAccumulator(IDictionary<string, ModelItem> accumulator, FindItemsCondition condition, Stopwatch searchStarted)
        {
            var comparison = NormalizeComparison(condition.Operator);
            var resolved = ResolveProperty(condition);

            foreach (var key in accumulator.Keys.ToList())
            {
                EnsureManualFilterWithinBudget(searchStarted);

                if (!MatchesManualCondition(accumulator[key], condition, resolved, comparison))
                    accumulator.Remove(key);
            }
        }

        private static void EnsureManualFilterWithinBudget(Stopwatch searchStarted)
        {
            if (searchStarted == null)
                return;

            if (searchStarted.ElapsedMilliseconds <= MaxManualTraversalMilliseconds)
                return;

            throw new TooAmbiguousSearchException(
                "The find_items anchor/filter path is too expensive for this model. Add a more selective equals/source-file condition.");
        }

        private static List<ModelItem> ExecuteConditionSearch(
            Document document,
            FindItemsCondition condition,
            IDictionary<ModelItem, string> pathCache)
        {
            // Native negation is saved for Search Sets below. For transient
            // find_items we evaluate it manually so every comparison type and
            // inherited property follows the same complement semantics.
            if (condition != null && condition.Negate.GetValueOrDefault(false))
                return ExecuteManualConditionSearch(document, condition);

            var resolved = ResolveProperty(condition);
            if (resolved.IsDefaultItemNameTarget)
                return ExecuteDefaultItemNameConditionSearch(document, condition, pathCache);

            return ExecuteExplicitConditionSearch(document, condition, resolved, pathCache);
        }

        private static List<ModelItem> ExecuteDefaultItemNameConditionSearch(
            Document document,
            FindItemsCondition condition,
            IDictionary<ModelItem, string> pathCache)
        {
            var comparison = NormalizeComparison(condition.Operator);

            if (string.Equals(comparison, FindItemsComparisons.NotEquals, StringComparison.OrdinalIgnoreCase))
                return ExecuteManualConditionSearch(document, condition);

            if (string.Equals(comparison, FindItemsComparisons.Defined, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(comparison, FindItemsComparisons.NotDefined, StringComparison.OrdinalIgnoreCase))
                return ExecuteDefaultItemNameExistenceSearch(document, comparison);

            var result = new Dictionary<string, ModelItem>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in DisplayNameProperties)
            {
                var resolved = CreateResolvedDisplayProperty(property.Category, property.Name);
                foreach (var item in ExecuteExplicitConditionSearch(document, new FindItemsCondition
                {
                    Category = property.Category,
                    Property = property.Name,
                    Operator = comparison,
                    Value = condition.Value,
                    IgnoreCase = condition.IgnoreCase,
                    IgnoreDiacritics = condition.IgnoreDiacritics,
                    IgnoreCharWidth = condition.IgnoreCharWidth,
                }, resolved, pathCache))
                {
                    var path = GetCachedPath(item, pathCache);
                    if (!result.ContainsKey(path))
                        result[path] = item;
                }
            }

            return result.Values.ToList();
        }

        private static List<ModelItem> ExecuteDefaultItemNameExistenceSearch(Document document, string comparison)
        {
            var requireDefined = string.Equals(comparison, FindItemsComparisons.Defined, StringComparison.OrdinalIgnoreCase);
            var started = Stopwatch.StartNew();
            var result = new List<ModelItem>();

            foreach (ModelItem item in document.Models.RootItemDescendantsAndSelf)
            {
                if (started.ElapsedMilliseconds > MaxDisplayNameTraversalMilliseconds)
                {
                    throw new TooAmbiguousSearchException(
                        "The requested Item/Name existence search is too broad for this model.");
                }

                string itemName;
                var hasName = TryGetDefaultItemNameValue(item, out itemName);
                if (hasName == requireDefined)
                {
                    result.Add(item);
                    if (result.Count > MaxBroadExistenceMatches)
                    {
                        throw new TooAmbiguousSearchException(
                            "The requested Item/Name existence search matched too many items.");
                    }
                }
            }

            return result;
        }

        private static List<ModelItem> ExecuteExplicitConditionSearch(
            Document document,
            FindItemsCondition condition,
            ResolvedProperty resolved,
            IDictionary<ModelItem, string> pathCache)
        {
            var comparison = NormalizeComparison(condition.Operator);

            if (string.Equals(comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase))
                return ExecuteEqualsConditionSearch(document, condition, resolved, pathCache);

            if (string.Equals(comparison, FindItemsComparisons.NotEquals, StringComparison.OrdinalIgnoreCase))
                return ExecuteManualConditionSearch(document, condition);

            if (string.Equals(comparison, FindItemsComparisons.NotDefined, StringComparison.OrdinalIgnoreCase))
                return ExecuteManualConditionSearch(document, condition);

            var matches = ExecuteSearchQuery(document, BuildPositiveSearchCondition(resolved, condition), pathCache);
            return ExpandInheritedMatchesIfNeeded(matches, condition, resolved, comparison, pathCache);
        }

        private static List<ModelItem> ExecuteEqualsConditionSearch(
            Document document,
            FindItemsCondition condition,
            ResolvedProperty resolved,
            IDictionary<ModelItem, string> pathCache)
        {
            var comparison = NormalizeComparison(condition.Operator);
            var matches = ExecuteSearchQuery(document, BuildPositiveSearchCondition(resolved, condition), pathCache);

            if (ShouldTryDisplayStringFallback(condition))
            {
                var fallbackMatches = ExecuteSearchQuery(
                    document,
                    BuildPositiveSearchCondition(resolved, condition, VariantData.FromDisplayString(condition.Value ?? string.Empty)),
                    pathCache);

                matches = MergeMatchesByPath(matches, fallbackMatches, pathCache);
            }

            return ExpandInheritedMatchesIfNeeded(matches, condition, resolved, comparison, pathCache);
        }

        private static List<ModelItem> ExpandInheritedMatchesIfNeeded(
            List<ModelItem> matches,
            FindItemsCondition condition,
            ResolvedProperty resolved,
            string comparison,
            IDictionary<ModelItem, string> pathCache)
        {
            if (resolved == null || !resolved.InheritFromAncestor)
                return matches;

            var expanded = ExpandMatchesToDescendantsAndSelf(matches, pathCache);
            if (IsBroadInheritedPositiveCondition(condition, resolved, comparison) &&
                expanded.Count > MaxInheritedBroadPositiveExpandedMatches)
            {
                throw new TooAmbiguousSearchException(
                    "The inherited-property defined/contains/wildcard search matched too many descendants. Use equals or a more specific source-file value.");
            }

            return expanded;
        }

        private static List<ModelItem> ExpandMatchesToDescendantsAndSelf(
            IEnumerable<ModelItem> matches,
            IDictionary<ModelItem, string> pathCache)
        {
            var expanded = new List<ModelItem>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in matches)
                CollectItems(item, expanded, pathCache, seenPaths);

            return expanded;
        }

        private static List<ModelItem> ExecuteManualConditionSearch(Document document, FindItemsCondition condition)
        {
            var started = Stopwatch.StartNew();
            var result = new List<ModelItem>();
            var comparison = NormalizeComparison(condition.Operator);
            var resolved = ResolveProperty(condition);

            foreach (ModelItem item in document.Models.RootItemDescendantsAndSelf)
            {
                if (started.ElapsedMilliseconds > MaxManualTraversalMilliseconds)
                {
                    throw new AgentCommandException(
                        ErrorCodes.CommandFailed,
                        "The requested find_items condition is too expensive for this model. Narrow the search or avoid not_defined on broad Item/Name scans.");
                }

                if (MatchesManualCondition(item, condition, resolved, comparison))
                    result.Add(item);
            }

            return result;
        }

        private static bool MatchesManualCondition(ModelItem item, FindItemsCondition condition)
        {
            var comparison = NormalizeComparison(condition.Operator);
            var resolved = ResolveProperty(condition);

            return MatchesManualCondition(item, condition, resolved, comparison);
        }

        private static bool MatchesManualCondition(ModelItem item, FindItemsCondition condition, ResolvedProperty resolved, string comparison)
        {
            var matched = MatchesManualConditionPositive(item, condition, resolved, comparison);
            return condition != null && condition.Negate.GetValueOrDefault(false) ? !matched : matched;
        }

        private static bool MatchesManualConditionPositive(ModelItem item, FindItemsCondition condition, ResolvedProperty resolved, string comparison)
        {
            if (resolved == null)
                resolved = ResolveProperty(condition);
            comparison = string.IsNullOrWhiteSpace(comparison) ? NormalizeComparison(condition.Operator) : comparison;

            if (resolved.IsDefaultItemNameTarget)
            {
                string itemName;
                var hasItemName = TryGetDefaultItemNameValue(item, out itemName);

                if (string.Equals(comparison, FindItemsComparisons.Defined, StringComparison.OrdinalIgnoreCase))
                    return hasItemName;

                if (string.Equals(comparison, FindItemsComparisons.NotDefined, StringComparison.OrdinalIgnoreCase))
                    return !hasItemName;

                if (!hasItemName)
                    return false;

                if (string.Equals(comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase))
                    return StringValueEquals(itemName, condition.Value, condition);

                if (string.Equals(comparison, FindItemsComparisons.NotEquals, StringComparison.OrdinalIgnoreCase))
                    return !StringValueEquals(itemName, condition.Value, condition);

                if (string.Equals(comparison, FindItemsComparisons.Contains, StringComparison.OrdinalIgnoreCase))
                    return StringValueContains(itemName, condition.Value, condition);

                if (string.Equals(comparison, FindItemsComparisons.StartsWith, StringComparison.OrdinalIgnoreCase))
                    return StringValueStartsWith(itemName, condition.Value, condition);

                if (string.Equals(comparison, FindItemsComparisons.EndsWith, StringComparison.OrdinalIgnoreCase))
                    return StringValueEndsWith(itemName, condition.Value, condition);

                if (string.Equals(comparison, FindItemsComparisons.Wildcard, StringComparison.OrdinalIgnoreCase))
                    return WildcardMatches(itemName, condition.Value, condition);
            }

            var property = TryFindProperty(item, resolved);

            if (string.Equals(comparison, FindItemsComparisons.Defined, StringComparison.OrdinalIgnoreCase))
                return property != null;

            if (string.Equals(comparison, FindItemsComparisons.NotDefined, StringComparison.OrdinalIgnoreCase))
                return property == null;

            if (property == null)
                return false;

            if (string.Equals(comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase))
                return PropertyValueEquals(property, condition);

            if (string.Equals(comparison, FindItemsComparisons.NotEquals, StringComparison.OrdinalIgnoreCase))
                return !PropertyValueEquals(property, condition);

            var value = GetPropertyDisplayValue(property);

            if (string.Equals(comparison, FindItemsComparisons.Contains, StringComparison.OrdinalIgnoreCase))
                return StringValueContains(value, condition.Value, condition);

            if (string.Equals(comparison, FindItemsComparisons.StartsWith, StringComparison.OrdinalIgnoreCase))
                return StringValueStartsWith(value, condition.Value, condition);

            if (string.Equals(comparison, FindItemsComparisons.EndsWith, StringComparison.OrdinalIgnoreCase))
                return StringValueEndsWith(value, condition.Value, condition);

            if (string.Equals(comparison, FindItemsComparisons.Wildcard, StringComparison.OrdinalIgnoreCase))
                return WildcardMatches(value, condition.Value, condition);

            throw new AgentCommandException(ErrorCodes.SchemaViolation, "Unsupported manual find_items operator: " + comparison);
        }

        private static bool TryGetDefaultItemNameValue(ModelItem item, out string value)
        {
            value = item == null ? string.Empty : item.DisplayName ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        private static DataProperty TryFindProperty(ModelItem item, ResolvedProperty resolved)
        {
            var current = item;
            while (current != null)
            {
                var property = TryFindPropertyOnItem(current, resolved);
                if (property != null)
                    return property;

                current = resolved.InheritFromAncestor ? current.Parent : null;
            }

            return null;
        }

        private static DataProperty TryFindPropertyOnItem(ModelItem item, ResolvedProperty resolved)
        {
            if (item == null || resolved == null)
                return null;

            foreach (var candidate in resolved.DisplayCandidates)
            {
                var displayProperty = TryFindDisplayPropertyCore(item, candidate.Category, candidate.Property);
                if (displayProperty != null)
                    return displayProperty;
            }

            if (!string.IsNullOrWhiteSpace(resolved.InternalProperty))
            {
                var internalProperty = TryFindInternalPropertyCore(item, resolved.InternalCategory, resolved.InternalProperty);
                if (internalProperty != null)
                    return internalProperty;
            }

            return null;
        }

        private static DataProperty TryFindDisplayPropertyCore(ModelItem item, string category, string property)
        {
            if (item == null || item.PropertyCategories == null || string.IsNullOrWhiteSpace(property))
                return null;

            if (string.IsNullOrWhiteSpace(category))
            {
                foreach (PropertyCategory propertyCategory in item.PropertyCategories)
                {
                    if (propertyCategory == null || propertyCategory.Properties == null)
                        continue;

                    var propertyInAnyCategory = propertyCategory.Properties.FindPropertyByDisplayName(property);
                    if (propertyInAnyCategory != null)
                        return propertyInAnyCategory;
                }

                return null;
            }

            return item.PropertyCategories.FindPropertyByDisplayName(category, property);
        }

        private static DataProperty TryFindInternalPropertyCore(ModelItem item, string category, string property)
        {
            if (item == null || item.PropertyCategories == null || string.IsNullOrWhiteSpace(property))
                return null;

            if (string.IsNullOrWhiteSpace(category))
            {
                foreach (PropertyCategory propertyCategory in item.PropertyCategories)
                {
                    if (propertyCategory == null || propertyCategory.Properties == null)
                        continue;

                    var propertyInAnyCategory = propertyCategory.Properties.FindPropertyByName(property);
                    if (propertyInAnyCategory != null)
                        return propertyInAnyCategory;
                }

                return null;
            }

            return item.PropertyCategories.FindPropertyByName(category, property);
        }

        private static string GetPropertyDisplayValue(DataProperty property)
        {
            if (property == null)
                return string.Empty;

            try
            {
                return property.Value == null ? string.Empty : property.Value.ToDisplayString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool PropertyValueEquals(DataProperty property, FindItemsCondition condition)
        {
            if (property == null)
                return false;

            var variant = property.Value;
            var dataType = NormalizeDataType(condition.DataType);

            if (string.IsNullOrEmpty(dataType))
            {
                int expectedInt;
                long actualIntegral;
                if (TryParseInt32(condition.Value, out expectedInt) &&
                    TryGetIntegralValue(variant, out actualIntegral))
                    return actualIntegral == expectedInt;

                double expectedDouble;
                double actualDouble;
                if (TryParseDouble(condition.Value, out expectedDouble) &&
                    TryGetDoubleValue(variant, out actualDouble))
                    return Math.Abs(actualDouble - expectedDouble) < 0.0000001d;

                bool expectedBool;
                bool actualBool;
                if (TryParseBoolean(condition.Value, out expectedBool) &&
                    TryGetBooleanValue(variant, out actualBool))
                    return actualBool == expectedBool;

                DateTime expectedDateTime;
                DateTime actualDateTime;
                if (TryParseDateTime(condition.Value, out expectedDateTime) &&
                    TryGetDateTimeValue(variant, out actualDateTime))
                    return actualDateTime == expectedDateTime;
            }

            if (string.Equals(dataType, "int32", StringComparison.Ordinal))
            {
                int expected;
                long actual;
                if (TryParseInt32(condition.Value, out expected) &&
                    TryGetIntegralValue(variant, out actual))
                    return actual == expected;

                bool matched;
                if (TryMatchDisplayNumericValue(GetPropertyDisplayValue(property), expected, out matched))
                    return matched;
            }
            else if (string.Equals(dataType, "double", StringComparison.Ordinal))
            {
                double expected;
                double actual;
                if (TryParseDouble(condition.Value, out expected) &&
                    TryGetDoubleValue(variant, out actual))
                    return Math.Abs(actual - expected) < 0.0000001d;

                bool matched;
                if (TryMatchDisplayNumericValue(GetPropertyDisplayValue(property), expected, out matched))
                    return matched;
            }
            else if (string.Equals(dataType, "bool", StringComparison.Ordinal))
            {
                bool expected;
                bool actual;
                if (TryParseBoolean(condition.Value, out expected) &&
                    TryGetBooleanValue(variant, out actual))
                    return actual == expected;
            }
            else if (string.Equals(dataType, "datetime", StringComparison.Ordinal))
            {
                DateTime expected;
                DateTime actual;
                if (TryParseDateTime(condition.Value, out expected) &&
                    TryGetDateTimeValue(variant, out actual))
                    return actual == expected;
            }

            if (string.IsNullOrEmpty(dataType))
            {
                int expectedInt;
                bool matchedInt;
                if (TryParseInt32(condition.Value, out expectedInt) &&
                    TryMatchDisplayNumericValue(GetPropertyDisplayValue(property), expectedInt, out matchedInt))
                    return matchedInt;

                double expectedDouble;
                bool matchedDouble;
                if (TryParseDouble(condition.Value, out expectedDouble) &&
                    TryMatchDisplayNumericValue(GetPropertyDisplayValue(property), expectedDouble, out matchedDouble))
                    return matchedDouble;
            }

            return StringValueEquals(GetPropertyDisplayValue(property), condition.Value, condition);
        }

        private static bool TryMatchDisplayNumericValue(string displayValue, int expected, out bool matched)
        {
            matched = false;

            double actual;
            if (!TryGetNumericValueFromDisplayString(displayValue, out actual))
                return false;

            matched = Math.Abs(actual - expected) < 0.0000001d;
            return true;
        }

        private static bool TryMatchDisplayNumericValue(string displayValue, double expected, out bool matched)
        {
            matched = false;

            double actual;
            if (!TryGetNumericValueFromDisplayString(displayValue, out actual))
                return false;

            matched = Math.Abs(actual - expected) < 0.0000001d;
            return true;
        }

        private static bool TryGetIntegralValue(VariantData value, out long result)
        {
            result = 0;
            if (value == null)
                return false;

            try
            {
                if (value.IsInt32)
                {
                    result = value.ToInt32();
                    return true;
                }

                long int64;
                if (TryGetVariantInt64(value, out int64))
                {
                    result = int64;
                    return true;
                }

                uint nat32;
                if (TryGetVariantNat32(value, out nat32))
                {
                    result = nat32;
                    return true;
                }

                ulong nat64;
                if (TryGetVariantNat64(value, out nat64))
                {
                    if (nat64 > long.MaxValue)
                        return false;

                    result = (long)nat64;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to convert Navisworks VariantData to integral value: " + ex.Message, "SearchMcp");
            }

            return false;
        }

        private static bool TryGetVariantInt64(VariantData value, out long result)
        {
            result = 0;
            object raw;
            if (!TryInvokeVariantBooleanProperty(value, "IsInt64") || !TryInvokeVariantMethod(value, "ToInt64", out raw) || raw == null)
                return false;

            try
            {
                result = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetVariantNat32(VariantData value, out uint result)
        {
            result = 0;
            object raw;
            if (!TryInvokeVariantBooleanProperty(value, "IsNat32") || !TryInvokeVariantMethod(value, "ToNat32", out raw) || raw == null)
                return false;

            try
            {
                result = Convert.ToUInt32(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetVariantNat64(VariantData value, out ulong result)
        {
            result = 0;
            object raw;
            if (!TryInvokeVariantBooleanProperty(value, "IsNat64") || !TryInvokeVariantMethod(value, "ToNat64", out raw) || raw == null)
                return false;

            try
            {
                result = Convert.ToUInt64(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInvokeVariantBooleanProperty(VariantData value, string propertyName)
        {
            if (value == null || string.IsNullOrWhiteSpace(propertyName))
                return false;

            try
            {
                var property = value.GetType().GetProperty(propertyName);
                if (property == null || property.PropertyType != typeof(bool))
                    return false;

                return (bool)property.GetValue(value, null);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInvokeVariantMethod(VariantData value, string methodName, out object result)
        {
            result = null;
            if (value == null || string.IsNullOrWhiteSpace(methodName))
                return false;

            try
            {
                var method = value.GetType().GetMethod(methodName, Type.EmptyTypes);
                if (method == null)
                    return false;

                result = method.Invoke(value, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetDoubleValue(VariantData value, out double result)
        {
            result = 0;
            if (value == null)
                return false;

            try
            {
                if (value.IsAnyDouble)
                {
                    result = value.ToAnyDouble();
                    return true;
                }

                long integral;
                if (TryGetIntegralValue(value, out integral))
                {
                    result = integral;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to convert Navisworks VariantData to double value: " + ex.Message, "SearchMcp");
            }

            return false;
        }

        private static bool TryGetBooleanValue(VariantData value, out bool result)
        {
            result = false;
            if (value == null)
                return false;

            try
            {
                if (!value.IsBoolean)
                    return false;

                result = value.ToBoolean();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetDateTimeValue(VariantData value, out DateTime result)
        {
            result = default(DateTime);
            if (value == null)
                return false;

            try
            {
                if (!value.IsDateTime)
                    return false;

                result = value.ToDateTime();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool WildcardMatches(string value, string pattern)
        {
            var safeValue = value ?? string.Empty;
            var safePattern = pattern ?? string.Empty;
            var regexPattern = "^" + Regex.Escape(safePattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            try
            {
                return Regex.IsMatch(safeValue, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
            }
            catch (RegexMatchTimeoutException ex)
            {
                Logger.Error("Wildcard match timed out for pattern '" + safePattern + "': " + ex.Message, "SearchMcp");
                return false;
            }
        }

        private static bool WildcardMatches(string value, string pattern, FindItemsCondition condition)
        {
            var safeValue = NormalizeConditionString(value, condition);
            var safePattern = NormalizeConditionString(pattern, condition);
            var regexPattern = "^" + Regex.Escape(safePattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            var options = condition != null && !condition.IgnoreCase.GetValueOrDefault(true)
                ? RegexOptions.CultureInvariant
                : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
            try
            {
                return Regex.IsMatch(safeValue, regexPattern, options, TimeSpan.FromSeconds(2));
            }
            catch (RegexMatchTimeoutException ex)
            {
                Logger.Error("Wildcard match timed out for pattern '" + safePattern + "': " + ex.Message, "SearchMcp");
                return false;
            }
        }

        private static bool StringValueEquals(string left, string right, FindItemsCondition condition)
        {
            return string.Equals(
                NormalizeConditionString(left, condition),
                NormalizeConditionString(right, condition),
                condition != null && !condition.IgnoreCase.GetValueOrDefault(true) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        private static bool StringValueContains(string value, string fragment, FindItemsCondition condition)
        {
            return NormalizeConditionString(value, condition).IndexOf(
                NormalizeConditionString(fragment, condition),
                condition != null && !condition.IgnoreCase.GetValueOrDefault(true) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool StringValueStartsWith(string value, string prefix, FindItemsCondition condition)
        {
            return FindItemsSearchRulesHelper.MatchesAnchoredText(
                value,
                prefix,
                FindItemsComparisons.StartsWith,
                condition == null || condition.IgnoreCase.GetValueOrDefault(true),
                condition != null && condition.IgnoreCharWidth.GetValueOrDefault(false),
                condition != null && condition.IgnoreDiacritics.GetValueOrDefault(false));
        }

        private static bool StringValueEndsWith(string value, string suffix, FindItemsCondition condition)
        {
            return FindItemsSearchRulesHelper.MatchesAnchoredText(
                value,
                suffix,
                FindItemsComparisons.EndsWith,
                condition == null || condition.IgnoreCase.GetValueOrDefault(true),
                condition != null && condition.IgnoreCharWidth.GetValueOrDefault(false),
                condition != null && condition.IgnoreDiacritics.GetValueOrDefault(false));
        }

        private static bool TryParseInt32(string value, out int result)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ||
                   int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out result);
        }

        private static bool TryParseDouble(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result) ||
                   double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out result);
        }

        private static bool TryGetNumericValueFromDisplayString(string value, out double result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim();
            if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result) ||
                double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out result))
                return true;

            var compact = trimmed.Replace(" ", string.Empty);
            if (double.TryParse(compact, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result) ||
                double.TryParse(compact, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out result))
                return true;

            var match = Regex.Match(trimmed, @"-?\d+(?:[.,]\d+)?");
            if (!match.Success)
                return false;

            var numericToken = match.Value;
            return double.TryParse(numericToken, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out result) ||
                   double.TryParse(numericToken, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result) ||
                   double.TryParse(numericToken.Replace(',', '.'), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryParseBoolean(string value, out bool result)
        {
            if (bool.TryParse(value, out result))
                return true;

            var normalized = NormalizeComparableText(value);
            if (normalized == "1" || normalized == "yes" || normalized == "y" || normalized == "true")
            {
                result = true;
                return true;
            }

            if (normalized == "0" || normalized == "no" || normalized == "n" || normalized == "false")
            {
                result = false;
                return true;
            }

            result = false;
            return false;
        }

        private static bool TryParseDateTime(string value, out DateTime result)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out result) ||
                   DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out result);
        }

        private static List<ModelItem> ExecuteSearchQuery(Document document, Search search, IDictionary<ModelItem, string> pathCache)
        {
            var matches = new List<ModelItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ModelItem item in search.FindAll(document, false))
            {
                var path = GetCachedPath(item, pathCache);
                if (seen.Add(path))
                    matches.Add(item);
            }

            return matches;
        }

        private static List<ModelItem> ExecuteSearchQuery(
            Document document,
            SearchCondition condition,
            IDictionary<ModelItem, string> pathCache)
        {
            return ExecuteSearchQuery(document, new[] { condition }, pathCache);
        }

        private static List<ModelItem> ExecuteSearchQuery(
            Document document,
            IEnumerable<SearchCondition> conditions,
            IDictionary<ModelItem, string> pathCache)
        {
            var search = new Search();
            search.Selection.SelectAll();
            search.Locations = SearchLocations.DescendantsAndSelf;
            foreach (var condition in conditions ?? Enumerable.Empty<SearchCondition>())
                search.SearchConditions.Add(condition);

            return ExecuteSearchQuery(document, search, pathCache);
        }

        private static List<ModelItem> MergeMatchesByPath(
            IEnumerable<ModelItem> primary,
            IEnumerable<ModelItem> secondary,
            IDictionary<ModelItem, string> pathCache)
        {
            var merged = new Dictionary<string, ModelItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in primary ?? Enumerable.Empty<ModelItem>())
                merged[GetCachedPath(item, pathCache)] = item;

            foreach (var item in secondary ?? Enumerable.Empty<ModelItem>())
                merged[GetCachedPath(item, pathCache)] = item;

            return merged.Values.ToList();
        }

        private static void CollectItems(
            ModelItem item,
            ICollection<ModelItem> items,
            IDictionary<ModelItem, string> pathCache,
            ISet<string> seenPaths)
        {
            if (item == null)
                return;

            var path = GetCachedPath(item, pathCache);
            if (seenPaths.Add(path))
                items.Add(item);

            foreach (ModelItem childItem in item.Children)
                CollectItems(childItem, items, pathCache, seenPaths);
        }

        private static void IntersectByPath(IDictionary<string, ModelItem> accumulator, IDictionary<string, ModelItem> current)
        {
            foreach (var key in accumulator.Keys.ToList())
            {
                if (!current.ContainsKey(key))
                    accumulator.Remove(key);
            }
        }

        private static void UnionByPath(IDictionary<string, ModelItem> accumulator, IDictionary<string, ModelItem> current)
        {
            foreach (var entry in current)
            {
                if (!accumulator.ContainsKey(entry.Key))
                    accumulator[entry.Key] = entry.Value;
            }
        }
    }
}
