using NavisHelper.Agent.Contracts;
using NavisHelper.AutomationRunner;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #502, found live on 2026-11-17 (main 8032572): the per-item Datasmith step started the runner with
// the job prefix "NavisHelper-ItemDatasmith-", which the runner refuses ("--job must be a server
// Datasmith job name"), and publishing the per-item NWD copy raised Models.CollectionChanged, so the
// panel emptied its own export list mid-run. Unit tests faked the runner, so neither showed in L0.
public sealed class ItemDatasmithWiringTests
{
    [Fact]
    public void TheSharedJobPrefix_IsAcceptedByTheRunner()
    {
        var jobName = DatasmithRunnerCommandLine.JobNamePrefix + Guid.NewGuid().ToString("N");
        var args = DatasmithRunnerCommandLine.BuildArguments(@"C:\Program Files\Autodesk\Navisworks Manage 2026",
            @"D:\tmp\001_A.nwd", @"D:\out\001_A.udatasmith", null, null, false, true, jobName);

        var parsed = DatasmithExportArguments.Parse(args.ToArray(), out var error);

        Assert.True(parsed != null, error);
        Assert.Equal(jobName, parsed!.JobName);
    }

    [Fact]
    public void BothDatasmithCallers_UseTheSharedPrefix()
    {
        foreach (var file in new[] { "DatasmithPanelExportJob.cs", "NavisworksFbxExportHost.cs" })
        {
            var source = Read("NavisHelper", "Agent", "Services", file);
            Assert.Contains("new KillOnCloseJob(DatasmithRunnerCommandLine.JobNamePrefix)", source, StringComparison.Ordinal);
            Assert.DoesNotContain("ItemDatasmith", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ThePanelKeepsItsListWhileItsOwnExportRuns()
    {
        var section = Read("NavisHelper", "WPF", "FbxLevelExportSection.cs");
        int start = section.IndexOf("private void OnModelsChanged(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int bump = section.IndexOf("_modelsGeneration++", start, StringComparison.Ordinal);
        int guard = section.IndexOf("if (_running)", start, StringComparison.Ordinal);
        Assert.True(guard > start && guard < bump, "OnModelsChanged must return while the section's own export runs, before it bumps the generation");
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
