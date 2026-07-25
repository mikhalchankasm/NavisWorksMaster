using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class SearchService
    {
        private const int MaxScopedTraversalMilliseconds = 45000;
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
            var matchedItems = countOnly ? null : new List<ModelItem>();
            var matchedSet = countOnly ? null : new HashSet<ModelItem>();
            var visited = new HashSet<ModelItem>();
            var sampleValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<ScopedSearchNode>();
            for (var index = roots.Count - 1; index >= 0; index--)
                stack.Push(new ScopedSearchNode(roots[index], GetModelItemDepth(roots[index])));

            while (stack.Count > 0)
            {
                if (started.ElapsedMilliseconds > MaxScopedTraversalMilliseconds)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Scoped find_items exceeded the 45 second traversal budget. Narrow the scope or use matchDepth=first/countOnly.");
                if (response.ScannedItemCount >= MaxScopedScannedItems)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Scoped find_items exceeded the 1,000,000 item traversal limit. Narrow the scope.");

                var node = stack.Pop();
                var item = node.Item;
                if (item == null || !visited.Add(item))
                    continue;
                response.ScannedItemCount++;

                var matched = MatchesSearchManually(item, search);
                if (matched)
                {
                    response.MatchedItemCount++;
                    if (!response.DepthHistogram.ContainsKey(node.Depth))
                        response.DepthHistogram[node.Depth] = 0;
                    response.DepthHistogram[node.Depth]++;

                    if (!countOnly && matchedSet.Add(item))
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

                var children = item.Children == null
                    ? new List<ModelItem>()
                    : item.Children.Cast<ModelItem>().Where(child => child != null).ToList();
                for (var childIndex = children.Count - 1; childIndex >= 0; childIndex--)
                    stack.Push(new ScopedSearchNode(children[childIndex], node.Depth + 1));
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
            if (search == null || search.Conditions == null || search.Conditions.Count == 0)
                return false;

            var hasConditionLogic = search.Conditions.Skip(1).Any(condition =>
                condition != null && string.Equals(
                    condition.LogicalOperator,
                    FindItemsConditionOptionsHelper.Or,
                    StringComparison.OrdinalIgnoreCase));
            if (!hasConditionLogic)
            {
                var matchAll = string.Equals(search.CombineOperator, FindItemsCombineOperators.All, StringComparison.OrdinalIgnoreCase);
                return matchAll
                    ? search.Conditions.All(condition => MatchesManualCondition(item, condition))
                    : search.Conditions.Any(condition => MatchesManualCondition(item, condition));
            }

            var anyGroupMatched = false;
            var currentGroupMatched = MatchesManualCondition(item, search.Conditions[0]);
            for (var index = 1; index < search.Conditions.Count; index++)
            {
                var condition = search.Conditions[index];
                if (string.Equals(condition.LogicalOperator, FindItemsConditionOptionsHelper.Or, StringComparison.OrdinalIgnoreCase))
                {
                    anyGroupMatched |= currentGroupMatched;
                    currentGroupMatched = MatchesManualCondition(item, condition);
                }
                else
                {
                    currentGroupMatched &= MatchesManualCondition(item, condition);
                }
            }
            return anyGroupMatched || currentGroupMatched;
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
