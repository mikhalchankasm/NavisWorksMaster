using System.Xml.Linq;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance for #478 slice beta: the Export tab gets a "Twinmotion (Datasmith)" card
// that exports the open, saved model file through the installed AutomationRunner and a hidden
// Navisworks 2026, with Cancel. The plugin cannot compile in L0, so it is pinned by source; L3 runs it.
public sealed class DatasmithExportSectionTests
{
    private static readonly string[] Keys =
    {
        "Panel_Export_Group_Datasmith", "Panel_Datasmith_Export_Action", "Panel_Datasmith_Cancel_Action",
        "Panel_Datasmith_Dialog_Title", "Panel_Datasmith_Running_Status", "Panel_Datasmith_Completed_Status",
        "Panel_Datasmith_Failed_Status", "Panel_Datasmith_Canceled_Status", "Panel_Datasmith_NotSaved_Status",
        "Panel_Datasmith_Unavailable_Status",
    };

    [Fact]
    public void TheSectionIsItsOwnCompiledType()
    {
        var section = Read("NavisHelper", "WPF", "DatasmithExportSection.cs");
        Assert.Contains("internal sealed class DatasmithExportSection : IDisposable", section, StringComparison.Ordinal);
        var project = Read("NavisHelper", "NavisHelper.csproj");
        Assert.Contains(@"<Compile Include=""WPF\DatasmithExportSection.cs"" />", project, StringComparison.Ordinal);
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
    public void TheExportTabHostsTheCardAndThePanelDisposesIt()
    {
        var panel = string.Join("\n", Directory.GetFiles(Path.Combine(Root(), "NavisHelper", "WPF"), "NavisHelperPanel*.cs").Select(File.ReadAllText));
        int tab = panel.IndexOf("private TabItem CreateExportTab()", StringComparison.Ordinal);
        Assert.True(tab >= 0);
        var export = panel.Substring(tab, panel.IndexOf("\n        }", tab, StringComparison.Ordinal) - tab);
        Assert.Contains("new DatasmithExportSection(", export, StringComparison.Ordinal);
        Assert.Contains("\"Panel_Export_Group_Datasmith\"", export, StringComparison.Ordinal);
        Assert.Contains("_datasmithExportSection?.Dispose()", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSectionExportsOnlyASavedFileAndAsksForTheTarget()
    {
        var section = Read("NavisHelper", "WPF", "DatasmithExportSection.cs");
        Assert.Contains("new SaveFileDialog", section, StringComparison.Ordinal);
        Assert.Contains(".udatasmith", section, StringComparison.Ordinal);
        Assert.Contains("OverwritePrompt = true", section, StringComparison.Ordinal);
        Assert.Contains("Panel_Datasmith_NotSaved_Status", section, StringComparison.Ordinal);
        Assert.Contains("FileName", section, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTextIsLocalized()
    {
        var english = Values("Resources.resx");
        var russian = Values("Resources.ru.resx");
        foreach (var key in Keys)
        {
            Assert.True(english.TryGetValue(key, out var en) && en.Length > 0, key + " in Resources.resx");
            Assert.True(russian.TryGetValue(key, out var ru) && ru.Any(c => c >= 'А' && c <= 'я') || key == "Panel_Export_Group_Datasmith",
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
