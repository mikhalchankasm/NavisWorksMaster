using System.Reflection;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ViewpointCameraArchitectureTests
{
    [Fact]
    public void McpTool_IsSeparateTypedContainerAndDefaultsToDryRun()
    {
        var method = typeof(NavisworksViewpointCameraTools).GetMethod(nameof(NavisworksViewpointCameraTools.ViewpointSetCamera));

        Assert.NotNull(method?.GetCustomAttribute<McpServerToolAttribute>());
        Assert.Equal(typeof(Point3Info), method!.GetParameters().Single(parameter => parameter.Name == "position").ParameterType);
        Assert.Equal(typeof(ViewpointCameraZoomTo), method.GetParameters().Single(parameter => parameter.Name == "zoomTo").ParameterType);
        Assert.Equal(false, method.GetParameters().Single(parameter => parameter.Name == "apply").DefaultValue);
    }

    [Fact]
    public void Registrations_ExposeOneNewCommandThroughDedicatedBoundaries()
    {
        var root = FindRepositoryRoot();
        var program = Read(root, "NavisHelper.McpServer", "Program.cs");
        var router = Read(root, "NavisHelper", "Agent", "Host", "AgentHostService.CommandRouter.cs");
        var project = Read(root, "NavisHelper", "NavisHelper.csproj");

        Assert.Contains("WithTools<NavisworksViewpointCameraTools>()", program, StringComparison.Ordinal);
        Assert.Contains("HostCommandNames.ViewpointSetCamera", router, StringComparison.Ordinal);
        Assert.Contains("_viewpointCameraService.SetCamera", router, StringComparison.Ordinal);
        Assert.Contains("Agent\\Services\\ViewpointCameraCommandService.cs", project, StringComparison.Ordinal);
    }

    [Fact]
    public void HostService_DoesNotTraverseOrMutateUnrelatedDocumentState()
    {
        var root = FindRepositoryRoot();
        var service = Read(root, "NavisHelper", "Agent", "Services", "ViewpointCameraCommandService.cs");

        Assert.DoesNotContain("Descendants", service, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentSelection", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SetHidden", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SetClippingPlanes", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SectionBox", service, StringComparison.Ordinal);
        Assert.Contains("document.CurrentViewpoint.CreateCopy()", service, StringComparison.Ordinal);
        Assert.Contains("document.CurrentViewpoint.CopyFrom(original)", service, StringComparison.Ordinal);
    }

    [Fact]
    public void HostService_UsesOnlyReviewedCameraAndViewpointSaveApis()
    {
        var root = FindRepositoryRoot();
        var service = Read(root, "NavisHelper", "Agent", "Services", "ViewpointCameraCommandService.cs");

        Assert.Contains("viewpoint.Position = position", service, StringComparison.Ordinal);
        Assert.Contains("viewpoint.PointAt(target)", service, StringComparison.Ordinal);
        Assert.Contains("viewpoint.AlignUp(up)", service, StringComparison.Ordinal);
        Assert.Contains("viewpoint.ZoomBox", service, StringComparison.Ordinal);
        Assert.Contains("_viewpointCommands.CreateViewpoint", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SavedViewpoints.AddCopy", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SavedViewpoints.InsertCopy", service, StringComparison.Ordinal);
    }

    [Fact]
    public void CameraFamily_DoesNotModifyLegacyViewpointGodPartial()
    {
        var root = FindRepositoryRoot();
        var service = Read(root, "NavisHelper", "Agent", "Services", "ViewpointCameraCommandService.cs");
        var tool = Read(root, "NavisHelper.McpServer", "Tools", "NavisworksViewpointCameraTools.cs");

        Assert.Contains("internal sealed class ViewpointCameraCommandService", service, StringComparison.Ordinal);
        Assert.Contains("internal sealed class NavisworksViewpointCameraTools", tool, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class DocumentCommandService", service, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class NavisworksTools", tool, StringComparison.Ordinal);
    }

    private static string Read(string root, params string[] parts)
    {
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
