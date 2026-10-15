using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashClusterPoint
    {
        public ClashClusterPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    public static class ClashClusterBuilder
    {
        public static List<List<T>> Build<T>(IList<T> rows, string groupMode, double clusterDistance,
            Func<T, string> associationKey, Func<T, ClashClusterPoint> clashPoint)
        {
            var input = rows == null ? new List<T>() : rows.ToList();
            if (input.Count == 0)
                return new List<List<T>>();
            if (string.Equals(groupMode, ClashClusterKeyHelper.ModeObjectPair, StringComparison.OrdinalIgnoreCase))
                return input.GroupBy(associationKey, StringComparer.OrdinalIgnoreCase).Select(group => group.ToList()).ToList();
            if (string.Equals(groupMode, ClashClusterKeyHelper.ModeSpatial, StringComparison.OrdinalIgnoreCase))
                return BuildSpatialClusters(input, clusterDistance, clashPoint);
            var clusters = new List<List<T>>();
            foreach (var group in input.GroupBy(associationKey, StringComparer.OrdinalIgnoreCase))
                clusters.AddRange(BuildSpatialClusters(group.ToList(), clusterDistance, clashPoint));
            return clusters;
        }

        private static List<List<T>> BuildSpatialClusters<T>(IList<T> rows, double clusterDistance, Func<T, ClashClusterPoint> clashPoint)
        {
            var input = rows == null ? new List<T>() : rows.ToList();
            if (input.Count == 0)
                return new List<List<T>>();
            var points = input.Select(clashPoint).ToArray();
            var unionFind = new ClusterUnionFind(input.Count);
            var grid = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var distance = Math.Max(clusterDistance, 0.000001);
            for (var index = 0; index < input.Count; index++)
            {
                var point = points[index];
                if (point == null)
                    continue;
                var cellX = (int)Math.Floor(point.X / distance);
                var cellY = (int)Math.Floor(point.Y / distance);
                var cellZ = (int)Math.Floor(point.Z / distance);
                for (var dx = -1; dx <= 1; dx++)
                    for (var dy = -1; dy <= 1; dy++)
                        for (var dz = -1; dz <= 1; dz++)
                        {
                            List<int> candidates;
                            if (!grid.TryGetValue(ClashClusterKeyHelper.BuildSpatialCellKey(cellX + dx, cellY + dy, cellZ + dz), out candidates))
                                continue;
                            foreach (var candidateIndex in candidates)
                                if (Distance(points[candidateIndex], point) <= distance)
                                    unionFind.Union(candidateIndex, index);
                        }
                var key = ClashClusterKeyHelper.BuildSpatialCellKey(cellX, cellY, cellZ);
                List<int> bucket;
                if (!grid.TryGetValue(key, out bucket))
                {
                    bucket = new List<int>();
                    grid[key] = bucket;
                }
                bucket.Add(index);
            }
            return input.Select((row, index) => new { Row = row, Root = unionFind.Find(index) })
                .GroupBy(item => item.Root).Select(group => group.Select(item => item.Row).ToList()).ToList();
        }

        private static double Distance(ClashClusterPoint a, ClashClusterPoint b)
        {
            if (a == null || b == null)
                return double.MaxValue;
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private sealed class ClusterUnionFind
        {
            private readonly int[] _parent;
            private readonly int[] _rank;
            public ClusterUnionFind(int count)
            {
                _parent = new int[count];
                _rank = new int[count];
                for (var index = 0; index < count; index++)
                    _parent[index] = index;
            }
            public int Find(int index)
            {
                if (_parent[index] != index)
                    _parent[index] = Find(_parent[index]);
                return _parent[index];
            }
            public void Union(int a, int b)
            {
                var rootA = Find(a);
                var rootB = Find(b);
                if (rootA == rootB) return;
                if (_rank[rootA] < _rank[rootB]) { _parent[rootA] = rootB; return; }
                if (_rank[rootA] > _rank[rootB]) { _parent[rootB] = rootA; return; }
                _parent[rootB] = rootA;
                _rank[rootA]++;
            }
        }
    }
}
