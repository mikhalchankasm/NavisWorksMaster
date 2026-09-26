using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Counts the items hide_unselected would keep visible -- the union of the
    /// selected items' subtrees and their ancestors -- without materializing that
    /// union: only the selected items and their ancestors ever enter a set, so a
    /// large selection does not keep thousands of subtree wrappers alive just to
    /// produce a count.
    /// </summary>
    public static class KeepVisibleCounter
    {
        public static int Count<TNode>(IEnumerable<TNode> selected, Func<TNode, TNode> parent,
            Func<TNode, IEnumerable<TNode>> children, IEqualityComparer<TNode> comparer = null) where TNode : class
        {
            if (selected == null)
                return 0;

            var effectiveComparer = comparer ?? EqualityComparer<TNode>.Default;
            var selectedItems = new HashSet<TNode>(effectiveComparer);
            foreach (var item in selected)
            {
                if (item != null)
                    selectedItems.Add(item);
            }

            var ancestors = new HashSet<TNode>(effectiveComparer);
            var subtreeTotal = 0;
            foreach (var item in selectedItems)
            {
                if (HasSelectedAncestor(item, parent, selectedItems))
                    continue;

                subtreeTotal += CountSubtree(item, children);

                var current = parent(item);
                while (current != null)
                {
                    ancestors.Add(current);
                    current = parent(current);
                }
            }

            return subtreeTotal + ancestors.Count;
        }

        private static bool HasSelectedAncestor<TNode>(TNode item, Func<TNode, TNode> parent, HashSet<TNode> selectedItems) where TNode : class
        {
            var current = parent(item);
            while (current != null)
            {
                if (selectedItems.Contains(current))
                    return true;
                current = parent(current);
            }

            return false;
        }

        private static int CountSubtree<TNode>(TNode item, Func<TNode, IEnumerable<TNode>> children) where TNode : class
        {
            var count = 1;
            var childItems = children(item);
            if (childItems != null)
            {
                foreach (var childItem in childItems)
                {
                    if (childItem != null)
                        count += CountSubtree(childItem, children);
                }
            }

            return count;
        }
    }
}
