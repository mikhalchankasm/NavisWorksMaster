using System;
using System.Collections.Generic;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class KeyedSortHelperTests
{
    private const int ItemCount = 1000;
    private const int DistinctKeys = 20;

    private sealed class KeyedItem
    {
        public KeyedItem(string key)
        {
            Key = key;
        }

        public string Key { get; }
    }

    private static List<KeyedItem> CreateItems(int seed, int count, int distinctKeys)
    {
        var random = new Random(seed);
        var items = new List<KeyedItem>(count);
        for (var i = 0; i < count; i++)
            items.Add(new KeyedItem($"key-{random.Next(distinctKeys):D3}"));
        return items;
    }

    private static List<KeyedItem> SortAsBefore(List<KeyedItem> items, IComparer<string> comparer)
    {
        var cache = new Dictionary<KeyedItem, string>();

        string CachedKey(KeyedItem item)
        {
            if (!cache.TryGetValue(item, out var key))
            {
                key = item.Key;
                cache[item] = key;
            }

            return key;
        }

        var copy = new List<KeyedItem>(items);
        copy.Sort((left, right) => comparer.Compare(CachedKey(left), CachedKey(right)));
        return copy;
    }

    private static void AssertSameOrder(List<KeyedItem> expected, List<KeyedItem> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
            Assert.Same(expected[i], actual[i]);
    }

    [Fact]
    public void SortByKey_ManyTies_MatchesSortWithKeysCachedPerItem()
    {
        foreach (var seed in new[] { 1, 7, 42, 137, 2026 })
        {
            var items = CreateItems(seed, ItemCount, DistinctKeys);
            var expected = SortAsBefore(items, StringComparer.OrdinalIgnoreCase);

            KeyedSortHelper.SortByKey(items, item => item.Key, StringComparer.OrdinalIgnoreCase);

            AssertSameOrder(expected, items);
        }
    }

    [Fact]
    public void SortByKey_BuildsEachKeyExactlyOnce()
    {
        var items = CreateItems(99, 250, 5);
        var calls = 0;

        KeyedSortHelper.SortByKey(
            items,
            item =>
            {
                calls++;
                return item.Key;
            },
            StringComparer.OrdinalIgnoreCase);

        Assert.Equal(items.Count, calls);
    }

    [Fact]
    public void SortByKey_EmptyList_ReturnsUnchanged()
    {
        var items = new List<KeyedItem>();

        KeyedSortHelper.SortByKey(items, item => item.Key, StringComparer.OrdinalIgnoreCase);

        Assert.Empty(items);
    }

    [Fact]
    public void SortByKey_SingleItem_ReturnsSameItem()
    {
        var only = new KeyedItem("key");
        var items = new List<KeyedItem> { only };

        KeyedSortHelper.SortByKey(items, item => item.Key, StringComparer.OrdinalIgnoreCase);

        var item = Assert.Single(items);
        Assert.Same(only, item);
    }

    [Fact]
    public void SortByKey_HonoursComparer_CaseInsensitiveKeysAreTies()
    {
        var ordinal = new List<KeyedItem>
        {
            new KeyedItem("b"),
            new KeyedItem("A"),
            new KeyedItem("a"),
            new KeyedItem("B"),
        };

        KeyedSortHelper.SortByKey(ordinal, item => item.Key, StringComparer.Ordinal);

        var ordinalKeys = new string[ordinal.Count];
        for (var i = 0; i < ordinal.Count; i++)
            ordinalKeys[i] = ordinal[i].Key;
        Assert.Equal(new[] { "A", "B", "a", "b" }, ordinalKeys);

        var tied = new List<KeyedItem>
        {
            new KeyedItem("b"),
            new KeyedItem("A"),
            new KeyedItem("a"),
            new KeyedItem("B"),
        };
        var expectedTies = SortAsBefore(tied, StringComparer.OrdinalIgnoreCase);

        KeyedSortHelper.SortByKey(tied, item => item.Key, StringComparer.OrdinalIgnoreCase);

        AssertSameOrder(expectedTies, tied);
    }
}
