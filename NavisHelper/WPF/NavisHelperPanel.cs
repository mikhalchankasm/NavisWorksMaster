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
        private TextBox _folderPathBox;

        private CheckBox _overwriteCheck;

        private ListBox _schemeListBox;

        private WrapPanel _previewPanel;

        private ComboBox _modelCombo;

        private CheckBox _thinkingCheck;

        private TextBox _aiResponseLog;

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

        private ProgressBar _globalBusyBar;

        private TabControl _mainTabControl;

        private TabItem _modelTab;
        private TabItem _colorsTab;
        private TabItem _viewsTab;
        private TabItem _settingsTab;
        private readonly SelectionGatingController _selectionGating;

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
            public string Label { get; set; }
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
            public string Title { get; set; }
            public string Description { get; set; }
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

        // null RGB = "без подсветки"

        private static readonly (string Name, byte? R, byte? G, byte? B)[] ClashColors = new[]
        {
            ("Без подсветки", (byte?)null, (byte?)null, (byte?)null),
            ("Красный",       (byte?)255, (byte?)50,   (byte?)50),
            ("Синий",         (byte?)50,  (byte?)100,  (byte?)255),
            ("Зелёный",       (byte?)50,  (byte?)200,  (byte?)50),
            ("Оранжевый",     (byte?)255, (byte?)165,  (byte?)0),
            ("Жёлтый",        (byte?)255, (byte?)255,  (byte?)50),
            ("Фиолетовый",    (byte?)180, (byte?)50,   (byte?)255),
            ("Голубой",       (byte?)50,  (byte?)200,  (byte?)255),
            ("Розовый",       (byte?)255, (byte?)100,  (byte?)180),
            ("Белый",         (byte?)255, (byte?)255,  (byte?)255),
            ("Тёмно-красный", (byte?)180, (byte?)0,    (byte?)0),
            ("Тёмно-синий",   (byte?)0,   (byte?)50,   (byte?)180),
            ("Бирюзовый",     (byte?)0,   (byte?)200,  (byte?)200),
            ("Лайм",          (byte?)150, (byte?)255,  (byte?)0),
            ("Коричневый",    (byte?)160, (byte?)100,  (byte?)50),
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

            _modelTab = new TabItem { Header = "🌳 Модель", Content = WrapInScroll(stack) };
            return _modelTab;
        }

        private TabItem CreateColorsTab()
        {
            var manual = WrapInScroll(CreateToolsContent());
            var ai = CreateAIColorsContent();

            var content = UiTheme.Segmented(
                new[] { "Ручная", "AI" },
                new UIElement[] { manual, ai },
                _panelUiSettings.ColorsSegment,
                idx => { _panelUiSettings.ColorsSegment = idx; _panelUiSettings.Save(); },
                out _selectColorsSegment);

            _colorsTab = new TabItem
            {
                Header = "🎨 Цвета",
                Content = WithTabPadding(content)
            };
            return _colorsTab;
        }

        private TabItem CreateViewsTab()
        {
            var markup = CreateViewpointsContent();
            var hmTab = CreateHeightMarksTab();
            var heightMarks = (UIElement)hmTab.Content;
            hmTab.Content = null;

            var content = UiTheme.Segmented(
                new[] { "Разметка", "Отметки" },
                new UIElement[] { markup, heightMarks },
                _panelUiSettings.ViewsSegment,
                idx => { _panelUiSettings.ViewsSegment = idx; _panelUiSettings.Save(); },
                out _selectViewsSegment);

            _viewsTab = new TabItem
            {
                Header = "📷 Виды",
                Content = WithTabPadding(content)
            };
            return _viewsTab;
        }

        private TabItem CreateSettingsTab()
        {
            var builder = new NavisHelperSettingsTabBuilder(
                (text, color) => SetGlobalStatus(text, color),
                OpenFileInShell,
                GetModelLogPath,
                OpenDevScriptsMenu,
                ExecutePlugin,
                Dispatcher);
            _settingsTab = builder.Build();
            _modelCombo = builder.ModelCombo;
            _thinkingCheck = builder.ThinkingCheck;
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
            Background = Brushes.Transparent;
            UiTheme.InstallImplicitControlStyles(this);
            Loaded += OnPanelLoaded;
            Unloaded += OnPanelUnloaded;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _globalStatusBar = new TextBlock
            {
                Text = "Готово",
                FontSize = 10,
                Foreground = Brushes.Gray,
                Padding = new Thickness(6, 0, 6, 0),
                Height = 22,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
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

            Content = root;

            RegisterPaletteCommands();
        }

        private void OnPanelLoaded(object sender, RoutedEventArgs e)
        {
            Current = this;
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

        private void RegisterPaletteCommand(string title, string description, Action action)
        {
            if (string.IsNullOrWhiteSpace(title) || action == null) return;
            _commandPalette.RemoveAll(c => string.Equals(c.Title, title, StringComparison.OrdinalIgnoreCase));
            _commandPalette.Add(new QuickPaletteCommand { Title = title, Description = description, Execute = action });
        }

        private void RegisterPaletteCommands()
        {
            _commandPalette.Clear();

            RegisterPaletteCommand("Colors By Name", "Окрашивание по списку name;R,G,B[;transparency]", () => ExecutePlugin("ColorsByName.CBC"));
            RegisterPaletteCommand("Override PDMS", "Отключить/скрыть элементы, соответствующие PDMS-списку", () => ExecutePlugin("HideItems.CBC"));
            RegisterPaletteCommand("Match Color", "Считать цвет первого выбранного объекта и применить к выделению", OnPickAndApplyColor);
            RegisterPaletteCommand("Match Color: Считать", "Считать цвет с выбранного объекта", () => OnPickColor(null, null));
            RegisterPaletteCommand("Match Color: Применить", "Применить считанный цвет к текущему выделению", () => OnPasteColor(null, null));
            RegisterPaletteCommand("Color by Property", "Авто-окраска выделения по значениям свойства", OnColorByProperty);
            RegisterPaletteCommand("Reset all overrides", "Сбросить все цветовые/прозрачностные overrides", ResetAllOverrides);
            RegisterPaletteCommand("Export colors", "Экспортировать цвета и прозрачность выделения в отдельные файлы", () => ExecutePlugin("ExportColors.CBC"));
            RegisterPaletteCommand("Import colors", "Загрузить цвета из файлов и применить к текущему выделению", () => ExecutePlugin("ImportColors.CBC"));
            RegisterPaletteCommand("AI-окраска", "Применить AI-цвета по схеме и модели", () => OnApplyColorScheme(null, null));
            RegisterPaletteCommand("Copy names", "Скопировать DisplayName выделенных элементов", () => ExecutePlugin("CopySelectedNames.CBC"));
            RegisterPaletteCommand("Filter by list", "Отфильтровать модель списком имён из файла", () => ExecutePlugin("FilterModels.COMPANY"));
            RegisterPaletteCommand("Select by Property Value", "Выбрать по значению свойства из выделения", OnSelectByPropertyValue);
            RegisterPaletteCommand("Сохранить поисковый набор", "Создать папку и сохранить динамический Search Set", OnCreateSearchSelectionSet);

            RegisterPaletteCommand("Parent", "Выбрать родительские элементы для текущего выделения", () => TreeNavigation.SafeExecute(TreeNavigation.SelectParents));
            RegisterPaletteCommand("Child", "Выбрать дочерние элементы", () => TreeNavigation.SafeExecute(TreeNavigation.SelectChildren));
            RegisterPaletteCommand("Sibling", "Выбрать соседние элементы (один уровень с выделенными)", () => TreeNavigation.SafeExecute(TreeNavigation.SelectSiblings));
            RegisterPaletteCommand("Leaf", "Выбрать концевые узлы (leaf)", () => TreeNavigation.SafeExecute(TreeNavigation.SelectLeafNodes));
            RegisterPaletteCommand("All Under", "Выбрать все потомки выделенных элементов", () => TreeNavigation.SafeExecute(TreeNavigation.SelectAllUnder));
            RegisterPaletteCommand("Invert Selection", "Инвертировать текущую выборку", InvertSelection);
            RegisterPaletteCommand("Isolate", "Скрыть все, кроме текущей выборки", IsolateSelection);
            RegisterPaletteCommand("Unhide All", "Показать все элементы", UnhideAll);
            RegisterPaletteCommand("Запомнить выборку", "Сохранить текущую выборку в ячейку памяти", () => SaveSelectionSetSlot(0));
            RegisterPaletteCommand("Вернуть выборку", "Восстановить выборку из ячейки памяти", () => RecallSelectionSetSlot(0));

            RegisterPaletteCommand("Markup Viewpoint", "Пометить выделенные элементы эллипсами на текущем виде", () => ExecutePlugin("MarkupViewpoint.CBC"));
            RegisterPaletteCommand("Height Marks", "Открыть таблицу отметок Max Z и размерных линий до уровня Z", () => ShowHeightMarksTab());
            RegisterPaletteCommand("Height Marks: Graphics", "Показать временные Graphics.Text2D-метки по текущему выделению", ShowHeightGraphicsMarkers);
            RegisterPaletteCommand("Height Marks: Screenshot", "Сохранить изображение текущего вида", SaveHeightScreenshot);
            RegisterPaletteCommand("Top View + Section", "Переключить на вид сверху, приблизиться к выделению и включить секцию", () => ExecutePlugin("TopViewSection.CBC"));
            RegisterPaletteCommand("Top View Bounding Rect", "Нарисовать габаритный прямоугольник вокруг выделения", () => ExecutePlugin("TopViewBoundingRect.CBC"));
            RegisterPaletteCommand("Top View Bounding Hatch", "Заштриховать экранный габарит выделения на текущем ракурсе", () => ExecutePlugin("TopViewBoundingHatch.CBC"));
            RegisterPaletteCommand("Selection Hatch Marker", "Показать временный многоугольный маркер выделения через clash overlay", () => ExecutePlugin("SelectionHatchMarker.CBC"));
            RegisterPaletteCommand("Selection Bounds Hatch Marker", "Показать временный многоугольный маркер по габаритам объектов через clash overlay", () => ExecutePlugin("SelectionHatchBoundsMarker.CBC"));
            RegisterPaletteCommand("Sort Viewpoints", "Сортировка точек обзора", () => ExecutePlugin("SortViewpoints.COMPANY"));
            RegisterPaletteCommand("Save Viewpoints", "Сохранить список точек обзора", () => ExecutePlugin("SaveViewpiontList.COMPANY"));
            RegisterPaletteCommand("Section Box по выделению", "Установить section box и отобразить контекст", () => ShowSelectionSectionBox());
            RegisterPaletteCommand("Сброс секции", "Сбросить section box и прозрачность контекста", ResetSelectionSectionBox);
            RegisterPaletteCommand("Габариты выделения", "Показать габариты выделения и скопировать значения в буфер обмена", ShowAndCopySelectionBounds);

            RegisterPaletteCommand("CSV → атрибуты", "Загрузить атрибуты из CSV", () => ExecutePlugin("CsvAttributeLoader.CSVL"));
            RegisterPaletteCommand("Import PS-листы", "Импортировать PS-листы", () => ExecutePlugin("ImportPslists.CBC"));
            RegisterPaletteCommand("Save hierarchy", "Сохранить дерево модели", () => ExecutePlugin("SaveHierarhy.COMPANY"));
            RegisterPaletteCommand("Save NWD 2018", "Экспортировать как Navisworks 2018", () => ExecutePlugin("SaveAsNavis2018.MS"));
            RegisterPaletteCommand("Export properties to Excel", "Экспортировать свойства выделенных элементов", ExportSelectedPropertiesToExcelLikeFile);

            RegisterPaletteCommand("Clashes: Load tests", "Загрузить все тесты коллизий", LoadClashTests);
            RegisterPaletteCommand("Clashes: Run all tests", "Запустить все Clash Test без сохранения отчёта или модели", RunAllClashTests);
            RegisterPaletteCommand("Clashes: Preview selected", "Показать выбранную коллизию", PreviewSelectedClash);
            RegisterPaletteCommand("Clashes: Clash viewpoint", "Сохранить вид с выбранной коллизией", SaveClashViewpoint);
            RegisterPaletteCommand("Clashes: Assign to", "Присвоить ответственного выбранной коллизии", () => SetClashAssignedToPrompt());
            RegisterPaletteCommand("Clashes: Section Box (коллизии)", "Включить/выключить Section Box для режима просмотра коллизий", SectionBoxHelper.Toggle);
            RegisterPaletteCommand("Clashes: Marker", "Показать/скрыть маркер коллизии", ToggleClashMarker);
            RegisterPaletteCommand("Clashes: Plane", "Показать/скрыть плоскость по выделению", ToggleSelectionPlane);
            RegisterPaletteCommand("Clashes: Export BCF", "Экспортировать выбранную коллизию в BCF-подобный файл", ExportSelectedClashesToBcf);

            RegisterPaletteCommand("Настройки", "Открыть вкладку «⚙ Настройки»", () =>
            {
                if (_mainTabControl != null && _settingsTab != null)
                    _mainTabControl.SelectedItem = _settingsTab;
            });
            RegisterPaletteCommand("Открыть лог", "Открыть файл NavisHelper-логов", () => OpenFileInShell(GetModelLogPath()));
            RegisterPaletteCommand("Dev: загрузить DLL", "Загрузить DLL с IDevScript", OpenDevScriptsMenu);
            RegisterPaletteCommand("О программе", "Открыть диалог «О программе»", () => ExecutePlugin("AboutNavisHelper.CBC"));
            RegisterPaletteCommand("Command Palette", "Открыть командную палитру", OpenCommandPalette);
        }

        private void SetGlobalStatus(string text, Brush color = null)
        {
            if (_globalStatusBar == null) return;
            _globalStatusBar.Text = (text ?? string.Empty).Replace("\r\n", " | ").Replace("\n", " | ");
            _globalStatusBar.Foreground = color ?? Brushes.Gray;
        }

        private void SetGlobalBusy(bool isBusy, string text = null)
        {
            if (!string.IsNullOrWhiteSpace(text))
                SetGlobalStatus(text, isBusy ? Brushes.DarkOrange : Brushes.Gray);

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

        private static void OpenFileInShell(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                {
                    MessageBox.Show("Файл не найден.", "NavisHelper", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть файл: " + ex.Message, "NavisHelper", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenDevScriptsMenu()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "DLL (*.dll)|*.dll",
                    Title = "Выберите DLL с Dev-скриптами"
                };
                if (dlg.ShowDialog() != true) return;

                var scripts = DevScriptLoader.LoadScripts(dlg.FileName);
                if (scripts == null || scripts.Count == 0)
                {
                    MessageBox.Show("Скрипты в DLL не найдены.", "NavisHelper", MessageBoxButton.OK, MessageBoxImage.Information);
                    SetGlobalStatus($"DLL загружена: {Path.GetFileName(dlg.FileName)} — скрипты не найдены", Brushes.Orange);
                    return;
                }

                var owner = Window.GetWindow(this);
                var wnd = new Window
                {
                    Title = "Dev скрипты",
                    Width = 360,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    WindowStyle = WindowStyle.ToolWindow,
                    Owner = owner
                };

                var panel = new StackPanel { Margin = new Thickness(8) };
                panel.Children.Add(new TextBlock
                {
                    Text = $"DLL: {Path.GetFileName(dlg.FileName)}",
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                });

                foreach (var script in scripts)
                {
                    var scriptRef = script;
                    panel.Children.Add(ActionBtn("dev_run", "\U000025B6", script.Name, $"Запустить скрипт {script.Name}", () =>
                    {
                        scriptRef.Run();
                        SetGlobalStatus($"Dev скрипт запущен: {scriptRef.Name}", Brushes.DarkGreen);
                    }));
                }

                wnd.Content = new ScrollViewer { Content = panel };
                wnd.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка загрузки DLL: " + ex.Message, "NavisHelper", MessageBoxButton.OK, MessageBoxImage.Error);
                SetGlobalStatus("Ошибка загрузки DLL", Brushes.Red);
            }
        }

        private void OpenCommandPalette()
        {
            if (_commandPalette.Count == 0)
            {
                MessageBox.Show("Команды не загружены.", "Командная палитра", MessageBoxButton.OK, MessageBoxImage.Information);
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
                Title = "Командная палитра",
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
                        Content = $"{command.Title} — {command.Description}",
                        FontSize = 12,
                        Padding = new Thickness(4)
                    });
                }

                var candidates = _commandPalette
                    .Where(c =>
                    {
                        if (c == null) return false;
                        var title = (c.Title ?? string.Empty).ToLowerInvariant();
                        var desc = (c.Description ?? string.Empty).ToLowerInvariant();
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
                    .OrderBy(c => c.Title)
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
                        SetGlobalStatus($"Выполнена команда: {command.Title}", Brushes.DarkGreen);
                    }
                    catch (Exception ex)
                    {
                        SetGlobalStatus($"Ошибка: {ex.Message}", Brushes.Red);
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
