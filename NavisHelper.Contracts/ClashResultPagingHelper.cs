using System;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashResultPageInfo
    {
        public int Offset { get; set; }
        public int Limit { get; set; }
        public int TotalCount { get; set; }
        public int ReturnedCount { get; set; }
        public int NextOffset { get; set; }
        public bool HasMore { get; set; }
        public bool IsOffsetBeyondTotal { get; set; }
    }

    public static class ClashResultPagingHelper
    {
        public static ClashResultPageInfo CreateLimitPage(int offset, int limit, int totalCount)
        {
            offset = Math.Max(0, offset);
            limit = Math.Max(0, limit);
            totalCount = Math.Max(0, totalCount);

            var returnedCount = offset >= totalCount ? 0 : Math.Min(limit, totalCount - offset);
            return new ClashResultPageInfo
            {
                Offset = offset,
                Limit = limit,
                TotalCount = totalCount,
                ReturnedCount = returnedCount,
                NextOffset = Math.Min(offset + limit, totalCount),
                HasMore = offset + limit < totalCount,
                IsOffsetBeyondTotal = offset > totalCount,
            };
        }

        public static ClashResultPageInfo CreateReturnedPage(int offset, int limit, int totalCount, int returnedCount, bool forceHasMore)
        {
            offset = Math.Max(0, offset);
            limit = Math.Max(0, limit);
            totalCount = Math.Max(0, totalCount);
            returnedCount = Math.Max(0, Math.Min(returnedCount, limit));

            var nextOffset = offset + returnedCount;
            return new ClashResultPageInfo
            {
                Offset = offset,
                Limit = limit,
                TotalCount = totalCount,
                ReturnedCount = returnedCount,
                NextOffset = nextOffset,
                HasMore = forceHasMore || nextOffset < totalCount,
                IsOffsetBeyondTotal = offset > totalCount,
            };
        }
    }
}
