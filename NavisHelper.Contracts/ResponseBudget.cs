namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// The most one tool answer may carry to an MCP client (#427). A client cuts or refuses a
    /// result past about 25 000 tokens; at the 3.375 bytes a token measured for this server's
    /// JSON that is 84 375 bytes.
    /// </summary>
    public static class ResponseBudget
    {
        public const int MaxBytes = 84000;

        /// <summary>Room left for a page's counters and the server's timing block.</summary>
        public const int EnvelopeBytes = 4000;

        /// <summary>The bytes a paged tool fills with items before it stops and returns nextOffset.</summary>
        public const int PageBytes = MaxBytes - EnvelopeBytes;
    }

    /// <summary>
    /// Fills one page up to a byte budget. The first item is always taken, however large, so a
    /// caller paging with nextOffset always moves forward; after that an item that would pass
    /// the budget is refused and the page is exhausted.
    /// </summary>
    public sealed class ResponsePageBudget
    {
        private readonly int _limit;

        public ResponsePageBudget(int limit)
        {
            _limit = limit;
        }

        public int Used { get; private set; }

        public bool Exhausted { get; private set; }

        public bool TryTake(int bytes)
        {
            if (Used > 0 && (long)Used + bytes > _limit)
            {
                Exhausted = true;
                return false;
            }

            Used += bytes;
            return true;
        }
    }
}
