using System.Xml.Linq;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #500 (owner, 2026-11-17): the FBX and Datasmith checkboxes stood too close together; they go into a
// captioned group box with spacing. Pinned by source like ExportListPanelTests; the owner checks the look.
public sealed class ExportFormatsGroupTests
{
    [Fact]
    public void TheFormatCheckboxesSitInACaptionedGroupBox()
    {
        var section = File.ReadAllText(Path.Combine(Root(), "NavisHelper", "WPF", "FbxLevelExportSection.cs"));

        Assert.Contains("new GroupBox", section, StringComparison.Ordinal);
        Assert.Contains("BindHeader(", section, StringComparison.Ordinal);
        Assert.Contains("\"Panel_Export_Formats_Header\"", section, StringComparison.Ordinal);
        Assert.Contains("_fbx.Margin =", section, StringComparison.Ordinal);
        Assert.Contains("_datasmith.Margin =", section, StringComparison.Ordinal);
        Assert.DoesNotContain("_root.Children.Add(_fbx);", section, StringComparison.Ordinal);
        Assert.DoesNotContain("_root.Children.Add(_datasmith);", section, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Resources.resx", "Formats")]
    [InlineData("Resources.ru.resx", "Форматы")]
    public void TheCaptionExistsInBothLanguages(string file, string expected)
    {
        var value = XDocument.Load(Path.Combine(Root(), "NavisHelper", "Properties", file))
            .Root!.Elements("data")
            .Where(e => (string?)e.Attribute("name") == "Panel_Export_Formats_Header")
            .Select(e => (string?)e.Element("value"))
            .SingleOrDefault();
        Assert.Equal(expected, value);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
