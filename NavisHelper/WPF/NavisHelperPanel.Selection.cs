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
        private void SaveSelectionSetSlot(int index)
        {
            try
            {
                if (index < 0 || index >= _selectionSlots.Length) return;
                var doc = NwApplication.ActiveDocument;
                if (doc == null) return;

                _selectionSlots[index] = CopyModelItems(doc.CurrentSelection.SelectedItems);
                SetGlobalStatus($"Слот {index + 1}: сохранено {_selectionSlots[index].Count} элементов", Brushes.DarkGreen);
                UpdateSelectionMemoryIndicator();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message, "Selection Slot", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    SetGlobalStatus($"Слот {index + 1} пуст", Brushes.Orange);
                    UpdateSelectionMemoryIndicator();
                    return;
                }

                doc.CurrentSelection.Clear();
                doc.CurrentSelection.CopyFrom(slot);
                SetGlobalStatus($"Слот {index + 1}: восстановлено {slot.Count} эл.", Brushes.DarkGreen);
                UpdateSelectionMemoryIndicator();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message, "Selection Slot", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateSelectionMemoryIndicator()
        {
            if (_selectionMemoryText == null)
                return;

            var slot = _selectionSlots[0];
            if (slot == null || slot.Count == 0)
            {
                _selectionMemoryText.Text = "память пуста";
                _selectionMemoryText.Foreground = Brushes.Gray;
                return;
            }

            _selectionMemoryText.Text = $"в памяти: {slot.Count} объектов";
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
                    MessageBox.Show("Выделите хотя бы один элемент для инверсии.", "Invert Selection");
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
                SetGlobalStatus($"Инвертировано: выбрано {inverted.Count} эл.", Brushes.DarkGreen);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message, "Invert Selection", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show("Выделите элементы для Isolate.", "Isolate");
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
                SetGlobalStatus($"Isolate: скрыто {hidden.Count} эл.", Brushes.DarkGreen);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message, "Isolate", MessageBoxButton.OK, MessageBoxImage.Error);
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
                SetGlobalStatus("Все элементы показаны", Brushes.DarkGreen);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message, "Unhide All", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowAndCopySelectionBounds()
        {
            const string title = "Габариты выделения";
            try
            {
                var document = NwApplication.ActiveDocument;
                var selection = document?.CurrentSelection?.SelectedItems;
                if (selection == null || selection.Count == 0)
                {
                    SetGlobalStatus("Выберите хотя бы один элемент", Brushes.Orange);
                    MessageBox.Show("Выберите хотя бы один элемент.", title, MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var bounds = selection.BoundingBox();
                if (bounds == null)
                {
                    SetGlobalStatus("Не удалось определить габариты выделения", Brushes.Orange);
                    MessageBox.Show("Не удалось определить габариты выделения.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var documentUnits = document.Units;
                var documentUnitsPerMillimeter = SectionBoxHelper.MmToDocUnits(1.0);
                if (documentUnitsPerMillimeter <= 0 || double.IsNaN(documentUnitsPerMillimeter) || double.IsInfinity(documentUnitsPerMillimeter))
                    throw new InvalidOperationException("Не удалось определить масштаб единиц модели.");

                var sizeXmm = (bounds.Max.X - bounds.Min.X) / documentUnitsPerMillimeter;
                var sizeYmm = (bounds.Max.Y - bounds.Min.Y) / documentUnitsPerMillimeter;
                var sizeZmm = (bounds.Max.Z - bounds.Min.Z) / documentUnitsPerMillimeter;
                var text = string.Format(
                    CultureInfo.InvariantCulture,
                    "Габариты выделения\r\n" +
                    "Объектов: {0}\r\n" +
                    "Размер X: {1:0.###} мм\r\n" +
                    "Размер Y: {2:0.###} мм\r\n" +
                    "Размер Z: {3:0.###} мм\r\n" +
                    "Единицы координат: {4}\r\n" +
                    "Min: {5:0.######}; {6:0.######}; {7:0.######}\r\n" +
                    "Max: {8:0.######}; {9:0.######}; {10:0.######}\r\n" +
                    "Center: {11:0.######}; {12:0.######}; {13:0.######}",
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
                SetGlobalStatus($"Габариты скопированы: {sizeXmm:0.###} × {sizeYmm:0.###} × {sizeZmm:0.###} мм", Brushes.DarkGreen);
                MessageBox.Show(text + "\r\n\r\nЗначения скопированы в буфер обмена.", title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SetGlobalStatus("Ошибка чтения габаритов: " + ex.Message, Brushes.Red);
                MessageBox.Show("Не удалось получить или скопировать габариты:\r\n" + ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
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
                SetGlobalStatus(_selMgr.LastStatus, _selMgr.LastSuccess ? Brushes.DarkGreen : Brushes.Orange);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message, "Section Box", MessageBoxButton.OK, MessageBoxImage.Error);
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
                return "(объект недоступен)";
            if (!string.IsNullOrWhiteSpace(item.DisplayName))
                return item.DisplayName;
            if (!string.IsNullOrWhiteSpace(item.ClassDisplayName))
                return item.ClassDisplayName;
            return "(без имени)";
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
                    SetGlobalStatus("История Section Box относится к другому документу и очищена", Brushes.Orange);
                    return;
                }

                var selectedRows = _selectionSectionHistoryGrid.SelectedItems
                    .OfType<SectionBoxHistoryRow>()
                    .Where(row => row.Item != null)
                    .ToList();
                if (selectedRows.Count == 0)
                    return;

                ApplySelectionSectionBox(false, true);
                SetGlobalStatus(
                    _selMgr.LastSuccess
                        ? $"Section Box применён к объектам из истории: {selectedRows.Count}"
                        : _selMgr.LastStatus,
                    _selMgr.LastSuccess ? Brushes.DarkGreen : Brushes.Orange);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to select Section Box history item: " + ex, "SelectionSectionBox");
                MessageBox.Show(
                    "Не удалось перейти к объекту из истории:\r\n" + ex.Message,
                    "Section Box",
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
                    SetGlobalStatus("История Section Box относится к другому документу и очищена", Brushes.Orange);
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
                    SetGlobalStatus("Выбранные объекты истории больше недоступны", Brushes.Orange);
                    return;
                }

                doc.CurrentSelection.Clear();
                if (selection.Count > 0)
                    doc.CurrentSelection.CopyFrom(selection);

                SetGlobalStatus(
                    selection.Count > 0
                        ? $"В модели выделено объектов из истории: {selection.Count}"
                        : "Выделение объектов из истории снято",
                    selection.Count > 0 ? Brushes.DarkGreen : Brushes.Gray);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to synchronize Section Box history selection: " + ex, "SelectionSectionBox");
                SetGlobalStatus("Не удалось выделить объекты из истории: " + ex.Message, Brushes.Red);
            }
        }

        private void ResetSelectionSectionBox()
        {
            try
            {
                _selectionSectionDebounceTimer?.Stop();
                _selMgr.ResetView();
                ResetSelectionSectionAxisControls();
                SetGlobalStatus("Section box по выделению сброшен", Brushes.Gray);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message, "Section Box", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    MessageBox.Show("Выделите объекты для экспорта.", "Export properties");
                    return;
                }

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Экспорт свойств",
                    Filter = "CSV (Excel-совместимый)|*.csv|Все файлы|*.*",
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
                SetGlobalStatus($"Сохранено свойств: {dlg.FileName}", Brushes.DarkGreen);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message, "Export properties", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
