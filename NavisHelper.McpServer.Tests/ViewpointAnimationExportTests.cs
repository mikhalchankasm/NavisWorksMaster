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
// format plugins. Slice 2 adds apply: a synchronous export through DriveIOPlugin("lcodpanim") that sets the
// current animation for the call, restores it, and reports the produced files with their detected kind.
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
        Assert.True(HostCommandEffects.RequiresApproval(HostCommandNames.ViewpointAnimationExport));
        Assert.Equal(new[] { "png", "jpg", "avi" }, ViewpointAnimationExportRules.Formats);
        Assert.Equal(new[] { "lcodpanim", "lcodpavi", "lcodpexpng", "lcodpexjpg", "lcodpcuranim" }, ViewpointAnimationExportRules.ProbedPlugins);
        Assert.Equal(200, ViewpointAnimationExportRules.MaxOptionsPerPlugin);
        Assert.Equal(3, ViewpointAnimationExportRules.MaxOptionDepth);
        var response = new ViewpointAnimationExportResponse();
        Assert.Empty(response.Plugins);
        Assert.Empty(response.Warnings);
        Assert.Empty(response.Files);
        Assert.Empty(response.AppliedOptions);
        var plugin = new ExporterPluginOptions { Plugin = "lcodpanim", Truncated = false, Error = null };
        plugin.Options.Add(new ExporterOption { Name = "export.anim.source", UserName = "Source", Parent = "", Type = "String", Value = "lcodpcuranim" });
        response.Plugins.Add(plugin);
        response.Files.Add(new ExportedFile { Name = "Flight0000.png", Bytes = 10, Kind = "png" });
        Assert.Single(response.Plugins[0].Options);
        Assert.Single(response.Files);
    }

    // Slice 2: the option names below are what GetIOPluginOptions("lcodpanim") reported on 2027 (L3 on #404).
    [Theory]
    [InlineData(-1, 0, 0, 0, "width and height must be 0 or between 16 and 4096.")]
    [InlineData(15, 0, 0, 0, "width and height must be 0 or between 16 and 4096.")]
    [InlineData(0, 4097, 0, 0, "width and height must be 0 or between 16 and 4096.")]
    [InlineData(0, 0, 61, 0, "fps must be 0 or between 1 and 60.")]
    [InlineData(0, 0, -2, 0, "fps must be 0 or between 1 and 60.")]
    [InlineData(0, 0, 0, 3, "antiAliasLevel must be 0, 1, 2, 4, 8, 16, 32 or 64.")]
    public void ExportSettingsAreRangeChecked(int width, int height, int fps, int antiAlias, string expected)
    {
        var request = Valid();
        request.Width = width;
        request.Height = height;
        request.Fps = fps;
        request.AntiAliasLevel = antiAlias;
        Assert.Equal(expected, ViewpointAnimationExportRules.Describe(request));
    }

    [Fact]
    public void OnlyNonZeroSettingsBecomeExporterOptionsInAFixedOrder()
    {
        var request = Valid();
        Assert.Empty(ViewpointAnimationExportRules.OptionAssignments(request));
        request.Width = 1280;
        request.Height = 720;
        request.Fps = 25;
        request.AntiAliasLevel = 4;
        Assert.Null(ViewpointAnimationExportRules.Describe(request));
        Assert.Equal(
            new[] { "export.image.width=1280", "export.image.height=720", "export.anim.fps=25", "export.image.anti-alias.level=4" },
            ViewpointAnimationExportRules.OptionAssignments(request).Select(pair => pair.Key + "=" + pair.Value));
        request = Valid();
        request.Fps = 12;
        Assert.Equal(new[] { "export.anim.fps=12" }, ViewpointAnimationExportRules.OptionAssignments(request).Select(pair => pair.Key + "=" + pair.Value));
    }

    [Theory]
    [InlineData("png", "Flight: 1/2", "Flight_ 1_2.png")]
    [InlineData("JPG", "Flight", "Flight.jpg")]
    [InlineData("avi", "...", "animation.avi")]
    public void TheOutputFileIsTheSafeAnimationNameWithTheFormatExtension(string format, string name, string expected)
    {
        var folder = Path.Combine(Path.GetTempPath(), "nh404");
        Assert.Equal(Path.Combine(folder, expected), ViewpointAnimationExportRules.OutputFile(folder, name, format));
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "png")]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10 }, "jpg")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x41, 0x56, 0x49, 0x20 }, "avi")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x41, 0x56, 0x45 }, "unknown")]
    [InlineData(new byte[] { 0x42, 0x4D, 0, 0 }, "bmp")]
    [InlineData(new byte[] { 0x89, 0x50 }, "unknown")]
    [InlineData(new byte[0], "unknown")]
    public void ProducedFilesAreRecognisedByTheirSignature(byte[] head, string expected)
    {
        Assert.Equal(expected, ViewpointAnimationExportRules.DetectKind(head));
        Assert.Equal("unknown", ViewpointAnimationExportRules.DetectKind(null));
    }

    [Fact]
    public void ProtectedFoldersAreRefusedAndTheirSiblingsAreNot()
    {
        var root = Path.Combine(Path.GetTempPath(), "nh404-protected");
        var roots = new[] { root + Path.DirectorySeparatorChar, "", null };
        Assert.True(ViewpointAnimationExportRules.IsForbiddenOutputRoot(root, roots));
        Assert.True(ViewpointAnimationExportRules.IsForbiddenOutputRoot(Path.Combine(root, "a", "b"), roots));
        Assert.True(ViewpointAnimationExportRules.IsForbiddenOutputRoot(Path.Combine(root.ToUpperInvariant(), "x"), roots));
        Assert.False(ViewpointAnimationExportRules.IsForbiddenOutputRoot(root + "-sibling", roots));
        Assert.False(ViewpointAnimationExportRules.IsForbiddenOutputRoot(Path.Combine(Path.GetTempPath(), "elsewhere"), roots));
        Assert.Equal("outputDirectory is inside a protected folder (Windows, Program Files or the NavisHelper install).",
            ViewpointAnimationExportRules.ProtectedFolderMessage);
    }

    [Fact]
    public void EarlierOutputOfTheSameAnimationNeedsOverwrite()
    {
        var files = new[] { "Other0001.png", "Flight.avi", "notes.txt" };
        Assert.False(ViewpointAnimationExportRules.HasExistingOutput(files, "Flight", "png"));
        Assert.True(ViewpointAnimationExportRules.HasExistingOutput(files, "Flight", "avi"));
        Assert.True(ViewpointAnimationExportRules.HasExistingOutput(new[] { "flight0007.PNG" }, "Flight", "png"));
        Assert.False(ViewpointAnimationExportRules.HasExistingOutput(null, "Flight", "png"));
        Assert.Equal("Output files for this animation already exist in outputDirectory; pass overwrite=true to replace them.",
            ViewpointAnimationExportRules.ExistingOutputMessage);
    }

    // Correction pass (#404 L3 on 7964f9d): lcodpanim returned eExport_FAIL in 0.1 s and exposes no format option, so
    // the tool lets L3 set or add exporter options and pick the plugin to drive, and returns the data on failure.
    [Fact]
    public void ExporterOptionsParseIntoTypedValues()
    {
        var parsed = ViewpointAnimationExportRules.ParseExporterOptions(new List<string>
        {
            " export.image.format = lcodpexpng ", "export.anim.fps=12", "flag=true", "off=False", "neg=-3", "empty=", "ratio=1.5",
        });
        Assert.Equal(
            new object[] { "lcodpexpng", 12, true, false, -3, "", "1.5" },
            parsed.Select(pair => pair.Value).ToArray());
        Assert.Equal(new[] { "export.image.format", "export.anim.fps", "flag", "off", "neg", "empty", "ratio" }, parsed.Select(pair => pair.Key));
        Assert.Empty(ViewpointAnimationExportRules.ParseExporterOptions(null));
    }

    [Theory]
    [InlineData("lcodpcuranim", null, "exporter must be lcodpanim, lcodpavi, lcodpexpng or lcodpexjpg.")]
    [InlineData("other", null, "exporter must be lcodpanim, lcodpavi, lcodpexpng or lcodpexjpg.")]
    [InlineData("", "novalue", "exporterOptions entries must be name=value; names use letters, digits, '.', '_' and '-'.")]
    [InlineData("", "=x", "exporterOptions entries must be name=value; names use letters, digits, '.', '_' and '-'.")]
    [InlineData("", "bad name=1", "exporterOptions entries must be name=value; names use letters, digits, '.', '_' and '-'.")]
    public void ExporterAndOptionProblemsHaveExactMessages(string exporter, string option, string expected)
    {
        var request = Valid();
        request.Exporter = exporter;
        request.ExporterOptions = option == null ? null : new List<string> { option };
        Assert.Equal(expected, ViewpointAnimationExportRules.Describe(request));
    }

    [Fact]
    public void ExporterDefaultsToTheAnimationExporterAndOptionsAreCapped()
    {
        Assert.Equal(new[] { "lcodpanim", "lcodpavi", "lcodpexpng", "lcodpexjpg" }, ViewpointAnimationExportRules.Exporters);
        Assert.Equal("lcodpanim", ViewpointAnimationExportRules.NormalizeExporter(null));
        Assert.Equal("lcodpavi", ViewpointAnimationExportRules.NormalizeExporter(" LCODPAVI "));
        var request = Valid();
        request.Exporter = "LcOdPeXpNg";
        request.ExporterOptions = Enumerable.Range(0, 20).Select(i => "o" + i + "=" + i).ToList();
        Assert.Null(ViewpointAnimationExportRules.Describe(request));
        request.ExporterOptions.Add("o20=20");
        Assert.Equal("exporterOptions accepts at most 20 entries.", ViewpointAnimationExportRules.Describe(request));
    }

    [Fact]
    public void ExporterOptionsOverrideTheTypedSettingsOfTheSameName()
    {
        var request = Valid();
        request.Width = 640;
        request.Fps = 6;
        request.ExporterOptions = new List<string> { "export.anim.fps=25", "export.image.format=lcodpexpng" };
        Assert.Equal(
            new[] { "export.image.width=640", "export.anim.fps=25", "export.image.format=lcodpexpng", "export.image.renderer=lcodpopengl" },
            ViewpointAnimationExportRules.MergedAssignments(request).Select(pair => pair.Key + "=" + Convert.ToString(pair.Value, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void AFailedExportStillReturnsItsData()
    {
        var response = new ViewpointAnimationExportResponse();
        Assert.False(response.Exported);
        Assert.Empty(response.OptionsAfter);
        Assert.Null(response.CurrentAnimationAfter);
        Assert.Equal("The animation exporter returned eExport_FAIL; nothing was exported.", ViewpointAnimationExportRules.FailedExportMessage("eExport_FAIL"));
        Assert.Equal("The previous current animation could not be restored; Navisworks keeps 'Flight' selected.",
            ViewpointAnimationExportRules.RestoreWarning("Flight"));
    }

    // #411 (L3 round 5 on #404): the dialog stores png and jpg as adapters over the single-image exporters, and a stored
    // viewport renderer fails every export, so the animation exporter gets both by default. GetIOPluginOptions is a copy
    // of the PluginOptions store that DriveIOPlugin overwrites; the call snapshots the store and puts changed keys back.
    [Theory]
    [InlineData(null, "image_anim_adapter_lcodpexpng")]
    [InlineData("png", "image_anim_adapter_lcodpexpng")]
    [InlineData(" JPG ", "image_anim_adapter_lcodpexjpg")]
    [InlineData("avi", "lcodpavi")]
    public void EachFormatMapsToTheValueTheExportDialogStores(string format, string expected)
    {
        Assert.Equal(expected, ViewpointAnimationExportRules.AnimationFormatValue(format));
        Assert.Equal("lcodpopengl", ViewpointAnimationExportRules.DefaultRenderer);
    }

    [Fact]
    public void TheAnimationExporterGetsFormatAndRendererByDefault()
    {
        var request = Valid();
        request.Format = "jpg";
        Assert.Equal(
            new[] { "export.image.format=image_anim_adapter_lcodpexjpg", "export.image.renderer=lcodpopengl" },
            ViewpointAnimationExportRules.MergedAssignments(request).Select(pair => pair.Key + "=" + pair.Value));
        request.Fps = 4;
        request.ExporterOptions = new List<string> { "export.image.renderer=lcodpviewport" };
        Assert.Equal(
            new[] { "export.anim.fps=4", "export.image.format=image_anim_adapter_lcodpexjpg", "export.image.renderer=lcodpviewport" },
            ViewpointAnimationExportRules.MergedAssignments(request).Select(pair => pair.Key + "=" + pair.Value));
        request.Exporter = "lcodpavi";
        request.ExporterOptions = null;
        Assert.Equal(new[] { "export.anim.fps=4" }, ViewpointAnimationExportRules.MergedAssignments(request).Select(pair => pair.Key + "=" + pair.Value));
    }

    [Fact]
    public void ChangedStoredOptionsAreComparedAsText()
    {
        var before = new Dictionary<string, object>
        {
            ["width"] = 320, ["fps"] = 10, ["format"] = "image_anim_adapter_lcodpexpng", ["renderer"] = "lcodpopengl", ["aa_level"] = "1", ["height"] = 215,
        };
        var after = new Dictionary<string, object>
        {
            ["width"] = "320", ["fps"] = 2, ["format"] = "lcodpavi", ["renderer"] = "lcodpopengl", ["height"] = 215, ["extra"] = 1,
        };
        Assert.Equal(new[] { "aa_level", "format", "fps" }, ViewpointAnimationExportRules.ChangedOptions(before, after));
        Assert.Empty(ViewpointAnimationExportRules.ChangedOptions(before, new Dictionary<string, object>(before)));
        Assert.Empty(ViewpointAnimationExportRules.ChangedOptions(null, after));
        Assert.Equal(new[] { "fps", "width" }, ViewpointAnimationExportRules.ChangedOptions(new Dictionary<string, object> { ["width"] = 1, ["fps"] = 2 }, null));
    }

    [Fact]
    public void OptionsTheDialogKeepsAreNamedInOneWarning()
    {
        var after = new Dictionary<string, object> { ["format"] = "lcodpavi", ["fps"] = 2 };
        Assert.Equal("The Export Animation dialog keeps this call's values: aa_level=, format=lcodpavi, fps=2.",
            ViewpointAnimationExportRules.OptionsRestoreWarning(new[] { "aa_level", "format", "fps" }, after));
        Assert.Null(ViewpointAnimationExportRules.OptionsRestoreWarning(Array.Empty<string>(), after));
        Assert.Null(ViewpointAnimationExportRules.OptionsRestoreWarning(null, after));
        var response = new ViewpointAnimationExportResponse();
        Assert.Empty(response.StoredOptions);
        Assert.Empty(response.RestoredOptions);
        Assert.False(response.OptionsRestored);
    }

    [Fact]
    public void TheToolPreviewsByDefaultAndExportsOnApply()
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == "ViewpointAnimationExport");
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
        Assert.Equal("viewpoint_animation_export", tool.ProtocolTool.Name);
        Assert.Equal(ToolEffects.Files | ToolEffects.View, method.GetCustomAttribute<ToolCapabilitiesAttribute>()!.Effects);
        var properties = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!["properties"]!;
        Assert.Equal("", properties["animation"]!["default"]!.GetValue<string>());
        Assert.Equal("png", properties["format"]!["default"]!.GetValue<string>());
        Assert.Equal("", properties["outputDirectory"]!["default"]!.GetValue<string>());
        Assert.False(properties["apply"]!["default"]!.GetValue<bool>());
        Assert.False(properties["overwrite"]!["default"]!.GetValue<bool>());
        foreach (var name in new[] { "width", "height", "fps", "antiAliasLevel" })
            Assert.Equal(0, properties[name]!["default"]!.GetValue<int>());
        Assert.Equal("lcodpanim", properties["exporter"]!["default"]!.GetValue<string>());
        Assert.NotNull(properties["exporterOptions"]);
    }

    [Fact]
    public void ThePluginExportsThroughTheAnimationExporterAndRestoresTheCurrentAnimation()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "ViewpointAnimationExportService.cs"));
        foreach (var expected in new[]
        {
            "ViewpointAnimationExportRules.Describe(", "ViewpointAnimationExportRules.NormalizeFormat(",
            "ViewpointAnimationExportRules.ResolveOutputDirectory(", "ViewpointAnimationExportRules.FormatOptionValue(",
            "ViewpointAnimationExportRules.ProbedPlugins", "ViewpointAnimationExportRules.ChangedOptions(",
            "ViewpointAnimationExportRules.OptionsRestoreWarning(", "get_PluginOptions(", "set_PluginOptions(",
            "ViewpointAnimationExportRules.OutputFile(", "ViewpointAnimationExportRules.DetectKind(",
            "ViewpointAnimationExportRules.IsForbiddenOutputRoot(", "ViewpointAnimationExportRules.HasExistingOutput(",
            "GetIOPluginOptions(", "DriveIOPlugin(", "state.CurrentAnimation =", "eSavedViewType_Anim",
            "ViewpointAnimationExportRules.NormalizeExporter(", "ViewpointAnimationExportRules.MergedAssignments(",
            "ViewpointAnimationExportRules.FailedExportMessage(", "ViewpointAnimationExportRules.RestoreWarning(",
            "eObjectType_nwOaProperty", "Directory.Delete(response.OutputDirectory)",
            "Directory.CreateDirectory(", "is SavedViewpointAnimation", "Saved viewpoint animation was not found.",
            "Marshal.ReleaseComObject(",
        })
            Assert.Contains(expected, service, StringComparison.Ordinal);
        // Never delete user files (only the empty folder this call created), never write single stored options or the registry, never touch the
        // document, and never throw away a failed export's data: it returns FailedExportMessage instead of an error.
        foreach (var forbidden in new[]
        {
            "File.Delete", "Directory.Delete(response.OutputDirectory, true", "recursive: true", "set_PluginOption(", "Registry",
            "SaveFile", "Transaction", "\"The animation exporter returned \" +",
        })
            Assert.DoesNotContain(forbidden, service, StringComparison.Ordinal);
        var client = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper.McpServer", "Services", "HostBridgeClient.Commands.cs"));
        Assert.Contains("HostCommandNames.ViewpointAnimationExport, request, cancellationToken, target, 600000)", client, StringComparison.Ordinal);
    }

    private static (string Type, string Value) Format(object value)
    {
        string type;
        var text = ViewpointAnimationExportRules.FormatOptionValue(value, out type);
        return (type, text);
    }
}
