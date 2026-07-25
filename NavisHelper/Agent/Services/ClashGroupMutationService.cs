using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal static class ClashGroupMutationService
    {
        public static ClashResultGroup FindOrCreateGroup(DocumentClashTests testsData, ClashTest test, string groupName)
        {
            if (testsData == null)
                throw new InvalidOperationException("Clash Detective data is not available.");
            if (test == null)
                throw new ArgumentNullException(nameof(test));

            var existing = FindGroup(test, groupName);
            if (existing != null)
                return existing;

            var newGroup = new ClashResultGroup { DisplayName = groupName };
            testsData.TestsInsertCopy(test, 0, newGroup);

            existing = FindGroup(test, groupName);
            if (existing == null)
                throw new InvalidOperationException("Could not create ClashResultGroup.");

            return existing;
        }

        public static ClashResultGroup FindGroup(ClashTest test, string groupName)
        {
            if (test == null || test.Children == null || string.IsNullOrWhiteSpace(groupName))
                return null;

            var groups = test.Children
                .OfType<ClashResultGroup>()
                .ToList();
            var matchingName = ClashGroupNameHelper.FindMatchingGroupName(groups.Select(group => group.DisplayName), groupName);
            if (string.IsNullOrWhiteSpace(matchingName))
                return null;

            return groups.FirstOrDefault(group => string.Equals(group.DisplayName, matchingName, StringComparison.OrdinalIgnoreCase));
        }

        public static int RebuildGroup(DocumentClashTests testsData, ClashTest selectedTest, ClashResultGroup group, IList<ClashResult> targetResults)
        {
            if (testsData == null || selectedTest == null || group == null)
                return 0;

            var targetIdentities = (targetResults ?? new List<ClashResult>())
                .Where(result => result != null)
                .Select(CreateSavedItemIdentity)
                .Where(identity => identity != null)
                .ToList();
            if (targetIdentities.Count == 0)
                return 0;

            var moved = 0;
            var staleLocations = EnumerateClashResultLocations(group)
                .Where(location => location.Result != null && !MatchesAnySavedItemIdentity(location.Result, targetIdentities))
                .GroupBy(location => location.Parent)
                .ToList();
            foreach (var parentGroup in staleLocations)
            {
                foreach (var location in parentGroup.OrderByDescending(item => item.Index))
                    testsData.TestsMove(parentGroup.Key, location.Index, selectedTest, selectedTest.Children.Count);
            }

            var locations = EnumerateClashResultLocations(selectedTest)
                .Where(location =>
                    location.Result != null &&
                    !object.ReferenceEquals(location.Parent, group) &&
                    MatchesAnySavedItemIdentity(location.Result, targetIdentities))
                .GroupBy(location => location.Parent)
                .ToList();

            foreach (var parentGroup in locations)
            {
                foreach (var location in parentGroup.OrderByDescending(item => item.Index))
                {
                    testsData.TestsMove(parentGroup.Key, location.Index, group, 0);
                    moved++;
                }
            }

            return moved;
        }

        public static void RemoveExistingGroups(DocumentClashTests testsData, ClashTest test, string side, string groupNamePrefix)
        {
            if (testsData == null || test == null || test.Children == null)
                return;

            var groups = test.Children
                .OfType<ClashResultGroup>()
                .Where(group => ClashGroupNameHelper.ShouldRemoveExistingGroup(group.DisplayName, side, groupNamePrefix))
                .ToList();

            foreach (var group in groups)
                RemoveGroup(testsData, test, group);
        }

        public static int RemoveEmptyGroups(DocumentClashTests testsData, ClashTest test, string side, IEnumerable<string> plannedGroupNames)
        {
            if (testsData == null || test == null || test.Children == null)
                return 0;

            var plannedKeys = ClashGroupNameHelper.BuildMatchKeySet(plannedGroupNames ?? new List<string>());

            var emptyGroups = test.Children
                .OfType<ClashResultGroup>()
                .Where(group =>
                    group.Children != null &&
                    group.Children.Count == 0 &&
                    ClashGroupNameHelper.ShouldRemoveEmptyGroup(group.DisplayName, side, plannedKeys))
                .ToList();

            foreach (var group in emptyGroups)
                RemoveGroup(testsData, test, group);

            return emptyGroups.Count;
        }

        public static int UngroupGroup(DocumentClashTests testsData, ClashTest test, ClashResultGroup group)
        {
            if (testsData == null || test == null || group == null)
                return 0;

            var resultCount = EnumerateClashResultLocations(group).Count();
            RemoveGroup(testsData, test, group);
            return resultCount;
        }

        private static void RemoveGroup(DocumentClashTests testsData, ClashTest test, ClashResultGroup group)
        {
            GroupItem parent;
            int index;
            if (!TryFindSavedItemLocation(test, group, out parent, out index))
                return;

            var persistentGroup = parent.Children[index] as ClashResultGroup;
            if (persistentGroup == null)
                return;

            var insertIndex = Math.Min(index + 1, parent.Children.Count);
            for (var childIndex = persistentGroup.Children.Count - 1; childIndex >= 0; childIndex--)
                testsData.TestsMove(persistentGroup, childIndex, parent, insertIndex);

            if (TryFindSavedItemLocation(test, persistentGroup, out parent, out index))
                testsData.TestsRemoveAt(parent, index);
        }

        private static IEnumerable<ClashResultLocation> EnumerateClashResultLocations(GroupItem parent)
        {
            if (parent == null || parent.Children == null)
                yield break;

            for (var i = 0; i < parent.Children.Count; i++)
            {
                var child = parent.Children[i];
                var result = child as ClashResult;
                if (result != null)
                {
                    yield return new ClashResultLocation
                    {
                        Parent = parent,
                        Index = i,
                        Result = result,
                    };
                    continue;
                }

                var group = child as GroupItem;
                if (group == null)
                    continue;

                foreach (var nested in EnumerateClashResultLocations(group))
                    yield return nested;
            }
        }

        private static SavedItemIdentity CreateSavedItemIdentity(SavedItem item)
        {
            if (item == null)
                return null;

            return new SavedItemIdentity
            {
                Guid = TryGetSavedItemGuid(item),
                Reference = item,
            };
        }

        private static bool MatchesAnySavedItemIdentity(SavedItem item, IList<SavedItemIdentity> identities)
        {
            if (item == null || identities == null || identities.Count == 0)
                return false;

            return identities.Any(identity => MatchesSavedItemIdentity(item, identity));
        }

        private static bool MatchesSavedItemIdentity(SavedItem item, SavedItemIdentity identity)
        {
            if (item == null || identity == null)
                return false;

            if (object.ReferenceEquals(item, identity.Reference))
                return true;

            var itemGuid = TryGetSavedItemGuid(item);
            if (itemGuid != Guid.Empty && identity.Guid != Guid.Empty)
                return itemGuid == identity.Guid;

            return false;
        }

        private static Guid TryGetSavedItemGuid(SavedItem item)
        {
            if (item == null)
                return Guid.Empty;

            try
            {
                return item.Guid;
            }
            catch
            {
                return Guid.Empty;
            }
        }

        private static bool TryFindSavedItemLocation(GroupItem parent, SavedItem target, out GroupItem targetParent, out int targetIndex)
        {
            targetParent = null;
            targetIndex = -1;
            if (parent == null || parent.Children == null || target == null)
                return false;

            var targetIdentity = CreateSavedItemIdentity(target);
            for (var i = 0; i < parent.Children.Count; i++)
            {
                var child = parent.Children[i];
                if (MatchesSavedItemIdentity(child, targetIdentity))
                {
                    targetParent = parent;
                    targetIndex = i;
                    return true;
                }

                var childGroup = child as GroupItem;
                if (childGroup != null && TryFindSavedItemLocation(childGroup, target, out targetParent, out targetIndex))
                    return true;
            }

            return false;
        }

        private sealed class ClashResultLocation
        {
            public GroupItem Parent;
            public int Index;
            public ClashResult Result;
        }

        private sealed class SavedItemIdentity
        {
            public Guid Guid;
            public SavedItem Reference;
        }
    }
}
