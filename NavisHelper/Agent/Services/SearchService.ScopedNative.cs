using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class SearchService
    {
        internal const string ScopedTraversalModeEnvironmentVariable = "NAVISHELPER_SCOPED_FIND_ITEMS";
        internal const string TraversalModeNative = "native";
        internal const string TraversalModeManual = "manual";

        /// <summary>
        /// Runs a scoped find_items through the Navisworks search engine instead
        /// of walking <c>ModelItem.Children</c> in managed code.
        ///
        /// The manual traversal visited every node under the scope: live logs
        /// show 87,996 items scanned to return a single match in 26.5 seconds,
        /// and a 45-second budget that turned the slowest cases into failures.
        /// The native engine takes the same scope through
        /// <c>Selection.CopyFrom(roots)</c> with
        /// <c>SearchLocations.DescendantsAndSelf</c>, which is the same node set.
        ///
        /// Returns false when the search is not exactly expressible as native
        /// conditions, leaving the caller on the proven manual traversal. It
        /// never returns a partial or approximate result: either the native
        /// engine can answer the whole search, or it is not used at all.
        /// </summary>
        private bool TryExecuteNativeScopedFindItems(
            Document document,
            FindItemsSearch search,
            IList<ModelItem> roots,
            string matchDepth,
            bool countOnly,
            int previewLimit,
            MatchSessionStore sessionStore,
            FindItemsResponse response)
        {
            if (roots == null || roots.Count == 0)
                return false;
            if (!IsNativeScopedTraversalEnabled())
                return false;
            if (!CanUseNativeScopedSearch(search))
                return false;

            var conditionGroups = TryBuildNativeScopedConditionGroups(search);
            if (conditionGroups == null)
                return false;

            var started = Stopwatch.StartNew();
            var pathCache = new Dictionary<ModelItem, string>();
            var matchMap = new Dictionary<string, ModelItem>(StringComparer.OrdinalIgnoreCase);
            var variantIndex = 0;

            foreach (var nativeConditions in BuildNativeAndFastPathConditionSets(conditionGroups))
            {
                variantIndex++;
                foreach (var item in EnumerateNativeScopedMatches(document, roots, nativeConditions, started))
                {
                    var path = GetCachedPath(item, pathCache);
                    if (!matchMap.ContainsKey(path))
                        matchMap[path] = item;

                    if (matchMap.Count > MaxScopedScannedItems)
                    {
                        throw new AgentCommandException(
                            ErrorCodes.CommandFailed,
                            "Scoped find_items matched more than " +
                            MaxScopedScannedItems.ToString("N0", CultureInfo.InvariantCulture) +
                            " items. Narrow the scope or add a selective condition.");
                    }
                }
            }

            var matched = matchMap.Values.ToList();
            matched.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(
                GetCachedPath(left, pathCache),
                GetCachedPath(right, pathCache)));

            if (string.Equals(matchDepth, FindItemsMatchDepths.First, StringComparison.Ordinal))
                matched = ScopedMatchTopology.KeepTopMostMatches(matched, item => item.Parent);

            Logger.Info(
                "find_items scoped_native=true variants=" + variantIndex +
                " roots=" + roots.Count +
                " matches=" + matched.Count +
                " elapsed_ms=" + started.ElapsedMilliseconds,
                "AgentHost");

            FillNativeScopedResponse(
                search,
                matched,
                matchDepth,
                countOnly,
                previewLimit,
                sessionStore,
                response);
            return true;
        }

        private static bool IsNativeScopedTraversalEnabled()
        {
            var configured = Environment.GetEnvironmentVariable(ScopedTraversalModeEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured))
                return true;

            return !string.Equals(configured.Trim(), TraversalModeManual, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A scoped search may go native only when every condition is one the
        /// whole-model path already answers natively, and the conditions combine
        /// with AND alone.
        ///
        /// Inherited-property conditions are excluded on purpose: the manual
        /// matcher resolves them by walking each item's ancestors, and the native
        /// path expresses them by expanding matches to descendants. Those agree
        /// for the whole model but not inside an arbitrary scope, so Source File
        /// searches stay on the traversal that is known to be right.
        /// </summary>
        private static bool CanUseNativeScopedSearch(FindItemsSearch search)
        {
            if (search?.Conditions == null || search.Conditions.Count == 0)
                return false;

            if (!string.Equals(
                    search.CombineOperator,
                    FindItemsCombineOperators.All,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            foreach (var condition in search.Conditions)
            {
                if (condition == null)
                    return false;

                if (string.Equals(
                        condition.LogicalOperator,
                        FindItemsConditionOptionsHelper.Or,
                        StringComparison.OrdinalIgnoreCase))
                    return false;

                var resolved = ResolveProperty(condition);
                if (resolved == null || resolved.InheritFromAncestor)
                    return false;
                if (!CanUseNativeAndFastPathCondition(condition, resolved))
                    return false;
            }

            return true;
        }

        private static List<List<SearchCondition>> TryBuildNativeScopedConditionGroups(FindItemsSearch search)
        {
            var groups = new List<List<SearchCondition>>();
            var variantCount = 1;

            foreach (var condition in search.Conditions)
            {
                var alternatives = BuildNativeAndFastPathConditionAlternatives(ResolveProperty(condition), condition);
                if (alternatives == null || alternatives.Count == 0)
                    return null;
                if (variantCount > MaxNativeAndFastPathVariants / alternatives.Count)
                    return null;

                variantCount *= alternatives.Count;
                groups.Add(alternatives);
            }

            return groups.Count == 0 ? null : groups;
        }

        private static IEnumerable<ModelItem> EnumerateNativeScopedMatches(
            Document document,
            IList<ModelItem> roots,
            IEnumerable<SearchCondition> conditions,
            Stopwatch started)
        {
            var search = new Search();
            search.Selection.CopyFrom(roots);
            search.Locations = SearchLocations.DescendantsAndSelf;

            // Search.PruneBelowMatch defaults to true, which makes the engine
            // ignore descendants of a matching item. That is matchDepth=first
            // semantics applied unconditionally, and it would silently drop
            // nested matches for matchDepth=all. Turn it off and let
            // ScopedMatchTopology.KeepTopMostMatches apply matchDepth=first, so
            // one tested code path decides depth semantics for both modes.
            search.PruneBelowMatch = false;

            foreach (var condition in conditions ?? Enumerable.Empty<SearchCondition>())
                search.SearchConditions.Add(condition);

            // FindIncremental rather than FindAll so the time budget the manual
            // traversal enforced per node is still enforced here, instead of
            // disappearing into one uninterruptible call.
            foreach (ModelItem item in search.FindIncremental(document, false))
            {
                if (started.ElapsedMilliseconds > MaxScopedTraversalMilliseconds)
                {
                    throw new AgentCommandException(
                        ErrorCodes.CommandFailed,
                        "Scoped find_items exceeded the 45 second native search budget. Narrow the scope or use matchDepth=first/countOnly.");
                }

                if (item != null)
                    yield return item;
            }
        }

        private void FillNativeScopedResponse(
            FindItemsSearch search,
            IList<ModelItem> matched,
            string matchDepth,
            bool countOnly,
            int previewLimit,
            MatchSessionStore sessionStore,
            FindItemsResponse response)
        {
            response.MatchDepth = matchDepth;
            response.CountOnly = countOnly;
            response.TraversalMode = TraversalModeNative;
            response.MatchedItemCount = matched.Count;

            // ScannedItemCount stays 0 under the native engine: it does not
            // report how many nodes it visited, and inventing a number would be
            // worse than an explicit zero next to traversalMode=native.
            response.ScannedItemCount = 0;

            foreach (var item in matched)
            {
                var depth = ScopedMatchTopology.DepthOf(item, node => node.Parent);
                if (!response.DepthHistogram.ContainsKey(depth))
                    response.DepthHistogram[depth] = 0;
                response.DepthHistogram[depth]++;
            }

            var sampleValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in matched)
            {
                if (sampleValues.Count >= MaxSearchSampleValues)
                    break;

                var sample = ReadSearchSampleValue(item, search);
                if (!string.IsNullOrWhiteSpace(sample))
                    sampleValues.Add(sample);
            }

            response.SampleValuesFromModel = sampleValues.ToList();

            var result = new FindItemsResult
            {
                Query = search.Query,
                Status = matched.Count == 0 ? FindItemStatuses.NotFound : FindItemStatuses.Matched,
            };

            if (matched.Count > 0)
            {
                response.Summary.MatchedQueries = 1;
                response.Summary.TotalItemsInMatches = matched.Count;
                if (!countOnly)
                {
                    result.Matches.Add(new FindItemsMatch
                    {
                        MatchHandle = sessionStore.Add(matched.ToList()),
                        ItemCount = matched.Count,
                        Preview = matched.Take(previewLimit).Select(BuildPreviewItem).ToList(),
                        PreviewTruncated = matched.Count > previewLimit,
                    });
                }
            }
            else
            {
                response.Summary.NotFoundQueries = 1;
            }

            response.Results.Add(result);
        }
    }
}
