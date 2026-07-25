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
        private UIElement CreateImportExportContent()
        {
            var stack = new StackPanel { Margin = new Thickness(8) };

            stack.Children.Add(CreateGroupHeader("Импорт"));
            stack.Children.Add(Btn("csv_import", "\U0001F4CA", "CSV → атрибуты", "Загрузить пользовательские атрибуты из CSV файла (разделитель — точка с запятой) и записать в свойства элементов", "CsvAttributeLoader.CSVL"));
            stack.Children.Add(Btn("import_ps", "\U0001F4C4", "Импорт PS-листов", "Импортировать PS-листы (PipeSpec) в текущий документ", "ImportPslists.CBC"));

            stack.Children.Add(CreateSeparator());
            stack.Children.Add(CreateGroupHeader("Экспорт"));
            stack.Children.Add(Btn("save_hierarchy", "\U0001F4BE", "Сохранить иерархию", "Экспортировать дерево модели в текстовый файл", "SaveHierarhy.COMPANY"));
            stack.Children.Add(Btn("save_nwd2018", "\U0001F4BE", "Сохранить как NWD 2018", "Пересохранить текущий файл в формате Navisworks 2018 (совместимость со старыми версиями)", "SaveAsNavis2018.MS"));
            stack.Children.Add(ActionBtn("export_selected_props", "\U0001F4BE", "Экспорт свойств в Excel", "Сохранить свойства выделения в CSV файл (для открытия в Excel)", ExportSelectedPropertiesToExcelLikeFile, requiresSelection: true));

            return stack;
        }

        // ============================================================
        //  ВКЛАДКА: Виды и разметка
        // ============================================================

        private UIElement CreateViewpointsContent()
        {
            var stack = new StackPanel { Margin = new Thickness(8) };

            stack.Children.Add(CreateGroupHeader("Разметка вида"));
            var viewRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            viewRow.Children.Add(Btn("markup_viewpoint", "\U0001F5BC", "Пометить выделенное", "Пометить выделенные элементы эллипсами текущего вида", "MarkupViewpoint.CBC"));
            viewRow.Children.Add(Btn("top_view", "\U0001F4C6", "Вид сверху + секция", "Переключить в вид сверху, сфокусироваться на выделении и включить секцию", "TopViewSection.CBC"));
            viewRow.Children.Add(Btn("top_view_bbox", "\U000025FC", "Габаритный прямоугольник", "Построить прямоугольник габарита выделения", "TopViewBoundingRect.CBC"));
            viewRow.Children.Add(Btn("top_view_hatch", "\U000025A7", "Заштриховать габарит", "Построить контур и диагональную штриховку габарита выделения", "TopViewBoundingHatch.CBC"));
            stack.Children.Add(viewRow);

            stack.Children.Add(CreateGroupHeader("Маркеры выделения"));
            var markerRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            markerRow.Children.Add(Btn("selection_center_dot_marker", "\U000025CF", "Точки центров", "Временные кружки в центре каждого выделенного объекта", "SelectionCenterDotMarker.CBC"));
            markerRow.Children.Add(Btn("selection_hatch_marker", "\U0001F4CD", "Маркер центров", "Временный многоугольный маркер через центры выделенных объектов", "SelectionHatchMarker.CBC"));
            markerRow.Children.Add(Btn("selection_bounds_hatch_marker", "\U0001F5D0", "Маркер габаритов", "Временный многоугольный маркер по углам bbox выделенных объектов", "SelectionHatchBoundsMarker.CBC"));
            stack.Children.Add(markerRow);

            stack.Children.Add(CreateGroupHeader("Точки обзора"));
            var vpRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            vpRow.Children.Add(Btn("sort_viewpoints", "\U0001F501", "Сортировать VP", "Сортировать все сохранённые точки обзора по имени (алфавитный порядок)", "SortViewpoints.COMPANY"));
            vpRow.Children.Add(Btn("save_viewpoints", "\U0001F4BE", "Сохранить список VP", "Сохранить список точек обзора", "SaveViewpiontList.COMPANY"));
            stack.Children.Add(vpRow);

            stack.Children.Add(CreateGroupHeader("Section Box по выделению"));

            var sectionPanel = new StackPanel();
            var controlsRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            controlsRow.Children.Add(ActionBtn("selection_section_show", "\U0001F4CD", "В выделенные элементы", "Установить Section Box по текущему выделению", ShowSelectionSectionBox, 0, ButtonKind.Primary, true));
            controlsRow.Children.Add(ActionBtn("selection_section_reset", "\U0001F504", "Сброс", "Сбросить Section Box и прозрачность контекста", ResetSelectionSectionBox, 0, ButtonKind.Destructive));
            sectionPanel.Children.Add(controlsRow);

            var expansionPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 2) };
            expansionPanel.Children.Add(CreateSectionAxisSliderRow(
                "Все", 0, 10000, 1000,
                "Общее расширение Section Box одновременно по X, Y и Z. Индивидуальные значения ниже добавляются к общему.",
                out _selOffsetAllSlider));
            expansionPanel.Children.Add(CreateSectionAxisSliderRow(
                "X+", 0, 10000, 0,
                "Дополнительное расширение Section Box по оси X сверх общего значения: добавляется с каждой стороны.",
                out _selOffsetXSlider));
            expansionPanel.Children.Add(CreateSectionAxisSliderRow(
                "Y+", 0, 10000, 0,
                "Дополнительное расширение Section Box по оси Y сверх общего значения: добавляется с каждой стороны.",
                out _selOffsetYSlider));
            expansionPanel.Children.Add(CreateSectionAxisSliderRow(
                "Z+", 0, 10000, 0,
                "Дополнительное расширение Section Box по оси Z сверх общего значения: добавляется с каждой стороны.",
                out _selOffsetZSlider));
            sectionPanel.Children.Add(new Expander
            {
                Header = "Расширение: общее + по осям · шаг 100 мм",
                IsExpanded = true,
                FontSize = 11,
                Content = expansionPanel,
                ToolTip = "Независимо расширяет или сужает Section Box по каждой оси."
            });

            var shiftPanel = new StackPanel { Margin = new Thickness(0, 4, 0, 2) };
            shiftPanel.Children.Add(CreateSectionAxisSliderRow(
                "X", -10000, 10000, 0,
                "Смещение всего Section Box по оси X.",
                out _selShiftXSlider));
            shiftPanel.Children.Add(CreateSectionAxisSliderRow(
                "Y", -10000, 10000, 0,
                "Смещение всего Section Box по оси Y.",
                out _selShiftYSlider));
            shiftPanel.Children.Add(CreateSectionAxisSliderRow(
                "Z", -10000, 10000, 0,
                "Смещение всего Section Box по оси Z.",
                out _selShiftZSlider));
            sectionPanel.Children.Add(new Expander
            {
                Header = "Смещение бокса по осям · шаг 100 мм",
                IsExpanded = true,
                FontSize = 11,
                Content = shiftPanel,
                ToolTip = "Перемещает Section Box целиком, не меняя его размер."
            });

            var transTooltip = "Прозрачность контекстных элементов вне выделения. Применяется автоматически, когда предпросмотр уже показан.";
            var transLabel = new TextBlock { Text = "70%" };
            _selTransSlider = CreateViewSlider(0, 100, 70, 10, transTooltip);
            _selTransSlider.ValueChanged += (s, e) =>
            {
                var value = (int)_selTransSlider.Value;
                transLabel.Text = $"{value}%";
                _selTransSlider.ToolTip = $"{transTooltip}\nТекущее значение: {value}%";
                ScheduleSelectionSectionRefresh();
            };
            sectionPanel.Children.Add(CreateViewSliderRow("Контекст", _selTransSlider, transLabel, "0%", "50%", "100%", transTooltip, 86, 48));

            var checkRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            _selUseSectionBox = new CheckBox
            {
                Content = "Section Box",
                FontSize = 11,
                IsChecked = true,
                Margin = new Thickness(0, 0, 14, 4),
                ToolTip = "Включать section box вокруг текущего выделения."
            };
            _selContextTrans = new CheckBox
            {
                Content = "Прозрачный контекст",
                FontSize = 11,
                IsChecked = false,
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip = "Делать элементы вне выделения полупрозрачными с выбранным процентом прозрачности."
            };
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
                ItemsSource = _selectionSectionHistory,
                ToolTip = "Последние 10 объектов, для которых Section Box применялся явно. Ctrl/Shift — выбрать несколько; двойной щелчок — применить Section Box."
            };
            _selectionSectionHistoryGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Объект",
                Binding = new System.Windows.Data.Binding("ObjectName"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 150
            });
            _selectionSectionHistoryGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Время",
                Binding = new System.Windows.Data.Binding("AppliedAt"),
                Width = new DataGridLength(72)
            });
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
                Text = "Последние объекты",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip = "Ctrl/Shift — выбрать несколько объектов в модели. Двойной щелчок — применить Section Box."
            };
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
            string tooltip,
            out Slider slider)
        {
            var valueLabel = new TextBlock { Text = $"{(int)value} мм" };
            var axisSlider = CreateViewSlider(minimum, maximum, value, 100, tooltip);
            axisSlider.ValueChanged += (s, e) =>
            {
                var currentValue = (int)axisSlider.Value;
                valueLabel.Text = $"{currentValue} мм";
                axisSlider.ToolTip = $"{tooltip}\nТекущее значение: {currentValue} мм. Шаг: 100 мм.";
                if (!_suppressSelectionSectionControlRefresh)
                    ScheduleSelectionSectionRefresh();
            };
            slider = axisSlider;

            return CreateViewSliderRow(
                axis,
                axisSlider,
                valueLabel,
                $"{(int)minimum}",
                $"{(int)((minimum + maximum) / 2.0)}",
                $"{(int)maximum}",
                tooltip,
                38,
                72);
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

        private static FrameworkElement CreateViewSliderRow(
            string labelText,
            Slider slider,
            TextBlock valueLabel,
            string minText,
            string midText,
            string maxText,
            string tooltip,
            double labelWidth,
            double valueWidth)
        {
            var root = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 90 });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(valueWidth) });

            var label = new TextBlock
            {
                Text = labelText,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                ToolTip = tooltip
            };
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
            valueLabel.ToolTip = tooltip;
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
