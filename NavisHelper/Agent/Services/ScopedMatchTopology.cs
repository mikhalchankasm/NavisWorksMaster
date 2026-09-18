using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Services
{
    /// <summary>
    /// Tree shape helpers for scoped find_items results, expressed over a parent
    /// accessor instead of <c>ModelItem</c> so they can be unit tested without
    /// Navisworks.
    ///
    /// The manual scoped traversal produced two things as a side effect of
    /// walking the tree itself: the depth of each match, and — for
    /// <c>matchDepth=first</c> — only the top-most match on each branch, because
    /// it stopped descending as soon as a node matched. The native search engine
    /// returns a flat match list, so both have to be derived afterwards. These
    /// helpers are that derivation, and they are what makes the native path
    /// provably equivalent to the traversal it replaces.
    /// </summary>
    internal static class ScopedMatchTopology
    {
        /// <summary>
        /// Number of ancestors above <paramref name="item"/>. A model root item
        /// has depth 0, matching what the manual traversal recorded.
        /// </summary>
        internal static int DepthOf<T>(T item, Func<T, T> parentOf) where T : class
        {
            if (parentOf == null)
                throw new ArgumentNullException(nameof(parentOf));

            var depth = 0;
            var current = item == null ? null : parentOf(item);
            while (current != null)
            {
                depth++;
                current = parentOf(current);
            }

            return depth;
        }

        /// <summary>
        /// Keeps only matches that have no matching ancestor, preserving input
        /// order.
        ///
        /// This is exactly what the manual traversal produced for
        /// <c>matchDepth=first</c>: a match under a non-matching intermediate
        /// node is still returned, while a match under another match is not. The
        /// traversal expressed that by not descending; here it is expressed by
        /// discarding a match whose ancestor chain reaches another match.
        /// </summary>
        internal static List<T> KeepTopMostMatches<T>(
            IEnumerable<T> matches,
            Func<T, T> parentOf,
            IEqualityComparer<T> comparer = null) where T : class
        {
            if (parentOf == null)
                throw new ArgumentNullException(nameof(parentOf));

            var ordered = new List<T>();
            foreach (var match in matches ?? Array.Empty<T>())
            {
                if (match != null)
                    ordered.Add(match);
            }

            if (ordered.Count < 2)
                return ordered;

            var matchedSet = new HashSet<T>(ordered, comparer ?? EqualityComparer<T>.Default);
            var result = new List<T>(ordered.Count);
            foreach (var match in ordered)
            {
                if (!HasMatchingAncestor(match, parentOf, matchedSet))
                    result.Add(match);
            }

            return result;
        }

        private static bool HasMatchingAncestor<T>(
            T item,
            Func<T, T> parentOf,
            ICollection<T> matchedSet) where T : class
        {
            var current = parentOf(item);
            while (current != null)
            {
                if (matchedSet.Contains(current))
                    return true;
                current = parentOf(current);
            }

            return false;
        }
    }
}
