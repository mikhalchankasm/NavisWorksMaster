using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class MarkupFrameGroupingHelper
    {
        public const int DefaultMaxMergeItems = 5000;
        public const long DefaultMaxCandidateComparisons = 2000000;

        public static List<MarkupFrameGroup> Group(
            IReadOnlyList<MarkupFrameBounds> bounds,
            double soloMinHorizontalSize,
            double mergeGap,
            int maxMergeItems = DefaultMaxMergeItems,
            long maxCandidateComparisons = DefaultMaxCandidateComparisons)
        {
            if (bounds == null)
                throw new ArgumentNullException(nameof(bounds));
            ValidateDistance(soloMinHorizontalSize, nameof(soloMinHorizontalSize));
            ValidateDistance(mergeGap, nameof(mergeGap));
            if (maxMergeItems < 1)
                throw new ArgumentOutOfRangeException(nameof(maxMergeItems), "Value must be greater than zero.");
            if (maxCandidateComparisons < 1)
                throw new ArgumentOutOfRangeException(nameof(maxCandidateComparisons), "Value must be greater than zero.");

            var indexed = bounds
                .Select((item, index) => new Node(item ?? throw new ArgumentException("Bounds cannot contain null items.", nameof(bounds)), index))
                .ToList();
            foreach (var node in indexed)
                ValidateBounds(node.Bounds);

            var groups = indexed
                .Where(node => node.Bounds.MaxHorizontalSize >= soloMinHorizontalSize)
                .OrderBy(node => node.Index)
                .Select(node => CreateGroup(new[] { node }, true))
                .ToList();
            var small = indexed
                .Where(node => node.Bounds.MaxHorizontalSize < soloMinHorizontalSize)
                .OrderBy(node => node.Index)
                .ToList();

            if (small.Count > maxMergeItems)
                throw new MarkupFrameGroupingLimitExceededException(small.Count, 0, maxMergeItems, maxCandidateComparisons);

            for (var index = 0; index < small.Count; index++)
                small[index].Parent = index;

            // Sweep by MinX. Only horizontally reachable boxes remain active;
            // this avoids the unconditional O(n^2) pair scan used previously.
            var sweepOrder = Enumerable.Range(0, small.Count)
                .OrderBy(index => small[index].Bounds.MinX)
                .ThenBy(index => small[index].Index)
                .ToList();
            var active = new LinkedList<int>();
            long candidateComparisons = 0;
            foreach (var currentIndex in sweepOrder)
            {
                var current = small[currentIndex];
                var activeNode = active.First;
                while (activeNode != null)
                {
                    var next = activeNode.Next;
                    var otherIndex = activeNode.Value;
                    var other = small[otherIndex];
                    if (other.Bounds.MaxX + mergeGap < current.Bounds.MinX)
                    {
                        active.Remove(activeNode);
                        activeNode = next;
                        continue;
                    }

                    candidateComparisons++;
                    if (candidateComparisons > maxCandidateComparisons)
                        throw new MarkupFrameGroupingLimitExceededException(small.Count, candidateComparisons, maxMergeItems, maxCandidateComparisons);

                    // Once both nodes already belong to the same connected
                    // component, their pair cannot change the final grouping.
                    if (Find(small, otherIndex) != Find(small, currentIndex) &&
                        AxisDistance(other.Bounds.MinY, other.Bounds.MaxY, current.Bounds.MinY, current.Bounds.MaxY) <= mergeGap)
                    {
                        if (HorizontalDistanceSquared(other.Bounds, current.Bounds) <= mergeGap * mergeGap)
                            Union(small, otherIndex, currentIndex);
                    }

                    activeNode = next;
                }
                active.AddLast(currentIndex);
            }

            var components = new Dictionary<int, List<Node>>();
            for (var index = 0; index < small.Count; index++)
            {
                var root = Find(small, index);
                List<Node> component;
                if (!components.TryGetValue(root, out component))
                {
                    component = new List<Node>();
                    components[root] = component;
                }
                component.Add(small[index]);
            }

            groups.AddRange(components.Values
                .OrderBy(component => component.Min(node => node.Index))
                .Select(component => CreateGroup(component, false)));
            return groups;
        }

        private static MarkupFrameGroup CreateGroup(IEnumerable<Node> source, bool isSolo)
        {
            var nodes = source.OrderBy(node => node.Index).ToList();
            return new MarkupFrameGroup
            {
                IsSolo = isSolo,
                SourceIndices = nodes.Select(node => node.Index).ToList(),
                Bounds = new MarkupFrameBounds
                {
                    MinX = nodes.Min(node => node.Bounds.MinX),
                    MinY = nodes.Min(node => node.Bounds.MinY),
                    MinZ = nodes.Min(node => node.Bounds.MinZ),
                    MaxX = nodes.Max(node => node.Bounds.MaxX),
                    MaxY = nodes.Max(node => node.Bounds.MaxY),
                    MaxZ = nodes.Max(node => node.Bounds.MaxZ),
                },
            };
        }

        private static double HorizontalDistanceSquared(MarkupFrameBounds left, MarkupFrameBounds right)
        {
            var dx = AxisDistance(left.MinX, left.MaxX, right.MinX, right.MaxX);
            var dy = AxisDistance(left.MinY, left.MaxY, right.MinY, right.MaxY);
            return dx * dx + dy * dy;
        }

        private static double AxisDistance(double minA, double maxA, double minB, double maxB)
        {
            if (maxA < minB)
                return minB - maxA;
            if (maxB < minA)
                return minA - maxB;
            return 0;
        }

        private static int Find(IList<Node> nodes, int index)
        {
            if (nodes[index].Parent == index)
                return index;
            nodes[index].Parent = Find(nodes, nodes[index].Parent);
            return nodes[index].Parent;
        }

        private static void Union(IList<Node> nodes, int left, int right)
        {
            var leftRoot = Find(nodes, left);
            var rightRoot = Find(nodes, right);
            if (leftRoot == rightRoot)
                return;

            if (nodes[leftRoot].Index <= nodes[rightRoot].Index)
                nodes[rightRoot].Parent = leftRoot;
            else
                nodes[leftRoot].Parent = rightRoot;
        }

        private static void ValidateBounds(MarkupFrameBounds bounds)
        {
            if (!IsFinite(bounds.MinX) || !IsFinite(bounds.MinY) || !IsFinite(bounds.MinZ) ||
                !IsFinite(bounds.MaxX) || !IsFinite(bounds.MaxY) || !IsFinite(bounds.MaxZ) ||
                bounds.MinX > bounds.MaxX || bounds.MinY > bounds.MaxY || bounds.MinZ > bounds.MaxZ)
            {
                throw new ArgumentException("Bounds must be finite and ordered.", nameof(bounds));
            }
        }

        private static void ValidateDistance(double value, string parameterName)
        {
            if (!IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and greater than or equal to 0.");
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private sealed class Node
        {
            public Node(MarkupFrameBounds bounds, int index)
            {
                Bounds = bounds;
                Index = index;
                Parent = index;
            }

            public MarkupFrameBounds Bounds { get; }
            public int Index { get; }
            public int Parent { get; set; }
        }
    }

    public sealed class MarkupFrameGroupingLimitExceededException : InvalidOperationException
    {
        public MarkupFrameGroupingLimitExceededException(int mergeItemCount, long candidateComparisons, int maxMergeItems, long maxCandidateComparisons)
            : base("Markup frame merging was stopped by the safety limit. Reduce markMergeGapMm, raise markSoloMinSizeMm, or split/cluster the selection. " +
                "mergeItemCount=" + mergeItemCount + ", candidateComparisons=" + candidateComparisons +
                ", maxMergeItems=" + maxMergeItems + ", maxCandidateComparisons=" + maxCandidateComparisons + ".")
        {
            MergeItemCount = mergeItemCount;
            CandidateComparisons = candidateComparisons;
            MaxMergeItems = maxMergeItems;
            MaxCandidateComparisons = maxCandidateComparisons;
        }

        public int MergeItemCount { get; }
        public long CandidateComparisons { get; }
        public int MaxMergeItems { get; }
        public long MaxCandidateComparisons { get; }
    }

    public sealed class MarkupFrameBounds
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MinZ { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }
        public double MaxZ { get; set; }

        public double MaxHorizontalSize => Math.Max(MaxX - MinX, MaxY - MinY);
    }

    public sealed class MarkupFrameGroup
    {
        public bool IsSolo { get; set; }
        public MarkupFrameBounds Bounds { get; set; }
        public List<int> SourceIndices { get; set; } = new List<int>();
    }
}
