using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace NavisHelper.WPF
{
    public partial class NavisHelperPanel : UserControl
    {
        // ============================================================
        //  ВКЛАДКА: Инструменты
        // ============================================================

        private UIElement CreateToolsContent()
        {
            var stack = new StackPanel { Margin = new Thickness(8) };

            stack.Children.Add(CreateGroupHeader("Окраска"));
            var paintRow = new WrapPanel();
            var colorsByNameBtn = Btn("colors_by_name", "\U0001F3A8", "Colors By Name", "Окраска элементов по имени из текстового файла (name;R,G,B;transparency)", "ColorsByName.CBC", ButtonKind.Primary);
            colorsByNameBtn.Margin = new Thickness(0, 2, 4, 2);
            paintRow.Children.Add(colorsByNameBtn);
            paintRow.Children.Add(ActionBtn("color_by_property", "\U0001F9EE", "Color by Property", "Авто-окраска по уникальному значению свойства (Система/Спец/Отметка)", OnColorByProperty, requiresSelection: true));
            var overridePdmsBtn = Btn("override_pdms", "\U0001F527", "Override PDMS", "Переопределить цвета объектов PDMS", "HideItems.CBC");
            overridePdmsBtn.Margin = new Thickness(0, 2, 4, 2);
            paintRow.Children.Add(overridePdmsBtn);
            paintRow.Children.Add(ActionBtn("reset_color_overrides", "\U0001F504", "Сбросить overrides", "Сбросить все overrides цвета/прозрачности модели", ResetAllOverrides, kind: ButtonKind.Destructive));
            stack.Children.Add(paintRow);

            stack.Children.Add(CreateMatchColorSection());

            stack.Children.Add(CreateColorTransferSection());

            stack.Children.Add(CreateColorHistorySection());

            return stack;
        }

        // ============================================================
        //  ВКЛАДКА: Навигация
        // ============================================================

        private UIElement CreateNavigationContent()
        {
            var stack = new StackPanel { Margin = new Thickness(8) };

            stack.Children.Add(CreateGroupHeader("Навигация по дереву"));
            var navRow1 = new WrapPanel();
            navRow1.Children.Add(NavBtn("parent", "\U00002B06", "Parent", "Ctrl+Q", "Выбрать родительский элемент для каждого выделенного объекта.\nПоднимается на 1 уровень вверх по дереву модели.", TreeNavigation.SelectParents, requiresSelection: true));
            navRow1.Children.Add(NavBtn("child", "\U00002B07", "Child", "Ctrl+W", "Выбрать все дочерние элементы (1 уровень вниз).\nРаскрывает следующий уровень дерева.", TreeNavigation.SelectChildren, requiresSelection: true));
            navRow1.Children.Add(NavBtn("sibling", "\U00002194", "Sibling", "Ctrl+E", "Выбрать всех соседей — элементы с тем же родителем.\nПолезно для выделения всех объектов на одном уровне.", TreeNavigation.SelectSiblings, requiresSelection: true));
            stack.Children.Add(navRow1);

            var navRow2 = new WrapPanel();
            navRow2.Children.Add(NavBtn("leaf", "\U0001F343", "Leaf", null, "Выбрать конечные узлы (листья) — элементы без потомков.\nФильтрует только геометрические объекты на самом нижнем уровне.", TreeNavigation.SelectLeafNodes, requiresSelection: true));
            navRow2.Children.Add(NavBtn("all_under", "\U0001F4C2", "All Under", null, "Выбрать ВСЕ потомки на всех уровнях вниз.\nПолный раскрыв ветки дерева.", TreeNavigation.SelectAllUnder, requiresSelection: true));
            stack.Children.Add(navRow2);

            stack.Children.Add(CreateGroupHeader("Операции с выборкой"));
            var setOpsRow = new WrapPanel();
            setOpsRow.Children.Add(ActionBtn("selection_invert", "\U00002195", "Invert", "Инвертировать текущую выборку", InvertSelection, requiresSelection: true));
            setOpsRow.Children.Add(ActionBtn("selection_isolate", "\U0001F5D1", "Isolate", "Скрыть всё, кроме выделенного", IsolateSelection, 0, ButtonKind.Destructive, true));
            setOpsRow.Children.Add(ActionBtn("selection_unhide", "\U0001F513", "Unhide All", "Показать все элементы", UnhideAll));
            stack.Children.Add(setOpsRow);

            var selectByPropertyRow = new WrapPanel();
            selectByPropertyRow.Children.Add(ActionBtn("selection_set_prop", "\U0001F50D", "Select by Property", "Скопировать выборку по значению свойства", OnSelectByPropertyValue, requiresSelection: true));
            stack.Children.Add(selectByPropertyRow);

            stack.Children.Add(CreateGroupHeader("Поисковые наборы"));
            var searchSetRow = new WrapPanel();
            searchSetRow.Children.Add(ActionBtn("selection_search_set", "\U0001F4C1", "Сохранить поиск", "Создать папку и сохранить динамический поисковый набор", OnCreateSearchSelectionSet));
            stack.Children.Add(searchSetRow);

            var copyFilterRow = new WrapPanel();
            copyFilterRow.Children.Add(Btn("copy_names", "\U0001F4CB", "Копировать имена", "Копировать DisplayName выделенных объектов в буфер обмена (Ctrl+M)", "CopySelectedNames.CBC", requiresSelection: true));
            copyFilterRow.Children.Add(ActionBtn("selection_bounds_info", "\U0001F4D0", "Габариты", "Показать габариты текущего выделения и скопировать значения в буфер обмена", ShowAndCopySelectionBounds, requiresSelection: true));
            copyFilterRow.Children.Add(Btn("filter", "\U0001F50D", "Filter by list", "Скрыть/показать элементы модели по списку имён из текстового файла", "FilterModels.COMPANY"));
            stack.Children.Add(copyFilterRow);

            stack.Children.Add(CreateGroupHeader("Запомнить выборку"));
            var memoryRow = new WrapPanel();
            memoryRow.Children.Add(ActionBtn("selection_save", "\U0001F4BE", "Запомнить", "Сохранить текущую выборку в память (Ctrl+Shift+1)", () => SaveSelectionSetSlot(0), requiresSelection: true));
            memoryRow.Children.Add(ActionBtn("selection_recall", "\U000021A9", "Вернуть", "Вернуть выборку из памяти (Ctrl+1)", () => RecallSelectionSetSlot(0)));
            stack.Children.Add(memoryRow);
            _selectionMemoryText = new TextBlock
            {
                Text = "память пуста",
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(2, 2, 0, 8)
            };
            stack.Children.Add(_selectionMemoryText);
            UpdateSelectionMemoryIndicator();

            stack.Children.Add(new TextBlock
            {
                Text = "Горячие клавиши:\n" +
                       "  Ctrl+Q  Parent (вверх по дереву)\n" +
                       "  Ctrl+W  Child (вниз по дереву)\n" +
                       "  Ctrl+E  Sibling (соседи)\n" +
                       "  Ctrl+M  Копировать имена\n" +
                       "  Ctrl+Shift+P  Командная палитра\n" +
                       "  Ctrl+Shift+H  Сохранить скрин текущего вида\n" +
                       "  Ctrl+1  Вернуть выборку\n" +
                       "  Ctrl+Shift+1  Запомнить выборку",
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 4, 0, 0)
            });

            return stack;
        }

        // ============================================================
        //  ВКЛАДКА: AI Цвета
        // ============================================================

        private UIElement CreateAIColorsContent()
        {
            // Grid: верхняя (Auto), лог (Star)
            var grid = new Grid { Margin = new Thickness(8) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // 0: настройки
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: лог — тянется

            // === Верхняя часть (фиксированная) ===
            var topStack = new StackPanel();

            // Модель и ключ настраиваются на вкладке «⚙ Настройки» (SettingsTab.cs)
            var settingsHint = new TextBlock
            {
                Text = "Модель и API-ключ — на вкладке «⚙ Настройки»",
                FontSize = 10,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(120, 120, 120)),
                Margin = new Thickness(0, 0, 0, 6)
            };
            topStack.Children.Add(settingsHint);

            // --- Цветовая схема ---
            topStack.Children.Add(new TextBlock { Text = "Цветовая схема:", FontSize = 12, Margin = new Thickness(0, 4, 0, 4) });

            _schemeListBox = new ListBox { Height = 220, Margin = new Thickness(0, 0, 0, 6), FontSize = 12 };
            foreach (ColorSchemeType scheme in Enum.GetValues(typeof(ColorSchemeType)))
                _schemeListBox.Items.Add(CreateSchemeListItem(scheme));

            try { _schemeListBox.SelectedIndex = (int)AIConfig.Instance.GetColorSchemeType() - 1; }
            catch { _schemeListBox.SelectedIndex = 0; }

            _schemeListBox.SelectionChanged += OnSchemeSelectionChanged;
            topStack.Children.Add(_schemeListBox);

            topStack.Children.Add(new TextBlock { Text = "Превью палитры:", FontSize = 12, Margin = new Thickness(0, 0, 0, 4) });
            _previewPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            topStack.Children.Add(_previewPanel);
            UpdateColorPreview();

            topStack.Children.Add(CreateSeparator());

            // --- Кнопка запуска ---
            var applyBtn = new Button
            {
                Content = MakeButtonContent("ai_color", "\U0001F3A8", "Применить AI-окраску"),
                Height = 36, FontSize = 13,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 6), Cursor = Cursors.Hand,
                ToolTip = "Сохранить выбранную схему и отправить выделенные объекты в AI для подбора цветов",
                Style = UiTheme.ButtonStyle(ButtonKind.Primary)
            };
            applyBtn.Click += OnApplyColorScheme;
            topStack.Children.Add(applyBtn);

            topStack.Children.Add(CreateSeparator());

            // --- Заголовок + кнопки лога ---
            var logHeader = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            logHeader.Children.Add(new TextBlock { Text = "Ответ модели:", FontSize = 12, VerticalAlignment = VerticalAlignment.Center });

            var logBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(logBtns, Dock.Right);

            var copyBtn = new Button
            {
                Content = "Копировать", FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(4, 0, 0, 0), Cursor = Cursors.Hand,
                ToolTip = "Копировать ответ в буфер обмена",
                Style = UiTheme.ButtonStyle(ButtonKind.Neutral)
            };
            copyBtn.Click += (s, e) =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(_aiResponseLog?.Text))
                    {
                        var text = _aiResponseLog.Text;
                        Exception threadEx = null;
                        var thread = new System.Threading.Thread(() =>
                        {
                            try
                            {
                                System.Windows.Forms.Clipboard.SetText(text);
                            }
                            catch (Exception ex2)
                            {
                                threadEx = ex2;
                            }
                        });
                        thread.SetApartmentState(System.Threading.ApartmentState.STA);
                        thread.Start();
                        thread.Join(2000);
                        if (threadEx != null)
                            MessageBox.Show("Не удалось скопировать: " + threadEx.Message, "AI Цвета");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Не удалось скопировать: " + ex.Message, "AI Цвета");
                }
            };
            logBtns.Children.Add(copyBtn);

            var saveBtn = new Button
            {
                Content = "Сохранить", FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(4, 0, 0, 0), Cursor = Cursors.Hand,
                ToolTip = "Сохранить ответ в .txt файл",
                Style = UiTheme.ButtonStyle(ButtonKind.Neutral)
            };
            saveBtn.Click += OnSaveAIResponse;
            logBtns.Children.Add(saveBtn);

            logHeader.Children.Add(logBtns);
            topStack.Children.Add(logHeader);

            Grid.SetRow(topStack, 0);
            grid.Children.Add(topStack);

            // === Нижняя часть (тянется за формой) ===
            _aiResponseLog = new TextBox
            {
                MinHeight = 100,
                FontSize = 11,
                FontFamily = new FontFamily("Consolas"),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(WpfColor.FromRgb(245, 245, 245)),
                BorderBrush = new SolidColorBrush(WpfColor.FromRgb(200, 200, 200)),
            };
            Grid.SetRow(_aiResponseLog, 1);
            grid.Children.Add(_aiResponseLog);

            return grid;
        }
    }
}

