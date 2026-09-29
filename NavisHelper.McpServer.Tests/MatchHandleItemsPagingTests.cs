using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// The paging planner is the whole contract of <c>match_handle_items</c> that can be
/// decided without Navisworks: which slice of a handle's item list a call gets, and
/// what the caller has to pass to get the next one.
/// </summary>
public sealed class MatchHandleItemsPagingTests
{
    [Fact]
    public void Plan_WithoutOffsetOrLimit_StartsAtTheFirstItemWithTheDocumentedPage()
    {
        var page = MatchHandleItemsPaging.Plan(null, null, 1200);

        Assert.Equal(1200, page.TotalItemCount);
        Assert.Equal(0, page.Offset);
        Assert.Equal(500, page.Limit);
        Assert.Equal(500, page.ReturnedItemCount);
        Assert.Equal(500, page.NextOffset);
        Assert.True(page.HasMore);
    }

    [Fact]
    public void TheDocumentedBoundsAreTheOnesThePlannerUses()
    {
        Assert.Equal(0, MatchHandleItemsPaging.DefaultOffset);
        Assert.Equal(500, MatchHandleItemsPaging.DefaultLimit);
        Assert.Equal(1, MatchHandleItemsPaging.MinLimit);
        Assert.Equal(5000, MatchHandleItemsPaging.MaxLimit);
    }

    [Theory]
    [InlineData(null, 500)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(500, 500)]
    [InlineData(5000, 5000)]
    [InlineData(5001, 5000)]
    [InlineData(int.MaxValue, 5000)]
    public void ClampLimit_KeepsTheRequestedPageInsideOneToFiveThousand(int? requested, int expected)
    {
        Assert.Equal(expected, MatchHandleItemsPaging.ClampLimit(requested));
        Assert.Equal(expected, MatchHandleItemsPaging.Plan(0, requested, 10000).Limit);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(-1000, 0)]
    [InlineData(499, 499)]
    public void ClampOffset_NeverStartsBeforeTheFirstItem(int? requested, int expected)
    {
        Assert.Equal(expected, MatchHandleItemsPaging.ClampOffset(requested));
        Assert.Equal(expected, MatchHandleItemsPaging.Plan(requested, 10, 1000).Offset);
    }

    [Fact]
    public void Plan_APartialLastPage_EndsAtTheTotalAndStops()
    {
        var page = MatchHandleItemsPaging.Plan(1000, 500, 1200);

        Assert.Equal(200, page.ReturnedItemCount);
        Assert.Equal(1200, page.NextOffset);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void Plan_APageEndingExactlyOnTheTotal_HasNothingMore()
    {
        var page = MatchHandleItemsPaging.Plan(500, 500, 1000);

        Assert.Equal(500, page.ReturnedItemCount);
        Assert.Equal(1000, page.NextOffset);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void Plan_AnOffsetPastTheEnd_IsAnEmptyPageRatherThanAnError()
    {
        var page = MatchHandleItemsPaging.Plan(5000, 500, 1200);

        Assert.Equal(5000, page.Offset);
        Assert.Equal(0, page.ReturnedItemCount);
        Assert.Equal(5000, page.NextOffset);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void Plan_AnOffsetAtTheEnd_IsAnEmptyPageToo()
    {
        var page = MatchHandleItemsPaging.Plan(1200, 500, 1200);

        Assert.Equal(0, page.ReturnedItemCount);
        Assert.Equal(1200, page.NextOffset);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void Plan_AHandleWithNoItems_IsAnEmptyFirstPage()
    {
        var page = MatchHandleItemsPaging.Plan(null, null, 0);

        Assert.Equal(0, page.TotalItemCount);
        Assert.Equal(0, page.ReturnedItemCount);
        Assert.Equal(0, page.NextOffset);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void Plan_ANearMaxValueOffset_DoesNotOverflowNextOffset()
    {
        var page = MatchHandleItemsPaging.Plan(int.MaxValue, 500, 1200);

        Assert.Equal(int.MaxValue, page.Offset);
        Assert.Equal(0, page.ReturnedItemCount);
        Assert.Equal(int.MaxValue, page.NextOffset);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void FollowingNextOffset_CoversEveryItemExactlyOnce()
    {
        const int total = 1234;
        var page = MatchHandleItemsPaging.Plan(null, 500, total);
        var covered = 0;
        var pages = 0;

        while (true)
        {
            Assert.Equal(covered, page.Offset);
            covered += page.ReturnedItemCount;
            Assert.Equal(covered, page.NextOffset);
            pages++;
            if (!page.HasMore)
                break;

            page = MatchHandleItemsPaging.Plan(page.NextOffset, page.Limit, total);
        }

        Assert.Equal(total, covered);
        Assert.Equal(3, pages);
    }

    [Fact]
    public void ASingleItemPage_WalksTheHandleOneItemAtATime()
    {
        const int total = 4;
        var offsets = new List<int>();

        for (var offset = 0; offset < total; offset++)
        {
            var page = MatchHandleItemsPaging.Plan(offset, 1, total);
            Assert.Equal(1, page.ReturnedItemCount);
            Assert.Equal(offset + 1, page.NextOffset);
            Assert.Equal(offset + 1 < total, page.HasMore);
            offsets.Add(page.Offset);
        }

        Assert.Equal(new[] { 0, 1, 2, 3 }, offsets);
    }
}  
