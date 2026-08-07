using System.Reflection;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class SectionBoxArchitectureTests
{
    [Fact]
    public void McpTools_ExposeCaptureAndReplayAsSeparateTypedOperations()
    {
        var capture = typeof(NavisworksSectionBoxTools).GetMethod(nameof(NavisworksSectionBoxTools.GetCurrentSectionBox));
        var replay = typeof(NavisworksSectionBoxTools).GetMethod(nameof(NavisworksSectionBoxTools.IsolateByBox));

        Assert.NotNull(capture?.GetCustomAttribute<McpServerToolAttribute>());
        Assert.NotNull(replay?.GetCustomAttribute<McpServerToolAttribute>());
        Assert.Equal(typeof(SectionBoxGeometry), replay!.GetParameters()[0].ParameterType);
        Assert.Equal(false, replay.GetParameters().Single(parameter => parameter.Name == "apply").DefaultValue);
        Assert.Equal("get_current_section_box", HostCommandNames.GetCurrentSectionBox);
        Assert.Equal("isolate_by_box", HostCommandNames.IsolateByBox);
    }

    [Fact]
    public void HostRouter_RegistersBothCommands_AndProgramRegistersSeparateToolContainer()
    {
        var root = FindRepositoryRoot();
        var router = File.ReadAllText(Path.Combine(root, "NavisHelper", "Agent", "Host", "AgentHostService.CommandRouter.cs"));
        var program = File.ReadAllText(Path.Combine(root, "NavisHelper.McpServer", "Program.cs"));

        Assert.Contains("HostCommandNames.GetCurrentSectionBox", router, StringComparison.Ordinal);
        Assert.Contains("HostCommandNames.IsolateByBox", router, StringComparison.Ordinal);
        Assert.Contains("WithTools<NavisworksSectionBoxTools>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void CaptureAndPreviewSources_DoNotWriteSectionBoxSelectionOrVisibility()
    {
        var root = FindRepositoryRoot();
        var capture = File.ReadAllText(Path.Combine(root, "NavisHelper", "Agent", "Services", "SectionBoxCaptureService.cs"));
        var replay = File.ReadAllText(Path.Combine(root, "NavisHelper", "Agent", "Services", "BoxIsolationService.cs"));

        Assert.DoesNotContain("SetClippingPlanes", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("SetHidden", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentSelection.CopyFrom", capture, StringComparison.Ordinal);
        Assert.True(replay.IndexOf("if (!applyRequested)", StringComparison.Ordinal) < replay.IndexOf("SetHidden(document", StringComparison.Ordinal));
        Assert.DoesNotContain("TrySetClippingPlanes", replay, StringComparison.Ordinal);
        Assert.DoesNotContain("SetClippingPlanes", replay, StringComparison.Ordinal);
        Assert.DoesNotContain("GetClippingPlanes", replay, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
            directory = directory.Parent;
        return Assert.IsType<DirectoryInfo>(directory).FullName;
    }
}
