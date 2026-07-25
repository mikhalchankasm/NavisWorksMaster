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
        private readonly Dictionary<string, List<ClashResult>> _clashTreeMatchCache =
            new Dictionary<string, List<ClashResult>>(StringComparer.OrdinalIgnoreCase);

        private GroupBox BuildClashGroupingTreePanel()
        {
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _clashGroupingStatus = new TextBlock
            {
                Text = "Группировка: нет",
                FontSize = 10,
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Grid.SetRow(_clashGroupingStatus, 0);
            layout.Children.Add(_clashGroupingStatus);

            _clashTreeA = MakeClashGroupingTree();
            _clashTreeB = MakeClashGroupingTree();
            var groupContentsPanel = MakeClashGroupContentsPanel();
            _clashTreeA.SelectedItemChanged += OnClashGroupingTreeSelected;
            _clashTreeB.SelectedItemChanged += OnClashGroupingTreeSelected;

            var tabs = new TabControl
            {
                FontSize = 11,
                Margin = new Thickness(0)
            };
            tabs.Items.Add(new TabItem { Header = "Объект A", Content = _clashTreeA });
            tabs.Items.Add(new TabItem { Header = "Объект B", Content = _clashTreeB });
            tabs.Items.Add(new TabItem { Header = "Состав", Content = groupContentsPanel });
            Grid.SetRow(tabs, 1);
            layout.Children.Add(tabs);

            var commands = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            _applyClashGroupingButton = new Button
            {
                Content = "Объединить по уровню",
                Height = 24,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(0, 0, 4, 4),
                FontSize = 11,
                Cursor = Cursors.Hand,
                IsEnabled = false,
                ToolTip = "Сначала выберите уровень дерева A или B",
                Style = UiTheme.ButtonStyle(ButtonKind.Primary)
            };
            _applyClashGroupingButton.Click += (s, e) => ApplyPendingClashGrouping();
            ToolTipService.SetShowOnDisabled(_applyClashGroupingButton, true);
            commands.Children.Add(_applyClashGroupingButton);

            var reset = new Button
            {
                Content = "Сброс группировки",
                Height = 24,
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 11,
                Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(ButtonKind.Destructive)
            };
            reset.Click += (s, e) =>
            {
                ResetSelectedClashGrouping();
                SetClashGroupPanelVisible(false);
            };
            commands.Children.Add(reset);
            Grid.SetRow(commands, 2);
            layout.Children.Add(commands);

            var group = ClashGroupBox("Дерево A/B", layout, 0);
            group.Margin = new Thickness(6, 0, 0, 0);
            group.VerticalAlignment = VerticalAlignment.Stretch;
            group.HorizontalAlignment = HorizontalAlignment.Stretch;
            return group;
        }

        private Grid MakeClashGroupContentsPanel()
        {
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _clashGroupContentsStatus = new TextBlock
            {
                Text = "Выберите коллизию или группу",
                FontSize = 10,
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 2, 2, 4)
            };
            Grid.SetRow(_clashGroupContentsStatus, 0);
            layout.Children.Add(_clashGroupContentsStatus);

            _clashGroupContentsGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HorizontalGridLinesBrush = new SolidColorBrush(WpfColor.FromRgb(0xD7, 0xDE, 0xE8)),
                VerticalGridLinesBrush = new SolidColorBrush(WpfColor.FromRgb(0xD7, 0xDE, 0xE8)),
                AlternationCount = 2,
                AlternatingRowBackground = new SolidColorBrush(WpfColor.FromRgb(0xF8, 0xFA, 0xFC)),
                Background = Brushes.White,
                FontSize = 10.5,
                RowHeight = 21,
                SelectionMode = DataGridSelectionMode.Single,
                CanUserAddRows = false,
                CanUserDeleteRows = false
            };
            _clashGroupContentsGrid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new System.Windows.Data.Binding("Index"), Width = new DataGridLength(34) });
            _clashGroupContentsGrid.Columns.Add(new DataGridTextColumn { Header = "Коллизия", Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            _clashGroupContentsGrid.Columns.Add(new DataGridTextColumn { Header = "A", Binding = new System.Windows.Data.Binding("ItemA"), Width = new DataGridLength(110) });
            _clashGroupContentsGrid.Columns.Add(new DataGridTextColumn { Header = "B", Binding = new System.Windows.Data.Binding("ItemB"), Width = new DataGridLength(110) });
            ScrollViewer.SetHorizontalScrollBarVisibility(_clashGroupContentsGrid, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(_clashGroupContentsGrid, ScrollBarVisibility.Auto);

            Grid.SetRow(_clashGroupContentsGrid, 1);
            layout.Children.Add(_clashGroupContentsGrid);
            return layout;
        }

        private static TreeView MakeClashGroupingTree()
        {
            var compactItemStyle = new Style(typeof(TreeViewItem));
            compactItemStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(-6, 0, 0, 0)));
            compactItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));

            var tree = new TreeView
            {
                FontSize = 11,
                BorderThickness = new Thickness(0),
                Background = Brushes.White,
                Padding = new Thickness(6, 0, 0, 0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ItemContainerStyle = compactItemStyle,
                ToolTip = "Выберите уровень дерева, чтобы сгруппировать коллизии по этому объекту"
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(tree, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(tree, ScrollBarVisibility.Auto);
            // Виртуализация верхнего уровня: узлы строятся вручную, поэтому эффект
            // частичный, но на больших деревьях коллизий заметный.
            VirtualizingStackPanel.SetIsVirtualizing(tree, true);
            VirtualizingStackPanel.SetVirtualizationMode(tree, VirtualizationMode.Recycling);
            var panelFactory = new FrameworkElementFactory(typeof(VirtualizingStackPanel));
            tree.ItemsPanel = new ItemsPanelTemplate(panelFactory);
            return tree;
        }

        private StackPanel BuildClashFilterPanel()
        {
            var filterRow = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };

            _clashFilterPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            _clashFilterPanel.Children.Add(new TextBlock
            {
                Text = "Статусы:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 2),
                FontSize = 10
            });
            MakeCheck(_clashFilterPanel, "New", true, Brushes.Red);
            MakeCheck(_clashFilterPanel, "Active", true, Brushes.OrangeRed);
            MakeCheck(_clashFilterPanel, "Reviewed", true, Brushes.DodgerBlue);
            MakeCheck(_clashFilterPanel, "Approved", false, Brushes.Green);
            MakeCheck(_clashFilterPanel, "Resolved", false, Brushes.Gray);
            filterRow.Children.Add(_clashFilterPanel);

            var columnFilters = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            columnFilters.Children.Add(new TextBlock { Text = "Коллизия:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 2), FontSize = 10 });
            _clashFilterBox = CreateClashColumnFilterBox("имя", 120);
            columnFilters.Children.Add(_clashFilterBox);
            columnFilters.Children.Add(new TextBlock { Text = "A:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 2), FontSize = 10 });
            _clashItemAFilterBox = CreateClashColumnFilterBox("объект A", 105);
            columnFilters.Children.Add(_clashItemAFilterBox);
            columnFilters.Children.Add(new TextBlock { Text = "B:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 2), FontSize = 10 });
            _clashItemBFilterBox = CreateClashColumnFilterBox("объект B", 105);
            columnFilters.Children.Add(_clashItemBFilterBox);
            filterRow.Children.Add(columnFilters);

            return filterRow;
        }

        private TextBox CreateClashColumnFilterBox(string hint, double width)
        {
            var box = new TextBox
            {
                Height = 22,
                Width = width,
                Margin = new Thickness(0, 0, 0, 2),
                FontSize = 11,
                ToolTip = "Фильтр: " + hint
            };
            box.TextChanged += (s, e) => RefreshClashGridRows();
            return box;
        }

        private static Style BuildClashGridRowStyle()
        {
            var style = new Style(typeof(DataGridRow));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));

            var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xF7, 0xC2))));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
            style.Triggers.Add(selected);

            return style;
        }

        private static Style BuildClashGridCellStyle()
        {
            var style = new Style(typeof(DataGridCell));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 0, 4, 0)));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(WpfColor.FromRgb(0xD7, 0xDE, 0xE8))));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));

            var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xF7, 0xC2))));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
            selected.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(WpfColor.FromRgb(0xD6, 0xB8, 0x45))));
            style.Triggers.Add(selected);

            return style;
        }

        private string GetSelectedClashBoxMode()
        {
            return _clashBoxModeItemsRadio?.IsChecked == true
                ? ClashPreviewManager.BoxModeItems
                : ClashPreviewManager.BoxModePoint;
        }

        private void SetClashBoxModeControls(string boxMode)
        {
            if (string.Equals(boxMode, ClashPreviewManager.BoxModeItems, StringComparison.OrdinalIgnoreCase))
            {
                if (_clashBoxModeItemsRadio != null) _clashBoxModeItemsRadio.IsChecked = true;
                return;
            }

            if (_clashBoxModePointRadio != null) _clashBoxModePointRadio.IsChecked = true;
        }

        private Button ClashActionButton(string text, string tooltip, Action action, double minWidth = 0, ButtonKind kind = ButtonKind.Neutral)
        {
            var btn = new Button
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.NoWrap },
                ToolTip = tooltip,
                Height = 26,
                MinWidth = minWidth,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 4, 4),
                FontSize = 11,
                Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(kind)
            };
            btn.Click += (s, e) =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    SetGlobalStatus("Ошибка: " + ex.Message, Brushes.Red);
                }
            };
            return btn;
        }

        private static System.Windows.Controls.Primitives.ToggleButton ClashActionToggle(string text, string tooltip)
        {
            return new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.NoWrap },
                ToolTip = tooltip,
                Height = 26,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 4, 4),
                FontSize = 11,
                Cursor = Cursors.Hand
            };
        }

        private Button ClashTopBarButton(string text, string tooltip, Action action, ButtonKind kind = ButtonKind.Neutral)
        {
            var btn = new Button
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.NoWrap, TextAlignment = TextAlignment.Center },
                ToolTip = tooltip,
                Height = 24,
                MinWidth = 0,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(0, 0, 3, 0),
                FontSize = 10.5,
                Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(kind)
            };

            if (action != null)
            {
                btn.Click += (s, e) =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception ex)
                    {
                        SetGlobalStatus("Ошибка: " + ex.Message, Brushes.Red);
                    }
                };
            }

            return btn;
        }

        private void RegisterClashInteractiveButton(Button button)
        {
            if (button != null && !_clashInteractiveButtons.Contains(button))
                _clashInteractiveButtons.Add(button);
        }

        private bool RejectClashInteractiveBusy(string action)
        {
            if (!NavisHelper.Agent.AgentRuntime.IsInteractiveBusy)
                return false;

            var reason = NavisHelper.Agent.AgentRuntime.InteractiveBusyReason;
            var message = string.IsNullOrWhiteSpace(reason)
                ? "Navisworks занят интерактивной операцией"
                : "Navisworks занят: " + reason;
            SetGlobalStatus(message, Brushes.Orange);
            Logger.Info("Ignored Clash UI action while interactive busy: " + (action ?? "unknown") + " reason=" + (reason ?? string.Empty), "ClashUI");
            return true;
        }

        private void SetClashInteractiveControlsEnabled(bool enabled)
        {
            foreach (var button in _clashInteractiveButtons.ToList())
            {
                if (button != null)
                    button.IsEnabled = enabled;
            }

            if (_applyClashGroupingButton != null)
                _applyClashGroupingButton.IsEnabled = enabled && _pendingClashGroupingTag != null;
        }

        private UIElement BuildClashMarkerControl()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 4, 4)
            };
            panel.Children.Add(ClashActionButton("Маркер", "Показать/скрыть 2D маркер в точке пересечения коллизии", ToggleClashMarker));
            _clashMarkerSizeText = new TextBox
            {
                Text = "10",
                Width = 34,
                Height = 22,
                FontSize = 10,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Радиус маркера (5-30, как в Autodesk ClashMarkers)",
                Margin = new Thickness(0, 0, 2, 4)
            };
            panel.Children.Add(_clashMarkerSizeText);
            panel.Children.Add(new TextBlock
            {
                Text = "px",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 9,
                Margin = new Thickness(0, 0, 4, 4)
            });
            return panel;
        }

        private void ResetClashView()
        {
            var doc = NwApplication.ActiveDocument;
            SetClashOnlyPairToggle(false);
            _clashMgr.UsePairIsolation = false;
            _clashMgr.ResetView();
            ClearActiveViewRedlines(doc);
            try { doc?.CurrentSelection?.Clear(); } catch { }
            try { doc?.ActiveView?.RequestDelayedRedraw(ViewRedrawRequests.All); } catch { }
            SetGlobalStatus("Вид сброшен", Brushes.Gray);
        }

        private void EnableSelectedClashPairIsolation()
        {
            if (RejectClashInteractiveBusy("Enable selected Clash pair isolation"))
            {
                SetClashOnlyPairToggle(false);
                _clashMgr.UsePairIsolation = false;
                return;
            }

            _clashMgr.UsePairIsolation = true;
            if (_clashGrid?.SelectedItem == null)
            {
                SetGlobalStatus("Режим «Только пара» включён. Дважды щёлкните по коллизии.", Brushes.Orange);
                return;
            }

            PreviewSelectedClash();
        }

        private void DisableSelectedClashPairIsolation()
        {
            _clashMgr.UsePairIsolation = false;
            _clashMgr.ClearPairIsolation();
            SetGlobalStatus("Показаны все ветви, временно скрытые режимом «Только пара»", Brushes.DarkGreen);
        }

        private void ShowAllAfterClashPairIsolation()
        {
            if (RejectClashInteractiveBusy("Restore Clash pair isolation visibility"))
                return;

            if (_clashOnlyPairToggle?.IsChecked == true)
            {
                SetClashOnlyPairToggle(false);
            }

            _clashMgr.UsePairIsolation = false;
            _clashMgr.ClearPairIsolation();
            SetGlobalStatus("Показаны все ветви, временно скрытые режимом «Только пара»", Brushes.DarkGreen);
        }

        private void SetClashOnlyPairToggle(bool isChecked)
        {
            if (_clashOnlyPairToggle == null)
                return;

            _suppressClashPairToggleEvents = true;
            try
            {
                _clashOnlyPairToggle.IsChecked = isChecked;
            }
            finally
            {
                _suppressClashPairToggleEvents = false;
            }
        }

        private void ClearClashPairIsolationForLifecycle()
        {
            SetClashOnlyPairToggle(false);
            _clashMgr.UsePairIsolation = false;
            _clashMgr.ClearPairIsolation();
        }

        private void ApplyClashSelectionTransparency()
        {
            try
            {
                _clashMgr.ContextTransparency = (_clashTransSlider?.Value ?? 70) / 100.0;
                var doc = NwApplication.ActiveDocument;
                var hasSelection = doc?.CurrentSelection?.SelectedItems != null && doc.CurrentSelection.SelectedItems.Count > 0;
                int count = hasSelection
                    ? _clashMgr.ApplyTransparencyToSelection()
                    : _clashMgr.ApplyClashRootContextTransparency();
                var details = string.IsNullOrWhiteSpace(_clashMgr.LastFullBoxTransparencyStatus)
                    ? string.Empty
                    : " | " + _clashMgr.LastFullBoxTransparencyStatus;
                SetGlobalStatus(hasSelection
                    ? $"Прозрачность: {count} владельцев по выделению{details}"
                    : $"Прозрачность: {count} владельцев A/B{details}", count > 0 ? Brushes.DarkGreen : Brushes.Orange);
            }
            catch (Exception ex)
            {
                SetGlobalStatus($"Ошибка: {ex.Message}", Brushes.Red);
            }
        }

        private CheckBox MakeCheck(WrapPanel panel, string text, bool isChecked, Brush color)
        {
            var cb = new CheckBox { Content = text, IsChecked = isChecked, Foreground = color, FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 0, 3, 0), FontSize = 11 };
            cb.Checked += (s, e) => RefreshClashGridRows();
            cb.Unchecked += (s, e) => RefreshClashGridRows();
            panel?.Children.Add(cb);
            return cb;
        }

        private ComboBox MakeColorCombo(int defIdx)
        {
            var combo = new ComboBox { Width = 110, Height = 22, FontSize = 11 };
            foreach (var (name, r, g, b) in ClashColors)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                if (r.HasValue)
                    sp.Children.Add(new Border { Width = 14, Height = 14, Background = new SolidColorBrush(WpfColor.FromRgb(r.Value, g.Value, b.Value)), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(2) });
                else
                    sp.Children.Add(new Border { Width = 14, Height = 14, Background = Brushes.Transparent, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 0), CornerRadius = new CornerRadius(2) });
                sp.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
                // Tag = null для "без подсветки", byte[] для цвета
                combo.Items.Add(new ComboBoxItem { Content = sp, Tag = r.HasValue ? new byte[] { r.Value, g.Value, b.Value } : null });
            }
            combo.SelectedIndex = defIdx;
            return combo;
        }

        private ContextMenu BuildClashTestContextMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(ClashTestMenuItem("Выполнить", () => ApplySelectedClashTestOperation("run")));
            menu.Items.Add(ClashTestMenuItem("Сброс", () => ApplySelectedClashTestOperation("reset")));
            menu.Items.Add(ClashTestMenuItem("Сжать", () => ApplySelectedClashTestOperation("compact")));
            menu.Items.Add(new Separator());
            menu.Items.Add(ClashTestMenuItem("Сформировать точки обзора", CreateViewpointsForSelectedClashTests));
            menu.Items.Add(BuildClashStatusMenu("Статус всех коллизий", true));
            menu.Items.Add(new Separator());
            menu.Items.Add(ClashTestMenuItem("Переименовать", RenameSelectedClashTest));
            menu.Items.Add(ClashTestMenuItem("Удалить", () => ApplySelectedClashTestOperation("delete")));
            return menu;
        }

        private enum ClashResultSelectionMode
        {
            ItemA,
            ItemB,
            Both
        }

        private sealed class ClashTreeNodeTag
        {
            public ClashGroupingSide Side { get; set; }
            public ModelItem Item { get; set; }
            public string Path { get; set; }
            public string Label { get; set; }
        }

        private sealed class ModelItemPathEntry
        {
            public ModelItem Item { get; set; }
            public string Name { get; set; }
            public string Path { get; set; }
            public int Depth { get; set; }
        }

        private sealed class ClashResultLocation
        {
            public Autodesk.Navisworks.Api.GroupItem Parent { get; set; }
            public int Index { get; set; }
            public ClashResult Result { get; set; }
        }

        private sealed class SavedItemIdentity
        {
            public Guid Guid { get; set; }
            public SavedItem Reference { get; set; }
            public string DisplayName { get; set; }
        }

        private ContextMenu BuildClashResultContextMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(ClashResultMenuItem("Перейти к объекту A", () => SelectClashResultItems(ClashResultSelectionMode.ItemA)));
            menu.Items.Add(ClashResultMenuItem("Перейти к объекту B", () => SelectClashResultItems(ClashResultSelectionMode.ItemB)));
            menu.Items.Add(ClashResultMenuItem("Выбрать оба объекта", () => SelectClashResultItems(ClashResultSelectionMode.Both)));
            menu.Items.Add(new Separator());
            menu.Items.Add(ClashResultMenuItem("Группировать по объекту A", () => SetClashGrouping(ClashGroupingSide.ItemA)));
            menu.Items.Add(ClashResultMenuItem("Группировать по объекту B", () => SetClashGrouping(ClashGroupingSide.ItemB)));
            menu.Items.Add(ClashResultMenuItem("Сбросить группировку", ResetSelectedClashGrouping));
            menu.Items.Add(new Separator());
            menu.Items.Add(BuildClashStatusMenu("Статус", false));
            menu.Items.Add(ClashResultMenuItem("Назначить исполнителя…", SetClashAssignedToPrompt));
            menu.Items.Add(ClashResultMenuItem("Добавить комментарий…", AddClashCommentPrompt));
            menu.Items.Add(new Separator());
            menu.Items.Add(ClashResultMenuItem("Сгруппировать выделенные…", GroupSelectedClashResultsPrompt));
            menu.Items.Add(ClashResultMenuItem("Разгруппировать", UngroupSelectedClashGroup));
            menu.Items.Add(new Separator());
            menu.Items.Add(ClashResultMenuItem("Сформировать выделенные точки обзора", CreateViewpointsForSelectedClashResults));
            return menu;
        }

        private MenuItem BuildClashStatusMenu(string header, bool testScope)
        {
            var menu = new MenuItem { Header = header };
            foreach (ClashResultStatus status in new[]
            {
                ClashResultStatus.Approved,
                ClashResultStatus.Reviewed,
                ClashResultStatus.Resolved,
                ClashResultStatus.Active,
                ClashResultStatus.New,
            })
            {
                var captured = status;
                menu.Items.Add(testScope
                    ? ClashTestMenuItem(captured.ToString(), () => SetSelectedClashStatus(captured, true))
                    : ClashResultMenuItem(captured.ToString(), () => SetSelectedClashStatus(captured, false)));
            }
            return menu;
        }

        private MenuItem ClashResultMenuItem(string text, Action action)
        {
            var item = new MenuItem { Header = text };
            item.Click += (s, e) =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Logger.Error("Clash Result action failed: " + ex, "ClashUI");
                    SetGlobalStatus("Ошибка: " + ex.Message, Brushes.Red);
                    MessageBox.Show("Ошибка: " + ex.Message, "Clash Result", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            return item;
        }

        private MenuItem ClashTestMenuItem(string text, Action action)
        {
            var item = new MenuItem { Header = text };
            item.Click += (s, e) =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    SetGlobalStatus("Ошибка: " + ex.Message, Brushes.Red);
                    MessageBox.Show("Ошибка: " + ex.Message, "Clash Test", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            return item;
        }

        private void OnClashTestGridRightClick(object sender, MouseButtonEventArgs e)
        {
            var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row == null)
                return;

            if (!row.IsSelected)
            {
                _testGrid.SelectedItems.Clear();
                row.IsSelected = true;
                _testGrid.SelectedItem = row.Item;
            }

            row.Focus();
        }

        private void OnClashResultGridRightClick(object sender, MouseButtonEventArgs e)
        {
            var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row == null)
            {
                _clashContextMenuItem = null;
                e.Handled = true;
                return;
            }

            if (!row.IsSelected)
            {
                _clashGrid.SelectedItems.Clear();
                row.IsSelected = true;
                _clashGrid.SelectedItem = row.Item;
            }

            _clashContextMenuItem = row.Item;
            row.Focus();
        }

        private void OnClashResultContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row == null)
            {
                _clashContextMenuItem = null;
                e.Handled = true;
                return;
            }

            _clashContextMenuItem = row.Item;
        }

        private void OnClashGridBeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            var header = e.Column?.Header as string;
            if (!string.Equals(header, "Имя группы", StringComparison.Ordinal))
            {
                e.Cancel = true;
                return;
            }

            var row = e.Row?.Item as ClashResultGridRow;
            if (row == null || row.VirtualGroupId == null)
            {
                e.Cancel = true;
                SetGlobalStatus("Переименование доступно только для ручных групп", Brushes.Orange);
            }
        }

        private void OnClashGridCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit)
                return;

            var header = e.Column?.Header as string;
            if (!string.Equals(header, "Имя группы", StringComparison.Ordinal))
                return;

            var row = e.Row?.Item as ClashResultGridRow;
            if (row == null || row.VirtualGroupId == null)
                return;

            var editor = e.EditingElement as TextBox;
            var nextName = (editor?.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(nextName))
            {
                SetGlobalStatus("Имя группы не может быть пустым", Brushes.Orange);
                BeginInvokeForCurrentClashDocument(RefreshClashGridRows, DispatcherPriority.Background);
                return;
            }

            BeginInvokeForCurrentClashDocument(() =>
            {
                RenameVirtualClashGroup(row.VirtualGroupId.Value, nextName);
            }, DispatcherPriority.Background);
        }

        private void RenameVirtualClashGroup(Guid groupId, string nextName)
        {
            var group = _virtualClashGroups.FirstOrDefault(item => item.Id == groupId);
            if (group == null)
                return;

            var persistentName = BuildPersistentClashGroupName(group.Side, nextName);
            var cleanName = GetUserClashGroupName(persistentName);
            try
            {
                var doc = NwApplication.ActiveDocument;
                var clash = doc == null ? null : doc.GetClash();
                var testsData = clash == null ? null : clash.TestsData;
                if (testsData == null)
                    throw new InvalidOperationException("Clash Detective недоступен");

                var selectedTest = (_testGrid == null ? null : _testGrid.SelectedItem as ClashTestRow)?.Test;
                var persistentGroup = group.PersistentGroup ?? FindClashResultGroup(selectedTest, group.Label, group.Side);
                if (persistentGroup == null)
                    throw new InvalidOperationException("Сохранённая группа не найдена");

                var duplicate = selectedTest == null
                    ? null
                    : selectedTest.Children
                        .OfType<ClashResultGroup>()
                        .FirstOrDefault(item =>
                            !object.ReferenceEquals(item, persistentGroup) &&
                            (string.Equals(item.DisplayName, persistentName, StringComparison.OrdinalIgnoreCase) ||
                             (InferClashGroupingSideFromGroupName(item.DisplayName) == group.Side &&
                              string.Equals(GetUserClashGroupName(item.DisplayName), cleanName, StringComparison.OrdinalIgnoreCase))));
                if (duplicate != null)
                    throw new InvalidOperationException("Группа с таким именем уже есть");

                using (var transaction = doc.BeginTransaction("NavisHelper Clash Group Rename"))
                {
                    testsData.TestsEditDisplayName(persistentGroup, persistentName);
                    transaction.Commit();
                }

                group.Label = cleanName;
                group.PersistentGroup = persistentGroup;
                SaveActiveClashGroupsToCache();
                RefreshClashGridRows();
                SetGlobalStatus($"Группа переименована: {group.Label}", Brushes.DarkGreen);
            }
            catch (Exception ex)
            {
                SetGlobalStatus("Группа не переименована: " + ex.Message, Brushes.Red);
                BeginInvokeForCurrentClashDocument(RefreshClashGridRows, DispatcherPriority.Background);
            }
        }

        private ClashResult GetSelectedClashResult()
        {
            return GetClashResultsFromRow(_clashContextMenuItem ?? _clashGrid?.SelectedItem).FirstOrDefault();
        }

        private void UpdateClashGroupingTrees()
        {
            if (_clashTreeA == null || _clashTreeB == null)
                return;

            ResetPendingClashGroupingSelection();
            _suppressClashTreeSelectionChanged = true;
            try
            {
                _clashTreeA.Items.Clear();
                _clashTreeB.Items.Clear();

                var selectedRow = _clashGrid?.SelectedItem;
                var selectedResults = GetClashResultsFromRow(selectedRow);
                UpdateClashGroupContents(selectedRow, selectedResults);

                var result = selectedResults.FirstOrDefault();
                if (result != null)
                {
                    FillClashGroupingTree(_clashTreeA, result, ClashGroupingSide.ItemA);
                    FillClashGroupingTree(_clashTreeB, result, ClashGroupingSide.ItemB);
                }

                UpdateClashGroupingStatusText();
            }
            finally
            {
                _suppressClashTreeSelectionChanged = false;
            }

        }

        private void UpdateClashGroupContents(object rowObject, IList<ClashResult> results)
        {
            if (_clashGroupContentsGrid == null)
                return;

            var rows = BuildClashGroupContentRows(results);
            _clashGroupContentsGrid.ItemsSource = rows;

            if (_clashGroupContentsStatus == null)
                return;

            if (rows.Count == 0)
            {
                _clashGroupContentsStatus.Text = "Выберите коллизию или группу";
                _clashGroupContentsStatus.Foreground = Brushes.DimGray;
                return;
            }

            var gridRow = rowObject as ClashResultGridRow;
            var label = gridRow != null && gridRow.IsGroup
                ? string.IsNullOrWhiteSpace(gridRow.GroupName) ? gridRow.Name : gridRow.GroupName
                : "Одиночная коллизия";
            var uniqueA = ClashGroupDisplayPolicy.CountDistinctNames(rows.Select(row => row.ItemA));
            var uniqueB = ClashGroupDisplayPolicy.CountDistinctNames(rows.Select(row => row.ItemB));
            _clashGroupContentsStatus.Text = $"Состав: {label} | коллизий: {rows.Count} | A: {uniqueA} | B: {uniqueB}";
            _clashGroupContentsStatus.Foreground = gridRow != null && gridRow.IsGroup ? Brushes.DarkGreen : Brushes.DimGray;
        }

        private static List<ClashGroupContentRow> BuildClashGroupContentRows(IEnumerable<ClashResult> results)
        {
            var rows = new List<ClashGroupContentRow>();
            if (results == null)
                return rows;

            var index = 1;
            foreach (var result in results.Where(item => item != null))
            {
                rows.Add(new ClashGroupContentRow
                {
                    Index = index++,
                    Name = result.DisplayName ?? string.Empty,
                    ItemA = GetClashItemName(result.Selection1),
                    ItemB = GetClashItemName(result.Selection2)
                });
            }

            return rows;
        }

        private void FillClashGroupingTree(TreeView tree, ClashResult result, ClashGroupingSide side)
        {
            if (tree == null || result == null)
                return;

            var seed = side == ClashGroupingSide.ItemA
                ? ResolveClashSideSeed(result.Item1, result.Selection1)
                : ResolveClashSideSeed(result.Item2, result.Selection2);

            var path = BuildModelItemPathEntries(seed);
            if (path.Count == 0)
            {
                tree.Items.Add(new TreeViewItem
                {
                    Header = new TextBlock { Text = "нет данных", Foreground = Brushes.Gray, FontStyle = FontStyles.Italic },
                    IsEnabled = false,
                    Style = tree.ItemContainerStyle
                });
                return;
            }

            TreeViewItem parent = null;
            foreach (var entry in path)
            {
                var text = new TextBlock
                {
                    Text = entry.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = entry.Path + "\nПКМ: выделить этот объект в модели"
                };

                var tag = new ClashTreeNodeTag
                {
                    Side = side,
                    Item = entry.Item,
                    Path = entry.Path,
                    Label = entry.Name
                };
                var item = new TreeViewItem
                {
                    Header = text,
                    IsExpanded = true,
                    Style = tree.ItemContainerStyle,
                    ToolTip = "Объединить коллизии внутри: " + entry.Path,
                    Tag = tag,
                    ContextMenu = BuildClashTreeContextMenu(tag)
                };
                item.PreviewMouseRightButtonDown += (sender, args) =>
                {
                    var clickedItem = FindVisualParent<TreeViewItem>(args.OriginalSource as DependencyObject);
                    if (!ReferenceEquals(clickedItem, sender))
                        return;

                    item.IsSelected = true;
                    item.Focus();
                };

                if (_clashGroupingSide == side && string.Equals(_clashGroupingPath, entry.Path, StringComparison.OrdinalIgnoreCase))
                    item.IsSelected = true;

                if (parent == null)
                    tree.Items.Add(item);
                else
                    parent.Items.Add(item);

                parent = item;
            }
        }

        private ContextMenu BuildClashTreeContextMenu(ClashTreeNodeTag tag)
        {
            var menu = new ContextMenu();
            var selectInModel = new MenuItem { Header = "Выделить в модели" };
            selectInModel.Click += (sender, args) => SelectClashTreeItemInModel(tag);
            menu.Items.Add(selectInModel);
            return menu;
        }

        private void SelectClashTreeItemInModel(ClashTreeNodeTag tag)
        {
            if (RejectClashInteractiveBusy("Select clash tree item in model"))
                return;

            try
            {
                var doc = NwApplication.ActiveDocument;
                if (doc == null || doc.IsClear)
                {
                    SetGlobalStatus("Нет активного документа", Brushes.Orange);
                    return;
                }

                var selection = new ModelItemCollection();
                if (tag == null || !AddSelectableModelItem(selection, tag.Item))
                {
                    SetGlobalStatus("Объект дерева больше недоступен в активной модели", Brushes.Orange);
                    return;
                }

                doc.CurrentSelection.CopyFrom(selection);
                var sideLabel = tag.Side == ClashGroupingSide.ItemA ? "A" : "B";
                SetGlobalStatus($"Выделен объект {sideLabel}: {tag.Label}", Brushes.DarkGreen);
            }
            catch (Exception ex)
            {
                SetGlobalStatus("Не удалось выделить объект: " + ex.Message, Brushes.Red);
            }
        }

        private void OnClashGroupingTreeSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_suppressClashTreeSelectionChanged)
                return;

            var selected = e.NewValue as TreeViewItem;
            var tag = selected?.Tag as ClashTreeNodeTag;
            if (tag == null || tag.Side == ClashGroupingSide.None || string.IsNullOrWhiteSpace(tag.Path))
                return;

            SetPendingClashGrouping(tag);
            e.Handled = true;
        }

        private void SetPendingClashGrouping(ClashTreeNodeTag tag)
        {
            _pendingClashGroupingTag = tag;
            var matchCount = FindClashResultsForTreeNode(tag).Count;
            if (_applyClashGroupingButton != null)
            {
                _applyClashGroupingButton.IsEnabled = matchCount > 0;
                _applyClashGroupingButton.ToolTip =
                    $"По выбранному уровню найдено коллизий: {matchCount}. Уже сгруппированные результаты обрабатываются отдельно.";
            }

            var sideLabel = tag.Side == ClashGroupingSide.ItemA ? "A" : "B";
            if (_clashGroupingStatus != null)
            {
                _clashGroupingStatus.Text = $"Уровень {sideLabel}: {tag.Label} | найдено: {matchCount}";
                _clashGroupingStatus.Foreground = Brushes.DarkSlateBlue;
            }
        }

        private void ApplyPendingClashGrouping()
        {
            var tag = _pendingClashGroupingTag;
            if (tag == null)
            {
                SetGlobalStatus("Выберите уровень дерева A или B", Brushes.Orange);
                return;
            }

            SetClashGroupingFromTree(tag);
            SetClashGroupPanelVisible(false);
        }

        private void SetClashGroupingFromTree(ClashTreeNodeTag tag)
        {
            if (RejectClashInteractiveBusy("Create Clash group from panel"))
                return;

            var sideLabel = tag.Side == ClashGroupingSide.ItemA ? "A" : "B";
            var previousCursor = Mouse.OverrideCursor;
            var interactiveBusy = NavisHelper.Agent.AgentRuntime.BeginInteractiveOperation("Create Clash group from panel");
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                SetClashInteractiveControlsEnabled(false);
                SetGlobalBusy(true, $"Создание группы {sideLabel}: {tag.Label}");
                SetGlobalStatus($"Группа {sideLabel}: поиск коллизий...", Brushes.Orange);
                PumpDispatcherOnce();
                var added = AddVirtualClashGroup(tag);
                if (!added)
                    return;

                RefreshClashGridRows();
                UpdateClashGroupingTrees();
                SetGlobalStatus($"Группа добавлена {sideLabel}: {tag.Label}", Brushes.DarkGreen);
            }
            finally
            {
                SetGlobalBusy(false);
                SetClashInteractiveControlsEnabled(true);
                if (_pendingClashGroupingTag == null)
                    ResetPendingClashGroupingSelection();
                else
                    SetPendingClashGrouping(_pendingClashGroupingTag);
                Mouse.OverrideCursor = previousCursor;
                interactiveBusy.Dispose();
            }
        }

        private bool AddVirtualClashGroup(ClashTreeNodeTag tag)
        {
            if (tag == null || tag.Side == ClashGroupingSide.None || string.IsNullOrWhiteSpace(tag.Path))
                return false;

            var matches = FindClashResultsForTreeNode(tag);

            if (matches.Count == 0)
            {
                SetGlobalStatus("Для выбранного уровня коллизии не найдены", Brushes.Orange);
                return false;
            }

            var persistentGroupName = BuildClashResultGroupName(tag);
            var sameGroup = _virtualClashGroups.FirstOrDefault(group =>
                (group.Side == tag.Side && string.Equals(group.Path, tag.Path, StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(BuildPersistentClashGroupName(group.Side, group.Label), persistentGroupName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(group.PersistentGroup == null ? null : group.PersistentGroup.DisplayName, persistentGroupName, StringComparison.OrdinalIgnoreCase));
            if (sameGroup != null)
            {
                var replace = MessageBox.Show(
                    $"Группа \"{sameGroup.Label}\" уже есть.\nОбновить её состав по текущим результатам?",
                    "Группа уже существует",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (replace != MessageBoxResult.Yes)
                    return false;

                ClashResultGroup persistedGroup;
                if (!PersistClashResultGroup(tag, matches, persistentGroupName, out persistedGroup))
                    return false;

                sameGroup.Results = matches;
                sameGroup.Label = GetUserClashGroupName(persistentGroupName);
                sameGroup.Side = tag.Side;
                sameGroup.Path = tag.Path;
                sameGroup.PersistentGroup = persistedGroup;
                RemoveEmptyVirtualClashGroups();
                SaveActiveClashGroupsToCache();
                return true;
            }

            var groupedResults = ClashVirtualGroupMembershipHelper.CollectReferenceSet(
                _virtualClashGroups
                    .Where(group => group != null && group.Results != null)
                    .Select(group => (IEnumerable<ClashResult>)group.Results));
            var overlapping = ClashVirtualGroupMembershipHelper
                .IntersectReferences(matches, groupedResults)
                .Distinct()
                .ToList();

            var groupResults = matches;
            if (overlapping.Count > 0)
            {
                var answer = MessageBox.Show(
                    $"Уже в других группах: {overlapping.Count} из {matches.Count} коллизий.\n\n" +
                    "Да - перенести их в новую группу.\n" +
                    "Нет - оставить их в старых группах и добавить только свободные.\n" +
                    "Отмена - не создавать группу.",
                    "Коллизии уже сгруппированы",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (answer == MessageBoxResult.Cancel)
                    return false;

                if (answer == MessageBoxResult.Yes)
                {
                    RemoveClashesFromVirtualGroups(overlapping);
                }
                else
                {
                    groupResults = ClashVirtualGroupMembershipHelper.ExceptReferences(matches, groupedResults);
                }
            }

            if (groupResults.Count == 0)
            {
                SetGlobalStatus("Новая группа не создана: все коллизии остались в старых группах", Brushes.Orange);
                return false;
            }

            ClashResultGroup newPersistentGroup;
            if (!PersistClashResultGroup(tag, groupResults, persistentGroupName, out newPersistentGroup))
                return false;

            _clashVirtualGroupState.AddGroup(new VirtualClashGroup
            {
                Side = tag.Side,
                Path = tag.Path,
                Label = GetUserClashGroupName(persistentGroupName),
                Results = groupResults.ToList(),
                PersistentGroup = newPersistentGroup
            });
            RemoveEmptyVirtualClashGroups();
            _clashGroupingSide = ClashGroupingSide.None;
            _clashGroupingPath = null;
            _clashGroupingLabel = null;
            SaveActiveClashGroupsToCache();
            return true;
        }

        private List<ClashResult> FindClashResultsForTreeNode(ClashTreeNodeTag tag)
        {
            if (tag == null || tag.Side == ClashGroupingSide.None || string.IsNullOrWhiteSpace(tag.Path))
                return new List<ClashResult>();

            var cacheKey = ((int)tag.Side).ToString(CultureInfo.InvariantCulture) + "|" + tag.Path;
            List<ClashResult> cached;
            if (_clashTreeMatchCache.TryGetValue(cacheKey, out cached))
                return cached.ToList();

            var matches = (_loadedResults ?? new List<ClashResult>())
                .Where(result => result != null && ResolveClashGroupingAncestor(result, tag.Side, tag.Path) != null)
                .Distinct()
                .ToList();
            _clashTreeMatchCache[cacheKey] = matches;
            return matches.ToList();
        }

        private void ResetPendingClashGroupingSelection()
        {
            _pendingClashGroupingTag = null;
            if (_applyClashGroupingButton == null)
                return;

            _applyClashGroupingButton.IsEnabled = false;
            _applyClashGroupingButton.ToolTip = "Сначала выберите уровень дерева A или B";
        }

        private void InvalidateClashTreeMatchCache()
        {
            _clashTreeMatchCache.Clear();
        }

        private bool PersistClashResultGroup(ClashTreeNodeTag tag, IList<ClashResult> results, string groupName, out ClashResultGroup persistedGroup)
        {
            persistedGroup = null;
            var selectedTest = (_testGrid == null ? null : _testGrid.SelectedItem as ClashTestRow)?.Test;
            if (selectedTest == null)
            {
                SetGlobalStatus("Выберите Clash Test для сохранения группы", Brushes.Orange);
                return false;
            }

            if (results == null || results.Count == 0)
                return false;

            try
            {
                var doc = NwApplication.ActiveDocument;
                var clash = doc == null ? null : doc.GetClash();
                var testsData = clash == null ? null : clash.TestsData;
                if (testsData == null)
                    throw new InvalidOperationException("Clash Detective недоступен");

                var moved = 0;
                var targetResults = results.Where(result => result != null).Distinct().ToList();
                using (var transaction = doc.BeginTransaction("NavisHelper Clash Grouping"))
                {
                    var group = FindOrCreateClashResultGroup(testsData, selectedTest, groupName);
                    moved = RebuildClashResultGroup(testsData, selectedTest, group, targetResults);
                    if (targetResults.Count > 0 && EnumerateClashResults(group.Children).Count() == 0)
                        throw new InvalidOperationException("ClashResultGroup создана, но результаты не были перенесены внутрь группы.");

                    transaction.Commit();
                    persistedGroup = group;
                }

                SetGlobalStatus($"Группа сохранена: {groupName}, перенесено {moved}", Brushes.DarkGreen);
                return true;
            }
            catch (Exception ex)
            {
                SetGlobalStatus("Группа не сохранена: " + ex.Message, Brushes.Red);
                return false;
            }
        }

        private static string BuildClashResultGroupName(ClashTreeNodeTag tag)
        {
            var label = tag == null || string.IsNullOrWhiteSpace(tag.Label) ? "Group" : tag.Label.Trim();
            return BuildPersistentClashGroupName(tag == null ? ClashGroupingSide.ItemB : tag.Side, label);
        }

        private static string BuildPersistentClashGroupName(ClashGroupingSide side, string label)
        {
            return ClashVirtualGroupIdentityHelper.BuildPersistentName(ToVirtualClashGroupSide(side), label);
        }

        private static ClashGroupingSide InferClashGroupingSideFromGroupName(string groupName)
        {
            return FromVirtualClashGroupSide(ClashVirtualGroupIdentityHelper.InferSide(groupName));
        }

        private static string GetUserClashGroupName(string groupName)
        {
            return ClashVirtualGroupIdentityHelper.GetUserName(groupName);
        }

        private static ClashVirtualGroupSide ToVirtualClashGroupSide(ClashGroupingSide side)
        {
            return side == ClashGroupingSide.ItemA
                ? ClashVirtualGroupSide.ItemA
                : side == ClashGroupingSide.ItemB ? ClashVirtualGroupSide.ItemB : ClashVirtualGroupSide.None;
        }

        private static ClashGroupingSide FromVirtualClashGroupSide(ClashVirtualGroupSide side)
        {
            return side == ClashVirtualGroupSide.ItemA
                ? ClashGroupingSide.ItemA
                : side == ClashVirtualGroupSide.ItemB ? ClashGroupingSide.ItemB : ClashGroupingSide.None;
        }

        private static ClashResultGroup FindOrCreateClashResultGroup(DocumentClashTests testsData, ClashTest test, string groupName)
        {
            if (testsData == null)
                throw new InvalidOperationException("Clash Detective data is not available.");
            if (test == null)
                throw new ArgumentNullException(nameof(test));

            var existing = FindClashResultGroup(test, groupName);
            if (existing != null)
                return existing;

            var newGroup = new ClashResultGroup { DisplayName = groupName };
            testsData.TestsInsertCopy(test, 0, newGroup);

            existing = test.Children
                .OfType<ClashResultGroup>()
                .FirstOrDefault(group => string.Equals(group.DisplayName, groupName, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                throw new InvalidOperationException("Не удалось создать ClashResultGroup.");

            return existing;
        }

        private static ClashResultGroup FindClashResultGroup(ClashTest test, string groupName)
        {
            return FindClashResultGroup(test, groupName, InferClashGroupingSideFromGroupName(groupName));
        }

        private static ClashResultGroup FindClashResultGroup(ClashTest test, string groupName, ClashGroupingSide side)
        {
            if (test == null || test.Children == null || string.IsNullOrWhiteSpace(groupName))
                return null;

            var cleanName = GetUserClashGroupName(groupName);
            return test.Children
                .OfType<ClashResultGroup>()
                .FirstOrDefault(group =>
                    string.Equals(group.DisplayName, groupName, StringComparison.OrdinalIgnoreCase) ||
                    ((side == ClashGroupingSide.None || InferClashGroupingSideFromGroupName(group.DisplayName) == side) &&
                     string.Equals(GetUserClashGroupName(group.DisplayName), cleanName, StringComparison.OrdinalIgnoreCase)));
        }

        private static int RebuildClashResultGroup(DocumentClashTests testsData, ClashTest selectedTest, ClashResultGroup group, IList<ClashResult> targetResults)
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
                {
                    testsData.TestsMove(parentGroup.Key, location.Index, selectedTest, selectedTest.Children.Count);
                }
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

    }
}
