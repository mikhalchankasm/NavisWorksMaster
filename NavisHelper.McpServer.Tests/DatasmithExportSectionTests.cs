using System.Xml.Linq;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #495 replaces the separate whole-file card with format choices in the export list.
// Keep the runner guard: whole-file export remains available through MCP.
public sealed class DatasmithExportSectionTests
{
    private static readonly string[] Keys =
    {
        "Panel_Export_FormatFbx_Label", "Panel_Export_FormatDatasmith_Label", "Panel_Export_NoFormat_Status",
        "Panel_Datasmith_Unavailable_Status",
    };

    [Fact]
    public void TheListReplacesTheSeparateCompiledSection()
    {
        Assert.False(File.Exists(Path.Combine(Root(), "NavisHelper", "WPF", "DatasmithExportSection.cs")));
        var project = Read("NavisHelper", "NavisHelper.csproj");
        Assert.DoesNotContain(@"<Compile Include=""WPF\DatasmithExportSection.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""WPF\FbxLevelExportSection.cs"" />", project, StringComparison.Ordinal);
        Assert.Contains(@"<Compile Include=""Agent\Services\DatasmithPanelExportJob.cs"" />", project, StringComparison.Ordinal);
    }

    [Fact]
    public void TheJobStartsTheInstalledRunnerInsideAKillOnCloseJob()
    {
        var job = Read("NavisHelper", "Agent", "Services", "DatasmithPanelExportJob.cs");
        foreach (var needle in new[]
                 {
                     "DatasmithRunnerCommandLine.BuildArguments(", "DatasmithRunnerCommandLine.Join(", "new KillOnCloseJob(",
                     "NAVISHELPER_AUTOMATION_RUNNER", "NavisHelper.AutomationRunner.exe", "Navisworks Manage 2026",
                     "Autodesk.Navisworks.Automation.dll", "CreateNoWindow = true",
                 })
            Assert.Contains(needle, job, StringComparison.Ordinal);
        // Cancel ends the runner's job object, which ends the runner and the hidden Navisworks with it.
        int cancel = job.IndexOf("public void Cancel()", StringComparison.Ordinal);
        Assert.True(cancel >= 0);
        Assert.Contains(".Dispose()", job.Substring(cancel, job.IndexOf("\n        }", cancel, StringComparison.Ordinal) - cancel), StringComparison.Ordinal);
    }

    [Fact]
    public void TheExportTabHostsOnlyTheListAndThePanelDisposesIt()
    {
        var panel = string.Join("\n", Directory.GetFiles(Path.Combine(Root(), "NavisHelper", "WPF"), "NavisHelperPanel*.cs").Select(File.ReadAllText));
        int tab = panel.IndexOf("private TabItem CreateExportTab()", StringComparison.Ordinal);
        Assert.True(tab >= 0);
        var export = panel.Substring(tab, panel.IndexOf("\n        }", tab, StringComparison.Ordinal) - tab);
        Assert.Contains("new FbxLevelExportSection(", export, StringComparison.Ordinal);
        Assert.DoesNotContain("DatasmithExportSection", panel, StringComparison.Ordinal);
        Assert.Contains("_fbxLevelExportSection?.Dispose()", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void TheListDefaultsToFbxAndDisablesDatasmithWhenADependencyIsMissing()
    {
        var section = Read("NavisHelper", "WPF", "FbxLevelExportSection.cs");
        Assert.Contains("_fbx = new CheckBox { IsChecked = true }", section, StringComparison.Ordinal);
        Assert.Contains("DatasmithPanelExportJob.MissingDependency()", section, StringComparison.Ordinal);
        Assert.Contains("_datasmith.IsEnabled = missing == null", section, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.SetShowOnDisabled(_datasmith, true)", section, StringComparison.Ordinal);
        Assert.Contains("_bindings.Text(\"Panel_Datasmith_Unavailable_Status\"), missing)", section, StringComparison.Ordinal);
        Assert.Contains("_datasmith.IsEnabled && _datasmith.IsChecked == true", section, StringComparison.Ordinal);
        foreach (var key in Keys) Assert.Contains("\"" + key + "\"", section, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTextIsLocalized()
    {
        var english = Values("Resources.resx");
        var russian = Values("Resources.ru.resx");
        foreach (var key in Keys)
        {
            Assert.True(english.TryGetValue(key, out var en) && en.Length > 0, key + " in Resources.resx");
            Assert.True(russian.TryGetValue(key, out var ru) && ru.Length > 0 &&
                (ru.Any(c => c >= 'А' && c <= 'я') || key == "Panel_Export_FormatFbx_Label" || key == "Panel_Export_FormatDatasmith_Label"),
                key + " in Russian");
        }
    }

    private static Dictionary<string, string> Values(string file) =>
        XDocument.Load(Path.Combine(Root(), "NavisHelper", "Properties", file)).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
