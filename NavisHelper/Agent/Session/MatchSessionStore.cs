using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;

namespace NavisHelper.Agent.Session
{
    internal sealed class MatchSessionStore
    {
        private const int MaxEntries = 100;
        private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(10);

        private readonly object _sync = new object();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
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
            lock (_sync)
            {
                EvictExpiredLocked();

                Entry entry;
                if (!_entries.TryGetValue(handle, out entry))
                {
                    items = null;
                    return false;
                }

                entry.LastAccessUtc = DateTime.UtcNow;
                items = entry.Items;
                return true;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
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
                _entries.Remove(key);
            }
        }

        private void EvictOverflowLocked()
        {
            while (_entries.Count >= MaxEntries)
            {
                var oldest = _entries.OrderBy(pair => pair.Value.LastAccessUtc).First();
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
