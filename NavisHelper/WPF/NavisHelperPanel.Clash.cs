using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.IO.Compression;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.VisualBasic;
using Path = System.IO.Path;
using NavisHelper.Core;
using NavisHelper.Agent.Contracts;
using NavisHelper.Interfaces;
using NavisHelper.Agent.Services;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop;
using WpfColor = System.Windows.Media.Color;
// DevExpress убран — crash при загрузке в Navisworks
// using DevExpress.Xpf.Grid;
// using DevExpress.Xpf.Core;
using NwApplication = Autodesk.Navisworks.Api.Application;
using NwColor = Autodesk.Navisworks.Api.Color;

namespace NavisHelper.WPF
{
    public partial class NavisHelperPanel : UserControl
    {

        private TextBox _testFilterBox;

        private TextBox _clashFilterBox;

        private TextBox _clashItemAFilterBox;

        private TextBox _clashItemBFilterBox;

        private WrapPanel _clashFilterPanel;

        private bool _suppressClashTestSelectionChanged;

        private bool _suppressClashResultSelectionChanged;

        private ClashGroupingSide _clashGroupingSide = ClashGroupingSide.None;



        private static IEnumerable<ClashResultLocation> EnumerateClashResultLocations(Autodesk.Navisworks.Api.GroupItem parent)
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
                        Result = result
                    };
                    continue;
                }

                var group = child as Autodesk.Navisworks.Api.GroupItem;
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
                // Preserve the original getter access and its exception behavior.
                // DisplayName is diagnostic identity context, not a match fallback.
                DisplayName = item.DisplayName ?? string.Empty
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

        private static bool TryFindSavedItemLocation(Autodesk.Navisworks.Api.GroupItem parent, SavedItem target, out Autodesk.Navisworks.Api.GroupItem targetParent, out int targetIndex)
        {
            targetParent = null;
            targetIndex = -1;
            if (parent == null || parent.Children == null || target == null)
                return false;

            if (parent.Children.Count == 0)
                return false;

            return TryFindSavedItemLocationByIdentity(parent, CreateSavedItemIdentity(target), out targetParent, out targetIndex);
        }

        private static bool TryFindSavedItemLocationByIdentity(Autodesk.Navisworks.Api.GroupItem parent, SavedItemIdentity targetIdentity, out Autodesk.Navisworks.Api.GroupItem targetParent, out int targetIndex)
        {
            targetParent = null;
            targetIndex = -1;
            if (parent == null || parent.Children == null || targetIdentity == null)
                return false;

            for (var i = 0; i < parent.Children.Count; i++)
            {
                var child = parent.Children[i];
                if (MatchesSavedItemIdentity(child, targetIdentity))
                {
                    targetParent = parent;
                    targetIndex = i;
                    return true;
                }

                var childGroup = child as Autodesk.Navisworks.Api.GroupItem;
                if (childGroup != null && TryFindSavedItemLocationByIdentity(childGroup, targetIdentity, out targetParent, out targetIndex))
                    return true;
            }

            return false;
        }

        private bool IsClashInVirtualGroup(ClashResult result)
        {
            return _clashVirtualGroupState.ContainsResult(result);
        }

        private void RemoveClashesFromVirtualGroups(IEnumerable<ClashResult> results)
        {
            _clashVirtualGroupState.RemoveResults(results);
        }

        private void RemoveEmptyVirtualClashGroups()
        {
            _clashVirtualGroupState.RemoveEmptyGroups();
        }

        private void SaveActiveClashGroupsToCache()
        {
            var key = GetClashTestCacheKey(_activeClashTest);
            if (string.IsNullOrWhiteSpace(key))
                return;

            _clashVirtualGroupState.SaveActiveGroups(key);
        }

        private void RestoreCachedClashGroups(ClashTest test)
        {
            var key = GetClashTestCacheKey(test);
            if (string.IsNullOrWhiteSpace(key))
                return;

            List<VirtualClashGroup> cached;
            if (!_clashVirtualGroupState.TryGetCachedGroups(key, out cached) || cached == null || cached.Count == 0)
                return;

            foreach (var cachedGroup in cached)
            {
                if (cachedGroup == null)
                    continue;

                var persistentGroup = FindClashResultGroup(test, BuildPersistentClashGroupName(cachedGroup.Side, cachedGroup.Label), cachedGroup.Side);
                if (persistentGroup == null)
                    continue;

                var results = EnumerateClashResults(persistentGroup.Children)
                    .Where(result => result != null && _loadedResults.Any(item => object.ReferenceEquals(item, result)))
                    .Distinct()
                    .ToList();
                if (results.Count == 0)
                    continue;

                if (_virtualClashGroups.Any(group =>
                    ClashVirtualGroupCachePolicy.MatchesRestoreDuplicate(
                        ToVirtualClashGroupSide(group.Side),
                        group.Path,
                        group.Label,
                        group.PersistentGroup,
                        ToVirtualClashGroupSide(cachedGroup.Side),
                        cachedGroup.Path,
                        cachedGroup.Label,
                        persistentGroup)))
                    continue;

                var restored = CloneVirtualClashGroup(cachedGroup);
                restored.Results = results;
                restored.PersistentGroup = persistentGroup;
                _clashVirtualGroupState.AddGroup(restored);
            }

            RemoveEmptyVirtualClashGroups();
        }

        private VirtualClashGroup CloneVirtualClashGroup(VirtualClashGroup group)
        {
            return _clashVirtualGroupState.CloneGroup(group);
        }

        private static bool SameVirtualClashGroup(VirtualClashGroup left, VirtualClashGroup right)
        {
            if (left == null || right == null)
                return false;

            return ClashVirtualGroupIdentityHelper.AreSame(
                ToVirtualClashGroupSide(left.Side), left.Path, left.Label,
                ToVirtualClashGroupSide(right.Side), right.Path, right.Label);
        }

        private static string GetClashTestCacheKey(ClashTest test)
        {
            if (test == null)
                return string.Empty;

            var testGuid = Guid.Empty;
            try
            {
                if (test.Guid != Guid.Empty)
                    testGuid = test.Guid;
            }
            catch
            {
            }

            if (testGuid != Guid.Empty)
                return ClashVirtualGroupIdentityHelper.BuildTestCacheKey(testGuid, null);

            return ClashVirtualGroupIdentityHelper.BuildTestCacheKey(Guid.Empty, GetSafeClashTestDisplayName(test));
        }

        private static string GetSafeClashTestDisplayName(ClashTest test)
        {
            if (test == null)
                return string.Empty;

            try
            {
                return test.DisplayName ?? string.Empty;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to read Clash test display name: " + ex.Message, "ClashUI");
                return string.Empty;
            }
        }

        private void UpdateClashGroupingStatusText()
        {
            if (_clashGroupingStatus == null)
                return;

            if (_virtualClashGroups.Count > 0)
            {
                var groupedClashes = _virtualClashGroups
                    .SelectMany(group => group.Results ?? new List<ClashResult>())
                    .Distinct()
                    .Count();
                _clashGroupingStatus.Text = $"Групп: {_virtualClashGroups.Count}, коллизий в группах: {groupedClashes}";
                _clashGroupingStatus.Foreground = Brushes.DarkGreen;
                return;
            }

            if (_clashGroupingSide == ClashGroupingSide.None)
            {
                _clashGroupingStatus.Text = "Группировка: нет";
                _clashGroupingStatus.Foreground = Brushes.DimGray;
                return;
            }

            var sideLabel = _clashGroupingSide == ClashGroupingSide.ItemA ? "A" : "B";
            var label = !string.IsNullOrWhiteSpace(_clashGroupingLabel)
                ? _clashGroupingLabel
                : string.IsNullOrWhiteSpace(_clashGroupingPath) ? "авто" : _clashGroupingPath;

            _clashGroupingStatus.Text = $"Группировка {sideLabel}: {label}";
            _clashGroupingStatus.Foreground = Brushes.DarkGreen;
        }





        // ============================================================
        //  Утилиты: иконки и кнопки
        // ============================================================
    }
}
