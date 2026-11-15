using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance for #466. In the Navisworks API a viewpoint animation is a GroupItem whose
// children are its frames, so the saved-viewpoint folder walk, which took the first child GroupItem
// with the segment's name, sent create_viewpoint, saved_viewpoints_import and the move operation into
// an animation as new frames, and reported success. The walk now matches FolderItem only and refuses
// an animation segment. The plugin cannot compile in L0, so the walk is pinned by source; L3 checks it.
public sealed class SavedViewpointFolderResolveTests
{
    private static string Service() =>
        File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "DocumentCommandService.cs"));

    [Fact]
    public void TheViewpointWalkMatchesFoldersOnly()
    {
        var walk = Body(Service(), "internal static bool TryResolveSavedViewpointFolder(");

        Assert.DoesNotContain("FindChildGroupByName(", walk, StringComparison.Ordinal);
        Assert.Contains("FindChildViewpointFolder(", walk, StringComparison.Ordinal);
        Assert.Contains("OfType<FolderItem>()", Body(Service(), "FindChildViewpointFolder(GroupItem"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnimationSegmentIsRefusedNotCreatedOver()
    {
        var walk = Body(Service(), "internal static bool TryResolveSavedViewpointFolder(");

        Assert.Contains("is a viewpoint animation, not a folder.", walk, StringComparison.Ordinal);
        int refusal = walk.IndexOf("is a viewpoint animation, not a folder.", StringComparison.Ordinal);
        int create = walk.IndexOf("savedViewpoints.AddCopy(", StringComparison.Ordinal);
        Assert.True(create < 0 || refusal < create, "the refusal comes before a folder is created");
    }

    [Fact]
    public void TheSelectionSetTreeKeepsItsGroupWalk()
    {
        var tree = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services",
            "DocumentCommandService.SelectionSetTree.cs"));

        Assert.Contains("FindChildGroupByName(", tree, StringComparison.Ordinal);
        Assert.Contains("private static GroupItem FindChildGroupByName(", Service(), StringComparison.Ordinal);
    }

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
