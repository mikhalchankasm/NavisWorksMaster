using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #498 (review P2 of #497): Step (UI thread) published an NWD copy per item faster than the Datasmith runner
// converted them, so a long list could fill the temporary drive. The job now keeps at most
// FbxLevelExportJob.MaxQueuedCopies copies on disk (queued plus the one being converted). A Step that would
// exceed it publishes nothing and returns false, so the UI is not rescheduled (no busy loop); after a
// conversion TakeWaitingStep() says once that the UI step may resume, and the service reschedules it.
public sealed class ItemExportQueueBoundTests
{
    private static readonly DateTime T0 = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
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
        public int MaxCopiesSeen;
        public string TempFolder => Temp;
        public void SetHidden(IReadOnlyList<IFbxTreeNode> nodes, bool hidden)
        {
            foreach (Node node in nodes) node.IsHidden = hidden;
        }
        public string Export(string path) { Existing.Add(path); return null; }
        public string PublishCopy(string path)
        {
            Calls.Add("publish " + Path.GetFileName(path));
            Existing.Add(path);
            MaxCopiesSeen = Math.Max(MaxCopiesSeen, Copies);
            return null;
        }
        public string ExportDatasmith(string sourcePath, string outputPath)
        {
            Calls.Add("datasmith " + Path.GetFileName(outputPath));
            Existing.Add(outputPath);
            return null;
        }
        public void DeleteFile(string path) { Existing.Remove(path); }
        public bool FileExists(string path) => Existing.Contains(path);
        public long FileSize(string path) => Existing.Contains(path) ? 1000 : 0;
        public long FreeBytes(string folder) => 100L * 1024 * 1024 * 1024;
        public int Copies => Existing.Count(path => path.StartsWith(Temp, StringComparison.OrdinalIgnoreCase));
    }

    private static FbxLevelExportPlan Plan()
    {
        var items = new[] { new Node("A"), new Node("B"), new Node("C"), new Node("D"), new Node("E") };
        var root = new Node("Root", items);
        var plan = FbxLevelExportPlan.BuildFromItems(new IFbxTreeNode[] { root }, items, 0, out var error);
        Assert.True(error == null, error);
        return plan;
    }

    private static FbxLevelExportJob Start(Host host, ItemExportFormats formats) =>
        FbxLevelExportJob.Start("items-498", Plan(), Folder, false, host, null, T0, formats);

    [Fact]
    public void WithConversionsHeldAtMostTwoCopiesArePublished()
    {
        var host = new Host();
        var job = Start(host, ItemExportFormats.Datasmith);
        var steps = 0;
        while (job.Step(host, () => T0.AddSeconds(++steps)) && steps < 100) { }

        Assert.Equal(2, FbxLevelExportJob.MaxQueuedCopies);
        Assert.Equal(2, host.Copies);
        Assert.Equal(2, host.Calls.Count(call => call.StartsWith("publish ", StringComparison.Ordinal)));
        Assert.Equal(FbxLevelExportJobStates.Running, job.State);
        // A waiting Step publishes nothing and asks for no reschedule; no resume while the queue is full.
        Assert.False(job.Step(host, () => T0.AddSeconds(++steps)));
        Assert.Equal(2, host.Copies);
        Assert.False(job.TakeWaitingStep());
    }

    [Fact]
    public void AConversionLetsTheWaitingStepResumeOnce()
    {
        var host = new Host();
        var job = Start(host, ItemExportFormats.Datasmith);
        var steps = 0;
        while (job.Step(host, () => T0.AddSeconds(++steps)) && steps < 100) { }

        Assert.True(job.ConvertNext(host, () => T0.AddSeconds(++steps)));
        Assert.Equal(1, host.Copies);
        Assert.True(job.TakeWaitingStep());
        Assert.False(job.TakeWaitingStep());
        Assert.False(job.Step(host, () => T0.AddSeconds(++steps)));
        Assert.Equal(2, host.Copies);
        Assert.Equal(3, host.Calls.Count(call => call.StartsWith("publish ", StringComparison.Ordinal)));
    }

    [Fact]
    public void AllFiveFilesAreWrittenOnceConversionsProceed()
    {
        var host = new Host();
        var job = Start(host, ItemExportFormats.Fbx | ItemExportFormats.Datasmith);
        var steps = 0;
        var resume = true;
        while (steps < 500 && job.State == FbxLevelExportJobStates.Running)
        {
            if (resume)
                while (job.Step(host, () => T0.AddSeconds(++steps)) && steps < 500) { }
            job.ConvertNext(host, () => T0.AddSeconds(++steps));
            resume = job.TakeWaitingStep();
            if (!resume && host.Copies == 0 && job.State == FbxLevelExportJobStates.Running)
                job.Step(host, () => T0.AddSeconds(++steps));
        }

        Assert.Equal(FbxLevelExportJobStates.Completed, job.State);
        Assert.True(host.MaxCopiesSeen <= 2, "copies on disk: " + host.MaxCopiesSeen);
        Assert.Equal(0, host.Copies);
        Assert.Equal(5, host.Calls.Count(call => call.StartsWith("datasmith ", StringComparison.Ordinal)));
        Assert.Equal(5, host.Calls.Distinct().Count(call => call.StartsWith("datasmith ", StringComparison.Ordinal)));
        foreach (var name in new[] { "001_A", "002_B", "003_C", "004_D", "005_E" })
        {
            Assert.Contains(Path.Combine(Folder, name + ".udatasmith"), host.Existing);
            Assert.Contains(Path.Combine(Folder, name + ".fbx"), host.Existing);
        }
    }

    [Fact]
    public void FbxOnlyJobsAreNotBounded()
    {
        var host = new Host();
        var job = Start(host, ItemExportFormats.Fbx);
        var steps = 0;
        while (job.Step(host, () => T0.AddSeconds(++steps)) && steps < 100) { }
        Assert.Equal(FbxLevelExportJobStates.Completed, job.State);
        Assert.False(job.TakeWaitingStep());
    }

    [Fact]
    public void CancelWhileWaitingRemovesTheCopiesAndCancels()
    {
        var host = new Host();
        var job = Start(host, ItemExportFormats.Datasmith);
        var steps = 0;
        while (job.Step(host, () => T0.AddSeconds(++steps)) && steps < 100) { }

        Assert.True(job.Cancel(T0.AddSeconds(++steps)));
        Assert.False(job.ConvertNext(host, () => T0.AddSeconds(++steps)));
        Assert.Equal(FbxLevelExportJobStates.Canceled, job.State);
        Assert.Equal(0, host.Copies);
        Assert.False(job.TakeWaitingStep());
    }

    [Fact]
    public void TheServiceReschedulesTheWaitingStepAfterAConversion()
    {
        var service = Read("NavisHelper", "Agent", "Services", "FbxLevelExportJobService.cs");
        Assert.Contains("job.TakeWaitingStep()", service, StringComparison.Ordinal);
        Assert.Contains("ResumeAfterConversion(job, host, documentKey)", service, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
