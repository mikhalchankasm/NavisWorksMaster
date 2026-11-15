using System.Reflection;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #444 slice alpha. The FBX-by-level job exports each Selection Tree item at level N to its own
// file: hide the rest, DriveIOPlugin("LcFbxExporterPlugin"), unhide, next. The probe on 2027
// (comment 6884583699 on #444) showed the exporter honours hidden items and opens no window.
// This pins the pure planner: which items, in which order, under which file names, what to hide
// for each and what to unhide afterwards so that the user's own hidden items stay hidden.
public sealed class FbxLevelExportPlanTests
{
    private sealed class Node : IFbxTreeNode
    {
        private readonly List<IFbxTreeNode> _children = new();

        public Node(string name, string className = "Group", bool hidden = false, params Node[] children)
        {
            Name = name;
            ClassName = className;
            IsHidden = hidden;
            _children.AddRange(children);
            foreach (var child in children)
                child.Parent = this;
        }

        public string Name { get; }
        public string ClassName { get; }
        public bool IsHidden { get; }
        public IFbxTreeNode? Parent { get; private set; }
        public IEnumerable<IFbxTreeNode> Children => _children;
    }

    // F1 { A { a1, a2 }, B { b1 }, G }   F2 { C, D { d1 } }   (G and C have no children)
    private static (Node[] roots, Dictionary<string, Node> byName) Model(params string[] hidden)
    {
        Node N(string name, params Node[] children) => new(name, "Group", hidden.Contains(name), children);
        var nodes = new Dictionary<string, Node>();
        Node Add(Node node) { nodes[node.Name] = node; return node; }
        var a = Add(N("A", Add(N("a1")), Add(N("a2"))));
        var b = Add(N("B", Add(N("b1"))));
        var g = Add(N("G"));
        var f1 = Add(N("F1", a, b, g));
        var c = Add(N("C"));
        var d = Add(N("D", Add(N("d1"))));
        var f2 = Add(N("F2", c, d));
        return (new[] { f1, f2 }, nodes);
    }

    private static FbxLevelExportPlan Build(IReadOnlyList<IFbxTreeNode> roots, int level)
    {
        var plan = FbxLevelExportPlan.Build(roots, level, out var error);
        Assert.True(error == null, error);
        Assert.NotNull(plan);
        return plan;
    }

    private static string[] Names(IEnumerable<IFbxTreeNode> nodes) => nodes.Select(n => n.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Fact]
    public void LevelOneIsTheRootItemsAndDeeperLevelsFollowTreeOrder()
    {
        var (roots, _) = Model();
        Assert.Equal(new[] { "F1", "F2" }, Build(roots, 1).Items.Select(i => i.Item.Name));
        Assert.Equal(new[] { "A", "B", "G", "C", "D" }, Build(roots, 2).Items.Select(i => i.Item.Name));
        Assert.Equal(new[] { "a1", "a2", "b1", "d1" }, Build(roots, 3).Items.Select(i => i.Item.Name));
        Assert.Equal(new[] { 1, 2, 3, 4 }, Build(roots, 3).Items.Select(i => i.Index));
    }

    [Fact]
    public void EachItemHidesTheSiblingsOfItselfAndOfEveryAncestor()
    {
        var (roots, _) = Model();
        var plan = Build(roots, 3);
        var a1 = plan.Items.Single(i => i.Item.Name == "a1");
        // a2 is a1's sibling; B and G are A's; F2 is F1's. G holds shallow geometry outside every level-3 item.
        Assert.Equal(new[] { "B", "F2", "G", "a2" }, Names(FbxLevelExportPlan.HideSet(a1)));
        // d1 has no siblings; C is D's; F1 is F2's, which also hides everything under F1.
        Assert.Equal(new[] { "C", "F1" }, Names(FbxLevelExportPlan.HideSet(plan.Items.Single(i => i.Item.Name == "d1"))));
        Assert.Equal(new[] { "F2" }, Names(FbxLevelExportPlan.HideSet(Build(roots, 1).Items[0])));
    }

