using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace NavisHelper.WPF
{
    /// <summary>
    /// Builds the standalone Settings tab without expanding NavisHelperPanel's
    /// compatibility partial surface.
    /// </summary>
    internal sealed class NavisHelperSettingsTabBuilder
    {
        private const string AiKeyEnvVar = "OPEN_ROUTER_NW_KEY";

        private readonly Action<string, Brush> _setStatus;
        private readonly Action<string> _openFile;
        private readonly Func<string> _getLogPath;
        private readonly Action _openDevScripts;
        private readonly Action<string> _executePlugin;
        private readonly Dispatcher _dispatcher;

        private Border _aiKeyChip;
        private TextBlock _aiKeyChipLabel;
        private TextBlock _aiTestResultText;
        private int _aiTestGeneration;

        public NavisHelperSettingsTabBuilder(
            Action<string, Brush> setStatus,
            Action<string> openFile,
            Func<string> getLogPath,
            Action openDevScripts,
            Action<string> executePlugin,
            Dispatcher dispatcher)
        {
            _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
            _openFile = openFile ?? throw new ArgumentNullException(nameof(openFile));
            _getLogPath = getLogPath ?? throw new ArgumentNullException(nameof(getLogPath));
            _openDevScripts = openDevScripts ?? throw new ArgumentNullException(nameof(openDevScripts));
            _executePlugin = executePlugin ?? throw new ArgumentNullException(nameof(executePlugin));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public ComboBox ModelCombo { get; private set; }
        public CheckBox ThinkingCheck { get; private set; }

        private static string AiConfigJsonPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NavisHelper", "ai_config.json");

        public TabItem Build()
        {
            var stack = new StackPanel { Margin = new Thickness(8) };
            stack.Children.Add(UiTheme.SectionCard("AI", BuildAiSettingsSection()));
            stack.Children.Add(UiTheme.SectionCard("Сервис", BuildServiceSection()));
            stack.Children.Add(UiTheme.SectionCard("О программе", BuildAboutSection()));
            stack.Children.Add(new TextBlock
            {
                Text = $"NavisHelper {NavisHelper.AppVersion.VersionString}",
                FontSize = UiTheme.FontCaption,
                Foreground = UiTheme.TextMuted,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 8)
            });

            return new TabItem
            {
                Header = "⚙ Настройки",
                Content = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = stack
                }
            };
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
            var refreshButton = UiTheme.ToolButton(
                "Обновить",
                "Перечитать переменную окружения " + AiKeyEnvVar + ".\nПосле setx нужен перезапуск Navisworks.",
                RefreshAiKeyStatus);
            refreshButton.Margin = new Thickness(8, 0, 0, 0);
            keyRow.Children.Add(refreshButton);
            stack.Children.Add(keyRow);
            RefreshAiKeyStatus();

            var helpText = new TextBlock
            {
                FontSize = UiTheme.FontSmall,
                TextWrapping = TextWrapping.Wrap,
                Foreground = UiTheme.TextSecondary,
                Margin = new Thickness(0, 4, 0, 4),
                Text = "1. Получите API-ключ на openrouter.ai (раздел Keys).\n" +
                       "2. Выполните в командной строке Windows:\n" +
                       "    setx " + AiKeyEnvVar + " sk-or-...\n" +
                       "3. Перезапустите Navisworks — ключ подхватится автоматически.\n" +
                       "Ключ хранится только в переменной окружения и не записывается в файлы."
            };
            var helpStack = new StackPanel();
            helpStack.Children.Add(helpText);
            helpStack.Children.Add(UiTheme.ToolButton(
                "Копировать команду",
                "Скопировать команду setx в буфер обмена (останется подставить ключ)",
                () =>
                {
                    Clipboard.SetText("setx " + AiKeyEnvVar + " ВАШ_КЛЮЧ");
                    _setStatus("Команда скопирована — подставьте свой ключ", UiTheme.Success);
                }));
            stack.Children.Add(new Expander
            {
                Header = "Как настроить ключ",
                FontSize = UiTheme.FontSmall,
                IsExpanded = false,
                Margin = new Thickness(0, 0, 0, 6),
                Content = helpStack
            });

            stack.Children.Add(new TextBlock
            {
                Text = "AI модель:",
                FontSize = UiTheme.FontBody,
                Margin = new Thickness(0, 0, 0, 4)
            });
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
                Content = "Thinking",
                IsChecked = AIConfig.Instance.EnableThinking,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                FontSize = UiTheme.FontSmall,
                ToolTip = "Включить режим расширенного мышления (для поддерживающих моделей)"
            };
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
            testRow.Children.Add(UiTheme.ToolButton(
                "Проверить",
                "Проверить доступность OpenRouter API с текущим ключом",
                TestAiApiAccess));
            _aiTestResultText = new TextBlock
            {
                FontSize = UiTheme.FontSmall,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            testRow.Children.Add(_aiTestResultText);
            stack.Children.Add(testRow);
            stack.Children.Add(UiTheme.ToolButton(
                "Открыть ai_config.json",
                "Открыть файл конфигурации AI (модель, температура, таймауты)",
                () => _openFile(AiConfigJsonPath)));
            return stack;
        }

        private void RefreshAiKeyStatus()
        {
            var hasKey = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(AiKeyEnvVar));
            UiTheme.SetChipState(
                _aiKeyChip,
                _aiKeyChipLabel,
                hasKey ? "✅ Ключ " + AiKeyEnvVar + " найден" : "❌ Ключ " + AiKeyEnvVar + " не найден",
                hasKey);
        }

        private void TestAiApiAccess()
        {
            var generation = ++_aiTestGeneration;
            var key = Environment.GetEnvironmentVariable(AiKeyEnvVar);
            if (string.IsNullOrEmpty(key))
            {
                _aiTestResultText.Text = "Ключ не задан";
                _aiTestResultText.Foreground = UiTheme.Error;
                return;
            }

            var modelsUrl = AIConfig.Instance.ApiUrl.Replace("/chat/completions", "/models");
            _aiTestResultText.Text = "Проверка…";
            _aiTestResultText.Foreground = UiTheme.TextMuted;

            _ = Task.Run(async () =>
            {
                string message;
                bool ok;
                try
                {
                    using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                    {
                        client.DefaultRequestHeaders.Authorization =
                            new AuthenticationHeaderValue("Bearer", key);
                        var response = await client.GetAsync(modelsUrl).ConfigureAwait(false);
                        ok = response.IsSuccessStatusCode;
                        message = ok
                            ? "OK — API доступен"
                            : $"Ошибка: {(int)response.StatusCode} {response.ReasonPhrase}";
                    }
                }
                catch (TaskCanceledException)
                {
                    ok = false;
                    message = "Таймаут (10 с) — нет ответа от API";
                }
                catch (Exception ex)
                {
                    ok = false;
                    message = "Ошибка: " + ex.Message;
                }

                _ = _dispatcher.BeginInvoke(new Action(() =>
                {
                    if (generation != _aiTestGeneration)
                        return;
                    _aiTestResultText.Text = message;
                    _aiTestResultText.Foreground = ok ? UiTheme.Success : UiTheme.Error;
                }));
            });
        }

        private UIElement BuildServiceSection()
        {
            var row = new WrapPanel();
            row.Children.Add(UiTheme.ToolButton(
                "Открыть лог",
                "Открыть файл NavisHelper-логов для текущей модели",
                () => _openFile(_getLogPath())));
            row.Children.Add(UiTheme.ToolButton(
                "Dev: загрузить DLL",
                "Загрузить DLL с IDevScript и запустить скрипты",
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
            stack.Children.Add(UiTheme.ToolButton(
                "Подробнее…",
                "Открыть диалог «О программе»",
                () => _executePlugin("AboutNavisHelper.CBC")));
            return stack;
        }
    }
}
