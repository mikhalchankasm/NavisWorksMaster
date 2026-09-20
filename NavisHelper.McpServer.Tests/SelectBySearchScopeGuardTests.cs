using System;
using System.IO;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// A source guard, and it says so rather than pretending to be more.
///
/// The scope test in `SelectBySearch` needs a live Navisworks tree -- `ModelItem.Parent`
/// and `Equals` on a real document -- so the behaviour is verified at L3 and recorded in
/// `docs/PERSISTENT_SCENARIO_LIBRARY_CONTRACT.md`. What can be checked here is the one
/// thing that would silently undo it: rebuilding the scope as a set.
/// </summary>
public sealed class SelectBySearchScopeGuardTests
{
    private static string ReadSelectBySearchBody()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var path = Path.Combine(
            directory!.FullName,
            "NavisHelper", "Agent", "Services", "SearchService.Commands.cs");
        Assert.True(File.Exists(path), path + " is missing; re-point this guard.");

        var source = File.ReadAllText(path);
        var at = source.IndexOf(
            "public SelectBySearchResponse SelectBySearch(",
            StringComparison.Ordinal);
        Assert.True(at >= 0, "SelectBySearch was renamed; re-point this guard.");

        // Up to the next member declaration, so the guard reads this method and not the
        // rest of the file.
        var body = source.Substring(at);
        var end = body.IndexOf("        public ", 1, StringComparison.Ordinal);
        return end > 0 ? body.Substring(0, end) : body;
    }

    [Fact]
    public void TheScopeIsTestedPerMatch_NotMaterialisedAsASet()
    {
        var body = ReadSelectBySearchBody();

        Assert.Contains("IsWithinSelectionScope(item, parent, directChildrenOnly)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSubtreeIsNotEnumeratedIntoACollection()
    {
        // Measured live: with the subtree materialised, selecting 4 items under a parent
        // of roughly 88 000 descendants took 4 462 ms; testing each match instead took
        // 553 ms for the same 4 items. Enumerating parent.Descendants here makes the
        // cost of an answer the size of the scope again.
        var body = ReadSelectBySearchBody();

        Assert.DoesNotContain("parent.Descendants", body, StringComparison.Ordinal);
        Assert.DoesNotContain("parent.Children", body, StringComparison.Ordinal);
        Assert.DoesNotContain("allowedItems", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAncestorWalkIsTheOnlyPlaceThatTouchesTheChain()
    {
        // IsWithinSelectionScope is where parent.Equals and the Parent chain belong. If
        // a second copy of that walk appears, the two can disagree about whether the
        // parent itself is in scope -- which is the distinction between
        // direct_children_of and descendants_of.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NavisHelper.sln")))
            directory = directory.Parent;

        var source = File.ReadAllText(Path.Combine(
            directory!.FullName,
            "NavisHelper", "Agent", "Services", "SearchService.Commands.cs"));

        var walks = 0;
        var index = 0;
        while (true)
        {
            index = source.IndexOf("ancestor = ancestor.Parent", index, StringComparison.Ordinal);
            if (index < 0)
                break;

            walks++;
            index += 1;
        }

        Assert.Equal(1, walks);
    }
}
