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
        ///
        /// countOnly used to force the traversal here so that scannedItemCount
        /// could be reported. On a model of any size that made the call fail
        /// rather than answer: a whole-model traversal of ~270k nodes exceeds the
        /// 45 second budget every time, measured on this repository's reference
        /// model. A count that arrives is worth more than a scanned-node total
        /// that never does, so countOnly now takes this path too and says in a
        /// warning what it cannot report.
        /// </summary>
        public static bool UsesPrunedNativeSearch(
            string scope,
            string matchDepth,
            bool requiresLiteralAnchorTraversal)
        {
            if (requiresLiteralAnchorTraversal)
                return false;

            return string.Equals(scope, FindItemsScopes.WholeModel, StringComparison.Ordinal)
                && string.Equals(matchDepth, FindItemsMatchDepths.All, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether a resolved property reference can be handed to the engine at all.
        ///
        /// `SearchCondition` has no property-only factory: the surface is
        /// `HasPropertyBy{CombinedName,DisplayName,Name}`, each of which takes a
        /// category. `CreateSearchCondition` therefore passes `string.Empty` when no
        /// category is available, and the engine is asked for a property in the
        /// category literally named "" -- which matches nothing and does not error.
        ///
        /// So a category-less condition is not ambiguous, it is inexpressible. An
        /// empty category means "any category" to the manual matcher and "the
        /// category named empty-string" to the engine, and the traversal can express
        /// something the engine structurally cannot. Measured live on 6501.5.nwd:
        /// `/DN equals "150mm"` returned 0 from the engine and 135 from a traversal
        /// over the same nodes, while `AVEVA/DN equals "150mm"` returned 135 from
        /// both.
        ///
        /// The two arguments are the two ways a category can arrive -- a display
        /// candidate that carries one, or a resolved internal category with its
        /// property. Passed as booleans so the rule can be tested without a live
        /// document.
        /// </summary>
        public static bool NativeConditionNeedsACategory(
            bool hasDisplayCandidateWithCategory,
            bool hasInternalCategoryAndProperty)
        {
            return !hasDisplayCandidateWithCategory && !hasInternalCategoryAndProperty;
        }

        /// <summary>
        /// Refusal text for a whole-model request whose condition names no category.
        ///
        /// The whole-model route refuses rather than falling back to the traversal,
        /// because that traversal cannot finish on a model of any size -- it would
        /// replace a confident zero with a 45 second failure whose message talks about
        /// narrowing the scope instead of naming the category. A scoped request does
        /// fall back, and answers, so no caller that gets a correct answer today loses
        /// it; the only behaviour that changes is the one that was lying.
        /// </summary>
        public const string CategoryRequiredForNativeSearch =
            "This condition names a property without a category, which the Navisworks search engine cannot express: "
            + "SearchCondition takes a category with every property, so the engine is asked for the property in the "
            + "category named \"\" and matches nothing. Name the property's category - for example category=\"AVEVA\" "
            + "with property=\"DN\", or property=\"AVEVA/DN\" - or scope the search with scope=under_handle or "
            + "scope=under_named_node, where the traversal matches a property in whatever category holds it.";

        public const string WholeModelCountOnlyWarning =
            "scope=whole_model with countOnly=true is answered by the native Navisworks search, which reports "
            + "matches rather than how many nodes it walked, so scannedItemCount is 0. matchedItemCount, "
            + "depthHistogram and sampleValuesFromModel are exact. For a scanned-node count, scope the search "
            + "with scope=under_handle or scope=under_named_node, where countOnly still traverses.";

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
