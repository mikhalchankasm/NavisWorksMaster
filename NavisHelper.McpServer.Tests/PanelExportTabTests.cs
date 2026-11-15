using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance for #476 (owner, 2026-11-15, mockup approved): the exports move to their
// own tab right after Model, and the Model tab becomes four coloured cards. The plugin cannot compile
// in L0, so the panel is pinned by source; L3 looks at it.
public sealed class PanelExportTabTests
{
    private static readonly string[] ExportButtons = { "\"save_hierarchy\"", "\"save_nwd2018\"", "\"export_selected_props\"" };

    [Fact]
    public void TheExportTabFollowsTheModelTab()
    {
        var panel = Read("NavisHelperPanel.cs");
        int model = panel.IndexOf("Items.Add(CreateModelTab())", StringComparison.Ordinal);
        int export = panel.IndexOf("Items.Add(CreateExportTab())", StringComparison.Ordinal);
        int colors = panel.IndexOf("Items.Add(CreateColorsTab())", StringComparison.Ordinal);
        Assert.True(model >= 0 && export > model && colors > export, "Model, Export, Colors in this order");
    }

    [Fact]
    public void TheExportTabHoldsTheExportsAndTheFbxSection()
    {
        var export = Body(AllPanelSources(), "private TabItem CreateExportTab()");
        foreach (var button in ExportButtons)
            Assert.Contains(button, export, StringComparison.Ordinal);
        Assert.Contains("_fbxLevelExportSection.Build()", export, StringComparison.Ordinal);
        Assert.Contains("\"Panel_Tab_Export\"", export, StringComparison.Ordinal);
        Assert.True(Count(export, "CreateGroupCard(") >= 2, "the exports sit in cards");
        Assert.Contains("UiTheme.GroupExport", export, StringComparison.Ordinal);
    }

    [Fact]
    public void EachExportButtonExistsOnceSoTheModelTabNoLongerHasIt()
    {
        var all = AllPanelSources();
        // Count the button calls: the icon map in NavisHelperPanel.Resources.cs also names each icon.
        foreach (var button in ExportButtons)
            Assert.Equal(1, Count(all, "Btn(" + button));
        Assert.Equal(1, Count(all, "_fbxLevelExportSection.Build()"));
        Assert.DoesNotContain("_fbxLevelExportSection.Build()", Body(all, "private TabItem CreateModelTab()"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheModelTabIsFourColouredCards()
    {
        var all = AllPanelSources();
        foreach (var accent in new[] { "UiTheme.GroupNavigation", "UiTheme.GroupSelection", "UiTheme.GroupInfo", "UiTheme.GroupImport" })
            Assert.Matches(new Regex(@"CreateGroupCard\([^;]*" + Regex.Escape(accent)), all);

        var theme = File.ReadAllText(Path.Combine(Root(), "NavisHelper", "WPF", "UiTheme.cs"));
        foreach (var (name, rgb) in new[] { ("GroupNavigation", "0x53, 0x4A, 0xB7"), ("GroupSelection", "0x0F, 0x6E, 0x56"),
                     ("GroupInfo", "0x5F, 0x5E, 0x5A"), ("GroupImport", "0x99, 0x3C, 0x1D"), ("GroupExport", "0x18, 0x5F, 0xA5") })
            Assert.Contains("public static readonly Brush " + name + " = Rgb(" + rgb + ")", theme, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExportTabIsLocalized()
    {
        Assert.Equal("Export", Value("Resources.resx", "Panel_Tab_Export").Trim().Split(' ').Last());
        Assert.Equal("Экспорт", Value("Resources.ru.resx", "Panel_Tab_Export").Trim().Split(' ').Last());
    }

    // L3 of #444: the panel's shared status line kept "FBX export completed" after Add selected.
    [Fact]
    public void EditingTheFbxListAlsoClearsTheSharedStatusLine()
    {
        var section = File.ReadAllText(Path.Combine(Root(), "NavisHelper", "WPF", "FbxLevelExportSection.cs"));
        Assert.Contains("_clearSharedStatus?.Invoke();", Body(section, "private void ClearStaleStatus()"), StringComparison.Ordinal);
        Assert.Contains("new FbxLevelExportSection(", AllPanelSources(), StringComparison.Ordinal);
    }

    private static string AllPanelSources()
    {
        var folder = Path.Combine(Root(), "NavisHelper", "WPF");
        return string.Join("\n", Directory.GetFiles(folder, "NavisHelperPanel*.cs").OrderBy(f => f, StringComparer.Ordinal).Select(File.ReadAllText));
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(Root(), "NavisHelper", "WPF", file));

    private static string Body(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " is present");
        int end = source.IndexOf("\n        }", start, StringComparison.Ordinal);
        return source.Substring(start, end - start);
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        for (int at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string Value(string file, string key)
    {
        var doc = XDocument.Load(Path.Combine(Root(), "NavisHelper", "Properties", file));
        var data = doc.Root!.Elements("data").FirstOrDefault(d => (string?)d.Attribute("name") == key);
        Assert.True(data != null, key + " in " + file);
        return (string?)data!.Element("value") ?? string.Empty;
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
