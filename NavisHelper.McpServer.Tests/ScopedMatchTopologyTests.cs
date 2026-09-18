using NavisHelper.Agent.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// These tests are the argument that the native scoped find_items returns the
/// same set as the manual traversal it replaces. The traversal produced
/// matchDepth=first by not descending below a match; the native path produces it
/// by discarding a match that has a matching ancestor. The cases below are the
/// tree shapes where those two could disagree.
/// </summary>
public sealed class ScopedMatchTopologyTests
{
    private sealed class Node
    {
        public Node(string name, Node parent = null)
        {
            Name = name;
            Parent = parent;
        }

        public string Name { get; }

        public Node Parent { get; }

        public override string ToString() => Name;
    }

    private static Node Child(Node parent, string name) => new Node(name, parent);

    [Fact]
    public void DepthOf_CountsAncestors()
    {
        var root = new Node("root");
        var level1 = Child(root, "level1");
        var level2 = Child(level1, "level2");

        Assert.Equal(0, ScopedMatchTopology.DepthOf(root, node => node.Parent));
        Assert.Equal(1, ScopedMatchTopology.DepthOf(level1, node => node.Parent));
        Assert.Equal(2, ScopedMatchTopology.DepthOf(level2, node => node.Parent));
    }

    [Fact]
    public void DepthOf_NullItemIsZero()
    {
        Assert.Equal(0, ScopedMatchTopology.DepthOf<Node>(null, node => node.Parent));
    }

    [Fact]
    public void KeepTopMostMatches_DropsAMatchUnderAnotherMatch()
    {
        var root = new Node("root");
        var parent = Child(root, "parent");
        var child = Child(parent, "child");

        var kept = ScopedMatchTopology.KeepTopMostMatches(
            new[] { parent, child },
            node => node.Parent);

        Assert.Equal(new[] { parent }, kept);
    }

    [Fact]
    public void KeepTopMostMatches_KeepsAMatchUnderANonMatchingIntermediateNode()
    {
        // The manual traversal descended through a non-matching node and still
        // reported the deeper match. Discarding it here would be a behavior
        // change, not an optimization.
        var root = new Node("root");
        var intermediate = Child(root, "intermediate");
        var deep = Child(intermediate, "deep");

        var kept = ScopedMatchTopology.KeepTopMostMatches(
            new[] { deep },
            node => node.Parent);

        Assert.Equal(new[] { deep }, kept);
    }

    [Fact]
    public void KeepTopMostMatches_DropsAGrandchildUnderAMatchingGrandparent()
    {
        var root = new Node("root");
        var grandparent = Child(root, "grandparent");
        var middle = Child(grandparent, "middle");
        var grandchild = Child(middle, "grandchild");

        var kept = ScopedMatchTopology.KeepTopMostMatches(
            new[] { grandparent, grandchild },
            node => node.Parent);

        Assert.Equal(new[] { grandparent }, kept);
    }

    [Fact]
    public void KeepTopMostMatches_KeepsSiblingBranchesIndependently()
    {
        var root = new Node("root");
        var left = Child(root, "left");
        var leftChild = Child(left, "leftChild");
        var right = Child(root, "right");
        var rightChild = Child(right, "rightChild");

        var kept = ScopedMatchTopology.KeepTopMostMatches(
            new[] { left, leftChild, rightChild },
            node => node.Parent);

        Assert.Equal(new[] { left, rightChild }, kept);
    }

    [Fact]
    public void KeepTopMostMatches_PreservesInputOrder()
    {
        var root = new Node("root");
        var first = Child(root, "a");
        var second = Child(root, "b");
        var third = Child(root, "c");

        var kept = ScopedMatchTopology.KeepTopMostMatches(
            new[] { third, first, second },
            node => node.Parent);

        Assert.Equal(new[] { third, first, second }, kept);
    }

    [Fact]
    public void KeepTopMostMatches_KeepsAMatchingRoot()
    {
        var root = new Node("root");
        var child = Child(root, "child");

        var kept = ScopedMatchTopology.KeepTopMostMatches(
            new[] { root, child },
            node => node.Parent);

        Assert.Equal(new[] { root }, kept);
    }

    [Fact]
    public void KeepTopMostMatches_HandlesEmptyAndSingleInput()
    {
        var root = new Node("root");

        Assert.Empty(ScopedMatchTopology.KeepTopMostMatches(new Node[0], node => node.Parent));
        Assert.Empty(ScopedMatchTopology.KeepTopMostMatches<Node>(null, node => node.Parent));
        Assert.Equal(new[] { root }, ScopedMatchTopology.KeepTopMostMatches(new[] { root }, node => node.Parent));
    }

    [Fact]
    public void KeepTopMostMatches_SkipsNullEntries()
    {
        var root = new Node("root");
        var child = Child(root, "child");

        var kept = ScopedMatchTopology.KeepTopMostMatches(
            new Node[] { null, root, null, child },
            node => node.Parent);

        Assert.Equal(new[] { root }, kept);
    }

    [Fact]
    public void KeepTopMostMatches_DeepChainKeepsOnlyTheTopMostMatch()
    {
        var current = new Node("n0");
        var chain = new Node[12];
        chain[0] = current;
        for (var index = 1; index < chain.Length; index++)
        {
            current = Child(current, "n" + index);
            chain[index] = current;
        }

        // Every node on a 12-deep chain matches; only the top one survives.
        var kept = ScopedMatchTopology.KeepTopMostMatches(chain, node => node.Parent);

        Assert.Equal(new[] { chain[0] }, kept);
    }
}
