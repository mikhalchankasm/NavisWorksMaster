using System.Reflection;
using ModelContextProtocol.Server;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SaveDocumentJobArchitectureTests
{
    [Fact]
    public void McpTools_LiveInTheirOwnContainerWithJobCapabilities()
    {
        var start = typeof(NavisworksSaveJobTools).GetMethod(nameof(NavisworksSaveJobTools.StartSaveDocument));
        var status = typeof(NavisworksSaveJobTools).GetMethod(nameof(NavisworksSaveJobTools.SaveDocumentStatus));

        Assert.NotNull(start!.GetCustomAttribute<McpServerToolAttribute>());
        Assert.NotNull(status!.GetCustomAttribute<McpServerToolAttribute>());
        Assert.Equal(
            ToolEffects.Document | ToolEffects.Files,
            start.GetCustomAttribute<ToolCapabilitiesAttribute>().Effects);
        Assert.Equal(ToolEffects.None, status.GetCustomAttribute<ToolCapabilitiesAttribute>().Effects);
        Assert.True(status.GetCustomAttribute<ToolCapabilitiesAttribute>().RequiresHost);
        Assert.False(status.GetCustomAttribute<ToolCapabilitiesAttribute>().RequiresDocument);
    }

    [Fact]
    public void Registrations_ExposeTheJobPairThroughDedicatedBoundaries()
    {
        var root = RepositoryPaths.Root;
        var program = Read(root, "NavisHelper.McpServer", "Program.cs");
        var profile = Read(root, "NavisHelper.McpServer", "Services", "McpToolProfile.cs");
        var router = Read(root, "NavisHelper", "Agent", "Host", "AgentHostService.CommandRouter.cs");
        var project = Read(root, "NavisHelper", "NavisHelper.csproj");

        Assert.Contains("WithTools<NavisworksSaveJobTools>()", program, StringComparison.Ordinal);
        Assert.Contains("\"start_save_document\"", profile, StringComparison.Ordinal);
        Assert.Contains("\"save_document_status\"", profile, StringComparison.Ordinal);
        Assert.Contains("HostCommandNames.StartSaveDocument", router, StringComparison.Ordinal);
        Assert.Contains("_saveDocumentJobService.Start", router, StringComparison.Ordinal);
        Assert.Contains("Agent\\Services\\SaveDocumentJobService.cs", project, StringComparison.Ordinal);
    }

    [Fact]
    public void HostService_PostsTheSaveAndAnswersStatusWithoutTheUiThread()
    {
        var root = RepositoryPaths.Root;
        var service = Read(root, "NavisHelper", "Agent", "Services", "SaveDocumentJobService.cs");
        var dispatch = Read(root, "NavisHelper", "Agent", "Host", "AgentHostService.Dispatch.cs");
        var policy = Read(root, "NavisHelper.Contracts", "HostRequestPolicy.cs");

        Assert.Contains("_postToUi(() => RunSave(job, documentKey))", service, StringComparison.Ordinal);
        Assert.Contains("case HostRequestGateBypassKind.SaveDocumentStatus", dispatch, StringComparison.Ordinal);
        Assert.Contains("_saveDocumentJobService.Status", dispatch, StringComparison.Ordinal);
        Assert.Contains("SaveDocumentStatus", policy, StringComparison.Ordinal);
        Assert.Contains("IsOperationStatusPollCommand", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("InvokeOnUiThread", service, StringComparison.Ordinal);
    }

    [Fact]
    public void HostService_ReusesTheReviewedSaveLogicAndItsOwnType()
    {
        var root = RepositoryPaths.Root;
        var service = Read(root, "NavisHelper", "Agent", "Services", "SaveDocumentJobService.cs");
        var savePartial = Read(root, "NavisHelper", "Agent", "Services", "DocumentCommandService.DocumentSave.cs");

        Assert.Contains("internal sealed class SaveDocumentJobService", service, StringComparison.Ordinal);
        Assert.Contains("DocumentCommandService.NormalizeExistingDocumentPath(document.FileName)", service, StringComparison.Ordinal);
        Assert.Contains("DocumentCommandService.SaveDocumentToPath(document, job.Path, false)", service, StringComparison.Ordinal);
        Assert.Contains("internal static SaveDocumentResponse SaveDocumentToPath", savePartial, StringComparison.Ordinal);
        Assert.DoesNotContain("TrySaveFile", service, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class DocumentCommandService", service, StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] parts)
    {
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
