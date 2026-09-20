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
        public void InvalidateRootSearchIndex()
        {
            lock (_rootSearchIndexLock)
            {
                _rootSearchIndex = null;
            }
        }

        public FindItemsResponse FindItems(Document document, FindItemsRequest request, MatchSessionStore sessionStore, int requestTimeoutMs = 0)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (sessionStore == null)
                throw new ArgumentNullException(nameof(sessionStore));

            var previewLimit = ClampPreviewLimit(request.PreviewLimit);
            var searches = NormalizeSearches(request);
            if (searches.Count > MaxQueries)
                throw new AgentCommandException(
                    ErrorCodes.SchemaViolation,
                    "The find_items request exceeds the maximum of one search/query per call. Split batches into separate find_items calls; do not concatenate part files.");

            if (request.Preflight.GetValueOrDefault(false))
                return BuildFindItemsPreflight(document, request, searches.FirstOrDefault());
            var requestStarted = Stopwatch.StartNew();
            var nextSearchDeadlineMs = GetNextSearchDeadlineMilliseconds(requestTimeoutMs);

            var scope = NormalizeFindItemsScope(request.Scope);
            var matchDepth = NormalizeFindItemsMatchDepth(request.MatchDepth);
            var countOnly = request.CountOnly.GetValueOrDefault(false);
            if (scope != FindItemsScopes.WholeModel ||
                matchDepth != FindItemsMatchDepths.All ||
                searches.Any(RequiresLiteralAnchorTraversal))
            {
                if (searches.Count != 1)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Scoped/countOnly find_items requires exactly one query/search.");
                return ExecuteScopedFindItems(document, request, searches[0], previewLimit, sessionStore);
            }

            var response = new FindItemsResponse
            {
                Scope = scope,
                MatchDepth = matchDepth,
                CountOnly = countOnly,
            };

            var anyMatchHasChildren = false;
            var countOnlyMatches = new List<ModelItem>();
            foreach (var search in searches)
            {
                EnsureCanStartNextSearch(requestStarted, nextSearchDeadlineMs);

                bool searchMatchHasChildren;
                List<ModelItem> searchMatches;
                var result = FindSingle(
                    document, search, previewLimit, sessionStore, countOnly,
                    out searchMatchHasChildren, out searchMatches);
                anyMatchHasChildren |= searchMatchHasChildren;
                if (countOnly && searchMatches != null)
                    countOnlyMatches.AddRange(searchMatches);
                response.Results.Add(result);

                if (string.Equals(result.Status, FindItemStatuses.Matched, StringComparison.OrdinalIgnoreCase))
                {
                    response.Summary.MatchedQueries++;
                }
                else if (string.Equals(result.Status, FindItemStatuses.Ambiguous, StringComparison.OrdinalIgnoreCase))
                {
                    response.Summary.AmbiguousQueries++;
                }
                else if (string.Equals(result.Status, FindItemStatuses.QueryTooAmbiguous, StringComparison.OrdinalIgnoreCase))
                {
                    response.Summary.QueryTooAmbiguousQueries++;
                }
                else
                {
                    response.Summary.NotFoundQueries++;
                }

                // countOnly registers no match, so the count comes from the items
                // themselves rather than from a handle's ItemCount.
                response.Summary.TotalItemsInMatches += countOnly
                    ? (searchMatches == null ? 0 : searchMatches.Count)
                    : result.Matches.Sum(m => m.ItemCount);
            }

            response.MatchedItemCount = response.Summary.TotalItemsInMatches;

            if (countOnly)
                AddWholeModelCountOnlyStatistics(response, searches, countOnlyMatches);

            // This branch is the pruned native search. Say so whenever pruning
            // could have discarded nested matches, so whole-model and scoped
            // matchDepth=all never disagree silently.
            var pruningWarning = FindItemsNativeSearchPolicy.BuildPrunedWholeModelWarning(anyMatchHasChildren);
            if (!string.IsNullOrEmpty(pruningWarning))
                response.Warnings.Add(pruningWarning);

            return response;
        }

        /// <summary>
        /// Fills the statistics a countOnly caller asked for, from the native
        /// result: an exact match count, the depth distribution of those matches,
        /// and sample values as they appear in the model.
        ///
        /// scannedItemCount stays 0, because the engine reports matches rather than
        /// how many nodes it walked. The warning says so, so that a zero is not
        /// read as "nothing was scanned". That is the whole trade: this call used to
        /// traverse in order to report a scanned total, and on a model of any size
        /// it exceeded its 45 second budget instead of answering at all.
        /// </summary>
        private static void AddWholeModelCountOnlyStatistics(
            FindItemsResponse response,
            IList<FindItemsSearch> searches,
            IList<ModelItem> matches)
        {
            var search = searches == null || searches.Count == 0 ? null : searches[0];
            var sampleValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in matches ?? new List<ModelItem>())
            {
                if (item == null)
                    continue;

                var depth = GetModelItemDepth(item);
                if (!response.DepthHistogram.ContainsKey(depth))
                    response.DepthHistogram[depth] = 0;
                response.DepthHistogram[depth]++;

                if (search != null && sampleValues.Count < MaxSearchSampleValues)
                {
                    var sample = ReadSearchSampleValue(item, search);
                    if (!string.IsNullOrWhiteSpace(sample))
                        sampleValues.Add(sample);
                }
            }

            response.SampleValuesFromModel = sampleValues.ToList();
            response.Warnings.Add(FindItemsNativeSearchPolicy.WholeModelCountOnlyWarning);
        }

        public SelectBySearchResponse SelectBySearch(Document document, SelectBySearchRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            request = request ?? new SelectBySearchRequest();

            var scope = string.IsNullOrWhiteSpace(request.Scope)
                ? SelectBySearchScopes.WholeModel
                : request.Scope.Trim().ToLowerInvariant();
            if (scope != SelectBySearchScopes.WholeModel &&
                scope != SelectBySearchScopes.DirectChildrenOf &&
                scope != SelectBySearchScopes.DescendantsOf)
            {
                throw new AgentCommandException(
                    ErrorCodes.SchemaViolation,
                    "scope must be whole_model, direct_children_of, or descendants_of.");
            }

            var conditions = request.Conditions ?? new List<FindItemsCondition>();
            if (conditions.Count == 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "conditions must contain at least one search condition.");

            var search = new FindItemsSearch
            {
                Query = "select_by_search",
                CombineOperator = FindItemsCombineOperators.All,
                Conditions = conditions,
            };
            EnsureSearchIsSafeToExecute(search);
            var matched = ExecuteSearch(document, search, Stopwatch.StartNew());

            if (scope != SelectBySearchScopes.WholeModel)
            {
                var parentConditions = request.ParentConditions ?? new List<FindItemsCondition>();
                if (parentConditions.Count == 0)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "parentConditions are required for parent-scoped selection.");

                var parentSearch = new FindItemsSearch
                {
                    Query = "select_by_search_parent",
                    CombineOperator = FindItemsCombineOperators.All,
                    Conditions = parentConditions,
                };
                EnsureSearchIsSafeToExecute(parentSearch);
                var parents = ExecuteSearch(document, parentSearch, Stopwatch.StartNew());
                if (parents.Count != 1)
                    throw new AgentCommandException(
                        ErrorCodes.SchemaViolation,
                        "parentConditions must resolve to exactly one parent item; resolved " +
                        parents.Count.ToString(CultureInfo.InvariantCulture) + ".");

                var parent = parents[0];
                var allowedItems = new HashSet<ModelItem>();
                if (scope == SelectBySearchScopes.DirectChildrenOf)
                {
                    foreach (ModelItem child in parent.Children)
                        allowedItems.Add(child);
                }
                else
                {
                    foreach (ModelItem descendant in parent.Descendants)
                        allowedItems.Add(descendant);
                }

                matched = matched.Where(item => allowedItems.Contains(item)).ToList();
            }

            var maxMatchedItems = request.MaxMatchedItems.GetValueOrDefault(5000);
            if (maxMatchedItems < 1 || maxMatchedItems > 100000)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "maxMatchedItems must be between 1 and 100000.");
            if (matched.Count > maxMatchedItems)
                throw new AgentCommandException(
                    ErrorCodes.SchemaViolation,
                    "select_by_search matched " + matched.Count.ToString(CultureInfo.InvariantCulture) +
                    " items, exceeding maxMatchedItems=" + maxMatchedItems.ToString(CultureInfo.InvariantCulture) + ".");

            var replace = request.ReplaceSelection.GetValueOrDefault(true);
            var selected = new ModelItemCollection();
            // Dedup by item identity: the display-name path is not unique, so
            // keying on it drops one of two same-named siblings and would select
            // fewer items than find_items reports.
            var selectedItems = new HashSet<ModelItem>();
            if (!replace && document.CurrentSelection.SelectedItems != null)
            {
                foreach (ModelItem item in document.CurrentSelection.SelectedItems)
                {
                    if (item != null && selectedItems.Add(item))
                        selected.Add(item);
                }
            }
            foreach (var item in matched)
            {
                if (item != null && selectedItems.Add(item))
                    selected.Add(item);
            }
            document.CurrentSelection.CopyFrom(selected);

            var previewLimit = ClampPreviewLimit(request.PreviewLimit);
            return new SelectBySearchResponse
            {
                Scope = scope,
                MatchedItemCount = matched.Count,
                SelectedItemCount = selected.Count,
                ReplacedSelection = replace,
                Preview = matched.Take(previewLimit).Select(BuildPreviewItem).ToList(),
            };
        }

        private static List<MatchPreviewItem> BuildRootNameSuggestions(IEnumerable<RootSearchCandidate> candidates, string query, int limit)
        {
            var candidateList = candidates == null ? new List<RootSearchCandidate>() : candidates.Where(candidate => candidate != null).ToList();
            var aliases = RootSearchSuggestionHelper.Rank(query, candidateList.SelectMany(candidate => candidate.Aliases), limit);
            if (aliases.Count == 0) return new List<MatchPreviewItem>();
            var ranks = aliases.Select((alias, index) => new { alias, index }).ToDictionary(item => item.alias, item => item.index, StringComparer.OrdinalIgnoreCase);
            return candidateList.Where(candidate => candidate.Aliases.Any(alias => ranks.ContainsKey(alias)))
                .OrderBy(candidate => candidate.Aliases.Where(alias => ranks.ContainsKey(alias)).Select(alias => ranks[alias]).DefaultIfEmpty(int.MaxValue).Min())
                .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase).Take(limit)
                .Select(candidate => new MatchPreviewItem { DisplayName = candidate.DisplayName, Path = candidate.Path, SourceFile = candidate.SourceFile }).ToList();
        }

        public FindItemsResponse FindRootItemsByName(Document document, FindRootItemsByNameRequest request, MatchSessionStore sessionStore)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (sessionStore == null)
                throw new ArgumentNullException(nameof(sessionStore));

            var comparison = NormalizeRootNameComparison(request.Comparison);
            var previewLimit = ClampPreviewLimit(request.PreviewLimit);
            var names = request.Names == null
                ? new List<string>()
                : request.Names
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            if (names.Count > MaxRootNameQueries)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "The names list exceeds the maximum of " + MaxRootNameQueries + " items.");

            var started = Stopwatch.StartNew();
            var index = GetRootSearchIndex(document);
            var candidates = index.Candidates;
            var response = new FindItemsResponse();

            foreach (var name in names)
            {
                // Dedup by item identity: the display-name path is not unique, so a
                // path key drops one of two same-named roots. The candidate already
                // carries the path the index built, so ordering no longer rebuilds
                // it twice per comparison inside the sort.
                var seenItems = new HashSet<ModelItem>();
                var matchedCandidates = new List<RootSearchCandidate>();
                foreach (var candidate in candidates)
                {
                    if (!RootCandidateMatches(candidate, name, comparison) || !seenItems.Add(candidate.Item))
                        continue;

                    matchedCandidates.Add(candidate);
                }

                var matchedItems = matchedCandidates
                    .OrderBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                    .Select(candidate => candidate.Item)
                    .ToList();

                var result = new FindItemsResult
                {
                    Query = name,
                    Status = matchedItems.Count == 0 ? FindItemStatuses.NotFound : FindItemStatuses.Matched,
                };

                if (matchedItems.Count > 0)
                {
                    var handle = sessionStore.Add(matchedItems);
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

                    response.Summary.MatchedQueries++;
                    response.Summary.TotalItemsInMatches += matchedItems.Count;
                }
                else
                {
                    if (string.Equals(comparison, FindItemsComparisons.Equal, StringComparison.OrdinalIgnoreCase))
                        result.Suggestions = BuildRootNameSuggestions(candidates, name, previewLimit);
                    response.Summary.NotFoundQueries++;
                }

                response.Results.Add(result);
            }

            started.Stop();
            Logger.Info(
                "find_root_items_by_name names=" + names.Count + " candidates=" + candidates.Count + " matched_queries=" + response.Summary.MatchedQueries + " elapsed_ms=" + started.ElapsedMilliseconds,
                "AgentHost");

            return response;
        }

        public ListRootItemsResponse ListRootItems(Document document, ListRootItemsRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ListRootItemsRequest();
            var includeAliases = request.IncludeAliases.GetValueOrDefault(false);
            var limit = request.Limit.GetValueOrDefault(MaxRootNameQueries);
            if (limit < 1)
                limit = MaxRootNameQueries;

            var index = GetRootSearchIndex(document);
            var response = new ListRootItemsResponse
            {
                DocumentTitle = GetDocumentTitle(document),
                ModelCount = index.ModelCount,
                RootItemCount = index.Candidates.Count,
                Truncated = index.Candidates.Count > limit,
                Items = index.Candidates
                    .Take(limit)
                    .Select(candidate => ToRootItemInfo(candidate, includeAliases))
                    .ToList(),
            };

            return response;
        }

        public ListItemChildrenResponse ListItemChildren(Document document, ListItemChildrenRequest request, MatchSessionStore sessionStore)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (sessionStore == null)
                throw new ArgumentNullException(nameof(sessionStore));

            request = request ?? new ListItemChildrenRequest();
            var parent = ResolveListChildrenParent(document, request, sessionStore);
            var includeHidden = request.IncludeHidden.GetValueOrDefault(true);
            var limit = ClampItemChildrenLimit(request.Limit);
            var children = parent.Children == null
                ? new List<ModelItem>()
                : parent.Children.Cast<ModelItem>().Where(child => child != null).ToList();
            var visibleChildren = includeHidden
                ? children
                : children.Where(child => !child.IsHidden).ToList();
            var returnedChildren = visibleChildren.Take(limit).ToList();

            var response = new ListItemChildrenResponse
            {
                DocumentTitle = GetDocumentTitle(document),
                ParentDisplayName = parent.DisplayName,
                ParentPath = BuildItemPath(parent),
                ParentSourceFile = TryGetSourceFile(parent),
                TotalChildCount = children.Count,
                ReturnedChildCount = returnedChildren.Count,
                SkippedHiddenChildCount = children.Count - visibleChildren.Count,
                Truncated = visibleChildren.Count > returnedChildren.Count,
            };

            if (returnedChildren.Count > 0)
                response.ChildrenMatchHandle = sessionStore.Add(returnedChildren);

            for (var i = 0; i < returnedChildren.Count; i++)
            {
                var child = returnedChildren[i];
                response.Children.Add(new ItemChildInfo
                {
                    Index = i + 1,
                    DisplayName = child.DisplayName,
                    ClassDisplayName = child.ClassDisplayName,
                    Path = BuildItemPath(child),
                    SourceFile = TryGetSourceFile(child),
                    ChildCount = child.Children == null ? 0 : child.Children.Count(),
                    IsHidden = child.IsHidden,
                });
            }

            return response;
        }

        public int GetRootItemCount(Document document)
        {
            if (document == null)
                return 0;

            return GetRootSearchIndex(document).Candidates.Count;
        }
    }
}
