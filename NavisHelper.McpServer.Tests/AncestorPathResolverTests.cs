using System;
using System.Collections.Generic;
using System.Linq;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// `AncestorPathResolver` is the clash-matrix walk's replacement for climbing
/// `Parent` to the root twice per scanned item -- once for the display path,
/// once for the source file. These pin, on a fake tree whose parent delegate
/// returns a fresh wrapper object per call (as `ModelItem.Parent` does) and
/// whose equality compares the shared node data (as `ModelItem` equality
/// compares the native object), that a pre-order walk answers from the
/// ancestor chain alone, that any other visiting order answers identically
/// through the full-climb fallback, and that the path and inherited-value rules
/// match a naive climb. The Navisworks side, which feeds real DisplayName and
/// source-file properties, is verified on the rig.
/// </summary>
public sealed class AncestorPathResolverTests
{
    // R(1) "Root"      own "site.nwd"
    // |- A(2) "Arch"   own "arch.nwd"
    // |  |- W(3) "Wall"  own null
    // |  '- D(4) "Door"  own null
    // '- M(5) "MEP"    own "" (a value: it stops the climb)
    private static FakeData SampleTree()
    {
        var root = new FakeData(1, "Root", null, "site.nwd");
        var arch = new FakeData(2, "Arch", root, "arch.nwd");
        new FakeData(3, "Wall", arch, null);
        new FakeData(4, "Door", arch, null);
        new FakeData(5, "MEP", root, string.Empty);
        return root;
    }

    private static List<FakeData> PreOrder(FakeData root)
    {
        var result = new List<FakeData>();
        CollectPreOrder(root, result);
        return result;
    }

    private static void CollectPreOrder(FakeData node, List<FakeData> result)
    {
        result.Add(node);
        foreach (var child in node.Children)
            CollectPreOrder(child, result);
    }

    private static FakeData Find(FakeData root, int id)
    {
        if (root.Id == id)
            return root;
        foreach (var child in root.Children)
        {
            var found = Find(child, id);
            if (found != null)
                return found;
        }

        return null;
    }

    private static string NaivePath(FakeData node)
    {
        var segments = new List<string>();
        for (var current = node; current != null; current = current.Parent)
            segments.Add(current.Segment);
        segments.Reverse();
        return string.Join(" / ", segments);
    }

    private static string NaiveValue(FakeData node)
    {
        for (var current = node; current != null; current = current.Parent)
        {
            if (current.OwnValue != null)
                return current.OwnValue;
        }

        return null;
    }

    private static int Depth(FakeData node)
    {
        var depth = 1;
        for (var current = node.Parent; current != null; current = current.Parent)
            depth++;
        return depth;
    }

    [Fact]
    public void PreOrderMatchesNaiveClimbForEveryNodeWithoutFullWalks()
    {
        var root = SampleTree();
        var harness = new ResolverHarness();
        var resolver = harness.CreateResolver();
        foreach (var node in PreOrder(root))
        {
            string path;
            string value;
            resolver.Resolve(new FakeNode(node), out path, out value);
            Assert.Equal(NaivePath(node), path);
            Assert.Equal(NaiveValue(node), value);
        }

        // Every answer came from the chain; nothing had to climb to the root.
        Assert.Equal(0, resolver.FullWalks);
    }

    [Fact]
    public void PostOrderStillMatchesNaiveClimbButNeedsFullWalks()
    {
        var root = SampleTree();
        var harness = new ResolverHarness();
        var resolver = harness.CreateResolver();
        var order = new[] { 3, 4, 2, 5, 1 }; // Wall, Door, Arch, MEP, Root -- deepest first

        foreach (var id in order)
        {
            var node = Find(root, id);
            string path;
            string value;
            resolver.Resolve(new FakeNode(node), out path, out value);
            Assert.Equal(NaivePath(node), path);
            Assert.Equal(NaiveValue(node), value);
        }

        // Wall's parent was never resolved before it, so at least one Resolve
        // had to climb to the root; the answers stay identical anyway.
        Assert.True(resolver.FullWalks > 0, "post-order must exercise the full-walk fallback");
    }

    [Fact]
    public void ShuffledOrderStillMatchesNaiveClimb()
    {
        var root = SampleTree();
        var harness = new ResolverHarness();
        var resolver = harness.CreateResolver();
        var order = new[]
        {
            2, // Arch -- the chain is empty: full walk
            3, // Wall -- fast path off Arch
            5, // MEP -- truncates the chain back to Root
            4, // Door -- Arch is gone from the chain: another full walk
            1, // Root -- restarts the chain
        };

        foreach (var id in order)
        {
            var node = Find(root, id);
            string path;
            string value;
            resolver.Resolve(new FakeNode(node), out path, out value);
            Assert.Equal(NaivePath(node), path);
            Assert.Equal(NaiveValue(node), value);
        }

        Assert.True(resolver.FullWalks > 0, "this order must exercise the full-walk fallback");
    }

    [Fact]
    public void OwnValueWinsOverEveryAncestorValue()
    {
        var root = new FakeData(1, "Root", null, "site.nwd");
        var child = new FakeData(2, "Child", root, "child.rvt");
        var resolver = new ResolverHarness().CreateResolver();

        string path;
        string value;
        resolver.Resolve(new FakeNode(root), out path, out value);
        Assert.Equal("site.nwd", value);
        resolver.Resolve(new FakeNode(child), out path, out value);
        Assert.Equal("Root / Child", path);
        Assert.Equal("child.rvt", value);
    }

