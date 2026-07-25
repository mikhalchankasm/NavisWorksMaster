using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal static class ClashClusterConstructionService
    {
        public static List<List<T>> Build<T>(
            IList<T> rows,
            string groupMode,
            double clusterDistanceUnits,
            Func<T, string> associationKey,
            Func<T, Point3D> clashPoint)
        {
            var input = rows == null ? new List<T>() : rows.ToList();
            if (input.Count == 0)
                return new List<List<T>>();

            if (string.Equals(groupMode, ClashClusterKeyHelper.ModeObjectPair, StringComparison.OrdinalIgnoreCase))
                return BuildObjectPairClusters(input, associationKey);

            if (string.Equals(groupMode, ClashClusterKeyHelper.ModeSpatial, StringComparison.OrdinalIgnoreCase))
                return BuildSpatialClusters(input, clusterDistanceUnits, clashPoint);

            var clusters = new List<List<T>>();
            foreach (var group in input.GroupBy(associationKey, StringComparer.OrdinalIgnoreCase))
                clusters.AddRange(BuildSpatialClusters(group.ToList(), clusterDistanceUnits, clashPoint));

            return clusters;
        }

        private static List<List<T>> BuildObjectPairClusters<T>(IList<T> rows, Func<T, string> associationKey)
        {
            return rows
                .GroupBy(associationKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.ToList())
                .ToList();
        }

        private static List<List<T>> BuildSpatialClusters<T>(IList<T> rows, double clusterDistanceUnits, Func<T, Point3D> clashPoint)
        {
            var input = rows == null ? new List<T>() : rows.ToList();
            if (input.Count == 0)
                return new List<List<T>>();

            var unionFind = new ClusterUnionFind(input.Count);
            var grid = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var distance = Math.Max(clusterDistanceUnits, 0.000001);
            for (var index = 0; index < input.Count; index++)
            {
                var point = clashPoint(input[index]);
                if (point == null)
                    continue;

                var cellX = (int)Math.Floor(point.X / distance);
                var cellY = (int)Math.Floor(point.Y / distance);
                var cellZ = (int)Math.Floor(point.Z / distance);
                for (var dx = -1; dx <= 1; dx++)
                {
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dz = -1; dz <= 1; dz++)
                        {
                            List<int> candidates;
                            if (!grid.TryGetValue(ClashClusterKeyHelper.BuildSpatialCellKey(cellX + dx, cellY + dy, cellZ + dz), out candidates))
                                continue;

                            foreach (var candidateIndex in candidates)
                            {
                                if (Distance(clashPoint(input[candidateIndex]), point) <= distance)
                                    unionFind.Union(candidateIndex, index);
                            }
                        }
                    }
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

            return input
                .Select((row, index) => new { Row = row, Root = unionFind.Find(index) })
                .GroupBy(item => item.Root)
                .Select(group => group.Select(item => item.Row).ToList())
                .ToList();
        }

        private static double Distance(Point3D a, Point3D b)
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
                if (rootA == rootB)
                    return;

                if (_rank[rootA] < _rank[rootB])
                {
                    _parent[rootA] = rootB;
                    return;
                }

                if (_rank[rootA] > _rank[rootB])
                {
                    _parent[rootB] = rootA;
                    return;
                }

                _parent[rootB] = rootA;
                _rank[rootA]++;
            }
        }
    }
}
