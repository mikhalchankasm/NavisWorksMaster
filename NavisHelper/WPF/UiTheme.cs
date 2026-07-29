using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using NavisHelper.Core.Localization;
using WpfColor = System.Windows.Media.Color;
using WpfBorder = System.Windows.Controls.Border;

namespace NavisHelper.WPF
{
    /// <summary>Семантика кнопки для цветовой индикации.</summary>
    internal enum ButtonKind
    {
        /// <summary>Обычное действие — светлая кнопка с тонкой рамкой.</summary>
        Neutral,
        /// <summary>Главное позитивное действие группы — заливка акцентом.</summary>
        Primary,
        /// <summary>Необратимое (удалить/сбросить/изолировать) — красный текст и рамка.</summary>
        Destructive
    }

    /// <summary>
    /// Единая стилевая система панели: палитра, размеры и фабрики общих контролов.
    /// Панель строится кодом (без XAML), поэтому тема — статический класс, а не ResourceDictionary.
    /// </summary>
    internal static class UiTheme
    {
        // ---- Палитра (frozen-кисти; значения консолидированы из существующего кода) ----

        public static readonly Brush TextPrimary = Rgb(0x33, 0x33, 0x33);
        public static readonly Brush TextMuted = Freeze(new SolidColorBrush(Colors.Gray));
        public static readonly Brush TextSecondary = Freeze(new SolidColorBrush(Colors.DimGray));
        public static readonly Brush Border = Rgb(0xDD, 0xDD, 0xDD);
        public static readonly Brush BorderStrong = Rgb(0xCC, 0xCC, 0xCC);
        public static readonly Brush GridLine = Rgb(0xD7, 0xDE, 0xE8);
        public static readonly Brush RowAlt = Rgb(0xF8, 0xFA, 0xFC);
        public static readonly Brush SplitterIdle = Rgb(0xDF, 0xE3, 0xE8);
        public static readonly Brush SplitterHover = Rgb(0xC7, 0xD4, 0xE4);
        public static readonly Brush PanelBg = Rgb(0xF0, 0xF0, 0xF0);
        public static readonly Brush BusyBg = Rgb(0xFF, 0xF4, 0xD6);
        public static readonly Brush CardBg = Freeze(new SolidColorBrush(Colors.White));
        public static readonly Brush Success = Freeze(new SolidColorBrush(Colors.DarkGreen));
        public static readonly Brush Warning = Freeze(new SolidColorBrush(Colors.DarkOrange));
        public static readonly Brush Error = Freeze(new SolidColorBrush(Colors.DarkRed));
        public static readonly Brush ChipOkBg = Rgb(0xE6, 0xF4, 0xEA);
        public static readonly Brush ChipOkText = Rgb(0x1E, 0x7E, 0x34);
        public static readonly Brush ChipErrBg = Rgb(0xFD, 0xE7, 0xE9);
        public static readonly Brush ChipErrText = Rgb(0xA8, 0x1E, 0x2C);

        // ---- Кнопки: акцент (Primary) ----
        public static readonly Brush Accent = Rgb(0x25, 0x69, 0xB4);
        public static readonly Brush AccentHover = Rgb(0x1F, 0x5A, 0x9C);
        public static readonly Brush AccentPressed = Rgb(0x19, 0x49, 0x7F);
        public static readonly Brush AccentText = Freeze(new SolidColorBrush(Colors.White));

        // ---- Кнопки: нейтральные ----
        public static readonly Brush NeutralBtnBg = Freeze(new SolidColorBrush(Colors.White));
        public static readonly Brush NeutralBtnHoverBg = Rgb(0xEE, 0xF3, 0xF9);
        public static readonly Brush NeutralBtnPressedBg = Rgb(0xE0, 0xE8, 0xF2);
        public static readonly Brush NeutralBtnBorder = Rgb(0xCC, 0xCE, 0xD4);

        // ---- Кнопки: деструктивные ----
        public static readonly Brush DestructiveText = Rgb(0xB0, 0x23, 0x30);
        public static readonly Brush DestructiveBorder = Rgb(0xE2, 0xB4, 0xB9);
        public static readonly Brush DestructiveHoverBg = Rgb(0xFC, 0xED, 0xEF);
        public static readonly Brush DestructivePressedBg = Rgb(0xF6, 0xDA, 0xDE);

        // ---- Кнопки: disabled ----
        public static readonly Brush BtnDisabledBg = Rgb(0xF2, 0xF2, 0xF2);
        public static readonly Brush BtnDisabledText = Rgb(0xA0, 0xA0, 0xA0);
        public static readonly Brush BtnDisabledBorder = Rgb(0xE0, 0xE0, 0xE0);

