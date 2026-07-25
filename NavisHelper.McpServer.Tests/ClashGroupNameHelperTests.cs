using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashGroupNameHelperTests
{
    [Theory]
    [InlineData("Pump Station [NH:A]", " [NH:A]")]
    [InlineData("Pump Station [nh:b]   ", " [NH:B]")]
    [InlineData("Pump Station", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ExtractSideTag_ReturnsCanonicalSideTag(string value, string expected)
    {
        Assert.Equal(expected, ClashGroupNameHelper.ExtractSideTag(value));
    }

    [Theory]
    [InlineData("Pump Station [NH:A]", "Pump Station")]
    [InlineData("  Pump Station [NH:B]   ", "Pump Station")]
    [InlineData("Pump Station", "Pump Station")]
    [InlineData(null, "")]
    public void RemoveSideTag_RemovesOnlyNavisHelperSuffix(string value, string expected)
    {
        Assert.Equal(expected, ClashGroupNameHelper.RemoveSideTag(value));
    }

    [Theory]
    [InlineData("0001 - Pump Station", "Pump Station")]
    [InlineData("12. Pump Station", "Pump Station")]
    [InlineData("[003] Pump Station", "Pump Station")]
    [InlineData("(004): Pump Station", "Pump Station")]
    [InlineData("NHX-0001 Pump Station", "NHX-0001 Pump Station")]
    [InlineData("Pump Station", "Pump Station")]
    public void StripLeadingNumber_RemovesOnlyLeadingRenumberPrefix(string value, string expected)
    {
        Assert.Equal(expected, ClashGroupNameHelper.StripLeadingNumber(value));
    }

    [Theory]
    [InlineData("Pump   Station [NH:A]", "pump station|[nh:a]")]
    [InlineData("0001 - Pump Station [NH:A]", "pump station|[nh:a]")]
    [InlineData("0002 - pump station [nh:a]", "pump station|[nh:a]")]
    [InlineData("0001 - Pump Station [NH:B]", "pump station|[nh:b]")]
    [InlineData("", "")]
    public void BuildMatchKey_NormalizesWhitespaceNumberingAndSideTag(string value, string expected)
    {
        Assert.Equal(expected, ClashGroupNameHelper.BuildMatchKey(value));
    }

    [Fact]
    public void FindMatchingGroupName_PrefersExactNameBeforeLogicalRenumberMatch()
    {
        var match = ClashGroupNameHelper.FindMatchingGroupName(
            new[] { "0001 - Pump Station [NH:B]", "Pump Station [NH:B]" },
            "Pump Station [NH:B]");

        Assert.Equal("Pump Station [NH:B]", match);
    }

    [Fact]
    public void FindMatchingGroupName_FindsLogicalMatchAfterRenumbering()
    {
        var match = ClashGroupNameHelper.FindMatchingGroupName(
            new[] { "0001 - Pump Station [NH:B]" },
            "Pump Station [NH:B]");

        Assert.Equal("0001 - Pump Station [NH:B]", match);
    }

    [Fact]
    public void FindMatchingGroupName_DoesNotMatchDifferentSideTag()
    {
        var match = ClashGroupNameHelper.FindMatchingGroupName(
            new[] { "0001 - Pump Station [NH:A]" },
            "Pump Station [NH:B]");

        Assert.Null(match);
    }

    [Theory]
    [InlineData("NHX-Pump Station [NH:B]", "NHX-", true)]
    [InlineData("0001 - NHX-Pump Station [NH:B]", "NHX-", true)]
    [InlineData("0001 - Other Pump Station [NH:B]", "NHX-", false)]
    [InlineData("Pump Station [NH:B]", "", true)]
    [InlineData("", "NHX-", false)]
    public void MatchesPrefix_IgnoresLeadingNumberAndSideTag(string groupName, string prefix, bool expected)
    {
        Assert.Equal(expected, ClashGroupNameHelper.MatchesPrefix(groupName, prefix));
    }

    [Theory]
    [InlineData("owner_name", "Pump A", "Root / Pump A", "pump a")]
    [InlineData("owner_path", "Pump A", "Root / Pump A", "root / pump a")]
    [InlineData("owner_name", "", "Root / Pump A", "root / pump a")]
    [InlineData("owner_name", null, null, "")]
    public void BuildGroupKey_SelectsSourceAndNormalizesWhitespace(string mode, string ownerName, string ownerPath, string expected)
    {
        Assert.Equal(expected, ClashGroupNameHelper.BuildGroupKey(mode, ownerName, ownerPath));
    }

    [Fact]
    public void BuildCleanName_UsesOwnerPathFallbackAndSanitizedPrefix()
    {
        var result = ClashGroupNameHelper.BuildCleanName(
            ClashGroupNameHelper.GroupNameModeOwnerName,
            " NH:Zone* ",
            "",
            "Root / Pump A");

        Assert.Equal("NH_Zone_ Root _ Pump A", result);
    }

    [Fact]
    public void BuildCleanName_UsesGroupFallbackWhenOwnerSourcesAreEmpty()
    {
        var result = ClashGroupNameHelper.BuildCleanName(
            ClashGroupNameHelper.GroupNameModeOwnerPath,
            "",
            "",
            "");

        Assert.Equal("Group", result);
    }

    [Theory]
    [InlineData("A", true, "Pump [NH:A]")]
    [InlineData("B", true, "Pump [NH:B]")]
    [InlineData("b", true, "Pump [NH:B]")]
    [InlineData("A", false, "Pump")]
    public void BuildGroupName_AppendsCanonicalSideTagOnlyWhenRequested(string side, bool includeTag, string expected)
    {
        Assert.Equal(expected, ClashGroupNameHelper.BuildGroupName(side, "Pump", includeTag));
    }

    [Theory]
    [InlineData("NHX-Pump Station [NH:B]", "B", "NHX-", true)]
    [InlineData("0001 - NHX-Pump Station [NH:B]", "B", "NHX-", true)]
    [InlineData("NHX-Pump Station [NH:A]", "B", "NHX-", false)]
    [InlineData("Other Pump Station [NH:B]", "B", "NHX-", false)]
    [InlineData("Pump Station", "B", "", false)]
    public void ShouldRemoveExistingGroup_RequiresSideTagAndPrefixMatch(string groupName, string side, string prefix, bool expected)
    {
        Assert.Equal(expected, ClashGroupNameHelper.ShouldRemoveExistingGroup(groupName, side, prefix));
    }

    [Fact]
    public void ShouldRemoveEmptyGroup_KeepsPlannedLogicalGroupsAfterRenumbering()
    {
        var planned = ClashGroupNameHelper.BuildMatchKeySet(new[] { "Pump Station [NH:B]" });

        Assert.False(ClashGroupNameHelper.ShouldRemoveEmptyGroup("0001 - Pump Station [NH:B]", "B", planned));
    }

    [Fact]
    public void ShouldRemoveEmptyGroup_RemovesUnplannedTaggedSideGroup()
    {
        var planned = ClashGroupNameHelper.BuildMatchKeySet(new[] { "Pump Station [NH:B]" });

        Assert.True(ClashGroupNameHelper.ShouldRemoveEmptyGroup("Old Group [NH:B]", "B", planned));
    }

    [Fact]
    public void ShouldRemoveEmptyGroup_DoesNotRemoveDifferentSideTag()
    {
        var planned = ClashGroupNameHelper.BuildMatchKeySet(new[] { "Pump Station [NH:B]" });

        Assert.False(ClashGroupNameHelper.ShouldRemoveEmptyGroup("Old Group [NH:A]", "B", planned));
    }
}
