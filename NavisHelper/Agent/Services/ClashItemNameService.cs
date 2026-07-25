using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;

namespace NavisHelper.Agent.Services
{
    internal static class ClashItemNameService
    {
        private const int DescendantProbeLimit = 100;

        public static List<string> GetNames(ModelItemCollection items, ModelItem fallback, int limit)
        {
            var names = new List<string>();
            if (limit <= 0)
                return names;

            if (items != null)
            {
                foreach (ModelItem item in items)
                {
                    AddResolvedName(names, item, limit);
                    if (names.Count >= limit)
                        break;
                }
            }

            if (names.Count == 0)
                AddResolvedName(names, fallback, limit);
            return names;
        }

        public static string GetFirstName(ModelItemCollection items, ModelItem fallback)
        {
            var names = GetNames(items, fallback, 1);
            return names.Count == 0 ? string.Empty : names[0];
        }

        private static void AddResolvedName(ICollection<string> names, ModelItem item, int limit)
        {
            if (names == null || item == null || names.Count >= limit)
                return;

            var name = ResolveName(item);
            if (!string.IsNullOrWhiteSpace(name) && !Contains(names, name))
                names.Add(name);
        }

        private static string ResolveName(ModelItem item)
        {
            var direct = ReadDisplayName(item);
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;

            try
            {
                var inspected = 0;
                foreach (ModelItem descendant in item.Descendants)
                {
                    if (++inspected > DescendantProbeLimit)
                        break;
                    var descendantName = ReadDisplayName(descendant);
                    if (!string.IsNullOrWhiteSpace(descendantName))
                        return descendantName;
                }
            }
            catch
            {
            }

            try
            {
                var ancestor = item.Parent;
                while (ancestor != null)
                {
                    var ancestorName = ReadDisplayName(ancestor);
                    if (!string.IsNullOrWhiteSpace(ancestorName))
                        return ancestorName;
                    ancestor = ancestor.Parent;
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static string ReadDisplayName(ModelItem item)
        {
            try
            {
                return item == null ? string.Empty : item.DisplayName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool Contains(IEnumerable<string> values, string candidate)
        {
            foreach (var value in values)
            {
                if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
