using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using NavisHelper.AI;
using NavisHelper.Core.Localization;

namespace NavisHelper.WPF
{
    /// <summary>
    /// Code-built responsive connection area for the OpenRouter settings card.
    /// Network and persistence behavior remain owned by the settings builder.
    /// </summary>
    internal sealed class OpenRouterConnectionPanel
    {
        private readonly StackPanel _connectForm;
        private readonly Grid _connectedSummary;
        private readonly StackPanel _modelHost;
        private readonly Border _statusChip;
        private readonly TextBlock _statusText;
        private readonly TextBlock _detailText;

        internal OpenRouterConnectionPanel(
            PanelLocalizationBindings bindings,
            UiLocalizationService localization,
            Action connect,
            Action disconnect,
            RequestNavigateEventHandler openKeysPage)
        {
            if (bindings == null)
                throw new ArgumentNullException(nameof(bindings));
            if (localization == null)
                throw new ArgumentNullException(nameof(localization));
            if (connect == null)
                throw new ArgumentNullException(nameof(connect));
            if (disconnect == null)
                throw new ArgumentNullException(nameof(disconnect));
            if (openKeysPage == null)
                throw new ArgumentNullException(nameof(openKeysPage));

            Root = new StackPanel();

            var identityRow = new Grid
            {
                Margin = new Thickness(0, 0, 0, 6)
            };
            identityRow.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            identityRow.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });

