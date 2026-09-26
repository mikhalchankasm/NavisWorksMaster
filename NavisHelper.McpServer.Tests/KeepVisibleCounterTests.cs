using System;
using System.Collections.Generic;
using System.Linq;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class KeepVisibleCounterTests
{
    [Fact]
    public void Count_MatchesNaiveUnionOnRandomTreesAndSelections()
    {
        for (var seed = 0; seed < 24; seed++)
        {
            var random = new Random(seed);
            var tree = FakeTree.BuildRandom(random, 200 + random.Next(200));

            var selectionIds = new HashSet<int>();
            for (var id = 0; id < tree.NodeCount; id++)
            {
                if (random.Next(4) == 0)
                    selectionIds.Add(id);
            }

            var nested = random.Next(1, tree.NodeCount);
            selectionIds.Add(nested);
            selectionIds.Add(tree.ParentOf(nested));
            selectionIds.Add(random.Next(tree.NodeCount));

            var selection = selectionIds.Select(tree.Node).ToList();
            selection.Add(selection[0]);
            selection.Insert(random.Next(selection.Count + 1), null);

            var expected = NaiveUnionCount(selection);
            var actual = KeepVisibleCounter.Count(selection, node => node.Parent, node => node.Children);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Count_SelectingRootGivesWholeTreeSize()
    {
        var random = new Random(1234);
        var tree = FakeTree.BuildRandom(random, 350);

        var actual = KeepVisibleCounter.Count(new[] { tree.Node(0) }, node => node.Parent, node => node.Children);

        Assert.Equal(tree.NodeCount, actual);
    }

    [Fact]
    public void Count_TwoDisjointLeavesShareAncestorsOnce()
    {
        var tree = FakeTree.BuildFromParents(new[] { -1, 0, 1, 0, 3 });

        var actual = KeepVisibleCounter.Count(
            new[] { tree.Node(2), tree.Node(4) }, node => node.Parent, node => node.Children);

        Assert.Equal(5, actual);
    }

    [Fact]
    public void Count_AncestorAndDescendantSelectionCountsAncestorSubtreeOnce()
    {
        var tree = FakeTree.BuildFromParents(new[] { -1, 0, 1, 2, 1, 4 });

        var actual = KeepVisibleCounter.Count(
            new[] { tree.Node(1), tree.Node(3) }, node => node.Parent, node => node.Children);

        Assert.Equal(6, actual);
    }

    [Fact]
    public void Count_EmptySelectionReturnsZero()
    {
        var actual = KeepVisibleCounter.Count(Array.Empty<FakeNode>(), node => node.Parent, node => node.Children);

        Assert.Equal(0, actual);
    }

    [Fact]
    public void Count_NullSelectedItemsAreIgnored()
    {
        var tree = FakeTree.BuildFromParents(new[] { -1, 0, 1 });

        var actual = KeepVisibleCounter.Count(
            new FakeNode[] { null, tree.Node(2), null }, node => node.Parent, node => node.Children);

        Assert.Equal(NaiveUnionCount(new[] { tree.Node(2) }), actual);
    }

    private static int NaiveUnionCount(IEnumerable<FakeNode> selection)
    {
        var seen = new HashSet<FakeNode>();
        var distinctSelected = new HashSet<FakeNode>(selection.Where(node => node != null));
        foreach (var node in distinctSelected)
        {
            var pending = new Stack<FakeNode>();
            pending.Push(node);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                seen.Add(current);
                foreach (var child in current.Children)
                    pending.Push(child);
            }

            var ancestor = node.Parent;
            while (ancestor != null)
            {
                seen.Add(ancestor);
                ancestor = ancestor.Parent;
            }
        }

        return seen.Count;
    }

    private sealed class FakeNode
    {
        private readonly FakeTree tree;

        internal FakeNode(FakeTree tree, int id)
        {
            this.tree = tree;
            Id = id;
        }

        internal int Id { get; }

        internal FakeNode Parent => Id == 0 ? null : new FakeNode(tree, tree.ParentOf(Id));

        internal IEnumerable<FakeNode> Children => tree.ChildIdsOf(Id).Select(childId => new FakeNode(tree, childId));

        public override bool Equals(object obj)
        {
            return obj is FakeNode other && other.Id == Id;
        }

        public override int GetHashCode()
        {
            return Id;
        }
    }

    private sealed class FakeTree
    {
        private readonly int[] parentOf;
        private readonly int[][] childrenOf;

        private FakeTree(int[] parentOf, int[][] childrenOf)
        {
            this.parentOf = parentOf;
            this.childrenOf = childrenOf;
        }

        internal int NodeCount => parentOf.Length;

        internal static FakeTree BuildRandom(Random random, int nodeCount)
        {
            var parents = new int[nodeCount];
            parents[0] = -1;
            for (var id = 1; id < nodeCount; id++)
                parents[id] = random.Next(id);
            return BuildFromParents(parents);
        }

        internal static FakeTree BuildFromParents(int[] parentOf)
        {
            var children = new List<int>[parentOf.Length];
            for (var id = 0; id < parentOf.Length; id++)
                children[id] = new List<int>();
            for (var id = 1; id < parentOf.Length; id++)
                children[parentOf[id]].Add(id);
            return new FakeTree((int[])parentOf.Clone(), children.Select(list => list.ToArray()).ToArray());
        }

        internal FakeNode Node(int id)
        {
            return new FakeNode(this, id);
        }

        internal int ParentOf(int id)
        {
            return parentOf[id];
        }

        internal IReadOnlyList<int> ChildIdsOf(int id)
        {
            return childrenOf[id];
        }
    }
}
