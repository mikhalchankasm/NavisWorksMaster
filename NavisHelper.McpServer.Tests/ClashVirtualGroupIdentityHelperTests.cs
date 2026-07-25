using NavisHelper.Agent.Contracts;
using System;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashVirtualGroupIdentityHelperTests
{
    [Theory]
    [InlineData(ClashVirtualGroupSide.None, null, "Group")]
    [InlineData(ClashVirtualGroupSide.ItemA, null, "Group A [NH:A]")]
    [InlineData(ClashVirtualGroupSide.ItemB, "  Pumps  ", "Pumps [NH:B]")]
    [InlineData(ClashVirtualGroupSide.ItemA, "Clash-B: Valves [NH:B]", "Valves [NH:A]")]
    public void BuildPersistentName_PreservesFallbackTagsAndLegacyPrefixRemoval(ClashVirtualGroupSide side, string label, string expected)
    {
        Assert.Equal(expected, ClashVirtualGroupIdentityHelper.BuildPersistentName(side, label));
    }

    [Fact]
    public void BuildPersistentName_RemovesControlCharactersBeforeLengthLimit()
    {
        var label = " A\r\nB " + new string('x', 130);

        var result = ClashVirtualGroupIdentityHelper.BuildPersistentName(ClashVirtualGroupSide.ItemA, label);

        Assert.StartsWith("AB ", result, StringComparison.Ordinal);
        Assert.Equal(120, result.Length);
        Assert.DoesNotContain("\r", result, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", result, StringComparison.Ordinal);
        Assert.EndsWith(ClashVirtualGroupIdentityHelper.SideTagA, result, StringComparison.Ordinal);
        Assert.Equal(ClashVirtualGroupSide.ItemA, ClashVirtualGroupIdentityHelper.InferSide(result));
    }

    [Theory]
    [InlineData(112)]
    [InlineData(113)]
    [InlineData(114)]
    [InlineData(115)]
    [InlineData(119)]
    [InlineData(120)]
    public void BuildPersistentName_ReservesSpaceForSideTagAtLengthBoundary(int labelLength)
    {
        var resultA = ClashVirtualGroupIdentityHelper.BuildPersistentName(
            ClashVirtualGroupSide.ItemA,
            new string('a', labelLength));
        var resultB = ClashVirtualGroupIdentityHelper.BuildPersistentName(
            ClashVirtualGroupSide.ItemB,
            new string('b', labelLength));

        Assert.Equal(Math.Min(labelLength, 113) + ClashVirtualGroupIdentityHelper.SideTagA.Length, resultA.Length);
        Assert.Equal(Math.Min(labelLength, 113) + ClashVirtualGroupIdentityHelper.SideTagB.Length, resultB.Length);
        Assert.EndsWith(ClashVirtualGroupIdentityHelper.SideTagA, resultA, StringComparison.Ordinal);
        Assert.EndsWith(ClashVirtualGroupIdentityHelper.SideTagB, resultB, StringComparison.Ordinal);
        Assert.Equal(ClashVirtualGroupSide.ItemA, ClashVirtualGroupIdentityHelper.InferSide(resultA));
        Assert.Equal(ClashVirtualGroupSide.ItemB, ClashVirtualGroupIdentityHelper.InferSide(resultB));
    }

    [Fact]
    public void BuildPersistentName_WithoutSideKeepsFullLengthLimit()
    {
        var result = ClashVirtualGroupIdentityHelper.BuildPersistentName(
            ClashVirtualGroupSide.None,
            new string('x', 121));

        Assert.Equal(120, result.Length);
        Assert.Equal(ClashVirtualGroupSide.None, ClashVirtualGroupIdentityHelper.InferSide(result));
    }

    [Theory]
    [InlineData("Clash-A:")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void BuildPersistentName_UsesSideFallbackWhenNormalizedLabelIsEmpty(string label)
    {
        Assert.Equal(
            "Group A [NH:A]",
            ClashVirtualGroupIdentityHelper.BuildPersistentName(ClashVirtualGroupSide.ItemA, label));
    }

    [Fact]
    public void BuildPersistentName_DoesNotSplitSurrogatePairAtBoundary()
    {
        var label = new string('x', 112) + "😀";

        var result = ClashVirtualGroupIdentityHelper.BuildPersistentName(ClashVirtualGroupSide.ItemA, label);

        Assert.Equal(new string('x', 112) + ClashVirtualGroupIdentityHelper.SideTagA, result);
        Assert.DoesNotContain('\uFFFD', result);
        Assert.Equal(ClashVirtualGroupSide.ItemA, ClashVirtualGroupIdentityHelper.InferSide(result));
    }

    [Fact]
    public void BuildPersistentName_TrimsWhitespaceAtTruncationBoundaryAndKeepsTag()
    {
        var label = new string('x', 110) + "   " + new string('y', 10);

        var result = ClashVirtualGroupIdentityHelper.BuildPersistentName(ClashVirtualGroupSide.ItemB, label);

        Assert.Equal(new string('x', 110) + ClashVirtualGroupIdentityHelper.SideTagB, result);
        Assert.Equal(ClashVirtualGroupSide.ItemB, ClashVirtualGroupIdentityHelper.InferSide(result));
    }

    [Theory]
    [InlineData(null, ClashVirtualGroupSide.None)]
    [InlineData("", ClashVirtualGroupSide.None)]
    [InlineData("Clash-A: Pumps", ClashVirtualGroupSide.ItemA)]
    [InlineData("clash-b old", ClashVirtualGroupSide.ItemB)]
    [InlineData("Pumps [NH:A]", ClashVirtualGroupSide.ItemA)]
    [InlineData("Pumps [nh:b]", ClashVirtualGroupSide.ItemB)]
    [InlineData("Pumps", ClashVirtualGroupSide.None)]
    public void InferSide_PreservesLegacyAliases(string value, ClashVirtualGroupSide expected)
    {
        Assert.Equal(expected, ClashVirtualGroupIdentityHelper.InferSide(value));
    }

    [Fact]
    public void InferSide_PreservesLegacyPrefixPrecedenceOverConflictingTag()
    {
        Assert.Equal(
            ClashVirtualGroupSide.ItemA,
            ClashVirtualGroupIdentityHelper.InferSide("Clash-A old [NH:B]"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("  Pumps  ", "Pumps")]
    [InlineData("Clash-A: Pumps", "Pumps")]
    [InlineData("Clash-B -- Pumps", "Pumps")]
    [InlineData("Pumps [NH:A]", "Pumps")]
    [InlineData("Clash-A: Pumps [NH:B]", "Pumps")]
    [InlineData("Clash-Axis", "xis")]
    public void GetUserName_PreservesBroadLegacyPrefixRules(string value, string expected)
    {
        Assert.Equal(expected, ClashVirtualGroupIdentityHelper.GetUserName(value));
    }

    [Fact]
    public void GetUserName_StripsOnlyTheLastOfMixedTags()
    {
        Assert.Equal("Pumps [NH:A]", ClashVirtualGroupIdentityHelper.GetUserName("Pumps [NH:A] [NH:B]"));
    }

    [Theory]
    [InlineData(ClashVirtualGroupSide.ItemA, "Root/A", "Pumps", ClashVirtualGroupSide.ItemA, "root/a", "Clash-B: pumps [NH:B]", true)]
    [InlineData(ClashVirtualGroupSide.ItemA, null, "Pumps", ClashVirtualGroupSide.ItemA, "", "pumps", true)]
    [InlineData(ClashVirtualGroupSide.ItemA, "A", "Pumps", ClashVirtualGroupSide.ItemB, "A", "Pumps", false)]
    [InlineData(ClashVirtualGroupSide.ItemA, "A", "Pumps", ClashVirtualGroupSide.ItemA, "B", "Pumps", false)]
    [InlineData(ClashVirtualGroupSide.ItemA, "A", "Pumps", ClashVirtualGroupSide.ItemA, "A", "Valves", false)]
    public void AreSame_UsesSidePathAndNormalizedUserLabel(
        ClashVirtualGroupSide leftSide, string leftPath, string leftLabel,
        ClashVirtualGroupSide rightSide, string rightPath, string rightLabel,
        bool expected)
    {
        Assert.Equal(expected, ClashVirtualGroupIdentityHelper.AreSame(leftSide, leftPath, leftLabel, rightSide, rightPath, rightLabel));
    }

    [Fact]
    public void BuildTestCacheKey_GuidTakesPrecedence()
    {
        var guid = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

        Assert.Equal("guid:01234567-89ab-cdef-0123-456789abcdef", ClashVirtualGroupIdentityHelper.BuildTestCacheKey(guid, "Name"));
    }

    [Theory]
    [InlineData(" Test 01 ", "name:Test 01")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void BuildTestCacheKey_UsesTrimmedNameWhenGuidMissing(string displayName, string expected)
    {
        Assert.Equal(expected, ClashVirtualGroupIdentityHelper.BuildTestCacheKey(Guid.Empty, displayName));
    }
}
