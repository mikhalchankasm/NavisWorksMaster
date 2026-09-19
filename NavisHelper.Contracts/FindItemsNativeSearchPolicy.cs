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
        /// than by the manual scoped traversal. Mirrors the routing in
        /// SearchService.FindItems.
        /// </summary>
        public static bool UsesPrunedNativeSearch(
            string scope,
            string matchDepth,
            bool countOnly,
            bool requiresLiteralAnchorTraversal)
        {
            if (countOnly || requiresLiteralAnchorTraversal)
                return false;

            return string.Equals(scope, FindItemsScopes.WholeModel, StringComparison.Ordinal)
                && string.Equals(matchDepth, FindItemsMatchDepths.All, StringComparison.Ordinal);
        }

        /// <summary>
        /// Warning text for a completed pruned whole-model search, or null when
        /// pruning cannot have changed the result. Every match being childless
        /// means the engine had nothing below a match to discard.
        /// </summary>
        public static string BuildPrunedWholeModelWarning(bool anyMatchHasChildren)
        {
            return anyMatchHasChildren ? WholeModelPrunedWarning : null;
        }
    }
}
