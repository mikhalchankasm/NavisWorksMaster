using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #444 slice beta2: the plugin side of the FBX-by-level job. Three host commands; status and
// cancel bypass the request gate (one export can hold the UI thread for 90 s) and are exempt
// from protected mode like the other job polls. The plugin cannot compile in L0, so its wiring
// is pinned by source, as SaveDocumentJobArchitectureTests does for the save job; L2 builds it
// and L3 on Navisworks 2027 runs it.
public sealed class FbxLevelExportHostWiringTests
{
    [Fact]
    public void TheThreeCommandsHaveStableNames()
    {
        Assert.Equal("start_fbx_level_export", HostCommandNames.StartFbxLevelExport);
        Assert.Equal("fbx_level_export_status", HostCommandNames.FbxLevelExportStatus);
        Assert.Equal("cancel_fbx_level_export", HostCommandNames.CancelFbxLevelExport);
    }

    [Fact]
    public void StatusAndCancelBypassTheGateAndOnlyStatusIsAPoll()
    {
        Assert.Equal(HostRequestGateBypassKind.None, HostRequestPolicy.GetRequestGateBypassKind(HostCommandNames.StartFbxLevelExport));
        Assert.Equal(HostRequestGateBypassKind.FbxLevelExportStatus, HostRequestPolicy.GetRequestGateBypassKind(HostCommandNames.FbxLevelExportStatus));
        Assert.Equal(HostRequestGateBypassKind.CancelFbxLevelExport, HostRequestPolicy.GetRequestGateBypassKind(HostCommandNames.CancelFbxLevelExport));

        Assert.True(HostRequestPolicy.IsOperationStatusPollCommand(HostCommandNames.FbxLevelExportStatus));
        Assert.False(HostRequestPolicy.IsOperationStatusPollCommand(HostCommandNames.CancelFbxLevelExport));
        Assert.True(OperationHistoryPolicy.CountsAsLastOperation(HostCommandNames.CancelFbxLevelExport));
        Assert.True(OperationHistoryPolicy.CountsAsLastOperation(HostCommandNames.StartFbxLevelExport));
    }

    [Fact]
    public void OnlyStartNeedsApproval()
    {
        Assert.True(HostCommandEffects.RequiresApproval(HostCommandNames.StartFbxLevelExport));
        Assert.False(HostCommandEffects.RequiresApproval(HostCommandNames.FbxLevelExportStatus));
        Assert.False(HostCommandEffects.RequiresApproval(HostCommandNames.CancelFbxLevelExport));
    }

    [Fact]
    public void TheHostRoutesStartAndAnswersStatusAndCancelOffTheGate()
    {
        var router = Read("NavisHelper", "Agent", "Host", "AgentHostService.CommandRouter.cs");
        Assert.Contains("router.Register<StartFbxLevelExportRequest>(", router, StringComparison.Ordinal);
        Assert.Contains("HostCommandNames.StartFbxLevelExport", router, StringComparison.Ordinal);
        Assert.Contains("_fbxLevelExportJobService.Start(", router, StringComparison.Ordinal);

        var dispatch = Read("NavisHelper", "Agent", "Host", "AgentHostService.Dispatch.cs");
        Assert.Contains("case HostRequestGateBypassKind.FbxLevelExportStatus", dispatch, StringComparison.Ordinal);
        Assert.Contains("_fbxLevelExportJobService.Status(", dispatch, StringComparison.Ordinal);
        Assert.Contains("case HostRequestGateBypassKind.CancelFbxLevelExport", dispatch, StringComparison.Ordinal);
        Assert.Contains("_fbxLevelExportJobService.Cancel(", dispatch, StringComparison.Ordinal);

        var host = Read("NavisHelper", "Agent", "Host", "AgentHostService.cs");
        Assert.Contains("new FbxLevelExportJobService(PostToUi)", host, StringComparison.Ordinal);
    }

    [Fact]
    public void TheServiceRunsThePureJobOneStepPerUiPost()
    {
        var service = Read("NavisHelper", "Agent", "Services", "FbxLevelExportJobService.cs");
        foreach (var expected in new[]
                 {
                     "FbxLevelExportPlan.Build(", "FbxLevelExportJob.Start(", ".Step(", "() => DateTime.UtcNow",
                     "_postToUi(", "BuildDocumentKey(", "Directory.CreateDirectory(", "Models.RootItems",
                 })
            Assert.Contains(expected, service, StringComparison.Ordinal);
        // The job owns the rules; the service must not grow its own loop over the plan.
        Assert.DoesNotContain("HideSet(", service, StringComparison.Ordinal);
        Assert.DoesNotContain("UnhideSet(", service, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHostAdaptsNavisworksToThePureJob()
    {
        var adapter = Read("NavisHelper", "Agent", "Services", "NavisworksFbxExportHost.cs");
        foreach (var expected in new[]
                 {
                     ": IFbxExportHost", ": IFbxTreeNode", ".IsHidden", "Models.SetHidden(",
                     "GetIOPluginOptions(\"LcFbxExporterPlugin\")", "DriveIOPlugin(\"LcFbxExporterPlugin\"",
                     "nwEExportStatus.eExport_OK", "GetDiskFreeSpaceEx",
                 })
            Assert.Contains(expected, adapter, StringComparison.Ordinal);

        var project = Read("NavisHelper", "NavisHelper.csproj");
        Assert.Contains(@"Agent\Services\FbxLevelExportJobService.cs", project, StringComparison.Ordinal);
        Assert.Contains(@"Agent\Services\NavisworksFbxExportHost.cs", project, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(parts).ToArray()));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory.FullName;
    }
}
