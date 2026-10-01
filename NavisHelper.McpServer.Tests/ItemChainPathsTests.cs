using System;
using System.Linq;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// `ItemChainPaths.Build` replaces the per-node `Parent` climb the host's
/// `BuildItemPath` repeated for every node of a `selected_items_tree` chain.
/// These pin that element i is names[0..i] joined with `" / "` -- the format
/// `BuildItemPath` prints and `FindItemsPathSegments` parses back -- against the
/// naive climb-and-join it replaces, including the null name that `string.Join`
/// renders as an empty segment. The host side, which feeds real `DisplayName`
/// and `ClassDisplayName` values and caches boxes and source files by those
/// paths, is verified on the rig.
/// </summary>
public sealed class ItemChainPathsTests
{
    [Fact]
    public void Build_ReturnsTheOnlyNameForAChainOfOne()
    {
        Assert.Equal(new[] { "6501.5.nwd" }, ItemChainPaths.Build(new[] { "6501.5.nwd" }));
    }

    [Fact]
    public void Build_PrefixesEveryNodeWithItsAncestors()
    {
        var paths = ItemChainPaths.Build(new[] { "6501.5.nwd", "Level 2", "Basic Wall" });

        Assert.Equal(
            new[]
            {
                "6501.5.nwd",
                "6501.5.nwd / Level 2",
                "6501.5.nwd / Level 2 / Basic Wall",
            },
            paths);
    }

    [Fact]
    public void Build_ReturnsNoPathForAnEmptyChain()
    {
        Assert.Empty(ItemChainPaths.Build(new string[0]));
    }

    [Fact]
    public void Build_RefusesNoChainAtAll()
    {
        Assert.Throws<ArgumentNullException>(() => ItemChainPaths.Build(null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Build_KeepsANamelessNodeAsAnEmptySegment(string name)
    {
        var paths = ItemChainPaths.Build(new[] { "Root", name, "Leaf" });

        Assert.Equal(new[] { "Root", "Root / ", "Root /  / Leaf" }, paths);
    }

    [Fact]
    public void Build_MatchesJoiningEveryPrefixTheWayTheHostClimbDid()
    {
        var names = new[] { "Model", "System", "Supply / Return", null, "Leaf" };

        var paths = ItemChainPaths.Build(names);

        Assert.Equal(names.Length, paths.Count);
        for (var depth = 0; depth < names.Length; depth++)
            Assert.Equal(string.Join(ItemChainPaths.Separator, names.Take(depth + 1)), paths[depth]);
    }

    [Fact]
    public void Build_KeepsASeparatorInsideANameIntact()
    {
        var paths = ItemChainPaths.Build(new[] { "Supply / Return", "Child" });

        Assert.Equal(new[] { "Supply / Return", "Supply / Return / Child" }, paths);
    }
}