        // ---- Размеры ----

        public const double FontCaption = 10;
        public const double FontSmall = 11;
        public const double FontBody = 12;
        public const double FontHeader = 13;
        public const double ControlHeight = 24;
        public const double ButtonHeight = 26;
        public const double Pad = 6;
        public const double Gap = 4;
        public const double CornerRadius = 4;

        private static Brush Rgb(byte r, byte g, byte b)
        {
            return Freeze(new SolidColorBrush(WpfColor.FromRgb(r, g, b)));
        }

        private static Brush Freeze(SolidColorBrush brush)
        {
            brush.Freeze();
            return brush;
        }

        // ---- Кнопки: плоский стиль с 3 вариантами ----

        private static Style _neutralButtonStyle;
        private static Style _primaryButtonStyle;
        private static Style _destructiveButtonStyle;
        private static Style _textBoxStyle;
        private static Style _checkBoxStyle;
        private static Style _radioButtonStyle;
        private static Style _comboBoxStyle;
        private static Style _sliderStyle;
        private static Style _segmentedRadioStyle;

        /// <summary>Готовый плоский стиль кнопки по семантике. Стили кэшируются и переиспользуются.</summary>
        public static Style ButtonStyle(ButtonKind kind)
        {
            switch (kind)
            {
                case ButtonKind.Primary:
                    return _primaryButtonStyle ?? (_primaryButtonStyle = BuildFlatButtonStyle(
                        Accent, AccentHover, AccentPressed, AccentText, Accent, AccentText));
                case ButtonKind.Destructive:
                    return _destructiveButtonStyle ?? (_destructiveButtonStyle = BuildFlatButtonStyle(
                        NeutralBtnBg, DestructiveHoverBg, DestructivePressedBg, DestructiveText, DestructiveBorder, DestructiveText));
                default:
                    return _neutralButtonStyle ?? (_neutralButtonStyle = BuildFlatButtonStyle(
                        NeutralBtnBg, NeutralBtnHoverBg, NeutralBtnPressedBg, TextPrimary, NeutralBtnBorder, Accent));
            }
        }

        /// <summary>
        /// Плоский стиль кнопки: собственный Border (скруглённый) вместо стандартного WPF-хрома,
        /// заливка/рамка/текст задаются кистями, hover/pressed/disabled — через триггеры шаблона.
        /// Красит свой Border, а не Button.Background — не конфликтует с локальными свойствами кнопки.
        /// </summary>
        private static Style BuildFlatButtonStyle(
            Brush bg,
            Brush hoverBg,
            Brush pressedBg,
            Brush fg,
            Brush border,
            Brush focusBorder)
        {
            // WpfBorder — тип System.Windows.Controls.Border; имя Border в этом классе занято кистью.
            var template = new ControlTemplate(typeof(Button));

            var borderFactory = new FrameworkElementFactory(typeof(WpfBorder), "Bd");
            borderFactory.SetValue(WpfBorder.BackgroundProperty, bg);
            borderFactory.SetValue(WpfBorder.BorderBrushProperty, border);
            borderFactory.SetValue(WpfBorder.BorderThicknessProperty, new Thickness(1));
            borderFactory.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(CornerRadius));
            borderFactory.SetValue(WpfBorder.SnapsToDevicePixelsProperty, true);

            var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty,
                new System.Windows.TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentFactory.SetValue(ContentPresenter.MarginProperty,
                new System.Windows.TemplateBindingExtension(Control.PaddingProperty));
            contentFactory.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            borderFactory.AppendChild(contentFactory);

