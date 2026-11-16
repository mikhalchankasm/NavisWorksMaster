using System.Xml.Linq;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #495 (owner, 2026-11-16): the Export tab has ONE export list. Its rows are exactly the items to
// export (no depth), and FBX / Datasmith checkboxes choose the formats; the separate whole-file
// Datasmith card is gone. The plugin cannot compile in L0, so the wiring is pinned by source, as
// FbxLevelExportPanelTests does; L2 builds it and L3 runs it.
public sealed class ExportListPanelTests
{
    private static readonly string[] NewKeys =
    {
        "Panel_Export_FormatFbx_Label",
        "Panel_Export_FormatDatasmith_Label",
        "Panel_Export_NoFormat_Status",
    };

    [Fact]
    public void TheListExportsItsRowsAsIs_InTheTickedFormats()
    {
        var section = Read("NavisHelper", "WPF", "FbxLevelExportSection.cs");

        Assert.DoesNotContain("new ComboBox", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Panel_Model_FbxLevel_Label", section, StringComparison.Ordinal);
        Assert.Contains("ItemExportFormats.Fbx", section, StringComparison.Ordinal);
        Assert.Contains("ItemExportFormats.Datasmith", section, StringComparison.Ordinal);
        Assert.Contains(".Start(document, _list.Items, 0,", section, StringComparison.Ordinal);
        foreach (var key in NewKeys)
            Assert.Contains("\"" + key + "\"", section, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExportTabHasNoSeparateDatasmithCard()
    {
        var panel = Read("NavisHelper", "WPF", "NavisHelperPanel.cs");

        Assert.DoesNotContain("new DatasmithExportSection(", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Panel_Export_Group_Datasmith\"", panel, StringComparison.Ordinal);
        Assert.Contains("new FbxLevelExportSection(", panel, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Resources.resx")]
    [InlineData("Resources.ru.resx")]
    public void NewStringsExistInBothLanguages(string file)
    {
        var values = XDocument.Load(Path.Combine(Root(), "NavisHelper", "Properties", file))
            .Root!.Elements("data")
            .ToDictionary(e => (string)e.Attribute("name")!, e => (string?)e.Element("value") ?? "");
        foreach (var key in NewKeys)
        {
            Assert.True(values.ContainsKey(key), file + " lacks " + key);
            Assert.False(string.IsNullOrWhiteSpace(values[key]), file + " has an empty " + key);
        }
        Assert.Contains("Datasmith", values["Panel_Model_Group_FbxByLevel"], StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
