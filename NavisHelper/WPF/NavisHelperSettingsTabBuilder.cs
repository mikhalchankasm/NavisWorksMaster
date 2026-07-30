using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NavisHelper.Core.Localization;

namespace NavisHelper.WPF
{
    /// <summary>
    /// Builds the standalone Settings tab without expanding NavisHelperPanel's
    /// compatibility partial surface.
    /// </summary>
    internal sealed class NavisHelperSettingsTabBuilder
    {
        private const string AiKeyEnvVar = "OPEN_ROUTER_NW_KEY";

        private readonly Action<string, Brush, object[]> _setStatusResource;
        private readonly Action<string> _openFile;
        private readonly Func<string> _getLogPath;
        private readonly Action _openDevScripts;
        private readonly Action<string> _executePlugin;
        private readonly Dispatcher _dispatcher;
        private readonly UiLocalizationService _localization;
        private readonly PanelLocalizationBindings _bindings;

        private Border _aiKeyChip;
        private TextBlock _aiKeyChipLabel;
        private TextBlock _aiTestResultText;
        private int _aiTestGeneration;
        private AiTestDisplayState _aiTestDisplayState;
        private string _aiTestErrorDetail;

        public NavisHelperSettingsTabBuilder(
            Action<string, Brush, object[]> setStatusResource,
            Action<string> openFile,
            Func<string> getLogPath,
            Action openDevScripts,
            Action<string> executePlugin,
            Dispatcher dispatcher,
            PanelLocalizationBindings bindings)
        {
            _setStatusResource = setStatusResource ??
                                 throw new ArgumentNullException(nameof(setStatusResource));
            _openFile = openFile ?? throw new ArgumentNullException(nameof(openFile));
            _getLogPath = getLogPath ?? throw new ArgumentNullException(nameof(getLogPath));
            _openDevScripts = openDevScripts ?? throw new ArgumentNullException(nameof(openDevScripts));
            _executePlugin = executePlugin ?? throw new ArgumentNullException(nameof(executePlugin));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _localization = UiLocalizationService.Current;
        }

        public ComboBox ModelCombo { get; private set; }
        public CheckBox ThinkingCheck { get; private set; }

        private static string AiConfigJsonPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NavisHelper", "ai_config.json");

        public TabItem Build()
        {
            var stack = new StackPanel { Margin = new Thickness(8) };
            stack.Children.Add(UiTheme.SectionCard(
                string.Empty,
                BuildLanguageSettingsSection()));
            stack.Children.Add(BuildLocalizedSection("AI", BuildAiSettingsSection()));
            stack.Children.Add(BuildLocalizedSection(
                "Settings_Service_Section",
                BuildServiceSection()));
            stack.Children.Add(BuildLocalizedSection(
                "Settings_About_Section",
                BuildAboutSection()));
            stack.Children.Add(new TextBlock
            {
                Text = $"NavisHelper {NavisHelper.AppVersion.VersionString}",
                FontSize = UiTheme.FontCaption,
                Foreground = UiTheme.TextMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 8)
            });

