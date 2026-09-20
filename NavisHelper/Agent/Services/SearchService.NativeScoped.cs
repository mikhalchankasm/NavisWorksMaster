using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class SearchService
    {
        /// <summary>
        /// Answers a scoped matchDepth=first find_items with the native
        /// Navisworks search rooted at the scope, letting the engine prune below
        /// each match instead of walking the subtree in managed code.
        ///
        /// Engine pruning is exactly matchDepth=first: stop at the shallowest
        /// match on each branch. The earlier attempt at this turned pruning off
        /// so it could also serve matchDepth=all, and paid 37.6 s for a query the
        /// manual traversal answered in 18 ms. Pruning is the whole optimization,
        /// so this path only takes requests where pruning is the right semantics.
        ///
        /// Returns false to leave the request to the manual traversal, which
        /// remains the reference behaviour for everything else.
        /// </summary>
        private static bool TryExecuteNativeScopedSearch(
            Document document,
            FindItemsSearch search,
            IList<ModelItem> roots,
            Stopwatch started,
            out List<ModelItem> matches)
        {
            matches = null;

            if (document == null || roots == null || roots.Count == 0)
                return false;

            List<List<SearchCondition>> variants;
            if (!TryBuildNativeScopedConditionSets(search, out variants))
                return false;

            var scopeSelection = new ModelItemCollection();
            foreach (var root in roots)
            {
                if (root != null)
                    scopeSelection.Add(root);
            }

            if (scopeSelection.Count == 0)
                return false;

            var found = new FindItemsMatchSet<ModelItem>();
            var completedVariants = 0;
            try
            {
                foreach (var conditions in variants)
                {
                    // The manual traversal checks this budget on every node. One
                    // FindAll cannot be interrupted, so the closest equivalent is
                    // to refuse to start another one once the budget is gone.
                    // Falling back to the manual traversal here would spend the
                    // budget a second time, so this fails the call the same way
                    // the manual path does.
                    if (FindItemsNativeScopedPolicy.ExceedsTraversalBudget(started.ElapsedMilliseconds))
                    {
                        Logger.Info(
                            "find_items scoped_native_budget_exceeded completed_variants=" + completedVariants +
                            " variants=" + variants.Count +
                            " native_hits=" + found.Count +
                            " elapsed_ms=" + GetElapsedMilliseconds(started),
                            "AgentHost");
                        throw AbandonNativeScopedSearch(
                            found,
                            FindItemsNativeScopedPolicy.BuildTraversalBudgetMessage(completedVariants, variants.Count));
                    }

                    var nativeSearch = new Search();
                    nativeSearch.Selection.CopyFrom(scopeSelection);
                    nativeSearch.Locations = SearchLocations.DescendantsAndSelf;
                    nativeSearch.PruneBelowMatch = FindItemsNativeSearchPolicy.PruneBelowMatch;
                    foreach (var condition in conditions)
                        nativeSearch.SearchConditions.Add(condition);

                    foreach (ModelItem item in nativeSearch.FindAll(document, false))
                        found.Add(item);

                    completedVariants++;
                }
            }
            catch (AgentCommandException)
            {
                // The budget check above is a decision about this request, not an
                // engine rejection. It must not be turned into a silent fallback.
                throw;
            }
            catch (Exception ex)
            {
                // Any engine rejection falls back to the manual traversal rather
                // than failing the call; the manual result is the contract.
                Logger.Info(
                    "find_items scoped_native_fallback variants=" + variants.Count + " reason=" + ex.GetType().Name + " message=" + ex.Message,
                    "AgentHost");
                return false;
            }

            // Each variant prunes independently, so a node matched only by
            // variant B can sit under a node matched only by variant A. Keep the
            // shallowest match on each branch to restore matchDepth=first across
            // the union.
            matches = KeepShallowestMatches(found);

            Logger.Info(
                "find_items scoped_native=true roots=" + scopeSelection.Count + " variants=" + variants.Count +
                " native_hits=" + found.Count + " matches=" + matches.Count +
                " elapsed_ms=" + GetElapsedMilliseconds(started),
                "AgentHost");

            return true;
        }

        private static bool TryBuildNativeScopedConditionSets(
            FindItemsSearch search,
            out List<List<SearchCondition>> variants)
        {
            variants = null;

            var conditionGroups = new List<List<SearchCondition>>();
            var variantCount = 1;

            foreach (var condition in search.Conditions)
            {
                var resolved = ResolveProperty(condition);
                if (resolved.InheritFromAncestor)
                    return false;

                var alternatives = BuildNativeAndFastPathConditionAlternatives(resolved, condition);
                if (alternatives == null || alternatives.Count == 0)
                    return false;

                if (variantCount > MaxNativeAndFastPathVariants / alternatives.Count)
                    return false;

                variantCount *= alternatives.Count;
                conditionGroups.Add(alternatives);
            }

            if (conditionGroups.Count == 0)
                return false;

            variants = BuildNativeAndFastPathConditionSets(conditionGroups).ToList();
            return variants.Count > 0;
        }

        /// <summary>
        /// Drops every match that has an ancestor in the same match set. Runs
        /// over the matches only, never over the scope, so it stays cheap even
        /// when the engine returns thousands of hits.
        /// </summary>
        private static List<ModelItem> KeepShallowestMatches(FindItemsMatchSet<ModelItem> found)
        {
            var result = new List<ModelItem>();
            foreach (var item in found)
            {
                var ancestor = item == null ? null : item.Parent;
                var hasMatchedAncestor = false;
                while (ancestor != null)
                {
                    if (found.Contains(ancestor))
                    {
                        hasMatchedAncestor = true;
                        break;
                    }

                    ancestor = ancestor.Parent;
                }

                if (!hasMatchedAncestor)
                    result.Add(item);
            }

            return SortMatchesByPath(result);
        }
    }
}