            var provider = new Grid
            {
                MinWidth = 0,
                VerticalAlignment = VerticalAlignment.Center,
                ClipToBounds = true,
                Margin = new Thickness(0, 0, 8, 0)
            };
            provider.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            provider.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            var providerLabel = new TextBlock
            {
                FontSize = UiTheme.FontSmall,
                Foreground = UiTheme.TextSecondary,
                Margin = new Thickness(0, 0, 4, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            bindings.BindText(providerLabel, "Settings_Ai_Provider_Label");
            Grid.SetColumn(providerLabel, 0);
            provider.Children.Add(providerLabel);
            var providerValue = new TextBlock
            {
                Text = "OpenRouter",
                FontSize = UiTheme.FontSmall,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(providerValue, 1);
            provider.Children.Add(providerValue);
            Grid.SetColumn(provider, 0);
            identityRow.Children.Add(provider);

            _statusText = new TextBlock
            {
                FontSize = UiTheme.FontSmall,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _statusChip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 112,
                Child = _statusText
            };
            Grid.SetColumn(_statusChip, 1);
            identityRow.Children.Add(_statusChip);
            Root.Children.Add(identityRow);

            _detailText = new TextBlock
            {
                FontSize = UiTheme.FontSmall,
                Foreground = UiTheme.Error,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
                Visibility = Visibility.Collapsed
            };
            Root.Children.Add(_detailText);

            _connectForm = new StackPanel();
            var getKeyText = new TextBlock
            {
                FontSize = UiTheme.FontSmall,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var getKeyLink = new Hyperlink
            {
                NavigateUri = new Uri(
                    "https://openrouter.ai/settings/keys",
                    UriKind.Absolute)
            };
            getKeyLink.RequestNavigate += openKeysPage;
            getKeyText.Inlines.Add(getKeyLink);
            bindings.BindAction(
                getKeyText,
                "Settings.AiGetKeyLink",
                () =>
                {
                    getKeyLink.Inlines.Clear();
                    getKeyLink.Inlines.Add(new Run(localization.GetString(
                        "Settings_Ai_GetKey_Link")));
                });
            _connectForm.Children.Add(getKeyText);

            var keyLabel = new TextBlock
            {
                FontSize = UiTheme.FontSmall,
                Margin = new Thickness(0, 0, 0, 3)
            };
            bindings.BindText(keyLabel, "Settings_Ai_Key_Label");
            _connectForm.Children.Add(keyLabel);

            KeyInput = new PasswordBox
            {
                MinWidth = 0,
                Height = UiTheme.ControlHeight,
                FontSize = UiTheme.FontSmall,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 6)
            };
            _connectForm.Children.Add(KeyInput);

            ConnectButton = LocalizedButton(
                bindings,
                localization,
                "Settings_Ai_Connect_Action",
                "Settings_Ai_Connect_ToolTip",
                connect);
            ConnectButton.HorizontalAlignment = HorizontalAlignment.Left;
            ConnectButton.Style = UiTheme.ButtonStyle(ButtonKind.Primary);
            _connectForm.Children.Add(ConnectButton);

            var keyHelp = new TextBlock
            {
                FontSize = UiTheme.FontCaption,
                Foreground = UiTheme.TextMuted,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
            bindings.BindText(keyHelp, "Settings_Ai_Key_Storage_Help");
            _connectForm.Children.Add(keyHelp);
            Root.Children.Add(_connectForm);

            _connectedSummary = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            _connectedSummary.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            _connectedSummary.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            var connectedText = new TextBlock
            {
                MinWidth = 0,
                FontSize = UiTheme.FontSmall,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            bindings.BindText(
                connectedText,
                "Settings_Ai_Connected_Summary");
            Grid.SetColumn(connectedText, 0);
            _connectedSummary.Children.Add(connectedText);

            DisconnectButton = LocalizedButton(
                bindings,
                localization,
                "Settings_Ai_Disconnect_Action",
                "Settings_Ai_Disconnect_ToolTip",
                disconnect);
            DisconnectButton.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(DisconnectButton, 1);
            _connectedSummary.Children.Add(DisconnectButton);
            Root.Children.Add(_connectedSummary);

            _modelHost = new StackPanel
            {
                MinWidth = 0
            };
            Root.Children.Add(_modelHost);
        }

        internal StackPanel Root { get; }
        internal PasswordBox KeyInput { get; }
        internal Button ConnectButton { get; }
        internal Button DisconnectButton { get; }

        internal void SetModelContent(UIElement content)
        {
            _modelHost.Children.Clear();
            if (content != null)
                _modelHost.Children.Add(content);
        }

        internal void Apply(
            AISettingsConnectionPresentation presentation,
            string headline,
            string detail)
        {
            if (presentation == null)
                throw new ArgumentNullException(nameof(presentation));

            _statusText.Text = headline ?? string.Empty;
            _detailText.Text = detail ?? string.Empty;
            _detailText.Foreground =
                presentation.StatusVisual == AISettingsStatusVisual.Error
                    ? UiTheme.Error
                    : UiTheme.TextSecondary;
            _detailText.Visibility = string.IsNullOrWhiteSpace(detail)
                ? Visibility.Collapsed
                : Visibility.Visible;
            _connectForm.Visibility = presentation.ShowConnectForm
                ? Visibility.Visible
                : Visibility.Collapsed;
            _connectedSummary.Visibility = presentation.ShowConnectedSummary
                ? Visibility.Visible
                : Visibility.Collapsed;
            _modelHost.Visibility = presentation.ShowModelBlock
                ? Visibility.Visible
                : Visibility.Collapsed;
            ConnectButton.Visibility = presentation.ShowConnectForm
                ? Visibility.Visible
                : Visibility.Collapsed;
            DisconnectButton.Visibility = presentation.ShowConnectedSummary
                ? Visibility.Visible
                : Visibility.Collapsed;
            ConnectButton.IsEnabled = presentation.ConnectEnabled;
            DisconnectButton.IsEnabled = presentation.DisconnectEnabled;

            switch (presentation.StatusVisual)
            {
                case AISettingsStatusVisual.Busy:
                    _statusChip.Background = UiTheme.BusyBg;
                    _statusText.Foreground = UiTheme.TextSecondary;
                    break;
                case AISettingsStatusVisual.Success:
                    _statusChip.Background = UiTheme.ChipOkBg;
                    _statusText.Foreground = UiTheme.ChipOkText;
                    break;
                case AISettingsStatusVisual.Error:
                    _statusChip.Background = UiTheme.ChipErrBg;
                    _statusText.Foreground = UiTheme.ChipErrText;
                    break;
                default:
                    _statusChip.Background = UiTheme.RowAlt;
                    _statusText.Foreground = UiTheme.TextSecondary;
                    break;
            }
        }

        private static Button LocalizedButton(
            PanelLocalizationBindings bindings,
            UiLocalizationService localization,
            string textResourceKey,
            string toolTipResourceKey,
            Action onClick)
        {
            var button = UiTheme.ToolButton(
                localization.GetString(textResourceKey),
                localization.GetString(toolTipResourceKey),
                onClick);
            bindings.BindContent(button, textResourceKey);
            bindings.BindToolTip(button, toolTipResourceKey);
            return button;
        }
    }
}
