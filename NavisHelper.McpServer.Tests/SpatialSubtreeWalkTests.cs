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

    [Fact]
    public void ReleaseIsCalledOncePerYieldedItem()
    {
        var yielded = new List<string>();
        var released = new List<string>();
        foreach (var node in SpatialSubtreeWalk.Walk(
                     SampleTree(),
                     node => node.Children,
                     node => false,
                     node => released.Add(node.Name)))
        {
            yielded.Add(node.Name);
        }

        // Every item the walk yielded is released, and no item twice: same
        // multiset of names, no repeats.
        Assert.Equal(9, yielded.Count);
        Assert.Equal(yielded.Count, released.Count);
        Assert.Equal(
            yielded.OrderBy(name => name, StringComparer.Ordinal),
            released.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(released.Count, released.Distinct().Count());
    }

    [Fact]
    public void ReleaseFollowsTheItemsDescendants()
    {
        var released = new List<string>();
        foreach (var node in SpatialSubtreeWalk.Walk(
                     SampleTree(),
                     node => node.Children,
                     node => false,
                     node => released.Add(node.Name)))
        {
            // The releases are recorded in the callback; the assertion is below.
        }

        // Every parent is released only after all of its descendants -- A after
        // a1 and a2, H after h1, R last of all -- the order a walk that owns
        // native wrappers must keep, because the descendants are reached
        // through the parent.
        Assert.Equal(new[] { "a1", "a2", "A", "B", "g1", "h1", "H", "G", "R" }, released);
    }

    [Fact]
    public void SkippedSubtreesAreReleasedToo()
    {
        var events = new List<string>();
        foreach (var node in SpatialSubtreeWalk.Walk(
                     SampleTree(),
                     node => node.Children,
                     node => node.Name == "A",
                     node => events.Add("release:" + node.Name)))
        {
            events.Add("visit:" + node.Name);
        }

        // A was yielded, so it is released -- right after the consumer saw it,
        // before the walk moves on to B, because none of A's descendants will
        // be walked. Everything else keeps the children-before-parent order.
        Assert.Equal(
            new[]
            {
                "visit:R",
                "visit:A", "release:A",
                "visit:B", "release:B",
                "visit:G", "visit:g1", "release:g1", "visit:H", "visit:h1",
                "release:h1", "release:H", "release:G", "release:R",
            },
            events);
    }

    [Fact]
    public void ItemsHiddenByASkipAreNeverReleased()
    {
        var released = new List<string>();
        foreach (var node in SpatialSubtreeWalk.Walk(
                     SampleTree(),
                     node => node.Children,
                     node => node.Name == "A" || node.Name == "G",
                     node => released.Add(node.Name)))
        {
            // Only the releases matter here.
        }

        // a1, a2, g1, H and h1 were never yielded, so they are never released;
        // A and G themselves were, each exactly once.
        Assert.Equal(new[] { "A", "B", "G", "R" }, released);
    }

    [Fact]
    public void AnAbandonedWalkStillReleasesEveryYieldedItem()
    {
        var released = new List<string>();
        foreach (var node in SpatialSubtreeWalk.Walk(
                     SampleTree(),
                     node => node.Children,
                     node => false,
                     node => released.Add(node.Name)))
        {
            if (node.Name == "a1")
                break;
        }

        // The search breaks out of the walk when its budget runs out. The item
        // in flight and every ancestor still on the stack are released, innermost
        // first, and so are the siblings already copied out but never reached
        // (a2 under A; B and G under R). Children never asked for (g1, H, h1)
        // were never obtained, so they are not released.
        Assert.Equal(new[] { "a1", "a2", "A", "B", "G", "R" }, released);
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
