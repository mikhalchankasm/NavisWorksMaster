using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashVirtualGroupCachePolicyTests
{
    [Fact]
    public void ShouldStore_RequiresAtLeastOneNonNullResult()
    {
        Assert.False(ClashVirtualGroupCachePolicy.ShouldStore<object>(null));
        Assert.False(ClashVirtualGroupCachePolicy.ShouldStore(System.Array.Empty<object>()));
        Assert.False(ClashVirtualGroupCachePolicy.ShouldStore(new object[] { null, null }));
        Assert.True(ClashVirtualGroupCachePolicy.ShouldStore(new[] { null, new object() }));
    }

    [Fact]
    public void CreateResultSnapshot_FiltersNullAndPreservesFirstDefaultDistinctValue()
    {
        var first = new EqualByValue(1);
        var equalButDifferent = new EqualByValue(1);
        var second = new EqualByValue(2);

        var result = ClashVirtualGroupCachePolicy.CreateResultSnapshot(
            new[] { null, first, equalButDifferent, second, first });

        Assert.Equal(2, result.Count);
        Assert.Same(first, result[0]);
        Assert.Same(second, result[1]);
    }

    [Fact]
    public void CreateResultSnapshot_NullInputReturnsEmptyList()
    {
        Assert.Empty(ClashVirtualGroupCachePolicy.CreateResultSnapshot<object>(null));
    }

    [Fact]
    public void MatchesRestoreDuplicate_MatchesFullIdentity()
    {
        Assert.True(Matches(
            ClashVirtualGroupSide.ItemA, "Root/A", "Pumps", new object(),
            ClashVirtualGroupSide.ItemA, "root/a", "Clash-B: pumps [NH:B]", new object()));
    }

    [Fact]
    public void MatchesRestoreDuplicate_MatchesPersistentReference()
    {
        var persistentGroup = new object();

        Assert.True(Matches(
            ClashVirtualGroupSide.ItemA, "A", "Pumps", persistentGroup,
            ClashVirtualGroupSide.ItemB, "B", "Valves", persistentGroup));
    }

    [Fact]
    public void MatchesRestoreDuplicate_PreservesNullPersistentReferenceParity()
    {
        Assert.True(Matches(
            ClashVirtualGroupSide.ItemA, "A", "Pumps", null,
            ClashVirtualGroupSide.ItemB, "B", "Valves", null));
        Assert.False(Matches(
            ClashVirtualGroupSide.ItemA, "A", "Pumps", null,
            ClashVirtualGroupSide.ItemB, "B", "Valves", new object()));
    }

    [Fact]
    public void MatchesRestoreDuplicate_MatchesSideAndCleanLabelWithoutPath()
    {
        Assert.True(Matches(
            ClashVirtualGroupSide.ItemB, "Root/One", "Clash-B: Valves", new object(),
            ClashVirtualGroupSide.ItemB, "Root/Two", "valves [NH:B]", new object()));
    }

    [Fact]
    public void MatchesRestoreDuplicate_TreatsNullLabelsAsTheSameCleanLabelForSameSide()
    {
        Assert.True(Matches(
            ClashVirtualGroupSide.ItemA, "Root/One", null, new object(),
            ClashVirtualGroupSide.ItemA, "Root/Two", null, new object()));
    }

    [Theory]
    [InlineData(ClashVirtualGroupSide.ItemA, "A", "Pumps", ClashVirtualGroupSide.ItemB, "A", "Pumps")]
    [InlineData(ClashVirtualGroupSide.ItemA, "A", "Pumps", ClashVirtualGroupSide.ItemA, "A", "Valves")]
    public void MatchesRestoreDuplicate_RejectsDifferentGroups(
        ClashVirtualGroupSide existingSide,
        string existingPath,
        string existingLabel,
        ClashVirtualGroupSide cachedSide,
        string cachedPath,
        string cachedLabel)
    {
        Assert.False(Matches(
            existingSide, existingPath, existingLabel, new object(),
            cachedSide, cachedPath, cachedLabel, new object()));
    }

    private static bool Matches(
        ClashVirtualGroupSide existingSide,
        string existingPath,
        string existingLabel,
        object existingPersistentGroup,
        ClashVirtualGroupSide cachedSide,
        string cachedPath,
        string cachedLabel,
        object resolvedPersistentGroup)
    {
        return ClashVirtualGroupCachePolicy.MatchesRestoreDuplicate(
            existingSide,
            existingPath,
            existingLabel,
            existingPersistentGroup,
            cachedSide,
            cachedPath,
            cachedLabel,
            resolvedPersistentGroup);
    }

    private sealed class EqualByValue
    {
        private readonly int _value;

        public EqualByValue(int value)
        {
            _value = value;
        }

        public override bool Equals(object obj)
        {
            return obj is EqualByValue other && other._value == _value;
        }

        public override int GetHashCode()
        {
            return _value;
        }
    }
}
