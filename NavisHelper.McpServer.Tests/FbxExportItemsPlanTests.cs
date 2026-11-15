using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #444 redesign, slice epsilon1 (owner, 2026-11-15): the export source is a list of items the user
// picked (panel "Add selected", MCP matchHandles or the current selection) plus a depth, not a
// level counted from the root. The plan climbs Parent from each item to build the branch it hides
// around; naming, hiding, restoring, skipping and limits stay as alpha decided.
//
// The Navisworks adapter makes a new wrapper on every walk, so the item a user picked and the copy
// of it in Parent.Children are different instances. Node identity must therefore be equality, not
// reference: with reference identity the item would land in its own hide set.
public sealed class FbxExportItemsPlanTests
{
    // Equality by Id, like ModelItem; Proxy() is a second wrapper of the same tree node.
    private sealed class Node : IFbxTreeNode
    {
        private readonly List<Node> _children;

        public Node(string name, bool hidden = false, params Node[] children)
            : this(name, name, hidden, null, new List<Node>(children))
        {
            foreach (var child in children)
                child.Parent = this;
        }

        private Node(string id, string name, bool hidden, IFbxTreeNode? parent, List<Node> children)
        {
            Id = id;
            Name = name;
            IsHidden = hidden;
            Parent = parent;
            _children = children;
        }

        public string Id { get; }
        public string Name { get; }
        public string ClassName => "Group";
        public bool IsHidden { get; }
        public IFbxTreeNode? Parent { get; private set; }
        public IEnumerable<IFbxTreeNode> Children => _children.Select(child => child.Proxy());

        public Node Proxy() => new(Id, Name, IsHidden, Parent, _children);
        public override bool Equals(object? obj) => obj is Node other && other.Id == Id;
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Id);
        public override string ToString() => Id;
    }

    // F1 { A { a1, a2 }, B { b1 } }   F2 { C { c1 } }
    private static (Node[] roots, Dictionary<string, Node> byName) Model(params string[] hidden)
    {
        var nodes = new Dictionary<string, Node>();
        Node N(string name, params Node[] children)
        {
            var node = new Node(name, hidden.Contains(name), children);
            nodes[name] = node;
            return node;
        }
        var f1 = N("F1", N("A", N("a1"), N("a2")), N("B", N("b1")));
        var f2 = N("F2", N("C", N("c1")));
        return (new[] { f1, f2 }, nodes);
    }

    private static FbxLevelExportPlan Plan(Node[] roots, int depth, params Node[] items)
    {
        var plan = FbxLevelExportPlan.BuildFromItems(roots, items, depth, out var error);
        Assert.True(error == null, error);
        return plan;
    }

    private static string[] Names(IEnumerable<IFbxTreeNode> nodes) => nodes.Select(node => node.Name).ToArray();

    [Fact]
    public void DepthZeroExportsTheListedItemsInListOrder()
    {
        var (roots, n) = Model();
        var plan = Plan(roots, 0, n["B"].Proxy(), n["a1"].Proxy());

        Assert.Equal(new[] { "001_B.fbx", "002_a1.fbx" }, plan.Items.Select(item => item.FileName));
        Assert.Equal(2, plan.TotalItems);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void TheHideSetIsTheSiblingsUpToTheRootAndNeverTheItemItself()
    {
        var (roots, n) = Model();
        var plan = Plan(roots, 0, n["B"].Proxy(), n["a1"].Proxy());

        Assert.Equal(new[] { "A", "F2" }, Names(FbxLevelExportPlan.HideSet(plan.Items[0])));
        Assert.Equal(new[] { "a2", "B", "F2" }, Names(FbxLevelExportPlan.HideSet(plan.Items[1])));
        Assert.DoesNotContain(FbxLevelExportPlan.HideSet(plan.Items[1]), node => node.Equals(n["a1"]));
        Assert.DoesNotContain(FbxLevelExportPlan.HideSet(plan.Items[1]), node => node.Equals(n["A"]));
    }

    [Fact]
    public void DepthExpandsEachItemInTreeOrder()
    {
        var (roots, n) = Model();

        var one = Plan(roots, 1, n["A"].Proxy(), n["F2"].Proxy());
        Assert.Equal(new[] { "001_a1.fbx", "002_a2.fbx", "003_C.fbx" }, one.Items.Select(item => item.FileName));
        Assert.Equal(new[] { "F1" }, Names(FbxLevelExportPlan.HideSet(one.Items[2])));

        var two = Plan(roots, 2, n["F1"].Proxy());
        Assert.Equal(new[] { "a1", "a2", "b1" }, Names(two.Items.Select(item => item.Item)));
    }

    [Fact]
    public void RepeatedItemsAndRepeatedResultsAreExportedOnce()
    {
        var (roots, n) = Model();

        var listed = Plan(roots, 0, n["B"].Proxy(), n["B"].Proxy(), n["a1"].Proxy());
        Assert.Equal(new[] { "B", "a1" }, Names(listed.Items.Select(item => item.Item)));

        // B listed twice at depth 1 gives its child b1 once.
        var expanded = Plan(roots, 1, n["B"].Proxy(), n["B"].Proxy());
        Assert.Equal(new[] { "b1" }, Names(expanded.Items.Select(item => item.Item)));
        Assert.Equal(1, expanded.TotalItems);
    }

    [Fact]
    public void AnItemUnderAHiddenAncestorIsSkipped()
    {
        var (roots, n) = Model("A");
        var plan = Plan(roots, 0, n["a1"].Proxy(), n["B"].Proxy());

        Assert.Equal(new[] { "B" }, Names(plan.Items.Select(item => item.Item)));
        var skip = Assert.Single(plan.Skipped);
        Assert.Equal("a1", skip.Item.Name);
        Assert.Equal(1, skip.Index);
        Assert.Equal("002_B.fbx", plan.Items[0].FileName);
    }

    [Theory]
    [InlineData(-1, "Export depth must be between 0 and 20.")]
    [InlineData(21, "Export depth must be between 0 and 20.")]
    public void DepthIsBounded(int depth, string expected)
    {
        var (roots, n) = Model();
        Assert.Null(FbxLevelExportPlan.BuildFromItems(roots, new IFbxTreeNode[] { n["A"] }, depth, out var error));
        Assert.Equal(expected, error);
    }

    [Fact]
    public void AnEmptyListAndALeafAtDepthAreRefused()
    {
        var (roots, n) = Model();

        Assert.Null(FbxLevelExportPlan.BuildFromItems(roots, Array.Empty<IFbxTreeNode>(), 0, out var empty));
        Assert.Equal("Select at least one item to export.", empty);

        Assert.Null(FbxLevelExportPlan.BuildFromItems(roots, new IFbxTreeNode[] { n["a1"].Proxy() }, 1, out var leaf));
        Assert.Equal("The selected items have no descendants at depth 1.", leaf);
    }

    [Fact]
    public void TheAdapterClimbsParentsAndComparesModelItems()
    {
        var adapter = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "NavisworksFbxExportHost.cs"));
        foreach (var expected in new[]
                 {
                     "public IFbxTreeNode Parent", "Item.Parent", "public override bool Equals(object",
                     "Item.Equals(", "public override int GetHashCode()", "Item.GetHashCode()",
                 })
            Assert.Contains(expected, adapter, StringComparison.Ordinal);
    }
}
