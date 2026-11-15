using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance for #475. "0000 Базовый вид", the first viewpoint of every Clash
// viewpoint folder, was saved camera-only (SaveCurrentViewpointOnlyAtStart): without the Hide/Required
// and Override Material attributes Navisworks keeps the current overrides when it is activated, and
// the panel could save the user's section planes into it. Both call sites now turn clipping off and
// save through SaveCurrentResetViewpointAtStart (COM, material and hide attributes on). The plugin
// cannot compile in L0, so the call sites are pinned by source; L3 activates the viewpoint.
public sealed class ClashResetViewpointTests
{
    [Fact]
    public void ThePanelSavesTheResetViewpointWithAppearanceAndWithoutClipping()
    {
        var source = Read("NavisHelper", "WPF", "NavisHelperPanel.Clash.Operations.cs");
        int start = source.IndexOf("if (createResetViewpoint)", StringComparison.Ordinal);
        Assert.True(start >= 0, "the reset block is present");
        int end = source.IndexOf("resetCreated++;", start, StringComparison.Ordinal);
        Assert.True(end > start);
        AssertResetSave(source.Substring(start, end - start));
    }

    [Fact]
    public void TheMcpPathSavesTheResetViewpointWithAppearanceAndWithoutClipping()
    {
        var source = Read("NavisHelper", "Agent", "Services", "ClashSavedViewpointCreationService.cs");
        int start = source.IndexOf("public static ClashSavedViewpointCreationResult SaveResetViewpoint(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = source.IndexOf("\n        }", start, StringComparison.Ordinal);
        AssertResetSave(source.Substring(start, end - start));
    }

    private static void AssertResetSave(string block)
    {
        int disable = block.IndexOf("SectionBoxHelper.Disable()", StringComparison.Ordinal);
        int save = block.IndexOf("SavedViewpointAppearanceHelper.SaveCurrentResetViewpointAtStart(", StringComparison.Ordinal);
        Assert.True(disable >= 0, "clipping is turned off before the reset viewpoint is saved");
        Assert.True(save > disable, "the reset viewpoint is saved with its appearance after clipping is off");
        Assert.DoesNotContain("SaveCurrentViewpointOnlyAtStart", block, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }
}
