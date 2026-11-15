using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance for #461. A SavedViewpointAnimation is a GroupItem whose Children are
// its frames, so saved_viewpoints_reorder recursive=true descended into animations and sorted
// their frames into name order, silently changing playback, and reported each one as a folder.
// Reorder now collects FolderItem only and refuses a folderPath/itemId that names an animation.
// The plugin cannot compile in L0, so the method bodies are pinned by source; L3 checks the
// runtime types and the frame order on Navisworks.
public sealed class SavedViewpointReorderArchitectureTests
{
    [Fact]
    public void RecursionCollectsFoldersOnlyNeverAnimations()
    {
        var collect = Body("private static void CollectFoldersForReorder(");

        Assert.Contains("as FolderItem", collect, StringComparison.Ordinal);
        Assert.DoesNotContain("as GroupItem", collect, StringComparison.Ordinal);
        Assert.DoesNotContain("is GroupItem", collect, StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderPathOrItemIdThatNamesAnAnimationIsRefused()
    {
        var resolve = Body("private static GroupItem ResolveSavedViewpointFolderForReorder(");

        Assert.Contains("FolderItem", resolve, StringComparison.Ordinal);
        Assert.Contains("is a viewpoint animation, not a folder", resolve, StringComparison.Ordinal);
        Assert.Contains("ErrorCodes.SchemaViolation", resolve, StringComparison.Ordinal);
    }

    private static string Body(string signature)
    {
        var source = Read("NavisHelper", "Agent", "Services", "DocumentCommandService.Viewpoints.Management.cs");
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature + " is present");
        int end = source.IndexOf("\n        }", start, StringComparison.Ordinal);
        Assert.True(end > start);
        return source.Substring(start, end - start);
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
