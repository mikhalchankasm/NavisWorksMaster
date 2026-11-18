using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class ItemChildrenHandleScope
    {
        public static int ClampLimit(int? limit, bool allChildren)
        {
            return Math.Max(allChildren ? 0 : 1, Math.Min(limit.GetValueOrDefault(200), 2000));
        }

        public static ItemChildrenScope<T> Select<T>(IList<T> visibleChildren, int limit, bool allChildren)
        {
            if (visibleChildren == null)
                throw new ArgumentNullException(nameof(visibleChildren));

            var rows = visibleChildren.Take(limit).ToList();
            return new ItemChildrenScope<T>(rows, allChildren ? visibleChildren.ToList() : rows);
        }
    }

    public sealed class ItemChildrenScope<T>
    {
        public List<T> Rows { get; }
        public List<T> HandleItems { get; }

        internal ItemChildrenScope(List<T> rows, List<T> handleItems)
        {
            Rows = rows;
            HandleItems = handleItems;
        }
    }
}
