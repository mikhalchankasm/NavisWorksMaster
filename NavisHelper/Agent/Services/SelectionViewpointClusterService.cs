using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal static class SelectionViewpointClusterService
    {
        internal const int DefaultMaxItemsForClustering = 500;
        internal const int DefaultClusterTargetSize = 100;

        public static SelectionViewpointClusterPlan Build(IList<ModelItem> selectedItems, SelectionClusteringOptions options)
        {
            var items = selectedItems == null ? new List<ModelItem>() : selectedItems.Where(item => item != null).ToList();
            var plan = new SelectionViewpointClusterPlan();
            if (items.Count == 0)
                return plan;

            options = options ?? new SelectionClusteringOptions();
            var mode = ValidateOptions(options);
            plan.ClusterBy = mode;
            if (string.Equals(mode, SelectionClusterModes.None, StringComparison.Ordinal))
            {
                plan.Clusters.Add(CreateSingleCluster(items));
                ApplyClusterCap(plan, options.MaxClusters);
                return plan;
            }

            var nodes = BuildNodes(items);
            if (nodes == null)
            {
                plan.Warnings.Add("Clustering was skipped because at least one selected item has no bounding box. One viewpoint will cover the full selection.");
                plan.Clusters.Add(CreateSingleCluster(items));
                ApplyClusterCap(plan, options.MaxClusters);
                return plan;
            }

            if (string.Equals(mode, SelectionClusterModes.Distance, StringComparison.Ordinal))
                BuildDistanceClusters(plan, nodes, options);
            else if (string.Equals(mode, SelectionClusterModes.Count, StringComparison.Ordinal))
                BuildCountClusters(plan, nodes, options);
            else
                BuildGridClusters(plan, nodes, options);

            ApplyClusterCap(plan, options.MaxClusters);
            return plan;
        }

        public static SelectionViewpointClusterPlan Build(IList<ModelItem> selectedItems, double? clusterMaxDistanceMm, int? maxItemsForDistanceClustering = null)
        {
            return Build(selectedItems, new SelectionClusteringOptions
            {
                ClusterBy = clusterMaxDistanceMm.HasValue && clusterMaxDistanceMm.Value > 0 ? SelectionClusterModes.Distance : SelectionClusterModes.None,
                ClusterMaxDistanceMm = clusterMaxDistanceMm,
                MaxItemsForClustering = maxItemsForDistanceClustering,
            });
        }

        public static string ValidateOptions(SelectionClusteringOptions options)
        {
            options = options ?? new SelectionClusteringOptions();
            var mode = NormalizeMode(options.ClusterBy, options.ClusterMaxDistanceMm);
            if (options.ClusterCount.HasValue && !string.Equals(mode, SelectionClusterModes.Count, StringComparison.Ordinal))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "clusterCount is supported only when clusterBy=count.");
            if (options.MaxClusters.HasValue && (options.MaxClusters.Value < 1 || options.MaxClusters.Value > 100))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "maxClusters must be from 1 to 100.");
            ResolveMaxItems(options);
            if (string.Equals(mode, SelectionClusterModes.Distance, StringComparison.Ordinal))
            {
                var distanceMm = options.ClusterMaxDistanceMm.GetValueOrDefault();
                if (!IsFinite(distanceMm) || distanceMm <= 0)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "clusterMaxDistanceMm must be greater than 0 when clusterBy=distance.");
            }
            if (string.Equals(mode, SelectionClusterModes.Count, StringComparison.Ordinal))
            {
                if (options.ClusterCount.HasValue && (options.ClusterCount.Value < 1 || options.ClusterCount.Value > 100))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "clusterCount must be from 1 to 100 when clusterBy=count.");
                var targetSize = options.ClusterTargetSize.GetValueOrDefault(DefaultClusterTargetSize);
                if (targetSize < 1 || targetSize > 10000)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "clusterTargetSize must be from 1 to 10000 when clusterBy=count.");
            }
            if (string.Equals(mode, SelectionClusterModes.Grid, StringComparison.Ordinal))
            {
                var gridSizeMm = options.ClusterGridSizeMm.GetValueOrDefault();
                if (!IsFinite(gridSizeMm) || gridSizeMm <= 0)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "clusterGridSizeMm must be greater than 0 when clusterBy=grid.");
            }
            return mode;
        }

        private static string NormalizeMode(string value, double? legacyDistance)
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? (legacyDistance.HasValue && legacyDistance.Value > 0 ? SelectionClusterModes.Distance : SelectionClusterModes.None)
                : value.Trim().ToLowerInvariant();
            if (normalized != SelectionClusterModes.None && normalized != SelectionClusterModes.Distance &&
                normalized != SelectionClusterModes.Count && normalized != SelectionClusterModes.Grid)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "clusterBy must be none, distance, count, or grid.");
            }
            return normalized;
        }

        private static void BuildDistanceClusters(SelectionViewpointClusterPlan plan, IList<ClusterNode> nodes, SelectionClusteringOptions options)
        {
            var distanceMm = options.ClusterMaxDistanceMm.GetValueOrDefault();
            if (!IsFinite(distanceMm) || distanceMm <= 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "clusterMaxDistanceMm must be greater than 0 when clusterBy=distance.");
            var itemLimit = ResolveMaxItems(options);
            if (nodes.Count > itemLimit)
            {
                plan.Warnings.Add("Distance clustering was skipped because the selection contains " + nodes.Count.ToString() +
                    " items (limit " + itemLimit.ToString() + "). One viewpoint will cover the full selection.");
                plan.Clusters.Add(CreateCluster(nodes));
                return;
            }

            var distanceUnits = SectionBoxHelper.MmToDocUnits(distanceMm);
            for (var left = 0; left < nodes.Count; left++)
            {
                for (var right = left + 1; right < nodes.Count; right++)
                {
                    if (BoundsDistance(nodes[left].Bounds, nodes[right].Bounds) <= distanceUnits)
                        Union(nodes, left, right);
                }
            }

            var groups = new Dictionary<int, List<ClusterNode>>();
            for (var index = 0; index < nodes.Count; index++)
            {
                var root = Find(nodes, index);
                List<ClusterNode> group;
                if (!groups.TryGetValue(root, out group))
                {
                    group = new List<ClusterNode>();
                    groups[root] = group;
                }
                group.Add(nodes[index]);
            }
            foreach (var group in groups.Values.OrderBy(values => values.Min(node => node.FirstIndex)))
                plan.Clusters.Add(CreateCluster(group.OrderBy(node => node.FirstIndex)));
        }

        private static void BuildCountClusters(SelectionViewpointClusterPlan plan, IList<ClusterNode> nodes, SelectionClusteringOptions options)
        {
            var targetSize = options.ClusterTargetSize.GetValueOrDefault(DefaultClusterTargetSize);
            if (targetSize < 1 || targetSize > 10000)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "clusterTargetSize must be from 1 to 10000 when clusterBy=count.");
            var axis = ResolveDominantAxis(nodes);
            var ordered = nodes.OrderBy(node => GetCenterCoordinate(node.Bounds, axis)).ThenBy(node => node.FirstIndex).ToList();
            if (options.ClusterCount.HasValue && options.ClusterCount.Value > ordered.Count)
                plan.Warnings.Add("clusterCount=" + options.ClusterCount.Value.ToString() + " exceeds itemCount=" + ordered.Count.ToString() + "; one non-empty cluster per item was created.");
            var clusterCount = options.ClusterCount.HasValue
                ? Math.Min(options.ClusterCount.Value, ordered.Count)
                : SelectionClusterPlanningHelper.GetCountClusterCount(ordered.Count, targetSize);
            var offset = 0;
            for (var index = 0; index < clusterCount; index++)
            {
                var size = ordered.Count / clusterCount + (index < ordered.Count % clusterCount ? 1 : 0);
                plan.Clusters.Add(CreateCluster(ordered.Skip(offset).Take(size)));
                offset += size;
            }
        }

        private static void BuildGridClusters(SelectionViewpointClusterPlan plan, IList<ClusterNode> nodes, SelectionClusteringOptions options)
        {
            var gridSizeMm = options.ClusterGridSizeMm.GetValueOrDefault();
            if (!IsFinite(gridSizeMm) || gridSizeMm <= 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "clusterGridSizeMm must be greater than 0 when clusterBy=grid.");
            var gridUnits = SectionBoxHelper.MmToDocUnits(gridSizeMm);
            var groups = nodes
                .GroupBy(node => new GridKey(
                    SelectionClusterPlanningHelper.GetGridIndex(node.Bounds.Center.X, gridUnits),
                    SelectionClusterPlanningHelper.GetGridIndex(node.Bounds.Center.Y, gridUnits)))
                .OrderBy(group => group.Key.X)
                .ThenBy(group => group.Key.Y);
            foreach (var group in groups)
                plan.Clusters.Add(CreateCluster(group.OrderBy(node => node.FirstIndex)));
        }

        private static int ResolveMaxItems(SelectionClusteringOptions options)
        {
            var resolved = options.MaxItemsForClustering ?? options.MaxItemsForDistanceClustering ?? DefaultMaxItemsForClustering;
            if (resolved < 1 || resolved > 10000)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "maxItemsForClustering must be from 1 to 10000.");
            return resolved;
        }

        private static void ApplyClusterCap(SelectionViewpointClusterPlan plan, int? value)
        {
            if (!value.HasValue)
                return;
            if (value.Value < 1 || value.Value > 100)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "maxClusters must be from 1 to 100.");
            if (plan.Clusters.Count <= value.Value)
                return;
            var ordered = plan.Clusters.Select((cluster, index) => new { cluster, index })
                .OrderByDescending(item => item.cluster.Items.Count)
                .ThenBy(item => item.index)
                .ToList();
            plan.DroppedClusterCount = ordered.Count - value.Value;
            plan.UncoveredItemCount = ordered.Skip(value.Value).Sum(item => item.cluster.Items.Count);
            var capped = ordered.Take(value.Value).Select(item => item.cluster).ToList();
            plan.Clusters.Clear();
            plan.Clusters.AddRange(capped);
            plan.ClusterCapApplied = true;
            plan.Warnings.Add("Cluster count was capped at " + value.Value.ToString() + "; largest clusters were retained. droppedClusterCount=" +
                plan.DroppedClusterCount.ToString() + ", uncoveredItemCount=" + plan.UncoveredItemCount.ToString() + ".");
        }

        private static List<ClusterNode> BuildNodes(IList<ModelItem> items)
        {
            var nodes = new List<ClusterNode>();
            for (var index = 0; index < items.Count; index++)
            {
                var bounds = TryGetBounds(items[index]);
                if (bounds == null)
                    return null;
                nodes.Add(new ClusterNode { Item = items[index], Bounds = bounds, Parent = index, FirstIndex = index });
            }
            return nodes;
        }

        private static SelectionViewpointCluster CreateSingleCluster(IList<ModelItem> items)
        {
            return new SelectionViewpointCluster
            {
                Items = items == null ? new List<ModelItem>() : items.ToList(),
                Bounds = MergeBounds((items ?? new List<ModelItem>()).Select(TryGetBounds)),
            };
        }

        private static SelectionViewpointCluster CreateCluster(IEnumerable<ClusterNode> nodes)
        {
            var materialized = (nodes ?? Enumerable.Empty<ClusterNode>()).ToList();
            return new SelectionViewpointCluster
            {
                Items = materialized.Select(node => node.Item).ToList(),
                Bounds = MergeBounds(materialized.Select(node => node.Bounds)),
            };
        }

        private static int ResolveDominantAxis(IList<ClusterNode> nodes)
        {
            var spans = new[]
            {
                nodes.Max(node => node.Bounds.Center.X) - nodes.Min(node => node.Bounds.Center.X),
                nodes.Max(node => node.Bounds.Center.Y) - nodes.Min(node => node.Bounds.Center.Y),
                nodes.Max(node => node.Bounds.Center.Z) - nodes.Min(node => node.Bounds.Center.Z),
            };
            return spans[1] > spans[0] && spans[1] >= spans[2] ? 1 : (spans[2] > spans[0] && spans[2] > spans[1] ? 2 : 0);
        }

        private static double GetCenterCoordinate(BoundingBox3D bounds, int axis)
        {
            return axis == 1 ? bounds.Center.Y : (axis == 2 ? bounds.Center.Z : bounds.Center.X);
        }

        private static BoundingBox3D TryGetBounds(ModelItem item)
        {
            try { return item == null ? null : item.BoundingBox(); }
            catch { return null; }
        }

        private static BoundingBox3D MergeBounds(IEnumerable<BoundingBox3D> boxes)
        {
            var valid = (boxes ?? Enumerable.Empty<BoundingBox3D>()).Where(box => box != null).ToList();
            if (valid.Count == 0)
                return null;
            return new BoundingBox3D(
                new Point3D(valid.Min(box => box.Min.X), valid.Min(box => box.Min.Y), valid.Min(box => box.Min.Z)),
                new Point3D(valid.Max(box => box.Max.X), valid.Max(box => box.Max.Y), valid.Max(box => box.Max.Z)));
        }

        private static double BoundsDistance(BoundingBox3D left, BoundingBox3D right)
        {
            var dx = AxisDistance(left.Min.X, left.Max.X, right.Min.X, right.Max.X);
            var dy = AxisDistance(left.Min.Y, left.Max.Y, right.Min.Y, right.Max.Y);
            var dz = AxisDistance(left.Min.Z, left.Max.Z, right.Min.Z, right.Max.Z);
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static double AxisDistance(double minA, double maxA, double minB, double maxB)
        {
            if (maxA < minB) return minB - maxA;
            if (maxB < minA) return minA - maxB;
            return 0;
        }

        private static int Find(IList<ClusterNode> nodes, int index)
        {
            if (nodes[index].Parent == index) return index;
            nodes[index].Parent = Find(nodes, nodes[index].Parent);
            return nodes[index].Parent;
        }

        private static void Union(IList<ClusterNode> nodes, int left, int right)
        {
            var leftRoot = Find(nodes, left);
            var rightRoot = Find(nodes, right);
            if (leftRoot == rightRoot) return;
            if (nodes[leftRoot].FirstIndex <= nodes[rightRoot].FirstIndex) nodes[rightRoot].Parent = leftRoot;
            else nodes[leftRoot].Parent = rightRoot;
        }

        private static bool IsFinite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private sealed class ClusterNode
        {
            public ModelItem Item;
            public BoundingBox3D Bounds;
            public int Parent;
            public int FirstIndex;
        }

        private struct GridKey : IEquatable<GridKey>
        {
            public GridKey(long x, long y) { X = x; Y = y; }
            public long X { get; private set; }
            public long Y { get; private set; }
            public bool Equals(GridKey other) { return X == other.X && Y == other.Y; }
            public override bool Equals(object obj) { return obj is GridKey && Equals((GridKey)obj); }
            public override int GetHashCode() { return unchecked((X.GetHashCode() * 397) ^ Y.GetHashCode()); }
        }
    }

    internal sealed class SelectionViewpointClusterPlan
    {
        public string ClusterBy { get; set; }
        public bool ClusterCapApplied { get; set; }
        public int DroppedClusterCount { get; set; }
        public int UncoveredItemCount { get; set; }
        public List<SelectionViewpointCluster> Clusters { get; } = new List<SelectionViewpointCluster>();
        public List<string> Warnings { get; } = new List<string>();
    }

    internal sealed class SelectionViewpointCluster
    {
        public List<ModelItem> Items { get; set; } = new List<ModelItem>();
        public BoundingBox3D Bounds { get; set; }
    }
}
