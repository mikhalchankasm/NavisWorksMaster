using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #494: the listed-item export job writes Datasmith per item alongside FBX. A Datasmith step publishes
// an NWD copy of the isolated state (Publish keeps the open document's file name; probe 2026-11-16), then
// the runner exports that copy after visibility is restored, and the copy is deleted.
public sealed class ItemExportFormatsTests
{
    private static readonly DateTime T0 = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);
    private const string Folder = @"D:\out\items";
    private const string Temp = @"D:\tmp\nh-items";

    private sealed class Node : IFbxTreeNode
    {
        private readonly List<IFbxTreeNode> _children = new();
        public Node(string name, params Node[] children)
        {
            Name = name;
            _children.AddRange(children);
            foreach (var child in children) child.Parent = this;
        }
        public string Name { get; }
        public string ClassName => "Group";
        public bool IsHidden { get; set; }
        public IFbxTreeNode? Parent { get; private set; }
        public IEnumerable<IFbxTreeNode> Children => _children;
    }

    private sealed class Host : IItemExportHost
    {
        public readonly List<string> Calls = new();
        public readonly HashSet<string> Existing = new(StringComparer.OrdinalIgnoreCase);
        public string TempFolder => Temp;

        public void SetHidden(IReadOnlyList<IFbxTreeNode> nodes, bool hidden)
        {
            foreach (Node node in nodes) node.IsHidden = hidden;
            Calls.Add((hidden ? "hide " : "unhide ") + string.Join(",", nodes.Select(n => n.Name).OrderBy(n => n, StringComparer.Ordinal)));
        }
        public string Export(string path) { Calls.Add("fbx " + Path.GetFileName(path)); Existing.Add(path); return null; }
        public string PublishCopy(string path) { Calls.Add("publish " + path); Existing.Add(path); return null; }
        public string ExportDatasmith(string sourcePath, string outputPath)
        {
            Calls.Add("datasmith " + Path.GetFileName(sourcePath) + " -> " + outputPath);
            Existing.Add(outputPath);
            return null;
        }
        public void DeleteFile(string path) { Calls.Add("delete " + path); Existing.Remove(path); }
        public bool FileExists(string path) => Existing.Contains(path);
        public long FileSize(string path) => Existing.Contains(path) ? 1000 : 0;
        public long FreeBytes(string folder) => 100L * 1024 * 1024 * 1024;
    }

    // Root { A, B }: the list holds A and B themselves (depth 0).
    private static (FbxLevelExportPlan plan, Node a, Node b) Plan()
    {
        var a = new Node("A");
        var b = new Node("B");
        var root = new Node("Root", a, b);
        var plan = FbxLevelExportPlan.BuildFromItems(new IFbxTreeNode[] { root }, new IFbxTreeNode[] { a, b }, 0, out var error);
        Assert.True(error == null, error);
        return (plan, a, b);
    }

    private static void RunToEnd(FbxLevelExportJob job, IItemExportHost host)
    {
        var steps = 0;
        while (job.Step(host, () => T0.AddSeconds(++steps)) && steps < 100) { }
    }

    [Fact]
    public void BothFormats_WriteFbxAndDatasmithPerItem_InOrder()
    {
        var (plan, _, _) = Plan();
        var host = new Host();
        var job = FbxLevelExportJob.Start("items-1", plan, Folder, false, host, null, T0,
            ItemExportFormats.Fbx | ItemExportFormats.Datasmith);

        RunToEnd(job, host);

        Assert.Equal(FbxLevelExportJobStates.Completed, job.State);
        Assert.Equal(new[]
        {
            "hide B", "fbx 001_A.fbx", @"publish D:\tmp\nh-items\001_A.nwd", "unhide B",
            @"datasmith 001_A.nwd -> D:\out\items\001_A.udatasmith", @"delete D:\tmp\nh-items\001_A.nwd",
            "hide A", "fbx 002_B.fbx", @"publish D:\tmp\nh-items\002_B.nwd", "unhide A",
            @"datasmith 002_B.nwd -> D:\out\items\002_B.udatasmith", @"delete D:\tmp\nh-items\002_B.nwd",
        }, host.Calls);
        Assert.DoesNotContain(host.Existing, path => path.StartsWith(Temp, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DatasmithOnly_WritesNoFbx()
    {
        var (plan, _, _) = Plan();
        var host = new Host();
        var job = FbxLevelExportJob.Start("items-2", plan, Folder, false, host, null, T0, ItemExportFormats.Datasmith);

        RunToEnd(job, host);

        Assert.Equal(FbxLevelExportJobStates.Completed, job.State);
        Assert.DoesNotContain(host.Calls, call => call.StartsWith("fbx ", StringComparison.Ordinal));
        Assert.Contains(@"datasmith 002_B.nwd -> D:\out\items\002_B.udatasmith", host.Calls);
    }

    [Fact]
    public void ExistingDatasmithTarget_IsRefusedWithoutOverwrite()
    {
        var (plan, _, _) = Plan();
        var host = new Host();
        host.Existing.Add(@"D:\out\items\001_A.udatasmith");

        var ex = Assert.Throws<InvalidOperationException>(() => FbxLevelExportJob.Start("items-3", plan, Folder, false, host, null, T0,
            ItemExportFormats.Fbx | ItemExportFormats.Datasmith));
        Assert.Contains("001_A.udatasmith", ex.Message);
    }

    [Fact]
    public void NoFormat_IsRefused()
    {
        var (plan, _, _) = Plan();
        Assert.Throws<ArgumentException>(() => FbxLevelExportJob.Start("items-4", plan, Folder, false, new Host(), null, T0, ItemExportFormats.None));
    }
}
