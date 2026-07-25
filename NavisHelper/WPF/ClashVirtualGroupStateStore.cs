using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.WPF
{
    internal enum ClashGroupingSide
    {
        None,
        ItemA,
        ItemB
    }

    internal sealed class VirtualClashGroup
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public ClashGroupingSide Side { get; set; }
        public string Path { get; set; }
        public string Label { get; set; }
        public List<ClashResult> Results { get; set; } = new List<ClashResult>();
        public ClashResultGroup PersistentGroup { get; set; }
    }

    internal sealed class ClashVirtualGroupStateStore
    {
        private readonly Dictionary<string, List<VirtualClashGroup>> _groupsByTest =
            new Dictionary<string, List<VirtualClashGroup>>(StringComparer.OrdinalIgnoreCase);

        private readonly List<VirtualClashGroup> _activeGroups = new List<VirtualClashGroup>();

        public IReadOnlyList<VirtualClashGroup> ActiveGroups => _activeGroups;

        public void AddGroup(VirtualClashGroup group)
        {
            _activeGroups.Add(group);
        }

        public bool RemoveGroup(VirtualClashGroup group)
        {
            return _activeGroups.Remove(group);
        }

        public void ClearActiveGroups()
        {
            _activeGroups.Clear();
        }

        public void ClearAll()
        {
            _activeGroups.Clear();
            _groupsByTest.Clear();
        }

        public bool ContainsResult(ClashResult result)
        {
            return result != null && ClashVirtualGroupMembershipHelper.ContainsReference(
                _activeGroups
                    .Where(group => group != null && group.Results != null)
                    .SelectMany(group => group.Results),
                result);
        }

        public void RemoveResults(IEnumerable<ClashResult> results)
        {
            var moving = results == null
                ? new List<ClashResult>()
                : results.Where(result => result != null).Distinct().ToList();
            if (moving.Count == 0)
                return;

            var movingSet = ClashVirtualGroupMembershipHelper.CreateReferenceSet(moving);
            foreach (var group in _activeGroups)
            {
                if (group.Results == null)
                    throw new ArgumentNullException("source");
                group.Results = ClashVirtualGroupMembershipHelper.ExceptReferences(group.Results, movingSet);
            }

            RemoveEmptyGroups();
        }

        public void RemoveEmptyGroups()
        {
            _activeGroups.RemoveAll(group => group.Results == null || group.Results.Count == 0);
        }

        public void SaveActiveGroups(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return;

            var groups = _activeGroups
                .Where(group => group != null && ClashVirtualGroupCachePolicy.ShouldStore(group.Results))
                .Select(CloneGroup)
                .ToList();

            if (groups.Count == 0)
                _groupsByTest.Remove(key);
            else
                _groupsByTest[key] = groups;
        }

        public bool TryGetCachedGroups(string key, out List<VirtualClashGroup> groups)
        {
            return _groupsByTest.TryGetValue(key, out groups);
        }

        public void RemoveCachedGroups(string key)
        {
            _groupsByTest.Remove(key);
        }

        public VirtualClashGroup CloneGroup(VirtualClashGroup group)
        {
            return new VirtualClashGroup
            {
                Id = group.Id,
                Side = group.Side,
                Path = group.Path,
                Label = group.Label,
                Results = ClashVirtualGroupCachePolicy.CreateResultSnapshot(group.Results),
                PersistentGroup = group.PersistentGroup
            };
        }
    }
}