            var tab = new TabItem
            {
                Content = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = stack
                }
            };
            _bindings.BindHeader(tab, "Panel_Tab_Settings");
            return tab;
        }

        private UIElement BuildLanguageSettingsSection()
        {
            var radioState = new UiLanguageRadioState(_localization.CurrentLanguage);
            var stack = new StackPanel();
            var sectionTitle = new TextBlock
            {
                FontSize = UiTheme.FontSmall,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiTheme.TextSecondary,
                Margin = new Thickness(0, 0, 0, 6)
            };
            var fieldLabel = new TextBlock
            {
                FontSize = UiTheme.FontBody,
                Margin = new Thickness(0, 0, 0, 4)
            };
            var languageRow = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            stack.Children.Add(sectionTitle);
            stack.Children.Add(fieldLabel);

            var russianRadio = new RadioButton
            {
                Content = _localization.GetString("SettingsLanguageRussianName"),
                GroupName = "NavisHelper.InterfaceLanguage",
                FontSize = UiTheme.FontBody,
                Margin = new Thickness(0, 0, 18, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var englishRadio = new RadioButton
            {
                Content = _localization.GetString("SettingsLanguageEnglishName"),
                GroupName = "NavisHelper.InterfaceLanguage",
                FontSize = UiTheme.FontBody,
                VerticalAlignment = VerticalAlignment.Center
            };
            languageRow.Children.Add(russianRadio);
            languageRow.Children.Add(englishRadio);
            stack.Children.Add(languageRow);

            Action refreshLanguageSection = () =>
            {
                sectionTitle.Text = _localization.GetString("SettingsLanguageSectionTitle");
                fieldLabel.Text = _localization.GetString("SettingsLanguageFieldLabel");
                russianRadio.Content = _localization.GetString("SettingsLanguageRussianName");
                englishRadio.Content = _localization.GetString("SettingsLanguageEnglishName");
                radioState.Refresh(
                    _localization.CurrentLanguage,
                    (isRussianChecked, isEnglishChecked) =>
                    {
                        russianRadio.IsChecked = isRussianChecked;
                        englishRadio.IsChecked = isEnglishChecked;
                    });
            };

            Action<UiLanguage> selectLanguage = selectedLanguage =>
            {
                try
                {
                    bool persisted;
                    if (!radioState.TrySelect(
                            selectedLanguage,
                            _localization.SetManualLanguage,
                            out persisted))
                        return;

                    refreshLanguageSection();
                    _setStatusResource(
                        persisted
                            ? "SettingsLanguageAppliedStatusFormat"
                            : "SettingsLanguagePersistenceWarningFormat",
                        persisted ? UiTheme.Success : UiTheme.Warning,
                        new object[]
                        {
                            UiLocalizedArgument.FromResource(
                                _localization.CurrentLanguage == UiLanguage.Russian
                                    ? "SettingsLanguageRussianName"
                                    : "SettingsLanguageEnglishName")
                        });
                }
                catch (Exception ex)
                {
                    global::NavisHelper.Core.Logger.Error(
                        ex.ToString(),
                        "NavisHelperSettingsTabBuilder.LanguageSelection");
                    try
                    {
                        refreshLanguageSection();
                        _setStatusResource(
                            "SettingsLanguageChangeFailed",
                            UiTheme.Warning,
                            new object[0]);
                    }
                    catch (Exception recoveryEx)
                    {
                        global::NavisHelper.Core.Logger.Error(
                            recoveryEx.ToString(),
                            "NavisHelperSettingsTabBuilder.LanguageSelectionRecovery");
                    }
                }
            };

            russianRadio.Checked += (sender, args) => selectLanguage(UiLanguage.Russian);
            englishRadio.Checked += (sender, args) => selectLanguage(UiLanguage.English);
            _bindings.BindAction(
                stack,
                "Settings.LanguageRadioState",
                refreshLanguageSection);
            radioState.CompleteInitialization();

            return stack;
        }

        private Border BuildLocalizedSection(string resourceKey, UIElement content)
        {
            if (string.Equals(resourceKey, "AI", StringComparison.Ordinal))
                return UiTheme.SectionCard("AI", content);

            var section = new StackPanel();
            var title = new TextBlock
            {
                FontSize = UiTheme.FontSmall,
                FontWeight = FontWeights.SemiBold,
                Foreground = UiTheme.TextSecondary,
                Margin = new Thickness(0, 0, 0, UiTheme.Gap)
            };
            _bindings.BindText(title, resourceKey);
            section.Children.Add(title);
            section.Children.Add(content);
            return UiTheme.SectionCard(string.Empty, section);
        }

        private Button LocalizedToolButton(
            string textResourceKey,
            string toolTipResourceKey,
            Action onClick)
        {
            var button = UiTheme.ToolButton(
                _localization.GetString(textResourceKey),
                _localization.GetString(toolTipResourceKey),
                onClick);
            _bindings.BindContent(button, textResourceKey);
            _bindings.BindToolTip(button, toolTipResourceKey);
            return button;
        }

        private UIElement BuildAiSettingsSection()
        {
            var stack = new StackPanel();
            var keyRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _aiKeyChip = UiTheme.Chip(string.Empty, true, out _aiKeyChipLabel);
            keyRow.Children.Add(_aiKeyChip);
            var refreshButton = LocalizedToolButton(
                "Panel_Refresh",
                "Settings_Ai_RefreshKey_ToolTip",
                RefreshAiKeyStatus);
            refreshButton.Margin = new Thickness(8, 0, 0, 0);
            keyRow.Children.Add(refreshButton);
            stack.Children.Add(keyRow);
            _bindings.BindAction(
                _aiKeyChip,
                "Settings.AiKeyStatus",
                RefreshAiKeyStatus);

            var helpText = new TextBlock
            {
                FontSize = UiTheme.FontSmall,
                TextWrapping = TextWrapping.Wrap,
                Foreground = UiTheme.TextSecondary,
                Margin = new Thickness(0, 4, 0, 4),
            };
            _bindings.BindText(helpText, "Settings_Ai_KeyHelp");
            var helpStack = new StackPanel();
            helpStack.Children.Add(helpText);
            helpStack.Children.Add(LocalizedToolButton(
                "Panel_CopyCommand",
                "Settings_Ai_CopyCommand_ToolTip",
                () =>
                {
                    Clipboard.SetText(
                        "setx " + AiKeyEnvVar + " " +
                        _localization.GetString("SettingsAiKeyPlaceholder"));
                    _setStatusResource(
                        "Settings_Ai_CommandCopied_Status",
                        UiTheme.Success,
                        new object[0]);
                }));
            var helpExpander = new Expander
            {
                FontSize = UiTheme.FontSmall,
                IsExpanded = false,
                Margin = new Thickness(0, 0, 0, 6),
                Content = helpStack
            };
            _bindings.BindHeader(helpExpander, "Settings_Ai_KeyHelp_Title");
            stack.Children.Add(helpExpander);

            var modelLabel = new TextBlock
            {
                FontSize = UiTheme.FontBody,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _bindings.BindText(modelLabel, "Settings_Ai_Model_Label");
            stack.Children.Add(modelLabel);
            var modelRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 6)
            };
            ModelCombo = new ComboBox { Width = 170, Height = 24, FontSize = UiTheme.FontSmall };
            var currentModel = AIConfig.Instance.ModelName;
            var selectedIndex = 0;
            for (var index = 0; index < AIModels.Available.Length; index++)
            {
                ModelCombo.Items.Add(AIModels.Available[index].DisplayName);
                if (AIModels.Available[index].DisplayName == currentModel)
                    selectedIndex = index;
            }
            ModelCombo.SelectedIndex = selectedIndex;
            ModelCombo.SelectionChanged += (sender, args) =>
            {
                AIConfig.Instance.ModelName =
                    ModelCombo.SelectedItem as string ?? AIModels.Available[0].DisplayName;
                AIConfig.Instance.SaveConfig();
            };
            modelRow.Children.Add(ModelCombo);

            ThinkingCheck = new CheckBox
            {
                IsChecked = AIConfig.Instance.EnableThinking,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                FontSize = UiTheme.FontSmall
            };
            _bindings.BindContent(ThinkingCheck, "Panel_Thinking");
            _bindings.BindToolTip(
                ThinkingCheck,
                "Settings_Ai_Thinking_ToolTip");
            ThinkingCheck.Checked += (sender, args) =>
            {
                AIConfig.Instance.EnableThinking = true;
                AIConfig.Instance.SaveConfig();
            };
            ThinkingCheck.Unchecked += (sender, args) =>
            {
                AIConfig.Instance.EnableThinking = false;
                AIConfig.Instance.SaveConfig();
            };
            modelRow.Children.Add(ThinkingCheck);
            stack.Children.Add(modelRow);

            var testRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 4)
            };
            testRow.Children.Add(LocalizedToolButton(
                "Panel_Check",
                "Settings_Ai_Test_ToolTip",
                TestAiApiAccess));
            _aiTestResultText = new TextBlock
            {
                FontSize = UiTheme.FontSmall,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _bindings.BindAction(
                _aiTestResultText,
                "Settings.AiTestResult",
                RefreshAiTestResult);
            testRow.Children.Add(_aiTestResultText);
            stack.Children.Add(testRow);
            stack.Children.Add(LocalizedToolButton(
                "Settings_Ai_OpenConfig_Action",
                "Settings_Ai_OpenConfig_ToolTip",
                () => _openFile(AiConfigJsonPath)));
            return stack;
        }

        private void RefreshAiKeyStatus()
        {
            var hasKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(AiKeyEnvVar));
            UiTheme.SetChipState(
                _aiKeyChip,
                _aiKeyChipLabel,
                _localization.GetString(
                    hasKey
                        ? "Settings_Ai_KeyFound"
                        : "Settings_Ai_KeyMissing"),
                hasKey);
        }

        private void TestAiApiAccess()
        {
            var generation = ++_aiTestGeneration;
            var key = Environment.GetEnvironmentVariable(AiKeyEnvVar);
            if (string.IsNullOrEmpty(key))
            {
                _aiTestDisplayState = AiTestDisplayState.NoKey;
                RefreshAiTestResult();
                return;
            }

            var modelsUrl = AIConfig.Instance.ApiUrl.Replace("/chat/completions", "/models");
            _aiTestDisplayState = AiTestDisplayState.Checking;
            RefreshAiTestResult();

            _ = Task.Run(async () =>
            {
                AiTestDisplayState displayState;
                string errorDetail = null;
                bool ok;
                try
                {
                    using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                    {
                        client.DefaultRequestHeaders.Authorization =
                            new AuthenticationHeaderValue("Bearer", key);
                        var response = await client.GetAsync(modelsUrl).ConfigureAwait(false);
                        ok = response.IsSuccessStatusCode;
                        displayState = ok
                            ? AiTestDisplayState.Success
                            : AiTestDisplayState.Error;
                        if (!ok)
                            errorDetail =
                                ((int)response.StatusCode) + " " + response.ReasonPhrase;
                    }
                }
                catch (TaskCanceledException)
                {
                    ok = false;
                    displayState = AiTestDisplayState.Timeout;
                }
                catch (Exception ex)
                {
                    ok = false;
                    displayState = AiTestDisplayState.Error;
                    errorDetail = ex.Message;
                }

                _ = _dispatcher.BeginInvoke(new Action(() =>
                {
                    if (generation != _aiTestGeneration)
                        return;
                    _aiTestDisplayState = displayState;
                    _aiTestErrorDetail = errorDetail;
                    RefreshAiTestResult();
                }));
            });
        }

        private void RefreshAiTestResult()
        {
            switch (_aiTestDisplayState)
            {
                case AiTestDisplayState.NoKey:
                    _aiTestResultText.Text = _localization.GetString("Settings_Ai_NoApiKey");
                    _aiTestResultText.Foreground = UiTheme.Error;
                    break;
                case AiTestDisplayState.Checking:
                    _aiTestResultText.Text = _localization.GetString("Panel_Checking");
                    _aiTestResultText.Foreground = UiTheme.TextMuted;
                    break;
                case AiTestDisplayState.Success:
                    _aiTestResultText.Text = _localization.GetString("Settings_Ai_ApiAvailable");
                    _aiTestResultText.Foreground = UiTheme.Success;
                    break;
                case AiTestDisplayState.Timeout:
                    _aiTestResultText.Text =
                        _localization.GetString("Settings_Ai_Timeout");
                    _aiTestResultText.Foreground = UiTheme.Error;
                    break;
                case AiTestDisplayState.Error:
                    _aiTestResultText.Text =
                        _localization.Format("Panel_Error0", _aiTestErrorDetail ?? string.Empty);
                    _aiTestResultText.Foreground = UiTheme.Error;
                    break;
                default:
                    _aiTestResultText.Text = string.Empty;
                    _aiTestResultText.Foreground = UiTheme.TextMuted;
                    break;
            }
        }

        private UIElement BuildServiceSection()
        {
            var row = new WrapPanel();
            row.Children.Add(LocalizedToolButton(
                "Panel_OpenLog",
                "Settings_Ai_OpenLog_ToolTip",
                () => _openFile(_getLogPath())));
            row.Children.Add(LocalizedToolButton(
                "Panel_DevScripts_Load_Action",
                "Panel_DevScripts_Load_ToolTip",
                _openDevScripts));
            return row;
        }

        private UIElement BuildAboutSection()
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = $"NavisHelper {NavisHelper.AppVersion.VersionString}",
                FontSize = UiTheme.FontBody,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2)
            });
            stack.Children.Add(new TextBlock
            {
                Text = NavisHelper.AppVersion.Copyright,
                FontSize = UiTheme.FontCaption,
                Foreground = UiTheme.TextMuted,
                Margin = new Thickness(0, 0, 0, 6)
            });
            stack.Children.Add(LocalizedToolButton(
                "Panel_More",
                "Settings_About_Open_ToolTip",
                () => _executePlugin("AboutNavisHelper.CBC")));
            return stack;
        }

        private enum AiTestDisplayState
        {
            None,
            NoKey,
            Checking,
            Success,
            Timeout,
            Error
        }
    }
}
