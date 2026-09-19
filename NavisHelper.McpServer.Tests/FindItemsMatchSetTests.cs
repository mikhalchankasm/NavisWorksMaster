using System.Collections.Generic;
using System.Linq;
using NavisHelper.Agent.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// Regression cover for the native find_items accumulator.
///
/// The native path used to key matches by the display-name path built from
/// ModelItem.DisplayName up the ancestor chain. Navisworks models contain
/// genuinely distinct siblings that share a display name, so that key collapsed
/// real matches: on 6501.5.nwd, Item/Name contains
/// "of BRANCH /Copy-of-15.15-SA02-2179-S3C1-N" returned 70 items from the native
/// whole-model path and 115 from the manual scoped traversal, and
/// list_item_children confirmed the duplicates were different nodes.
///
/// FakeItem below stands in for ModelItem: equal Path, distinct identity.
/// </summary>
public sealed class FindItemsMatchSetTests
{
    private sealed class FakeItem
    {
        public FakeItem(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public override string ToString() => Path;
    }

    private sealed class PathComparer : IEqualityComparer<FakeItem>
    {
        public bool Equals(FakeItem x, FakeItem y) => x?.Path == y?.Path;

        public int GetHashCode(FakeItem obj) => obj?.Path?.GetHashCode() ?? 0;
    }

    [Fact]
    public void Add_KeepsDistinctSiblingsThatShareADisplayPath()
    {
        var first = new FakeItem("model / branch / GASKET 1 of BRANCH");
        var second = new FakeItem("model / branch / GASKET 1 of BRANCH");

        var set = new FindItemsMatchSet<FakeItem>();
        Assert.True(set.Add(first));
        Assert.True(set.Add(second));

        Assert.Equal(2, set.Count);
        Assert.Equal(new[] { first, second }, set.ToList());
    }

    [Fact]
    public void Add_WithPathComparer_WouldCollapseThem_DocumentingTheOldBug()
    {
        var set = new FindItemsMatchSet<FakeItem>(new PathComparer());
        set.Add(new FakeItem("model / branch / GASKET 1 of BRANCH"));
        set.Add(new FakeItem("model / branch / GASKET 1 of BRANCH"));

        Assert.Equal(1, set.Count);
    }

    [Fact]
    public void Add_DedupsTheSameItemSeenTwice()
    {
        var item = new FakeItem("model / node");

        var set = new FindItemsMatchSet<FakeItem>();
        Assert.True(set.Add(item));
        Assert.False(set.Add(item));
        Assert.False(set.Add(null));

        Assert.Equal(1, set.Count);
    }

    [Fact]
    public void AddRange_PreservesInsertionOrder()
    {
        var items = new[] { new FakeItem("c"), new FakeItem("a"), new FakeItem("b") };

        var set = new FindItemsMatchSet<FakeItem>();
        set.AddRange(items);
        set.AddRange(null);

        Assert.Equal(items, set.ToList());
    }

    [Fact]
    public void UnionWith_KeepsBothSidesAndSkipsRepeats()
    {
        var shared = new FakeItem("shared");
        var left = new FindItemsMatchSet<FakeItem>();
        var right = new FindItemsMatchSet<FakeItem>();
        var onlyRight = new FakeItem("right");

        left.Add(shared);
        right.Add(shared);
        right.Add(onlyRight);

        left.UnionWith(right);
        left.UnionWith(null);

        Assert.Equal(new[] { shared, onlyRight }, left.ToList());
    }

    [Fact]
    public void IntersectWith_KeepsOnlySharedIdentities_EvenWhenPathsMatch()
    {
        var shared = new FakeItem("model / node");
        var twinByPath = new FakeItem("model / node");

        var left = new FindItemsMatchSet<FakeItem>();
        left.Add(shared);
        left.Add(twinByPath);

        var right = new FindItemsMatchSet<FakeItem>();
        right.Add(shared);

        left.IntersectWith(right);

        Assert.Equal(new[] { shared }, left.ToList());
    }

    [Fact]
    public void IntersectWith_NullClearsTheSet()
    {
        var set = new FindItemsMatchSet<FakeItem>();
        set.Add(new FakeItem("a"));

        set.IntersectWith(null);

        Assert.Equal(0, set.Count);
    }

    [Fact]
    public void RemoveWhere_DropsMatchesAndFreesThemForReAdd()
    {
        var keep = new FakeItem("keep");
        var drop = new FakeItem("drop");

        var set = new FindItemsMatchSet<FakeItem>();
        set.AddRange(new[] { keep, drop });
        set.RemoveWhere(item => item.Path == "drop");
        set.RemoveWhere(null);

        Assert.Equal(new[] { keep }, set.ToList());
        Assert.False(set.Contains(drop));
        Assert.True(set.Add(drop));
    }

    [Fact]
    public void Sort_OrdersWithoutLosingDuplicatePaths()
    {
        var firstTwin = new FakeItem("b");
        var secondTwin = new FakeItem("b");
        var set = new FindItemsMatchSet<FakeItem>();
        set.AddRange(new[] { new FakeItem("c"), firstTwin, secondTwin, new FakeItem("a") });

        set.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));

        Assert.Equal(new[] { "a", "b", "b", "c" }, set.Select(item => item.Path).ToArray());
    }
}
