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
using NavisHelper.Core.Localization;
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
        private object _clashGroupContentsRowObject;
        private IList<ClashResult> _clashGroupContentsResults;

        private GroupBox BuildClashGroupingTreePanel()
        {
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 80 });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _clashGroupingStatus = new TextBlock
            {
                FontSize = 10,
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _panelLocalizationBindings.BindAction(
                _clashGroupingStatus,
                "Clash.GroupingStatus",
                UpdateClashGroupingStatusText);
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
            var objectATab = new TabItem { Content = _clashTreeA };
            var objectBTab = new TabItem { Content = _clashTreeB };
            var contentsTab = new TabItem { Content = groupContentsPanel };
            _panelLocalizationBindings.BindHeader(objectATab, "Panel_ObjectA");
            _panelLocalizationBindings.BindHeader(objectBTab, "Panel_ObjectB");
            _panelLocalizationBindings.BindHeader(contentsTab, "Panel_Contents");
            tabs.Items.Add(objectATab);
            tabs.Items.Add(objectBTab);
            tabs.Items.Add(contentsTab);
            Grid.SetRow(tabs, 1);
            layout.Children.Add(tabs);

            var commands = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            _applyClashGroupingButton = new Button
            {
                Height = 24,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(0, 0, 4, 4),
                FontSize = 11,
                Cursor = Cursors.Hand,
                IsEnabled = false,
                Style = UiTheme.ButtonStyle(ButtonKind.Primary)
            };
            _panelLocalizationBindings.BindContent(
                _applyClashGroupingButton,
                "Panel_Clash_MergeLevel_Label");
            _panelLocalizationBindings.BindAction(
                _applyClashGroupingButton,
                "Clash.ApplyGroupingToolTip",
                RefreshApplyClashGroupingToolTip);
            _applyClashGroupingButton.Click += (s, e) => ApplyPendingClashGrouping();
            ToolTipService.SetShowOnDisabled(_applyClashGroupingButton, true);
            commands.Children.Add(_applyClashGroupingButton);

            var reset = new Button
            {
                Height = 24,
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 11,
                Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(ButtonKind.Destructive)
            };
            _panelLocalizationBindings.BindContent(reset, "Panel_ResetGrouping");
            reset.Click += (s, e) =>
            {
                ResetSelectedClashGrouping();
                SetClashGroupPanelVisible(false);
            };
            commands.Children.Add(reset);
            Grid.SetRow(commands, 2);
            layout.Children.Add(commands);

            var group = ClashGroupBox("Panel_Clash_Group_TreeAB", layout, 0);
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
                FontSize = 10,
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 2, 2, 4)
            };
            _panelLocalizationBindings.BindAction(
                _clashGroupContentsStatus,
                "Clash.GroupContentsStatus",
                () => UpdateClashGroupContents(
                    _clashGroupContentsRowObject,
                    _clashGroupContentsResults));
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
            var indexColumn = new DataGridTextColumn { Header = "#", Binding = new System.Windows.Data.Binding("Index"), Width = new DataGridLength(34) };
            var clashColumn = new DataGridTextColumn { Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
            var itemAColumn = new DataGridTextColumn { Header = "A", Binding = new System.Windows.Data.Binding("ItemA"), Width = new DataGridLength(110) };
            var itemBColumn = new DataGridTextColumn { Header = "B", Binding = new System.Windows.Data.Binding("ItemB"), Width = new DataGridLength(110) };
            _panelLocalizationBindings.BindColumnHeader(
                clashColumn,
                "Panel_Clash_Column_Name");
            _clashGroupContentsGrid.Columns.Add(indexColumn);
            _clashGroupContentsGrid.Columns.Add(clashColumn);
            _clashGroupContentsGrid.Columns.Add(itemAColumn);
            _clashGroupContentsGrid.Columns.Add(itemBColumn);
            ScrollViewer.SetHorizontalScrollBarVisibility(_clashGroupContentsGrid, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollBarVisibility(_clashGroupContentsGrid, ScrollBarVisibility.Auto);

            Grid.SetRow(_clashGroupContentsGrid, 1);
            layout.Children.Add(_clashGroupContentsGrid);
            return layout;
        }

        private TreeView MakeClashGroupingTree()
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
                ItemContainerStyle = compactItemStyle
            };
            _panelLocalizationBindings.BindToolTip(
                tree,
                "Panel_Clash_GroupTree_Instruction");
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
            _clashFilterPanel.Children.Add(BindPanelText(new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 2),
                FontSize = 10
            }, "Panel_Statuses"));
            MakeCheck(_clashFilterPanel, "New", true, Brushes.Red);
            MakeCheck(_clashFilterPanel, "Active", true, Brushes.OrangeRed);
            MakeCheck(_clashFilterPanel, "Reviewed", true, Brushes.DodgerBlue);
            MakeCheck(_clashFilterPanel, "Approved", false, Brushes.Green);
            MakeCheck(_clashFilterPanel, "Resolved", false, Brushes.Gray);
            filterRow.Children.Add(_clashFilterPanel);

            var columnFilters = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            columnFilters.Children.Add(BindPanelText(
                new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 2), FontSize = 10 },
                "Panel_Clash_Filter_Name_Label"));
            _clashFilterBox = CreateClashColumnFilterBox("Panel_Clash_Filter_Name_Hint", 120);
            columnFilters.Children.Add(_clashFilterBox);
            columnFilters.Children.Add(new TextBlock { Text = "A:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 2), FontSize = 10 });
            _clashItemAFilterBox = CreateClashColumnFilterBox("Panel_ObjectA", 105);
            columnFilters.Children.Add(_clashItemAFilterBox);
            columnFilters.Children.Add(new TextBlock { Text = "B:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 2), FontSize = 10 });
            _clashItemBFilterBox = CreateClashColumnFilterBox("Panel_ObjectB", 105);
            columnFilters.Children.Add(_clashItemBFilterBox);
            filterRow.Children.Add(columnFilters);

            return filterRow;
        }

        private TextBox CreateClashColumnFilterBox(string hintResourceKey, double width)
        {
            var box = new TextBox
            {
                Height = 22,
                Width = width,
                Margin = new Thickness(0, 0, 0, 2),
                FontSize = 11
            };
            _panelLocalizationBindings.BindAction(
                box,
                "Clash.FilterToolTip:" + hintResourceKey,
                () => box.ToolTip = UiLocalizationService.Current.Format(
                    "Panel_Filter0",
                    PanelUi(hintResourceKey)));
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

        private Button ClashActionButton(
            string textResourceKey,
            string toolTipResourceKey,
            Action action,
            double minWidth = 0,
            ButtonKind kind = ButtonKind.Neutral)
        {
            var label = new TextBlock { TextWrapping = TextWrapping.NoWrap };
            _panelLocalizationBindings.BindText(label, textResourceKey);
            var btn = new Button
            {
                Content = label,
                Height = 26,
                MinWidth = minWidth,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 4, 4),
                FontSize = 11,
                Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(kind)
            };
            _panelLocalizationBindings.BindToolTip(btn, toolTipResourceKey);
            btn.Click += (s, e) =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    SetGlobalStatusResource("Panel_Common_Error_Format", Brushes.Red, ex.Message);
                }
            };
            return btn;
        }

        private System.Windows.Controls.Primitives.ToggleButton ClashActionToggle(
            string textResourceKey,
            string toolTipResourceKey)
        {
            var label = new TextBlock { TextWrapping = TextWrapping.NoWrap };
            _panelLocalizationBindings.BindText(label, textResourceKey);
            var toggle = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = label,
                Height = 26,
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 4, 4),
                FontSize = 11,
                Cursor = Cursors.Hand
            };
            _panelLocalizationBindings.BindToolTip(toggle, toolTipResourceKey);
            return toggle;
        }

        private Button ClashTopBarButton(
            string textResourceKey,
            string toolTipResourceKey,
            Action action,
            ButtonKind kind = ButtonKind.Neutral)
        {
            var label = new TextBlock
            {
                TextWrapping = TextWrapping.NoWrap,
                TextAlignment = TextAlignment.Center
            };
            _panelLocalizationBindings.BindText(label, textResourceKey);
            var btn = new Button
            {
                Content = label,
                Height = 24,
                MinWidth = 0,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(0, 0, 3, 0),
                FontSize = 10.5,
                Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(kind)
            };
            _panelLocalizationBindings.BindToolTip(btn, toolTipResourceKey);

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
                        SetGlobalStatusResource("Panel_Common_Error_Format", Brushes.Red, ex.Message);
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
            if (string.IsNullOrWhiteSpace(reason))
                SetGlobalStatusResource("Panel_Clash_InteractiveBusy", Brushes.Orange);
            else
                SetGlobalStatusResource("Panel_Clash_InteractiveBusy_Format", Brushes.Orange, reason);
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
            panel.Children.Add(ClashActionButton(
                "Panel_Clash_Action_Marker",
                "Panel_Clash_Action_Marker_ToolTip",
                ToggleClashMarker));
            _clashMarkerSizeText = new TextBox
            {
                Text = "10",
                Width = 34,
                Height = 22,
                FontSize = 10,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 2, 4)
            };
            _panelLocalizationBindings.BindToolTip(
                _clashMarkerSizeText,
                "Panel_Clash_MarkerRadius_ToolTip");
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
            SetGlobalStatusResource("Panel_Clash_ViewReset", Brushes.Gray);
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
                SetGlobalStatusResource("Panel_Clash_OnlyPairEnabled", Brushes.Orange);
                return;
            }

            PreviewSelectedClash();
        }

        private void DisableSelectedClashPairIsolation()
        {
            _clashMgr.UsePairIsolation = false;
            _clashMgr.ClearPairIsolation();
            SetGlobalStatusResource("Panel_Clash_OnlyPairRestored", Brushes.DarkGreen);
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
            SetGlobalStatusResource("Panel_Clash_OnlyPairRestored", Brushes.DarkGreen);
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
                UiStatusResourceDescriptor detailsDescriptor =
                    PreviewManagerUiStatusMapper.ForTransparencyDetails(
                        _clashMgr.LastTransparencyUiOutcome);
                object details = detailsDescriptor == null
                    ? (object)string.Empty
                    : detailsDescriptor.AsLocalizedArgument();
                SetGlobalStatusResource(
                    hasSelection
                        ? "Panel_Clash_Transparency_Selection_Format"
                        : "Panel_Clash_Transparency_Pair_Format",
                    count > 0 ? Brushes.DarkGreen : Brushes.Orange,
                    count,
                    details);
            }
            catch (Exception ex)
            {
                SetGlobalStatusResource("Panel_Common_Error_Format", Brushes.Red, ex.Message);
            }
        }

        private CheckBox MakeCheck(WrapPanel panel, string text, bool isChecked, Brush color)
        {
            var cb = new CheckBox
            {
                IsChecked = isChecked,
                Foreground = color,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(3, 0, 3, 0),
                FontSize = 11,
                Tag = text
            };
            _panelLocalizationBindings.BindContent(cb, "Panel_Clash_Status_" + text);
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
                var nameText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                _panelLocalizationBindings.BindText(nameText, "Panel_Color_" + name);
                sp.Children.Add(nameText);
                // Tag is null for no highlight and byte[] for a color.
                combo.Items.Add(new ComboBoxItem { Content = sp, Tag = r.HasValue ? new byte[] { r.Value, g.Value, b.Value } : null });
            }
            combo.SelectedIndex = defIdx;
            return combo;
        }

        private ContextMenu BuildClashTestContextMenu()
        {
            var menu = new ContextMenu();
            menu.Items.Add(ClashTestMenuItem("Panel_Clash_Menu_Run", () => ApplySelectedClashTestOperation("run")));
            menu.Items.Add(ClashTestMenuItem("Panel_Clash_Menu_Reset", () => ApplySelectedClashTestOperation("reset")));
            menu.Items.Add(ClashTestMenuItem("Panel_Clash_Menu_Compact", () => ApplySelectedClashTestOperation("compact")));
            menu.Items.Add(new Separator());
            menu.Items.Add(ClashTestMenuItem("Panel_Clash_Menu_CreateViewpoints", CreateViewpointsForSelectedClashTests));
            menu.Items.Add(BuildClashStatusMenu("Panel_Clash_Menu_AllStatuses", true));
            menu.Items.Add(new Separator());
            menu.Items.Add(ClashTestMenuItem("Panel_Clash_Menu_Rename", RenameSelectedClashTest));
            menu.Items.Add(ClashTestMenuItem("Panel_Clash_Menu_Delete", () => ApplySelectedClashTestOperation("delete")));
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
            menu.Items.Add(ClashResultMenuItem("Panel_Clash_Menu_SelectA", () => SelectClashResultItems(ClashResultSelectionMode.ItemA)));
            menu.Items.Add(ClashResultMenuItem("Panel_Clash_Menu_SelectB", () => SelectClashResultItems(ClashResultSelectionMode.ItemB)));
            menu.Items.Add(ClashResultMenuItem("Panel_Clash_Menu_SelectBoth", () => SelectClashResultItems(ClashResultSelectionMode.Both)));
            menu.Items.Add(new Separator());
            menu.Items.Add(ClashResultMenuItem("Panel_Clash_Menu_GroupByA", () => SetClashGrouping(ClashGroupingSide.ItemA)));
            menu.Items.Add(ClashResultMenuItem("Panel_Clash_Menu_GroupByB", () => SetClashGrouping(ClashGroupingSide.ItemB)));
            menu.Items.Add(ClashResultMenuItem("Panel_Clash_Menu_ResetGrouping", ResetSelectedClashGrouping));
            menu.Items.Add(new Separator());
            menu.Items.Add(BuildClashStatusMenu("Panel_Clash_Menu_Status", false));
            menu.Items.Add(ClashResultMenuItem("Panel_Clash_Menu_Assign", SetClashAssignedToPrompt));
            menu.Items.Add(ClashResultMenuItem("Panel_Clash_Menu_Comment", AddClashCommentPrompt));
            menu.Items.Add(new Separator());
            menu.Items.Add(ClashResultMenuItem("Panel_Clash_Menu_GroupSelected", GroupSelectedClashResultsPrompt));
            menu.Items.Add(ClashResultMenuItem("Panel_Clash_Menu_Ungroup", UngroupSelectedClashGroup));
            menu.Items.Add(new Separator());
            menu.Items.Add(ClashResultMenuItem("Panel_Clash_Menu_CreateSelectedViewpoints", CreateViewpointsForSelectedClashResults));
            return menu;
        }

        private MenuItem BuildClashStatusMenu(string headerResourceKey, bool testScope)
        {
            var menu = new MenuItem();
            _panelLocalizationBindings.BindHeader(menu, headerResourceKey);
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
                    ? ClashTestMenuItem("Panel_Clash_Status_" + captured, () => SetSelectedClashStatus(captured, true))
                    : ClashResultMenuItem("Panel_Clash_Status_" + captured, () => SetSelectedClashStatus(captured, false)));
            }
            return menu;
        }

        private MenuItem ClashResultMenuItem(string resourceKey, Action action)
        {
            var item = new MenuItem();
            _panelLocalizationBindings.BindHeader(item, resourceKey);
            item.Click += (s, e) =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Logger.Error("Clash Result action failed: " + ex, "ClashUI");
                    SetGlobalStatusResource("Panel_Common_Error_Format", Brushes.Red, ex.Message);
                    MessageBox.Show(
                        UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message),
                        PanelUi("Panel_Clash_Result_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            };
            return item;
        }

        private MenuItem ClashTestMenuItem(string resourceKey, Action action)
        {
            var item = new MenuItem();
            _panelLocalizationBindings.BindHeader(item, resourceKey);
            item.Click += (s, e) =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    SetGlobalStatusResource("Panel_Common_Error_Format", Brushes.Red, ex.Message);
                    MessageBox.Show(
                        UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message),
                        PanelUi("Panel_Clash_Test_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
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
            if (!ReferenceEquals(e.Column, _clashGroupNameColumn))
            {
                e.Cancel = true;
                return;
            }

            var row = e.Row?.Item as ClashResultGridRow;
            if (row == null || row.VirtualGroupId == null)
            {
                e.Cancel = true;
                SetGlobalStatusResource("Panel_Clash_GroupRenameManualOnly", Brushes.Orange);
            }
        }

        private void OnClashGridCellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit)
                return;

            if (!ReferenceEquals(e.Column, _clashGroupNameColumn))
                return;

            var row = e.Row?.Item as ClashResultGridRow;
            if (row == null || row.VirtualGroupId == null)
                return;

            var editor = e.EditingElement as TextBox;
            var nextName = (editor?.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(nextName))
            {
                SetGlobalStatusResource("Panel_Clash_GroupNameRequired", Brushes.Orange);
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
                    throw new InvalidOperationException(PanelUi("Panel_Clash_EngineUnavailable"));

                var selectedTest = (_testGrid == null ? null : _testGrid.SelectedItem as ClashTestRow)?.Test;
                var persistentGroup = group.PersistentGroup ?? FindClashResultGroup(selectedTest, group.Label, group.Side);
                if (persistentGroup == null)
                    throw new InvalidOperationException(PanelUi("Panel_Clash_SavedGroupNotFound"));

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
                    throw new InvalidOperationException(PanelUi("Panel_Clash_GroupNameDuplicate"));

                using (var transaction = doc.BeginTransaction("NavisHelper Clash Group Rename"))
                {
                    testsData.TestsEditDisplayName(persistentGroup, persistentName);
                    transaction.Commit();
                }

                group.Label = cleanName;
                group.PersistentGroup = persistentGroup;
                SaveActiveClashGroupsToCache();
                RefreshClashGridRows();
                SetGlobalStatusResource("Panel_Clash_GroupRenamed_Format", Brushes.DarkGreen, group.Label);
            }
            catch (Exception ex)
            {
                SetGlobalStatusResource("Panel_Clash_GroupRenameFailed_Format", Brushes.Red, ex.Message);
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
            _clashGroupContentsRowObject = rowObject;
            _clashGroupContentsResults = results;
            if (_clashGroupContentsGrid == null)
                return;

            var rows = BuildClashGroupContentRows(results);
            _clashGroupContentsGrid.ItemsSource = rows;

            if (_clashGroupContentsStatus == null)
                return;

            if (rows.Count == 0)
            {
                _clashGroupContentsStatus.Text = PanelUi("Panel_Clash_SelectResultOrGroup_Hint");
                _clashGroupContentsStatus.Foreground = Brushes.DimGray;
                return;
            }

            var gridRow = rowObject as ClashResultGridRow;
            var label = gridRow != null && gridRow.IsGroup
                ? string.IsNullOrWhiteSpace(gridRow.GroupName) ? gridRow.Name : gridRow.GroupName
                : PanelUi("Panel_Clash_SingleClash");
            var uniqueA = ClashGroupDisplayPolicy.CountDistinctNames(rows.Select(row => row.ItemA));
            var uniqueB = ClashGroupDisplayPolicy.CountDistinctNames(rows.Select(row => row.ItemB));
            _clashGroupContentsStatus.Text = UiLocalizationService.Current.Format(
                "Panel_Clash_GroupContents_Format",
                label,
                rows.Count,
                uniqueA,
                uniqueB);
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
                    Header = BindPanelText(
                        new TextBlock { Foreground = Brushes.Gray, FontStyle = FontStyles.Italic },
                        "Panel_NoData"),
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
                    ToolTip = UiLocalizationService.Current.Format(
                        "Panel_Clash_TreeNode_RightClickHint_Format",
                        entry.Path)
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
                    ToolTip = UiLocalizationService.Current.Format(
                        "Panel_Clash_MergeInside_Format",
                        entry.Path),
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
            var selectInModel = new MenuItem
            {
                Header = PanelUi("Panel_Clash_SelectInModel_Action")
            };
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
                    SetGlobalStatusResource("Panel_Common_NoActiveDocument", Brushes.Orange);
                    return;
                }

                var selection = new ModelItemCollection();
                if (tag == null || !AddSelectableModelItem(selection, tag.Item))
                {
                    SetGlobalStatusResource("Panel_Clash_TreeItemUnavailable", Brushes.Orange);
                    return;
                }

                doc.CurrentSelection.CopyFrom(selection);
                var sideLabel = tag.Side == ClashGroupingSide.ItemA ? "A" : "B";
                SetGlobalStatusResource("Panel_Clash_TreeItemSelected_Format", Brushes.DarkGreen, sideLabel, tag.Label);
            }
            catch (Exception ex)
            {
                SetGlobalStatusResource("Panel_Clash_TreeItemSelectFailed_Format", Brushes.Red, ex.Message);
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
                RefreshApplyClashGroupingToolTip();
            }

            var sideLabel = tag.Side == ClashGroupingSide.ItemA ? "A" : "B";
            if (_clashGroupingStatus != null)
            {
                _clashGroupingStatus.Text = UiLocalizationService.Current.Format(
                    "Panel_Level01Found2",
                    sideLabel,
                    tag.Label,
                    matchCount);
                _clashGroupingStatus.Foreground = Brushes.DarkSlateBlue;
            }
        }

        private void ApplyPendingClashGrouping()
        {
            var tag = _pendingClashGroupingTag;
            if (tag == null)
            {
                SetGlobalStatusResource("Panel_Clash_SelectTreeLevel", Brushes.Orange);
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
                SetGlobalStatusResource("Panel_Clash_GroupCreating_Format", Brushes.Orange, sideLabel, tag.Label);
                SetGlobalBusy(true);
                SetGlobalStatusResource("Panel_Clash_GroupSearching_Format", Brushes.Orange, sideLabel);
                PumpDispatcherOnce();
                var added = AddVirtualClashGroup(tag);
                if (!added)
                    return;

                RefreshClashGridRows();
                UpdateClashGroupingTrees();
                SetGlobalStatusResource("Panel_Clash_GroupAdded_Format", Brushes.DarkGreen, sideLabel, tag.Label);
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
                SetGlobalStatusResource("Panel_Clash_GroupNoResults", Brushes.Orange);
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
                    UiLocalizationService.Current.Format("Panel_Clash_GroupExists_Message_Format", sameGroup.Label),
                    PanelUi("Panel_Clash_GroupExists_Title"),
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
                    UiLocalizationService.Current.Format(
                        "Panel_Clash_GroupOverlap_Message_Format",
                        overlapping.Count,
                        matches.Count),
                    PanelUi("Panel_Clash_GroupOverlap_Title"),
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
                SetGlobalStatusResource("Panel_Clash_GroupNoMove", Brushes.Orange);
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
            RefreshApplyClashGroupingToolTip();
        }

        private void RefreshApplyClashGroupingToolTip()
        {
            if (_applyClashGroupingButton == null)
                return;

            var tag = _pendingClashGroupingTag;
            _applyClashGroupingButton.ToolTip = tag == null
                ? PanelUi("Panel_Clash_GroupTree_SelectLevel_ToolTip")
                : UiLocalizationService.Current.Format(
                    "Panel_Clash_GroupingMatches_ToolTip_Format",
                    FindClashResultsForTreeNode(tag).Count);
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
                SetGlobalStatusResource("Panel_Clash_SelectTestForGroup", Brushes.Orange);
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
                    throw new InvalidOperationException(PanelUi("Panel_Clash_EngineUnavailable"));

                var moved = 0;
                var targetResults = results.Where(result => result != null).Distinct().ToList();
                using (var transaction = doc.BeginTransaction("NavisHelper Clash Grouping"))
                {
                    var group = FindOrCreateClashResultGroup(testsData, selectedTest, groupName);
                    moved = RebuildClashResultGroup(testsData, selectedTest, group, targetResults);
                    if (targetResults.Count > 0 && EnumerateClashResults(group.Children).Count() == 0)
                        throw new InvalidOperationException(PanelUi("Panel_Clash_GroupMoveInvariantFailed"));

                    transaction.Commit();
                    persistedGroup = group;
                }

                SetGlobalStatusResource("Panel_Clash_GroupSaved_Format", Brushes.DarkGreen, groupName, moved);
                return true;
            }
            catch (Exception ex)
            {
                SetGlobalStatusResource("Panel_Clash_GroupSaveFailed_Format", Brushes.Red, ex.Message);
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
                throw new InvalidOperationException(
                    UiLocalizationService.Current.GetString("Panel_Clash_GroupCreateFailed"));

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
