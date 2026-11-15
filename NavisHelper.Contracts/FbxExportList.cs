using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>An ordered, duplicate-free export list bound to one model owner.</summary>
    /// <typeparam name="T">The reference type of an export item.</typeparam>
    public sealed class FbxExportList<T> where T : class
    {
        private readonly List<T> _items = new List<T>();
        private readonly HashSet<T> _membership;

        /// <summary>Creates a list using the supplied comparer or default equality.</summary>
        /// <param name="comparer">The equality comparer used for items.</param>
        public FbxExportList(IEqualityComparer<T> comparer = null)
        {
            _membership = new HashSet<T>(comparer ?? EqualityComparer<T>.Default);
        }

        /// <summary>Gets a read-only snapshot in insertion order.</summary>
        public IReadOnlyList<T> Items => new List<T>(_items).AsReadOnly();

        /// <summary>Gets the number of listed items.</summary>
        public int Count => _items.Count;

        /// <summary>Gets the model owner most recently bound to this list.</summary>
        public object Owner { get; private set; }

        /// <summary>Appends non-null, previously absent items in enumeration order.</summary>
        /// <param name="items">Items to append; null is treated as an empty sequence.</param>
        /// <returns>The number of items added.</returns>
        public int Add(IEnumerable<T> items)
        {
            if (items == null) return 0;
            var added = 0;
            foreach (var item in items)
            {
                if (item == null || !_membership.Add(item)) continue;
                _items.Add(item);
                added++;
            }
            return added;
        }

        /// <summary>Removes listed items matching the supplied items.</summary>
        /// <param name="items">Items to remove; null is treated as an empty sequence.</param>
        /// <returns>The number of items removed.</returns>
        public int Remove(IEnumerable<T> items)
        {
            if (items == null) return 0;
            var requested = new HashSet<T>(items, _membership.Comparer);
            requested.Remove(null);
            var removed = _items.RemoveAll(item => requested.Contains(item));
            _membership.ExceptWith(requested);
            return removed;
        }

        /// <summary>Removes all items while retaining the owner binding.</summary>
        public void Clear()
        {
            _items.Clear();
            _membership.Clear();
        }

        /// <summary>Binds an owner, clearing a nonempty list when its owner changes.</summary>
        /// <param name="owner">The model owner, compared by reference identity.</param>
        /// <returns>True if items were cleared because the owner changed.</returns>
        public bool BindTo(object owner)
        {
            var reset = Count > 0 && !ReferenceEquals(Owner, owner);
            if (reset) Clear();
            Owner = owner;
            return reset;
        }
    }
}
