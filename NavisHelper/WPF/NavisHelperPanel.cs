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
using NavisHelper.AI;
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
    public partial class NavisHelperPanel : UserControl, IDisposable
    {
        private TextBox _folderPathBox;

        private CheckBox _overwriteCheck;

        private ListBox _schemeListBox;

        private WrapPanel _previewPanel;

        private ComboBox _modelCombo;

        private NavisHelperSettingsTabBuilder _settingsTabBuilder;


        private TextBox _aiResponseLog;

        private Button _aiApplyButton;

        private Button _localPaletteButton;

        private AiPanelOutcome _aiPanelOutcome = AiPanelOutcome.None;

        private ListBox _historyListBox;

        private Border _matchColorSwatch;

        private TextBlock _matchColorText;

        private TextBox _manualColorBox;

        private string _matchColorRgb;

        private CheckBox _matchTransCheck;

        private TextBlock _matchTransText;

        private double _matchTransparency = -1;

        private Border _globalStatusBorder;

        private TextBlock _globalStatusBar;

        private string _globalStatusResourceKey;

        private object[] _globalStatusArguments;

        private ProgressBar _globalBusyBar;

        private TabControl _mainTabControl;

        private TabItem _modelTab;
        private TabItem _colorsTab;
        private TabItem _viewsTab;
        private TabItem _settingsTab;
        private readonly SelectionGatingController _selectionGating;
        private readonly PanelLocalizationBindings _panelLocalizationBindings;
        private bool _isDisposed;

        private Action<int> _selectColorsSegment;
        private Action<int> _selectViewsSegment;

        private PanelUiSettings _panelUiSettings;

        private readonly List<QuickPaletteCommand> _commandPalette = new List<QuickPaletteCommand>();

        private bool _isPaletteHookPaused;

        private Window _commandPaletteWindow;

        private TextBox _commandPaletteQuery;

        // Маркер поля для маркера коллизий.

        private TextBox _clashMarkerSizeText;

        // История покрасок: до 10 последних

        private readonly List<ColorHistoryEntry> _colorHistory = new List<ColorHistoryEntry>();

        private class ColorHistoryEntry
        {
            public int ObjectCount { get; set; }
            public int ColorGroupCount { get; set; }
            public List<string> ObjectNames { get; set; }
            public Dictionary<string, string> Colors { get; set; }
            public DateTime Time { get; set; }
            /// <summary>
            /// Сохранённые ModelItem для быстрого выделения (без поиска по модели)
            /// </summary>
            public Autodesk.Navisworks.Api.ModelItemCollection SavedSelection { get; set; }
        }

        private class QuickPaletteCommand
        {
            public string ResourceId { get; set; }
            public Action Execute { get; set; }
            public DateTime? LastUsed { get; set; }
        }

        private readonly Autodesk.Navisworks.Api.ModelItemCollection[] _selectionSlots = new Autodesk.Navisworks.Api.ModelItemCollection[5];

        private TextBlock _selectionMemoryText;

        // Section Box по выделению

        private SelectionPreviewManager _selMgr = new SelectionPreviewManager();

        private Slider _selOffsetAllSlider;

        private Slider _selOffsetXSlider;

        private Slider _selOffsetYSlider;

        private Slider _selOffsetZSlider;

        private Slider _selShiftXSlider;

        private Slider _selShiftYSlider;

        private Slider _selShiftZSlider;

        private Slider _selTransSlider;

        private CheckBox _selUseSectionBox;

        private CheckBox _selContextTrans;

        private DispatcherTimer _selectionSectionDebounceTimer;

        private bool _suppressSelectionSectionControlRefresh;

        private DataGrid _selectionSectionHistoryGrid;

        private bool _suppressSelectionSectionHistorySync;

        private readonly List<SectionBoxHistoryRow> _selectionSectionHistory = new List<SectionBoxHistoryRow>();

        private Document _selectionSectionHistoryDocument;

        private sealed class SectionBoxHistoryRow
        {
            public string ObjectName { get; set; }
            public string AppliedAt { get; set; }
            public ModelItem Item { get; set; }
        }

        // Коллизии

        private ClashPreviewManager _clashMgr = new ClashPreviewManager();

        private DataGrid _testGrid;

        private DataGrid _clashGrid;

        private RowDefinition _clashTestGridRow;

        private RowDefinition _clashListRow;

        private Slider _clashOffsetSlider;

        private Slider _clashTransSlider;

        private ComboBox _clashColorA;

        private ComboBox _clashColorB;

        private RadioButton _clashBoxModePointRadio;

        private RadioButton _clashBoxModeItemsRadio;

        private CheckBox _clashContextTrans;

        private CheckBox _clashGroupMarkersForViewpoints;

        private CheckBox _clashDualViewpoints;

        private CheckBox _clashUseSectionBox;

        private DispatcherTimer _clashPreviewDebounceTimer;

        private string _clashCameraDiagDir;

        private int _clashCameraDiagIndex;

        private bool _clashViewpointBatchBusy;

        private object _clashContextMenuItem;

        private TreeView _clashTreeA;

        private TreeView _clashTreeB;

        private DataGrid _clashGroupContentsGrid;

        private TextBlock _clashGroupContentsStatus;

        private TextBlock _clashGroupingStatus;

        private string _clashGroupingPath;

        private string _clashGroupingLabel;

        private ClashTreeNodeTag _pendingClashGroupingTag;

        private Button _applyClashGroupingButton;

        private bool _suppressClashTreeSelectionChanged;

        private readonly List<Button> _clashInteractiveButtons = new List<Button>();

        private readonly ClashVirtualGroupStateStore _clashVirtualGroupState = new ClashVirtualGroupStateStore();

        private IReadOnlyList<VirtualClashGroup> _virtualClashGroups => _clashVirtualGroupState.ActiveGroups;

        private ClashTest _activeClashTest;

        private Document _clashEventDocument;

        private DocumentClashTests _clashEventTestsData;

        private bool _clashApplicationEventsAttached;

        private int _clashDocumentGeneration;

        private int _clashDataRefreshGeneration;

        private int _clashPreviewRefreshGeneration;

        private DispatcherTimer _clashDataChangedDebounceTimer;

        private string _pendingClashDataRefreshReason;

        private static readonly (string Category, string Name)[] PropertyAliases =
        {
            ("Item", "Система"),
            ("Item", "System"),
            ("Элемент", "Система"),
            ("Элемент", "System"),
            ("", "Система"),
            ("", "System"),
            ("Item", "Спец"),
            ("Item", "Специализация"),
            ("Item", "Special"),
            ("Элемент", "Спец"),
            ("Элемент", "Специализация"),
            ("Элемент", "Special"),
            ("", "Спец"),
            ("", "Специализация"),
            ("", "Special"),
            ("Item", "Отметка"),
            ("Item", "Mark"),
            ("Item", "Marker"),
            ("Элемент", "Отметка"),
            ("Элемент", "Mark"),
            ("Элемент", "Marker"),
            ("", "Отметка"),
            ("", "Mark"),
            ("", "Marker")
        };

        private const string SearchSetItemInternalCategory = "LcOaNode";

        private const string SearchSetItemNameInternalProperty = "LcOaSceneBaseUserName";

        // Null RGB means no highlight.

        private static readonly (string Name, byte? R, byte? G, byte? B)[] ClashColors = new[]
        {
            ("None",     (byte?)null, (byte?)null, (byte?)null),
            ("Red",      (byte?)255, (byte?)50,   (byte?)50),
            ("Blue",     (byte?)50,  (byte?)100,  (byte?)255),
            ("Green",    (byte?)50,  (byte?)200,  (byte?)50),
            ("Orange",   (byte?)255, (byte?)165,  (byte?)0),
            ("Yellow",   (byte?)255, (byte?)255,  (byte?)50),
            ("Purple",   (byte?)180, (byte?)50,   (byte?)255),
            ("Cyan",     (byte?)50,  (byte?)200,  (byte?)255),
            ("Pink",     (byte?)255, (byte?)100,  (byte?)180),
            ("White",    (byte?)255, (byte?)255,  (byte?)255),
            ("DarkRed",  (byte?)180, (byte?)0,    (byte?)0),
            ("DarkBlue", (byte?)0,   (byte?)50,   (byte?)180),
            ("Teal",     (byte?)0,   (byte?)200,  (byte?)200),
            ("Lime",     (byte?)150, (byte?)255,  (byte?)0),
            ("Brown",    (byte?)160, (byte?)100,  (byte?)50),
        };

        private static readonly object GlobalHotkeySync = new object();

        private static KeyboardHook _hook;

        private static NavisHelperPanel _hookOwner;

        /// <summary>
        /// Последний активный экземпляр панели для обратной связи из плагинов
        /// </summary>

        public static NavisHelperPanel Current { get; private set; }

        private static ScrollViewer WrapInScroll(UIElement content)
        {
            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content
            };
        }

        private TabItem CreateModelTab()
        {
            var stack = new StackPanel();
            stack.Children.Add(CreateNavigationContent());
            stack.Children.Add(CreateSeparator());
            stack.Children.Add(CreateImportExportContent());

            _modelTab = new TabItem { Content = WrapInScroll(stack) };
            _panelLocalizationBindings.BindHeader(_modelTab, "Panel_Tab_Model");
            return _modelTab;
        }

        private TabItem CreateColorsTab()
        {
            var manual = WrapInScroll(CreateToolsContent());
            var ai = CreateAIColorsContent();

            RadioButton[] segmentButtons;
            var content = UiTheme.Segmented(
                new[] { PanelUi("Panel_Colors_Segment_Manual"), "AI" },
                new UIElement[] { manual, ai },
                _panelUiSettings.ColorsSegment,
                idx => { _panelUiSettings.ColorsSegment = idx; _panelUiSettings.Save(); },
                out _selectColorsSegment,
                out segmentButtons);
            _panelLocalizationBindings.BindContent(
                segmentButtons[0],
                "Panel_Colors_Segment_Manual");

            _colorsTab = new TabItem
            {
                Content = WithTabPadding(content)
            };
            _panelLocalizationBindings.BindHeader(_colorsTab, "Panel_Tab_Colors");
            return _colorsTab;
        }

        private TabItem CreateViewsTab()
        {
            var markup = CreateViewpointsContent();
            var hmTab = CreateHeightMarksTab();
            var heightMarks = (UIElement)hmTab.Content;
            hmTab.Content = null;

            RadioButton[] segmentButtons;
            var content = UiTheme.Segmented(
                new[]
                {
                    PanelUi("Panel_Views_Segment_Markup"),
                    PanelUi("Panel_Views_Segment_Elevations")
                },
                new UIElement[] { markup, heightMarks },
                _panelUiSettings.ViewsSegment,
                idx => { _panelUiSettings.ViewsSegment = idx; _panelUiSettings.Save(); },
                out _selectViewsSegment,
                out segmentButtons);
            _panelLocalizationBindings.BindContent(
                segmentButtons[0],
                "Panel_Views_Segment_Markup");
            _panelLocalizationBindings.BindContent(
                segmentButtons[1],
                "Panel_Views_Segment_Elevations");

            _viewsTab = new TabItem
            {
                Content = WithTabPadding(content)
            };
            _panelLocalizationBindings.BindHeader(_viewsTab, "Panel_Tab_Views");
            return _viewsTab;
        }

        private TabItem CreateSettingsTab()
        {
            _settingsTabBuilder = new NavisHelperSettingsTabBuilder(
                (resourceKey, color, arguments) =>
                    SetGlobalStatusResource(resourceKey, color, arguments),
                OpenFileInShell,
                GetModelLogPath,
                OpenDevScriptsMenu,
                ExecutePlugin,
                Dispatcher,
                _panelLocalizationBindings);
            _settingsTab = _settingsTabBuilder.Build();
            _modelCombo = _settingsTabBuilder.ModelCombo;
            return _settingsTab;
        }

        private static Border WithTabPadding(UIElement content)
        {
            return new Border
            {
                Padding = new Thickness(4, 4, 4, 0),
                Child = content
            };
        }

        public NavisHelperPanel()
        {
            _selectionGating = new SelectionGatingController(Dispatcher);
            _panelLocalizationBindings = new PanelLocalizationBindings(
                UiLocalizationService.Current,
                Dispatcher);
            Background = Brushes.Transparent;
            UiTheme.InstallImplicitControlStyles(this);
            Loaded += OnPanelLoaded;
            Unloaded += OnPanelUnloaded;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _globalStatusBar = new TextBlock
            {
                FontSize = 10,
                Foreground = Brushes.Gray,
                Padding = new Thickness(6, 0, 6, 0),
                Height = 22,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _panelLocalizationBindings.BindAction(
                _globalStatusBar,
                "Panel.GlobalStatus",
                ApplyGlobalStatusResource);
            _globalBusyBar = new ProgressBar
            {
                Width = 84,
                Height = 10,
                IsIndeterminate = true,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var statusContent = new DockPanel();
            DockPanel.SetDock(_globalBusyBar, Dock.Left);
            statusContent.Children.Add(_globalBusyBar);
            statusContent.Children.Add(_globalStatusBar);

            _globalStatusBorder = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromRgb(0xF0, 0xF0, 0xF0)),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xCC, 0xCC, 0xCC)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Height = 24,
                Child = statusContent
            };
            root.Children.Add(_globalStatusBorder);
            Grid.SetRow(_globalStatusBorder, 1);

            _panelUiSettings = Core.PanelUiSettings.Load();

            _mainTabControl = new TabControl { Margin = new Thickness(2), FontSize = 11.5 };
            _mainTabControl.Items.Add(CreateModelTab());
            _mainTabControl.Items.Add(CreateColorsTab());
            _mainTabControl.Items.Add(CreateViewsTab());
            _mainTabControl.Items.Add(CreateClashTab());
            _mainTabControl.Items.Add(CreateSettingsTab());
            if (_panelUiSettings.MainTabIndex >= 0 && _panelUiSettings.MainTabIndex < _mainTabControl.Items.Count)
                _mainTabControl.SelectedIndex = _panelUiSettings.MainTabIndex;
            _mainTabControl.SelectionChanged += (s, e) =>
            {
                if (!ReferenceEquals(e.OriginalSource, _mainTabControl)) return;
                _panelUiSettings.MainTabIndex = _mainTabControl.SelectedIndex;
                _panelUiSettings.Save();
            };
            root.Children.Add(_mainTabControl);
            Grid.SetRow(_mainTabControl, 0);

            _panelLocalizationBindings.BindAction(
                this,
                "Panel.ColorSchemes",
                RefreshSchemeListItemTexts);
            Content = root;

            RegisterPaletteCommands();
        }

        private void OnPanelLoaded(object sender, RoutedEventArgs e)
        {
            Current = this;
            _panelLocalizationBindings.Attach();
            _settingsTabBuilder?.ResumeAfterLoad();
            try
            {
                Logger.Info("NavisHelperPanel loaded.", "AgentHost");
                NavisHelper.Agent.AgentRuntime.Initialize(new DispatcherSynchronizationContext(Dispatcher));
                AttachClashUiDocumentEvents();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to initialize AgentRuntime from NavisHelperPanel: " + ex, "AgentHost");
            }

            try
            {
                _selectionGating.Attach();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to attach selection gating: " + ex, "SelectionGating");
            }

            try
            {
                InstallGlobalHotkeys();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to install NavisHelper global hotkeys: " + ex, "KeyboardHook");
            }
        }

        private void OnPanelUnloaded(object sender, RoutedEventArgs e)
        {
            AIColorOperationCoordinator.Current.CancelCurrent();
            _settingsTabBuilder?.CancelPendingOperations();
            _panelLocalizationBindings.Detach();
            try
            {
                _selectionGating.Detach();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to detach selection gating: " + ex, "SelectionGating");
            }

            try
            {
                DetachClashUiDocumentEvents();
                ClearClashPairIsolationForLifecycle();
            }
            finally
            {
                try
                {
                    StopPanelDebounceTimers();
                    CloseCommandPaletteForUnload();
                }
                finally
                {
                    try
                    {
                        UninstallGlobalHotkeys();
                    }
                    finally
                    {
                        if (ReferenceEquals(Current, this))
                            Current = null;
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            AIColorOperationCoordinator.Current.CancelCurrent();
            _settingsTabBuilder?.Dispose();
            _settingsTabBuilder = null;
            Loaded -= OnPanelLoaded;
            Unloaded -= OnPanelUnloaded;
            _panelLocalizationBindings.Dispose();
            if (ReferenceEquals(Current, this))
                Current = null;
            _isDisposed = true;
        }

        private void StopPanelDebounceTimers()
        {
            _selectionSectionDebounceTimer?.Stop();
            _clashPreviewDebounceTimer?.Stop();
            _clashDataChangedDebounceTimer?.Stop();
        }

        private void CloseCommandPaletteForUnload()
        {
            var window = _commandPaletteWindow;
            if (window == null)
                return;

            window.Close();
            if (ReferenceEquals(_commandPaletteWindow, window))
            {
                _commandPaletteWindow = null;
                _commandPaletteQuery = null;
                ResumeGlobalHotkeysForPalette();
            }
        }

        private void InstallGlobalHotkeys()
        {
            lock (GlobalHotkeySync)
            {
                if (ReferenceEquals(_hookOwner, this) && _hook != null)
                    return;

                if (_hook != null)
                {
                    var previousHook = _hook;
                    var previousOwner = _hookOwner;
                    _hook = null;
                    _hookOwner = null;
                    if (previousOwner != null)
                        previousHook.KeyPressed -= previousOwner.OnGlobalKeyPressed;
                    previousHook.Dispose();
                }

                var hook = new KeyboardHook();
                hook.KeyPressed += OnGlobalKeyPressed;
                try
                {
                    hook.Install();
                }
                catch
                {
                    hook.KeyPressed -= OnGlobalKeyPressed;
                    hook.Dispose();
                    throw;
                }

                _hook = hook;
                _hookOwner = this;
            }
        }

        private void UninstallGlobalHotkeys()
        {
            lock (GlobalHotkeySync)
            {
                if (!ReferenceEquals(_hookOwner, this))
                    return;

                var hook = _hook;
                _hook = null;
                _hookOwner = null;
                if (hook == null)
                    return;

                hook.KeyPressed -= OnGlobalKeyPressed;
                hook.Dispose();
            }
        }

        private void OnGlobalKeyPressed(Key key, ModifierKeys modifiers)
        {
            if (_isPaletteHookPaused || (_commandPaletteWindow != null && _commandPaletteWindow.IsVisible))
                return;

            if ((modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
            bool hasShift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            bool hasAlt = (modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;
            if (hasAlt) return;

            var focused = Keyboard.FocusedElement as FrameworkElement;
            if (focused is TextBox || focused is ComboBox || focused is PasswordBox)
                return;

            switch (key)
            {
                case Key.Q:
                    Dispatcher.BeginInvoke(new Action(() =>
                        TreeNavigation.SafeExecute(TreeNavigation.SelectParents)));
                    break;
                case Key.W:
                    Dispatcher.BeginInvoke(new Action(() =>
                        TreeNavigation.SafeExecute(TreeNavigation.SelectChildren)));
                    break;
                case Key.E:
                    Dispatcher.BeginInvoke(new Action(() =>
                        TreeNavigation.SafeExecute(TreeNavigation.SelectSiblings)));
                    break;
                case Key.P:
                    if (hasShift)
                    {
                        Dispatcher.BeginInvoke(new Action(OpenCommandPalette));
                    }
                    break;
                case Key.H:
                    if (hasShift)
                    {
                        Dispatcher.BeginInvoke(new Action(SaveHeightScreenshot));
                    }
                    break;
                case Key.M:
                    if (!hasShift)
                    {
                        Dispatcher.BeginInvoke(new Action(() => ExecutePlugin("CopySelectedNames.CBC")));
                    }
                    break;
                case Key.D1:
                case Key.NumPad1:
                    if (hasShift)
                        Dispatcher.BeginInvoke(new Action(() => SaveSelectionSetSlot(0)));
                    else
                        Dispatcher.BeginInvoke(new Action(() => RecallSelectionSetSlot(0)));
                    break;
            }
        }

        private void PauseGlobalHotkeysForPalette()
        {
            _isPaletteHookPaused = true;
        }

        private void ResumeGlobalHotkeysForPalette()
        {
            _isPaletteHookPaused = false;
        }

        private void RegisterPaletteCommand(string resourceId, Action action)
        {
            if (string.IsNullOrWhiteSpace(resourceId) || action == null) return;
            _commandPalette.RemoveAll(c =>
                string.Equals(c.ResourceId, resourceId, StringComparison.Ordinal));
            _commandPalette.Add(new QuickPaletteCommand
            {
                ResourceId = resourceId,
                Execute = action
            });
        }

        private void RegisterPaletteCommands()
        {
            _commandPalette.Clear();

            RegisterPaletteCommand("ColorsByName", () => ExecutePlugin("ColorsByName.CBC"));
            RegisterPaletteCommand("OverridePdms", () => ExecutePlugin("HideItems.CBC"));
            RegisterPaletteCommand("MatchColor", OnPickAndApplyColor);
            RegisterPaletteCommand("MatchColorRead", () => OnPickColor(null, null));
            RegisterPaletteCommand("MatchColorApply", () => OnPasteColor(null, null));
            RegisterPaletteCommand("ColorByProperty", OnColorByProperty);
            RegisterPaletteCommand("ResetOverrides", ResetAllOverrides);
            RegisterPaletteCommand("ExportColors", () => ExecutePlugin("ExportColors.CBC"));
            RegisterPaletteCommand("ImportColors", () => ExecutePlugin("ImportColors.CBC"));
            RegisterPaletteCommand("AiColoring", () => OnApplyColorScheme(null, null));
            RegisterPaletteCommand("CopyNames", () => ExecutePlugin("CopySelectedNames.CBC"));
            RegisterPaletteCommand("FilterByList", () => ExecutePlugin("FilterModels.COMPANY"));
            RegisterPaletteCommand("SelectByProperty", OnSelectByPropertyValue);
            RegisterPaletteCommand("SaveSearchSet", OnCreateSearchSelectionSet);

            RegisterPaletteCommand("Parent", () => TreeNavigation.SafeExecute(TreeNavigation.SelectParents));
            RegisterPaletteCommand("Child", () => TreeNavigation.SafeExecute(TreeNavigation.SelectChildren));
            RegisterPaletteCommand("Sibling", () => TreeNavigation.SafeExecute(TreeNavigation.SelectSiblings));
            RegisterPaletteCommand("Leaf", () => TreeNavigation.SafeExecute(TreeNavigation.SelectLeafNodes));
            RegisterPaletteCommand("AllUnder", () => TreeNavigation.SafeExecute(TreeNavigation.SelectAllUnder));
            RegisterPaletteCommand("InvertSelection", InvertSelection);
            RegisterPaletteCommand("Isolate", IsolateSelection);
            RegisterPaletteCommand("UnhideAll", UnhideAll);
            RegisterPaletteCommand("RememberSelection", () => SaveSelectionSetSlot(0));
            RegisterPaletteCommand("RestoreSelection", () => RecallSelectionSetSlot(0));

            RegisterPaletteCommand("MarkupViewpoint", () => ExecutePlugin("MarkupViewpoint.CBC"));
            RegisterPaletteCommand("HeightMarks", () => ShowHeightMarksTab());
            RegisterPaletteCommand("HeightGraphics", ShowHeightGraphicsMarkers);
            RegisterPaletteCommand("HeightScreenshot", SaveHeightScreenshot);
            RegisterPaletteCommand("TopViewSection", () => ExecutePlugin("TopViewSection.CBC"));
            RegisterPaletteCommand("TopViewBounds", () => ExecutePlugin("TopViewBoundingRect.CBC"));
            RegisterPaletteCommand("TopViewHatch", () => ExecutePlugin("TopViewBoundingHatch.CBC"));
            RegisterPaletteCommand("SelectionHatch", () => ExecutePlugin("SelectionHatchMarker.CBC"));
            RegisterPaletteCommand("SelectionBoundsHatch", () => ExecutePlugin("SelectionHatchBoundsMarker.CBC"));
            RegisterPaletteCommand("SortViewpoints", () => ExecutePlugin("SortViewpoints.COMPANY"));
            RegisterPaletteCommand("SaveViewpoints", () => ExecutePlugin("SaveViewpiontList.COMPANY"));
            RegisterPaletteCommand("SelectionSectionBox", () => ShowSelectionSectionBox());
            RegisterPaletteCommand("ResetSectionBox", ResetSelectionSectionBox);
            RegisterPaletteCommand("SelectionBounds", ShowAndCopySelectionBounds);

            RegisterPaletteCommand("CsvAttributes", () => ExecutePlugin("CsvAttributeLoader.CSVL"));
            RegisterPaletteCommand("ImportPsLists", () => ExecutePlugin("ImportPslists.CBC"));
            RegisterPaletteCommand("SaveHierarchy", () => ExecutePlugin("SaveHierarhy.COMPANY"));
            RegisterPaletteCommand("SaveNwd2018", () => ExecutePlugin("SaveAsNavis2018.MS"));
            RegisterPaletteCommand("ExportProperties", ExportSelectedPropertiesToExcelLikeFile);

            RegisterPaletteCommand("ClashLoadTests", LoadClashTests);
            RegisterPaletteCommand("ClashRunAll", RunAllClashTests);
            RegisterPaletteCommand("ClashPreview", PreviewSelectedClash);
            RegisterPaletteCommand("ClashViewpoint", SaveClashViewpoint);
            RegisterPaletteCommand("ClashAssignTo", () => SetClashAssignedToPrompt());
            RegisterPaletteCommand("ClashSectionBox", SectionBoxHelper.Toggle);
            RegisterPaletteCommand("ClashMarker", ToggleClashMarker);
            RegisterPaletteCommand("ClashPlane", ToggleSelectionPlane);
            RegisterPaletteCommand("ClashExportBcf", ExportSelectedClashesToBcf);

            RegisterPaletteCommand("Settings", () =>
            {
                if (_mainTabControl != null && _settingsTab != null)
                    _mainTabControl.SelectedItem = _settingsTab;
            });
            RegisterPaletteCommand("OpenLog", () => OpenFileInShell(GetModelLogPath()));
            RegisterPaletteCommand("DevScripts", OpenDevScriptsMenu);
            RegisterPaletteCommand("About", () => ExecutePlugin("AboutNavisHelper.CBC"));
            RegisterPaletteCommand("CommandPalette", OpenCommandPalette);
        }

        private string PaletteCommandTitle(QuickPaletteCommand command)
        {
            return command == null
                ? string.Empty
                : PanelUi("Panel_CommandPalette_" + command.ResourceId + "_Title");
        }

        private string PaletteCommandDescription(QuickPaletteCommand command)
        {
            return command == null
                ? string.Empty
                : PanelUi("Panel_CommandPalette_" + command.ResourceId + "_Description");
        }

        private static UiLocalizedArgument PaletteCommandTitleStatusArgument(
            QuickPaletteCommand command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            return UiLocalizedArgument.FromResource(
                "Panel_CommandPalette_" + command.ResourceId + "_Title");
        }

        private void SetGlobalStatusResource(
            string resourceKey,
            Brush color = null,
            params object[] arguments)
        {
            _globalStatusResourceKey = resourceKey;
            _globalStatusArguments = arguments ?? new object[0];
            ApplyGlobalStatusResource();
            if (_globalStatusBar != null)
                _globalStatusBar.Foreground = color ?? Brushes.Gray;
        }

        private void SetGlobalStatusResource(
            UiStatusResourceDescriptor descriptor,
            Brush color = null)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));

            SetGlobalStatusResource(
                descriptor.ResourceKey,
                color,
                descriptor.Arguments);
        }

        private static string FormatStatusForLog(
            UiStatusResourceDescriptor descriptor)
        {
            if (descriptor == null)
                return string.Empty;

            object[] resolvedArguments = UiLocalizedArgument.Resolve(
                descriptor.Arguments,
                (resourceKey, arguments) =>
                    UiLocalizationService.Current.Format(resourceKey, arguments));
            return UiLocalizationService.Current.Format(
                descriptor.ResourceKey,
                resolvedArguments);
        }

        private void ApplyGlobalStatusResource()
        {
            if (_globalStatusBar == null)
                return;

            object[] resolvedArguments = UiLocalizedArgument.Resolve(
                _globalStatusArguments,
                (resourceKey, arguments) =>
                    UiLocalizationService.Current.Format(resourceKey, arguments));
            string text = string.IsNullOrWhiteSpace(_globalStatusResourceKey)
                ? PanelUi("Panel_Status_Ready")
                : UiLocalizationService.Current.Format(
                    _globalStatusResourceKey,
                    resolvedArguments);
            _globalStatusBar.Text =
                (text ?? string.Empty).Replace("\r\n", " | ").Replace("\n", " | ");
        }

        private static object LocalizedStatusArgument(string resourceKey)
        {
            return UiLocalizedArgument.FromResource(resourceKey);
        }

        private void SetGlobalBusy(bool isBusy)
        {
            if (_globalBusyBar != null)
                _globalBusyBar.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;

            if (_globalStatusBorder != null)
            {
                _globalStatusBorder.Background = isBusy
                    ? new SolidColorBrush(WpfColor.FromRgb(0xFF, 0xF4, 0xD6))
                    : new SolidColorBrush(WpfColor.FromRgb(0xF0, 0xF0, 0xF0));
            }
        }

        private static string GetModelLogPath()
        {
            try
            {
                var doc = NwApplication.ActiveDocument;
                var modelPath = doc?.FileName;
                return Logger.GetLogFilePath(modelPath);
            }
            catch
            {
                return Logger.GetLogFilePath();
            }
        }

        private void OpenFileInShell(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    MessageBox.Show(
                        PanelUi("Panel_Common_FileNotFound"),
                        "NavisHelper",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    UiLocalizationService.Current.Format(
                        "Panel_Common_OpenFileFailed_Format",
                        ex.Message),
                    "NavisHelper",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void OpenDevScriptsMenu()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = PanelUi("Panel_DevScripts_DllFilter"),
                    Title = PanelUi("Panel_DevScripts_SelectDll_Title")
                };
                if (dlg.ShowDialog() != true) return;

                var scripts = DevScriptLoader.LoadScripts(dlg.FileName);
                if (scripts == null || scripts.Count == 0)
                {
                    MessageBox.Show(
                        PanelUi("Panel_DevScripts_NoneFound"),
                        "NavisHelper",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    SetGlobalStatusResource(
                        "Panel_DevScripts_NoneFound_Format",
                        Brushes.Orange,
                        Path.GetFileName(dlg.FileName));
                    return;
                }

                var owner = Window.GetWindow(this);
                var wnd = new Window
                {
                    Title = PanelUi("Panel_DevScripts_WindowTitle"),
                    Width = 360,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    WindowStyle = WindowStyle.ToolWindow,
                    Owner = owner
                };

                var panel = new StackPanel { Margin = new Thickness(8) };
                panel.Children.Add(new TextBlock
                {
                    Text = UiLocalizationService.Current.Format(
                        "Panel_DevScripts_DllLabel_Format",
                        Path.GetFileName(dlg.FileName)),
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                });

                foreach (var script in scripts)
                {
                    var scriptRef = script;
                    var scriptButton = new Button
                    {
                        Content = script.Name,
                        ToolTip = UiLocalizationService.Current.Format(
                            "Panel_DevScripts_Run_ToolTip_Format",
                            script.Name),
                        Margin = new Thickness(0, 2, 4, 2),
                        Padding = new Thickness(8, 4, 8, 4),
                        Cursor = Cursors.Hand
                    };
                    scriptButton.Click += (sender, args) =>
                    {
                        scriptRef.Run();
                        SetGlobalStatusResource(
                            "Panel_DevScripts_Started_Format",
                            Brushes.DarkGreen,
                            scriptRef.Name);
                    };
                    panel.Children.Add(scriptButton);
                }

                wnd.Content = new ScrollViewer { Content = panel };
                wnd.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    UiLocalizationService.Current.Format(
                        "Panel_DevScripts_LoadFailed_Format",
                        ex.Message),
                    "NavisHelper",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                SetGlobalStatusResource(
                    "Panel_DevScripts_LoadFailed",
                    Brushes.Red);
            }
        }

        private void OpenCommandPalette()
        {
            if (_commandPalette.Count == 0)
            {
                MessageBox.Show(
                    PanelUi("Panel_CommandPalette_NoCommands"),
                    PanelUi("Panel_CommandPalette_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var owner = Window.GetWindow(this);
            if (_commandPaletteWindow != null && _commandPaletteWindow.IsVisible)
            {
                _commandPaletteWindow.Activate();
                if (_commandPaletteQuery != null)
                {
                    _commandPaletteQuery.Focus();
                    Keyboard.Focus(_commandPaletteQuery);
                }
                return;
            }

            var window = new Window
            {
                Title = PanelUi("Panel_CommandPalette_Title"),
                Width = 520,
                Height = 360,
                WindowStartupLocation = WindowStartupLocation.Manual,
                WindowStyle = WindowStyle.ToolWindow,
                Opacity = 0.86,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Topmost = true,
                ShowInTaskbar = false,
                ShowActivated = true,
                Owner = owner
            };
            if (owner == null)
            {
                var ownerHandle = Process.GetCurrentProcess().MainWindowHandle;
                if (ownerHandle != IntPtr.Zero)
                {
                    new System.Windows.Interop.WindowInteropHelper(window).Owner = ownerHandle;
                }
            }
            _commandPaletteWindow = window;
            window.Closed += (s, e) =>
            {
                if (ReferenceEquals(_commandPaletteWindow, window))
                {
                    _panelLocalizationBindings.UnbindAction(
                        window,
                        "CommandPalette.Localization");
                    _commandPaletteWindow = null;
                    _commandPaletteQuery = null;
                    ResumeGlobalHotkeysForPalette();
                }
            };

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var query = new TextBox
            {
                Margin = new Thickness(8),
                FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Center,
                Height = 28,
                Focusable = true,
                IsEnabled = true,
                IsReadOnly = false,
                IsTabStop = true,
                Background = Brushes.White,
                Foreground = Brushes.Black,
                CaretBrush = Brushes.Black,
                SelectionBrush = new SolidColorBrush(WpfColor.FromArgb(60, 0, 120, 215)),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(0xB0, 0xB0, 0xB0))
            };
            _commandPaletteQuery = query;

            var results = new ListBox { Margin = new Thickness(8, 0, 8, 8) };
            bool IsCommandItem(ListBoxItem item) => item != null && item.Tag is QuickPaletteCommand;
            int FindNextCommandIndex(int start, int step)
            {
                if (results.Items.Count == 0) return -1;
                int index = start;
                while (index >= 0 && index < results.Items.Count)
                {
                    var item = results.Items[index] as ListBoxItem;
                    if (IsCommandItem(item)) return index;
                    index += step;
                }
                return -1;
            }

            void UpdateList(string filter)
            {
                results.Items.Clear();
                var raw = (filter ?? string.Empty).Trim();
                if (raw.StartsWith(">", StringComparison.Ordinal))
                {
                    raw = raw.Substring(1).TrimStart();
                }
                var q = raw.ToLowerInvariant();

                void AddCommandItem(QuickPaletteCommand command)
                {
                    if (command == null) return;
                    results.Items.Add(new ListBoxItem
                    {
                        Tag = command,
                        Content = PaletteCommandTitle(command) + " — " +
                                  PaletteCommandDescription(command),
                        FontSize = 12,
                        Padding = new Thickness(4)
                    });
                }

                var candidates = _commandPalette
                    .Where(c =>
                    {
                        if (c == null) return false;
                        var title = PaletteCommandTitle(c).ToLowerInvariant();
                        var desc = PaletteCommandDescription(c).ToLowerInvariant();
                        return string.IsNullOrWhiteSpace(q) || title.Contains(q) || desc.Contains(q);
                    })
                    .ToList();

                var recent = candidates
                    .Where(c => c.LastUsed.HasValue)
                    .OrderByDescending(c => c.LastUsed.Value)
                    .Take(8)
                    .ToList();

                var recentSet = new HashSet<QuickPaletteCommand>(recent);
                var regular = candidates
                    .Where(c => !recentSet.Contains(c))
                    .OrderBy(PaletteCommandTitle)
                    .ToList();

                foreach (var c in recent)
                {
                    AddCommandItem(c);
                }

                if (recent.Count > 0 && regular.Count > 0)
                {
                    results.Items.Add(new ListBoxItem
                    {
                        Tag = null,
                        Content = new Separator(),
                        IsEnabled = false,
                        Focusable = false,
                        Padding = new Thickness(0),
                        Margin = new Thickness(0, 2, 0, 2)
                    });
                }

                foreach (var c in regular)
                {
                    AddCommandItem(c);
                }
            }

            _panelLocalizationBindings.BindAction(
                window,
                "CommandPalette.Localization",
                () =>
                {
                    window.Title = PanelUi("Panel_CommandPalette_Title");
                    UpdateList(query.Text);
                });

            void ExecuteSelection()
            {
                var item = results.SelectedItem as ListBoxItem;
                var command = item?.Tag as QuickPaletteCommand;
                if (command == null || command.Execute == null) return;

                window.Close();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        command.Execute();
                        command.LastUsed = DateTime.Now;
                        SetGlobalStatusResource(
                            "Panel_CommandPalette_Executed_Format",
                            Brushes.DarkGreen,
                            PaletteCommandTitleStatusArgument(command));
                    }
                    catch (Exception ex)
                    {
                        SetGlobalStatusResource("Panel_Common_Error_Format", Brushes.Red, ex.Message);
                    }
                }));
            }

            query.TextChanged += (s, e) =>
            {
                UpdateList(query.Text);
                if (results.Items.Count > 0)
                {
                    var next = FindNextCommandIndex(0, 1);
                    if (next >= 0) results.SelectedIndex = next;
                }
            };
            results.MouseDoubleClick += (s, e) => ExecuteSelection();
            query.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    ExecuteSelection();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Escape)
                {
                    window.Close();
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Down && results.Items.Count > 0)
                {
                    results.Focus();
                    var start = Math.Max(0, results.SelectedIndex + 1);
                    var next = FindNextCommandIndex(start, 1);
                    if (next < 0) next = FindNextCommandIndex(0, 1);
                    results.SelectedIndex = next >= 0 ? next : 0;
                    e.Handled = true;
                }
            };
            results.KeyDown += (s, e) =>
            {
                switch (e.Key)
                {
                    case Key.Enter:
                        ExecuteSelection();
                        e.Handled = true;
                        break;
                    case Key.Escape:
                        window.Close();
                        e.Handled = true;
                        break;
                    case Key.Up:
                    case Key.Down:
                        if (results.Items.Count == 0) return;
                        int step = e.Key == Key.Up ? -1 : 1;
                        int start = results.SelectedIndex + step;
                        if (start < 0 || start >= results.Items.Count)
                        {
                            start = step > 0 ? 0 : results.Items.Count - 1;
                        }

                        var next = FindNextCommandIndex(start, step);
                        if (next >= 0)
                        {
                            results.SelectedIndex = next;
                            e.Handled = true;
                        }
                        break;
                }
            };

            Action focusQuery = () =>
            {
                if (ReferenceEquals(_commandPaletteWindow, window) && !query.IsFocused)
                {
                    query.Focus();
                    Keyboard.Focus(query);
                }
            };
            window.Loaded += (s, e) => focusQuery();
            window.Activated += (s, e) => Dispatcher.BeginInvoke(focusQuery, DispatcherPriority.Input);

            if (owner != null)
            {
                var workArea = System.Windows.SystemParameters.WorkArea;
                double left = owner.Left + (owner.Width - window.Width) / 2.0;
                double top = owner.Top + 52;

                if (double.IsNaN(left) || double.IsNaN(top))
                {
                    left = workArea.Left;
                    top = workArea.Top;
                }

                left = Math.Max(workArea.Left, Math.Min(left, workArea.Right - window.Width - 4));
                top = Math.Max(workArea.Top, Math.Min(top, workArea.Bottom - window.Height - 4));

                window.Left = left;
                window.Top = top;
            }
            else
            {
                var workArea = System.Windows.SystemParameters.WorkArea;
                window.Left = Math.Max(workArea.Left, workArea.Left + (workArea.Width - window.Width) / 2.0);
                window.Top = Math.Max(workArea.Top, workArea.Top + 40);
            }

            UpdateList(string.Empty);
            if (results.Items.Count > 0) results.SelectedIndex = 0;

            Grid.SetRow(query, 0);
            Grid.SetRow(results, 1);
            layout.Children.Add(query);
            layout.Children.Add(results);
            window.Content = layout;
            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(window);
            try
            {
                PauseGlobalHotkeysForPalette();
                window.Show();
                window.Activate();
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    query.Focus();
                    Keyboard.Focus(query);
                }), System.Windows.Threading.DispatcherPriority.Input);
            }
            catch
            {
                _panelLocalizationBindings.UnbindAction(
                    window,
                    "CommandPalette.Localization");
                if (ReferenceEquals(_commandPaletteWindow, window))
                {
                    _commandPaletteWindow = null;
                    _commandPaletteQuery = null;
                }
                ResumeGlobalHotkeysForPalette();
                throw;
            }
        }

    }
}
