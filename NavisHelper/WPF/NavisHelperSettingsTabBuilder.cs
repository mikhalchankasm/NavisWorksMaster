using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using NavisHelper.AI;
using NavisHelper.Core.Localization;

namespace NavisHelper.WPF
{
    internal sealed class DispatcherAISettingsUiBoundary :
        IAISettingsUiBoundary
    {
        private readonly Dispatcher _dispatcher;

        internal DispatcherAISettingsUiBoundary(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ??
                          throw new ArgumentNullException(nameof(dispatcher));
        }

        public Task RunAsync(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            if (_dispatcher.HasShutdownStarted ||
                _dispatcher.HasShutdownFinished)
                return Task.CompletedTask;
            if (_dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    action();
                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }));
            return completion.Task;
        }
    }

    /// <summary>
    /// Builds the standalone Settings tab without expanding NavisHelperPanel's
    /// compatibility partial surface.
    /// </summary>
    internal sealed class NavisHelperSettingsTabBuilder : IDisposable
    {
        private readonly Action<string, Brush, object[]> _setStatusResource;
        private readonly Action<string> _openFile;
        private readonly Func<string> _getLogPath;
        private readonly Action _openDevScripts;
        private readonly Action<string> _executePlugin;
        private readonly Dispatcher _dispatcher;
        private readonly UiLocalizationService _localization;
        private readonly PanelLocalizationBindings _bindings;
        private readonly AISettingsOperationLifetime _aiOperationLifetime =
            new AISettingsOperationLifetime();
        private readonly IAISettingsInfrastructureExecutor _infrastructure;
        private readonly IAISettingsUiBoundary _uiBoundary;
        private readonly AISettingsUiMutationGate _uiMutationGate;
        private PasswordBox _aiKeyInput;
        private Button _aiConnectButton;
        private Button _aiDisconnectButton;
        private Button _aiRefreshModelsButton;
        private TextBlock _aiModelStatusText;
        private TextBox _aiModelSearch;
        private TextBlock _aiModelCountText;
        private OpenRouterConnectionPanel _aiConnectionPanel;
        private OpenRouterModelSelector _aiModelSelector;
        private readonly OpenRouterModelPicker _aiModelPicker =
            new OpenRouterModelPicker();
        private OpenRouterCatalogResult _aiCatalog;
        private AiConnectionDisplayState _aiConnectionState;
        private bool _hasConnectedKey;
        private bool _isCatalogLoading;
        private bool _updatingAiModelUi;
        private bool _isDisposed;

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
            _infrastructure = AISettingsInfrastructureExecutor.CreateDefault(
                Thread.CurrentThread.ManagedThreadId);
            _uiBoundary = new DispatcherAISettingsUiBoundary(dispatcher);
            _uiMutationGate = new AISettingsUiMutationGate(_uiBoundary);
        }

        public ComboBox ModelCombo { get; private set; }

        internal void ResumeAfterLoad()
        {
            if (!_isDisposed)
                VerifyExistingKey();
        }

        internal void CancelPendingOperations()
        {
            _aiOperationLifetime.CancelPendingOperations();
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            _aiOperationLifetime.Dispose();
        }

        public TabItem Build()
        {
            var stack = new StackPanel { Margin = new Thickness(8) };
            stack.Children.Add(BuildLocalizedSection("AI", BuildAiSettingsSection()));
            stack.Children.Add(BuildLocalizedSection(
                "Settings_Service_Section",
                BuildServiceSection()));
            stack.Children.Add(BuildLocalizedSection(
                "SettingsLanguageSectionTitle",
                BuildLanguageSettingsSection()));
            stack.Children.Add(BuildLocalizedSection(
                "Settings_About_Section",
                BuildAboutSection()));

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
            var languageRow = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            var russianRadio = new RadioButton
            {
                Content = _localization.GetString("SettingsLanguageRussianName"),
                GroupName = "NavisHelper.InterfaceLanguage",
                FontSize = UiTheme.FontBody,
                Style = UiTheme.SegmentedRadioStyle(),
                MinWidth = 84,
                Margin = new Thickness(0, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var englishRadio = new RadioButton
            {
                Content = _localization.GetString("SettingsLanguageEnglishName"),
                GroupName = "NavisHelper.InterfaceLanguage",
                FontSize = UiTheme.FontBody,
                Style = UiTheme.SegmentedRadioStyle(),
                MinWidth = 84,
                VerticalAlignment = VerticalAlignment.Center
            };
            languageRow.Children.Add(russianRadio);
            languageRow.Children.Add(englishRadio);
            stack.Children.Add(languageRow);

            Action refreshLanguageSection = () =>
            {
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
            _aiConnectionPanel = new OpenRouterConnectionPanel(
                _bindings,
                _localization,
                ConnectOpenRouter,
                DisconnectOpenRouter,
                OpenOpenRouterKeysPage);
            _aiKeyInput = _aiConnectionPanel.KeyInput;
            _aiConnectButton = _aiConnectionPanel.ConnectButton;
            _aiDisconnectButton = _aiConnectionPanel.DisconnectButton;

            _aiModelSelector = new OpenRouterModelSelector(
                _bindings,
                _localization,
                RefreshModels);
            _aiModelSearch = _aiModelSelector.SearchBox;
            _aiModelSearch.TextChanged += (sender, args) => ApplyModelFilter();
            _aiModelCountText = _aiModelSelector.CountText;
            _bindings.BindAction(
                _aiModelCountText,
                "Settings.AiModelCount",
                () => UpdateModelCount(
                    _aiModelPicker.Filter(_aiModelSearch?.Text).Count));
            ModelCombo = _aiModelSelector.ModelCombo;
            ModelCombo.SelectionChanged +=
                (sender, args) => CommitSelectedModel();
            _bindings.BindAction(
                ModelCombo,
                "Settings.AiModelCapabilities",
                RefreshModelChoiceLocalization);
            _aiRefreshModelsButton = _aiModelSelector.RefreshButton;
            _aiModelStatusText = _aiModelSelector.StatusText;
            _bindings.BindAction(
                _aiModelStatusText,
                "Settings.AiModelState",
                RefreshAiModelDisplay);

            _aiConnectionPanel.SetModelContent(_aiModelSelector.Root);
            _bindings.BindAction(
                _aiConnectionPanel.Root,
                "Settings.AiConnectionState",
                RefreshAiConnectionDisplay);

            RefreshAiConnectionDisplay();
            RefreshAiModelDisplay();
            return _aiConnectionPanel.Root;
        }

        private async void ConnectOpenRouter()
        {
            try
            {
                await AISettingsAsyncBoundary.RunAsync(
                    ConnectOpenRouterAsync,
                    ex => HandleUnexpectedConnectionErrorAsync(null, ex));
            }
            catch
            {
                // The WPF event boundary must never propagate an exception.
            }
        }

        private async Task ConnectOpenRouterAsync()
        {
            var enteredKey = (_aiKeyInput.Password ?? string.Empty).Trim();
            if (!AISettingsConnectInputPolicy.MayStartConnection(enteredKey))
            {
                _hasConnectedKey = false;
                _aiConnectionState = AiConnectionDisplayState.MissingKey;
                RefreshAiConnectionDisplay();
                return;
            }

            var operation = _aiOperationLifetime.Begin(-1);
            if (operation == null)
                return;
            _aiConnectionState = AiConnectionDisplayState.Checking;
            RefreshAiConnectionDisplay();
            await ConnectWithKeyAsync(
                    enteredKey,
                    true,
                    operation)
                .ConfigureAwait(false);
        }

        private async void VerifyExistingKey()
        {
            try
            {
                await AISettingsAsyncBoundary.RunAsync(
                    VerifyExistingKeyAsync,
                    ex => HandleUnexpectedConnectionErrorAsync(null, ex));
            }
            catch
            {
                // ResumeAfterLoad must not launch an unobserved faulted Task.
            }
        }

        private async Task VerifyExistingKeyAsync()
        {
            var operation = _aiOperationLifetime.Begin(-1);
            if (operation == null)
                return;
            await ConnectWithKeyAsync(
                    string.Empty,
                    false,
                    operation)
                .ConfigureAwait(false);
        }

        private async Task ConnectWithKeyAsync(
            string enteredKey,
            bool persist,
            AISettingsOperationLease operation)
        {
            var cancellationToken = operation.CancellationToken;
            OpenRouterKeySnapshot keySnapshot;
            try
            {
                keySnapshot = await _infrastructure.CaptureKeyStateAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                await HandleUnexpectedConnectionErrorAsync(operation, ex)
                    .ConfigureAwait(false);
                return;
            }
            if (!_aiOperationLifetime.IsCurrent(operation))
                return;
            var key = persist ? enteredKey : keySnapshot.Key;
            if (string.IsNullOrWhiteSpace(key))
            {
                await ApplyOnUiThreadAsync(operation, () =>
                {
                    _hasConnectedKey = false;
                    _aiConnectionState = AiConnectionDisplayState.MissingKey;
                    RefreshAiConnectionDisplay();
                }).ConfigureAwait(false);
                return;
            }

            OpenRouterValidationResult validation;
            bool validationTimedOut;
            bool validationCancelled;
            try
            {
                using (var validationTimeout =
                           new CancellationTokenSource(
                               AISettingsOperationPolicy.KeyValidationTimeout))
                {
                    try
                    {
                        validation = await _infrastructure.ValidateKeyAsync(
                                key,
                                cancellationToken,
                                validationTimeout.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        var failure =
                            AISettingsOperationPolicy.EffectiveFailure(
                                OpenRouterFailureKind.Cancelled,
                                validationTimeout.Token,
                                cancellationToken);
                        await ApplyOnUiThreadAsync(operation, () =>
                        {
                            _aiConnectionState = MapConnectionFailure(
                                failure,
                                cancellationToken.IsCancellationRequested,
                                validationTimeout.IsCancellationRequested);
                            RefreshAiConnectionDisplay();
                        }).ConfigureAwait(false);
                        return;
                    }
                    validationTimedOut =
                        validationTimeout.IsCancellationRequested;
                    validationCancelled =
                        cancellationToken.IsCancellationRequested;
                    var effectiveValidationFailure =
                        AISettingsOperationPolicy.EffectiveFailure(
                            validation.FailureKind,
                            validationTimeout.Token,
                            cancellationToken);
                    validation = effectiveValidationFailure ==
                                 validation.FailureKind
                        ? validation
                        : OpenRouterValidationResult.Failure(
                            effectiveValidationFailure,
                            validation.HttpStatus);
                }
            }
            catch (Exception ex)
            {
                await HandleUnexpectedConnectionErrorAsync(operation, ex)
                    .ConfigureAwait(false);
                return;
            }

            var validationFailure = AISettingsOperationPolicy.EffectiveFailure(
                validation.FailureKind,
                validationTimedOut,
                validationCancelled);
            if (!_aiOperationLifetime.IsCurrent(operation))
                return;
            if (!AISettingsOperationPolicy.MayMutateKey(
                    validation,
                    validationTimedOut,
                    validationCancelled))
            {
                await ApplyOnUiThreadAsync(operation, () =>
                {
                    _aiConnectionState = MapConnectionFailure(
                        validationFailure,
                        validationCancelled,
                        validationTimedOut);
                    RefreshAiConnectionDisplay();
                }).ConfigureAwait(false);
                return;
            }

            KeyStoreMutationResult mutation;
            try
            {
                mutation = await _infrastructure.PersistKeyAsync(
                        key,
                        persist,
                        keySnapshot.Generation,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                await HandleUnexpectedConnectionErrorAsync(operation, ex)
                    .ConfigureAwait(false);
                return;
            }
            if (!_aiOperationLifetime.IsCurrent(operation) ||
                !mutation.GenerationMatched)
                return;
            if (!mutation.IsSuccess ||
                (persist && !mutation.IsFullyConnected) ||
                (!persist &&
                 (!mutation.HasProcessValue ||
                  !mutation.HasRuntimeValue)))
            {
                await ApplyOnUiThreadAsync(operation, () =>
                {
                    _aiConnectionState = AiConnectionDisplayState.StorageFailed;
                    RefreshAiConnectionDisplay();
                }).ConfigureAwait(false);
                return;
            }

            await ApplyOnUiThreadAsync(operation, () =>
            {
                _hasConnectedKey = true;
                _aiKeyInput.Clear();
                _aiConnectionState = AiConnectionDisplayState.Connected;
                RefreshAiConnectionDisplay();
            }).ConfigureAwait(false);
            if (!_aiOperationLifetime.IsCurrent(operation))
                return;
            await RefreshModelCatalogAsync(
                    key,
                    operation,
                    mutation.Generation)
                .ConfigureAwait(false);
        }

        private async Task RefreshModelCatalogAsync(
            string key,
            AISettingsOperationLease operation,
            int keyGeneration)
        {
            await ApplyOnUiThreadAsync(operation, () =>
            {
                _isCatalogLoading = true;
                _aiCatalog = null;
                ClearModelChoices();
                _aiConnectionState = AiConnectionDisplayState.Connected;
                RefreshAiConnectionDisplay();
                RefreshAiModelDisplay();
            }).ConfigureAwait(false);
            if (!_aiOperationLifetime.IsCurrent(operation))
                return;
            OpenRouterCatalogResult catalog;
            bool catalogTimedOut;
            bool catalogCancelled;
            try
            {
                using (var catalogTimeout =
                           new CancellationTokenSource(
                               AISettingsOperationPolicy.ModelCatalogTimeout))
                {
                    try
                    {
                        catalog = await _infrastructure.LoadModelsAsync(
                                key,
                                operation.CancellationToken,
                                catalogTimeout.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        var failure =
                            AISettingsOperationPolicy.EffectiveFailure(
                                OpenRouterFailureKind.Cancelled,
                                catalogTimeout.Token,
                                operation.CancellationToken);
                        if (!_aiOperationLifetime.IsCurrent(operation))
                            return;
                        catalog = OpenRouterCatalogResult.Unavailable(failure);
                    }
                    catalogTimedOut = catalogTimeout.IsCancellationRequested;
                    catalogCancelled =
                        operation.CancellationToken.IsCancellationRequested;
                    catalog = AISettingsOperationPolicy.NormalizeCatalog(
                        catalog,
                        catalogTimedOut,
                        catalogCancelled);
                }
            }
            catch (Exception ex)
            {
                if (!_aiOperationLifetime.IsCurrent(operation))
                    return;
                await HandleUnexpectedCatalogErrorAsync(operation, ex)
                    .ConfigureAwait(false);
                catalog = OpenRouterCatalogResult.Unavailable(
                    OpenRouterFailureKind.Network);
            }

            if (!_aiOperationLifetime.IsCurrent(operation))
                return;
            AISettingsModelBinding binding;
            try
            {
                binding = await _infrastructure.PrepareModelBindingAsync(
                        catalog,
                        operation.CancellationToken)
                    .ConfigureAwait(false);
                if (!await _infrastructure.IsKeyGenerationCurrentAsync(
                            keyGeneration,
                            operation.CancellationToken)
                        .ConfigureAwait(false))
                    return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (!_aiOperationLifetime.IsCurrent(operation))
                return;
            bool catalogReplaced;
            if (!_aiOperationLifetime.TryExecuteCurrent(
                    operation,
                    () =>
                    {
                        _infrastructure.ReplaceCatalog(
                            keyGeneration,
                            catalog);
                        return true;
                    },
                    out catalogReplaced) ||
                !catalogReplaced)
                return;
            await ApplyOnUiThreadAsync(operation, () =>
            {
                var bindWatch = Stopwatch.StartNew();
                _isCatalogLoading = false;
                _aiCatalog = catalog;
                BindModelChoices(binding);
                _aiConnectionState =
                    AISettingsOperationPolicy.CatalogCompletionState(
                        catalog,
                        binding.SelectedChoice != null);
                RefreshAiConnectionDisplay();
                RefreshAiModelDisplay();
                _infrastructure.ReportPhase(
                    AISettingsOperationStage.BindModels,
                    OpenRouterFailureKind.None,
                    catalog.HttpStatus,
                    bindWatch.ElapsedMilliseconds,
                    false);
            }).ConfigureAwait(false);
        }

        private async void RefreshModels()
        {
            try
            {
                await AISettingsAsyncBoundary.RunAsync(
                    RefreshModelsSafelyAsync,
                    ex => HandleUnexpectedCatalogErrorAsync(null, ex));
            }
            catch
            {
                // The WPF event boundary must never propagate an exception.
            }
        }

        private async Task RefreshModelsSafelyAsync()
        {
            var operation = _aiOperationLifetime.Begin(-1);
            if (operation == null)
                return;
            OpenRouterKeySnapshot keySnapshot;
            try
            {
                keySnapshot = await _infrastructure.CaptureKeyStateAsync(
                        operation.CancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (!_aiOperationLifetime.IsCurrent(operation))
                return;
            if (string.IsNullOrWhiteSpace(keySnapshot.Key))
            {
                await ApplyOnUiThreadAsync(operation, () =>
                {
                    _isCatalogLoading = false;
                    _hasConnectedKey = false;
                    _aiConnectionState = AiConnectionDisplayState.MissingKey;
                    RefreshAiConnectionDisplay();
                }).ConfigureAwait(false);
                return;
            }

            await ApplyOnUiThreadAsync(operation, () =>
            {
                _hasConnectedKey = true;
                _aiConnectionState = AiConnectionDisplayState.Connected;
                RefreshAiConnectionDisplay();
            }).ConfigureAwait(false);
            try
            {
                await RefreshModelCatalogAsync(
                    keySnapshot.Key,
                    operation,
                    keySnapshot.Generation);
            }
            catch (OperationCanceledException)
            {
                await ApplyOnUiThreadAsync(operation, () =>
                {
                    _aiConnectionState = AiConnectionDisplayState.Connected;
                    RefreshAiConnectionDisplay();
                    RefreshAiModelDisplay();
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (_aiOperationLifetime.IsCurrent(operation))
                {
                    await HandleUnexpectedCatalogErrorAsync(operation, ex)
                        .ConfigureAwait(false);
                }
            }
        }

        private async void DisconnectOpenRouter()
        {
            try
            {
                await AISettingsAsyncBoundary.RunAsync(
                    DisconnectOpenRouterAsync,
                    ex => HandleUnexpectedConnectionErrorAsync(null, ex));
            }
            catch
            {
                // The WPF event boundary must never propagate an exception.
            }
        }

        private async Task DisconnectOpenRouterAsync()
        {
            AIColorOperationCoordinator.Current.CancelCurrent();
            CancelPendingOperations();
            var operation = _aiOperationLifetime.Begin(-1);
            if (operation == null)
                return;
            _hasConnectedKey = false;
            _isCatalogLoading = false;
            _aiKeyInput.Clear();
            _aiCatalog = null;
            ClearModelChoices();
            _aiConnectionState = AiConnectionDisplayState.Disconnected;
            RefreshAiConnectionDisplay();
            RefreshAiModelDisplay();
            var disconnectTask = _infrastructure.DisconnectAsync();
            var invalidateTask = _infrastructure.InvalidateCatalogAsync();
            KeyStoreMutationResult mutation;
            try
            {
                await Task.WhenAll(disconnectTask, invalidateTask)
                    .ConfigureAwait(false);
                mutation = await disconnectTask
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await HandleUnexpectedConnectionErrorAsync(operation, ex)
                    .ConfigureAwait(false);
                return;
            }
            if (!mutation.IsFullyDisconnected)
            {
                await ApplyOnUiThreadAsync(operation, () =>
                {
                    _aiConnectionState = AiConnectionDisplayState.StorageFailed;
                    RefreshAiConnectionDisplay();
                }).ConfigureAwait(false);
            }
        }

        private async void CommitSelectedModel()
        {
            try
            {
                await AISettingsAsyncBoundary.RunAsync(
                    CommitSelectedModelAsync,
                    ex => HandleUnexpectedCatalogErrorAsync(null, ex));
            }
            catch
            {
                // The WPF event boundary must never propagate an exception.
            }
        }

        private async Task CommitSelectedModelAsync()
        {
            if (_updatingAiModelUi)
                return;
            var choice = ModelCombo.SelectedItem as OpenRouterModelChoice;
            var selectedModelId = choice?.Id ?? string.Empty;
            if (choice != null)
                _aiModelPicker.Select(choice.Id);
            _infrastructure.UpdateSelectedModelRuntime(selectedModelId);
            _aiConnectionState = choice == null
                ? AiConnectionDisplayState.Connected
                : AiConnectionDisplayState.Ready;
            RefreshAiConnectionDisplay();
            RefreshAiModelDisplay();
            await _infrastructure.SaveSelectedModelAsync()
                .ConfigureAwait(false);
        }

        private void ClearModelChoices()
        {
            if (ModelCombo == null)
                return;
            _updatingAiModelUi = true;
            try
            {
                ModelCombo.ItemsSource = Array.Empty<OpenRouterModelChoice>();
                ModelCombo.SelectedItem = null;
                _aiModelPicker.Replace(
                    Array.Empty<OpenRouterModelChoice>(),
                    string.Empty);
                UpdateModelCount(0);
            }
            finally
            {
                _updatingAiModelUi = false;
            }
        }

        private void BindModelChoices(AISettingsModelBinding binding)
        {
            if (ModelCombo == null || binding == null)
                return;
            _updatingAiModelUi = true;
            try
            {
                _aiModelPicker.Replace(
                    binding.Choices,
                    binding.SelectedChoice?.Id);
                RelocalizeModelChoices();
                ApplyModelFilterCore();
            }
            finally
            {
                _updatingAiModelUi = false;
            }
        }

        private void RefreshModelChoiceLocalization()
        {
            if (ModelCombo == null)
                return;
            _updatingAiModelUi = true;
            try
            {
                RelocalizeModelChoices();
                ApplyModelFilterCore();
            }
            finally
            {
                _updatingAiModelUi = false;
            }
        }

        private void RelocalizeModelChoices()
        {
            _aiModelPicker.Relocalize(
                key => _localization.GetString(key),
                (key, args) => _localization.Format(key, args));
        }

        private void ApplyModelFilter()
        {
            if (_updatingAiModelUi || ModelCombo == null)
                return;
            _updatingAiModelUi = true;
            try
            {
                ApplyModelFilterCore();
            }
            finally
            {
                _updatingAiModelUi = false;
            }
        }

        private void ApplyModelFilterCore()
        {
            var filtered = _aiModelPicker.Filter(_aiModelSearch?.Text);
            ModelCombo.ItemsSource = filtered;
            ModelCombo.SelectedItem = filtered.FirstOrDefault(choice =>
                string.Equals(
                    choice.Id,
                    _aiModelPicker.SelectedModelId,
                    StringComparison.OrdinalIgnoreCase));
            UpdateModelCount(filtered.Count);
        }

        private void UpdateModelCount(int count)
        {
            if (_aiModelCountText == null)
                return;
            _aiModelCountText.Text = _localization.Format(
                "Settings_Ai_Model_Search_Count_Format",
                count);
        }

        private void RefreshAiConnectionDisplay()
        {
            if (_aiConnectionPanel == null)
                return;

            var presentation = AISettingsConnectionPresentation.Evaluate(
                _aiConnectionState,
                _hasConnectedKey,
                _isCatalogLoading);
            var detail = string.IsNullOrEmpty(presentation.DetailResource)
                ? string.Empty
                : _localization.GetString(presentation.DetailResource);
            _aiConnectionPanel.Apply(
                presentation,
                _localization.GetString(presentation.HeadlineResource),
                detail);
            if (_aiRefreshModelsButton != null)
                _aiRefreshModelsButton.IsEnabled = presentation.RefreshEnabled;
        }

        private static AiConnectionDisplayState MapConnectionFailure(
            OpenRouterFailureKind failure,
            bool userCancelled,
            bool timedOut)
        {
            if (userCancelled)
                return AiConnectionDisplayState.Cancelled;
            if (timedOut)
                return AiConnectionDisplayState.Timeout;
            switch (failure)
            {
                case OpenRouterFailureKind.MissingKey:
                    return AiConnectionDisplayState.MissingKey;
                case OpenRouterFailureKind.Unauthorized:
                    return AiConnectionDisplayState.Unauthorized;
                case OpenRouterFailureKind.RateLimited:
                    return AiConnectionDisplayState.RateLimited;
                case OpenRouterFailureKind.Timeout:
                    return AiConnectionDisplayState.Timeout;
                case OpenRouterFailureKind.Cancelled:
                    return AiConnectionDisplayState.Cancelled;
                case OpenRouterFailureKind.WorkerMissing:
                    return AiConnectionDisplayState.WorkerMissing;
                case OpenRouterFailureKind.WorkerRuntimeMissing:
                    return AiConnectionDisplayState.WorkerRuntimeMissing;
                case OpenRouterFailureKind.WorkerStartupFailed:
                    return AiConnectionDisplayState.WorkerStartupFailed;
                case OpenRouterFailureKind.WorkerFailed:
                    return AiConnectionDisplayState.WorkerFailed;
                case OpenRouterFailureKind.WorkerInternalFailure:
                    return AiConnectionDisplayState.WorkerInternalFailure;
                case OpenRouterFailureKind.ProtocolMismatch:
                    return AiConnectionDisplayState.ProtocolMismatch;
                default:
                    return AiConnectionDisplayState.NetworkUnavailable;
            }
        }

        private void RefreshAiModelDisplay()
        {
            if (_aiModelStatusText == null)
                return;

            var choice = ModelCombo?.SelectedItem as OpenRouterModelChoice;
            var modelId = choice?.Id ?? _aiModelPicker.SelectedModelId;
            var display = AiModelStatusMapper.Evaluate(
                _aiConnectionState == AiConnectionDisplayState.Connected ||
                _aiConnectionState == AiConnectionDisplayState.Ready,
                _aiCatalog,
                modelId);

            _aiModelStatusText.Text =
                _localization.GetString(display.StatusResource);
            _aiModelStatusText.Foreground = display.IsReady
                ? UiTheme.Success
                : UiTheme.TextSecondary;
        }

        private void OpenOpenRouterKeysPage(
            object sender,
            RequestNavigateEventArgs args)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://openrouter.ai/settings/keys",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                global::NavisHelper.Core.Logger.Error(
                    "OpenRouter keys page exception: " +
                    ex.GetType().Name,
                    "NavisHelperSettingsTabBuilder.OpenRouterKeys");
                _setStatusResource(
                    "Settings_Ai_OpenKeysFailed",
                    UiTheme.Warning,
                    new object[0]);
            }
        }

        private Task HandleUnexpectedConnectionErrorAsync(
            AISettingsOperationLease operation,
            Exception ex)
        {
            return ApplyOnUiThreadAsync(operation, () =>
            {
                _aiConnectionState =
                    AiConnectionDisplayState.NetworkUnavailable;
                RefreshAiConnectionDisplay();
            });
        }

        private Task HandleUnexpectedCatalogErrorAsync(
            AISettingsOperationLease operation,
            Exception ex)
        {
            return ApplyOnUiThreadAsync(operation, () =>
            {
                _isCatalogLoading = false;
                _aiCatalog = OpenRouterCatalogResult.Unavailable(
                    OpenRouterFailureKind.Network);
                _aiConnectionState = AiConnectionDisplayState.Connected;
                RefreshAiConnectionDisplay();
                RefreshAiModelDisplay();
            });
        }

        private Task ApplyOnUiThreadAsync(
            AISettingsOperationLease operation,
            Action action)
        {
            return _uiMutationGate.RunAsync(
                () => !_isDisposed &&
                      (operation == null ||
                       _aiOperationLifetime.IsCurrent(operation)),
                action);
        }

        private UIElement BuildServiceSection()
        {
            var stack = new StackPanel();
            var actions = new WrapPanel();
            actions.Children.Add(LocalizedToolButton(
                "Panel_OpenLog",
                "Settings_Ai_OpenLog_ToolTip",
                () => _openFile(_getLogPath())));
            stack.Children.Add(actions);

            var developerActions = new WrapPanel
            {
                Margin = new Thickness(0, 4, 0, 0)
            };
            developerActions.Children.Add(LocalizedToolButton(
                "Panel_DevScripts_Load_Action",
                "Panel_DevScripts_Load_ToolTip",
                _openDevScripts));
            var developers = new Expander
            {
                IsExpanded = false,
                Content = developerActions,
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            _bindings.BindHeader(
                developers,
                "Settings_Service_Developers_Expander");
            stack.Children.Add(developers);
            return stack;
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
            var moreButton = LocalizedToolButton(
                "Panel_More",
                "Settings_About_Open_ToolTip",
                () => _executePlugin("AboutNavisHelper.CBC"));
            moreButton.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(moreButton);
            return stack;
        }

    }
}
