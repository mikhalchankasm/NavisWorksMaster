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
        private UIElement CreateImportExportContent()
        {
            var stack = new StackPanel { Margin = new Thickness(8) };

            stack.Children.Add(CreateGroupHeader("Panel_Model_Group_Import"));
            stack.Children.Add(Btn("csv_import", "\U0001F4CA", "Panel_Model_ImportCsv_Action", "Panel_Model_ImportCsv_ToolTip", "CsvAttributeLoader.CSVL"));
            stack.Children.Add(Btn("import_ps", "\U0001F4C4", "Panel_Model_ImportPsLists_Action", "Panel_Model_ImportPsLists_ToolTip", "ImportPslists.CBC"));

            stack.Children.Add(CreateSeparator());
            stack.Children.Add(CreateGroupHeader("Panel_Model_Group_Export"));
            stack.Children.Add(Btn("save_hierarchy", "\U0001F4BE", "Panel_SaveHierarchy", "Panel_Model_ExportTree_ToolTip", "SaveHierarhy.COMPANY"));
            stack.Children.Add(Btn("save_nwd2018", "\U0001F4BE", "Panel_Model_Save2018_Action", "Panel_Model_Save2018_ToolTip", "SaveAsNavis2018.MS"));
            stack.Children.Add(ActionBtn("export_selected_props", "\U0001F4BE", "Panel_Model_ExportProperties_Action", "Panel_Model_ExportProperties_ToolTip", ExportSelectedPropertiesToExcelLikeFile, requiresSelection: true));

            return stack;
        }

        // ============================================================
        //  ВКЛАДКА: Виды и разметка
        // ============================================================

        private UIElement CreateViewpointsContent()
        {
            var stack = new StackPanel { Margin = new Thickness(8) };

            stack.Children.Add(CreateGroupHeader("Panel_Views_Group_Markup"));
            var viewRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            viewRow.Children.Add(Btn("markup_viewpoint", "\U0001F5BC", "Panel_MarkSelection", "Panel_Views_MarkSelection_ToolTip", "MarkupViewpoint.CBC"));
            viewRow.Children.Add(Btn("top_view", "\U0001F4C6", "Panel_Views_TopSection_Action", "Panel_Views_TopSection_ToolTip", "TopViewSection.CBC"));
            viewRow.Children.Add(Btn("top_view_bbox", "\U000025FC", "Panel_BoundingRectangle", "Panel_Views_BoundsRectangle_ToolTip", "TopViewBoundingRect.CBC"));
            viewRow.Children.Add(Btn("top_view_hatch", "\U000025A7", "Panel_HatchBounds", "Panel_Views_BoundsHatch_ToolTip", "TopViewBoundingHatch.CBC"));
            stack.Children.Add(viewRow);

            stack.Children.Add(CreateGroupHeader("Panel_Views_Group_SelectionMarkers"));
            var markerRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            markerRow.Children.Add(Btn("selection_center_dot_marker", "\U000025CF", "Panel_CenterPoints", "Panel_Views_CenterPoints_ToolTip", "SelectionCenterDotMarker.CBC"));
            markerRow.Children.Add(Btn("selection_hatch_marker", "\U0001F4CD", "Panel_CenterMarker", "Panel_Views_CenterMarker_ToolTip", "SelectionHatchMarker.CBC"));
            markerRow.Children.Add(Btn("selection_bounds_hatch_marker", "\U0001F5D0", "Panel_BoundsMarker", "Panel_Views_BoundsMarker_ToolTip", "SelectionHatchBoundsMarker.CBC"));
            stack.Children.Add(markerRow);

            stack.Children.Add(CreateGroupHeader("Panel_Views_Group_Viewpoints"));
            var vpRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            vpRow.Children.Add(Btn("sort_viewpoints", "\U0001F501", "Panel_Views_Sort_Action", "Panel_Views_Sort_ToolTip", "SortViewpoints.COMPANY"));
            vpRow.Children.Add(Btn("save_viewpoints", "\U0001F4BE", "Panel_Views_SaveList_Action", "Panel_Views_SaveList_ToolTip", "SaveViewpiontList.COMPANY"));
            stack.Children.Add(vpRow);

            stack.Children.Add(CreateGroupHeader("Panel_Views_Group_SelectionSectionBox"));

            var sectionPanel = new StackPanel();
            var controlsRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            controlsRow.Children.Add(ActionBtn("selection_section_show", "\U0001F4CD", "Panel_Section_FitSelection_Action", "Panel_Section_Show_ToolTip", ShowSelectionSectionBox, 0, ButtonKind.Primary, true));
            controlsRow.Children.Add(ActionBtn("selection_section_reset", "\U0001F504", "Panel_Reset", "Panel_Section_Reset_ToolTip", ResetSelectionSectionBox, 0, ButtonKind.Destructive));
            sectionPanel.Children.Add(controlsRow);

            var expansionPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 2) };
            expansionPanel.Children.Add(CreateSectionAxisSliderRow(
                "Panel_Section_All", 0, 10000, 1000,
                "Panel_Section_ExpansionAll_ToolTip",
                out _selOffsetAllSlider,
                true));
            expansionPanel.Children.Add(CreateSectionAxisSliderRow(
                "X+", 0, 10000, 0,
                "Panel_Section_ExpansionX_ToolTip",
                out _selOffsetXSlider));
            expansionPanel.Children.Add(CreateSectionAxisSliderRow(
                "Y+", 0, 10000, 0,
                "Panel_Section_ExpansionY_ToolTip",
                out _selOffsetYSlider));
            expansionPanel.Children.Add(CreateSectionAxisSliderRow(
                "Z+", 0, 10000, 0,
                "Panel_Section_ExpansionZ_ToolTip",
                out _selOffsetZSlider));
            var expansionExpander = new Expander
            {
                IsExpanded = true,
                FontSize = 11,
                Content = expansionPanel
            };
            _panelLocalizationBindings.BindHeader(
                expansionExpander,
                "Panel_Section_Expansion_GroupLabel");
            _panelLocalizationBindings.BindToolTip(
                expansionExpander,
                "Panel_Section_AxisExpansion_Description");
            sectionPanel.Children.Add(expansionExpander);

            var shiftPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 2) };
            shiftPanel.Children.Add(CreateSectionAxisSliderRow(
                "X", -10000, 10000, 0,
                "Panel_Section_ShiftX_ToolTip",
                out _selShiftXSlider));
            shiftPanel.Children.Add(CreateSectionAxisSliderRow(
                "Y", -10000, 10000, 0,
                "Panel_Section_ShiftY_ToolTip",
                out _selShiftYSlider));
            shiftPanel.Children.Add(CreateSectionAxisSliderRow(
                "Z", -10000, 10000, 0,
                "Panel_Section_ShiftZ_ToolTip",
                out _selShiftZSlider));
            var shiftExpander = new Expander
            {
                IsExpanded = true,
                FontSize = 11,
                Content = shiftPanel
            };
            _panelLocalizationBindings.BindHeader(
                shiftExpander,
                "Panel_Section_Shift_GroupLabel");
            _panelLocalizationBindings.BindToolTip(
                shiftExpander,
                "Panel_Section_Shift_Description");
            sectionPanel.Children.Add(shiftExpander);

            var transLabel = new TextBlock { Text = "70%" };
            _selTransSlider = CreateViewSlider(
                0,
                100,
                70,
                10,
                PanelUi("Panel_Section_ContextTransparency_ToolTip"));
            _selTransSlider.ValueChanged += (s, e) =>
            {
                var value = (int)_selTransSlider.Value;
                transLabel.Text = $"{value}%";
                RefreshContextTransparencyToolTip();
                ScheduleSelectionSectionRefresh();
            };
            _panelLocalizationBindings.BindAction(
                _selTransSlider,
                "SelectionSection.ContextTransparencyToolTip",
                RefreshContextTransparencyToolTip);
            sectionPanel.Children.Add(CreateViewSliderRow(
                "Panel_Context",
                _selTransSlider,
                transLabel,
                "0%",
                "50%",
                "100%",
                PanelUi("Panel_Section_ContextTransparency_ToolTip"),
                86,
                48,
                true));

            var checkRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            _selUseSectionBox = new CheckBox
            {
                FontSize = 11,
                IsChecked = true,
                Margin = new Thickness(0, 0, 14, 4)
            };
            _panelLocalizationBindings.BindContent(_selUseSectionBox, "Panel_SectionBox");
            _panelLocalizationBindings.BindToolTip(
                _selUseSectionBox,
                "Panel_Section_Enable_ToolTip");
            _selContextTrans = new CheckBox
            {
                FontSize = 11,
                IsChecked = false,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _panelLocalizationBindings.BindContent(
                _selContextTrans,
                "Panel_TransparentContext");
            _panelLocalizationBindings.BindToolTip(
                _selContextTrans,
                "Panel_Section_ApplyTransparency_ToolTip");
            _selUseSectionBox.Checked += (s, e) => ScheduleSelectionSectionRefresh();
            _selUseSectionBox.Unchecked += (s, e) => ScheduleSelectionSectionRefresh();
            _selContextTrans.Checked += (s, e) => ScheduleSelectionSectionRefresh();
            _selContextTrans.Unchecked += (s, e) => ScheduleSelectionSectionRefresh();
            checkRow.Children.Add(_selUseSectionBox);
            checkRow.Children.Add(_selContextTrans);
            sectionPanel.Children.Add(checkRow);

            _selectionSectionHistoryGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                MaxHeight = 190,
                MinHeight = 82,
                ItemsSource = _selectionSectionHistory
            };
            _panelLocalizationBindings.BindToolTip(
                _selectionSectionHistoryGrid,
                "Panel_Section_History_Description");
            var objectColumn = new DataGridTextColumn
            {
                Binding = new System.Windows.Data.Binding("ObjectName"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 150
            };
            _panelLocalizationBindings.BindColumnHeader(objectColumn, "Panel_Object");
            _selectionSectionHistoryGrid.Columns.Add(objectColumn);
            var timeColumn = new DataGridTextColumn
            {
                Binding = new System.Windows.Data.Binding("AppliedAt"),
                Width = new DataGridLength(72)
            };
            _panelLocalizationBindings.BindColumnHeader(timeColumn, "Panel_Time");
            _selectionSectionHistoryGrid.Columns.Add(timeColumn);
            _selectionSectionHistoryGrid.SelectionChanged += OnSelectionSectionHistorySelectionChanged;
            _selectionSectionHistoryGrid.MouseDoubleClick += OnSelectionSectionHistoryDoubleClick;

            stack.Children.Add(new Border
            {
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xD7, 0xDE, 0xE8)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 2, 0, 6),
                Background = new SolidColorBrush(WpfColor.FromRgb(0xFA, 0xFB, 0xFD)),
                Child = sectionPanel
            });

            var historyPanel = new DockPanel { LastChildFill = true };
            var historyTitle = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _panelLocalizationBindings.BindText(historyTitle, "Panel_RecentObjects");
            _panelLocalizationBindings.BindToolTip(
                historyTitle,
                "Panel_Clash_Grid_SelectionHint");
            DockPanel.SetDock(historyTitle, Dock.Top);
            historyPanel.Children.Add(historyTitle);
            historyPanel.Children.Add(_selectionSectionHistoryGrid);

            var historyBorder = new Border
            {
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xD7, 0xDE, 0xE8)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(8, 2, 8, 8),
                Background = new SolidColorBrush(WpfColor.FromRgb(0xFA, 0xFB, 0xFD)),
                Child = historyPanel
            };

            var tabLayout = new Grid();
            tabLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            tabLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = stack
            };
            Grid.SetRow(scroll, 0);
            tabLayout.Children.Add(scroll);
            Grid.SetRow(historyBorder, 1);
            tabLayout.Children.Add(historyBorder);

            return tabLayout;
        }

        private FrameworkElement CreateSectionAxisSliderRow(
            string axis,
            double minimum,
            double maximum,
            double value,
            string tooltipResourceKey,
            out Slider slider,
            bool localizeAxis = false)
        {
            var valueLabel = new TextBlock();
            var axisSlider = CreateViewSlider(
                minimum,
                maximum,
                value,
                100,
                PanelUi(tooltipResourceKey));
            Action refreshToolTip = () =>
            {
                string tooltip = PanelUi(tooltipResourceKey);
                axisSlider.ToolTip = UiLocalizationService.Current.Format(
                    "Panel_Section_CurrentValue_FormatMmStep100Mm",
                    tooltip,
                    (int)axisSlider.Value);
                valueLabel.ToolTip = tooltip;
                valueLabel.Text = UiLocalizationService.Current.Format(
                    "Panel_Common_Millimetres_Format",
                    (int)axisSlider.Value);
            };
            axisSlider.ValueChanged += (s, e) =>
            {
                var currentValue = (int)axisSlider.Value;
                refreshToolTip();
                if (!_suppressSelectionSectionControlRefresh)
                    ScheduleSelectionSectionRefresh();
            };
            _panelLocalizationBindings.BindAction(
                axisSlider,
                "SelectionSection.AxisToolTip:" + tooltipResourceKey,
                refreshToolTip);
            slider = axisSlider;

            return CreateViewSliderRow(
                axis,
                axisSlider,
                valueLabel,
                $"{(int)minimum}",
                $"{(int)((minimum + maximum) / 2.0)}",
                $"{(int)maximum}",
                PanelUi(tooltipResourceKey),
                38,
                72,
                localizeAxis);
        }

        private void RefreshContextTransparencyToolTip()
        {
            string tooltip = PanelUi(
                "Panel_Section_ContextTransparency_ToolTip");
            _selTransSlider.ToolTip = UiLocalizationService.Current.Format(
                "Panel_Section_CurrentValue_Format",
                tooltip,
                (int)_selTransSlider.Value);
        }

        private static Slider CreateViewSlider(double minimum, double maximum, double value, double tickFrequency, string tooltip)
        {
            return new Slider
            {
                Minimum = minimum,
                Maximum = maximum,
                Value = value,
                TickFrequency = tickFrequency,
                SmallChange = tickFrequency,
                LargeChange = tickFrequency,
                IsSnapToTickEnabled = true,
                TickPlacement = System.Windows.Controls.Primitives.TickPlacement.BottomRight,
                MinWidth = 80,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = tooltip
            };
        }

        private FrameworkElement CreateViewSliderRow(
            string labelText,
            Slider slider,
            TextBlock valueLabel,
            string minText,
            string midText,
            string maxText,
            string tooltip,
            double labelWidth,
            double valueWidth,
            bool localizeLabel = false)
        {
            var root = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 90 });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(valueWidth) });

            var label = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11
            };
            if (localizeLabel)
                _panelLocalizationBindings.BindText(label, labelText);
            else
                label.Text = labelText;
            Grid.SetColumn(label, 0);
            root.Children.Add(label);

            var sliderBlock = new StackPanel { Orientation = Orientation.Vertical };
            sliderBlock.Children.Add(slider);
            sliderBlock.Children.Add(CreateViewSliderScale(minText, midText, maxText));
            Grid.SetColumn(sliderBlock, 1);
            root.Children.Add(sliderBlock);

            valueLabel.HorizontalAlignment = HorizontalAlignment.Right;
            valueLabel.VerticalAlignment = VerticalAlignment.Center;
            valueLabel.TextAlignment = TextAlignment.Right;
            valueLabel.FontSize = 11;
            valueLabel.FontWeight = FontWeights.SemiBold;
            Grid.SetColumn(valueLabel, 2);
            root.Children.Add(valueLabel);

            return root;
        }

        private static Grid CreateViewSliderScale(string minText, string midText, string maxText)
        {
            var scale = new Grid { Margin = new Thickness(0, -2, 0, 0), IsHitTestVisible = false };
            scale.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scale.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scale.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var min = CreateViewSliderScaleLabel(minText, HorizontalAlignment.Left);
            var mid = CreateViewSliderScaleLabel(midText, HorizontalAlignment.Center);
            var max = CreateViewSliderScaleLabel(maxText, HorizontalAlignment.Right);
            Grid.SetColumn(min, 0);
            Grid.SetColumn(mid, 1);
            Grid.SetColumn(max, 2);
            scale.Children.Add(min);
            scale.Children.Add(mid);
            scale.Children.Add(max);
            return scale;
        }

        private static TextBlock CreateViewSliderScaleLabel(string text, HorizontalAlignment alignment)
        {
            return new TextBlock
            {
                Text = text,
                HorizontalAlignment = alignment,
                FontSize = 9,
                Foreground = Brushes.DimGray
            };
        }

        // ============================================================
        //  ВКЛАДКА: Коллизии
        // ============================================================
    }
}
