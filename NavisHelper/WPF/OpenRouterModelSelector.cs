using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using NavisHelper.Core.Localization;

namespace NavisHelper.WPF
{
    /// <summary>
    /// Responsive OpenRouter catalog search and selection controls.
    /// Catalog filtering and selection state remain owned by the AI picker.
    /// </summary>
    internal sealed class OpenRouterModelSelector
    {
        internal OpenRouterModelSelector(
            PanelLocalizationBindings bindings,
            UiLocalizationService localization,
            Action refreshModels)
        {
            if (bindings == null)
                throw new ArgumentNullException(nameof(bindings));
            if (localization == null)
                throw new ArgumentNullException(nameof(localization));
            if (refreshModels == null)
                throw new ArgumentNullException(nameof(refreshModels));

            Root = new StackPanel
            {
                MinWidth = 0
            };

            var modelLabel = new TextBlock
            {
                FontSize = UiTheme.FontBody,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            bindings.BindText(modelLabel, "Settings_Ai_Model_Label");
            Root.Children.Add(modelLabel);

            var searchLabel = new TextBlock
            {
                FontSize = UiTheme.FontSmall,
                Margin = new Thickness(0, 0, 0, 3)
            };
            bindings.BindText(searchLabel, "Settings_Ai_Model_Search_Label");
            Root.Children.Add(searchLabel);

            SearchBox = new TextBox
            {
                MinWidth = 0,
                Height = UiTheme.ControlHeight,
                FontSize = UiTheme.FontSmall,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 4)
            };
            bindings.BindAction(
                SearchBox,
                "Settings.AiModelSearch",
                () => SearchBox.ToolTip = localization.GetString(
                    "Settings_Ai_Model_Search_ToolTip"));
            Root.Children.Add(SearchBox);

            ModelCombo = new ComboBox
            {
                MinWidth = 0,
                Height = 30,
                FontSize = UiTheme.FontSmall,
                IsEditable = true,
                IsReadOnly = true,
                IsTextSearchEnabled = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                MaxDropDownHeight = 360,
                ItemTemplate = BuildDropdownItemTemplate(),
                ItemContainerStyle = BuildItemContainerStyle()
            };
            TextSearch.SetTextPath(ModelCombo, "DisplayHeader");
            var comboHost = new Grid
            {
                MinWidth = 0
            };
            comboHost.Children.Add(ModelCombo);
            comboHost.Children.Add(BuildClosedSelectionView(ModelCombo));
            Root.Children.Add(comboHost);

            var catalogRow = new Grid
            {
                MinWidth = 0,
                Margin = new Thickness(0, 6, 0, 0)
            };
            catalogRow.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            catalogRow.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            var catalogText = new StackPanel
            {
                MinWidth = 0,
                Margin = new Thickness(0, 0, 8, 0)
            };
            CountText = new TextBlock
            {
                FontSize = UiTheme.FontCaption,
                Foreground = UiTheme.TextMuted,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            catalogText.Children.Add(CountText);
            StatusText = new TextBlock
            {
                FontSize = UiTheme.FontSmall,
                TextWrapping = TextWrapping.Wrap,
                Foreground = UiTheme.TextSecondary
            };
            catalogText.Children.Add(StatusText);
            Grid.SetColumn(catalogText, 0);
            catalogRow.Children.Add(catalogText);

            RefreshButton = UiTheme.ToolButton(
                localization.GetString("Settings_Ai_RefreshModels_Action"),
                localization.GetString("Settings_Ai_RefreshModels_ToolTip"),
                refreshModels);
            RefreshButton.HorizontalAlignment = HorizontalAlignment.Right;
            RefreshButton.VerticalAlignment = VerticalAlignment.Top;
            bindings.BindContent(
                RefreshButton,
                "Settings_Ai_RefreshModels_Action");
            bindings.BindToolTip(
                RefreshButton,
                "Settings_Ai_RefreshModels_ToolTip");
            Grid.SetColumn(RefreshButton, 1);
            catalogRow.Children.Add(RefreshButton);
            Root.Children.Add(catalogRow);
        }

        internal StackPanel Root { get; }
        internal TextBox SearchBox { get; }
        internal ComboBox ModelCombo { get; }
        internal TextBlock CountText { get; }
        internal TextBlock StatusText { get; }
        internal Button RefreshButton { get; }

        private static TextBlock BuildClosedSelectionView(ComboBox combo)
        {
            var header = new TextBlock
            {
                MinWidth = 0,
                Margin = new Thickness(7, 0, 28, 0),
                Background = UiTheme.CardBg,
                FontSize = UiTheme.FontSmall,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                IsHitTestVisible = false,
                Focusable = false
            };
            header.SetBinding(
                TextBlock.TextProperty,
                new Binding("SelectedItem.DisplayHeader")
                {
                    Source = combo
                });
            return header;
        }

        private static DataTemplate BuildDropdownItemTemplate()
        {
            var template = new DataTemplate();
            var stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(FrameworkElement.MinWidthProperty, 0d);

            var header = new FrameworkElementFactory(typeof(TextBlock));
            header.SetBinding(
                TextBlock.TextProperty,
                new Binding("DisplayHeader"));
            header.SetValue(
                TextBlock.TextTrimmingProperty,
                TextTrimming.CharacterEllipsis);
            header.SetValue(
                TextBlock.FontWeightProperty,
                FontWeights.SemiBold);
            stack.AppendChild(header);

            var capabilities = new FrameworkElementFactory(typeof(TextBlock));
            capabilities.SetBinding(
                TextBlock.TextProperty,
                new Binding("CapabilityText"));
            capabilities.SetValue(
                TextBlock.TextWrappingProperty,
                TextWrapping.Wrap);
            capabilities.SetValue(
                TextBlock.FontSizeProperty,
                UiTheme.FontCaption);
            capabilities.SetValue(
                TextBlock.ForegroundProperty,
                UiTheme.TextMuted);
            stack.AppendChild(capabilities);

            template.VisualTree = stack;
            return template;
        }

        private static Style BuildItemContainerStyle()
        {
            var style = new Style(typeof(ComboBoxItem));
            style.Setters.Add(new Setter(
                Control.HorizontalContentAlignmentProperty,
                HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(
                Control.PaddingProperty,
                new Thickness(6, 4, 6, 4)));
            return style;
        }
    }
}
