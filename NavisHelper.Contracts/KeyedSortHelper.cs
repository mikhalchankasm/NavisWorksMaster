using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Sorts lists by a precomputed string key instead of comparing the items themselves.
    /// Sorting items directly with a key lookup per comparison hashes two items on every
    /// comparison (about 2 x N x log2 N lookups), which is prohibitively expensive when the
    /// item's hash goes through a native runtime object. Building each key once and sorting
    /// (key, item) pairs turns that into N key builds and plain string comparisons, while
    /// producing exactly the permutation the direct sort would produce, ties included.
    /// </summary>
    public static class KeyedSortHelper
    {
        /// <summary>
        /// Sorts <paramref name="items"/> in place by a key built once per item with
        /// <paramref name="key"/>, comparing only the keys with <paramref name="comparer"/>.
        /// </summary>
        public static void SortByKey<T>(List<T> items, Func<T, string> key, IComparer<string> comparer)
        {
            if (items == null || items.Count == 0)
            {
                return;
            }

            var pairs = new List<(string Key, T Item)>(items.Count);
            foreach (var item in items)
            {
                pairs.Add((key(item), item));
            }

            pairs.Sort((left, right) => comparer.Compare(left.Key, right.Key));

            for (var i = 0; i < pairs.Count; i++)
            {
                items[i] = pairs[i].Item;
            }
        }
    }
}
