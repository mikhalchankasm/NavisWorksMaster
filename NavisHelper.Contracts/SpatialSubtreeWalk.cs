using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// A pre-order tree walk whose consumer can refuse to descend, item by item.
    ///
    /// `isolate_by_box` always had this from its stack walk: an item whose own box
    /// misses the zone cannot hold a matching descendant -- Autodesk defines
    /// `ModelItem.BoundingBox()` as the box of the item *and its children* -- so the
    /// children are never asked for. `find_items_by_bbox`, walking
    /// `rootItem.DescendantsAndSelf`, paid for every item of every model it had
    /// decided to enter. This walker is the shared, testable form of that stack walk:
    /// it lives in the contracts assembly, away from the Navisworks API, and takes
    /// the tree as delegates, so the skipping semantics are pinned by tests that
    /// need no document.
    /// </summary>
    public static class SpatialSubtreeWalk
    {
        /// <summary>
        /// Yields <paramref name="root"/> and its descendants in pre-order -- the order
        /// `DescendantsAndSelf` gives -- while letting the consumer prune subtrees.
        ///
        /// <paramref name="skipChildrenOf"/> is consulted once per yielded item, after
        /// that item has been yielded and before any of its children is requested. A
        /// consumer that decides, while processing an item, that its subtree is not
        /// worth walking returns `true` for it: no descendant of that item is ever
        /// yielded, and its children are never asked for. The decision governs that
        /// item's subtree only -- its siblings and everything after them are
        /// unaffected.
        ///
        /// <paramref name="childrenOf"/> is called only when the walk actually
        /// descends into an item, so a skipped subtree costs nothing: not even its
        /// children are enumerated.
        ///
        /// A null root yields nothing, and null items are never yielded, matching
        /// what a real item collection yields.
        /// </summary>
        /// <typeparam name="T">The node type.</typeparam>
        /// <param name="root">The item to start from; it is yielded first.</param>
        /// <param name="childrenOf">Returns the children of an item. May return null or an empty sequence for a leaf; called at most once per yielded item, and never for a skipped one.</param>
        /// <param name="skipChildrenOf">Returns true to skip the children of the item it is given; called after that item was yielded.</param>
        public static IEnumerable<T> Walk<T>(
            T root,
            Func<T, IEnumerable<T>> childrenOf,
            Func<T, bool> skipChildrenOf)
        {
            if (childrenOf == null)
                throw new ArgumentNullException(nameof(childrenOf));
            if (skipChildrenOf == null)
                throw new ArgumentNullException(nameof(skipChildrenOf));

            return WalkCore(root, childrenOf, skipChildrenOf);
        }

        private static IEnumerable<T> WalkCore<T>(
            T root,
            Func<T, IEnumerable<T>> childrenOf,
            Func<T, bool> skipChildrenOf)
        {
            if (root == null)
                yield break;

            var pending = new Stack<IEnumerator<T>>();
            pending.Push(((IEnumerable<T>)new[] { root }).GetEnumerator());
            try
            {
                while (pending.Count > 0)
                {
                    var siblings = pending.Peek();
                    if (!siblings.MoveNext())
                    {
                        pending.Pop();
                        siblings.Dispose();
                        continue;
                    }

                    var item = siblings.Current;
                    if (item == null)
                        continue;

                    yield return item;

                    // Read after the yield, so a decision the consumer made while
                    // processing this item -- in the loop body that just received it
                    // -- governs this item's subtree and nothing else.
                    if (skipChildrenOf(item))
                        continue;

                    var children = childrenOf(item);
                    if (children != null)
                        pending.Push(children.GetEnumerator());
                }
            }
            finally
            {
                while (pending.Count > 0)
                    pending.Pop().Dispose();
            }
        }
    }
}
