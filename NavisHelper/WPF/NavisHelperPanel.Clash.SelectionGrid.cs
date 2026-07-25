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
using NavisHelper.Interfaces;
using NavisHelper.Agent.Contracts;
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
        private void SelectClashResultItems(ClashResultSelectionMode mode)
        {
            var doc = NwApplication.ActiveDocument;
            if (doc == null || doc.IsClear)
            {
                SetGlobalStatus("Нет активного документа", Brushes.Orange);
                return;
            }

            var results = GetClashResultsFromRow(_clashContextMenuItem ?? _clashGrid?.SelectedItem);
            if (results.Count == 0)
            {
                SetGlobalStatus("Выберите коллизию", Brushes.Orange);
                return;
            }

            var selection = new ModelItemCollection();
            foreach (var result in results)
            {
                if (mode == ClashResultSelectionMode.ItemA || mode == ClashResultSelectionMode.Both)
                    AddClashSideItems(selection, result.Item1, result.Selection1);
                if (mode == ClashResultSelectionMode.ItemB || mode == ClashResultSelectionMode.Both)
                    AddClashSideItems(selection, result.Item2, result.Selection2);
            }

            if (selection.Count == 0)
            {
                try { doc.CurrentSelection.Clear(); } catch { }
                SetGlobalStatus($"Selection: {GetClashSelectionLabel(mode)} не найден в дереве модели", Brushes.Orange);
                return;
            }

            try
            {
                doc.CurrentSelection.Clear();
                doc.CurrentSelection.CopyFrom(selection);
                SetGlobalStatus($"Selection: {GetClashSelectionLabel(mode)} ({selection.Count})", Brushes.DarkGreen);
            }
            catch (Exception ex)
            {
                SetGlobalStatus("Selection error: " + ex.Message, Brushes.Red);
            }
        }

        private static void AddClashSideItems(ModelItemCollection target, ModelItem primaryItem, ModelItemCollection sideItems)
        {
            if (sideItems != null && sideItems.Count > 0)
            {
                foreach (var item in sideItems)
                    AddSelectableModelItem(target, item);
                return;
            }

            AddSelectableModelItem(target, primaryItem);
        }

        private static bool AddSelectableModelItem(ModelItemCollection target, ModelItem item)
        {
            if (target == null || item == null)
                return false;

            var selectable = ResolveSelectableModelItem(item);
            if (selectable == null)
                return false;

            if (!target.Contains(selectable))
                target.Add(selectable);

            return true;
        }

        private static ModelItem ResolveSelectableModelItem(ModelItem item)
        {
            var current = item;
            while (current != null)
            {
                ModelItem parent;
                try
                {
                    parent = current.Parent;
                }
                catch
                {
                    return null;
                }

                if (parent == null)
                    return null;

                try
                {
                    var ignored = current.DisplayName;
                    return current;
                }
                catch
                {
                    current = parent;
                }
            }

            return null;
        }

        private static string GetClashSelectionLabel(ClashResultSelectionMode mode)
        {
            switch (mode)
            {
                case ClashResultSelectionMode.ItemA:
                    return "объект A";
                case ClashResultSelectionMode.ItemB:
                    return "объект B";
                case ClashResultSelectionMode.Both:
                    return "объекты A+B";
                default:
                    return "объекты";
            }
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                var typed = child as T;
                if (typed != null)
                    return typed;
                child = VisualTreeHelper.GetParent(child);
            }

            return null;
        }

        /// <summary>Получить цвет из комбо. null = "без подсветки".</summary>

        private NwColor GetClashColor(ComboBox combo)
        {
            var item = combo.SelectedItem as ComboBoxItem;
            if (item?.Tag is byte[] rgb && rgb.Length == 3)
                return new NwColor(rgb[0] / 255.0, rgb[1] / 255.0, rgb[2] / 255.0);
            return null; // без подсветки
        }

        // Данные для грида

        private sealed class ClashTestRow
        {
            public int TestIndex { get; set; }
            public string Name { get; set; }
            public int Total { get; set; }
            public int New { get; set; }
            public int Active { get; set; }
            public ClashTest Test { get; set; }
        }

        private sealed class ClashResultGridRow
        {
            public string Status { get; set; }
            public string Name { get; set; }
            public string GroupName { get; set; }
            public string Distance { get; set; }
            public string ItemA { get; set; }
            public string ItemB { get; set; }
            public ClashResult Result { get; set; }
            public List<ClashResult> Results { get; set; }
            public bool IsGroup { get; set; }
            public ModelItem GroupItem { get; set; }
            public ClashGroupingSide GroupingSide { get; set; }
            public Guid? VirtualGroupId { get; set; }
            public ClashResultGroup PersistentGroup { get; set; }
        }

        private sealed class ClashGroupContentRow
        {
            public int Index { get; set; }
            public string Name { get; set; }
            public string ItemA { get; set; }
            public string ItemB { get; set; }
        }

        private sealed class ClashGroupBucket
        {
            public ModelItem Ancestor { get; set; }
            public List<ClashResult> Results { get; } = new List<ClashResult>();
        }

        private sealed class ClashTestLocation
        {
            public Autodesk.Navisworks.Api.GroupItem Parent { get; set; }
            public int Index { get; set; }
            public ClashTest Test { get; set; }
        }

        private List<ClashTest> _loadedTests = new List<ClashTest>();

        private List<ClashResult> _loadedResults = new List<ClashResult>();

        private ClashTestRow[] _allTestRows; // кеш строк тестов для фильтрации

        private static List<ClashResult> GetClashResultsFromRow(object rowObject)
        {
            var results = new List<ClashResult>();
            if (rowObject == null)
                return results;

            var typed = rowObject as ClashResultGridRow;
            if (typed != null)
            {
                if (typed.Results != null)
                    results.AddRange(typed.Results.Where(result => result != null));
                else if (typed.Result != null)
                    results.Add(typed.Result);

                return results
                    .Distinct()
                    .ToList();
            }

            try
            {
                dynamic row = rowObject;
                ClashResult result = row.Result as ClashResult;
                if (result != null)
                    results.Add(result);
            }
            catch
            {
            }

            return results;
        }

        private void SetClashGrouping(ClashGroupingSide side)
        {
            _clashGroupingSide = side;
            _clashGroupingPath = null;
            _clashGroupingLabel = null;
            if (side != ClashGroupingSide.None)
                _clashVirtualGroupState.ClearActiveGroups();
            _pendingClashGroupingTag = null;
            if (_applyClashGroupingButton != null)
                _applyClashGroupingButton.IsEnabled = false;
            SaveActiveClashGroupsToCache();
            RefreshClashGridRows();
            UpdateClashGroupingTrees();
            var label = side == ClashGroupingSide.ItemA
                ? "A"
                : side == ClashGroupingSide.ItemB ? "B" : "выключена";
            SetGlobalStatus($"Группировка: {label}", Brushes.DarkGreen);
        }

        private void ResetSelectedClashGrouping()
        {
            var row = (_clashContextMenuItem ?? _clashGrid?.SelectedItem) as ClashResultGridRow;
            if (row == null)
            {
                UngroupSelectedClashGroup();
                return;
            }
            if (!row.IsGroup)
            {
                SetGlobalStatus("Выберите строку-группу для сброса", Brushes.Orange);
                return;
            }
            if (!row.VirtualGroupId.HasValue)
            {
                UngroupSelectedClashGroups(new List<ClashResultGridRow> { row });
                return;
            }

            var virtualGroup = _virtualClashGroups.FirstOrDefault(item => item.Id == row.VirtualGroupId.Value);
            if (virtualGroup == null || virtualGroup.PersistentGroup != null)
            {
                UngroupSelectedClashGroups(new List<ClashResultGridRow> { row });
                return;
            }

            _clashVirtualGroupState.RemoveGroup(virtualGroup);
            SaveActiveClashGroupsToCache();
            RefreshClashGridRows();
            UpdateClashGroupingTrees();
            _clashContextMenuItem = null;
            SetGlobalStatus("Группа предпросмотра сброшена: " + (virtualGroup.Label ?? row.GroupName ?? row.Name), Brushes.DarkGreen);
        }

        private void RefreshClashGridRows()
        {
            if (_clashGrid == null)
                return;

            var selectedResult = GetClashResultsFromRow(_clashGrid.SelectedItem).FirstOrDefault();
            var rows = BuildClashGridRows(_loadedResults, _clashGroupingSide);
            ReplaceDataGridItemsSourcePreservingSort(_clashGrid, rows);
            if (selectedResult != null)
            {
                var selectedRow = rows.FirstOrDefault(row => row.Results != null && row.Results.Any(result => object.ReferenceEquals(result, selectedResult)));
                if (selectedRow != null)
                {
                    _clashGrid.SelectedItem = selectedRow;
                    _clashGrid.ScrollIntoView(selectedRow);
                }
            }

            var groupedCount = rows.Count(row => row.IsGroup);
            var message = groupedCount > 0
                ? $"Коллизий: {_loadedResults.Count}, групп: {groupedCount}, строк: {rows.Count}"
                : $"Коллизий: {rows.Count} / {_loadedResults.Count}";
            SetGlobalStatus(message, Brushes.DarkGreen);
            UpdateClashGroupingStatusText();
        }

        private static void ReplaceDataGridItemsSourcePreservingSort(
            DataGrid grid,
            System.Collections.IEnumerable itemsSource)
        {
            if (grid == null)
                return;

            var sortDescriptions = new List<System.ComponentModel.SortDescription>();
            if (grid.ItemsSource != null)
            {
                var currentView = System.Windows.Data.CollectionViewSource.GetDefaultView(grid.ItemsSource);
                if (currentView != null && currentView.CanSort)
                    sortDescriptions.AddRange(currentView.SortDescriptions);
            }

            grid.ItemsSource = itemsSource;
            if (sortDescriptions.Count == 0 || grid.ItemsSource == null)
                return;

            var nextView = System.Windows.Data.CollectionViewSource.GetDefaultView(grid.ItemsSource);
            if (nextView == null || !nextView.CanSort)
                return;

            using (nextView.DeferRefresh())
            {
                nextView.SortDescriptions.Clear();
                foreach (var sortDescription in sortDescriptions)
                    nextView.SortDescriptions.Add(sortDescription);
            }

            foreach (var column in grid.Columns)
                column.SortDirection = null;

            foreach (var sortDescription in sortDescriptions)
            {
                var column = grid.Columns.FirstOrDefault(candidate =>
                    string.Equals(
                        GetDataGridColumnSortMember(candidate),
                        sortDescription.PropertyName,
                        StringComparison.Ordinal));
                if (column != null)
                    column.SortDirection = sortDescription.Direction;
            }
        }

        private static string GetDataGridColumnSortMember(DataGridColumn column)
        {
            if (column == null)
                return string.Empty;
            if (!string.IsNullOrWhiteSpace(column.SortMemberPath))
                return column.SortMemberPath;

            var boundColumn = column as DataGridBoundColumn;
            var binding = boundColumn?.Binding as System.Windows.Data.Binding;
            return binding?.Path?.Path ?? string.Empty;
        }

        private List<ClashResultGridRow> BuildClashGridRows(IEnumerable<ClashResult> results, ClashGroupingSide groupingSide)
        {
            var source = results == null
                ? new List<ClashResult>()
                : results.Where(result => result != null).ToList();

            if (_virtualClashGroups.Count > 0)
                return BuildVirtualClashGridRows(source);

            source = source.Where(PassesClashFilters).ToList();

            if (groupingSide == ClashGroupingSide.None)
                return source.Select(CreateRawClashGridRow)
                    .OrderBy(row => row.Name ?? string.Empty, NaturalStringComparer.Instance)
                    .ToList();

            var buckets = new Dictionary<string, ClashGroupBucket>(StringComparer.OrdinalIgnoreCase);
            var rawRows = new List<ClashResultGridRow>();

            foreach (var result in source)
            {
                var ancestor = ResolveClashGroupingAncestor(result, groupingSide, _clashGroupingPath);
                var key = BuildModelItemPath(ancestor);
                if (ancestor == null || string.IsNullOrWhiteSpace(key))
                {
                    rawRows.Add(CreateRawClashGridRow(result));
                    continue;
                }

                ClashGroupBucket bucket;
                if (!buckets.TryGetValue(key, out bucket))
                {
                    bucket = new ClashGroupBucket { Ancestor = ancestor };
                    buckets.Add(key, bucket);
                }

                bucket.Results.Add(result);
            }

            var rows = new List<ClashResultGridRow>();
            foreach (var row in rawRows)
                rows.Add(row);

            foreach (var bucket in buckets.Values)
            {
                if (bucket.Results.Count <= 1)
                {
                    rows.Add(CreateRawClashGridRow(bucket.Results[0]));
                    continue;
                }

                rows.Add(CreateGroupedClashGridRow(bucket, groupingSide));
            }

            return rows
                .OrderByDescending(row => row.IsGroup)
                .ThenBy(row => row.Name ?? string.Empty, NaturalStringComparer.Instance)
                .ToList();
        }

        private bool PassesClashFilters(ClashResult result)
        {
            if (result == null)
                return false;

            if (!ShouldShowStatus(result.Status))
                return false;

            if (!TextMatchesFilter(result.DisplayName, _clashFilterBox))
                return false;
            if (!TextMatchesFilter(GetClashItemName(result.Selection1), _clashItemAFilterBox))
                return false;
            if (!TextMatchesFilter(GetClashItemName(result.Selection2), _clashItemBFilterBox))
                return false;

            return true;
        }

        private bool ShouldShowStatus(ClashResultStatus status)
        {
            if (_clashFilterPanel == null)
                return true;

            if (status == ClashResultStatus.New) return IsClashStatusChecked("New", true);
            if (status == ClashResultStatus.Active) return IsClashStatusChecked("Active", true);
            if (status == ClashResultStatus.Reviewed) return IsClashStatusChecked("Reviewed", true);
            if (status == ClashResultStatus.Approved) return IsClashStatusChecked("Approved", false);
            if (status == ClashResultStatus.Resolved) return IsClashStatusChecked("Resolved", false);

            return true;
        }

        private bool IsClashStatusChecked(string label, bool fallback)
        {
            if (_clashFilterPanel == null)
                return fallback;

            foreach (var cb in _clashFilterPanel.Children.OfType<CheckBox>())
            {
                var text = cb.Content as string;
                if (string.Equals(text, label, StringComparison.OrdinalIgnoreCase))
                    return cb.IsChecked == true;
            }

            return fallback;
        }

        private static bool TextMatchesFilter(string value, TextBox filterBox)
        {
            var filter = filterBox?.Text;
            if (string.IsNullOrWhiteSpace(filter))
                return true;

            return (value ?? string.Empty).IndexOf(filter.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private List<ClashResultGridRow> BuildVirtualClashGridRows(IList<ClashResult> source)
        {
            var rows = new List<ClashResultGridRow>();
            var grouped = new List<ClashResult>();

            foreach (var group in _virtualClashGroups.ToList())
            {
                var groupResults = (group.Results ?? new List<ClashResult>())
                    .Where(result => result != null && source.Any(item => object.ReferenceEquals(item, result)))
                    .Distinct()
                    .ToList();

                group.Results = groupResults;
                if (groupResults.Count == 0)
                    continue;

                grouped.AddRange(groupResults);
                var visibleResults = groupResults
                    .Where(PassesClashFilters)
                    .ToList();
                if (visibleResults.Count == 0)
                    continue;

                rows.Add(CreateVirtualClashGridRow(group, visibleResults));
            }

            RemoveEmptyVirtualClashGroups();

            foreach (var result in source.Where(PassesClashFilters))
            {
                if (grouped.Any(item => object.ReferenceEquals(item, result)))
                    continue;

                rows.Add(CreateRawClashGridRow(result));
            }

            return rows
                .OrderByDescending(row => row.IsGroup)
                .ThenBy(row => row.Name ?? string.Empty, NaturalStringComparer.Instance)
                .ToList();
        }

        private ClashResultGridRow CreateRawClashGridRow(ClashResult result)
        {
            return new ClashResultGridRow
            {
                Status = StatusLabel(result.Status),
                Name = result.DisplayName,
                Distance = FormatClashDistance(result),
                ItemA = GetClashItemName(result.Selection1),
                ItemB = GetClashItemName(result.Selection2),
                Result = result,
                Results = new List<ClashResult> { result },
                IsGroup = false,
            };
        }

        private ClashResultGridRow CreateGroupedClashGridRow(ClashGroupBucket bucket, ClashGroupingSide groupingSide)
        {
            var groupName = GetDisplayNameOrFallback(bucket.Ancestor, groupingSide == ClashGroupingSide.ItemA ? "Группа A" : "Группа B");
            var prefix = groupingSide == ClashGroupingSide.ItemA ? "A" : "B";
            return new ClashResultGridRow
            {
                Status = "Гр.",
                Name = $"{prefix}: {groupName} ({bucket.Results.Count})",
                GroupName = groupName,
                Distance = bucket.Results.Count.ToString(CultureInfo.InvariantCulture) + " шт.",
                ItemA = groupingSide == ClashGroupingSide.ItemA ? groupName : FormatDistinctClashSideNames(bucket.Results, ClashGroupingSide.ItemA),
                ItemB = groupingSide == ClashGroupingSide.ItemB ? groupName : FormatDistinctClashSideNames(bucket.Results, ClashGroupingSide.ItemB),
                Result = bucket.Results.FirstOrDefault(),
                Results = bucket.Results.ToList(),
                IsGroup = true,
                GroupItem = bucket.Ancestor,
                GroupingSide = groupingSide,
            };
        }

        private ClashResultGridRow CreateVirtualClashGridRow(VirtualClashGroup group, IList<ClashResult> visibleResults = null)
        {
            var groupName = string.IsNullOrWhiteSpace(group.Label)
                ? group.Side == ClashGroupingSide.ItemA ? "Группа A" : "Группа B"
                : GetUserClashGroupName(group.Label);
            var prefix = group.Side == ClashGroupingSide.ItemA
                ? "A"
                : group.Side == ClashGroupingSide.ItemB ? "B" : string.Empty;
            var resultsSource = visibleResults ?? group.Results;
            var results = resultsSource == null
                ? new List<ClashResult>()
                : resultsSource.Where(result => result != null).ToList();
            var groupItem = results
                .Select(result => ResolveClashGroupingAncestor(result, group.Side, group.Path))
                .FirstOrDefault(item => item != null);

            return new ClashResultGridRow
            {
                Status = "Гр.",
                Name = string.IsNullOrWhiteSpace(prefix)
                    ? $"{groupName} ({results.Count})"
                    : $"{prefix}: {groupName} ({results.Count})",
                GroupName = groupName,
                Distance = results.Count.ToString(CultureInfo.InvariantCulture) + " шт.",
                ItemA = group.Side == ClashGroupingSide.ItemA ? groupName : FormatDistinctClashSideNames(results, ClashGroupingSide.ItemA),
                ItemB = group.Side == ClashGroupingSide.ItemB ? groupName : FormatDistinctClashSideNames(results, ClashGroupingSide.ItemB),
                Result = results.FirstOrDefault(),
                Results = results,
                IsGroup = true,
                GroupItem = groupItem,
                GroupingSide = group.Side,
                VirtualGroupId = group.Id,
                PersistentGroup = group.PersistentGroup,
            };
        }

        private static string FormatDistinctClashSideNames(IEnumerable<ClashResult> results, ClashGroupingSide side)
        {
            return ClashGroupDisplayPolicy.FormatDistinctNames(
                results.Select(result => side == ClashGroupingSide.ItemA
                    ? GetClashItemName(result.Selection1)
                    : GetClashItemName(result.Selection2)));
        }

        private static ModelItem ResolveClashGroupingAncestor(ClashResult result, ClashGroupingSide side, string selectedPath)
        {
            if (result == null)
                return null;

            var item = side == ClashGroupingSide.ItemA
                ? ResolveClashSideSeed(result.Item1, result.Selection1)
                : ResolveClashSideSeed(result.Item2, result.Selection2);

            if (item == null)
                return null;

            if (!string.IsNullOrWhiteSpace(selectedPath))
                return ResolveClashGroupingAncestorByPath(item, selectedPath);

            var current = item;
            ModelItem nearestNamedComposite = null;
            ModelItem highestNamedComposite = null;
            while (current != null)
            {
                try
                {
                    var parent = current.Parent;
                    if (parent == null || parent.Parent == null)
                        break;

                    if (current.IsComposite && !string.IsNullOrWhiteSpace(current.DisplayName))
                    {
                        if (nearestNamedComposite == null)
                            nearestNamedComposite = current;
                        highestNamedComposite = current;
                    }

                    current = parent;
                }
                catch
                {
                    break;
                }
            }

            return highestNamedComposite ?? nearestNamedComposite ?? GetNamedAncestor(item);
        }

        private static ModelItem ResolveClashGroupingAncestorByPath(ModelItem item, string selectedPath)
        {
            if (item == null || string.IsNullOrWhiteSpace(selectedPath))
                return null;

            var entries = BuildModelItemPathEntries(item);
            var index = ClashGroupingPathPolicy.FindPathIndex(entries.Select(entry => entry.Path), selectedPath);
            return index < 0 ? null : entries[index].Item;
        }

        private static ModelItem ResolveClashSideSeed(ModelItem primaryItem, ModelItemCollection sideItems)
        {
            if (primaryItem != null)
                return primaryItem;

            if (sideItems == null || sideItems.Count == 0)
                return null;

            return sideItems.Cast<ModelItem>().FirstOrDefault(item => item != null);
        }

        private static List<ModelItemPathEntry> BuildModelItemPathEntries(ModelItem item)
        {
            var raw = CollectModelItemPath(item);
            var segments = ClashGroupingPathPolicy.BuildSegments(raw.Select(entry => entry.Item2));
            var entries = new List<ModelItemPathEntry>();
            for (var i = 0; i < raw.Count; i++)
            {
                entries.Add(new ModelItemPathEntry
                {
                    Item = raw[i].Item1,
                    Name = segments[i].Name,
                    Path = segments[i].Path,
                    Depth = segments[i].Depth
                });
            }

            return entries;
        }

        private static List<Tuple<ModelItem, string>> CollectModelItemPath(ModelItem item)
        {
            var raw = new List<Tuple<ModelItem, string>>();
            var current = item;
            while (current != null)
            {
                try
                {
                    var name = current.DisplayName;
                    if (!string.IsNullOrWhiteSpace(name))
                        raw.Add(Tuple.Create(current, name.Trim()));

                    current = current.Parent;
                }
                catch
                {
                    break;
                }
            }

            raw.Reverse();
            return raw;
        }

        private static ModelItem GetNamedAncestor(ModelItem item)
        {
            var current = item;
            while (current != null)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(current.DisplayName))
                        return current;

                    current = current.Parent;
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private static string BuildModelItemPath(ModelItem item)
        {
            if (item == null)
                return string.Empty;

            return ClashGroupingPathPolicy.BuildPath(
                CollectModelItemPath(item).Select(entry => entry.Item2));
        }

        private static string GetDisplayNameOrFallback(ModelItem item, string fallback)
        {
            if (item == null)
                return fallback;

            try
            {
                if (!string.IsNullOrWhiteSpace(item.DisplayName))
                    return item.DisplayName.Trim();
            }
            catch
            {
            }

            return fallback;
        }
    }
}
