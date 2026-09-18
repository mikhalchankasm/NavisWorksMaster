using System;
using System.Collections.Generic;

namespace NavisHelper.Core
{
    // Display paths are labels, not filesystem paths. Consume complete names so
    // slashes (including the display separator itself) remain part of a name.
    internal static class ModelItemPathResolver
    {
        public static List<T> Resolve<T>(IEnumerable<T> starts, string path,
            Func<T, IEnumerable<string>> names, Func<T, IEnumerable<T>> children)
        {
            var result = new List<T>();
            var seen = new HashSet<T>();
            var pending = new Stack<Tuple<T, string>>();
            foreach (var start in starts) pending.Push(Tuple.Create(start, path ?? string.Empty));
            while (pending.Count > 0)
            {
                var entry = pending.Pop();
                foreach (var name in names(entry.Item1))
                {
                    if (string.IsNullOrEmpty(name)) continue;
                    if (string.Equals(entry.Item2, name, StringComparison.OrdinalIgnoreCase))
                    {
                        if (seen.Add(entry.Item1)) result.Add(entry.Item1);
                    }
                    else if (entry.Item2.StartsWith(name + " / ", StringComparison.OrdinalIgnoreCase))
                    {
                        var remaining = entry.Item2.Substring(name.Length + 3);
                        foreach (var child in children(entry.Item1)) pending.Push(Tuple.Create(child, remaining));
                    }
                }
            }
            return result;
        }
    }
}