    [Fact]
    public void TheUsersHiddenItemsAreSkippedAndNeverUnhidden()
    {
        var (roots, _) = Model("a2", "B");
        var plan = Build(roots, 3);

        Assert.Equal(new[] { "a1", "d1" }, plan.Items.Select(i => i.Item.Name));
        Assert.Equal(new[] { "a2", "b1" }, plan.Skipped.Select(s => s.Item.Name));
        Assert.All(plan.Skipped, s => Assert.False(string.IsNullOrWhiteSpace(s.Reason)));
        // Numbers stay those of the full level, so names do not shift when the user hides something.
        Assert.Equal(new[] { 1, 4 }, plan.Items.Select(i => i.Index));
        Assert.Equal(new[] { 2, 3 }, plan.Skipped.Select(s => s.Index));

        var a1 = plan.Items.Single(i => i.Item.Name == "a1");
        var hide = FbxLevelExportPlan.HideSet(a1);
        Assert.Equal(new[] { "B", "F2", "G", "a2" }, Names(hide));
        Assert.Equal(new[] { "F2", "G" }, Names(FbxLevelExportPlan.UnhideSet(hide)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void ALevelOutsideOneToTwentyIsRefused(int level)
    {
        var (roots, _) = Model();
        Assert.Null(FbxLevelExportPlan.Build(roots, level, out var error));
        Assert.Contains("level", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ALevelWithNoItemsIsRefused()
    {
        var (roots, _) = Model();
        Assert.Null(FbxLevelExportPlan.Build(roots, 4, out var error));
        Assert.Contains("no items", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MoreThanFiveThousandItemsIsRefusedWithTheCount()
    {
        var wide = new Node("F", "File", false, Enumerable.Range(1, 5001).Select(i => new Node("n" + i)).ToArray());
        Assert.Null(FbxLevelExportPlan.Build(new IFbxTreeNode[] { wide }, 2, out var error));
        Assert.Contains("5001", error, StringComparison.Ordinal);

        var limit = new Node("F", "File", false, Enumerable.Range(1, 5000).Select(i => new Node("n" + i)).ToArray());
        Assert.Equal(5000, Build(new IFbxTreeNode[] { limit }, 2).Items.Count);
    }

    [Fact]
    public void FileNamesAreNumberedInTreeOrderWithAtLeastThreeDigits()
    {
        var (roots, _) = Model();
        Assert.Equal(new[] { "001_a1.fbx", "002_a2.fbx", "003_b1.fbx", "004_d1.fbx" }, Build(roots, 3).Items.Select(i => i.FileName));

        var wide = new Node("F", "File", false, Enumerable.Range(1, 1200).Select(i => new Node("n" + i)).ToArray());
        var items = Build(new IFbxTreeNode[] { wide }, 2).Items;
        Assert.Equal("0001_n1.fbx", items[0].FileName);
        Assert.Equal("1200_n1200.fbx", items[1199].FileName);
    }

    [Theory]
    [InlineData("/6501.5-S", "6501.5-S")]
    [InlineData("/6501.5.АМ", "6501.5.АМ")]
    [InlineData("a<b>:c\"d/e\\f|g?h*i", "a_b__c_d_e_f_g_h_i")]
    [InlineData("  .name. . ", "name")]
    [InlineData("tab\there", "tab_here")]
    public void NamesAreCleanedForTheFileSystem(string name, string expected)
    {
        Assert.Equal(expected, FbxLevelExportPlan.CleanName(name));
        Assert.Equal("001_" + expected + ".fbx", FbxLevelExportPlan.FileName(1, 3, new Node(name)));
    }

    [Fact]
    public void LongNamesAreCutToEightyCharacters()
    {
        var cut = FbxLevelExportPlan.CleanName(new string('x', 79) + " ." + new string('y', 30));
        Assert.Equal(new string('x', 79), cut);
        Assert.Equal(80, FbxLevelExportPlan.CleanName(new string('z', 200)).Length);
    }

    [Fact]
    public void AnUnnamedItemTakesTheFirstNamedDescendantThenTheClassThenItem()
    {
        // The DXF demo: level-2 inserts have no name, their block child does.
        var insert = new Node("", "Insert", false, new Node("", "Group", false, new Node("HVAC-02", "Block")), new Node("later"));
        Assert.Equal("001_HVAC-02.fbx", FbxLevelExportPlan.FileName(1, 1, insert));
        Assert.Equal("001_Insert.fbx", FbxLevelExportPlan.FileName(1, 1, new Node("///", "Insert", false, new Node(" . "))));
        Assert.Equal("001_item.fbx", FbxLevelExportPlan.FileName(1, 1, new Node("", "")));
    }

    [Fact]
    public void TheDescendantSearchStopsAfterOneThousandNodes()
    {
        Node chain = new Node("deep-name");
        for (var i = 0; i < 1001; i++)
            chain = new Node("", "Group", false, chain);
        var top = new Node("", "Insert", false, chain);
        Assert.Equal("001_Insert.fbx", FbxLevelExportPlan.FileName(1, 1, top));
    }

    [Theory]
    [InlineData(1073741823L, 0L, false)]
    [InlineData(1073741824L, 0L, true)]
    [InlineData(1073741824L, 700_000_000L, true)]
    [InlineData(1_400_000_000L, 1_000_000_000L, false)]
    [InlineData(1_500_000_000L, 1_000_000_000L, true)]
    public void TheNextItemNeedsOneGibibyteOrOneAndAHalfTimesTheLargestFile(long free, long largest, bool expected)
    {
        Assert.Equal(expected, FbxLevelExportPlan.HasRoomForNext(free, largest));
    }

    [Fact]
    public void TheContractsCarryTheJobShape()
    {
        Assert.Equal("running", FbxLevelExportJobStates.Running);
        Assert.Equal("completed", FbxLevelExportJobStates.Completed);
        Assert.Equal("failed", FbxLevelExportJobStates.Failed);
        Assert.Equal("canceled", FbxLevelExportJobStates.Canceled);

        AssertProperties(typeof(StartFbxLevelExportRequest), "Level", "Folder", "Overwrite");
        AssertProperties(typeof(FbxLevelExportStatusRequest), "OperationId");
        AssertProperties(typeof(CancelFbxLevelExportRequest), "OperationId");
        AssertProperties(typeof(FbxLevelExportStatusResponse), "OperationId", "InstanceId", "State", "IsRunning", "Level",
            "Folder", "TotalItems", "ExportedItems", "SkippedItems", "CurrentItem", "Files", "ErrorMessage", "ElapsedMs",
            "StartedAtUtc", "CompletedAtUtc", "Message");
        AssertProperties(typeof(FbxLevelExportFile), "Index", "Name", "Path", "Bytes", "ElapsedMs");
        Assert.Equal(20, FbxLevelExportPlan.MaxLevel);
        Assert.Equal(5000, FbxLevelExportPlan.MaxItems);
    }

    private static void AssertProperties(Type type, params string[] names)
    {
        var have = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToHashSet();
        Assert.All(names, n => Assert.Contains(n, have));
    }
}
