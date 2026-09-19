using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Eligibility rules for answering a scoped find_items with the native
    /// Navisworks search instead of the manual traversal.
    ///
    /// The native engine is only equivalent to the manual traversal for
    /// matchDepth=first, because Search.PruneBelowMatch=true has exactly the
    /// same meaning: stop at the shallowest match on each branch. A previous
    /// attempt turned pruning off to serve matchDepth=all and was 2000x slower
    /// on the shape it was meant to speed up, so matchDepth=all stays manual.
    ///
    /// countOnly stays manual as well: the engine reports matches, not how many
    /// nodes it walked, and scannedItemCount is the point of that call.
    /// </summary>
    public static class FindItemsNativeScopedPolicy
    {
        /// <summary>
        /// Set NAVISHELPER_FIND_ITEMS_NATIVE_SCOPE=0 to force every scoped
        /// find_items back onto the manual traversal. Diagnostic escape hatch
        /// for A/B measurement and for a model where the two paths disagree.
        /// </summary>
        public const string DisableEnvironmentVariable = "NAVISHELPER_FIND_ITEMS_NATIVE_SCOPE";

        /// <summary>
        /// Wall-clock budget for a scoped find_items, shared with the manual
        /// traversal so neither path outlives the other.
        ///
        /// The native path cannot interrupt a single Search.FindAll, so it
        /// enforces the budget between condition variants: an exhausted budget
        /// must not buy another full engine scan. Without this the fast path
        /// silently dropped the guard the manual traversal has always had, and
        /// that guard fires on real models -- Item/Name contains "насос" over the
        /// nine discipline roots of 6501.5.nwd fails manually at 45 018 ms.
        /// </summary>
        public const int TraversalBudgetMilliseconds = 45000;

        /// <summary>
        /// Mirrors the manual traversal's comparison exactly, so neither path
        /// tolerates a millisecond the other rejects.
        /// </summary>
        public static bool ExceedsTraversalBudget(long elapsedMilliseconds)
        {
            return elapsedMilliseconds > TraversalBudgetMilliseconds;
        }

        /// <summary>
        /// Failure text for a budget exhausted part-way through the variants.
        /// Falling back to the manual traversal at this point would spend the
        /// budget a second time, so the call fails the way the manual path
        /// fails. matchDepth is already first here, so narrowing the scope is
        /// the only remedy left to name.
        /// </summary>
        public static string BuildTraversalBudgetMessage(int completedVariants, int totalVariants)
        {
            return "Scoped find_items exceeded the 45 second traversal budget after "
                + completedVariants.ToString(CultureInfo.InvariantCulture)
                + " of "
                + totalVariants.ToString(CultureInfo.InvariantCulture)
                + " native searches. Narrow the scope.";
        }

        private static readonly string[] NativeComparisons =
        {
            FindItemsComparisons.Equal,
            FindItemsComparisons.Contains,
            FindItemsComparisons.Wildcard,
        };

        public static bool IsDisabledByEnvironment(string environmentValue)
        {
            if (string.IsNullOrWhiteSpace(environmentValue))
                return false;

            var value = environmentValue.Trim();
            return string.Equals(value, "0", StringComparison.Ordinal)
                || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "manual", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True when the whole request can be handed to one engine-pruned native
        /// search per condition variant. Any false here keeps the existing
        /// manual traversal, which stays the reference behaviour.
        /// </summary>
        public static bool IsEligible(FindItemsSearch search, string scope, string matchDepth, bool countOnly)
        {
            if (search == null || search.Conditions == null || search.Conditions.Count == 0)
                return false;

            if (countOnly)
                return false;

            if (!string.Equals(matchDepth, FindItemsMatchDepths.First, StringComparison.Ordinal))
                return false;

            // whole_model keeps its own native routing; this path is for the
            // scoped executor, which is the only one that resolves explicit roots.
            if (string.Equals(scope, FindItemsScopes.WholeModel, StringComparison.Ordinal))
                return false;

            // Native SearchConditions are ANDed. Anything with OR semantics,
            // whether at the search level or between conditions, stays manual.
            if (!string.Equals(search.CombineOperator, FindItemsCombineOperators.All, StringComparison.OrdinalIgnoreCase))
                return false;

            if (search.Conditions.Skip(1).Any(condition =>
                    condition != null && !string.Equals(
                        condition.LogicalOperator,
                        FindItemsConditionOptionsHelper.And,
                        StringComparison.OrdinalIgnoreCase)))
                return false;

            return search.Conditions.All(IsNativeCondition);
        }

        private static bool IsNativeCondition(FindItemsCondition condition)
        {
            if (condition == null)
                return false;

            // Negation and inherited properties are evaluated manually so that
            // complement and ancestor semantics stay identical across paths.
            if (condition.Negate.GetValueOrDefault(false))
                return false;

            if (condition.InheritFromAncestor.GetValueOrDefault(false))
                return false;

            string comparison;
            try
            {
                comparison = FindItemsSearchRulesHelper.NormalizeComparison(condition.Operator);
            }
            catch (ArgumentException)
            {
                // An unsupported comparison is rejected upstream; never let the
                // eligibility probe be the thing that throws.
                return false;
            }

            return NativeComparisons.Contains(comparison);
        }
    }
}
