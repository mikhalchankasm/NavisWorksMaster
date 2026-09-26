using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Counts the items hide_unselected would keep visible -- the union of the
    /// selected items' subtrees and their ancestors -- without allocating any
    /// set of its own: the caller already owns the selected items and every
    /// keep marker, and each keep marker lies either inside a kept subtree or
    /// is a strict ancestor of one, so the count is the sum of the kept
    /// subtree sizes plus the caller's keep-marker count minus the keep
    /// markers already inside those subtrees, found with one Contains per
    /// visited node. Nothing is stored, so subtree wrappers are never kept
    /// alive just to produce a count.
    /// </summary>
    public static class KeepVisibleCounter
    {
        public static int Count<TNode>(ISet<TNode> selected, ISet<TNode> keepMarkers,
            Func<TNode, TNode> parent, Func<TNode, IEnumerable<TNode>> children) where TNode : class
        {
            if (selected == null || keepMarkers == null)
                return 0;

            var subtreeTotal = 0;
            var subtreeKeepMarkers = 0;
            foreach (var item in selected)
            {
                if (item == null || HasSelectedAncestor(item, parent, selected))
                    continue;

                var (size, markers) = CountSubtree(item, children, keepMarkers);
                subtreeTotal += size;
                subtreeKeepMarkers += markers;
            }

            return subtreeTotal + keepMarkers.Count - subtreeKeepMarkers;
        }

        private static bool HasSelectedAncestor<TNode>(TNode item, Func<TNode, TNode> parent, ISet<TNode> selected) where TNode : class
        {
            var current = parent(item);
            while (current != null)
            {
                if (selected.Contains(current))
                    return true;
                current = parent(current);
            }

            return false;
        }

        private static (int size, int markers) CountSubtree<TNode>(TNode item,
            Func<TNode, IEnumerable<TNode>> children, ISet<TNode> keepMarkers) where TNode : class
        {
            var size = 1;
            var markers = keepMarkers.Contains(item) ? 1 : 0;
            var childItems = children(item);
            if (childItems != null)
            {
                foreach (var childItem in childItems)
                {
                    if (childItem == null)
                        continue;

                    var (childSize, childMarkers) = CountSubtree(childItem, children, keepMarkers);
                    size += childSize;
                    markers += childMarkers;
                }
            }

            return (size, markers);
        }
    }
}
