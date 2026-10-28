using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #404 slice 1 (#397 part 3): no public document names the options of the Navisworks animation
// exporter (COM IO plugin "lcodpanim"), so the first slice only reads them. The tool resolves a saved
// viewpoint animation, validates the output folder, and reports every option of the exporter and its
// format plugins. It must not export, write files, or change the current animation; slice 2 adds that.
public sealed class ViewpointAnimationExportTests
{
    private static ViewpointAnimationExportRequest Valid() =>
        new ViewpointAnimationExportRequest { Animation = "Animations/Flight" };

    [Fact]
    public void AValidRequestHasNoProblem()
    {
        Assert.Null(ViewpointAnimationExportRules.Describe(Valid()));
        var request = Valid();
        request.Format = " AVI ";
        request.OutputDirectory = @"D:\Exports\Flight";
        Assert.Null(ViewpointAnimationExportRules.Describe(request));
        request.OutputDirectory = @"\\server\share\Flight";
        Assert.Null(ViewpointAnimationExportRules.Describe(request));
    }

    [Theory]
    [InlineData("", "png", "", "animation is required.")]
    [InlineData("  ", "png", "", "animation is required.")]
    [InlineData("Flight", "gif", "", "format must be png, jpg or avi.")]
    [InlineData("Flight", "png", "exports", "outputDirectory must be an absolute path.")]
    [InlineData("Flight", "png", @"D:exports", "outputDirectory must be an absolute path.")]
    [InlineData("Flight", "png", @"\exports", "outputDirectory must be an absolute path.")]
    public void ProblemsAreReportedWithExactMessages(string animation, string format, string outputDirectory, string expected)
    {
        var request = new ViewpointAnimationExportRequest { Animation = animation, Format = format, OutputDirectory = outputDirectory };
        Assert.Equal(expected, ViewpointAnimationExportRules.Describe(request));
    }

    [Theory]
    [InlineData(null, "png")]
    [InlineData("", "png")]
    [InlineData(" PNG ", "png")]
    [InlineData("Jpg", "jpg")]
    [InlineData("avi", "avi")]
    public void FormatDefaultsToPngAndIsLowerCase(string format, string expected)
    {
        Assert.Equal(expected, ViewpointAnimationExportRules.NormalizeFormat(format));
    }

    [Fact]
    public void TheOutputFolderDefaultsToAFolderBesideTheDocument()
    {
        var document = Path.Combine(Path.GetTempPath(), "nh404", "model.nwd");
        Assert.Equal(
            Path.Combine(Path.GetTempPath(), "nh404", "viewpoint_animations", "Flight_ 1_2"),
            ViewpointAnimationExportRules.ResolveOutputDirectory(document, "", "Flight: 1/2"));
        var explicitFolder = Path.Combine(Path.GetTempPath(), "nh404", "out", "..", "video");
        Assert.Equal(Path.GetFullPath(explicitFolder), ViewpointAnimationExportRules.ResolveOutputDirectory(document, " " + explicitFolder + " ", "Flight"));
        Assert.Equal(Path.GetFullPath(explicitFolder), ViewpointAnimationExportRules.ResolveOutputDirectory(null, explicitFolder, "Flight"));
        var error = Assert.Throws<ArgumentException>(() => ViewpointAnimationExportRules.ResolveOutputDirectory("", "", "Flight"));
        Assert.Equal("Specify outputDirectory when the document has no file path.", error.Message);
    }

    [Theory]
    [InlineData("Flight", "Flight")]
    [InlineData("Flight: 1/2", "Flight_ 1_2")]
    [InlineData("  Pause.  ", "Pause")]
    [InlineData("...", "animation")]
    [InlineData("   ", "animation")]
    [InlineData(null, "animation")]
    public void FolderNamesAreSafe(string name, string expected)
    {
        Assert.Equal(expected, ViewpointAnimationExportRules.SafeFileName(name));
    }

