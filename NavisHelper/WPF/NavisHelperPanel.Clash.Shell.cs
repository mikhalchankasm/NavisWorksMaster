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
        private Border _clashTreeOverlay;
        private System.Windows.Controls.Primitives.ToggleButton _clashTreePanelToggle;
        private bool _suppressClashTreeToggleEvents;
        private System.Windows.Controls.Primitives.ToggleButton _clashSettingsToggle;
        private System.Windows.Controls.Primitives.ToggleButton _clashOnlyPairToggle;
        private bool _suppressClashPairToggleEvents;
        private Border _clashSettingsOverlay;
        private bool _clashGroupPanelVisible;
        private double _clashGroupPanelSavedWidth = 360;

        private TabItem CreateClashTab()
        {
            var savedClashSettings = ClashSettings.Load();
            var testGridStar = ClampClashStar(savedClashSettings.TestGridHeightStar, 1);
            var clashAreaStar = ClampClashStar(savedClashSettings.ClashAreaHeightStar, 2);
            var groupPanelWidth = Math.Max(280, savedClashSettings.ClashGroupPanelWidth);
            _clashGroupPanelSavedWidth = Math.Min(440, groupPanelWidth);
            // Flyouts always start closed; visibility is transient UI state.
            _clashGroupPanelVisible = false;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });   // 0: верхние команды
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });   // 1: тесты / фильтр
            _clashTestGridRow = new RowDefinition { Height = new GridLength(testGridStar, GridUnitType.Star), MinHeight = 80 };
            root.RowDefinitions.Add(_clashTestGridRow); // 2: тесты грид
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });                       // 3: splitter
            _clashListRow = new RowDefinition { Height = new GridLength(clashAreaStar, GridUnitType.Star), MinHeight = 120 };
            root.RowDefinitions.Add(_clashListRow); // 4: коллизии + навигация
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });   // 5: настройки и действия
            root.Margin = new Thickness(4);

            var topBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6), VerticalAlignment = VerticalAlignment.Center };
            var loadTestsBtn = ClashTopBarButton(
                "Panel_Clash_Top_LoadTests",
                "Panel_Clash_Top_LoadTests_ToolTip",
                LoadClashTests);
            topBar.Children.Add(loadTestsBtn);
            var runSelectedTestsBtn = ClashTopBarButton(
                "Panel_Clash_Top_RunSelected",
                "Panel_Clash_Top_RunSelected_ToolTip",
                RunSelectedClashTests,
                ButtonKind.Primary);
            RegisterClashInteractiveButton(runSelectedTestsBtn);
            topBar.Children.Add(runSelectedTestsBtn);
            var runAllTestsBtn = ClashTopBarButton(
                "Panel_Clash_Top_RunAll",
                "Panel_Clash_Top_RunAll_ToolTip",
                RunAllClashTests,
                ButtonKind.Primary);
            RegisterClashInteractiveButton(runAllTestsBtn);
            topBar.Children.Add(runAllTestsBtn);
            var deleteZeroTestsBtn = ClashTopBarButton(
                "Panel_Clash_Top_DeleteZero",
                "Panel_Clash_Top_DeleteZero_ToolTip",
                DeleteZeroClashTests,
                ButtonKind.Destructive);
            RegisterClashInteractiveButton(deleteZeroTestsBtn);
            topBar.Children.Add(deleteZeroTestsBtn);
            topBar.Children.Add(new Border
            {
                Width = 1,
                Height = 24,
                Background = new SolidColorBrush(WpfColor.FromRgb(0xDD, 0xDD, 0xDD)),
                Margin = new Thickness(4, 1, 6, 1)
            });
            var checkAllTestsBtn = ClashTopBarButton(
                "Panel_Clash_Top_ExternalCheck",
                "Panel_Clash_Top_ExternalCheck_ToolTip",
                null,
                ButtonKind.Primary);
            RegisterClashInteractiveButton(checkAllTestsBtn);
            checkAllTestsBtn.Click += (s, e) =>
            {
                if (RejectClashInteractiveBusy("External clash check command"))
                    return;

                SetClashInteractiveControlsEnabled(false);
                using (NavisHelper.Agent.AgentRuntime.BeginInteractiveOperation("External clash check command"))
                {
                    try
                    {
                        ExecutePlugin("RunSaveClashReport.MS");
                        LoadClashTests();
                    }
                    finally
                    {
                        SetClashInteractiveControlsEnabled(true);
                    }
                }
            };
            topBar.Children.Add(checkAllTestsBtn);

            _clashTreePanelToggle = new System.Windows.Controls.Primitives.ToggleButton
            {
                Height = 24,
                Padding = new Thickness(6, 0, 6, 0),
                FontSize = 11,
                Cursor = Cursors.Hand,
                IsChecked = _clashGroupPanelVisible,
                VerticalAlignment = VerticalAlignment.Top
            };
            _panelLocalizationBindings.BindContent(
                _clashTreePanelToggle,
                "Panel_Clash_TreeToggle");
            _panelLocalizationBindings.BindToolTip(
                _clashTreePanelToggle,
                "Panel_Clash_GroupTree_ToolTip");
            _clashTreePanelToggle.Checked += (s, e) =>
            {
                if (!_suppressClashTreeToggleEvents)
                    SetClashGroupPanelVisible(true);
            };
            _clashTreePanelToggle.Unchecked += (s, e) =>
            {
                if (!_suppressClashTreeToggleEvents)
                    SetClashGroupPanelVisible(false);
            };

            _clashSettingsToggle = new System.Windows.Controls.Primitives.ToggleButton
            {
                Content = "⚙",
                Height = 24,
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 11,
                Cursor = Cursors.Hand,
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            _panelLocalizationBindings.BindToolTip(
                _clashSettingsToggle,
                "Panel_Clash_Settings_Description");

            var topBarHost = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
            DockPanel.SetDock(_clashSettingsToggle, Dock.Right);
            topBarHost.Children.Add(_clashSettingsToggle);
            DockPanel.SetDock(_clashTreePanelToggle, Dock.Right);
            topBarHost.Children.Add(_clashTreePanelToggle);
            topBar.Margin = new Thickness(0);
            topBarHost.Children.Add(topBar);
            Grid.SetRow(topBarHost, 0);
            root.Children.Add(topBarHost);

            var testHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            testHeader.Children.Add(ClashCaption("Panel_Clash_Caption_Tests"));
            _testFilterBox = new TextBox { Height = 22, Margin = new Thickness(0, 0, 0, 2), FontSize = 11 };
            _testFilterBox.AcceptsReturn = false;
            _testFilterBox.TextChanged += (s, e) => FilterTestGrid();
            var testFilterLabel = BindPanelText(
                new TextBlock { Foreground = Brushes.Gray, FontSize = 11, IsHitTestVisible = false, Margin = new Thickness(4, 3, 0, 0) },
                "Panel_TestFilter");
            var testFilterContainer = new Grid();
            testFilterContainer.Children.Add(_testFilterBox);
            testFilterContainer.Children.Add(testFilterLabel);
            _testFilterBox.GotFocus += (s, e) => testFilterLabel.Visibility = Visibility.Collapsed;
            _testFilterBox.LostFocus += (s, e) => { if (string.IsNullOrEmpty(_testFilterBox.Text)) testFilterLabel.Visibility = Visibility.Visible; };
            testHeader.Children.Add(testFilterContainer);
            Grid.SetRow(testHeader, 1);
            root.Children.Add(testHeader);

            _testGrid = new DataGrid
            {
                AutoGenerateColumns = false, IsReadOnly = true,
                SelectionMode = DataGridSelectionMode.Extended,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                FontSize = 11, RowHeight = 22
            };
            var testNameColumn = new DataGridTextColumn { Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) };
            var totalColumn = new DataGridTextColumn { Binding = new System.Windows.Data.Binding("Total"), Width = new DataGridLength(45) };
            _panelLocalizationBindings.BindColumnHeader(testNameColumn, "Panel_Test");
            _panelLocalizationBindings.BindColumnHeader(totalColumn, "Panel_Total");
            _testGrid.Columns.Add(testNameColumn);
            _testGrid.Columns.Add(totalColumn);
            var newColumn = new DataGridTextColumn { Binding = new System.Windows.Data.Binding("New"), Width = new DataGridLength(35) };
            var activeColumn = new DataGridTextColumn { Binding = new System.Windows.Data.Binding("Active"), Width = new DataGridLength(35) };
            _panelLocalizationBindings.BindColumnHeader(newColumn, "Panel_Clash_Column_New");
            _panelLocalizationBindings.BindColumnHeader(activeColumn, "Panel_Clash_Column_Active");
            _testGrid.Columns.Add(newColumn);
            _testGrid.Columns.Add(activeColumn);
            _testGrid.SelectionChanged += (s, e) =>
            {
                if (!_suppressClashTestSelectionChanged)
                    OnClashTestSelected();
            };
            _testGrid.PreviewMouseRightButtonDown += OnClashTestGridRightClick;
            _testGrid.ContextMenu = BuildClashTestContextMenu();
            Grid.SetRow(_testGrid, 2);
            root.Children.Add(_testGrid);

            var splitter = new GridSplitter
            {
                Height = 8, HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(WpfColor.FromRgb(0xDF, 0xE3, 0xE8)),
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ShowsPreview = false,
                Cursor = Cursors.SizeNS
            };
            _panelLocalizationBindings.BindToolTip(
                splitter,
                "Panel_Clash_TableSplitter_ToolTip");
            splitter.MouseEnter += (s, e) => splitter.Background = new SolidColorBrush(WpfColor.FromRgb(0xC7, 0xD4, 0xE4));
            splitter.MouseLeave += (s, e) => splitter.Background = new SolidColorBrush(WpfColor.FromRgb(0xDF, 0xE3, 0xE8));
            splitter.DragCompleted += (s, e) => SaveClashSettings();
            Grid.SetRow(splitter, 3);
            root.Children.Add(splitter);

            var clashListLayout = new Grid();
            _clashGrid = new DataGrid
            {
                AutoGenerateColumns = false, IsReadOnly = false,
                SelectionMode = DataGridSelectionMode.Extended,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.All,
                HorizontalGridLinesBrush = new SolidColorBrush(WpfColor.FromRgb(0xD7, 0xDE, 0xE8)),
                VerticalGridLinesBrush = new SolidColorBrush(WpfColor.FromRgb(0xD7, 0xDE, 0xE8)),
                AlternationCount = 2,
                AlternatingRowBackground = new SolidColorBrush(WpfColor.FromRgb(0xF8, 0xFA, 0xFC)),
                Background = Brushes.White,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                FontSize = 11, RowHeight = 22
            };
            _clashGrid.RowStyle = BuildClashGridRowStyle();
            _clashGrid.CellStyle = BuildClashGridCellStyle();
            var statusColumn = new DataGridTextColumn { Binding = new System.Windows.Data.Binding("Status"), Width = new DataGridLength(35), IsReadOnly = true };
            var resultNameColumn = new DataGridTextColumn { Binding = new System.Windows.Data.Binding("Name"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), IsReadOnly = true };
            var groupNameColumn = new DataGridTextColumn
            {
                Binding = new System.Windows.Data.Binding("GroupName")
                {
                    Mode = System.Windows.Data.BindingMode.TwoWay,
                    UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.LostFocus
                },
                Width = new DataGridLength(125),
                IsReadOnly = false
            };
            var distanceColumn = new DataGridTextColumn { Binding = new System.Windows.Data.Binding("Distance"), Width = new DataGridLength(72), IsReadOnly = true };
            _panelLocalizationBindings.BindColumnHeader(statusColumn, "Panel_St");
            _panelLocalizationBindings.BindColumnHeader(resultNameColumn, "Panel_Clash_Column_Name");
            _panelLocalizationBindings.BindColumnHeader(groupNameColumn, "Panel_GroupName");
            _panelLocalizationBindings.BindColumnHeader(distanceColumn, "Panel_Dist");
            _clashGroupNameColumn = groupNameColumn;
            _clashGrid.Columns.Add(statusColumn);
            _clashGrid.Columns.Add(resultNameColumn);
            _clashGrid.Columns.Add(groupNameColumn);
            _clashGrid.Columns.Add(distanceColumn);
            _clashGrid.Columns.Add(new DataGridTextColumn { Header = "A", Binding = new System.Windows.Data.Binding("ItemA"), Width = new DataGridLength(80), IsReadOnly = true });
            _clashGrid.Columns.Add(new DataGridTextColumn { Header = "B", Binding = new System.Windows.Data.Binding("ItemB"), Width = new DataGridLength(80), IsReadOnly = true });
            _clashGrid.SelectionChanged += (s, e) =>
            {
                if (!_suppressClashResultSelectionChanged)
                    UpdateClashGroupingTrees();
            };
            _clashGrid.BeginningEdit += OnClashGridBeginningEdit;
            _clashGrid.CellEditEnding += OnClashGridCellEditEnding;
            _clashGrid.MouseDoubleClick += (s, e) => PreviewSelectedClash();
            _clashGrid.PreviewMouseRightButtonDown += OnClashResultGridRightClick;
            _clashGrid.ContextMenuOpening += OnClashResultContextMenuOpening;
            _clashGrid.ContextMenu = BuildClashResultContextMenu();
            _clashGrid.ContextMenu.Closed += (s, e) => _clashContextMenuItem = null;
            _panelLocalizationBindings.BindAction(
                _clashGrid,
                "Clash.GridRows",
                RefreshClashGridLocalization);
            clashListLayout.Children.Add(_clashGrid);

            var clashArea = new Grid();
            clashArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            clashArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            clashArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 100 });
            var clashCaption = ClashCaption("Panel_Clash_Caption_Clashes");
            clashCaption.Margin = new Thickness(0, 0, 0, 2);
            Grid.SetRow(clashCaption, 0);
            clashArea.Children.Add(clashCaption);
            var clashFilters = BuildClashFilterPanel();
            Grid.SetRow(clashFilters, 1);
            clashArea.Children.Add(clashFilters);
            Grid.SetRow(clashListLayout, 2);
            clashArea.Children.Add(clashListLayout);

            // Тонкая рамка вместо GroupBox — экономит место по вертикали и горизонтали
            var clashListGroup = new Border
            {
                BorderBrush = UiTheme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 2, 6, 6),
                Margin = new Thickness(0, 0, 0, 4),
                Child = clashArea
            };
            Grid.SetRow(clashListGroup, 4);
            root.Children.Add(clashListGroup);

            var settings = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };

            var colorsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            colorsRow.Children.Add(BindPanelText(
                new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 12, 0), FontSize = 11 },
                "Panel_Highlight"));
            colorsRow.Children.Add(new TextBlock { Text = "A", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), FontSize = 11 });
            _clashColorA = MakeColorCombo(1); // Красный
            colorsRow.Children.Add(_clashColorA);
            colorsRow.Children.Add(new TextBlock { Text = "B", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0), FontSize = 11 });
            _clashColorB = MakeColorCombo(4); // Оранжевый
            colorsRow.Children.Add(_clashColorB);
            settings.Children.Add(colorsRow);

            var sectionBoxRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _clashUseSectionBox = new CheckBox
            {
                Content = BindPanelText(
                    new TextBlock { FontSize = 11, LineHeight = 13 },
                    "Panel_Clash_EnableDuringNavigation"),
                IsChecked = true,
                Margin = new Thickness(0, 0, 10, 2),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            };
            _clashUseSectionBox.Unchecked += (s, e) => SectionBoxHelper.Disable();
            sectionBoxRow.Children.Add(_clashUseSectionBox);
            var boxModePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 0)
            };
            _clashBoxModePointRadio = new RadioButton { GroupName = "ClashBoxMode", IsChecked = true, FontSize = 11, Margin = new Thickness(0, 1, 8, 0) };
            _clashBoxModeItemsRadio = new RadioButton { GroupName = "ClashBoxMode", FontSize = 11, Margin = new Thickness(0, 1, 0, 0) };
            _panelLocalizationBindings.BindContent(_clashBoxModePointRadio, "Panel_FromPoint");
            _panelLocalizationBindings.BindContent(_clashBoxModeItemsRadio, "Panel_ByObjects");
            _clashBoxModePointRadio.Checked += (s, e) => ScheduleClashPreviewRefresh();
            _clashBoxModeItemsRadio.Checked += (s, e) => ScheduleClashPreviewRefresh();
            boxModePanel.Children.Add(_clashBoxModePointRadio);
            boxModePanel.Children.Add(_clashBoxModeItemsRadio);
            var boxModeGroup = ClashGroupBox("Panel_Bounds", boxModePanel, 0);
            boxModeGroup.Padding = new Thickness(6, 2, 6, 3);
            boxModeGroup.Margin = new Thickness(0, 0, 10, 2);
            sectionBoxRow.Children.Add(boxModeGroup);

            var offsetBlock = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            offsetBlock.Children.Add(BindPanelText(
                new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), FontSize = 11, Foreground = Brushes.DimGray },
                "Panel_Offset"));
            var offsetSliderBlock = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            _clashOffsetSlider = new Slider
            {
                Minimum = 0,
                Maximum = 10000,
                Value = 1000,
                Width = 135,
                TickFrequency = 250,
                IsSnapToTickEnabled = true,
                TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
                IsMoveToPointEnabled = true,
                AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft,
                AutoToolTipPrecision = 0
            };
            var offsetLabel = new TextBlock
            {
                Text = UiLocalizationService.Current.Format(
                    "Panel_Common_Millimetres_Format",
                    1000),
                MinWidth = 60,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            _clashOffsetSlider.ValueChanged += (s, ev) =>
            {
                offsetLabel.Text = UiLocalizationService.Current.Format(
                    "Panel_Common_Millimetres_Format",
                    (int)_clashOffsetSlider.Value);
                ScheduleClashPreviewRefresh();
            };
            offsetSliderBlock.Children.Add(_clashOffsetSlider);
            offsetBlock.Children.Add(offsetSliderBlock);
            offsetBlock.Children.Add(offsetLabel);
            sectionBoxRow.Children.Add(offsetBlock);
            settings.Children.Add(PanelSectionCard("Panel_SectionBox", sectionBoxRow));

            var contextTransparencyRow = new WrapPanel();
            _clashContextTrans = new CheckBox
            {
                IsChecked = false,
                Margin = new Thickness(0, 1, 14, 4),
                FontSize = 11
            };
            _panelLocalizationBindings.BindContent(
                _clashContextTrans,
                "Panel_ContextTransparency");
            _panelLocalizationBindings.BindToolTip(
                _clashContextTrans,
                "Panel_Clash_Transparency_Description");
            contextTransparencyRow.Children.Add(_clashContextTrans);
            var transBlock = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            transBlock.Children.Add(BindPanelText(
                new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), FontSize = 11, Foreground = Brushes.DimGray },
                "Panel_Level"));
            var transSliderBlock = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            _clashTransSlider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = 70,
                Width = 150,
                TickFrequency = 10,
                IsSnapToTickEnabled = true,
                TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
                IsMoveToPointEnabled = true,
                AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft,
                AutoToolTipPrecision = 0
            };
            _panelLocalizationBindings.BindToolTip(
                _clashTransSlider,
                "Panel_Clash_Transparency_ToolTip");
            var transLabel = new TextBlock { Text = "70%", MinWidth = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = 11, FontWeight = FontWeights.SemiBold };
            _clashTransSlider.ValueChanged += (s, ev) => transLabel.Text = $"{(int)_clashTransSlider.Value}%";
            transSliderBlock.Children.Add(_clashTransSlider);
            transSliderBlock.Children.Add(new TextBlock
            {
                Text = "0             50            100",
                Foreground = Brushes.Gray,
                FontSize = 9,
                Margin = new Thickness(4, -3, 0, 0),
                TextAlignment = TextAlignment.Center
            });
            transBlock.Children.Add(transSliderBlock);
            transBlock.Children.Add(transLabel);
            contextTransparencyRow.Children.Add(transBlock);
            settings.Children.Add(PanelSectionCard("Panel_ViewingTransparency", contextTransparencyRow));

            var viewpointOptionsRow = new WrapPanel();
            _clashDualViewpoints = new CheckBox
            {
                IsChecked = false,
                Margin = new Thickness(0, 1, 14, 4),
                FontSize = 11
            };
            _panelLocalizationBindings.BindContent(_clashDualViewpoints, "Panel_Clash_DualViewpoints_Label");
            _panelLocalizationBindings.BindToolTip(
                _clashDualViewpoints,
                "Panel_Clash_DualViewpoints_ToolTip");
            viewpointOptionsRow.Children.Add(_clashDualViewpoints);
            _clashGroupMarkersForViewpoints = new CheckBox
            {
                IsChecked = true,
                Margin = new Thickness(0, 1, 14, 4),
                FontSize = 11
            };
            _panelLocalizationBindings.BindContent(
                _clashGroupMarkersForViewpoints,
                "Panel_CenterMarks");
            _panelLocalizationBindings.BindToolTip(
                _clashGroupMarkersForViewpoints,
                "Panel_Clash_MarkersForViewpoint_ToolTip");
            viewpointOptionsRow.Children.Add(_clashGroupMarkersForViewpoints);
            settings.Children.Add(PanelSectionCard("Panel_Views_Group_Viewpoints", viewpointOptionsRow));

            SetGlobalStatusResource("Panel_Clash_LoadTestsFirst", Brushes.Gray);

            // Frequently used result actions stay in a named compact panel below the result list.
            var actions = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            _clashOnlyPairToggle = ClashActionToggle(
                "Panel_Clash_Action_OnlyPair",
                "Panel_Clash_Action_OnlyPair_ToolTip");
            _clashOnlyPairToggle.Checked += (s, e) =>
            {
                if (!_suppressClashPairToggleEvents)
                    EnableSelectedClashPairIsolation();
            };
            _clashOnlyPairToggle.Unchecked += (s, e) =>
            {
                if (!_suppressClashPairToggleEvents)
                    DisableSelectedClashPairIsolation();
            };
            actions.Children.Add(_clashOnlyPairToggle);
            actions.Children.Add(ClashActionButton("Panel_Clash_Action_ShowAll", "Panel_Clash_Action_ShowAll_ToolTip", ShowAllAfterClashPairIsolation));
            actions.Children.Add(ClashActionsDivider());
            actions.Children.Add(ClashActionButton("Panel_Clash_Action_Section", "Panel_Clash_Action_Section_ToolTip", SectionBoxHelper.Toggle));
            actions.Children.Add(ClashActionButton("Panel_Reset", "Panel_Clash_Action_ResetView_ToolTip", ResetClashView, kind: ButtonKind.Destructive));
            actions.Children.Add(BuildClashMarkerControl());
            actions.Children.Add(ClashActionButton("Panel_Clash_Action_Plane", "Panel_Clash_Action_Plane_ToolTip", ToggleSelectionPlane));
            actions.Children.Add(ClashActionButton("Panel_Clash_Action_Marks", "Panel_Clash_Action_Marks_ToolTip", DrawSelectedClashCenterMarkers));
            actions.Children.Add(ClashActionButton("Panel_Clash_Action_SaveViewpoint", "Panel_Clash_Action_SaveViewpoint_ToolTip", SaveCurrentClashManualViewpoint));
            actions.Children.Add(ClashActionButton("Panel_Clash_Action_Gif", "Panel_Clash_Action_Gif_ToolTip", CreateClashOrbitGif));
            actions.Children.Add(ClashActionsDivider());
            actions.Children.Add(ClashActionButton("Panel_Clash_Action_AssignedTo", "Panel_Clash_Action_AssignedTo_ToolTip", SetClashAssignedToPrompt));
            actions.Children.Add(ClashActionButton("Panel_Clash_Action_Bcf", "Panel_Clash_Action_Bcf_ToolTip", ExportSelectedClashesToBcf));
            actions.Children.Add(ClashActionsDivider());
            actions.Children.Add(ClashActionButton("Panel_Context", "Panel_Clash_Action_Context_ToolTip", ApplyClashSelectionTransparency));
            var resultActions = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
            resultActions.Children.Add(BindPanelText(new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiTheme.TextSecondary,
                Margin = new Thickness(0, 0, 0, 2)
            }, "Panel_ResultActions"));
            resultActions.Children.Add(actions);
            Grid.SetRow(resultActions, 5);
            root.Children.Add(resultActions);

            // Настройки — оверлей справа (открывается тумблером в топ-баре);
            // не отнимает высоту у таблиц в отличие от прежнего Expander.
            var overlayHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var overlayClose = new Button
            {
                Content = "✕",
                Width = 22,
                Height = 22,
                FontSize = 11,
                Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(ButtonKind.Neutral)
            };
            _panelLocalizationBindings.BindToolTip(
                overlayClose,
                "Panel_CloseSettings");
            overlayClose.Click += (s, e) => _clashSettingsToggle.IsChecked = false;
            DockPanel.SetDock(overlayClose, Dock.Right);
            overlayHeader.Children.Add(overlayClose);
            overlayHeader.Children.Add(BindPanelText(new TextBlock
            {
                FontSize = UiTheme.FontBody,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            }, "Panel_ClashSettings"));

            var overlayLayout = new Grid();
            overlayLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            overlayLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(overlayHeader, 0);
            overlayLayout.Children.Add(overlayHeader);
            var overlayScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = settings
            };
            Grid.SetRow(overlayScroll, 1);
            overlayLayout.Children.Add(overlayScroll);

            _clashSettingsOverlay = new Border
            {
                Background = UiTheme.CardBg,
                BorderBrush = UiTheme.BorderStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                Width = 340,
                MinWidth = 0,
                MaxWidth = 340,
                Visibility = Visibility.Collapsed,
                Child = overlayLayout
            };
            Grid.SetRow(_clashSettingsOverlay, 1);
            Grid.SetRowSpan(_clashSettingsOverlay, 5);
            Panel.SetZIndex(_clashSettingsOverlay, 100);
            root.Children.Add(_clashSettingsOverlay);

            var treeOverlayHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var treeOverlayClose = new Button
            {
                Content = "✕",
                Width = 22,
                Height = 22,
                FontSize = 11,
                Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(ButtonKind.Neutral)
            };
            _panelLocalizationBindings.BindToolTip(
                treeOverlayClose,
                "Panel_CloseTree");
            treeOverlayClose.Click += (s, e) => _clashTreePanelToggle.IsChecked = false;
            DockPanel.SetDock(treeOverlayClose, Dock.Right);
            treeOverlayHeader.Children.Add(treeOverlayClose);
            treeOverlayHeader.Children.Add(BindPanelText(new TextBlock
            {
                FontSize = UiTheme.FontBody,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            }, "Panel_Clash_ObjectTree_Header"));

            var treeOverlayLayout = new Grid();
            treeOverlayLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            treeOverlayLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            treeOverlayLayout.Children.Add(treeOverlayHeader);
            var clashTreePanel = BuildClashGroupingTreePanel();
            clashTreePanel.Margin = new Thickness(0);
            Grid.SetRow(clashTreePanel, 1);
            treeOverlayLayout.Children.Add(clashTreePanel);

            _clashTreeOverlay = new Border
            {
                Background = UiTheme.CardBg,
                BorderBrush = UiTheme.BorderStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Right,
                Width = _clashGroupPanelSavedWidth,
                MinWidth = 0,
                MaxWidth = 440,
                Visibility = Visibility.Collapsed,
                Child = treeOverlayLayout
            };
            Grid.SetRow(_clashTreeOverlay, 1);
            Grid.SetRowSpan(_clashTreeOverlay, 5);
            Panel.SetZIndex(_clashTreeOverlay, 101);
            root.Children.Add(_clashTreeOverlay);
            root.SizeChanged += (sender, args) =>
            {
                _clashSettingsOverlay.Width = Math.Min(340, Math.Max(0, args.NewSize.Width));
                _clashTreeOverlay.Width = Math.Min(_clashGroupPanelSavedWidth, Math.Max(0, args.NewSize.Width));
            };

            _clashSettingsToggle.Checked += (s, e) =>
            {
                if (_clashTreePanelToggle.IsChecked == true)
                    _clashTreePanelToggle.IsChecked = false;
                _clashSettingsOverlay.Visibility = Visibility.Visible;
            };
            _clashSettingsToggle.Unchecked += (s, e) =>
            {
                _clashSettingsOverlay.Visibility = Visibility.Collapsed;
                SaveClashSettings();
            };

            // Загрузка сохранённых настроек
            try
            {
                var cs = savedClashSettings;
                if (cs.ColorAIndex >= 0 && cs.ColorAIndex < _clashColorA.Items.Count) _clashColorA.SelectedIndex = cs.ColorAIndex;
                if (cs.ColorBIndex >= 0 && cs.ColorBIndex < _clashColorB.Items.Count) _clashColorB.SelectedIndex = cs.ColorBIndex;
                _clashOffsetSlider.Value = cs.OffsetMm;
                SetClashBoxModeControls(cs.BoxMode);
                _clashUseSectionBox.IsChecked = cs.UseSectionBox;
                _clashContextTrans.IsChecked = cs.UseContextTransparency;
                _clashDualViewpoints.IsChecked = cs.UseDualClashViewpoints;
                _clashGroupMarkersForViewpoints.IsChecked = cs.UseGroupCenterMarkersForViewpoints;
                _clashTransSlider.Value = cs.TransparencyPercent;
            }
            catch { }

            var tab = new TabItem { Content = root };
            _panelLocalizationBindings.BindHeader(tab, "Panel_Tab_Clashes");
            return tab;
        }

        /// <summary>
        /// Показывает/скрывает дерево группировки как оверлей поверх Clash UI.
        /// </summary>
        private void SetClashGroupPanelVisible(bool visible, bool save = true)
        {
            _clashGroupPanelVisible = visible;
            if (_clashTreeOverlay != null)
                _clashTreeOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

            if (visible && _clashSettingsToggle?.IsChecked == true)
                _clashSettingsToggle.IsChecked = false;

            if (_clashTreePanelToggle != null && _clashTreePanelToggle.IsChecked != visible)
            {
                _suppressClashTreeToggleEvents = true;
                try
                {
                    _clashTreePanelToggle.IsChecked = visible;
                }
                finally
                {
                    _suppressClashTreeToggleEvents = false;
                }
            }

            if (save && !visible) SaveClashSettings();
        }

        private TextBlock ClashCaption(string resourceKey)
        {
            var caption = new TextBlock
            {
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 4, 0, 2)
            };
            _panelLocalizationBindings.BindText(caption, resourceKey);
            return caption;
        }

        private static double ClampClashStar(double value, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.1)
                return fallback;
            return Math.Min(value, 10000);
        }

        /// <summary>Тонкая вертикальная линия между группами кнопок в ряду действий.</summary>
        private static Border ClashActionsDivider()
        {
            return new Border
            {
                Width = 1,
                Height = 22,
                Background = UiTheme.Border,
                Margin = new Thickness(2, 0, 6, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static Border ClashSeparator()
        {
            return new Border
            {
                Height = 1,
                Background = new SolidColorBrush(WpfColor.FromRgb(0xDD, 0xDD, 0xDD)),
                Margin = new Thickness(0, 4, 0, 4)
            };
        }

        private GroupBox ClashGroupBox(
            string headerResourceKey,
            UIElement content,
            double minWidth)
        {
            var group = new GroupBox
            {
                Content = content,
                Padding = new Thickness(8, 5, 8, 6),
                Margin = new Thickness(0, 0, 8, 6),
                FontSize = 11,
                FontWeight = FontWeights.Normal
            };
            if (!string.IsNullOrWhiteSpace(headerResourceKey))
                _panelLocalizationBindings.BindHeader(group, headerResourceKey);

            if (minWidth > 0)
                group.MinWidth = minWidth;

            if (content is FrameworkElement element)
                element.Margin = new Thickness(0);

            return group;
        }
    }
}
