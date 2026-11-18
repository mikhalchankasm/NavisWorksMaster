using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public static class ResponsePaging
    {
        public static ResponsePage<T> Take<T>(IEnumerable<T> candidates, int offset,
            int selectedCount, Func<T, int> sizeOf, int pageBytes) where T : class
        {
            var page = new ResponsePage<T>();
            var budget = new ResponsePageBudget(pageBytes);
            foreach (var item in candidates)
            {
                if (item != null && !budget.TryTake(sizeOf(item)))
                {
                    if (page.Items.Count == 0)
                    {
                        page.OversizedPosition = offset + page.Consumed;
                        return page;
                    }

                    page.BudgetTruncated = true;
                    break;
                }

                page.Consumed++;
                if (item != null)
                    page.Items.Add(item);
            }

            page.NextOffset = SelectedItemsTreePaging.NextOffset(offset, page.Consumed, selectedCount);
            return page;
        }
    }

    public sealed class ResponsePage<T> where T : class
    {
        public List<T> Items { get; } = new List<T>();
        public int Consumed { get; internal set; }
        public int? NextOffset { get; internal set; }
        public bool BudgetTruncated { get; internal set; }
        public int? OversizedPosition { get; internal set; }
    }
}
