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

using NavisHelper.Core.Localization;

namespace NavisHelper.WPF
{
    public partial class NavisHelperPanel : UserControl
    {
        private void ApplyCurrentClashPreviewSettings(bool usePreviewTransparency = true)
        {
            _clashMgr.ColorA = GetClashColor(_clashColorA);
            _clashMgr.ColorB = GetClashColor(_clashColorB);
            _clashMgr.OffsetMm = _clashOffsetSlider?.Value ?? 1000;
            _clashMgr.BoxMode = GetSelectedClashBoxMode();
            _clashMgr.UseSectionBox = _clashUseSectionBox?.IsChecked == true;
            _clashMgr.UseContextTransparency = usePreviewTransparency && _clashContextTrans?.IsChecked == true;
            _clashMgr.ContextTransparency = (_clashTransSlider?.Value ?? 70) / 100.0;
        }

        private static Autodesk.Navisworks.Api.FolderItem FindOrCreateSavedViewpointFolder(Document doc, string folderName)
        {
            if (doc == null || doc.SavedViewpoints == null || doc.SavedViewpoints.RootItem == null)
                throw new InvalidOperationException(
                    UiLocalizationService.Current.GetString("Panel_Clash_SavedViewpointsUnavailable"));

            folderName = NormalizeSavedItemName(folderName, "Clash Test");
            foreach (SavedItem item in doc.SavedViewpoints.RootItem.Children)
            {
                if (item.IsGroup && string.Equals(item.DisplayName, folderName, StringComparison.OrdinalIgnoreCase))
                    return item as Autodesk.Navisworks.Api.FolderItem;
            }

            var folder = new Autodesk.Navisworks.Api.FolderItem { DisplayName = folderName };
            doc.SavedViewpoints.AddCopy(folder);

            foreach (SavedItem item in doc.SavedViewpoints.RootItem.Children)
            {
                if (item.IsGroup && string.Equals(item.DisplayName, folderName, StringComparison.OrdinalIgnoreCase))
                    return item as Autodesk.Navisworks.Api.FolderItem;
            }

            throw new InvalidOperationException(
                UiLocalizationService.Current.Format(
                    "Panel_Clash_SavedViewpointFolderFailed_Format",
                    folderName));
        }

        private static string MakeUniqueSavedViewpointName(Autodesk.Navisworks.Api.FolderItem folder, string baseName)
        {
            return SavedItemNamePolicy.MakeUnique(
                baseName,
                "Conflict",
                candidate => SavedItemNameExists(folder, candidate),
                () => Guid.NewGuid().ToString("N").Substring(0, 8));
        }

