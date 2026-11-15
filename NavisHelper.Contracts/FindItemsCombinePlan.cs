using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class FindItemsCombinePlan
    {
        public static List<T> Evaluate<T>(IList<FindItemsCondition> conditions,
            Func<int, IEnumerable<T>> matches, IEqualityComparer<T> comparer)
        {
            comparer = comparer ?? EqualityComparer<T>.Default;
            var result = new List<T>();
            var seen = new HashSet<T>(comparer);
            foreach (var group in FindItemsConditionGroups.Split(conditions))
            {
                List<T> intersection = null;
                foreach (var index in group)
                {
                    var current = matches(index);
                    if (intersection == null)
                        intersection = current.Distinct(comparer).ToList();
                    else
                    {
                        var currentSet = new HashSet<T>(current, comparer);
                        intersection.RemoveAll(item => !currentSet.Contains(item));
                    }

                    if (intersection.Count == 0)
                        break;
                }

                foreach (var item in intersection)
                    if (seen.Add(item))
                        result.Add(item);
            }

            return result;
        }
    }
}
