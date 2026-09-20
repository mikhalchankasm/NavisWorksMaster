using System;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Pruning contract for the native whole-model find_items path.
    ///
    /// Autodesk.Navisworks.Api.Search.PruneBelowMatch defaults to true: the
    /// engine "ignores descendants of any matching model items". The
    /// whole-model path has always run with that default, so
    /// scope=whole_model + matchDepth=all returns the shallowest match on each
    /// branch, while the manual scoped traversal with matchDepth=all returns
    /// nested matches as well.
    ///
    /// That asymmetry is the documented contract (see
    /// docs/MCP_TOOL_CONTRACTS.md). This type exists so the pruning flag is set
    /// deliberately rather than inherited from an SDK default, and so the
    /// pruned path can say out loud when pruning may have suppressed nested
    /// matches instead of differing from the scoped path in silence.
    /// </summary>
    public static class FindItemsNativeSearchPolicy
    {
        /// <summary>
        /// Value assigned to Search.PruneBelowMatch on the native whole-model
        /// path. Kept true by decision: whole-model results stay pruned.
        /// </summary>
        public const bool PruneBelowMatch = true;

        public const string WholeModelPrunedWarning =
            "scope=whole_model with matchDepth=all is answered by the native Navisworks search with PruneBelowMatch=true, "
            + "so descendants of a matching item are not returned. At least one match has children, so nested matches may be missing. "
            + "For an unpruned result, repeat the search with scope=under_handle or scope=under_named_node and matchDepth=all.";

        /// <summary>
        /// True when the request is answered by the pruned native search rather
        /// than by the manual scoped traversal. This is the routing decision
        /// itself, not a copy of it: SearchService.FindItems calls this method.
        ///
        /// countOnly used to force the traversal here so that scannedItemCount
        /// could be reported. On a model of any size that made the call fail
        /// rather than answer: a whole-model traversal of ~270k nodes exceeds the
        /// 45 second budget every time, measured on this repository's reference
        /// model. A count that arrives is worth more than a scanned-node total
        /// that never does, so countOnly now takes this path too and says in a
        /// warning what it cannot report.
        ///
        /// matchDepth=first is here for the same reason and with a stronger
        /// argument. Engine pruning *is* first -- stop at the shallowest match on
        /// each branch -- which is why an eligible scoped first is already handed
        /// to the engine. At whole-model scope it was not, so the cheapest
        /// matchDepth was the only one that could not be answered: measured live
        /// on 6501.5.nwd, whole_model + first with a single equals condition took
        /// 45 228 ms and failed on the traversal budget, while the same conditions
        /// with matchDepth=all returned from the engine in under a second. The set
        /// is identical because pruning is what first means, so the only thing the
        /// traversal added was the failure.
        /// </summary>
        public static bool UsesPrunedNativeSearch(
            string scope,
            string matchDepth,
            bool requiresLiteralAnchorTraversal)
        {
            if (requiresLiteralAnchorTraversal)
                return false;

            if (!string.Equals(scope, FindItemsScopes.WholeModel, StringComparison.Ordinal))
                return false;

            return string.Equals(matchDepth, FindItemsMatchDepths.All, StringComparison.Ordinal)
                || string.Equals(matchDepth, FindItemsMatchDepths.First, StringComparison.Ordinal);
        }

        public const string WholeModelCountOnlyWarning =
            "scope=whole_model with countOnly=true is answered by the native Navisworks search, which reports "
            + "matches rather than how many nodes it walked, so scannedItemCount is 0. matchedItemCount, "
            + "depthHistogram and sampleValuesFromModel are exact. For a scanned-node count, scope the search "
            + "with scope=under_handle or scope=under_named_node, where countOnly still traverses.";

        public const string WholeModelFirstWarning =
            "scope=whole_model with matchDepth=first is answered by the native Navisworks search with "
            + "PruneBelowMatch=true, which is what matchDepth=first means, so the match set is exact. The engine "
            + "reports matches rather than how many nodes it walked, so scannedItemCount is 0. For a scanned-node "
            + "count, scope the search with scope=under_handle or scope=under_named_node.";

        /// <summary>
        /// Warning text for a completed pruned whole-model search, or null when
        /// pruning cannot have changed the result. Every match being childless
        /// means the engine had nothing below a match to discard.
        ///
        /// matchDepth is required because the warning is about a surprise, and
        /// there is no surprise when pruning is what the caller asked for. A
        /// matchDepth=first caller wants the shallowest match on each branch, which
        /// is exactly what the engine returns, so telling it that descendants of a
        /// match are missing would be describing the request as a defect. It gets
        /// <see cref="WholeModelFirstWarning"/> instead, which reports the one
        /// thing that really is missing: scannedItemCount.
        /// </summary>
        public static string BuildPrunedWholeModelWarning(string matchDepth, bool anyMatchHasChildren)
        {
            if (string.Equals(matchDepth, FindItemsMatchDepths.First, StringComparison.Ordinal))
                return WholeModelFirstWarning;

            return anyMatchHasChildren ? WholeModelPrunedWarning : null;
        }
    }
}
