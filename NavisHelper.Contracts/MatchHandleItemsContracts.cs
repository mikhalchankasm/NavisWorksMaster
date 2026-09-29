using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class MatchHandleItemsRequest
    {
        public string MatchHandle { get; set; }
        public int? Offset { get; set; }
        public int? Limit { get; set; }
        public bool? IncludePaths { get; set; }
        public bool? IncludeSourceFiles { get; set; }
    }

    public sealed class MatchHandleItemsResponse
    {
        public string MatchHandle { get; set; }
        public int TotalItemCount { get; set; }
        public int Offset { get; set; }
        public int Limit { get; set; }
        public int ReturnedItemCount { get; set; }
        public int NextOffset { get; set; }
        public bool HasMore { get; set; }
        public List<MatchHandleItemInfo> Items { get; set; } = new List<MatchHandleItemInfo>();
    }

    public sealed class MatchHandleItemInfo
    {
        /// <summary>
        /// The item's position in the handle's item list, so a page reads
        /// <c>items[i].index == offset + i</c> and two pages cannot be spliced
        /// in the wrong order by mistake.
        /// </summary>
        public int Index { get; set; }
        public string DisplayName { get; set; }
        public string ClassDisplayName { get; set; }
        public string Path { get; set; }
        public string SourceFile { get; set; }
    }

    public sealed class MatchHandleItemsPage
    {
        public int TotalItemCount { get; set; }
        public int Offset { get; set; }
        public int Limit { get; set; }
        public int ReturnedItemCount { get; set; }
        public int NextOffset { get; set; }
        public bool HasMore { get; set; }
    }

    /// <summary>
    /// Plans one page of a match handle's item list.
    ///
    /// A handle can hold every match of a whole-model search while the preview
    /// beside it stops at 20 rows. Paging that list is arithmetic on a count the
    /// host already has, so it is decided here rather than in the host: the
    /// caller's <c>offset</c> and <c>limit</c> are clamped instead of rejected,
    /// an <c>offset</c> at or past the end is an empty page rather than an
    /// error, and <see cref="MatchHandleItemsPage.NextOffset"/> is always the
    /// offset the next page continues from.
    /// </summary>
    public static class MatchHandleItemsPaging
    {
        public const int DefaultOffset = 0;
        public const int DefaultLimit = 500;
        public const int MinLimit = 1;
        public const int MaxLimit = 5000;

        public static int ClampOffset(int? offset)
        {
            var value = offset.GetValueOrDefault(DefaultOffset);
            return value < DefaultOffset ? DefaultOffset : value;
        }

        public static int ClampLimit(int? limit)
        {
            var value = limit.GetValueOrDefault(DefaultLimit);
            return value < MinLimit ? MinLimit : value > MaxLimit ? MaxLimit : value;
        }

        public static MatchHandleItemsPage Plan(int? offset, int? limit, int totalItemCount)
        {
            var pageOffset = ClampOffset(offset);
            var pageLimit = ClampLimit(limit);
            var remaining = totalItemCount - pageOffset;
            if (remaining < 0)
                remaining = 0;
            var returned = remaining < pageLimit ? remaining : pageLimit;
            // A caller-supplied offset near int.MaxValue would overflow the sum.
            var next = (long)pageOffset + returned;

            return new MatchHandleItemsPage
            {
                TotalItemCount = totalItemCount,
                Offset = pageOffset,
                Limit = pageLimit,
                ReturnedItemCount = returned,
                NextOffset = next > int.MaxValue ? int.MaxValue : (int)next,
                HasMore = next < totalItemCount,
            };
        }
    }
}  