    [Fact]
    public void NearestAncestorValueIsInherited()
    {
        var root = new FakeData(1, "Root", null, "site.nwd");
        var mid = new FakeData(2, "Mid", root, "mid.nwd");
        var leaf = new FakeData(3, "Leaf", mid, null);
        var resolver = new ResolverHarness().CreateResolver();

        string path;
        string value;
        resolver.Resolve(new FakeNode(root), out path, out value);
        resolver.Resolve(new FakeNode(mid), out path, out value);
        resolver.Resolve(new FakeNode(leaf), out path, out value);
        Assert.Equal("Root / Mid / Leaf", path);
        // The nearest ancestor that carries a value, not the farthest.
        Assert.Equal("mid.nwd", value);
    }

    [Fact]
    public void EmptyOwnValueStopsTheClimb()
    {
        var root = new FakeData(1, "Root", null, "site.nwd");
        var child = new FakeData(2, "Child", root, string.Empty);
        var resolver = new ResolverHarness().CreateResolver();

        string path;
        string value;
        resolver.Resolve(new FakeNode(root), out path, out value);
        resolver.Resolve(new FakeNode(child), out path, out value);
        // Empty is a present value, exactly like a source-file property whose
        // display value is blank: the ancestor's value must not leak through.
        Assert.Equal(string.Empty, value);
    }

    [Fact]
    public void NoValueAnywhereGivesNull()
    {
        var root = new FakeData(1, "Root", null, null);
        var child = new FakeData(2, "Child", root, null);
        var resolver = new ResolverHarness().CreateResolver();

        string path;
        string value;
        resolver.Resolve(new FakeNode(root), out path, out value);
        Assert.Null(value);
        resolver.Resolve(new FakeNode(child), out path, out value);
        Assert.Equal("Root / Child", path);
        Assert.Null(value);
    }

    [Fact]
    public void ChainDepthNeverExceedsTheDepthOfTheTree()
    {
        var maxDepth = 3; // Root / Arch / Wall
        var orders = new[]
        {
            new[] { 1, 2, 3, 4, 5 }, // pre-order
            new[] { 3, 4, 2, 5, 1 }, // post-order
            new[] { 2, 3, 5, 4, 1 }, // shuffled
        };

        foreach (var order in orders)
        {
            var root = SampleTree();
            var resolver = new ResolverHarness().CreateResolver();
            foreach (var id in order)
            {
                var node = Find(root, id);
                string path;
                string value;
                resolver.Resolve(new FakeNode(node), out path, out value);
                Assert.True(resolver.ChainDepth <= maxDepth,
                    "id " + id + " left " + resolver.ChainDepth + " chain entries");
                Assert.Equal(Depth(node), resolver.ChainDepth);
            }
        }
    }

    [Fact]
    public void ParentIsCalledAtMostOncePerNodeInPreOrder()
    {
        var root = SampleTree();
        var harness = new ResolverHarness();
        var resolver = harness.CreateResolver();
        var expected = PreOrder(root).Select(node => node.Id).ToList();
        foreach (var id in expected)
        {
            string path;
            string value;
            resolver.Resolve(new FakeNode(Find(root, id)), out path, out value);
        }

        // The fast path asks for each node's parent once and never re-asks for an
        // ancestor's; a second call per node would mean the chain missed.
        Assert.Equal(expected.Count, harness.ParentCalls.Count);
        foreach (var entry in harness.ParentCalls)
            Assert.True(entry.Value <= 1, "parent called " + entry.Value + " times for id " + entry.Key);
        Assert.Equal(expected.Count, harness.ParentCalls.Values.Sum());
    }

    private sealed class FakeData
    {
        public FakeData(int id, string segment, FakeData parent, string ownValue)
        {
            Id = id;
            Segment = segment;
            Parent = parent;
            OwnValue = ownValue;
            Children = new List<FakeData>();
            if (parent != null)
                parent.Children.Add(this);
        }

        public int Id { get; }

        public string Segment { get; }

        public FakeData Parent { get; }

        public string OwnValue { get; }

        public List<FakeData> Children { get; }
    }

    private sealed class FakeNode
    {
        public FakeNode(FakeData data)
        {
            Data = data;
        }

        public FakeData Data { get; }

        // ModelItem.Parent returns a fresh wrapper per call while ModelItem
        // equality compares the native object, so equality here compares the
        // shared payload, never the wrapper.
        public override bool Equals(object obj)
        {
            var other = obj as FakeNode;
            return other != null && other.Data.Id == Data.Id;
        }

        public override int GetHashCode()
        {
            return Data.Id;
        }
    }

    private sealed class ResolverHarness
    {
        public readonly Dictionary<int, int> ParentCalls = new Dictionary<int, int>();

        public AncestorPathResolver<FakeNode> CreateResolver()
        {
            return new AncestorPathResolver<FakeNode>(
                node =>
                {
                    int count;
                    ParentCalls.TryGetValue(node.Data.Id, out count);
                    ParentCalls[node.Data.Id] = count + 1;
                    // A fresh wrapper per call: reference equality would never hit.
                    return node.Data.Parent == null ? null : new FakeNode(node.Data.Parent);
                },
                node => node.Data.Segment,
                node => node.Data.OwnValue,
                " / ");
        }
    }
}
