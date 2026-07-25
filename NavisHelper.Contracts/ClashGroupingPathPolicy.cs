using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashGroupingPathSegment
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public int Depth { get; set; }
    }

    public static class ClashGroupingPathPolicy
    {
        // Callers provide root-to-leaf, non-blank, already-trimmed names.
        // The Navisworks adapter owns DisplayName access and exception handling.
        public static List<ClashGroupingPathSegment> BuildSegments(IEnumerable<string> names)
        {
            var result = new List<ClashGroupingPathSegment>();
            if (names == null)
                return result;

            var parts = new List<string>();
            foreach (var name in names)
            {
                parts.Add(name);
                result.Add(new ClashGroupingPathSegment
                {
                    Name = name,
                    Path = string.Join("/", parts),
                    Depth = result.Count
                });
            }

            return result;
        }

        public static string BuildPath(IEnumerable<string> names)
        {
            var segments = BuildSegments(names);
            return segments.Count == 0 ? string.Empty : segments[segments.Count - 1].Path;
        }

        public static int FindPathIndex(IEnumerable<string> paths, string selectedPath)
        {
            if (paths == null || string.IsNullOrWhiteSpace(selectedPath))
                return -1;

            var index = 0;
            foreach (var path in paths)
            {
                if (string.Equals(path, selectedPath, StringComparison.OrdinalIgnoreCase))
                    return index;
                index++;
            }

            return -1;
        }
    }
}
