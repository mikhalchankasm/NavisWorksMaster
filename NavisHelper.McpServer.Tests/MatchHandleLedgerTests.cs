using System;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests
{
    public class MatchHandleLedgerTests
    {
        [Fact]
        public void ExpiredHandleReportsIdleMinutesAtRemoval()
        {
            var now = new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);
            var ledger = new MatchHandleLedger(() => now);

            ledger.RecordExpired("mh_000001", now.AddMinutes(-12));
            now = now.AddHours(1);

            Assert.Equal("This match handle expired after 12 minutes idle.", ledger.Explain("mh_000001", 1));
        }

        [Fact]
        public void EvictedHandleReportsCapacity()
        {
            var ledger = new MatchHandleLedger();
            ledger.RecordEvicted("mh_000002");

            Assert.Equal("This match handle was evicted because only the 100 most recently used handles are kept.",
                ledger.Explain("mh_000002", 2));
        }

        [Fact]
        public void ClearedHandleReportsDocumentOrHostChange()
        {
            var ledger = new MatchHandleLedger();
            ledger.RecordCleared("mh_000003");

            Assert.Equal("This match handle was cleared because the document changed or the host restarted.",
                ledger.Explain("mh_000003", 3));
        }

        [Fact]
        public void UnissuedHandlesAreIdentified()
        {
            var ledger = new MatchHandleLedger();

            Assert.Equal("This match handle was never issued by this host.", ledger.Explain("mh_000004", 3));
            Assert.Equal("This match handle was never issued by this host.", ledger.Explain("other_000001", 3));
        }

        [Fact]
        public void OldestRemovalFallsOutOfBoundedHistory()
        {
            var ledger = new MatchHandleLedger();
            for (var sequence = 1; sequence <= 1001; sequence++)
                ledger.RecordEvicted("mh_" + sequence.ToString("D6"));

            Assert.Contains("removal history was discarded", ledger.Explain("mh_000001", 1001));
            Assert.Contains("evicted", ledger.Explain("mh_001001", 1001));
        }
    }
}
