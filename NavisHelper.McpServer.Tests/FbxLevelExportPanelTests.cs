using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #444 slice delta: the panel section for FBX export by Selection Tree level. The plugin cannot
// compile in L0, so the wiring is pinned by source, as FbxLevelExportHostWiringTests does for the
// host side; L2 builds it and L3 on Navisworks 2027 runs it. The one rule that matters most: the
// panel drives the host's own job service, so a panel export and an MCP export share the single
// running-job lock and the same status.
public sealed class FbxLevelExportPanelTests
{
    private static readonly string[] ResourceKeys =
    {
        "Panel_Model_Group_FbxByLevel",
        "Panel_Model_FbxLevel_Label",
        "Panel_Model_FbxLevel_ToolTip",
        "Panel_Model_FbxFolder_Label",
        "Panel_Model_FbxFolder_Prompt",
        "Panel_Model_FbxOverwrite_Label",
        "Panel_Model_FbxStart_Action",
        "Panel_Model_FbxCancel_Action",
        "Panel_Model_FbxProgress_Status",
        "Panel_Model_FbxCompleted_Status",
        "Panel_Model_FbxCanceled_Status",
        "Panel_Model_FbxFailed_Status",
        "Panel_Model_FbxNoDocument_Status",
        "Panel_Model_FbxUnavailable_Status",
    };

    [Fact]
    public void TheSectionIsItsOwnTypeNotAPanelPartial()
    {
        var section = Read("NavisHelper", "WPF", "FbxLevelExportSection.cs");
        Assert.Contains("internal sealed class FbxLevelExportSection : IDisposable", section, StringComparison.Ordinal);
        Assert.Contains("internal UIElement Build()", section, StringComparison.Ordinal);
        Assert.DoesNotContain("partial class NavisHelperPanel", section, StringComparison.Ordinal);

        var project = Read("NavisHelper", "NavisHelper.csproj");
        Assert.Contains(@"<Compile Include=""WPF\FbxLevelExportSection.cs"" />", project, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePanelDrivesTheHostsOwnJobService()
    {
        var section = Read("NavisHelper", "WPF", "FbxLevelExportSection.cs");
        foreach (var expected in new[]
                 {
                     "AgentRuntime.FbxLevelExportJobs", ".Start(", ".Status(", ".Cancel(",
                     "new StartFbxLevelExportRequest", "Overwrite =", "FolderPickerDialog.Show(",
                     "DispatcherTimer", "FbxLevelExportJobStates.Running",
                 })
            Assert.Contains(expected, section, StringComparison.Ordinal);

        var runtime = Read("NavisHelper", "Agent", "AgentRuntime.cs");
        Assert.Contains("FbxLevelExportJobs", runtime, StringComparison.Ordinal);
        var host = Read("NavisHelper", "Agent", "Host", "AgentHostService.cs");
        Assert.Contains("FbxLevelExportJobs => _fbxLevelExportJobService", host, StringComparison.Ordinal);

        // Exactly one job service in the plugin: the host's. A second instance would let a panel
        // export and an MCP export run at once over one document.
        var creations = Directory
            .EnumerateFiles(Path.Combine(RepositoryPaths.Root, "NavisHelper"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Sum(path => Regex.Matches(File.ReadAllText(path), @"new\s+FbxLevelExportJobService\s*\(").Count);
        Assert.Equal(1, creations);
    }

    [Fact]
    public void TheModelTabHostsTheSectionAndThePanelDisposesIt()
    {
        var panel = Read("NavisHelper", "WPF", "NavisHelperPanel.cs");
        Assert.Contains("new FbxLevelExportSection(", panel, StringComparison.Ordinal);
        Assert.Contains("_fbxLevelExportSection.Build()", panel, StringComparison.Ordinal);
        Assert.Contains("_fbxLevelExportSection?.Dispose()", panel, StringComparison.Ordinal);

        int modelTab = panel.IndexOf("private TabItem CreateModelTab()", StringComparison.Ordinal);
        int colorsTab = panel.IndexOf("private TabItem CreateColorsTab()", StringComparison.Ordinal);
        int build = panel.IndexOf("_fbxLevelExportSection.Build()", StringComparison.Ordinal);
        Assert.True(modelTab >= 0 && modelTab < build && build < colorsTab,
            "The section is added in CreateModelTab.");
    }

    [Fact]
    public void EveryLabelIsAResourceInBothLanguagesAndTheSectionUsesThemAll()
    {
        var neutral = Values("Resources.resx");
        var russian = Values("Resources.ru.resx");
        var section = Read("NavisHelper", "WPF", "FbxLevelExportSection.cs");
        foreach (var key in ResourceKeys)
        {
            Assert.True(neutral.TryGetValue(key, out var en) && !string.IsNullOrWhiteSpace(en), "neutral " + key);
            Assert.True(russian.TryGetValue(key, out var ru) && Regex.IsMatch(ru, "[А-Яа-яЁё]"), "russian " + key);
            Assert.Contains("\"" + key + "\"", section, StringComparison.Ordinal);
        }
    }

    private static Dictionary<string, string> Values(string file) =>
        XDocument.Load(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Properties", file))
            .Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string?)data.Element("value") ?? "");

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryPaths.Root }.Concat(parts).ToArray()));
}
