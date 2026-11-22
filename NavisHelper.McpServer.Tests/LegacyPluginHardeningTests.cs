using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class LegacyPluginHardeningTests
{
    [Fact]
    public void LegacyCommands_GuardActiveDocumentAndDisposeResources()
    {
        var parentChild = ReadRepoFile("NavisHelper/ParentChildSelector.cs");
        Assert.Contains("Application.ActiveDocument?.CurrentSelection?.SelectedItems", parentChild, StringComparison.Ordinal);
        Assert.Contains("Application.ActiveDocument?.CurrentSelection;", parentChild, StringComparison.Ordinal);

        var importPs = ReadRepoFile("NavisHelper/ImportPslists.cs");
        Assert.Contains("using (var openDialog = new OpenFileDialog()", importPs, StringComparison.Ordinal);
        Assert.Contains("if (doc == null || doc.SelectionSets == null || doc.SelectionSets.RootItem == null)", importPs, StringComparison.Ordinal);
        Assert.Contains("(string)item.Attribute(\"Name\") ?? string.Empty", importPs, StringComparison.Ordinal);
        Assert.DoesNotContain(".Attribute(\"Name\").Value", importPs, StringComparison.Ordinal);

        var hideItems = ReadRepoFile("NavisHelper/HideItems.cs");
        Assert.Contains("DisplayName = \"Hide Items\"", hideItems, StringComparison.Ordinal);
        Assert.Contains("if (doc == null)", hideItems, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayName = \"Override PDMS colors\"", hideItems, StringComparison.Ordinal);

        var colorsByName = ReadRepoFile("NavisHelper/ColorsByName.cs");
        Assert.Contains("var doc = Application.ActiveDocument;", colorsByName, StringComparison.Ordinal);

        var about = ReadRepoFile("NavisHelper/AboutDialog.cs");
        Assert.Contains("protected override void Dispose(bool disposing)", about, StringComparison.Ordinal);
        Assert.Contains("_ownedFonts.Clear();", about, StringComparison.Ordinal);

        var showPanel = ReadRepoFile("NavisHelper/ShowNavisHelperPanel.cs");
        Assert.Contains("_panelFont?.Dispose();", showPanel, StringComparison.Ordinal);
        Assert.Contains("_groupHeaderFont?.Dispose();", showPanel, StringComparison.Ordinal);

        var clashMatrix = ReadRepoFile("NavisHelper.Dev/ClashMatrixFromFileScript.cs");
        Assert.Contains("using (var dialog = new OpenFileDialog", clashMatrix, StringComparison.Ordinal);

        var colorService = ReadRepoFile("ColorService/Program.cs");
        Assert.Contains("static readonly HttpClient SharedHttpClient = new HttpClient", colorService, StringComparison.Ordinal);
        Assert.Contains("static string UnescapeJsonString(string value)", colorService, StringComparison.Ordinal);
        Assert.DoesNotContain("using (var client = new HttpClient())", colorService, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "NavisHelper.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir.FullName, relativePath));
    }
}
