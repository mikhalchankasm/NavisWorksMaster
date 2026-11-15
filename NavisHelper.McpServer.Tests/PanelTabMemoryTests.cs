using System.Text.RegularExpressions;
using NavisHelper.Core;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance for #486. The panel remembered its tab by index, so inserting the Export
// tab (#476) shifted every saved choice by one. It now stores the tab's resource key; a file with
// only the old MainTabIndex is read in the 2.20 tab order. The FBX section becomes a card like its
// neighbours. The resolver is plain code compiled here; the panel is pinned by source, L3 looks at it.
public sealed class PanelTabMemoryTests
{
    private static readonly string[] Tabs =
    {
        "Panel_Tab_Model", "Panel_Tab_Export", "Panel_Tab_Colors", "Panel_Tab_Views", "Panel_Tab_Clashes", "Panel_Tab_Settings",
    };

    [Fact]
    public void ASavedKeyWins()
    {
        Assert.Equal("Panel_Tab_Views", PanelTabSelection.Resolve("Panel_Tab_Views", 0, Tabs));
    }

    [Theory]
    [InlineData(0, "Panel_Tab_Model")]
    [InlineData(1, "Panel_Tab_Colors")]
    [InlineData(2, "Panel_Tab_Views")]
    [InlineData(3, "Panel_Tab_Clashes")]
    [InlineData(4, "Panel_Tab_Settings")]
    public void AnOldIndexIsReadInThe220Order(int legacyIndex, string expected)
    {
        Assert.Equal(expected, PanelTabSelection.Resolve(null, legacyIndex, Tabs));
    }

    [Theory]
    [InlineData("Panel_Tab_Gone", 2, "Panel_Tab_Views")]
    [InlineData("Panel_Tab_Gone", null, "Panel_Tab_Model")]
    [InlineData(null, 9, "Panel_Tab_Model")]
    [InlineData(null, -1, "Panel_Tab_Model")]
    [InlineData("", null, "Panel_Tab_Model")]
    public void UnknownValuesFallBack(string? savedKey, int? legacyIndex, string expected)
    {
        Assert.Equal(expected, PanelTabSelection.Resolve(savedKey, legacyIndex, Tabs));
    }

    [Fact]
    public void TheSettingsFileStoresTheKey()
    {
        var settings = Read("NavisHelper", "Core", "PanelUiSettings.cs");
        var save = Body(settings, "public void Save()");

        Assert.Contains("MainTab=", save, StringComparison.Ordinal);
        Assert.DoesNotContain("MainTabIndex=", save, StringComparison.Ordinal);
        Assert.Contains("case \"MainTab\":", settings, StringComparison.Ordinal);
        Assert.Contains("case \"MainTabIndex\":", settings, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePanelRestoresAndSavesByKey()
    {
        var panel = Read("NavisHelper", "WPF", "NavisHelperPanel.cs");

        Assert.Contains("PanelTabSelection.Resolve(", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("_mainTabControl.SelectedIndex = _panelUiSettings.MainTabIndex", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("_panelUiSettings.MainTabIndex = _mainTabControl.SelectedIndex", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFbxSectionIsACardWithOneHeader()
    {
        var export = Body(Read("NavisHelper", "WPF", "NavisHelperPanel.cs"), "private TabItem CreateExportTab()");
        var section = Read("NavisHelper", "WPF", "FbxLevelExportSection.cs");

        Assert.Matches(new Regex(@"CreateGroupCard\(""Panel_Model_Group_FbxByLevel"",[^;]*_fbxLevelExportSection\.Build\(\)\)"), export);
        Assert.DoesNotContain("\"Panel_Model_Group_FbxByLevel\"", section, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryPaths.Root }.Concat(parts).ToArray()));

    private static string Body(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " not found");
        int open = source.IndexOf('{', start), depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        return source.Substring(open);
    }
}
