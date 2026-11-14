using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #444 slice beta1. The FBX-by-level job as a pure state machine over IFbxExportHost, so the loop
// is tested without Navisworks; slice beta2 adapts ModelItem, Models.SetHidden and
// DriveIOPlugin("LcFbxExporterPlugin") and posts one Step per item to the UI thread.
// Codex review of #449: IsHidden is live, so the unhide set must be taken BEFORE hiding. The fake
// host below flips IsHidden on SetHidden exactly as Navisworks does.
public sealed class FbxLevelExportJobTests
{
    private static readonly DateTime T0 = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);
    private const string Folder = @"D:\out\fbx";

    private sealed class Node : IFbxTreeNode
    {
        private readonly List<IFbxTreeNode> _children = new();

        public Node(string name, params Node[] children)
        {
            Name = name;
            _children.AddRange(children);
        }

        public string Name { get; }
        public string ClassName => "Group";
        public bool IsHidden { get; set; }
        public IEnumerable<IFbxTreeNode> Children => _children;
    }

    private sealed class Host : IFbxExportHost
    {
        public readonly List<string> Calls = new();
        public readonly HashSet<string> Existing = new(StringComparer.OrdinalIgnoreCase);
        public Func<string, string> OnExport = _ => null;
        public long Free = 100L * 1024 * 1024 * 1024;
        public long Size = 1000;

        public void SetHidden(IReadOnlyList<IFbxTreeNode> nodes, bool hidden)
        {
            foreach (Node node in nodes)
                node.IsHidden = hidden;
            Calls.Add((hidden ? "hide " : "unhide ") + string.Join(",", nodes.Select(n => n.Name).OrderBy(n => n, StringComparer.Ordinal)));
        }

        public string Export(string path)
        {
            Calls.Add("export " + Path.GetFileName(path));
            var error = OnExport(path);
            if (error == null)
                Existing.Add(path);
            return error;
        }

        public bool FileExists(string path) => Existing.Contains(path);
        public long FileSize(string path) => Existing.Contains(path) ? Size : 0;
        public long FreeBytes(string folder) => Free;
    }

    // F1 { A { a1, a2 }, B { b1 } }   F2 { C }
    private static (FbxLevelExportPlan plan, Dictionary<string, Node> nodes) Plan(int level, params string[] hidden)
    {
        var nodes = new Dictionary<string, Node>();
        Node N(string name, params Node[] children)
        {
            var node = new Node(name, children) { IsHidden = hidden.Contains(name) };
            nodes[name] = node;
            return node;
        }
        var roots = new IFbxTreeNode[] { N("F1", N("A", N("a1"), N("a2")), N("B", N("b1"))), N("F2", N("C")) };
        var plan = FbxLevelExportPlan.Build(roots, level, out var error);
        Assert.True(error == null, error);
        return (plan, nodes);
    }

    private static FbxLevelExportJob Start(FbxLevelExportPlan plan, Host host, bool overwrite = false, FbxLevelExportJob previous = null) =>
        FbxLevelExportJob.Start("fbx-job-1", plan, Folder, overwrite, host, previous, T0);

    private static int RunToEnd(FbxLevelExportJob job, Host host)
    {
        var steps = 0;
        while (job.Step(host, T0.AddSeconds(++steps)) && steps < 100) { }
        return steps;
    }

    [Fact]
    public void EachStepHidesExportsAndRestoresOneItem()
    {
        var (plan, _) = Plan(3);
        var host = new Host();
        var job = Start(plan, host);
        Assert.Equal(FbxLevelExportJobStates.Running, job.State);
        Assert.True(job.IsRunning);
        Assert.Empty(host.Calls);

        Assert.True(job.Step(host, T0.AddSeconds(1)));
        Assert.Equal(new[] { "hide B,F2,a2", "export 001_a1.fbx", "unhide B,F2,a2" }, host.Calls);

        RunToEnd(job, host);
        Assert.Equal(FbxLevelExportJobStates.Completed, job.State);
        Assert.False(job.IsRunning);
        Assert.Equal(9, host.Calls.Count);
        Assert.Equal(new[] { "export 001_a1.fbx", "export 002_a2.fbx", "export 003_b1.fbx" },
            host.Calls.Where(c => c.StartsWith("export ", StringComparison.Ordinal)));
        Assert.False(job.Step(host, T0.AddMinutes(1)));
        Assert.Equal(9, host.Calls.Count);
    }

    [Fact]
    public void TheUnhideSetIsTakenBeforeHidingSoTheUsersHiddenNodesStayHidden()
    {
        var (plan, nodes) = Plan(3, "B");
        var host = new Host();
        var job = Start(plan, host);

        job.Step(host, T0.AddSeconds(1));
        // B was hidden by the user: hidden again with the rest, never unhidden.
        Assert.Equal(new[] { "hide B,F2,a2", "export 001_a1.fbx", "unhide F2,a2" }, host.Calls);
        Assert.True(nodes["B"].IsHidden);
        Assert.False(nodes["F2"].IsHidden);
        Assert.False(nodes["a2"].IsHidden);

        RunToEnd(job, host);
        Assert.Equal(FbxLevelExportJobStates.Completed, job.State);
        var status = job.BuildStatus(T0.AddMinutes(1));
        Assert.Equal(3, status.TotalItems);
        Assert.Equal(2, status.ExportedItems);
        Assert.Equal(1, status.SkippedItems);
    }

    [Fact]
    public void AFailedExportStillRestoresVisibilityAndStopsTheJob()
    {
        var (plan, nodes) = Plan(3);
        var host = new Host { OnExport = path => path.EndsWith("002_a2.fbx", StringComparison.Ordinal) ? "eExport_FAIL" : null };
        var job = Start(plan, host);

        RunToEnd(job, host);
        Assert.Equal(FbxLevelExportJobStates.Failed, job.State);
        Assert.Equal("unhide B,F2,a1", host.Calls.Last());
        Assert.DoesNotContain("export 003_b1.fbx", host.Calls);
        Assert.All(nodes.Values, n => Assert.False(n.IsHidden));
        var status = job.BuildStatus(T0.AddMinutes(1));
        Assert.Contains("002_a2.fbx", status.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains("eExport_FAIL", status.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal(1, status.ExportedItems);
        Assert.NotNull(status.CompletedAtUtc);
    }

    [Fact]
    public void AThrowingExportIsAFailureNotACrash()
    {
        var (plan, nodes) = Plan(3);
        var host = new Host { OnExport = _ => throw new InvalidOperationException("COM is gone") };
        var job = Start(plan, host);

        Assert.False(job.Step(host, T0.AddSeconds(1)));
        Assert.Equal(FbxLevelExportJobStates.Failed, job.State);
        Assert.Contains("COM is gone", job.BuildStatus(T0.AddSeconds(2)).ErrorMessage, StringComparison.Ordinal);
        Assert.Equal("unhide B,F2,a2", host.Calls.Last());
        Assert.All(nodes.Values, n => Assert.False(n.IsHidden));
    }

    [Fact]
    public void CancelTakesEffectAtTheNextStepWithoutTouchingVisibility()
    {
        var (plan, _) = Plan(3);
        var host = new Host();
        var job = Start(plan, host);
        job.Step(host, T0.AddSeconds(1));
        var calls = host.Calls.Count;

        Assert.True(job.Cancel(T0.AddSeconds(2)));
        Assert.Equal(FbxLevelExportJobStates.Running, job.State);
        Assert.False(job.Step(host, T0.AddSeconds(3)));
        Assert.Equal(FbxLevelExportJobStates.Canceled, job.State);
        Assert.Equal(calls, host.Calls.Count);
        Assert.False(job.Cancel(T0.AddSeconds(4)));
    }

    [Fact]
    public void LowDiskSpaceFailsBeforeHiding()
    {
        var (plan, _) = Plan(3);
        var host = new Host { Free = FbxLevelExportPlan.MinFreeBytes - 1 };
        var job = Start(plan, host);

        Assert.False(job.Step(host, T0.AddSeconds(1)));
        Assert.Equal(FbxLevelExportJobStates.Failed, job.State);
        Assert.Empty(host.Calls);
        Assert.Contains("free space", job.BuildStatus(T0.AddSeconds(2)).ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheDiskRuleUsesTheLargestFileWrittenSoFar()
    {
        var (plan, _) = Plan(3);
        var host = new Host { Free = 2_000_000_000L, Size = 1_400_000_000L };
        var job = Start(plan, host);

        Assert.True(job.Step(host, T0.AddSeconds(1)));
        // 2.0 GB free < 1.5 x 1.4 GB.
        Assert.False(job.Step(host, T0.AddSeconds(2)));
        Assert.Equal(FbxLevelExportJobStates.Failed, job.State);
        Assert.Single(host.Calls, c => c.StartsWith("export ", StringComparison.Ordinal));
    }

    [Fact]
    public void ExistingTargetsAreRefusedWithoutOverwrite()
    {
        var (plan, _) = Plan(3);
        var host = new Host();
        host.Existing.Add(Path.Combine(Folder, "002_a2.fbx"));

        var refused = Assert.Throws<InvalidOperationException>(() => Start(plan, host));
        Assert.Contains("002_a2.fbx", refused.Message, StringComparison.Ordinal);
        Assert.Contains("overwrite", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(host.Calls);

        var job = Start(plan, host, overwrite: true);
        RunToEnd(job, host);
        Assert.Equal(FbxLevelExportJobStates.Completed, job.State);
    }

    [Fact]
    public void OnlyOneJobRunsAtATime()
    {
        var (plan, _) = Plan(3);
        var host = new Host();
        var first = Start(plan, host);

        var refused = Assert.Throws<InvalidOperationException>(() => Start(plan, host, previous: first));
        Assert.Contains("already running", refused.Message, StringComparison.OrdinalIgnoreCase);

        RunToEnd(first, host);
        var second = FbxLevelExportJob.Start("fbx-job-2", plan, Folder, true, host, first, T0.AddMinutes(5));
        Assert.Equal("fbx-job-2", second.OperationId);
    }

    [Fact]
    public void ALevelWhoseItemsAreAllHiddenCompletesAtTheFirstStep()
    {
        var (plan, _) = Plan(2, "F1", "F2");
        var host = new Host();
        var job = Start(plan, host);

        Assert.False(job.Step(host, T0.AddSeconds(1)));
        Assert.Equal(FbxLevelExportJobStates.Completed, job.State);
        Assert.Empty(host.Calls);
        Assert.Equal(3, job.BuildStatus(T0.AddSeconds(2)).SkippedItems);
        Assert.Empty(job.BuildStatus(T0.AddSeconds(2)).Files);
    }

    [Fact]
    public void TheStatusReportsProgressAndTheLastTwentyFiles()
    {
        var root = new Node("F", Enumerable.Range(1, 25).Select(i => new Node("n" + i)).ToArray());
        var plan = FbxLevelExportPlan.Build(new IFbxTreeNode[] { root }, 2, out _);
        var host = new Host();
        var job = FbxLevelExportJob.Start("fbx-job-25", plan, Folder, false, host, null, T0);

        var running = job.BuildStatus(T0.AddSeconds(3));
        Assert.Equal("fbx-job-25", running.OperationId);
        Assert.Equal(FbxLevelExportJobStates.Running, running.State);
        Assert.True(running.IsRunning);
        Assert.Equal(2, running.Level);
        Assert.Equal(Folder, running.Folder);
        Assert.Equal(25, running.TotalItems);
        Assert.Equal(0, running.ExportedItems);
        Assert.Equal("001_n1.fbx", running.CurrentItem);
        Assert.Equal(3000, running.ElapsedMs);
        Assert.Null(running.CompletedAtUtc);
        Assert.Equal(T0, running.StartedAtUtc);

        for (var i = 1; i <= 25; i++)
            job.Step(host, T0.AddSeconds(10 + i));
        var done = job.BuildStatus(T0.AddHours(1));
        Assert.Equal(FbxLevelExportJobStates.Completed, done.State);
        Assert.False(done.IsRunning);
        Assert.Equal(25, done.ExportedItems);
        Assert.Null(done.CurrentItem);
        Assert.Equal(FbxLevelExportJob.StatusFileLimit, done.Files.Count);
        Assert.Equal(20, FbxLevelExportJob.StatusFileLimit);
        Assert.Equal(6, done.Files[0].Index);
        Assert.Equal("025_n25.fbx", done.Files[^1].Name);
        Assert.Equal(Path.Combine(Folder, "025_n25.fbx"), done.Files[^1].Path);
        Assert.Equal(1000, done.Files[^1].Bytes);
        Assert.Equal(T0.AddSeconds(35), done.CompletedAtUtc);
        Assert.Equal(35000, done.ElapsedMs);
    }
}
