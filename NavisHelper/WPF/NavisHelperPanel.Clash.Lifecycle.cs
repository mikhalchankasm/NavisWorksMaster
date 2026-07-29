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
        private bool _localClashMutationRefreshPending;

        private void AttachClashUiDocumentEvents()
        {
            try
            {
                if (!_clashApplicationEventsAttached)
                {
                    NwApplication.ActiveDocumentChanged += OnClashUiActiveDocumentChanged;
                    _clashApplicationEventsAttached = true;
                }

                AttachCurrentClashTestsDataChanged();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to attach Clash UI document events: " + ex, "ClashUI");
            }
        }

        private void DetachClashUiDocumentEvents()
        {
            try
            {
                if (_clashApplicationEventsAttached)
                {
                    NwApplication.ActiveDocumentChanged -= OnClashUiActiveDocumentChanged;
                    _clashApplicationEventsAttached = false;
                }

                DetachCurrentClashTestsDataChanged();
                if (_clashDataChangedDebounceTimer != null)
                    _clashDataChangedDebounceTimer.Stop();
            }
            catch
            {
            }
        }

        private void OnClashUiActiveDocumentChanged(object sender, EventArgs e)
        {
            Action resetForDocumentChange = () =>
            {
                _clashDocumentGeneration++;
                ResetClashUiForDocumentChange();
            };

            if (Dispatcher.CheckAccess())
                resetForDocumentChange();
            else
                Dispatcher.Invoke(resetForDocumentChange);

            BeginInvokeForCurrentClashDocument(() =>
            {
                AttachCurrentClashTestsDataChanged();
                ScheduleClashDataRefresh("active document changed");
            }, DispatcherPriority.Background);
        }

        private void ResetClashUiForDocumentChange()
        {
            // Clear managed references first. Native wrappers from the outgoing
            // document may already be disposed when ActiveDocumentChanged fires.
            _pendingClashDataRefreshReason = null;
            _localClashMutationRefreshPending = false;
            _clashContextMenuItem = null;
            _activeClashTest = null;
            _allTestRows = new ClashTestRow[0];
            _loadedTests.Clear();
            _loadedResults.Clear();
            _clashVirtualGroupState.ClearAll();
            InvalidateClashTreeMatchCache();
            _clashGroupingSide = ClashGroupingSide.None;
            _clashGroupingPath = null;
            _clashGroupingLabel = null;

            try { DetachCurrentClashTestsDataChanged(); }
            catch (Exception ex) { Logger.Error("Failed to detach Clash events during document switch: " + ex.Message, "ClashUI"); }
            if (_clashDataChangedDebounceTimer != null)
                _clashDataChangedDebounceTimer.Stop();
            if (_clashPreviewDebounceTimer != null)
                _clashPreviewDebounceTimer.Stop();

            try { ClearClashPairIsolationForLifecycle(); }
            catch (Exception ex) { Logger.Error("Failed to clear Clash pair isolation during document switch: " + ex.Message, "ClashUI"); }
            try { _clashMgr?.ForgetDocumentState(); }
            catch (Exception ex) { Logger.Error("Failed to forget Clash preview state during document switch: " + ex.Message, "ClashUI"); }
            try { ClashMarkerTool.Hide(); } catch { }
            try { SelectionPlaneTool.Hide(); } catch { }

            _suppressClashTestSelectionChanged = true;
            _suppressClashResultSelectionChanged = true;
            _suppressClashTreeSelectionChanged = true;
            try
            {
                ResetPendingClashGroupingSelection();
                if (_testGrid != null)
                {
                    _testGrid.SelectedItems.Clear();
                    _testGrid.ItemsSource = null;
                }
                if (_clashGrid != null)
                {
                    _clashGrid.SelectedItems.Clear();
                    _clashGrid.ItemsSource = null;
                }
                _clashTreeA?.Items.Clear();
                _clashTreeB?.Items.Clear();
                if (_clashGroupContentsGrid != null)
                    _clashGroupContentsGrid.ItemsSource = null;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to clear Clash controls during document switch: " + ex.Message, "ClashUI");
                try { if (_testGrid != null) _testGrid.ItemsSource = null; } catch { }
                try { if (_clashGrid != null) _clashGrid.ItemsSource = null; } catch { }
            }
            finally
            {
                _suppressClashTreeSelectionChanged = false;
                _suppressClashResultSelectionChanged = false;
                _suppressClashTestSelectionChanged = false;
            }

            if (_clashGroupContentsStatus != null)
                _clashGroupContentsStatus.Text = PanelUi("Panel_Clash_SelectResultOrGroup");
            if (_clashGroupingStatus != null)
                _clashGroupingStatus.Text = PanelUi("Panel_Clash_GroupingNone");
            try { SetClashGroupPanelVisible(false, save: false); } catch { }
            try { if (_clashSettingsToggle != null) _clashSettingsToggle.IsChecked = false; } catch { }
            try { SetGlobalStatusResource("Panel_Clash_DocumentChanged", Brushes.Gray); } catch { }
        }

        private void BeginInvokeForCurrentClashDocument(Action action, DispatcherPriority priority)
        {
            var generation = _clashDocumentGeneration;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation != _clashDocumentGeneration)
                    return;
                action();
            }), priority);
        }

        private void AttachCurrentClashTestsDataChanged()
        {
            DetachCurrentClashTestsDataChanged();

            var doc = NwApplication.ActiveDocument;
            if (doc == null || doc.IsClear)
                return;

            try
            {
                var clash = doc.GetClash();
                var testsData = clash == null ? null : clash.TestsData;
                if (testsData == null)
                    return;

                testsData.Changed += OnClashTestsDataChanged;
                _clashEventDocument = doc;
                _clashEventTestsData = testsData;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to attach Clash TestsData.Changed: " + ex, "ClashUI");
            }
        }

        private void DetachCurrentClashTestsDataChanged()
        {
            if (_clashEventTestsData != null)
            {
                try
                {
                    _clashEventTestsData.Changed -= OnClashTestsDataChanged;
                }
                catch
                {
                }
            }

            _clashEventTestsData = null;
            _clashEventDocument = null;
        }

        private void OnClashTestsDataChanged(object sender, SavedItemChangedEventArgs e)
        {
            if (_localClashMutationRefreshPending || !object.ReferenceEquals(sender, _clashEventTestsData))
                return;

            var action = e == null ? "changed" : e.Action.ToString();
            BeginInvokeForCurrentClashDocument(() => ScheduleClashDataRefresh("tests data " + action), DispatcherPriority.Background);
        }

        private void ScheduleClashDataRefresh(string reason)
        {
            if (_testGrid == null || _clashGrid == null)
                return;

            _pendingClashDataRefreshReason = reason;
            _clashDataRefreshGeneration = _clashDocumentGeneration;
            if (_clashDataChangedDebounceTimer == null)
            {
                _clashDataChangedDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                _clashDataChangedDebounceTimer.Tick += (s, e) =>
                {
                    _clashDataChangedDebounceTimer.Stop();
                    if (_clashDataRefreshGeneration != _clashDocumentGeneration)
                        return;
                    RefreshClashDataFromDocumentPreservingState(_pendingClashDataRefreshReason);
                };
            }

            _clashDataChangedDebounceTimer.Stop();
            _clashDataChangedDebounceTimer.Start();
        }

        private void SetClashAssignedToPrompt()
        {
            try
            {
                var clashes = GetSelectedClashResults();
                var clash = clashes.FirstOrDefault();
                if (clash == null)
                {
                    MessageBox.Show(
                        PanelUi("Panel_Clash_SelectResultOrGroup"),
                        PanelUi("Panel_Clash_AssignedTo_Title"));
                    return;
                }

                var currentValue = TryGetClashAssignedTo(clash);
                var nextValue = Interaction.InputBox(
                    PanelUi("Panel_Clash_AssignedTo_Prompt"),
                    PanelUi("Panel_Clash_AssignedTo_Title"),
                    currentValue ?? string.Empty);
                if (string.IsNullOrWhiteSpace(nextValue)) return;
                nextValue = nextValue.Trim();
                var doc = NwApplication.ActiveDocument;
                var testsData = doc == null ? null : doc.GetClash()?.TestsData;
                if (testsData == null)
                    throw new InvalidOperationException(PanelUi("Panel_Clash_EngineUnavailable"));
                ClashWorkflowService.ApplyMetadata(testsData, clashes, nextValue, string.Empty);
                LoadClashTests();
                SetGlobalStatusResource("Panel_Clash_AssignedTo_Saved_Format", Brushes.DarkGreen, nextValue, clashes.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message),
                    PanelUi("Panel_Clash_AssignedTo_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private List<ClashResult> GetSelectedClashResults()
        {
            var results = new List<ClashResult>();
            if (_clashGrid != null)
            {
                foreach (var row in _clashGrid.SelectedItems)
                    results.AddRange(GetClashResultsFromRow(row));
            }
            if (results.Count == 0)
                results.AddRange(GetClashResultsFromRow(_clashContextMenuItem ?? _clashGrid?.SelectedItem));
            return results.Where(item => item != null).Distinct().ToList();
        }

        private void SetSelectedClashStatus(ClashResultStatus status, bool testScope)
        {
            var doc = NwApplication.ActiveDocument;
            var testsData = doc == null ? null : doc.GetClash()?.TestsData;
            if (testsData == null)
                throw new InvalidOperationException(PanelUi("Panel_Clash_EngineUnavailable"));

            var results = testScope
                ? GetSelectedClashTests().SelectMany(test => ClashWorkflowService.EnumerateResults(test)).Distinct().ToList()
                : GetSelectedClashResults();
            if (results.Count == 0)
            {
                SetGlobalStatusResource("Panel_Clash_NoSelectedResults", Brushes.Orange);
                return;
            }
            if (results.Count > 500 && MessageBox.Show(
                UiLocalizationService.Current.Format("Panel_Clash_BulkStatus_Message_Format", results.Count, status),
                PanelUi("Panel_Clash_BulkStatus_Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            ClashWorkflowService.ApplyResultUpdates(testsData, results, status, string.Empty, string.Empty);
            LoadClashTests();
            SetGlobalStatusResource("Panel_Clash_StatusUpdated_Format", Brushes.DarkGreen, status, results.Count);
        }

        private void AddClashCommentPrompt()
        {
            var results = GetSelectedClashResults();
            if (results.Count == 0)
            {
                SetGlobalStatusResource("Panel_Clash_SelectResultsForComment", Brushes.Orange);
                return;
            }
            var comment = Interaction.InputBox(
                PanelUi("Panel_Clash_Comment_Prompt"),
                PanelUi("Panel_Clash_Comment_Title"),
                string.Empty);
            if (string.IsNullOrWhiteSpace(comment))
                return;
            var doc = NwApplication.ActiveDocument;
            var testsData = doc == null ? null : doc.GetClash()?.TestsData;
            if (testsData == null)
                throw new InvalidOperationException(PanelUi("Panel_Clash_EngineUnavailable"));
            ClashWorkflowService.ApplyMetadata(testsData, results, string.Empty, comment.Trim());
            SetGlobalStatusResource("Panel_Clash_CommentAdded_Format", Brushes.DarkGreen, results.Count);
        }

        private void GroupSelectedClashResultsPrompt()
        {
            if (_localClashMutationRefreshPending)
            {
                SetGlobalStatusResource("Panel_Clash_WaitForRefresh", Brushes.Orange);
                return;
            }
            if (RejectClashInteractiveBusy("Group selected Clash results"))
                return;

            var tests = GetSelectedClashTests();
            var results = GetSelectedClashResults();
            if (tests.Count != 1 || results.Count == 0)
            {
                SetGlobalStatusResource("Panel_Clash_SelectOneTestAndResults", Brushes.Orange);
                return;
            }
            var name = Interaction.InputBox(
                PanelUi("Panel_Clash_GroupSelected_Prompt"),
                PanelUi("Panel_Clash_GroupSelected_Title"),
                PersistedModelNames.ClashGroupDefault);
            if (string.IsNullOrWhiteSpace(name))
                return;
            var doc = NwApplication.ActiveDocument;
            var testsData = doc == null ? null : doc.GetClash()?.TestsData;
            if (testsData == null)
                throw new InvalidOperationException(PanelUi("Panel_Clash_EngineUnavailable"));
            var existing = ClashGroupMutationService.FindGroup(tests[0], name.Trim());
            if (existing != null && MessageBox.Show(
                PanelUi("Panel_Clash_GroupSelected_Existing_Message"),
                PanelUi("Panel_Clash_GroupSelected_Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            var selectedTests = CaptureSelectedClashTestSelections();
            if (selectedTests.Count != 1)
                throw new InvalidOperationException(PanelUi("Panel_Clash_GroupSelected_TestChanged"));
            var selectedTestRow = _testGrid?.SelectedItem as ClashTestRow;
            if (selectedTestRow == null || selectedTestRow.TestIndex != selectedTests[0].TestIndex ||
                !string.Equals(selectedTestRow.Name ?? string.Empty, selectedTests[0].Name ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                !object.ReferenceEquals(selectedTestRow.Test, tests[0]))
            {
                throw new InvalidOperationException(PanelUi("Panel_Clash_GroupSelected_TestChanged"));
            }

            var previousCursor = Mouse.OverrideCursor;
            var interactiveBusy = NavisHelper.Agent.AgentRuntime.BeginInteractiveOperation("Group selected Clash results");
            var ownsLocalRefresh = false;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                SetClashInteractiveControlsEnabled(false);
                SetGlobalStatusResource("Panel_Clash_GroupSelected_Busy", Brushes.Orange);
                SetGlobalBusy(true);
                PumpDispatcherOnce();

                var currentTestSelection = CaptureSelectedClashTestSelections();
                var currentTestRow = _testGrid?.SelectedItem as ClashTestRow;
                var currentResults = GetSelectedClashResults();
                if (currentTestSelection.Count != 1 ||
                    currentTestRow == null ||
                    currentTestRow.TestIndex != selectedTests[0].TestIndex ||
                    !string.Equals(currentTestRow.Name ?? string.Empty, selectedTests[0].Name ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                    !object.ReferenceEquals(currentTestRow.Test, tests[0]) ||
                    currentResults.Count != results.Count ||
                    results.Any(result => !currentResults.Any(current => object.ReferenceEquals(current, result))))
                {
                    throw new InvalidOperationException(PanelUi("Panel_Clash_GroupSelected_SelectionChanged"));
                }

                if (!BeginLocalClashMutationRefresh())
                    return;
                ownsLocalRefresh = true;

                using (var transaction = doc.BeginTransaction("NavisHelper UI Clash Group"))
                {
                    var group = existing ?? ClashGroupMutationService.FindOrCreateGroup(testsData, tests[0], name.Trim());
                    ClashGroupMutationService.RebuildGroup(testsData, tests[0], group, results);
                    var groupedCount = EnumerateClashResults(group.Children).Count();
                    if (groupedCount != results.Count)
                        throw new InvalidOperationException(
                            UiLocalizationService.Current.Format(
                                "Panel_Clash_GroupSelected_CountMismatch_Format",
                                groupedCount,
                                results.Count));
                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                if (ownsLocalRefresh && _localClashMutationRefreshPending)
                    CancelLocalClashMutationRefresh();
                Logger.Error("Failed to group selected Clash results: " + ex, "ClashUI");
                SetGlobalStatusResource("Panel_Clash_GroupSelected_Failed_Format", Brushes.Red, ex.Message);
                MessageBox.Show(
                    UiLocalizationService.Current.Format("Panel_Clash_GroupSelected_Failed_Format", ex.Message),
                    PanelUi("Panel_Clash_GroupSelected_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
            finally
            {
                _clashContextMenuItem = null;
                SetGlobalBusy(false);
                SetClashInteractiveControlsEnabled(true);
                Mouse.OverrideCursor = previousCursor;
                interactiveBusy.Dispose();
            }

            ScheduleLocalClashMutationRefresh(
                selectedTests,
                "Panel_Clash_GroupSelected_Created_Format",
                name.Trim(),
                results.Count);
        }

        private void UngroupSelectedClashGroup()
        {
            UngroupSelectedClashGroups(null);
        }

        private void UngroupSelectedClashGroups(IList<ClashResultGridRow> explicitRows)
        {
            if (_localClashMutationRefreshPending)
            {
                SetGlobalStatusResource("Panel_Clash_WaitForRefresh", Brushes.Orange);
                return;
            }
            if (RejectClashInteractiveBusy("Ungroup selected Clash groups"))
                return;

            var tests = GetSelectedClashTests();
            var contextRow = _clashContextMenuItem as ClashResultGridRow;
            var selectedRows = explicitRows == null
                ? (_clashGrid == null
                    ? new List<ClashResultGridRow>()
                    : _clashGrid.SelectedItems.OfType<ClashResultGridRow>().ToList())
                : explicitRows.Where(row => row != null).Distinct().ToList();
            if (explicitRows == null && contextRow != null && !selectedRows.Contains(contextRow))
            {
                selectedRows.Clear();
                selectedRows.Add(contextRow);
            }
            else if (explicitRows == null && selectedRows.Count == 0 && contextRow != null)
                selectedRows.Add(contextRow);

            if (tests.Count != 1 || selectedRows.Count == 0)
            {
                SetGlobalStatusResource("Panel_Clash_SelectGroupsInOneTest", Brushes.Orange);
                return;
            }

            if (selectedRows.Any(row => row == null || !row.IsGroup))
            {
                var message = PanelUi("Panel_Clash_Ungroup_GroupsOnly");
                SetGlobalStatusResource("Panel_Clash_Ungroup_GroupsOnly", Brushes.Orange);
                MessageBox.Show(message, PanelUi("Panel_Clash_Ungroup_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (selectedRows.Any(row => ResolvePersistentClashGroup(row) == null))
            {
                SetGlobalStatusResource("Panel_Clash_Ungroup_PersistedOnly", Brushes.Orange);
                return;
            }

            var previousCursor = Mouse.OverrideCursor;
            var interactiveBusy = NavisHelper.Agent.AgentRuntime.BeginInteractiveOperation("Ungroup selected Clash groups");
            var moved = 0;
            var ownsLocalRefresh = false;
            List<ClashTestSelection> selectedTests = null;
            List<ClashUngroupTarget> targets = null;
            ClashUngroupSelectionPlan plan = null;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                SetClashInteractiveControlsEnabled(false);
                SetGlobalStatusResource("Panel_Clash_Ungroup_Busy_Format", Brushes.Orange, selectedRows.Count);
                SetGlobalBusy(true);
                PumpDispatcherOnce();

                var doc = NwApplication.ActiveDocument;
                var testsData = doc == null ? null : doc.GetClash()?.TestsData;
                if (testsData == null)
                    throw new InvalidOperationException(PanelUi("Panel_Clash_EngineUnavailable"));

                selectedTests = CaptureSelectedClashTestSelections();
                if (selectedTests.Count != 1)
                    throw new InvalidOperationException(PanelUi("Panel_Clash_Ungroup_TestChanged"));
                var testSelection = selectedTests[0];
                var selectedTestRow = _testGrid?.SelectedItem as ClashTestRow;
                if (selectedTestRow == null || selectedTestRow.TestIndex != testSelection.TestIndex ||
                    !string.Equals(selectedTestRow.Name ?? string.Empty, testSelection.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                    !object.ReferenceEquals(selectedTestRow.Test, tests[0]))
                {
                    throw new InvalidOperationException(PanelUi("Panel_Clash_Ungroup_TestChanged"));
                }

                targets = BuildClashUngroupTargets(tests[0], selectedRows);
                plan = ClashUngroupSelectionPlanHelper.Build(targets.Select(target => target.Candidate));

                if (!BeginLocalClashMutationRefresh())
                    return;
                ownsLocalRefresh = true;

                using (var transaction = doc.BeginTransaction("NavisHelper UI Clash Ungroup"))
                {
                    foreach (var item in plan.Groups)
                    {
                        var target = targets.First(value => string.Equals(value.Candidate.GroupKey, item.GroupKey, StringComparison.Ordinal));
                        var movedForGroup = ClashGroupMutationService.UngroupGroup(testsData, tests[0], target.Group);
                        if (movedForGroup != item.ResultCount)
                            throw new InvalidOperationException(PanelUi("Panel_Clash_Ungroup_CountMismatch"));
                        moved += movedForGroup;
                        Autodesk.Navisworks.Api.GroupItem ignoredParent;
                        int ignoredIndex;
                        if (TryFindSavedItemLocation(tests[0], target.Group, out ignoredParent, out ignoredIndex))
                            throw new InvalidOperationException(PanelUi("Panel_Clash_Ungroup_Incomplete"));
                    }

                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                if (ownsLocalRefresh && _localClashMutationRefreshPending)
                    CancelLocalClashMutationRefresh();
                Logger.Error("Failed to ungroup selected Clash groups: " + ex, "ClashUI");
                SetGlobalStatusResource("Panel_Clash_Ungroup_Failed_Format", Brushes.Red, ex.Message);
                MessageBox.Show(
                    UiLocalizationService.Current.Format("Panel_Clash_Ungroup_Failed_Format", ex.Message),
                    PanelUi("Panel_Clash_Ungroup_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
            finally
            {
                _clashContextMenuItem = null;
                SetGlobalBusy(false);
                SetClashInteractiveControlsEnabled(true);
                Mouse.OverrideCursor = previousCursor;
                interactiveBusy.Dispose();
            }

            try
            {
                RemoveUngroupedVirtualClashGroups(targets, plan);
            }
            catch (Exception ex)
            {
                Logger.Error("Clash groups were ungrouped, but virtual cache cleanup failed: " + ex, "ClashUI");
            }

            ScheduleLocalClashMutationRefresh(
                selectedTests,
                "Panel_Clash_Ungroup_Completed_Format",
                plan.Groups.Count,
                moved);
        }

        private bool BeginLocalClashMutationRefresh()
        {
            if (_localClashMutationRefreshPending)
            {
                SetGlobalStatusResource("Panel_Clash_WaitForRefresh", Brushes.Orange);
                return false;
            }

            _localClashMutationRefreshPending = true;
            if (_clashDataChangedDebounceTimer != null)
                _clashDataChangedDebounceTimer.Stop();
            DetachCurrentClashTestsDataChanged();
            return true;
        }

        private void CancelLocalClashMutationRefresh()
        {
            _localClashMutationRefreshPending = false;
            AttachCurrentClashTestsDataChanged();
        }

        private void ScheduleLocalClashMutationRefresh(
            ICollection<ClashTestSelection> selectedTests,
            string successResourceKey,
            params object[] successArguments)
        {
            var scheduledDocumentGeneration = _clashDocumentGeneration;
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (!IsLoaded || scheduledDocumentGeneration != _clashDocumentGeneration)
                            return;

                        LoadClashTestsCore(quiet: true);
                        RestoreClashTestSelectionByIdentity(selectedTests);
                        OnClashTestSelected();
                        _clashContextMenuItem = null;
                        SetGlobalStatusResource(successResourceKey, Brushes.DarkGreen, successArguments);
                    }
                    catch (Exception ex)
                    {
                        AttachCurrentClashTestsDataChanged();
                        Logger.Error("Clash mutation committed but UI refresh failed: " + ex, "ClashUI");
                        SetGlobalStatusResource("Panel_Clash_RefreshFailedAfterMutation_Format", Brushes.Red, ex.Message);
                    }
                    finally
                    {
                        if (scheduledDocumentGeneration == _clashDocumentGeneration)
                        {
                            _clashContextMenuItem = null;
                            if (IsLoaded)
                                AttachCurrentClashTestsDataChanged();
                            _localClashMutationRefreshPending = false;
                        }
                    }
                }), DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                CancelLocalClashMutationRefresh();
                Logger.Error("Failed to schedule Clash UI refresh after committed mutation: " + ex, "ClashUI");
                SetGlobalStatusResource("Panel_Clash_RefreshScheduleFailed_Format", Brushes.Red, ex.Message);
            }
        }

        private List<ClashUngroupTarget> BuildClashUngroupTargets(
            ClashTest selectedTest,
            IList<ClashResultGridRow> selectedRows)
        {
            var targets = new List<ClashUngroupTarget>();
            var containers = new List<Autodesk.Navisworks.Api.GroupItem>();

            foreach (var row in selectedRows)
            {
                var group = ResolvePersistentClashGroup(row);

                if (group == null)
                    throw new InvalidOperationException(PanelUi("Panel_Clash_Ungroup_RowNotPersisted"));

                Autodesk.Navisworks.Api.GroupItem parent;
                int index;
                if (!TryFindSavedItemLocation(selectedTest, group, out parent, out index))
                    throw new InvalidOperationException(PanelUi("Panel_Clash_Ungroup_GroupOutsideTest"));

                var groupGuid = TryGetSavedItemGuid(group);
                var existing = targets.FirstOrDefault(target =>
                    object.ReferenceEquals(target.Group, group) ||
                    (groupGuid != Guid.Empty && target.GroupGuid != Guid.Empty && target.GroupGuid == groupGuid));
                var groupKey = existing == null ? "group:" + targets.Count.ToString(CultureInfo.InvariantCulture) : existing.Candidate.GroupKey;

                var parentIdentity = CreateSavedItemIdentity(parent);
                var containerIndex = containers.FindIndex(item => MatchesSavedItemIdentity(item, parentIdentity));
                if (containerIndex < 0)
                {
                    containers.Add(parent);
                    containerIndex = containers.Count - 1;
                }

                var candidate = new ClashUngroupSelectionCandidate
                {
                    GroupKey = groupKey,
                    ContainerKey = "container:" + containerIndex.ToString("D8", CultureInfo.InvariantCulture),
                    GroupIndex = index,
                    ResultCount = EnumerateClashResults(group.Children).Count(),
                };

                if (existing == null)
                {
                    targets.Add(new ClashUngroupTarget
                    {
                        Group = group,
                        GroupGuid = groupGuid,
                        Candidate = candidate,
                    });
                }
                else
                {
                    targets.Add(new ClashUngroupTarget
                    {
                        Group = existing.Group,
                        GroupGuid = existing.GroupGuid,
                        Candidate = candidate,
                    });
                }
            }

            return targets;
        }

        private ClashResultGroup ResolvePersistentClashGroup(ClashResultGridRow row)
        {
            if (row == null)
                return null;
            if (row.PersistentGroup != null)
                return row.PersistentGroup;
            if (!row.VirtualGroupId.HasValue)
                return null;

            return _virtualClashGroups
                .FirstOrDefault(item => item.Id == row.VirtualGroupId.Value)
                ?.PersistentGroup;
        }

        private void RemoveUngroupedVirtualClashGroups(
            IList<ClashUngroupTarget> targets,
            ClashUngroupSelectionPlan plan)
        {
            var removedTargets = plan.Groups
                .Select(item => targets.First(target => string.Equals(target.Candidate.GroupKey, item.GroupKey, StringComparison.Ordinal)))
                .ToList();

            foreach (var virtualGroup in _virtualClashGroups.ToList())
            {
                if (virtualGroup == null || virtualGroup.PersistentGroup == null)
                    continue;

                var shouldRemove = removedTargets.Any(target =>
                    object.ReferenceEquals(virtualGroup.PersistentGroup, target.Group) ||
                    (target.GroupGuid != Guid.Empty && TryGetSavedItemGuid(virtualGroup.PersistentGroup) == target.GroupGuid));
                if (shouldRemove)
                    _clashVirtualGroupState.RemoveGroup(virtualGroup);
            }

            SaveActiveClashGroupsToCache();
        }

        private sealed class ClashUngroupTarget
        {
            public ClashResultGroup Group { get; set; }
            public Guid GroupGuid { get; set; }
            public ClashUngroupSelectionCandidate Candidate { get; set; }
        }

        private void ToggleClashMarker()
        {
            try
            {
                if (_clashMgr.LastClashCenter == null)
                {
                    SetGlobalStatusResource("Panel_Clash_Marker_ShowResultFirst", Brushes.Orange);
                    return;
                }

                if (ClashMarkerTool.IsActive)
                {
                    ClashMarkerTool.Hide();
                    SetGlobalStatusResource("Panel_Clash_Marker_Hidden", Brushes.Gray);
                    return;
                }

                if (SelectionPlaneTool.IsActive)
                    SelectionPlaneTool.Hide();

                double radius = 10;
                if (_clashMarkerSizeText != null && !double.TryParse(_clashMarkerSizeText.Text, out radius))
                    radius = 10;

                if (radius < 1) radius = 1;
                if (radius > 30) radius = 30;

                ClashMarkerTool.Show(_clashMgr.LastClashCenter, radius);
                if (ClashMarkerTool.LastError != null)
                {
                    SetGlobalStatusResource("Panel_Clash_Marker_Error_Format", Brushes.Red, ClashMarkerTool.LastError);
                    return;
                }

                SetGlobalStatusResource(
                    "Panel_Clash_Marker_Enabled_Format",
                    Brushes.DarkGreen,
                    _clashMgr.LastClashCenter.X,
                    _clashMgr.LastClashCenter.Y,
                    _clashMgr.LastClashCenter.Z,
                    radius);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message),
                    PanelUi("Panel_Clash_Marker_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ToggleSelectionPlane()
        {
            try
            {
                if (SelectionPlaneTool.IsActive)
                {
                    SelectionPlaneTool.Hide();
                    SetGlobalStatusResource("Panel_Clash_Plane_Hidden", Brushes.Gray);
                    return;
                }

                if (ClashMarkerTool.IsActive)
                    ClashMarkerTool.Hide();

                if (!SelectionPlaneTool.ShowFromCurrentSelection())
                {
                    SetGlobalStatusResource("Panel_Clash_Plane_Error_Format", Brushes.Red, SelectionPlaneTool.LastError);
                    return;
                }

                SetGlobalStatusResource("Panel_Clash_Plane_Enabled_Format", Brushes.DarkGreen, SelectionPlaneTool.PlaneInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message),
                    PanelUi("Panel_Clash_Plane_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
