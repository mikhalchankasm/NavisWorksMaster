using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class SearchService
    {
        // One number for both paths: the native fast path enforces the same
        // budget between its variants.
        private const int MaxScopedTraversalMilliseconds =
            FindItemsNativeScopedPolicy.TraversalBudgetMilliseconds;
        private const int MaxScopedScannedItems = 1000000;
        private const int MaxSearchSampleValues = 10;

        private FindItemsResponse ExecuteScopedFindItems(
            Document document,
            FindItemsRequest request,
            FindItemsSearch search,
            int previewLimit,
            MatchSessionStore sessionStore)
        {
            var scope = NormalizeFindItemsScope(request.Scope);
            var matchDepth = NormalizeFindItemsMatchDepth(request.MatchDepth);
            var countOnly = request.CountOnly.GetValueOrDefault(false);
            var roots = ResolveFindItemsScopeRoots(document, request, scope, sessionStore);
            var response = new FindItemsResponse
            {
                Scope = scope,
                MatchDepth = matchDepth,
                CountOnly = countOnly,
            };
            AddSearchRiskWarnings(search, scope, response.Warnings);

            EnsureSearchIsSafeToExecute(search);
            var started = Stopwatch.StartNew();

            List<ModelItem> nativeMatches;
            if (ShouldTryNativeScopedSearch(search, scope, matchDepth, countOnly) &&
                TryExecuteNativeScopedSearch(document, search, roots, started, out nativeMatches))
            {
                return BuildNativeScopedResponse(response, search, nativeMatches, previewLimit, sessionStore);
            }

            var preparedSearch = PrepareManualSearch(search);
            var matchedItems = countOnly ? null : new List<ModelItem>();
            var sampleValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<ScopedSearchNode>();
            var scopeRoots = new HashSet<ModelItem>(roots);
            // Scope roots are disjoint, and each child is pushed once by its unique parent.
            for (var index = roots.Count - 1; index >= 0; index--)
                stack.Push(new ScopedSearchNode(roots[index], GetModelItemDepth(roots[index])));

            // TECH-W13 live probe: Navisworks hands out a fresh managed ModelItem
            // wrapper on every access, so every wrapper this walk pops is solely
            // its own and can be released as soon as its iteration is fully
            // processed (children pushed, or the matchDepth=first continue)
            // instead of waiting for its finalizer. The previous iteration's
            // wrapper is released at the top of the next iteration and after the
            // loop; scope roots and items kept in matchedItems outlive the walk
            // and are never disposed here.
            ModelItem previousItem = null;
            while (stack.Count > 0)
            {
                if (previousItem != null && IsWalkOwnedWrapper(previousItem, scopeRoots, matchedItems))
                {
                    try { previousItem.Dispose(); }
                    catch { }
                }
                previousItem = stack.Peek().Item;
                if (started.ElapsedMilliseconds > MaxScopedTraversalMilliseconds)
                    throw AbandonScopedTraversal(response.ScannedItemCount, matchedItems, stack,
                        "Scoped find_items exceeded the 45 second traversal budget. Narrow the scope or use matchDepth=first/countOnly.");
                if (response.ScannedItemCount >= MaxScopedScannedItems)
                    throw AbandonScopedTraversal(response.ScannedItemCount, matchedItems, stack,
                        "Scoped find_items exceeded the 1,000,000 item traversal limit. Narrow the scope.");

                var node = stack.Pop();
                var item = node.Item;
                if (item == null)
                    continue;
                response.ScannedItemCount++;

                var matched = MatchesSearchManually(item, preparedSearch);
                if (matched)
                {
                    response.MatchedItemCount++;
                    if (!response.DepthHistogram.ContainsKey(node.Depth))
                        response.DepthHistogram[node.Depth] = 0;
                    response.DepthHistogram[node.Depth]++;

                    if (!countOnly)
                        matchedItems.Add(item);
                    if (sampleValues.Count < MaxSearchSampleValues)
                    {
                        var sample = ReadSearchSampleValue(item, search);
                        if (!string.IsNullOrWhiteSpace(sample))
                            sampleValues.Add(sample);
                    }

                    if (string.Equals(matchDepth, FindItemsMatchDepths.First, StringComparison.Ordinal))
                        continue;
                }

                var itemChildren = item.Children;
                var children = itemChildren == null
                    ? new List<ModelItem>()
                    : itemChildren.Cast<ModelItem>().Where(child => child != null).ToList();
                if (itemChildren is IDisposable disposableChildren)
                {
                    try { disposableChildren.Dispose(); }
                    catch { }
                }
                for (var childIndex = children.Count - 1; childIndex >= 0; childIndex--)
                    stack.Push(new ScopedSearchNode(children[childIndex], node.Depth + 1));
            }

            if (previousItem != null && IsWalkOwnedWrapper(previousItem, scopeRoots, matchedItems))
            {
                try { previousItem.Dispose(); }
                catch { }
            }
            response.SampleValuesFromModel = sampleValues.ToList();
            var result = new FindItemsResult
            {
                Query = search.Query,
                Status = response.MatchedItemCount == 0 ? FindItemStatuses.NotFound : FindItemStatuses.Matched,
            };
            if (response.MatchedItemCount > 0)
            {
                response.Summary.MatchedQueries = 1;
                response.Summary.TotalItemsInMatches = response.MatchedItemCount;
                if (!countOnly)
                {
                    result.Matches.Add(new FindItemsMatch
                    {
                        MatchHandle = sessionStore.Add(matchedItems),
                        ItemCount = matchedItems.Count,
                        Preview = matchedItems.Take(previewLimit).Select(BuildPreviewItem).ToList(),
                        PreviewTruncated = matchedItems.Count > previewLimit,
                    });
                }
            }
            else
            {
                response.Summary.NotFoundQueries = 1;
            }
            response.Results.Add(result);
            return response;
        }

        private static bool IsWalkOwnedWrapper(
            ModelItem item,
            HashSet<ModelItem> scopeRoots,
            List<ModelItem> matchedItems)
        {
            if (item == null || scopeRoots.Contains(item))
                return false;
            // A kept item was added to matchedItems in its own iteration, so at
            // release time it is always the list's last entry.
            return matchedItems == null || matchedItems.Count == 0 ||
                !ReferenceEquals(matchedItems[matchedItems.Count - 1], item);
        }

        private static FindItemsResponse BuildFindItemsPreflight(
            Document document,
            FindItemsRequest request,
            FindItemsSearch search)
        {
            var hasSelection = document != null &&
                               document.CurrentSelection != null &&
                               document.CurrentSelection.SelectedItems != null &&
                               document.CurrentSelection.SelectedItems.Count > 0;
            var explicitScope = !string.IsNullOrWhiteSpace(request.Scope);
            var explicitDepth = !string.IsNullOrWhiteSpace(request.MatchDepth);
            var condition = search?.Conditions?.FirstOrDefault();
            var property = condition == null ? DefaultProperty : condition.Property;
            var comparison = condition == null ? FindItemsComparisons.Contains : NormalizeComparison(condition.Operator);
            var preflight = new FindItemsPreflight
            {
                Ready = explicitScope && explicitDepth,
                HasCurrentSelection = hasSelection,
                UnderstoodScope = explicitScope
                    ? NormalizeFindItemsScope(request.Scope)
                    : hasSelection ? FindItemsScopes.CurrentSelection : FindItemsScopes.WholeModel,
                UnderstoodProperty = string.IsNullOrWhiteSpace(property) ? DefaultProperty : property,
                UnderstoodComparison = comparison,
                UnderstoodMatchDepth = explicitDepth
                    ? NormalizeFindItemsMatchDepth(request.MatchDepth)
                    : IsNameContainsOrWildcard(condition) ? FindItemsMatchDepths.First : FindItemsMatchDepths.All,
            };
            preflight.Clarifications.Add(new FindItemsClarification
            {
                Id = "scope",
                Question = "Искать во всей модели или внутри текущего выделения/конкретного узла?",
                SuggestedAnswer = preflight.UnderstoodScope,
                Options = new List<string> { FindItemsScopes.CurrentSelection, FindItemsScopes.UnderHandle, FindItemsScopes.UnderNamedNode, FindItemsScopes.WholeModel },
            });
            preflight.Clarifications.Add(new FindItemsClarification
            {
                Id = "property",
                Question = "Искать по имени Item/Name или по конкретному свойству модели?",
                SuggestedAnswer = preflight.UnderstoodProperty,
                Options = new List<string> { "Name", "Type", "custom property" },
            });
            preflight.Clarifications.Add(new FindItemsClarification
            {
                Id = "comparison",
                Question = "Как сравнивать значение?",
                SuggestedAnswer = preflight.UnderstoodComparison,
                Options = new List<string> { FindItemsComparisons.Contains, FindItemsComparisons.StartsWith, FindItemsComparisons.Equal, FindItemsComparisons.Wildcard },
            });
            preflight.Clarifications.Add(new FindItemsClarification
            {
                Id = "match_depth",
                Question = "Вернуть только первое верхнее совпадение на ветке или все вложенные совпадения?",
                SuggestedAnswer = preflight.UnderstoodMatchDepth,
                Options = new List<string> { FindItemsMatchDepths.First, FindItemsMatchDepths.All },
            });
            preflight.Clarifications.Add(new FindItemsClarification
            {
                Id = "text_options",
                Question = "Игнорировать регистр, диакритику и ширину символов?",
                SuggestedAnswer = "ignoreCase=true",
                Options = new List<string> { "ignoreCase=true", "exact text options" },
            });
            preflight.Clarifications.Add(new FindItemsClarification
            {
                Id = "homoglyphs",
                Question = "Если запрос содержит кириллицу или похожие латинские буквы, подтвердить значение по образцу из модели?",
                SuggestedAnswer = "confirm model sample",
                Options = new List<string> { "confirm model sample", "use input literally" },
            });

            var warnings = new List<string>();
            AddSearchRiskWarnings(search, preflight.UnderstoodScope, warnings);
            return new FindItemsResponse
            {
                Scope = preflight.UnderstoodScope,
                MatchDepth = preflight.UnderstoodMatchDepth,
                Preflight = preflight,
                Warnings = warnings,
            };
        }

        private static bool MatchesSearchManually(ModelItem item, FindItemsSearch search)
        {
            return MatchesSearchManually(item, PrepareManualSearch(search));
        }

        private static bool MatchesSearchManually(ModelItem item, PreparedManualSearch prepared)
        {
            if (prepared == null)
                return false;
            if (prepared.EqualsValues != null)
            {
                // Same name source as the per-condition path, so the two can never disagree.
                string name;
                return TryGetDefaultItemNameValue(item, out name) &&
                       prepared.EqualsValues.Contains(NormalizeConditionString(name, prepared.EqualsReference));
            }

            return prepared.Groups.Any(group => group.All(entry =>
                MatchesManualCondition(item, entry.Condition, entry.Resolved, entry.Comparison)));
        }

        private static PreparedManualSearch PrepareManualSearch(FindItemsSearch search)
        {
            if (search == null || search.Conditions == null || search.Conditions.Count == 0)
                return null;

            var groups = FindItemsConditionGroups.Split(search.Conditions);
            if (groups.Count == 1 &&
                !string.Equals(search.CombineOperator, FindItemsCombineOperators.All, StringComparison.OrdinalIgnoreCase))
                groups = search.Conditions.Select((condition, index) => new List<int> { index }).ToList();

            var preparedGroups = groups
                .Select(group => group.Select(index => PrepareManualCondition(search.Conditions[index])).ToList())
                .ToList();
            var equalsValues = TryBuildEqualsFastPath(preparedGroups);
            return new PreparedManualSearch(
                preparedGroups,
                equalsValues,
                equalsValues == null ? null : preparedGroups[0][0].Condition);
        }

        private static (FindItemsCondition Condition, ResolvedProperty Resolved, string Comparison) PrepareManualCondition(FindItemsCondition condition)
        {
            return (condition, ResolveProperty(condition), NormalizeComparison(condition.Operator));
        }

        private static HashSet<string> TryBuildEqualsFastPath(
            List<List<(FindItemsCondition Condition, ResolvedProperty Resolved, string Comparison)>> groups)
        {
            var first = groups[0][0].Condition;
            var ignoreCase = first.IgnoreCase.GetValueOrDefault(true);
            var ignoreCharWidth = first.IgnoreCharWidth.GetValueOrDefault(false);
            var ignoreDiacritics = first.IgnoreDiacritics.GetValueOrDefault(false);
            var values = new HashSet<string>(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var group in groups)
            {
                if (group.Count != 1)
                    return null;

                var entry = group[0];
                if (!string.Equals(entry.Comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase) ||
                    entry.Condition.Negate.GetValueOrDefault(false) ||
                    entry.Condition.IgnoreCase.GetValueOrDefault(true) != ignoreCase ||
                    entry.Condition.IgnoreCharWidth.GetValueOrDefault(false) != ignoreCharWidth ||
                    entry.Condition.IgnoreDiacritics.GetValueOrDefault(false) != ignoreDiacritics ||
                    entry.Resolved == null ||
                    !entry.Resolved.IsDefaultItemNameTarget)
                    return null;

                values.Add(NormalizeConditionString(entry.Condition.Value, entry.Condition));
            }

            return values;
        }

        private sealed class PreparedManualSearch
        {
            public PreparedManualSearch(
                List<List<(FindItemsCondition Condition, ResolvedProperty Resolved, string Comparison)>> groups,
                HashSet<string> equalsValues,
                FindItemsCondition equalsReference)
            {
                Groups = groups;
                EqualsValues = equalsValues;
                EqualsReference = equalsReference;
            }

            public List<List<(FindItemsCondition Condition, ResolvedProperty Resolved, string Comparison)>> Groups { get; private set; }
            public HashSet<string> EqualsValues { get; private set; }
            public FindItemsCondition EqualsReference { get; private set; }
        }

        private static List<ModelItem> ResolveFindItemsScopeRoots(
            Document document,
            FindItemsRequest request,
            string scope,
            MatchSessionStore sessionStore)
        {
            var roots = new List<ModelItem>();
            if (scope == FindItemsScopes.WholeModel)
            {
                if (document?.Models != null)
                {
                    foreach (Model model in document.Models)
                    {
                        if (model?.RootItem != null)
                            roots.Add(model.RootItem);
                    }
                }
                return roots;
            }

            if (scope == FindItemsScopes.CurrentSelection)
            {
                if (document?.CurrentSelection?.SelectedItems != null)
                    roots.AddRange(document.CurrentSelection.SelectedItems.Cast<ModelItem>().Where(item => item != null));
                if (roots.Count == 0)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "scope=current_selection requires a non-empty current selection.");
                return RemoveNestedScopeRoots(roots);
            }

            if (scope == FindItemsScopes.UnderHandle)
            {
                var handle = (request.ScopeHandle ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(handle))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "scopeHandle is required for scope=under_handle.");
                IList<ModelItem> items;
                if (!sessionStore.TryGet(handle, out items) || items == null || items.Count == 0)
                    throw new AgentCommandException(ErrorCodes.StaleMatchReference, "scopeHandle is stale or was not found. Re-run find_items/list_item_children.");
                roots.AddRange(items.Where(item => item != null));
                return RemoveNestedScopeRoots(roots);
            }

            var path = (request.ScopeNodePath ?? string.Empty).Trim();
            var name = (request.ScopeNodeName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(path) && string.IsNullOrWhiteSpace(name))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "scopeNodePath or scopeNodeName is required for scope=under_named_node.");

            if (!string.IsNullOrWhiteSpace(path))
                roots = ResolveListChildrenParentsByPath(document, path);
            else
                roots = FindNamedScopeNodes(document, name);
            if (roots.Count != 1)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Named search scope must resolve to exactly one node; resolved " + roots.Count.ToString(CultureInfo.InvariantCulture) + ".");
            return roots;
        }

        private static List<ModelItem> FindNamedScopeNodes(Document document, string name)
        {
            var result = new List<ModelItem>();
            var started = Stopwatch.StartNew();
            if (document?.Models == null)
                return result;
            foreach (ModelItem item in document.Models.RootItemDescendantsAndSelf)
            {
                if (started.ElapsedMilliseconds > 10000)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Resolving scopeNodeName exceeded 10 seconds. Use scopeNodePath or scopeHandle.");
                if (item != null && string.Equals(item.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(item);
                    if (result.Count > 1)
                        break;
                }
            }
            return result;
        }

        private static List<ModelItem> RemoveNestedScopeRoots(IEnumerable<ModelItem> candidates)
        {
            var roots = candidates.Where(item => item != null).Distinct().ToList();
            var rootSet = new HashSet<ModelItem>(roots);
            return roots.Where(candidate =>
            {
                var current = candidate.Parent;
                while (current != null)
                {
                    if (rootSet.Contains(current))
                        return false;
                    current = current.Parent;
                }
                return true;
            }).ToList();
        }

        private static int GetModelItemDepth(ModelItem item)
        {
            var depth = 0;
            var current = item?.Parent;
            while (current != null)
            {
                depth++;
                current = current.Parent;
            }
            return depth;
        }

        private static string NormalizeFindItemsScope(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? FindItemsScopes.WholeModel : value.Trim().ToLowerInvariant();
            if (normalized == FindItemsScopes.WholeModel ||
                normalized == FindItemsScopes.CurrentSelection ||
                normalized == FindItemsScopes.UnderHandle ||
                normalized == FindItemsScopes.UnderNamedNode)
                return normalized;
            throw new AgentCommandException(ErrorCodes.SchemaViolation, "scope must be whole_model, current_selection, under_handle, or under_named_node.");
        }

        private static string NormalizeFindItemsMatchDepth(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? FindItemsMatchDepths.All : value.Trim().ToLowerInvariant();
            if (normalized == FindItemsMatchDepths.First || normalized == FindItemsMatchDepths.All)
                return normalized;
            throw new AgentCommandException(ErrorCodes.SchemaViolation, "matchDepth must be first or all.");
        }

        private static string ReadSearchSampleValue(ModelItem item, FindItemsSearch search)
        {
            var condition = search?.Conditions?.FirstOrDefault();
            if (condition == null)
                return item?.DisplayName ?? string.Empty;
            var resolved = ResolveProperty(condition);
            if (resolved.IsDefaultItemNameTarget)
                return item?.DisplayName ?? string.Empty;
            return GetPropertyDisplayValue(TryFindProperty(item, resolved));
        }

        private static void AddSearchRiskWarnings(FindItemsSearch search, string scope, ICollection<string> warnings)
        {
            foreach (var warning in BuildSearchTextWarnings(search))
                warnings.Add(warning);
            if (search?.Conditions?.Any(IsExpensiveSourceFileCondition) == true)
            {
                warnings.Add(
                    scope == FindItemsScopes.WholeModel
                        ? "Source File/inherited-property search can be expensive. Prefer scopeNodePath, scopeHandle, or a root/path filter."
                        : "Source File/inherited-property values are resolved per scoped item; keep the scope narrow.");
            }
        }

        private static List<string> BuildSearchTextWarnings(FindItemsSearch search)
        {
            var warnings = new List<string>();
            var values = search?.Conditions == null
                ? new List<string>()
                : search.Conditions.Select(condition => condition?.Value ?? string.Empty).ToList();
            if (values.Any(ContainsMixedScripts))
                warnings.Add("The query mixes Cyrillic and Latin characters. Confirm sampleValuesFromModel before relying on exact text matching.");
            else if (values.Any(ContainsConfusableLatinLetters))
                warnings.Add("The query contains Latin letters with common Cyrillic homoglyphs (A/B/C/E/H/K/M/O/P/T/X/Y). If the model uses Cyrillic, confirm a value sampled from the model.");
            return warnings;
        }

        private static bool IsExpensiveSourceFileCondition(FindItemsCondition condition)
        {
            var property = (condition?.Property ?? string.Empty).Trim();
            return condition?.InheritFromAncestor.GetValueOrDefault(false) == true ||
                   property.Equals("Source File", StringComparison.OrdinalIgnoreCase) ||
                   property.Equals("Файл источника", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsMixedScripts(string value)
        {
            var hasLatin = (value ?? string.Empty).Any(character =>
                (character >= 'A' && character <= 'Z') ||
                (character >= 'a' && character <= 'z'));
            var hasCyrillic = (value ?? string.Empty).Any(character => character >= '\u0400' && character <= '\u04FF');
            return hasLatin && hasCyrillic;
        }

        private static bool ContainsConfusableLatinLetters(string value)
        {
            const string confusables = "ABCEHKMOPTXYabcehkmoptxy";
            return (value ?? string.Empty).Any(confusables.Contains);
        }

        private static bool IsNameContainsOrWildcard(FindItemsCondition condition)
        {
            if (condition == null)
                return false;
            var property = string.IsNullOrWhiteSpace(condition.Property) ? DefaultProperty : condition.Property;
            var comparison = NormalizeComparison(condition.Operator);
            return property.Equals(DefaultProperty, StringComparison.OrdinalIgnoreCase) &&
                   (comparison == FindItemsComparisons.Contains || comparison == FindItemsComparisons.Wildcard);
        }

        /// <summary>
        /// Releases an abandoned traversal before reporting it, and returns the
        /// exception for the caller to throw.
        ///
        /// A traversal that hits its budget on a large model has materialized up to
        /// MaxScopedScannedItems ModelItem wrappers. The matched items and the pending
        /// stack keep some of them reachable, and a loaded heap makes every later
        /// search in the session dramatically slower. Measured live on 6501.5.nwd,
        /// when a `visited` set still held every scanned item: a whole-model search
        /// returning 3616 matches took 554 ms in a fresh process and 7428 ms
        /// immediately after one budget-exceeded traversal, with the whole
        /// difference in path building rather than in the engine. Dropping the
        /// references and collecting here stops one failed call from degrading the
        /// calls after it. The cost is paid only on a path that has already spent
        /// its entire budget.
        /// </summary>
        private static AgentCommandException AbandonScopedTraversal(
            int abandoned,
            List<ModelItem> matchedItems,
            Stack<ScopedSearchNode> stack,
            string message)
        {
            if (matchedItems != null)
                matchedItems.Clear();
            if (stack != null)
                stack.Clear();

            return ReleaseAbandonedItems("scoped_traversal_abandoned", abandoned, message);
        }

        /// <summary>
        /// Same invariant for the native scoped path: it abandons whatever the
        /// engine has already handed back across earlier variants, so those
        /// wrappers must be released before the failure is reported. Fewer items
        /// than a manual traversal holds, but the rule does not depend on the
        /// count, and a wide matchDepth=first scope can still accumulate
        /// thousands.
        /// </summary>
        private static AgentCommandException AbandonNativeScopedSearch(
            FindItemsMatchSet<ModelItem> found,
            string message)
        {
            var abandoned = found == null ? 0 : found.Count;
            if (found != null)
                found.Clear();

            return ReleaseAbandonedItems("scoped_native_abandoned", abandoned, message);
        }

        private static AgentCommandException ReleaseAbandonedItems(
            string logEvent,
            int abandonedItems,
            string message)
        {
            var releaseStarted = Stopwatch.StartNew();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            releaseStarted.Stop();

            Logger.Info(
                "find_items " + logEvent + " abandoned_items=" + abandonedItems +
                " release_ms=" + releaseStarted.ElapsedMilliseconds,
                "AgentHost");

            return new AgentCommandException(ErrorCodes.CommandFailed, message);
        }

        private static bool ShouldTryNativeScopedSearch(
            FindItemsSearch search,
            string scope,
            string matchDepth,
            bool countOnly)
        {
            if (FindItemsNativeScopedPolicy.IsDisabledByEnvironment(
                    Environment.GetEnvironmentVariable(FindItemsNativeScopedPolicy.DisableEnvironmentVariable)))
            {
                return false;
            }

            return FindItemsNativeScopedPolicy.IsEligible(search, scope, matchDepth, countOnly);
        }

        /// <summary>
        /// Fills the scoped response shape from a native result. scannedItemCount
        /// stays 0 because the engine reports matches, not how many nodes it
        /// walked; countOnly requests, where that number is the answer, never
        /// reach this path.
        /// </summary>
        private static FindItemsResponse BuildNativeScopedResponse(
            FindItemsResponse response,
            FindItemsSearch search,
            List<ModelItem> matchedItems,
            int previewLimit,
            MatchSessionStore sessionStore)
        {
            var sampleValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in matchedItems)
            {
                var depth = GetModelItemDepth(item);
                if (!response.DepthHistogram.ContainsKey(depth))
                    response.DepthHistogram[depth] = 0;
                response.DepthHistogram[depth]++;

                if (sampleValues.Count < MaxSearchSampleValues)
                {
                    var sample = ReadSearchSampleValue(item, search);
                    if (!string.IsNullOrWhiteSpace(sample))
                        sampleValues.Add(sample);
                }
            }

            response.MatchedItemCount = matchedItems.Count;
            response.SampleValuesFromModel = sampleValues.ToList();

            var result = new FindItemsResult
            {
                Query = search.Query,
                Status = matchedItems.Count == 0 ? FindItemStatuses.NotFound : FindItemStatuses.Matched,
            };
            if (matchedItems.Count > 0)
            {
                response.Summary.MatchedQueries = 1;
                response.Summary.TotalItemsInMatches = matchedItems.Count;
                result.Matches.Add(new FindItemsMatch
                {
                    MatchHandle = sessionStore.Add(matchedItems),
                    ItemCount = matchedItems.Count,
                    Preview = matchedItems.Take(previewLimit).Select(BuildPreviewItem).ToList(),
                    PreviewTruncated = matchedItems.Count > previewLimit,
                });
            }
            else
            {
                response.Summary.NotFoundQueries = 1;
            }

            response.Results.Add(result);
            return response;
        }

        private static bool RequiresLiteralAnchorTraversal(FindItemsSearch search)
        {
            return search?.Conditions?.Any(condition =>
            {
                var comparison = NormalizeComparison(condition?.Operator);
                return comparison == FindItemsComparisons.StartsWith ||
                       comparison == FindItemsComparisons.EndsWith;
            }) == true;
        }

        private sealed class ScopedSearchNode
        {
            public ScopedSearchNode(ModelItem item, int depth)
            {
                Item = item;
                Depth = depth;
            }

            public ModelItem Item { get; private set; }
            public int Depth { get; private set; }
        }
    }
}
