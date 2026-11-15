using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// Curator-owned acceptance for #460: a leading number the caller supplied is part of the group name.
// Only NavisHelper's own renumber prefix ("NNNN - ") is ignored, and only on the existing name.
public sealed class ClashGroupNameHelperLeadingNumberAcceptanceTests
{
    [Fact]
    public void OwnersThatDifferOnlyInTheirLeadingNumberAreDistinctGroups()
    {
        Assert.Null(ClashGroupNameHelper.FindMatchingGroupName(
            new[] { "12470-14-1400 [NH:B]" }, "12471-14-1400 [NH:B]"));
    }

    [Fact]
    public void NumberedUserNamesAreDistinctGroups()
    {
        Assert.Null(ClashGroupNameHelper.FindMatchingGroupName(new[] { "01 Pipe [NH:A]" }, "02 Pipe [NH:A]"));
        Assert.Null(ClashGroupNameHelper.FindMatchingGroupName(new[] { "01 Zone" }, "02 Zone"));
    }

    [Fact]
    public void ARenumberedExistingGroupStillMatchesItsCleanName()
    {
        Assert.Equal("0003 - Pipes [NH:A]", ClashGroupNameHelper.FindMatchingGroupName(
            new[] { "0003 - Pipes [NH:A]" }, "Pipes [NH:A]"));
    }

    [Fact]
    public void TheRenumberPrefixIsIgnoredOnlyOnTheExistingName()
    {
        Assert.Null(ClashGroupNameHelper.FindMatchingGroupName(new[] { "Pipes [NH:A]" }, "0003 - Pipes [NH:A]"));
    }

    [Fact]
    public void AnEmptyGroupOfAnotherOwnerIsNotKeptAsPlanned()
    {
        var planned = ClashGroupNameHelper.BuildMatchKeySet(new[] { "12471-14-1400 [NH:B]" });

        Assert.True(ClashGroupNameHelper.ShouldRemoveEmptyGroup("12470-14-1400 [NH:B]", "B", planned));
        Assert.False(ClashGroupNameHelper.ShouldRemoveEmptyGroup("12471-14-1400 [NH:B]", "B", planned));
    }

    [Fact]
    public void RenumberingKeepsANumericOwnerName()
    {
        Assert.Equal("0001 - 12470-14-1400 [NH:B]", ClashRenumberNameHelper.BuildName(
            "12470-14-1400 [NH:B]", "0001", "", "", " - ", preserveExistingName: true, stripExistingNumber: true));
    }

    [Fact]
    public void RenumberingReplacesItsOwnPreviousNumber()
    {
        Assert.Equal("0001 - Pump Station [NH:B]", ClashRenumberNameHelper.BuildName(
            "0007 - Pump Station [NH:B]", "0001", "", "", " - ", preserveExistingName: true, stripExistingNumber: true));
    }
}
