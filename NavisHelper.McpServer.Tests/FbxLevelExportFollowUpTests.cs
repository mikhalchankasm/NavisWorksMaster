using Xunit;

namespace NavisHelper.McpServer.Tests;

// #444 follow-ups found by the L3 run on Navisworks 2027 (2026-11-15). The plugin cannot compile in
// L0, so the service and the panel are pinned by source, as FbxLevelExportHostWiringTests does.
public sealed class FbxLevelExportFollowUpTests
{
    // L3: a start refused for its depth, an empty selection or existing files left an empty folder.
    [Fact]
    public void TheFolderIsCreatedOnlyAfterThePlanAndTheJobWereAccepted()
    {
        var service = Read("NavisHelper", "Agent", "Services", "FbxLevelExportJobService.cs");
        var start = Body(service, "public FbxLevelExportStatusResponse Start(Document document, IReadOnlyList<ModelItem> items,");
        int plan = start.IndexOf("FbxLevelExportPlan.BuildFromItems(", StringComparison.Ordinal);
        int job = start.IndexOf("FbxLevelExportJob.Start(", StringComparison.Ordinal);
        int create = start.IndexOf("Directory.CreateDirectory(folder)", StringComparison.Ordinal);
        int publish = start.IndexOf("_job = job;", StringComparison.Ordinal);
        Assert.True(plan >= 0 && job > plan, "the plan is built before the job starts");
        Assert.True(create > job, "the folder is created after the job was accepted");
        Assert.True(publish > create, "a folder that cannot be created leaves no job behind");
        Assert.Equal(1, Count(start, "Directory.CreateDirectory("));
    }

    // L3: after a finished export, a successful Add kept "Exported 16, skipped 8" and the full
    // progress bar; after the list was reset the bar stayed full.
    [Fact]
    public void EditingTheListClearsTheLastJobsStatusAndProgress()
    {
        var section = Read("NavisHelper", "WPF", "FbxLevelExportSection.cs");
        var clear = Body(section, "private void ClearStaleStatus()");
        Assert.Contains("if (_running) return;", clear, StringComparison.Ordinal);
        Assert.Contains("_statusKey = null;", clear, StringComparison.Ordinal);
        Assert.Contains("_status.Text = string.Empty;", clear, StringComparison.Ordinal);
        Assert.Contains("_progress.Value = 0;", clear, StringComparison.Ordinal);
        Assert.Contains("_running = running;", Body(section, "private void SetRunning(bool running)"), StringComparison.Ordinal);

        foreach (var handler in new[]
                 {
                     "private void OnAddSelected(", "private void OnRemove(", "private void OnClear(",
                     "private void OnModelsChanged(",
                 })
            Assert.Contains("ClearStaleStatus();", Body(section, handler), StringComparison.Ordinal);

        // The reset warning is shown after the clear, so it is what the user reads.
        var changed = Body(section, "private void OnModelsChanged(");
        Assert.True(changed.IndexOf("ClearStaleStatus();", StringComparison.Ordinal)
                    < changed.IndexOf("Panel_Model_FbxListReset_Status", StringComparison.Ordinal));
    }

    // The level-only planner of slice alpha lost its last caller in epsilon2.
    [Fact]
    public void TheLevelOnlyPlannerIsGone()
    {
        var plan = Read("NavisHelper.Contracts", "FbxLevelExportPlan.cs");
        Assert.DoesNotContain("public static FbxLevelExportPlan Build(", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("CollectLevel(", plan, StringComparison.Ordinal);
        Assert.Contains("public static FbxLevelExportPlan BuildFromItems(", plan, StringComparison.Ordinal);
    }

    // The method text from its signature to the next member at the same indentation.
    private static string Body(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " is present");
        int end = source.IndexOf("\n        }", start, StringComparison.Ordinal);
        Assert.True(end > start, signature + " has a body");
        return source.Substring(start, end - start);
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }
}
