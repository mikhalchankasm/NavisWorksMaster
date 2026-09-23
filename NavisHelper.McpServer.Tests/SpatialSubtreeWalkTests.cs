using System;
using System.Collections.Generic;
using System.Linq;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// `SpatialSubtreeWalk` is `find_items_by_bbox`'s replacement for a flat
/// `DescendantsAndSelf` enumeration: a pre-order walk that can be told, per item, to
/// skip that item's children -- the walk `isolate_by_box` always had, now shared and
/// testable. These pin the skipping semantics on a fake tree: the order, the blast
/// radius of a skip, and the laziness guarantee that a skipped subtree's children are
/// never even asked for. The Navisworks side, which reads real bounding boxes to make
/// the decision, is verified on the rig.
/// </summary>
public sealed class SpatialSubtreeWalkTests
{
    // R
    // |- A -> a1, a2
    // |- B (leaf)
    // '- G -> g1, H -> h1
    private static Node SampleTree()
    {
        return new Node("R",
            new Node("A", new Node("a1"), new Node("a2")),
            new Node("B"),
            new Node("G", new Node("g1"), new Node("H", new Node("h1"))));
    }

    [Fact]
    public void PreOrderIsYieldedWhenNothingIsSkipped()
    {
        // The same order DescendantsAndSelf gives: the parent, then its children in
        // order, depth-first. A walker that changed this order would change which
        // item a truncated scan reached first.
        var visited = WalkNames(SampleTree(), node => false);
        Assert.Equal(new[] { "R", "A", "a1", "a2", "B", "G", "g1", "H", "h1" }, visited);
    }

    [Fact]
    public void SkippingAnItemHidesExactlyItsSubtree()
    {
        // A itself is still visited -- the caller must read its box to rule it out --
        // but nothing under it.
        var visited = WalkNames(SampleTree(), node => node.Name == "A");
        Assert.Equal(new[] { "R", "A", "B", "G", "g1", "H", "h1" }, visited);
    }

    [Fact]
    public void SiblingsAndLaterBranchesKeepTheirOrder()
    {
        var full = WalkNames(SampleTree(), node => false);

        // Skipping the last branch G leaves everything before it untouched...
        var withoutG = WalkNames(SampleTree(), node => node.Name == "G");
        Assert.Equal(new[] { "R", "A", "a1", "a2", "B", "G" }, withoutG);

        // ...and skipping a middle child A leaves the later branches identical to an
        // unskipped walk, including their depth-first order. A's subtree in the full
        // walk is A, a1, a2 -- positions 1 through 3.
        var withoutA = WalkNames(SampleTree(), node => node.Name == "A");
        Assert.Equal(full.Skip(4), withoutA.Skip(2));
    }

    [Fact]
    public void ChildrenOfASkippedItemAreNeverEnumerated()
    {
        // The point of the prune is that the skipped subtree costs nothing: its
        // children are not merely unvisited, they are never requested.
        var childRequests = new List<string>();
        foreach (var node in SpatialSubtreeWalk.Walk(
                     SampleTree(),
                     node =>
                     {
                         childRequests.Add(node.Name);
                         return node.Children;
                     },
                     node => node.Name == "A"))
        {
            // Visiting happens in the loop; only the children requests are counted.
        }

        Assert.Contains("R", childRequests);
        Assert.DoesNotContain("A", childRequests);
        Assert.Contains("B", childRequests);
        Assert.Contains("H", childRequests);
    }

    [Fact]
    public void SkippingALeafIsHarmless()
    {
        var full = WalkNames(SampleTree(), node => false);
        var skippingLeafB = WalkNames(SampleTree(), node => node.Name == "B");
        Assert.Equal(full, skippingLeafB);
    }

    [Fact]
    public void ChildrenAreAskedForOnlyAfterTheItemItselfWasYielded()
    {
        // The contract the search relies on: the skip decision for an item is read
        // after the consumer's loop body has processed that item, and the children
        // are requested only then. A decision made while processing the item must
        // govern its own subtree, and the interleaving is what proves it.
        var events = new List<string>();
        foreach (var node in SpatialSubtreeWalk.Walk(
                     SampleTree(),
                     node =>
                     {
                         events.Add("children:" + node.Name);
                         return node.Children;
                     },
                     node => false))
        {
            events.Add("visit:" + node.Name);
        }

        Assert.Equal(
            new[]
            {
                "visit:R", "children:R",
                "visit:A", "children:A", "visit:a1", "children:a1", "visit:a2", "children:a2",
                "visit:B", "children:B",
                "visit:G", "children:G", "visit:g1", "children:g1", "visit:H", "children:H",
                "visit:h1", "children:h1",
            },
            events);
    }

    private static List<string> WalkNames(Node root, Func<Node, bool> skipChildrenOf)
    {
        var names = new List<string>();
        foreach (var node in SpatialSubtreeWalk.Walk(root, node => node.Children, skipChildrenOf))
            names.Add(node.Name);
        return names;
    }

    private sealed class Node
    {
        public Node(string name, params Node[] children)
        {
            Name = name;
            Children = children;
        }

        public string Name { get; }

        public Node[] Children { get; }
    }
}
