using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Contracts;

namespace NavisHelper.Agent.Session
{
    internal sealed class MatchSessionStore
    {
        private const int MaxEntries = 100;
        private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(10);

        private readonly object _sync = new object();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly MatchHandleLedger _ledger = new MatchHandleLedger();
        private long _sequence;

        public string Add(IList<ModelItem> items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            lock (_sync)
            {
                EvictExpiredLocked();
                EvictOverflowLocked();

                _sequence++;
                var handle = "mh_" + _sequence.ToString("D6");
                _entries[handle] = new Entry(items.ToList(), DateTime.UtcNow);
                return handle;
            }
        }

        public bool TryGet(string handle, out IList<ModelItem> items)
        {
            string reason;
            return TryGet(handle, out items, out reason);
        }

        public bool TryGet(string handle, out IList<ModelItem> items, out string reason)
        {
            lock (_sync)
            {
                EvictExpiredLocked();

                Entry entry;
                if (!_entries.TryGetValue(handle, out entry))
                {
                    items = null;
                    reason = _ledger.Explain(handle, _sequence);
                    return false;
                }

                entry.LastAccessUtc = DateTime.UtcNow;
                items = entry.Items;
                reason = string.Empty;
                return true;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                foreach (var handle in _entries.Keys)
                    _ledger.RecordCleared(handle);
                _entries.Clear();
            }
        }

        private void EvictExpiredLocked()
        {
            var now = DateTime.UtcNow;
            var expired = _entries
                .Where(pair => now - pair.Value.LastAccessUtc > EntryTtl)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var key in expired)
            {
                _ledger.RecordExpired(key, _entries[key].LastAccessUtc);
                _entries.Remove(key);
            }
        }

        private void EvictOverflowLocked()
        {
            while (_entries.Count >= MaxEntries)
            {
                var oldest = _entries.OrderBy(pair => pair.Value.LastAccessUtc).First();
                _ledger.RecordEvicted(oldest.Key);
                _entries.Remove(oldest.Key);
            }
        }

        private sealed class Entry
        {
            public Entry(IList<ModelItem> items, DateTime lastAccessUtc)
            {
                Items = items;
                LastAccessUtc = lastAccessUtc;
            }

            public IList<ModelItem> Items { get; private set; }
            public DateTime LastAccessUtc { get; set; }
        }
    }
}
