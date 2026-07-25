using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClashResultPagingHelperTests
{
    [Fact]
    public void CreateLimitPage_ComputesNextOffsetFromLimit()
    {
        var page = ClashResultPagingHelper.CreateLimitPage(offset: 10, limit: 25, totalCount: 100);

        Assert.Equal(10, page.Offset);
        Assert.Equal(25, page.ReturnedCount);
        Assert.Equal(35, page.NextOffset);
        Assert.True(page.HasMore);
        Assert.False(page.IsOffsetBeyondTotal);
    }

    [Fact]
    public void CreateLimitPage_ClampsNextOffsetToTotal()
    {
        var page = ClashResultPagingHelper.CreateLimitPage(offset: 90, limit: 25, totalCount: 100);

        Assert.Equal(10, page.ReturnedCount);
        Assert.Equal(100, page.NextOffset);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void CreateLimitPage_OffsetBeyondTotal_ReturnsEmptyPageWithTotalAsNextOffset()
    {
        var page = ClashResultPagingHelper.CreateLimitPage(offset: 120, limit: 25, totalCount: 100);

        Assert.Equal(0, page.ReturnedCount);
        Assert.Equal(100, page.NextOffset);
        Assert.False(page.HasMore);
        Assert.True(page.IsOffsetBeyondTotal);
    }

    [Fact]
    public void CreateReturnedPage_PreservesReportStyleNextOffsetWhenOffsetBeyondTotal()
    {
        var page = ClashResultPagingHelper.CreateReturnedPage(offset: 120, limit: 25, totalCount: 100, returnedCount: 0, forceHasMore: false);

        Assert.Equal(0, page.ReturnedCount);
        Assert.Equal(120, page.NextOffset);
        Assert.False(page.HasMore);
        Assert.True(page.IsOffsetBeyondTotal);
    }

    [Fact]
    public void CreateReturnedPage_CanForceHasMoreForCancelledBatch()
    {
        var page = ClashResultPagingHelper.CreateReturnedPage(offset: 10, limit: 25, totalCount: 20, returnedCount: 10, forceHasMore: true);

        Assert.Equal(20, page.NextOffset);
        Assert.True(page.HasMore);
    }
}
