using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #444 redesign, slice epsilon3: the panel keeps a list of items to export, filled by "Add
// selected" (owner, 2026-11-15). The list rules are pure so they are pinned here; the panel only
// shows the list. Items are compared like ModelItem (equality), and a list built in one document
// is cleared before it is used with another, so no stale ModelItem reaches the job.
public sealed class FbxExportListTests
{
    private sealed record Item(string Id);

    private static readonly object DocA = new();
    private static readonly object DocB = new();

    [Fact]
    public void AddKeepsOrderDropsRepeatsAndNullsAndCountsWhatWasAdded()
    {
        var list = new FbxExportList<Item>();
        Assert.Equal(2, list.Add(new[] { new Item("b"), new Item("a") }));
        Assert.Equal(1, list.Add(new[] { new Item("a"), null!, new Item("c"), new Item("c") }));

        Assert.Equal(new[] { "b", "a", "c" }, list.Items.Select(item => item.Id));
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void RemoveAndClear()
    {
        var list = new FbxExportList<Item>();
        list.Add(new[] { new Item("a"), new Item("b"), new Item("c") });

        Assert.Equal(1, list.Remove(new[] { new Item("b"), new Item("zz") }));
        Assert.Equal(new[] { "a", "c" }, list.Items.Select(item => item.Id));

        list.Clear();
        Assert.Empty(list.Items);
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void AListFromAnotherDocumentIsClearedBeforeUse()
    {
        var list = new FbxExportList<Item>();
        Assert.False(list.BindTo(DocA));
        list.Add(new[] { new Item("a") });

        Assert.False(list.BindTo(DocA));
        Assert.Single(list.Items);

        Assert.True(list.BindTo(DocB));
        Assert.Empty(list.Items);
        Assert.Same(DocB, list.Owner);

        // An empty list changes owner silently: there was nothing to lose.
        Assert.False(list.BindTo(DocA));
        Assert.Same(DocA, list.Owner);
    }

    [Fact]
    public void ItemsIsASnapshotTheCallerCannotChange()
    {
        var list = new FbxExportList<Item>();
        list.Add(new[] { new Item("a") });
        var snapshot = list.Items;
        list.Add(new[] { new Item("b") });

        Assert.Single(snapshot);
        Assert.False(snapshot is List<Item>);
    }
}
