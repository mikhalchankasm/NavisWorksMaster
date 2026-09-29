using System;
using System.Collections.Generic;
using System.Globalization;

namespace NavisHelper.Contracts
{
    public sealed class MatchHandleLedger
    {
        private const int MaxRecords = 1000;
        private readonly Func<DateTime> _clock;
        private readonly Dictionary<string, Removal> _removals =
            new Dictionary<string, Removal>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _order = new Queue<string>();

        public MatchHandleLedger(Func<DateTime> clock = null)
        {
            _clock = clock ?? (() => DateTime.UtcNow);
        }

        public void RecordExpired(string handle, DateTime lastAccessUtc)
        {
            Record(handle, "expired", lastAccessUtc);
        }

        public void RecordEvicted(string handle)
        {
            Record(handle, "evicted", null);
        }

        public void RecordCleared(string handle)
        {
            Record(handle, "cleared", null);
        }

        public string Explain(string handle, long lastIssuedSequence)
        {
            long sequence;
            if (string.IsNullOrEmpty(handle) ||
                !handle.StartsWith("mh_", StringComparison.OrdinalIgnoreCase) ||
                !long.TryParse(handle.Substring(3), NumberStyles.None, CultureInfo.InvariantCulture, out sequence) ||
                sequence < 1 || sequence > lastIssuedSequence)
                return "This match handle was never issued by this host.";

            lock (_removals)
            {
                Removal removal;
                if (_removals.TryGetValue(handle, out removal))
                {
                    if (removal.Reason == "expired")
                    {
                        var minutes = Math.Max(0, (int)Math.Floor((removal.RemovedAtUtc - removal.LastAccessUtc.Value).TotalMinutes));
                        return "This match handle expired after " + minutes.ToString(CultureInfo.InvariantCulture) + " minutes idle.";
                    }
                    if (removal.Reason == "evicted")
                        return "This match handle was evicted because only the 100 most recently used handles are kept.";
                    return "This match handle was cleared because the document changed or the host restarted.";
                }
            }

            return "This match handle is no longer available because its removal history was discarded or the host restarted.";
        }

        private void Record(string handle, string reason, DateTime? lastAccessUtc)
        {
            if (string.IsNullOrEmpty(handle))
                throw new ArgumentException("A match handle is required.", nameof(handle));

            lock (_removals)
            {
                if (!_removals.ContainsKey(handle))
                    _order.Enqueue(handle);
                _removals[handle] = new Removal(reason, _clock(), lastAccessUtc);
                while (_order.Count > MaxRecords)
                    _removals.Remove(_order.Dequeue());
            }
        }

        private sealed class Removal
        {
            public Removal(string reason, DateTime removedAtUtc, DateTime? lastAccessUtc)
            {
                Reason = reason;
                RemovedAtUtc = removedAtUtc;
                LastAccessUtc = lastAccessUtc;
            }

            public string Reason { get; private set; }
            public DateTime RemovedAtUtc { get; private set; }
            public DateTime? LastAccessUtc { get; private set; }
        }
    }
}
