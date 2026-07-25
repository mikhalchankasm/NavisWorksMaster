using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashVirtualGroupMembershipHelper
    {
        public static HashSet<T> CreateReferenceSet<T>(IEnumerable<T> items)
            where T : class
        {
            var result = new HashSet<T>(ReferenceEqualityComparer<T>.Instance);
            if (items == null)
                return result;

            foreach (var item in items)
            {
                if (item != null)
                    result.Add(item);
            }

            return result;
        }

        public static HashSet<T> CollectReferenceSet<T>(IEnumerable<IEnumerable<T>> groups)
            where T : class
        {
            var result = new HashSet<T>(ReferenceEqualityComparer<T>.Instance);
            if (groups == null)
                return result;

            foreach (var group in groups)
            {
                if (group == null)
                    continue;

                foreach (var item in group)
                {
                    if (item != null)
                        result.Add(item);
                }
            }

            return result;
        }

        public static bool ContainsReference<T>(IEnumerable<T> items, T target)
            where T : class
        {
            if (items == null || target == null)
                return false;

            foreach (var item in items)
            {
                if (object.ReferenceEquals(item, target))
                    return true;
            }

            return false;
        }

        // The set must be created by CreateReferenceSet or CollectReferenceSet so
        // that Contains keeps object-identity semantics.
        public static List<T> ExceptReferences<T>(IEnumerable<T> items, ISet<T> excluded)
            where T : class
        {
            var result = new List<T>();
            if (items == null)
                return result;

            foreach (var item in items)
            {
                if (excluded == null || !excluded.Contains(item))
                    result.Add(item);
            }

            return result;
        }

        // The set must be created by CreateReferenceSet or CollectReferenceSet so
        // that Contains keeps object-identity semantics.
        public static List<T> IntersectReferences<T>(IEnumerable<T> items, ISet<T> included)
            where T : class
        {
            var result = new List<T>();
            if (items == null || included == null || included.Count == 0)
                return result;

            foreach (var item in items)
            {
                if (item != null && included.Contains(item))
                    result.Add(item);
            }

            return result;
        }

        private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
            where T : class
        {
            public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();

            public bool Equals(T x, T y)
            {
                return object.ReferenceEquals(x, y);
            }

            public int GetHashCode(T obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