            template.VisualTree = borderFactory;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(WpfBorder.BackgroundProperty, hoverBg, "Bd"));
            template.Triggers.Add(hover);

            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(WpfBorder.BackgroundProperty, pressedBg, "Bd"));
            template.Triggers.Add(pressed);

            var focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focused.Setters.Add(new Setter(WpfBorder.BorderBrushProperty, focusBorder, "Bd"));
            focused.Setters.Add(new Setter(WpfBorder.BorderThicknessProperty, new Thickness(2), "Bd"));
            template.Triggers.Add(focused);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(WpfBorder.BackgroundProperty, BtnDisabledBg, "Bd"));
            disabled.Setters.Add(new Setter(WpfBorder.BorderBrushProperty, BtnDisabledBorder, "Bd"));
            disabled.Setters.Add(new Setter(Control.ForegroundProperty, BtnDisabledText));
            template.Triggers.Add(disabled);

            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.ForegroundProperty, fg));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            style.Setters.Add(new Setter(Control.SnapsToDevicePixelsProperty, true));
            style.Seal();
            return style;
        }

        // ---- Небутонные контролы: лёгкие implicit-стили ----

        /// <summary>
        /// Installs panel-wide flat styles. Local and explicit styles keep precedence,
        /// so specialized controls can opt out without changing the global theme.
        /// </summary>
        public static void InstallImplicitControlStyles(FrameworkElement root)
        {
            if (root == null) return;

            root.Resources[typeof(TextBox)] = _textBoxStyle ?? (_textBoxStyle = BuildTextBoxStyle());
            root.Resources[typeof(CheckBox)] = _checkBoxStyle ?? (_checkBoxStyle = BuildCheckBoxStyle());
            root.Resources[typeof(RadioButton)] = _radioButtonStyle ?? (_radioButtonStyle = BuildRadioButtonStyle());
            root.Resources[typeof(ComboBox)] = _comboBoxStyle ?? (_comboBoxStyle = BuildComboBoxStyle());
            root.Resources[typeof(Slider)] = _sliderStyle ?? (_sliderStyle = BuildSliderStyle());
        }

        public static Style SegmentedRadioStyle()
        {
            return _segmentedRadioStyle ?? (_segmentedRadioStyle = BuildSegmentedRadioStyle());
        }

        private static Style BuildTextBoxStyle()
        {
            var template = new ControlTemplate(typeof(TextBox));
            var border = new FrameworkElementFactory(typeof(WpfBorder), "Bd");
            border.SetValue(WpfBorder.BackgroundProperty,
                new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(WpfBorder.BorderBrushProperty,
                new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(WpfBorder.BorderThicknessProperty,
                new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(CornerRadius));
            border.SetValue(WpfBorder.SnapsToDevicePixelsProperty, true);

            var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            host.SetValue(FrameworkElement.MarginProperty,
                new TemplateBindingExtension(Control.PaddingProperty));
            border.AppendChild(host);
            template.VisualTree = border;

            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(WpfBorder.BorderBrushProperty, Accent, "Bd"));
            template.Triggers.Add(focus);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(WpfBorder.BackgroundProperty, BtnDisabledBg, "Bd"));
            disabled.Setters.Add(new Setter(Control.ForegroundProperty, BtnDisabledText));
            template.Triggers.Add(disabled);

            var style = new Style(typeof(TextBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, CardBg));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, NeutralBtnBorder));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 2, 5, 2)));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static Style BuildCheckBoxStyle()
        {
            var template = new ControlTemplate(typeof(CheckBox));
            var root = new FrameworkElementFactory(typeof(DockPanel));
            root.SetValue(DockPanel.LastChildFillProperty, true);

            var box = new FrameworkElementFactory(typeof(WpfBorder), "Box");
            box.SetValue(DockPanel.DockProperty, Dock.Left);
            box.SetValue(FrameworkElement.WidthProperty, 16.0);
            box.SetValue(FrameworkElement.HeightProperty, 16.0);
            box.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            box.SetValue(WpfBorder.BackgroundProperty, CardBg);
            box.SetValue(WpfBorder.BorderBrushProperty, NeutralBtnBorder);
            box.SetValue(WpfBorder.BorderThicknessProperty, new Thickness(1));
            box.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(3));
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var mark = new FrameworkElementFactory(typeof(TextBlock), "Mark");
            mark.SetValue(TextBlock.TextProperty, "\u2713");
            mark.SetValue(TextBlock.ForegroundProperty, AccentText);
            mark.SetValue(TextBlock.FontSizeProperty, 12.0);
            mark.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            mark.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            mark.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            mark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            box.AppendChild(mark);
            root.AppendChild(box);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            root.AppendChild(content);
            template.VisualTree = root;

            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(WpfBorder.BackgroundProperty, Accent, "Box"));
            checkedTrigger.Setters.Add(new Setter(WpfBorder.BorderBrushProperty, Accent, "Box"));
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Mark"));
            template.Triggers.Add(checkedTrigger);

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(WpfBorder.BorderBrushProperty, Accent, "Box"));
            template.Triggers.Add(hover);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55));
            template.Triggers.Add(disabled);

            var style = new Style(typeof(CheckBox));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static Style BuildRadioButtonStyle()
        {
            var template = new ControlTemplate(typeof(RadioButton));
            var root = new FrameworkElementFactory(typeof(DockPanel));
            root.SetValue(DockPanel.LastChildFillProperty, true);

            var ring = new FrameworkElementFactory(typeof(WpfBorder), "Ring");
            ring.SetValue(DockPanel.DockProperty, Dock.Left);
            ring.SetValue(FrameworkElement.WidthProperty, 16.0);
            ring.SetValue(FrameworkElement.HeightProperty, 16.0);
            ring.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 6, 0));
            ring.SetValue(WpfBorder.BackgroundProperty, CardBg);
            ring.SetValue(WpfBorder.BorderBrushProperty, NeutralBtnBorder);
            ring.SetValue(WpfBorder.BorderThicknessProperty, new Thickness(1));
            ring.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(8));
            ring.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var dot = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse), "Dot");
            dot.SetValue(FrameworkElement.WidthProperty, 8.0);
            dot.SetValue(FrameworkElement.HeightProperty, 8.0);
            dot.SetValue(System.Windows.Shapes.Shape.FillProperty, Accent);
            dot.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            dot.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            dot.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            ring.AppendChild(dot);
            root.AppendChild(ring);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            root.AppendChild(content);
            template.VisualTree = root;

            var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            checkedTrigger.Setters.Add(new Setter(WpfBorder.BorderBrushProperty, Accent, "Ring"));
            checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "Dot"));
            template.Triggers.Add(checkedTrigger);

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(WpfBorder.BorderBrushProperty, Accent, "Ring"));
            template.Triggers.Add(hover);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55));
            template.Triggers.Add(disabled);

            var style = new Style(typeof(RadioButton));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static Style BuildComboBoxStyle()
        {
            var style = new Style(typeof(ComboBox));
            style.Setters.Add(new Setter(Control.MinHeightProperty, ControlHeight));
            style.Setters.Add(new Setter(Control.BackgroundProperty, CardBg));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, NeutralBtnBorder));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 1, 5, 1)));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));

            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderBrushProperty, Accent));
            style.Triggers.Add(focus);
            return style;
        }

        private static Style BuildSliderStyle()
        {
            var style = new Style(typeof(Slider));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Accent));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            return style;
        }

        private static Style BuildSegmentedRadioStyle()
        {
            var template = new ControlTemplate(typeof(RadioButton));
            var border = new FrameworkElementFactory(typeof(WpfBorder), "Bd");
            border.SetValue(WpfBorder.BackgroundProperty, CardBg);
            border.SetValue(WpfBorder.BorderBrushProperty, NeutralBtnBorder);
            border.SetValue(WpfBorder.BorderThicknessProperty, new Thickness(1));
            border.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(CornerRadius));

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(ContentPresenter.MarginProperty,
                new TemplateBindingExtension(Control.PaddingProperty));
            content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            border.AppendChild(content);
            template.VisualTree = border;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(WpfBorder.BackgroundProperty, NeutralBtnHoverBg, "Bd"));
            template.Triggers.Add(hover);

            var selected = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            selected.Setters.Add(new Setter(WpfBorder.BackgroundProperty, Accent, "Bd"));
            selected.Setters.Add(new Setter(WpfBorder.BorderBrushProperty, Accent, "Bd"));
            selected.Setters.Add(new Setter(Control.ForegroundProperty, AccentText));
            template.Triggers.Add(selected);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(WpfBorder.BackgroundProperty, BtnDisabledBg, "Bd"));
            disabled.Setters.Add(new Setter(Control.ForegroundProperty, BtnDisabledText));
            template.Triggers.Add(disabled);

            var style = new Style(typeof(RadioButton));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextPrimary));
            style.Setters.Add(new Setter(Control.MinHeightProperty, ControlHeight));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        // ---- Фабрики ----

        /// <summary>Карточка-секция: замена GroupBox без тяжёлого хрома и MinWidth.</summary>
        public static Border SectionCard(string header, UIElement content)
        {
            var stack = new StackPanel();
            if (!string.IsNullOrEmpty(header))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = header,
                    FontSize = FontSmall,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = TextSecondary,
                    Margin = new Thickness(0, 0, 0, Gap)
                });
            }
            stack.Children.Add(content);

            return new Border
            {
                CornerRadius = new CornerRadius(CornerRadius),
                BorderBrush = Border,
                BorderThickness = new Thickness(1),
                Background = CardBg,
                Padding = new Thickness(Pad),
                Margin = new Thickness(0, 0, 0, Pad),
                Child = stack
            };
        }

        /// <summary>Статус-чип (например, «✅ Ключ найден»). label — для последующего обновления.</summary>
        public static Border Chip(string text, bool ok, out TextBlock label)
        {
            label = new TextBlock
            {
                Text = text,
                FontSize = FontSmall,
                VerticalAlignment = VerticalAlignment.Center
            };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = label
            };
            SetChipState(chip, label, text, ok);
            return chip;
        }

        public static void SetChipState(Border chip, TextBlock label, string text, bool ok)
        {
            label.Text = text;
            label.Foreground = ok ? ChipOkText : ChipErrText;
            chip.Background = ok ? ChipOkBg : ChipErrBg;
        }

        /// <summary>Компактная кнопка топ-бара: без фиксированной ширины, с try/catch.</summary>
        public static Button ToolButton(string text, string tooltip, Action onClick)
        {
            var btn = new Button
            {
                Content = text,
                ToolTip = tooltip,
                MinWidth = 0,
                Height = ControlHeight,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(0, 0, Gap, 0),
                FontSize = FontSmall,
                Cursor = Cursors.Hand
            };
            btn.Click += (s, e) =>
            {
                try { onClick(); }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message),
                        "NavisHelper",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            };
            return btn;
        }

        /// <summary>Горизонтальный сплиттер (между строками) с hover-подсветкой.</summary>
        public static GridSplitter HSplitter(double height = 8)
        {
            var splitter = new GridSplitter
            {
                Height = height,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Background = SplitterIdle,
                ShowsPreview = false,
                Cursor = Cursors.SizeNS
            };
            AttachHover(splitter);
            return splitter;
        }

        /// <summary>Вертикальный сплиттер (между колонками) с hover-подсветкой.</summary>
        public static GridSplitter VSplitter(double width = 7)
        {
            var splitter = new GridSplitter
            {
                Width = width,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Background = SplitterIdle,
                ShowsPreview = false,
                Cursor = Cursors.SizeWE
            };
            AttachHover(splitter);
            return splitter;
        }

        private static void AttachHover(GridSplitter splitter)
        {
            splitter.MouseEnter += (s, e) => splitter.Background = SplitterHover;
            splitter.MouseLeave += (s, e) => splitter.Background = SplitterIdle;
        }

        /// <summary>
        /// Сегмент-контрол: ряд плоских toggle-кнопок сверху, под ним — все сегменты сразу
        /// (переключение через Visibility, без ре-парентинга — ссылки на поля и состояние
        /// скролла сохраняются). selectSegment — делегат программного переключения.
        /// </summary>
        public static UIElement Segmented(
            string[] headers,
            UIElement[] contents,
            int initialIndex,
            Action<int> onChanged,
            out Action<int> selectSegment,
            out RadioButton[] segmentButtons)
        {
            if (headers == null || contents == null || headers.Length != contents.Length || headers.Length == 0)
                throw new ArgumentException("Segmented headers and contents must be non-empty and have equal lengths.");
            if (initialIndex < 0 || initialIndex >= headers.Length) initialIndex = 0;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var switcher = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, Gap)
            };
            Grid.SetRow(switcher, 0);
            root.Children.Add(switcher);

            var groupName = Guid.NewGuid().ToString("N");
            var radios = new RadioButton[headers.Length];
            segmentButtons = radios;
            var suppress = false;

            for (int i = 0; i < headers.Length; i++)
            {
                var index = i;
                var content = contents[i];
                content.Visibility = i == initialIndex ? Visibility.Visible : Visibility.Collapsed;
                Grid.SetRow((FrameworkElement)content, 1);
                root.Children.Add(content);

                var radio = new RadioButton
                {
                    Content = headers[i],
                    GroupName = groupName,
                    FontSize = FontSmall,
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(0, 0, Gap, 0),
                    Cursor = Cursors.Hand,
                    IsChecked = i == initialIndex
                };
                radio.Style = SegmentedRadioStyle();

                radio.Checked += (s, e) =>
                {
                    if (suppress) return;
                    for (int j = 0; j < contents.Length; j++)
                        contents[j].Visibility = j == index ? Visibility.Visible : Visibility.Collapsed;
                    onChanged?.Invoke(index);
                };
                radios[i] = radio;
                switcher.Children.Add(radio);
            }

            selectSegment = index =>
            {
                if (index < 0 || index >= radios.Length) return;
                if (radios[index].IsChecked == true)
                {
                    // Радио уже активно — просто гарантируем видимость нужного сегмента.
                    for (int j = 0; j < contents.Length; j++)
                        contents[j].Visibility = j == index ? Visibility.Visible : Visibility.Collapsed;
                    return;
                }
                suppress = true;
                try { radios[index].IsChecked = true; }
                finally { suppress = false; }
                for (int j = 0; j < contents.Length; j++)
                    contents[j].Visibility = j == index ? Visibility.Visible : Visibility.Collapsed;
                onChanged?.Invoke(index);
            };

            return root;
        }
    }
}
