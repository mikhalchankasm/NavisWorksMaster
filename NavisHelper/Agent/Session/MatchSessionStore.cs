using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;

namespace NavisHelper.Agent.Session
{
    internal sealed class MatchSessionStore
    {
        private const int MaxEntries = 100;
        private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(10);

        private readonly object _sync = new object();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private long _sequence;
        private readonly string _session = Guid.NewGuid().ToString("N");
        public string InstanceId { get; set; } = "unassigned";

        public static string ItemHandle(string pageHandle, int index)
        {
            return pageHandle + ":" + index.ToString(CultureInfo.InvariantCulture);
        }

        public static string DescribeStale(string handle)
        {
            var parts = (handle ?? string.Empty).Split('|');
            var origin = parts.Length == 4 ? parts[1] : "unknown (legacy handle)";
            return "Match handle is stale or unknown. Issuing instanceId=" + origin +
                ". Use that instanceId and re-run find_items/list_item_children after expiry or document changes.";
        }

        public string Add(IList<ModelItem> items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            lock (_sync)
            {
                EvictExpiredLocked();
                EvictOverflowLocked();

                _sequence++;
                var handle = "mh|" + InstanceId + "|" + _session + "|" + _sequence.ToString("D6", CultureInfo.InvariantCulture);
                _entries[handle] = new Entry(items.ToList(), DateTime.UtcNow);
                return handle;
            }
        }

        public bool TryGet(string handle, out IList<ModelItem> items)
        {
            lock (_sync)
            {
                EvictExpiredLocked();
                items = null;
                if (string.IsNullOrWhiteSpace(handle)) return false;
                var parts = handle.Split('|');
                if (parts.Length == 4 && !string.Equals(parts[1], InstanceId, StringComparison.Ordinal))
                    return false;
                var itemIndex = -1;
                var colon = handle.IndexOf(':', handle.LastIndexOf('|') + 1);
                if (colon >= 0)
                {
                    if (!int.TryParse(handle.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out itemIndex)) return false;
                    handle = handle.Substring(0, colon);
                }

                Entry entry;
                if (!_entries.TryGetValue(handle, out entry))
                {
                    items = null;
                    return false;
                }

                entry.LastAccessUtc = DateTime.UtcNow;
                if (itemIndex >= entry.Items.Count) return false;
                items = itemIndex < 0 ? entry.Items : new[] { entry.Items[itemIndex] };
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
