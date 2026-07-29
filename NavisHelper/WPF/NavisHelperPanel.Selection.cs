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
using NavisHelper.Core.Localization;
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
        private void SaveSelectionSetSlot(int index)
        {
            try
            {
                if (index < 0 || index >= _selectionSlots.Length) return;
                var doc = NwApplication.ActiveDocument;
                if (doc == null) return;

                _selectionSlots[index] = CopyModelItems(doc.CurrentSelection.SelectedItems);
                SetGlobalStatusResource(
                    "Panel_Selection_MemorySaved_Format",
                    Brushes.DarkGreen,
                    index + 1,
                    _selectionSlots[index].Count);
                UpdateSelectionMemoryIndicator();
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Selection_Memory_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RecallSelectionSetSlot(int index)
        {
            try
            {
                if (index < 0 || index >= _selectionSlots.Length) return;
                var doc = NwApplication.ActiveDocument;
                if (doc == null) return;

                var slot = _selectionSlots[index];
                if (slot == null || slot.Count == 0)
                {
                    SetGlobalStatusResource(
                        "Panel_Selection_MemoryEmpty_Format",
                        Brushes.Orange,
                        index + 1);
                    UpdateSelectionMemoryIndicator();
                    return;
                }

                doc.CurrentSelection.Clear();
                doc.CurrentSelection.CopyFrom(slot);
                SetGlobalStatusResource(
                    "Panel_Selection_MemoryRestored_Format",
                    Brushes.DarkGreen,
                    index + 1,
                    slot.Count);
                UpdateSelectionMemoryIndicator();
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Selection_Memory_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateSelectionMemoryIndicator()
        {
            if (_selectionMemoryText == null)
                return;

            var slot = _selectionSlots[0];
            if (slot == null || slot.Count == 0)
            {
                _selectionMemoryText.Text = PanelUi("Panel_Selection_MemoryEmpty");
                _selectionMemoryText.Foreground = Brushes.Gray;
                return;
            }

            _selectionMemoryText.Text =
                UiLocalizationService.Current.Format("Panel_Selection_MemoryCount_Format", slot.Count);
            _selectionMemoryText.Foreground = Brushes.DarkGreen;
        }

        private void InvertSelection()
        {
            try
            {
                var doc = NwApplication.ActiveDocument;
                if (doc == null) return;

                var selected = doc.CurrentSelection.SelectedItems;
                if (selected == null || selected.Count == 0)
                {
                    MessageBox.Show(
                        PanelUi("Panel_Selection_Invert_SelectItems"),
                        PanelUi("Panel_Selection_Invert_Title"));
                    return;
                }

                var all = CollectModelItems(doc);
                var selectedSet = new HashSet<ModelItem>(selected.Cast<ModelItem>());
                var inverted = new ModelItemCollection();

                foreach (var item in all)
                    if (!selectedSet.Contains(item))
                        inverted.Add(item);

                doc.CurrentSelection.Clear();
                doc.CurrentSelection.CopyFrom(inverted);
                SetGlobalStatusResource(
                    "Panel_Selection_Invert_Result_Format",
                    Brushes.DarkGreen,
                    inverted.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Selection_Invert_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void IsolateSelection()
        {
            try
            {
                var doc = NwApplication.ActiveDocument;
                if (doc == null) return;

                var selected = doc.CurrentSelection.SelectedItems;
                if (selected == null || selected.Count == 0)
                {
                    MessageBox.Show(
                        PanelUi("Panel_Selection_Isolate_SelectItems"),
                        PanelUi("Panel_Selection_Isolate_Title"));
                    return;
                }

                var all = CollectModelItems(doc);
                var selectedItems = selected.Cast<ModelItem>().ToList();
                var keep = new HashSet<ModelItem>(selectedItems);
                foreach (var item in selectedItems)
                {
                    var descendants = new Stack<ModelItem>();
                    descendants.Push(item);
                    while (descendants.Count > 0)
                    {
                        var node = descendants.Pop();
                        if (node.Children == null) continue;
                        foreach (var child in node.Children)
                        {
                            if (keep.Add(child))
                                descendants.Push(child);
                        }
                    }

                    var current = item.Parent;
                    while (current != null)
                    {
                        keep.Add(current);
                        current = current.Parent;
                    }
                }

                var hidden = new Autodesk.Navisworks.Api.ModelItemCollection();
                foreach (var item in all)
                    if (!keep.Contains(item))
                        hidden.Add(item);

                try { doc.Models.SetHidden(hidden, true); } catch { }
                SetGlobalStatusResource(
                    "Panel_Selection_Isolate_Result_Format",
                    Brushes.DarkGreen,
                    hidden.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Selection_Isolate_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UnhideAll()
        {
            try
            {
                var doc = NwApplication.ActiveDocument;
                if (doc == null) return;

                var all = CollectModelItems(doc);
                if (all.Count == 0) return;
                doc.Models.SetHidden(all, false);
                SetGlobalStatusResource(
                    "Panel_Selection_Unhide_Result",
                    Brushes.DarkGreen);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Selection_Unhide_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowAndCopySelectionBounds()
        {
            string title = PanelUi("Panel_Selection_Bounds_Title");
            try
            {
                var document = NwApplication.ActiveDocument;
                var selection = document?.CurrentSelection?.SelectedItems;
                if (selection == null || selection.Count == 0)
                {
                    SetGlobalStatusResource(
                        "Panel_Selection_Bounds_SelectItems",
                        Brushes.Orange);
                    MessageBox.Show(
                        PanelUi("Panel_Selection_Bounds_SelectItems"),
                        title,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var bounds = selection.BoundingBox();
                if (bounds == null)
                {
                    SetGlobalStatusResource(
                        "Panel_Selection_BoundsUnavailable",
                        Brushes.Orange);
                    MessageBox.Show(
                        PanelUi("Panel_Selection_BoundsUnavailable"),
                        title,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var documentUnits = document.Units;
                var documentUnitsPerMillimeter = SectionBoxHelper.MmToDocUnits(1.0);
                if (documentUnitsPerMillimeter <= 0 || double.IsNaN(documentUnitsPerMillimeter) || double.IsInfinity(documentUnitsPerMillimeter))
                    throw new InvalidOperationException(
                        PanelUi("Panel_Selection_ModelUnitScaleFailed"));

                var sizeXmm = (bounds.Max.X - bounds.Min.X) / documentUnitsPerMillimeter;
                var sizeYmm = (bounds.Max.Y - bounds.Min.Y) / documentUnitsPerMillimeter;
                var sizeZmm = (bounds.Max.Z - bounds.Min.Z) / documentUnitsPerMillimeter;
                var text = UiLocalizationService.Current.Format(
                    "Panel_Selection_BoundsReport_Format",
                    selection.Count,
                    sizeXmm,
                    sizeYmm,
                    sizeZmm,
                    documentUnits,
                    bounds.Min.X,
                    bounds.Min.Y,
                    bounds.Min.Z,
                    bounds.Max.X,
                    bounds.Max.Y,
                    bounds.Max.Z,
                    bounds.Center.X,
                    bounds.Center.Y,
                    bounds.Center.Z);

                System.Windows.Clipboard.SetText(text);
                SetGlobalStatusResource(
                    "Panel_Selection_BoundsCopied_Format",
                    Brushes.DarkGreen,
                    sizeXmm,
                    sizeYmm,
                    sizeZmm);
                MessageBox.Show(
                    text + "\r\n\r\n" + PanelUi("Panel_Selection_BoundsCopiedToClipboard"),
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetGlobalStatusResource(
                    "Panel_Selection_BoundsFailed_Format",
                    Brushes.Red,
                    ex.Message);
                MessageBox.Show(
                    UiLocalizationService.Current.Format(
                        "Panel_Selection_BoundsFailed_Format",
                        ex.Message),
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ShowSelectionSectionBox()
        {
            _selectionSectionDebounceTimer?.Stop();
            ResetSelectionSectionAxisControls();
            ApplySelectionSectionBox(true, true);
        }

        private void ResetSelectionSectionAxisControls()
        {
            _suppressSelectionSectionControlRefresh = true;
            try
            {
                if (_selOffsetXSlider != null) _selOffsetXSlider.Value = 0;
                if (_selOffsetYSlider != null) _selOffsetYSlider.Value = 0;
                if (_selOffsetZSlider != null) _selOffsetZSlider.Value = 0;
                if (_selShiftXSlider != null) _selShiftXSlider.Value = 0;
                if (_selShiftYSlider != null) _selShiftYSlider.Value = 0;
                if (_selShiftZSlider != null) _selShiftZSlider.Value = 0;
            }
            finally
            {
                _suppressSelectionSectionControlRefresh = false;
            }
        }

        private void ApplySelectionSectionBox(bool rememberHistory, bool useCurrentSelectionAsAnchor = false)
        {
            try
            {
                _selMgr.CommonOffsetMm = _selOffsetAllSlider?.Value ?? 1000;
                _selMgr.OffsetXMm = _selOffsetXSlider?.Value ?? 0;
                _selMgr.OffsetYMm = _selOffsetYSlider?.Value ?? 0;
                _selMgr.OffsetZMm = _selOffsetZSlider?.Value ?? 0;
                _selMgr.ShiftXMm = _selShiftXSlider?.Value ?? 0;
                _selMgr.ShiftYMm = _selShiftYSlider?.Value ?? 0;
                _selMgr.ShiftZMm = _selShiftZSlider?.Value ?? 0;
                _selMgr.ContextTransparency = (_selTransSlider?.Value ?? 70) / 100.0;
                _selMgr.UseSectionBox = _selUseSectionBox?.IsChecked ?? true;
                _selMgr.UseContextTransparency = _selContextTrans?.IsChecked ?? false;

                _selMgr.ShowSelection(useCurrentSelectionAsAnchor);
                if (rememberHistory && _selMgr.LastSuccess)
                    RememberSelectionSectionObjects();
                SetGlobalStatusResource(
                    PreviewManagerUiStatusMapper.ForSelection(_selMgr.LastUiOutcome),
                    _selMgr.LastSuccess ? Brushes.DarkGreen : Brushes.Orange);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Selection_SectionBox_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ScheduleSelectionSectionRefresh()
        {
            if (_selMgr == null || _selMgr.LastExpandedBox == null)
                return;

            if (_selectionSectionDebounceTimer == null)
            {
                _selectionSectionDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _selectionSectionDebounceTimer.Tick += (s, e) =>
                {
                    _selectionSectionDebounceTimer.Stop();
                    try
                    {
                        var doc = NwApplication.ActiveDocument;
                        if (doc == null)
                            return;

                        ApplySelectionSectionBox(false);
                    }
                    catch
                    {
                    }
                };
            }

            _selectionSectionDebounceTimer.Stop();
            _selectionSectionDebounceTimer.Start();
        }

        private void RememberSelectionSectionObjects()
        {
            var doc = NwApplication.ActiveDocument;
            var selection = doc?.CurrentSelection?.SelectedItems;
            if (doc == null || selection == null || selection.Count == 0)
                return;

            if (!ReferenceEquals(_selectionSectionHistoryDocument, doc))
            {
                _selectionSectionHistory.Clear();
                _selectionSectionHistoryDocument = doc;
            }

            var appliedAt = DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
            foreach (ModelItem item in selection)
            {
                for (var i = _selectionSectionHistory.Count - 1; i >= 0; i--)
                {
                    var existing = _selectionSectionHistory[i];
                    if (ReferenceEquals(existing.Item, item) || Equals(existing.Item, item))
                        _selectionSectionHistory.RemoveAt(i);
                }

                _selectionSectionHistory.Insert(0, new SectionBoxHistoryRow
                {
                    ObjectName = GetSectionBoxHistoryObjectName(item),
                    AppliedAt = appliedAt,
                    Item = item
                });
            }

            while (_selectionSectionHistory.Count > 10)
                _selectionSectionHistory.RemoveAt(_selectionSectionHistory.Count - 1);

            _suppressSelectionSectionHistorySync = true;
            try
            {
                _selectionSectionHistoryGrid?.Items.Refresh();
            }
            finally
            {
                _suppressSelectionSectionHistorySync = false;
            }
        }

        private static string GetSectionBoxHistoryObjectName(ModelItem item)
        {
            if (item == null)
                return UiLocalizationService.Current.GetString(
                    "Panel_Selection_HistoryObjectUnavailable");
            if (!string.IsNullOrWhiteSpace(item.DisplayName))
                return item.DisplayName;
            if (!string.IsNullOrWhiteSpace(item.ClassDisplayName))
                return item.ClassDisplayName;
            return UiLocalizationService.Current.GetString(
                "Panel_Selection_HistoryObjectUnnamed");
        }

        private void OnSelectionSectionHistoryDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var gridRow = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
                if (!(gridRow?.Item is SectionBoxHistoryRow))
                    return;

                var doc = NwApplication.ActiveDocument;
                if (doc == null || doc.CurrentSelection == null || !ReferenceEquals(doc, _selectionSectionHistoryDocument))
                {
                    _suppressSelectionSectionHistorySync = true;
                    try
                    {
                        _selectionSectionHistory.Clear();
                        _selectionSectionHistoryDocument = doc;
                        _selectionSectionHistoryGrid?.Items.Refresh();
                    }
                    finally
                    {
                        _suppressSelectionSectionHistorySync = false;
                    }
                    SetGlobalStatusResource(
                        "Panel_Selection_HistoryDocumentChanged",
                        Brushes.Orange);
                    return;
                }

                var selectedRows = _selectionSectionHistoryGrid.SelectedItems
                    .OfType<SectionBoxHistoryRow>()
                    .Where(row => row.Item != null)
                    .ToList();
                if (selectedRows.Count == 0)
                    return;

                ApplySelectionSectionBox(false, true);
                if (_selMgr.LastSuccess)
                {
                    SetGlobalStatusResource(
                        "Panel_Selection_HistoryApplied_Format",
                        Brushes.DarkGreen,
                        selectedRows.Count);
                }
                else
                {
                    SetGlobalStatusResource(
                        PreviewManagerUiStatusMapper.ForSelection(_selMgr.LastUiOutcome),
                        Brushes.Orange);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to select Section Box history item: " + ex, "SelectionSectionBox");
                MessageBox.Show(
                    UiLocalizationService.Current.Format(
                        "Panel_Selection_HistoryNavigateFailed_Format",
                        ex.Message),
                    PanelUi("Panel_Selection_SectionBox_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OnSelectionSectionHistorySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionSectionHistorySync)
                return;

            try
            {
                var doc = NwApplication.ActiveDocument;
                if (doc == null || !ReferenceEquals(doc, _selectionSectionHistoryDocument))
                {
                    _suppressSelectionSectionHistorySync = true;
                    try
                    {
                        _selectionSectionHistory.Clear();
                        _selectionSectionHistoryDocument = doc;
                        _selectionSectionHistoryGrid?.Items.Refresh();
                    }
                    finally
                    {
                        _suppressSelectionSectionHistorySync = false;
                    }
                    SetGlobalStatusResource(
                        "Panel_Selection_HistoryDocumentChanged",
                        Brushes.Orange);
                    return;
                }

                var selection = new ModelItemCollection();
                var selectedRows = _selectionSectionHistoryGrid.SelectedItems.OfType<SectionBoxHistoryRow>().ToList();
                foreach (var row in selectedRows)
                {
                    if (row.Item != null)
                        selection.Add(row.Item);
                }

                if (selectedRows.Count > 0 && selection.Count == 0)
                {
                    SetGlobalStatusResource(
                        "Panel_Selection_HistoryItemsUnavailable",
                        Brushes.Orange);
                    return;
                }

                doc.CurrentSelection.Clear();
                if (selection.Count > 0)
                    doc.CurrentSelection.CopyFrom(selection);

                SetGlobalStatusResource(
                    selection.Count > 0
                        ? "Panel_Selection_HistorySelected_Format"
                        : "Panel_Selection_HistorySelectionCleared",
                    selection.Count > 0 ? Brushes.DarkGreen : Brushes.Gray,
                    selection.Count);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to synchronize Section Box history selection: " + ex, "SelectionSectionBox");
                SetGlobalStatusResource(
                    "Panel_Selection_HistorySelectionFailed_Format",
                    Brushes.Red,
                    ex.Message);
            }
        }

        private void ResetSelectionSectionBox()
        {
            try
            {
                _selectionSectionDebounceTimer?.Stop();
                _selMgr.ResetView();
                ResetSelectionSectionAxisControls();
                SetGlobalStatusResource(
                    "Panel_Selection_SectionBoxReset",
                    Brushes.Gray);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Selection_SectionBox_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportSelectedPropertiesToExcelLikeFile()
        {
            try
            {
                var doc = NwApplication.ActiveDocument;
                if (doc == null) return;
                var selection = doc.CurrentSelection.SelectedItems;
                if (selection == null || selection.Count == 0)
                {
                    MessageBox.Show(
                        PanelUi("Panel_Selection_ExportProperties_SelectItems"),
                        PanelUi("Panel_Selection_ExportProperties_Title"));
                    return;
                }

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = PanelUi("Panel_Selection_ExportProperties_Title"),
                    Filter = PanelUi("Panel_Selection_ExportProperties_FileFilter"),
                    FileName = $"SelectionProperties_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                    DefaultExt = ".csv"
                };
                if (dlg.ShowDialog() != true) return;

                var lines = new List<string>
                {
                    "\"Path\";\"DisplayName\";\"Category\";\"Property\";\"Value\""
                };

                foreach (var item in selection)
                {
                    var path = BuildItemPath(item);
                    var display = item.DisplayName;
                    if (item.PropertyCategories == null)
                        continue;

                    foreach (PropertyCategory category in item.PropertyCategories)
                    {
                        if (category == null || category.Properties == null) continue;
                        var categoryName = string.IsNullOrWhiteSpace(category.DisplayName) ? (string.IsNullOrWhiteSpace(category.Name) ? "(unknown)" : category.Name) : category.DisplayName;
                        foreach (var property in category.Properties)
                        {
                            if (property == null) continue;
                            string propertyName;
                            try { propertyName = property.DisplayName; } catch { propertyName = "(property)"; }
                            if (string.IsNullOrWhiteSpace(propertyName)) propertyName = property.Name ?? "(property)";
                            string value = string.Empty;
                            try { value = property.Value == null ? string.Empty : property.Value.ToDisplayString() ?? string.Empty; } catch { }
                            lines.Add($"{EscapeCsv(path)};{EscapeCsv(display)};{EscapeCsv(categoryName)};{EscapeCsv(propertyName)};{EscapeCsv(value)}");
                        }
                    }
                }

                File.WriteAllLines(dlg.FileName, lines, System.Text.Encoding.UTF8);
                SetGlobalStatusResource(
                    "Panel_Selection_ExportProperties_Saved_Format",
                    Brushes.DarkGreen,
                    dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Selection_ExportProperties_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
