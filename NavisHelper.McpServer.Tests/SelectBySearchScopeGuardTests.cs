using System;
using System.IO;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// Source guard keeping subtree enumeration out of SelectBySearch itself.
/// Parent-scoped traversal belongs in CollectParentScopeMatches; runtime behaviour
/// requires a live Navisworks tree and is verified at L3.
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

}
