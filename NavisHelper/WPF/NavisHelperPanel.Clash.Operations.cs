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
        private void RunSelectedClashTests()
        {
            ApplySelectedClashTestOperation("run");
        }

        private void RunAllClashTests()
        {
            if (RejectClashInteractiveBusy("Clash Test: run all"))
                return;

            var doc = NwApplication.ActiveDocument;
            if (doc == null || doc.IsClear)
            {
                SetGlobalStatus("Нет активного документа", Brushes.Orange);
                return;
            }

            var clash = doc.GetClash();
            if (clash == null || clash.TestsData == null)
            {
                SetGlobalStatus("Clash Detective недоступен", Brushes.Orange);
                return;
            }

            var selectedTests = CaptureSelectedClashTestSelections();
            var selectedClash = CaptureSelectedClashResult();

            var tests = ClashApiCompat.GetClashTests(clash).ToList();
            if (tests.Count == 0)
            {
                SetGlobalStatus("Clash Test: нет тестов для запуска", Brushes.Orange);
                return;
            }

            var affected = 0;
            var interactiveBusy = NavisHelper.Agent.AgentRuntime.BeginInteractiveOperation("Clash Test: run all");
            Progress progress = null;
            ScreenUpdateSuppressor screenUpdates = null;
            try
            {
                SetClashInteractiveControlsEnabled(false);
                progress = NwApplication.BeginProgress("Clash Test: запуск всех");
                screenUpdates = ScreenUpdateSuppressor.TryDisable();
                for (var i = 0; i < tests.Count; i++)
                {
                    if (progress.IsCanceled)
                        break;

                    var test = tests[i];
                    if (test == null)
                        continue;

                    ClashRunPreservationService.RunTestPreservingReviewState(clash.TestsData, test);
                    affected++;
                    progress.Update((double)(i + 1) / tests.Count);
                }
            }
            catch (Exception ex)
            {
                SetGlobalStatus("Ошибка запуска всех тестов: " + ex.Message, Brushes.Red);
                return;
            }
            finally
            {
                if (screenUpdates != null)
                    screenUpdates.Dispose();
                if (progress != null)
                    NwApplication.EndProgress();
                SetClashInteractiveControlsEnabled(true);
                interactiveBusy.Dispose();
            }

            RefreshClashResultsAfterTestOperation(selectedTests, selectedClash, false);
            SetGlobalStatus($"Clash Test: выполнено ({affected}/{tests.Count})", Brushes.DarkGreen);
        }

        private List<ClashTest> GetSelectedClashTests()
        {
            var tests = new List<ClashTest>();
            if (_testGrid == null)
                return tests;

            foreach (var selected in _testGrid.SelectedItems)
            {
                if (selected == null)
                    continue;

                var row = selected as ClashTestRow;
                var test = row?.Test;
                if (test != null && !tests.Any(existing => object.ReferenceEquals(existing, test)))
                    tests.Add(test);
            }

            if (tests.Count == 0 && _testGrid.SelectedItem != null)
            {
                var row = _testGrid.SelectedItem as ClashTestRow;
                var test = row?.Test;
                if (test != null)
                    tests.Add(test);
            }

            return tests;
        }

        private void DeleteZeroClashTests()
        {
            if (RejectClashInteractiveBusy("Clash Test: delete zero"))
                return;

            if (_allTestRows == null || _allTestRows.Length == 0)
                LoadClashTests();

            var zeroRows = (_allTestRows ?? new ClashTestRow[0])
                .Where(row => row != null && row.Test != null && row.Total == 0)
                .ToList();
            if (zeroRows.Count == 0)
            {
                SetGlobalStatus("Нулевых Clash Test не найдено", Brushes.DarkGreen);
                return;
            }

            var safeZeroRows = zeroRows
                .Where(row => IsClashTestRunAtLeastOnce(row.Test))
                .ToList();
            var skippedUnrunRows = zeroRows
                .Where(row => !IsClashTestRunAtLeastOnce(row.Test))
                .ToList();

            if (safeZeroRows.Count == 0)
            {
                var skippedPreview = FormatClashTestPreview(skippedUnrunRows.Select(row => row.Name), skippedUnrunRows.Count);
                MessageBox.Show(
                    $"Ничего не удалено.\n\nНайдено нулевых тестов: {zeroRows.Count}, но они похожи на незапущенные: у них нет LastRun.{skippedPreview}\n\n" +
                    "Сначала запустите проверки, затем повторите удаление нулевых тестов.",
                    "Нулевые тесты не удалены",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                SetGlobalStatus("Нулевые тесты не удалены: все найденные тесты не запускались", Brushes.Orange);
                return;
            }

            var preview = FormatClashTestPreview(safeZeroRows.Select(row => row.Name), safeZeroRows.Count);
            var skippedText = skippedUnrunRows.Count == 0
                ? string.Empty
                : $"\n\nПропущено незапущенных нулевых тестов: {skippedUnrunRows.Count}. Они не будут удалены.";
            var confirm = MessageBox.Show(
                $"Удалить только запущенные Clash Test с 0 результатами: {safeZeroRows.Count}?{preview}{skippedText}",
                "Удалить нулевые Clash Test",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;

            DeleteClashTests(safeZeroRows.Select(row => row.Test).ToList());
        }

        private static string FormatClashTestPreview(IEnumerable<string> names, int totalCount)
        {
            var previewNames = (names ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Take(10)
                .ToList();

            return previewNames.Count == 0
                ? string.Empty
                : "\n\n" + string.Join("\n", previewNames) + (totalCount > previewNames.Count ? "\n..." : string.Empty);
        }

        private static bool IsClashTestRunAtLeastOnce(ClashTest test)
        {
            if (test == null)
                return false;

            try
            {
                var lastRun = test.LastRun;
                return lastRun > new DateTime(2000, 1, 1) && lastRun < DateTime.Now.AddDays(1);
            }
            catch
            {
                return false;
            }
        }

        private int DeleteClashTests(IList<ClashTest> testsToDelete)
        {
            if (RejectClashInteractiveBusy("Clash Test: delete"))
                return 0;

            if (testsToDelete == null || testsToDelete.Count == 0)
            {
                SetGlobalStatus("Нет Clash Test для удаления", Brushes.Orange);
                return 0;
            }

            var doc = NwApplication.ActiveDocument;
            if (doc == null || doc.IsClear)
            {
                SetGlobalStatus("Нет активного документа", Brushes.Orange);
                return 0;
            }

            var clash = doc.GetClash();
            if (clash == null || clash.TestsData == null)
            {
                SetGlobalStatus("Clash Detective недоступен", Brushes.Orange);
                return 0;
            }

            var affected = 0;
            var interactiveBusy = NavisHelper.Agent.AgentRuntime.BeginInteractiveOperation("Clash Test: delete");
            Progress progress = null;
            try
            {
                SetClashInteractiveControlsEnabled(false);
                progress = NwApplication.BeginProgress("Clash Test: удаление");
                var locations = ResolveClashTestLocations(clash.TestsData, testsToDelete);
                var total = Math.Max(1, locations.Count);
                foreach (var group in locations.GroupBy(location => location.Parent))
                {
                    if (progress.IsCanceled)
                        break;

                    foreach (var location in group.OrderByDescending(item => item.Index))
                    {
                        if (progress.IsCanceled)
                            break;

                        RemoveClashTestAtLocation(clash.TestsData, location);
                        affected++;
                        progress.Update((double)affected / total);
                    }
                }
            }
            finally
            {
                if (progress != null)
                    NwApplication.EndProgress();
                SetClashInteractiveControlsEnabled(true);
                interactiveBusy.Dispose();
            }

            foreach (var key in testsToDelete.Select(GetClashTestCacheKey).Where(key => !string.IsNullOrWhiteSpace(key)).ToList())
                _clashVirtualGroupState.RemoveCachedGroups(key);

            _activeClashTest = null;
            InvalidateClashTreeMatchCache();
            _loadedResults.Clear();
            _clashVirtualGroupState.ClearActiveGroups();
            LoadClashTests();
            SetGlobalStatus($"Clash Test: удалено ({affected})", Brushes.DarkGreen);
            return affected;
        }

        private void ApplySelectedClashTestOperation(string operation)
        {
            if (RejectClashInteractiveBusy("Clash Test: " + OperationLabel(operation)))
                return;

            var selectedTests = GetSelectedClashTests();
            if (selectedTests.Count == 0)
            {
                SetGlobalStatus("Выберите Clash Test", Brushes.Orange);
                return;
            }

            var selectedTestIdentities = CaptureSelectedClashTestSelections();
            var selectedClash = CaptureSelectedClashResult();

            if (string.Equals(operation, "delete", StringComparison.OrdinalIgnoreCase))
            {
                var confirm = MessageBox.Show(
                    $"Удалить выбранные Clash Test: {selectedTests.Count}?",
                    "Удалить Clash Test",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (confirm != MessageBoxResult.Yes)
                    return;

                DeleteClashTests(selectedTests);
                return;
            }

            var doc = NwApplication.ActiveDocument;
            if (doc == null || doc.IsClear)
            {
                SetGlobalStatus("Нет активного документа", Brushes.Orange);
                return;
            }

            var clash = doc.GetClash();
            if (clash == null || clash.TestsData == null)
            {
                SetGlobalStatus("Clash Detective недоступен", Brushes.Orange);
                return;
            }

            var affected = 0;
            var interactiveBusy = NavisHelper.Agent.AgentRuntime.BeginInteractiveOperation("Clash Test: " + OperationLabel(operation));
            Progress operationProgress = null;
            ScreenUpdateSuppressor screenUpdates = null;
            try
            {
                SetClashInteractiveControlsEnabled(false);
                operationProgress = NwApplication.BeginProgress("Clash Test: " + OperationLabel(operation));
                if (string.Equals(operation, "run", StringComparison.OrdinalIgnoreCase))
                    screenUpdates = ScreenUpdateSuppressor.TryDisable();

                for (var i = 0; i < selectedTests.Count; i++)
                {
                    if (operationProgress.IsCanceled)
                        break;

                    var test = selectedTests[i];
                    if (test == null)
                        continue;

                    switch (operation)
                    {
                        case "run":
                            ClashRunPreservationService.RunTestPreservingReviewState(clash.TestsData, test);
                            break;
                        case "reset":
                            clash.TestsData.TestsClearResults(test);
                            break;
                        case "compact":
                            clash.TestsData.TestsCompactTest(test);
                            break;
                        default:
                            throw new InvalidOperationException("Неизвестная операция: " + operation);
                    }

                    affected++;
                    operationProgress.Update((double)(i + 1) / selectedTests.Count);
                }
            }
            finally
            {
                if (screenUpdates != null)
                    screenUpdates.Dispose();
                if (operationProgress != null)
                    NwApplication.EndProgress();
                SetClashInteractiveControlsEnabled(true);
                interactiveBusy.Dispose();
            }

            RefreshClashResultsAfterTestOperation(selectedTestIdentities, selectedClash, string.Equals(operation, "run", StringComparison.OrdinalIgnoreCase));
            SetGlobalStatus($"Clash Test: {OperationLabel(operation)} ({affected})", Brushes.DarkGreen);
        }

        private sealed class ClashResultSelection
        {
            public string Name { get; set; }
            public string ItemA { get; set; }
            public string ItemB { get; set; }
        }

        private sealed class ClashTestSelection
        {
            public int TestIndex { get; set; }
            public string Name { get; set; }
        }

        private ClashResultSelection CaptureSelectedClashResult()
        {
            try
            {
                dynamic row = _clashGrid?.SelectedItem;
                ClashResult result = row?.Result as ClashResult;
                if (result == null)
                    return null;

                return new ClashResultSelection
                {
                    Name = result.DisplayName ?? string.Empty,
                    ItemA = GetClashItemName(result.Selection1),
                    ItemB = GetClashItemName(result.Selection2),
                };
            }
            catch
            {
                return null;
            }
        }

        private void RefreshClashResultsAfterTestOperation(ICollection<ClashTestSelection> selectedTests, ClashResultSelection selectedClash, bool previewRestoredClash)
        {
            LoadClashTests();
            RestoreClashTestSelectionByIdentity(selectedTests);
            OnClashTestSelected();
            var restoredOriginalClash = RestoreClashResultSelection(selectedClash);

            if (previewRestoredClash && restoredOriginalClash && _clashGrid?.SelectedItem != null)
                PreviewSelectedClash();
        }

        private void RestoreClashTestSelection(ICollection<string> selectedTestNames)
        {
            if (_testGrid == null || selectedTestNames == null || selectedTestNames.Count == 0)
                return;

            var selectedNames = new HashSet<string>(selectedTestNames, StringComparer.OrdinalIgnoreCase);
            _suppressClashTestSelectionChanged = true;
            try
            {
                _testGrid.SelectedItems.Clear();

                ClashTestRow first = null;
                foreach (var item in _testGrid.Items)
                {
                    var row = item as ClashTestRow;
                    if (row == null || string.IsNullOrWhiteSpace(row.Name) || !selectedNames.Contains(row.Name))
                        continue;

                    _testGrid.SelectedItems.Add(row);
                    if (first == null)
                        first = row;
                }

                if (first == null)
                {
                    foreach (var item in _testGrid.Items)
                    {
                        first = item as ClashTestRow;
                        if (first != null)
                            break;
                    }
                }

                if (first != null)
                {
                    _testGrid.SelectedItem = first;
                    _testGrid.ScrollIntoView(first);
                }
            }
            finally
            {
                _suppressClashTestSelectionChanged = false;
            }
        }

        private void RestoreClashTestSelectionByIdentity(ICollection<ClashTestSelection> selectedTests)
        {
            if (_testGrid == null || selectedTests == null || selectedTests.Count == 0)
                return;

            _suppressClashTestSelectionChanged = true;
            try
            {
                _testGrid.SelectedItems.Clear();

                ClashTestRow first = null;
                foreach (var item in _testGrid.Items)
                {
                    var row = item as ClashTestRow;
                    if (row == null)
                        continue;

                    var matches = selectedTests.Any(selected =>
                        selected != null &&
                        selected.TestIndex == row.TestIndex &&
                        string.Equals(selected.Name ?? string.Empty, row.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                    if (!matches)
                        continue;

                    _testGrid.SelectedItems.Add(row);
                    if (first == null)
                        first = row;
                }

                if (first == null)
                {
                    RestoreClashTestSelection(selectedTests
                        .Select(selected => selected == null ? string.Empty : selected.Name)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .ToList());
                    return;
                }

                _testGrid.SelectedItem = first;
                _testGrid.ScrollIntoView(first);
            }
            finally
            {
                _suppressClashTestSelectionChanged = false;
            }
        }

        private bool RestoreClashResultSelection(ClashResultSelection selectedClash)
        {
            if (_clashGrid == null || _clashGrid.Items.Count == 0)
                return false;

            object fallback = null;
            object exact = null;

            foreach (var item in _clashGrid.Items)
            {
                if (fallback == null)
                    fallback = item;

                if (selectedClash == null)
                    continue;

                try
                {
                    dynamic row = item;
                    ClashResult result = row.Result as ClashResult;
                    if (result == null)
                        continue;

                    var sameName = string.Equals(result.DisplayName ?? string.Empty, selectedClash.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                    if (!sameName)
                        continue;

                    var sameA = string.Equals(GetClashItemName(result.Selection1), selectedClash.ItemA ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                    var sameB = string.Equals(GetClashItemName(result.Selection2), selectedClash.ItemB ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                    if (sameA && sameB)
                    {
                        exact = item;
                        break;
                    }

                    if (exact == null)
                        exact = item;
                }
                catch
                {
                }
            }

            var target = exact ?? fallback;
            if (target != null)
            {
                _clashGrid.SelectedItem = target;
                _clashGrid.ScrollIntoView(target);
            }

            return exact != null;
        }

        private static List<ClashTestLocation> ResolveClashTestLocations(DocumentClashTests testsData, IEnumerable<ClashTest> tests)
        {
            if (testsData == null || testsData.Value == null || testsData.Value.TestsRoot == null)
                throw new InvalidOperationException("Clash Detective tests root недоступен.");

            var allLocations = EnumerateClashTestLocations(testsData.Value.TestsRoot).ToList();
            var resolved = new List<ClashTestLocation>();
            foreach (var test in tests ?? Enumerable.Empty<ClashTest>())
            {
                ClashTestLocation location;
                if (TryResolveClashTestLocation(allLocations, test, out location) &&
                    !resolved.Any(item => object.ReferenceEquals(item.Parent, location.Parent) && item.Index == location.Index))
                {
                    resolved.Add(location);
                }
            }

            if (resolved.Count == 0)
                throw new InvalidOperationException("Не удалось найти выбранные Clash Test в дереве.");

            return resolved;
        }

        private static bool TryResolveClashTestLocation(IList<ClashTestLocation> locations, ClashTest target, out ClashTestLocation location)
        {
            location = null;
            if (locations == null || target == null)
                return false;

            location = locations.FirstOrDefault(candidate => object.ReferenceEquals(candidate.Test, target));
            if (location != null)
                return true;

            try
            {
                if (target.Guid != Guid.Empty)
                {
                    var guidMatches = locations
                        .Where(candidate => candidate.Test != null && candidate.Test.Guid == target.Guid)
                        .ToList();
                    if (guidMatches.Count == 1)
                    {
                        location = guidMatches[0];
                        return true;
                    }
                }
            }
            catch
            {
            }

            var name = target.DisplayName ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(name))
            {
                var nameMatches = locations
                    .Where(candidate => string.Equals(candidate.Test == null ? string.Empty : candidate.Test.DisplayName ?? string.Empty, name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (nameMatches.Count == 1)
                {
                    location = nameMatches[0];
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<ClashTestLocation> EnumerateClashTestLocations(Autodesk.Navisworks.Api.GroupItem parent)
        {
            if (parent == null || parent.Children == null)
                yield break;

            for (var i = 0; i < parent.Children.Count; i++)
            {
                var child = parent.Children[i];
                var test = child as ClashTest;
                if (test != null)
                {
                    yield return new ClashTestLocation
                    {
                        Parent = parent,
                        Index = i,
                        Test = test
                    };
                    continue;
                }

                var childGroup = child as Autodesk.Navisworks.Api.GroupItem;
                if (childGroup == null)
                    continue;

                foreach (var nested in EnumerateClashTestLocations(childGroup))
                    yield return nested;
            }
        }

        private static void RemoveClashTestAtLocation(DocumentClashTests testsData, ClashTestLocation location)
        {
            if (testsData == null || testsData.Value == null || testsData.Value.TestsRoot == null || location == null || location.Parent == null)
                throw new InvalidOperationException("Clash Detective tests root недоступен.");

            Exception removeError;
            try
            {
                testsData.TestsRemoveAt(location.Parent, location.Index);
                return;
            }
            catch (Exception ex)
            {
                if (!object.ReferenceEquals(location.Parent, testsData.Value.TestsRoot))
                    throw;

                removeError = ex;
            }

            var oneArgumentRemove = testsData.GetType().GetMethod(
                "TestsRemoveAt",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(int) },
                null);
            if (oneArgumentRemove == null)
                throw removeError;

            oneArgumentRemove.Invoke(testsData, new object[] { location.Index });
        }

        private void RenameSelectedClashTest()
        {
            var selectedTests = GetSelectedClashTests();
            if (selectedTests.Count != 1)
            {
                SetGlobalStatus("Переименование доступно для одного Clash Test", Brushes.Orange);
                return;
            }

            var test = selectedTests[0];
            var currentName = test.DisplayName ?? string.Empty;
            var nextName = Interaction.InputBox("Новое имя проверки:", "Переименовать Clash Test", currentName);
            if (string.IsNullOrWhiteSpace(nextName) || string.Equals(currentName, nextName.Trim(), StringComparison.Ordinal))
                return;

            var doc = NwApplication.ActiveDocument;
            if (doc == null || doc.IsClear)
            {
                SetGlobalStatus("Нет активного документа", Brushes.Orange);
                return;
            }

            var clash = doc.GetClash();
            if (clash == null || clash.TestsData == null)
            {
                SetGlobalStatus("Clash Detective недоступен", Brushes.Orange);
                return;
            }

            clash.TestsData.TestsEditDisplayName(test, nextName.Trim());
            LoadClashTests();
            SetGlobalStatus("Clash Test переименован", Brushes.DarkGreen);
        }

        private void CreateViewpointsForSelectedClashTests()
        {
            var selectedTests = GetSelectedClashTests();
            if (selectedTests.Count == 0)
            {
                SetGlobalStatus("Выберите Clash Test", Brushes.Orange);
                return;
            }

            var jobs = new List<ClashViewpointJob>();
            foreach (var test in selectedTests)
            {
                if (test == null)
                    continue;

                var folderName = NormalizeSavedItemName(test.DisplayName, "Clash Test");
                jobs.AddRange(BuildClashViewpointJobsForTest(test, folderName));
            }

            CreateClashViewpoints(jobs);
        }

        private static IEnumerable<ClashViewpointJob> BuildClashViewpointJobsForTest(ClashTest test, string folderName)
        {
            if (test == null || test.Children == null)
                yield break;

            foreach (var child in test.Children
                .Cast<SavedItem>()
                .OrderBy(item => item.DisplayName ?? string.Empty, NaturalStringComparer.Instance))
            {
                var result = child as ClashResult;
                if (result != null)
                {
                    yield return new ClashViewpointJob
                    {
                        FolderName = folderName,
                        Result = result,
                        DisplayName = result.DisplayName
                    };
                    continue;
                }

                var group = child as ClashResultGroup;
                if (group == null)
                    continue;

                var groupResults = EnumerateClashResults(group.Children)
                    .OrderBy(item => item.DisplayName ?? string.Empty, NaturalStringComparer.Instance)
                    .ToList();
                if (groupResults.Count == 0)
                    continue;

                yield return new ClashViewpointJob
                {
                    FolderName = folderName,
                    Result = groupResults.FirstOrDefault(),
                    Results = groupResults,
                    DisplayName = GetUserClashGroupName(group.DisplayName)
                };
            }
        }

        private void CreateViewpointsForSelectedClashResults()
        {
            var rows = GetSelectedClashResultRows();
            if (rows.Count == 0)
            {
                SetGlobalStatus("Выберите коллизии", Brushes.Orange);
                return;
            }

            var selectedTest = _testGrid?.SelectedItem as ClashTestRow;
            var folderName = NormalizeSavedItemName(selectedTest?.Test?.DisplayName, "Clash Test");
            var jobs = new List<ClashViewpointJob>();
            foreach (var row in rows.OrderBy(row => row.Name ?? row.Result?.DisplayName ?? string.Empty, NaturalStringComparer.Instance))
            {
                var results = row.Results ?? (row.Result == null ? new List<ClashResult>() : new List<ClashResult> { row.Result });
                if (results.Count > 0)
                    jobs.Add(new ClashViewpointJob { FolderName = folderName, Result = results.FirstOrDefault(), Results = results, DisplayName = row.Name });
            }

            CreateClashViewpoints(jobs);
        }

        private sealed class ClashViewpointJob
        {
            public string FolderName { get; set; }
            public ClashResult Result { get; set; }
            public List<ClashResult> Results { get; set; }
            public string DisplayName { get; set; }
        }

        private sealed class ClashResultGridRowInfo
        {
            public string Name { get; set; }
            public ClashResult Result { get; set; }
            public List<ClashResult> Results { get; set; }
        }

        private List<ClashResultGridRowInfo> GetSelectedClashResultRows()
        {
            var rows = new List<ClashResultGridRowInfo>();
            if (_clashGrid == null)
                return rows;

            foreach (var selected in _clashGrid.SelectedItems)
                AddClashResultGridRow(rows, selected);

            if (rows.Count == 0)
                AddClashResultGridRow(rows, _clashContextMenuItem ?? _clashGrid.SelectedItem);

            return rows;
        }

        private static void AddClashResultGridRow(ICollection<ClashResultGridRowInfo> rows, object rowObject)
        {
            if (rows == null || rowObject == null)
                return;

            try
            {
                var results = GetClashResultsFromRow(rowObject);
                if (results.Count == 0)
                    return;

                var first = results.First();
                if (rows.Any(existing => existing.Results != null && existing.Results.Any(result => object.ReferenceEquals(result, first))))
                    return;

                string name = null;
                try
                {
                    dynamic row = rowObject;
                    bool isGroup = row.IsGroup;
                    string groupName = row.GroupName as string;
                    name = isGroup && !string.IsNullOrWhiteSpace(groupName)
                        ? groupName
                        : row.Name as string;
                }
                catch
                {
                }

                rows.Add(new ClashResultGridRowInfo
                {
                    Name = name,
                    Result = first,
                    Results = results,
                });
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось прочитать строку коллизии: " + ex, "ClashViewpoints");
            }
        }

        private static IEnumerable<ClashResult> EnumerateClashResults(IEnumerable<SavedItem> children)
        {
            if (children == null)
                yield break;

            foreach (var child in children)
            {
                var result = child as ClashResult;
                if (result != null)
                {
                    yield return result;
                    continue;
                }

                var group = child as Autodesk.Navisworks.Api.GroupItem;
                if (group == null)
                    continue;

                foreach (var nested in EnumerateClashResults(group.Children))
                    yield return nested;
            }
        }

        private void CreateClashViewpoints(IList<ClashViewpointJob> jobs)
        {
            if (RejectClashInteractiveBusy("Create Clash viewpoints"))
                return;

            if (_clashViewpointBatchBusy)
            {
                SetGlobalStatus("Создание VP уже выполняется", Brushes.Orange);
                Logger.Info("Ignored Clash VP batch start because another batch is already running.", "ClashViewpoints");
                return;
            }

            if (jobs == null || jobs.Count == 0)
            {
                SetGlobalStatus("Нет коллизий для создания VP", Brushes.Orange);
                return;
            }

            var doc = NwApplication.ActiveDocument;
            if (doc == null || doc.IsClear)
            {
                SetGlobalStatus("Нет активного документа", Brushes.Orange);
                return;
            }

            SaveClashSettings();
            ApplyCurrentClashPreviewSettings(usePreviewTransparency: false);
            var originalViewpoint = doc.CurrentViewpoint == null ? null : doc.CurrentViewpoint.CreateCopy();
            var folderCache = new Dictionary<string, Autodesk.Navisworks.Api.FolderItem>(StringComparer.OrdinalIgnoreCase);

            var previousCursor = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            _clashViewpointBatchBusy = true;
            var interactiveBusy = NavisHelper.Agent.AgentRuntime.BeginInteractiveOperation("Create Clash viewpoints");
            SetClashInteractiveControlsEnabled(false);
            SetGlobalBusy(true, $"Создание VP: 0 / {jobs.Count}");
            PumpDispatcherOnce();

            var created = 0;
            var failed = 0;
            var resetCreated = 0;
            var batchStopwatch = System.Diagnostics.Stopwatch.StartNew();
            long resetMs = 0;
            long previewMs = 0;
            long markerMs = 0;
            long saveMs = 0;
            long restoreMs = 0;
            var errorSummaries = new List<string>();
            var createResetViewpoint = true;
            var createTwoViewpointsSetting = _clashDualViewpoints?.IsChecked == true;
            var drawMarkersSetting = _clashGroupMarkersForViewpoints?.IsChecked == true;
            var captureAppearanceEffective = true;
            Progress progress = null;
            try
            {
                progress = NwApplication.BeginProgress("Создание Clash viewpoints");
                resetMs += MeasureElapsedMilliseconds(() =>
                {
                    _clashMgr.ResetView();
                    if (originalViewpoint != null && doc.CurrentViewpoint != null)
                        doc.CurrentViewpoint.CopyFrom(originalViewpoint);
                });

                Logger.Info(
                    $"Start Clash VP batch: jobs={jobs.Count}; reset={createResetViewpoint}; two={createTwoViewpointsSetting}; markers={drawMarkersSetting}; captureAttrs=True; transparency=False; section={_clashUseSectionBox?.IsChecked == true}",
                    "ClashViewpoints");

                foreach (var folderName in jobs
                    .Where(job => job != null && !string.IsNullOrWhiteSpace(job.FolderName))
                    .Select(job => job.FolderName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, NaturalStringComparer.Instance))
                {
                    var folder = FindOrCreateSavedViewpointFolder(doc, folderName);
                    if (createResetViewpoint)
                    {
                        SetGlobalBusy(true, $"Создание VP reset: {folderName}");
                        PumpDispatcherOnce();
                        var resetName = MakeUniqueSavedViewpointName(folder, "0000 Базовый вид");
                        resetMs += MeasureElapsedMilliseconds(() =>
                            SavedViewpointAppearanceHelper.SaveCurrentViewpointOnlyAtStart(doc, folder, resetName));
                        resetCreated++;
                    }
                    folderCache[folderName] = FindOrCreateSavedViewpointFolder(doc, folderName);
                }

                _clashMgr.UseFixedIsoView = true;
                for (var i = 0; i < jobs.Count; i++)
                {
                    if (progress.IsCanceled)
                        break;

                    var job = jobs[i];
                    var jobResults = job?.Results ?? (job?.Result == null ? new List<ClashResult>() : new List<ClashResult> { job.Result });
                    if (job == null || jobResults.Count == 0)
                        continue;

                    try
                    {
                        SetGlobalBusy(true, $"VP {i + 1} / {jobs.Count}: превью");
                        PumpDispatcherOnce();
                        previewMs += MeasureElapsedMilliseconds(() =>
                        {
                            ApplyCurrentClashPreviewSettings(usePreviewTransparency: false);
                            if (jobResults.Count > 1)
                                _clashMgr.ShowClashResults(jobResults, job.DisplayName);
                            else
                                _clashMgr.ShowClashResult(jobResults[0]);
                        });
                        if (!_clashMgr.LastSuccess)
                            throw new InvalidOperationException(_clashMgr.LastStatus);

                        Autodesk.Navisworks.Api.FolderItem folder;
                        if (!folderCache.TryGetValue(job.FolderName, out folder))
                        {
                            folder = FindOrCreateSavedViewpointFolder(doc, job.FolderName);
                            folderCache[job.FolderName] = folder;
                        }

                        var baseName = NormalizeSavedItemName(job.DisplayName ?? jobResults[0].DisplayName, "Conflict");
                        var createTwoViewpoints = createTwoViewpointsSetting;
                        var firstName = createTwoViewpoints ? baseName + " (1)" : baseName;
                        var viewpointName = MakeUniqueSavedViewpointName(folder, firstName);
                        if (drawMarkersSetting)
                        {
                            SetGlobalBusy(true, $"VP {i + 1} / {jobs.Count}: метки");
                            PumpDispatcherOnce();
                            markerMs += MeasureElapsedMilliseconds(() =>
                            {
                                string markerDebug;
                                ApplyClashCenterRedlines(doc, GetClashCentersForRedlines(jobResults, includeFallbackCenter: true), out markerDebug);
                            });
                        }
                        else
                        {
                            markerMs += MeasureElapsedMilliseconds(() => ClearActiveViewRedlines(doc));
                        }

                        SetGlobalBusy(true, $"VP {i + 1} / {jobs.Count}: сохранение");
                        PumpDispatcherOnce();
                        saveMs += MeasureElapsedMilliseconds(() =>
                            SaveCurrentClashBatchViewpoint(doc, folder, job.FolderName, viewpointName, captureAppearanceEffective, drawMarkersSetting));
                        ClearActiveViewRedlines(doc);
                        created++;

                        if (createTwoViewpoints && _clashMgr.LastExpandedBox != null)
                        {
                            previewMs += MeasureElapsedMilliseconds(() =>
                            {
                                ViewpointCameraHelper.ApplyIsoOppositeViewToBox(doc, _clashMgr.LastExpandedBox, _clashMgr.LastClashCenter);
                                SectionBoxHelper.SetSectionBox(_clashMgr.LastExpandedBox);
                            });

                            var secondName = MakeUniqueSavedViewpointName(folder, baseName + " (2)");
                            if (drawMarkersSetting)
                            {
                                SetGlobalBusy(true, $"VP {i + 1} / {jobs.Count}: метки 2");
                                PumpDispatcherOnce();
                                markerMs += MeasureElapsedMilliseconds(() =>
                                {
                                    string markerDebug;
                                    ApplyClashCenterRedlines(doc, GetClashCentersForRedlines(jobResults, includeFallbackCenter: true), out markerDebug);
                                });
                            }
                            else
                            {
                                markerMs += MeasureElapsedMilliseconds(() => ClearActiveViewRedlines(doc));
                            }

                            SetGlobalBusy(true, $"VP {i + 1} / {jobs.Count}: сохранение 2");
                            PumpDispatcherOnce();
                            saveMs += MeasureElapsedMilliseconds(() =>
                                SaveCurrentClashBatchViewpoint(doc, folder, job.FolderName, secondName, captureAppearanceEffective, drawMarkersSetting));
                            ClearActiveViewRedlines(doc);
                            created++;
                        }
                    }
                    catch (Exception ex)
                    {
                        ClearActiveViewRedlines(doc);
                        failed++;
                        var errorSummary = BuildClashViewpointErrorSummary(i + 1, jobs.Count, job, ex);
                        if (errorSummaries.Count < 5)
                            errorSummaries.Add(errorSummary);
                        Logger.Error(errorSummary, "ClashViewpoints");
                        SetGlobalStatus("VP ошибка: " + errorSummary, Brushes.Orange);
                    }

                    var ratio = (double)(i + 1) / jobs.Count;
                    progress.Update(ratio);
                    SetGlobalBusy(true, $"Создание VP: {i + 1} / {jobs.Count}");
                    PumpDispatcherOnce();
                }
            }
            finally
            {
                if (progress != null)
                    NwApplication.EndProgress();
                _clashMgr.UseFixedIsoView = false;
                try
                {
                    restoreMs += MeasureElapsedMilliseconds(() =>
                    {
                        _clashMgr.ResetView();
                        if (originalViewpoint != null && doc.CurrentViewpoint != null)
                            doc.CurrentViewpoint.CopyFrom(originalViewpoint);
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to restore view after Clash VP batch: " + ex, "ClashViewpoints");
                }

                _clashViewpointBatchBusy = false;
                Mouse.OverrideCursor = previousCursor;
                SetGlobalBusy(false);
                SetClashInteractiveControlsEnabled(true);
                interactiveBusy.Dispose();
            }

            var message = $"Создано VP: {created}";
            if (failed > 0)
                message += $", ошибок: {failed}";
            if (resetCreated > 0)
                message += $", reset: {resetCreated}";
            message += $", прошло: {FormatElapsedHuman(batchStopwatch.Elapsed)}";
            message += $" | reset {FormatElapsedHuman(TimeSpan.FromMilliseconds(resetMs))}";
            message += $", превью {FormatElapsedHuman(TimeSpan.FromMilliseconds(previewMs))}";
            message += $", метки {FormatElapsedHuman(TimeSpan.FromMilliseconds(markerMs))}";
            message += $", save {FormatElapsedHuman(TimeSpan.FromMilliseconds(saveMs))}";
            message += $", restore {FormatElapsedHuman(TimeSpan.FromMilliseconds(restoreMs))}";
            if (errorSummaries.Count > 0)
                message += " | первые ошибки: " + string.Join(" | ", errorSummaries);
            Logger.Info(message, "ClashViewpoints");
            SetGlobalStatus(message, failed == 0 ? Brushes.DarkGreen : Brushes.Orange);
        }

        private static string BuildClashViewpointErrorSummary(int index, int total, ClashViewpointJob job, Exception ex)
        {
            var name = NormalizeSavedItemName(job?.DisplayName ?? job?.Result?.DisplayName, "Conflict");
            var message = ex == null ? "unknown error" : ex.Message;
            return $"#{index}/{total} {name}: {TrimStatusText(message, 180)}";
        }

        private static string TrimStatusText(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var clean = value.Replace("\r\n", " ").Replace("\n", " ").Trim();
            if (maxLength <= 3 || clean.Length <= maxLength)
                return clean;

            return clean.Substring(0, maxLength - 3).TrimEnd() + "...";
        }

        private static long MeasureElapsedMilliseconds(Action action)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            action();
            stopwatch.Stop();
            return stopwatch.ElapsedMilliseconds;
        }

        private static string FormatElapsedHuman(TimeSpan elapsed)
        {
            if (elapsed.TotalMilliseconds < 1)
                return "0 сек";

            if (elapsed.TotalHours >= 1)
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} ч {1} мин {2} сек",
                    (int)elapsed.TotalHours,
                    elapsed.Minutes,
                    elapsed.Seconds);

            if (elapsed.TotalMinutes >= 1)
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} мин {1} сек",
                    (int)elapsed.TotalMinutes,
                    elapsed.Seconds);

            return string.Format(CultureInfo.InvariantCulture, "{0:0.0} сек", Math.Max(0, elapsed.TotalSeconds));
        }

        private static SavedViewpoint SaveCurrentClashBatchViewpoint(
            Document doc,
            Autodesk.Navisworks.Api.FolderItem folder,
            string folderName,
            string viewpointName,
            bool captureAppearance,
            bool captureRedlines)
        {
            if (captureAppearance || captureRedlines)
            {
                return SavedViewpointAppearanceHelper.SaveCurrentViewWithAppearanceOverrides(
                    doc,
                    folder,
                    folderName,
                    viewpointName,
                    captureAppearance,
                    captureAppearance);
            }

            return SavedViewpointAppearanceHelper.SaveCurrentViewpointOnly(doc, folder, viewpointName);
        }
    }
}