    [Fact]
    public void OptionValuesAreReportedWithTheirTypeInTheInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            Assert.Equal(("Double", "2.5"), Format(2.5));
            Assert.Equal(("Int32", "25"), Format(25));
            Assert.Equal(("Boolean", "true"), Format(true));
            Assert.Equal(("Boolean", "false"), Format(false));
            Assert.Equal(("String", "lcodpexpng"), Format("lcodpexpng"));
            Assert.Equal(("null", ""), Format(null));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ContractsNameTheCommandAndTheProbedPlugins()
    {
        Assert.Equal("viewpoint_animation_export", HostCommandNames.ViewpointAnimationExport);
        Assert.True(HostCommandEffects.IsKnown(HostCommandNames.ViewpointAnimationExport));
        Assert.False(HostCommandEffects.RequiresApproval(HostCommandNames.ViewpointAnimationExport));
        Assert.Equal(new[] { "png", "jpg", "avi" }, ViewpointAnimationExportRules.Formats);
        Assert.Equal(new[] { "lcodpanim", "lcodpavi", "lcodpexpng", "lcodpexjpg", "lcodpcuranim" }, ViewpointAnimationExportRules.ProbedPlugins);
        Assert.Equal(200, ViewpointAnimationExportRules.MaxOptionsPerPlugin);
        Assert.Equal(3, ViewpointAnimationExportRules.MaxOptionDepth);
        var response = new ViewpointAnimationExportResponse();
        Assert.Empty(response.Plugins);
        Assert.Empty(response.Warnings);
        var plugin = new ExporterPluginOptions { Plugin = "lcodpanim", Truncated = false, Error = null };
        plugin.Options.Add(new ExporterOption { Name = "export.anim.source", UserName = "Source", Parent = "", Type = "String", Value = "lcodpcuranim" });
        response.Plugins.Add(plugin);
        Assert.Single(response.Plugins[0].Options);
    }

    [Fact]
    public void TheToolOnlyReadsAndDefaultsToPng()
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == "ViewpointAnimationExport");
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
        Assert.Equal("viewpoint_animation_export", tool.ProtocolTool.Name);
        Assert.Equal(ToolEffects.None, method.GetCustomAttribute<ToolCapabilitiesAttribute>()!.Effects);
        var properties = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!["properties"]!;
        Assert.Equal("", properties["animation"]!["default"]!.GetValue<string>());
        Assert.Equal("png", properties["format"]!["default"]!.GetValue<string>());
        Assert.Equal("", properties["outputDirectory"]!["default"]!.GetValue<string>());
        Assert.Null(properties["apply"]);
    }

    [Fact]
    public void ThePluginReadsExporterOptionsAndNeverExports()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "ViewpointAnimationExportService.cs"));
        foreach (var expected in new[]
        {
            "ViewpointAnimationExportRules.Describe(", "ViewpointAnimationExportRules.NormalizeFormat(",
            "ViewpointAnimationExportRules.ResolveOutputDirectory(", "ViewpointAnimationExportRules.FormatOptionValue(",
            "ViewpointAnimationExportRules.ProbedPlugins", "ViewpointAnimationExportRules.MaxOptionsPerPlugin",
            "ViewpointAnimationExportRules.MaxOptionDepth", "GetIOPluginOptions(", "is SavedViewpointAnimation",
            "Saved viewpoint animation was not found.", "Marshal.ReleaseComObject(",
        })
            Assert.Contains(expected, service, StringComparison.Ordinal);
        // Slice 1 is read-only: no export, no option writes, no folder creation, no current-animation change.
        foreach (var forbidden in new[]
        {
            "DriveIOPlugin", "CurrentAnimation =", "set_PluginOption", ".PluginOption", ".value = ", "Directory.CreateDirectory",
            "File.Write", "File.Create", "Transaction",
        })
            Assert.DoesNotContain(forbidden, service, StringComparison.Ordinal);
    }

    private static (string Type, string Value) Format(object value)
    {
        string type;
        var text = ViewpointAnimationExportRules.FormatOptionValue(value, out type);
        return (type, text);
    }
}