        private static bool SavedItemNameExists(Autodesk.Navisworks.Api.FolderItem folder, string name)
        {
            if (folder == null || folder.Children == null)
                return false;

            foreach (SavedItem item in folder.Children)
            {
                if (string.Equals(item.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string NormalizeSavedItemName(string value, string fallback)
        {
            return SavedItemNamePolicy.Normalize(value, fallback);
        }

        private static string OperationLabel(string operation)
        {
            switch (operation)
            {
                case "run":
                    return UiLocalizationService.Current.GetString("Panel_Clash_Operation_Run");
                case "reset":
                    return UiLocalizationService.Current.GetString("Panel_Clash_Operation_Reset");
                case "compact":
                    return UiLocalizationService.Current.GetString("Panel_Clash_Operation_Compact");
                case "delete":
                    return UiLocalizationService.Current.GetString("Panel_Clash_Operation_Delete");
                default:
                    return operation;
            }
        }

        private List<ClashTestSelection> CaptureSelectedClashTestSelections()
        {
            var selections = new List<ClashTestSelection>();
            if (_testGrid != null)
            {
                foreach (var selected in _testGrid.SelectedItems)
                {
                    var row = selected as ClashTestRow;
                    if (row == null || string.IsNullOrWhiteSpace(row.Name) ||
                        selections.Any(item => item.TestIndex == row.TestIndex))
                    {
                        continue;
                    }

                    selections.Add(new ClashTestSelection { TestIndex = row.TestIndex, Name = row.Name });
                }
            }

            if (selections.Count == 0 && _activeClashTest != null)
            {
                var activeRow = (_allTestRows ?? new ClashTestRow[0])
                    .FirstOrDefault(row => row != null && object.ReferenceEquals(row.Test, _activeClashTest));
                if (activeRow != null && !string.IsNullOrWhiteSpace(activeRow.Name))
                    selections.Add(new ClashTestSelection { TestIndex = activeRow.TestIndex, Name = activeRow.Name });
            }

            return selections;
        }

        private void RefreshClashDataFromDocumentPreservingState(string reason)
        {
            if (_testGrid == null || _clashGrid == null)
                return;

            var selectedTests = CaptureSelectedClashTestSelections();
            var selectedClash = CaptureSelectedClashResult();
            try
            {
                LoadClashTestsCore(quiet: true);
                RestoreClashTestSelectionByIdentity(selectedTests);
                OnClashTestSelected();
                RestoreClashResultSelection(selectedClash);

                SetGlobalStatusResource(
                    "Panel_Clash_RefreshCompleted_Format",
                    Brushes.DarkGreen,
                    _loadedTests.Count);
            }
            catch (Exception ex)
            {
                SetGlobalStatusResource("Panel_Clash_RefreshFailed_Format", Brushes.Red, ex.Message);
                Logger.Error("Failed to refresh Clash UI from document: " + ex, "ClashUI");
            }
        }

        private void LoadClashTests()
        {
            try
            {
                LoadClashTestsCore(quiet: false);
            }
            catch (Exception ex)
            {
                SetGlobalStatusResource("Panel_Common_Error_Format", Brushes.Red, ex.Message);
            }
        }

        private void LoadClashTestsCore(bool quiet)
        {
            AttachCurrentClashTestsDataChanged();

            var doc = NwApplication.ActiveDocument;
            if (doc == null || doc.IsClear)
                throw new InvalidOperationException(PanelUi("Panel_Common_NoActiveDocument"));

            var clash = doc.GetClash();
            if (clash == null || clash.TestsData == null)
                throw new InvalidOperationException(PanelUi("Panel_Clash_EngineUnavailable"));

            _loadedTests = ClashApiCompat.GetClashTests(clash).ToList();

            var rows = _loadedTests.Select((t, index) =>
            {
                int total = 0, nw = 0, act = 0;
                AccumulateClashResultCounts(t.Children, ref total, ref nw, ref act);
                return new ClashTestRow { TestIndex = index, Name = t.DisplayName, Total = total, New = nw, Active = act, Test = t };
            }).ToList();

            _allTestRows = rows.ToArray();
            FilterTestGrid();
            if (!quiet)
                SetGlobalStatusResource("Panel_Clash_TestsLoaded_Format", Brushes.DarkGreen, _loadedTests.Count);
        }

        private void FilterTestGrid()
        {
            if (_allTestRows == null) return;
            var filter = (_testFilterBox?.Text ?? "").Trim().ToLower();
            if (string.IsNullOrEmpty(filter))
            {
                ReplaceDataGridItemsSourcePreservingSort(_testGrid, _allTestRows);
            }
            else
            {
                ReplaceDataGridItemsSourcePreservingSort(_testGrid, _allTestRows.Where(r =>
                {
                    return (r.Name ?? string.Empty).ToLower().Contains(filter);
                }).ToArray());
            }
        }

        private void OnClashTestSelected()
        {
            try
            {
                var row = _testGrid.SelectedItem as ClashTestRow;
                if (row == null) return;

                ClashTest test = row.Test;
                if (test == null) return;

                var refreshedAfterDisposedHandle = false;
                while (true)
                {
                    try
                    {
                        SaveActiveClashGroupsToCache();
                        InvalidateClashTreeMatchCache();
                        _loadedResults.Clear();
                        _clashVirtualGroupState.ClearActiveGroups();
                        LoadClashResultsFromChildren(test.Children);
                        _activeClashTest = test;
                        RestoreCachedClashGroups(test);
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (refreshedAfterDisposedHandle || !IsDisposedNativeHandleException(ex))
                            throw;

                        refreshedAfterDisposedHandle = true;
                        Logger.Error("Reloading Clash test after disposed native handle while selecting '" + row.Name + "': " + ex, "ClashUI");
                        var freshTest = ResolveCurrentClashTest(row.TestIndex, row.Name);
                        if (freshTest == null)
                            throw;

                        row.Test = freshTest;
                        test = freshTest;
                    }
                }

                RefreshClashGridRows();
            }
            catch (Exception ex)
            {
                SetGlobalStatusResource("Panel_Clash_ResultsLoadFailed_Format", Brushes.Red, ex.Message);
                Logger.Error("Failed to load Clash results for selected test: " + ex, "ClashUI");
            }
        }

        private static bool IsDisposedNativeHandleException(Exception ex)
        {
            while (ex != null)
            {
                var message = ex.Message ?? string.Empty;
                if (ex is ObjectDisposedException ||
                    message.IndexOf("Object has been Disposed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("NativeHandle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("WeakRef", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                ex = ex.InnerException;
            }

            return false;
        }

        private static ClashTest ResolveCurrentClashTest(int testIndex, string testName)
        {
            if (string.IsNullOrWhiteSpace(testName))
                return null;

            var doc = NwApplication.ActiveDocument;
            if (doc == null || doc.IsClear)
                return null;

            var clash = doc.GetClash();
            if (clash == null || clash.TestsData == null)
                return null;

            var tests = ClashApiCompat.GetClashTests(clash).ToList();
            if (testIndex >= 0 && testIndex < tests.Count)
            {
                var indexedTest = tests[testIndex];
                if (indexedTest != null && string.Equals(GetSafeClashTestDisplayName(indexedTest), testName, StringComparison.OrdinalIgnoreCase))
                    return indexedTest;
            }

            return tests.FirstOrDefault(test => test != null && string.Equals(GetSafeClashTestDisplayName(test), testName, StringComparison.OrdinalIgnoreCase));
        }

        private static void AccumulateClashResultCounts(IEnumerable<SavedItem> children, ref int total, ref int nw, ref int act)
        {
            if (children == null)
                return;

            foreach (var child in children)
            {
                var result = child as ClashResult;
                if (result != null)
                {
                    total++;
                    if (result.Status == ClashResultStatus.New) nw++;
                    if (result.Status == ClashResultStatus.Active) act++;
                    continue;
                }

                var group = child as ClashResultGroup;
                if (group != null)
                    AccumulateClashResultCounts(group.Children, ref total, ref nw, ref act);
            }
        }

        private void LoadClashResultsFromChildren(IEnumerable<SavedItem> children)
        {
            if (children == null)
                return;

            foreach (var child in children)
            {
                var result = child as ClashResult;
                if (result != null)
                {
                    _loadedResults.Add(result);
                    continue;
                }

                var group = child as ClashResultGroup;
                if (group == null)
                    continue;

                var groupResults = EnumerateClashResults(group.Children)
                    .Where(item => item != null)
                    .Distinct()
                    .ToList();
                foreach (var groupResult in groupResults)
                {
                    if (!_loadedResults.Any(item => object.ReferenceEquals(item, groupResult)))
                        _loadedResults.Add(groupResult);
                }

                if (groupResults.Count > 0)
                {
                    _clashVirtualGroupState.AddGroup(new VirtualClashGroup
                    {
                        Side = InferClashGroupingSideFromGroupName(group.DisplayName),
                        Path = string.Empty,
                        Label = string.IsNullOrWhiteSpace(group.DisplayName) ? "Clash Group" : GetUserClashGroupName(group.DisplayName),
                        Results = groupResults,
                        PersistentGroup = group
                    });
                }
            }
        }

        private void PreviewSelectedClash()
        {
            try
            {
                EnsureSelectedClashRowFreshForPreview();

                var row = _clashGrid.SelectedItem;
                if (row == null) { SetGlobalStatusResource("Panel_Clash_SelectResult", Brushes.Orange); return; }

                var results = GetClashResultsFromRow(row);
                if (results.Count == 0) return;

                string name = null;
                try
                {
                    dynamic dyn = row;
                    name = dyn.Name as string;
                }
                catch
                {
                }

                ApplyCurrentClashPreviewSettings();
                var gridRow = row as ClashResultGridRow;
                if (results.Count > 1)
                {
                    var groupItemIsA = gridRow != null && gridRow.GroupingSide != ClashGroupingSide.None
                        ? (bool?)(gridRow.GroupingSide == ClashGroupingSide.ItemA)
                        : null;
                    _clashMgr.ShowClashResults(results, name, gridRow?.GroupItem, groupItemIsA);
                }
                else
                    _clashMgr.ShowClashResult(results[0]);

                UiStatusResourceDescriptor previewStatus =
                    PreviewManagerUiStatusMapper.ForClashPreview(_clashMgr.LastUiOutcome);
                if (_clashMgr.LastSuccess && _clashMgr.UsePairIsolation)
                {
                    UiStatusResourceDescriptor isolationStatus =
                        PreviewManagerUiStatusMapper.ForPairIsolation(
                            _clashMgr.LastPairIsolationUiOutcome);
                    previewStatus = new UiStatusResourceDescriptor(
                        "Panel_Clash_Preview_OnlyPair_Format",
                        previewStatus.AsLocalizedArgument(),
                        isolationStatus.AsLocalizedArgument(),
                        _clashMgr.LastPairIsolationElapsedMilliseconds);
                }

                SetGlobalStatusResource(
                    previewStatus,
                    _clashMgr.LastSuccess ? Brushes.DarkGreen : Brushes.Red);

                // Сохраняем настройки
                SaveClashSettings();
            }
            catch (Exception ex)
            {
                SetGlobalStatusResource("Panel_Common_Error_Format", Brushes.Red, ex.Message);
            }
        }

        private void EnsureSelectedClashRowFreshForPreview()
        {
            var selectedRow = _clashGrid?.SelectedItem;
            var selectedResults = GetClashResultsFromRow(selectedRow);
            if (selectedResults.Count == 0)
                return;

            var testRow = _testGrid?.SelectedItem as ClashTestRow;
            var test = testRow?.Test;
            if (test == null)
                return;

            try
            {
                var currentResults = EnumerateClashResults(test.Children)
                    .Where(result => result != null)
                    .ToList();
                if (currentResults.Count == 0 ||
                    selectedResults.Any(result => !currentResults.Any(current => object.ReferenceEquals(current, result))))
                {
                    RefreshClashDataFromDocumentPreservingState("preview reload");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to validate Clash preview row freshness: " + ex, "ClashUI");
                RefreshClashDataFromDocumentPreservingState("preview reload");
            }
        }

        private void ScheduleClashPreviewRefresh()
        {
            if (_clashMgr == null || _clashMgr.LastExpandedBox == null || _clashGrid == null || _clashGrid.SelectedItem == null)
                return;

            if (_clashPreviewDebounceTimer == null)
            {
                _clashPreviewDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _clashPreviewDebounceTimer.Tick += (s, e) =>
                {
                    _clashPreviewDebounceTimer.Stop();
                    try
                    {
                        if (_clashPreviewRefreshGeneration != _clashDocumentGeneration)
                            return;
                        if (_clashGrid == null || _clashGrid.SelectedItem == null)
                            return;

                        PreviewSelectedClash();
                    }
                    catch
                    {
                    }
                };
            }

            _clashPreviewRefreshGeneration = _clashDocumentGeneration;
            _clashPreviewDebounceTimer.Stop();
            _clashPreviewDebounceTimer.Start();
        }











































        private void DrawSelectedClashCenterMarkers()
        {
            try
            {
                var doc = NwApplication.ActiveDocument;
                if (doc == null || doc.IsClear || doc.ActiveView == null)
                {
                    SetGlobalStatusResource("Panel_Common_NoActiveDocument", Brushes.Orange);
                    return;
                }

                var results = GetClashResultsFromRow(_clashGrid?.SelectedItem);
                if (results.Count == 0)
                {
                    SetGlobalStatusResource("Panel_Clash_SelectResultOrGroup", Brushes.Orange);
                    return;
                }

                var centers = GetClashCentersForRedlines(results, includeFallbackCenter: true);
                if (centers.Count == 0)
                {
                    SetGlobalStatusResource("Panel_Clash_Markers_NoPoints", Brushes.Orange);
                    return;
                }

                UiStatusResourceDescriptor debug;
                var drawn = ApplyClashCenterRedlines(doc, centers, out debug);
                SetGlobalStatusResource(
                    "Panel_Clash_Markers_Completed_Format",
                    drawn > 0 ? Brushes.DarkGreen : Brushes.Orange,
                    drawn,
                    centers.Count,
                    debug == null
                        ? (object)string.Empty
                        : debug.AsLocalizedArgument());
            }
            catch (Exception ex)
            {
                SetGlobalStatusResource("Panel_Clash_Markers_Failed_Format", Brushes.Red, ex.Message);
            }
        }

        private void SaveCurrentClashManualViewpoint()
        {
            try
            {
                var doc = NwApplication.ActiveDocument;
                if (doc == null || doc.IsClear || doc.CurrentViewpoint == null)
                {
                    SetGlobalStatusResource("Panel_Clash_Viewpoint_NoActiveView", Brushes.Orange);
                    return;
                }

                var selectedTest = _testGrid?.SelectedItem as ClashTestRow;
                var folderName = NormalizeSavedItemName(selectedTest?.Test?.DisplayName, "NavisHelper Clashes");
                var folder = FindOrCreateSavedViewpointFolder(doc, folderName);
                var baseName = BuildManualClashViewpointName();
                var viewpointName = MakeUniqueSavedViewpointName(folder, baseName);
                _clashMgr?.ClearPreviewTransparency();
                SavedViewpointAppearanceHelper.SaveCurrentViewWithAppearanceOverrides(doc, folder, folderName, viewpointName);
                SaveClashSettings();
                SetGlobalStatusResource(
                    "Panel_Clash_Viewpoint_Saved_Format",
                    Brushes.DarkGreen,
                    folderName,
                    viewpointName);
            }
            catch (Exception ex)
            {
                SetGlobalStatusResource("Panel_Clash_Viewpoint_Failed_Format", Brushes.Red, ex.Message);
            }
        }

        private string BuildManualClashViewpointName()
        {
            try
            {
                dynamic row = _clashGrid?.SelectedItem;
                if (row != null)
                {
                    bool isGroup = row.IsGroup;
                    string groupName = row.GroupName as string;
                    string name = isGroup && !string.IsNullOrWhiteSpace(groupName)
                        ? groupName
                        : row.Name as string;
                    if (!string.IsNullOrWhiteSpace(name))
                        return NormalizeSavedItemName(name, "Manual Clash View");
                }
            }
            catch
            {
            }

            return "Manual Clash View " + DateTime.Now.ToString("yyyyMMdd HHmmss", CultureInfo.InvariantCulture);
        }

        private List<Point3D> GetClashCentersForRedlines(IEnumerable<ClashResult> results, bool includeFallbackCenter)
        {
            var centers = new List<Point3D>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (results != null)
            {
                foreach (var result in results)
                {
                    if (result?.Center == null)
                        continue;

                    AddUniqueClashCenter(centers, seen, result.Center);
                }
            }

            if (centers.Count == 0 && includeFallbackCenter && _clashMgr?.LastClashCenter != null)
                AddUniqueClashCenter(centers, seen, _clashMgr.LastClashCenter);

            return centers;
        }

        private static void AddUniqueClashCenter(List<Point3D> centers, HashSet<string> seen, Point3D point)
        {
            if (centers == null || seen == null || point == null)
                return;

            var key = string.Format(CultureInfo.InvariantCulture, "{0:F4}|{1:F4}|{2:F4}", point.X, point.Y, point.Z);
            if (seen.Add(key))
                centers.Add(point);
        }

        private int ApplyClashCenterRedlines(
            Document doc,
            IList<Point3D> centers,
            out UiStatusResourceDescriptor debug)
        {
            debug = null;
            if (doc == null || doc.ActiveView == null || centers == null || centers.Count == 0)
                return 0;

            var view = doc.ActiveView;
            var values = new List<string>();
            var culture = CultureInfo.InvariantCulture;
            var radii = GetRedlineMarkerRadii(view, 14);
            var rx = radii[0];
            var ry = radii[1];
            var lineX = rx * 1.7;
            var lineY = ry * 1.7;
            var drawn = 0;

            foreach (var center in centers)
            {
                if (center == null)
                    continue;

                ProjectionResult projection = null;
                try { projection = view.ProjectPoint(center, false, false); }
                catch { }

                if (projection == null)
                    continue;

                Point2D cameraSpace;
                try { cameraSpace = LcOpRedline.ScreenToCameraSpace(view.Viewer, projection.X, projection.Y); }
                catch { continue; }

                if (!IsFinite(cameraSpace.X) || !IsFinite(cameraSpace.Y))
                    continue;

                values.Add(string.Format(culture,
                    "{{\"Type\":\"RedlineEllipse\",\"Version\":1,\"Thickness\":3,\"Color\":[1.0,0.0,0.0],\"MinPoint\":[{0},{1}],\"MaxPoint\":[{2},{3}]}}",
                    cameraSpace.X - rx, cameraSpace.Y - ry, cameraSpace.X + rx, cameraSpace.Y + ry));
                values.Add(string.Format(culture,
                    "{{\"Type\":\"RedlineLine\",\"Version\":1,\"Thickness\":2,\"Color\":[1,0,0],\"Start\":[{0},{1}],\"End\":[{2},{3}]}}",
                    cameraSpace.X - lineX, cameraSpace.Y, cameraSpace.X + lineX, cameraSpace.Y));
                values.Add(string.Format(culture,
                    "{{\"Type\":\"RedlineLine\",\"Version\":1,\"Thickness\":2,\"Color\":[1,0,0],\"Start\":[{0},{1}],\"End\":[{2},{3}]}}",
                    cameraSpace.X, cameraSpace.Y - lineY, cameraSpace.X, cameraSpace.Y + lineY));
                drawn++;
            }

            if (drawn == 0)
            {
                debug = new UiStatusResourceDescriptor(
                    "Panel_Clash_Markers_ProjectPointNull_Detail");
                return 0;
            }

            var builder = new StringBuilder();
            builder.Append("{\"Type\":\"RedlineCollection\",\"Version\":1,\"Values\":[");
            builder.Append(string.Join(",", values));
            builder.Append("]}");
            RedlineJsonSanitizer.SetSupportedRedlines(view, builder.ToString(), null);
            view.RequestDelayedRedraw(ViewRedrawRequests.All);
            debug = new UiStatusResourceDescriptor(
                "Panel_Clash_Markers_Debug_Detail_Format",
                drawn,
                centers.Count);
            return drawn;
        }

        private static double[] GetRedlineMarkerRadii(Autodesk.Navisworks.Api.View activeView, int pixels)
        {
            try
            {
                var centerX = Math.Max(activeView.Width / 2, 1);
                var centerY = Math.Max(activeView.Height / 2, 1);
                var rightX = Math.Min(centerX + 1, Math.Max(activeView.Width - 1, 1));
                var downY = Math.Min(centerY + 1, Math.Max(activeView.Height - 1, 1));

                var center = LcOpRedline.ScreenToCameraSpace(activeView.Viewer, centerX, centerY);
                var right = LcOpRedline.ScreenToCameraSpace(activeView.Viewer, rightX, centerY);
                var down = LcOpRedline.ScreenToCameraSpace(activeView.Viewer, centerX, downY);

                var unitX = Math.Abs(right.X - center.X);
                var unitY = Math.Abs(down.Y - center.Y);
                if (!IsFinite(unitX) || unitX < 1e-9)
                    unitX = 0.002;
                if (!IsFinite(unitY) || unitY < 1e-9)
                    unitY = 0.002;

                return new[] { unitX * pixels, unitY * pixels };
            }
            catch
            {
                return new[] { 0.03, 0.03 };
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void ClearActiveViewRedlines(Document doc)
        {
            try
            {
                if (doc?.ActiveView == null)
                    return;

                RedlineJsonSanitizer.SetSupportedRedlines(doc.ActiveView, "{\"Type\":\"RedlineCollection\",\"Version\":1,\"Values\":[]}", null);
                doc.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
            }
            catch { }
        }
    }
}
