using System;
using System.Collections;
using System.Collections.Generic;

namespace NavisHelper.Agent.Services
{
    /// <summary>
    /// Insertion-ordered set of find_items matches keyed by item identity.
    ///
    /// The native search path used to accumulate matches in a dictionary keyed
    /// by the display-name path built by BuildItemPath. That path carries no
    /// sibling index, so two genuinely different siblings that share a display
    /// name produce the same key and the second item is silently dropped. The
    /// manual scoped traversal keys by the item itself, which is why the two
    /// paths disagreed on models that contain same-named siblings.
    ///
    /// Navisworks ModelItem.Equals compares the underlying native object, so
    /// item identity is the correct key for both paths.
    /// </summary>
    internal sealed class FindItemsMatchSet<TItem> : IEnumerable<TItem>
        where TItem : class
    {
        private readonly List<TItem> _items;
        private readonly HashSet<TItem> _seen;

        public FindItemsMatchSet()
            : this(null)
        {
        }

        public FindItemsMatchSet(IEqualityComparer<TItem> comparer)
        {
            _items = new List<TItem>();
            _seen = comparer == null ? new HashSet<TItem>() : new HashSet<TItem>(comparer);
        }

        public int Count
        {
            get { return _items.Count; }
        }

        public bool Add(TItem item)
        {
            if (item == null || !_seen.Add(item))
                return false;

            _items.Add(item);
            return true;
        }

        public void AddRange(IEnumerable<TItem> items)
        {
            if (items == null)
                return;

            foreach (var item in items)
                Add(item);
        }

        public bool Contains(TItem item)
        {
            return item != null && _seen.Contains(item);
        }

        public void UnionWith(FindItemsMatchSet<TItem> other)
        {
            if (other == null)
                return;

            AddRange(other._items);
        }

        public void IntersectWith(FindItemsMatchSet<TItem> other)
        {
            if (other == null)
            {
                Clear();
                return;
            }

            RemoveWhere(item => !other.Contains(item));
        }

        /// <summary>
        /// Removes every item the predicate accepts. The predicate is evaluated
        /// against a snapshot, so it may inspect the model without invalidating
        /// the enumeration.
        /// </summary>
        public void RemoveWhere(Func<TItem, bool> shouldRemove)
        {
            if (shouldRemove == null)
                return;

            var retained = new List<TItem>(_items.Count);
            foreach (var item in _items)
            {
                if (shouldRemove(item))
                    _seen.Remove(item);
                else
                    retained.Add(item);
            }

            _items.Clear();
            _items.AddRange(retained);
        }

        public void Clear()
        {
            _items.Clear();
            _seen.Clear();
        }

        public void Sort(Comparison<TItem> comparison)
        {
            if (comparison != null)
                _items.Sort(comparison);
        }

        public List<TItem> ToList()
        {
            return new List<TItem>(_items);
        }

        public IEnumerator<TItem> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
