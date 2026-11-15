using System.Xml.Linq;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance for #462. "Save as NWD 2018" used to call
// document.TrySaveFile(document.FileName, Navisworks2018) in place: no dialog, the open file
// (even an .nwf) overwritten, an unsaved document written to %TEMP%, and the result only in the
// log. It now asks for a separate .nwd in a save dialog and saves through the guarded save-as
// path. The plugin cannot compile in L0, so the add-in is pinned by source; L2 builds it and the
// owner's L3 clicks it on a scratch copy.
public sealed class SaveAsPublishNavis2018DialogTests
{
    private static readonly string[] Keys =
    {
        "Save2018_Dialog_Title",
        "Save2018_Dialog_Filter",
        "Save2018_Saved_Message",
        "Save2018_Failed_Message",
        "Save2018_NoDocument_Message",
    };

    [Fact]
    public void TheAddInAsksForASeparateNwdAndSavesThroughTheGuardedPath()
    {
        var execute = ExecuteBody();

        Assert.Contains("new SaveFileDialog", execute, StringComparison.Ordinal);
        Assert.Contains("OverwritePrompt = true", execute, StringComparison.Ordinal);
        Assert.Contains("_2018.nwd", execute, StringComparison.Ordinal);
        Assert.Contains("DialogResult.OK", execute, StringComparison.Ordinal);
        Assert.Contains("DocumentCommandService.SaveDocumentAsToPath(", execute, StringComparison.Ordinal);
        Assert.Contains("\"2018\"", execute, StringComparison.Ordinal);
        Assert.Contains("MessageBox.Show(", execute, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAddInNeverSavesInPlaceOrToTemp()
    {
        var execute = ExecuteBody();

        Assert.DoesNotContain("TrySaveFile(", execute, StringComparison.Ordinal);
        Assert.DoesNotContain(".SaveFile(", execute, StringComparison.Ordinal);
        Assert.DoesNotContain("GetTempPath(", execute, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryMessageIsLocalizedInEnglishAndRussian()
    {
        var execute = ExecuteBody();
        var english = Values("Resources.resx");
        var russian = Values("Resources.ru.resx");

        foreach (var key in Keys)
        {
            Assert.Contains("\"" + key + "\"", execute, StringComparison.Ordinal);
            Assert.True(english.ContainsKey(key) && english[key].Length > 0, key + " in Resources.resx");
            Assert.True(russian.ContainsKey(key) && russian[key].Any(c => c >= 'А' && c <= 'я'), key + " in Russian");
        }
    }

    // The button, the palette and the tooltip said "Save as", "Export" and "Resave".
    [Fact]
    public void TheLabelsNoLongerPromiseAnInPlaceResaveOrAnExport()
    {
        var english = Values("Resources.resx");

        Assert.DoesNotContain("Resave", english["Panel_Model_Save2018_ToolTip"], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Export", english["Panel_CommandPalette_SaveNwd2018_Description"], StringComparison.OrdinalIgnoreCase);
        var russian = Values("Resources.ru.resx");
        Assert.DoesNotContain("Пересохранить", russian["Panel_Model_Save2018_ToolTip"], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Экспорт", russian["Panel_CommandPalette_SaveNwd2018_Description"], StringComparison.OrdinalIgnoreCase);
    }

    private static string ExecuteBody()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "NavisHelper", "SaveAsNavis2018.cs"));
        int start = source.IndexOf("public override int Execute(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = source.IndexOf("\n        }", start, StringComparison.Ordinal);
        Assert.True(end > start);
        // Helpers called from Execute belong to it as well.
        return source.Substring(start);
    }

    private static Dictionary<string, string> Values(string file)
    {
        var document = XDocument.Load(Path.Combine(RepositoryRoot(), "NavisHelper", "Properties", file));
        return document.Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
