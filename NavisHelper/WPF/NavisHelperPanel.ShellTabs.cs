using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NavisHelper.Core.Localization;
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

            stack.Children.Add(CreateGroupHeader("Panel_Colors_Group_Coloring"));
            var paintRow = new WrapPanel();
            var colorsByNameBtn = Btn("colors_by_name", "\U0001F3A8", "Panel_Colors_ByName_Action", "Panel_Colors_ByName_ToolTip", "ColorsByName.CBC", ButtonKind.Primary);
            colorsByNameBtn.Margin = new Thickness(0, 2, 4, 2);
            paintRow.Children.Add(colorsByNameBtn);
            paintRow.Children.Add(ActionBtn("color_by_property", "\U0001F9EE", "Panel_Colors_Property_Action", "Panel_Colors_Property_Action_ToolTip", OnColorByProperty, requiresSelection: true));
            var overridePdmsBtn = Btn("override_pdms", "\U0001F527", "Panel_Colors_OverridePdms_Action", "Panel_Colors_OverridePdms_ActionObjectColors", "HideItems.CBC");
            overridePdmsBtn.Margin = new Thickness(0, 2, 4, 2);
            paintRow.Children.Add(overridePdmsBtn);
            paintRow.Children.Add(ActionBtn("reset_color_overrides", "\U0001F504", "Panel_ResetOverrides", "Panel_Colors_ResetOverrides_ToolTip", ResetAllOverrides, kind: ButtonKind.Destructive));
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

            stack.Children.Add(CreateGroupHeader("Panel_Model_Group_TreeNavigation"));
            var navRow1 = new WrapPanel();
            navRow1.Children.Add(NavBtn("parent", "\U00002B06", "Panel_Parent", "Ctrl+Q", "Panel_Model_Parent_ToolTip", TreeNavigation.SelectParents, requiresSelection: true));
            navRow1.Children.Add(NavBtn("child", "\U00002B07", "Panel_Child", "Ctrl+W", "Panel_Model_Child_ToolTip", TreeNavigation.SelectChildren, requiresSelection: true));
            navRow1.Children.Add(NavBtn("sibling", "\U00002194", "Panel_Sibling", "Ctrl+E", "Panel_Model_Siblings_ToolTip", TreeNavigation.SelectSiblings, requiresSelection: true));
            stack.Children.Add(navRow1);

            var navRow2 = new WrapPanel();
            navRow2.Children.Add(NavBtn("leaf", "\U0001F343", "Panel_Leaf", null, "Panel_Model_Leaves_ToolTip", TreeNavigation.SelectLeafNodes, requiresSelection: true));
            navRow2.Children.Add(NavBtn("all_under", "\U0001F4C2", "Panel_AllUnder", null, "Panel_Model_AllDescendants_ToolTip", TreeNavigation.SelectAllUnder, requiresSelection: true));
            stack.Children.Add(navRow2);

            stack.Children.Add(CreateGroupHeader("Panel_Model_Group_SelectionOperations"));
            var setOpsRow = new WrapPanel();
            setOpsRow.Children.Add(ActionBtn("selection_invert", "\U00002195", "Panel_Invert", "Panel_Selection_Invert_ToolTip", InvertSelection, requiresSelection: true));
            setOpsRow.Children.Add(ActionBtn("selection_isolate", "\U0001F5D1", "Panel_Isolate", "Panel_Model_Isolate_ToolTip", IsolateSelection, 0, ButtonKind.Destructive, true));
            setOpsRow.Children.Add(ActionBtn("selection_unhide", "\U0001F513", "Panel_UnhideAll", "Panel_Selection_UnhideAll_ToolTip", UnhideAll));
            stack.Children.Add(setOpsRow);

            var selectByPropertyRow = new WrapPanel();
            selectByPropertyRow.Children.Add(ActionBtn("selection_set_prop", "\U0001F50D", "Panel_Colors_SelectByProperty_Label", "Panel_Colors_SelectByProperty_Action", OnSelectByPropertyValue, requiresSelection: true));
            stack.Children.Add(selectByPropertyRow);

            stack.Children.Add(CreateGroupHeader("Panel_Model_Group_SearchSets"));
            var searchSetRow = new WrapPanel();
            searchSetRow.Children.Add(ActionBtn("selection_search_set", "\U0001F4C1", "Panel_SaveSearch", "Panel_Colors_SearchSet_Action_ToolTip", OnCreateSearchSelectionSet));
            stack.Children.Add(searchSetRow);

            var copyFilterRow = new WrapPanel();
            copyFilterRow.Children.Add(Btn("copy_names", "\U0001F4CB", "Panel_CopyNames", "Panel_Model_CopyNames_ToolTip", "CopySelectedNames.CBC", requiresSelection: true));
            copyFilterRow.Children.Add(ActionBtn("selection_bounds_info", "\U0001F4D0", "Panel_Bounds", "Panel_Model_BoundsInfo_ToolTip", ShowAndCopySelectionBounds, requiresSelection: true));
            copyFilterRow.Children.Add(Btn("filter", "\U0001F50D", "Panel_Model_FilterList_Action", "Panel_Model_FilterList_ToolTip", "FilterModels.COMPANY"));
            stack.Children.Add(copyFilterRow);

            stack.Children.Add(CreateGroupHeader("Panel_Model_Group_SelectionMemory"));
            var memoryRow = new WrapPanel();
            memoryRow.Children.Add(ActionBtn("selection_save", "\U0001F4BE", "Panel_Remember", "Panel_Model_MemorySave_ToolTip", () => SaveSelectionSetSlot(0), requiresSelection: true));
            memoryRow.Children.Add(ActionBtn("selection_recall", "\U000021A9", "Panel_Restore", "Panel_Model_MemoryRestore_ToolTip", () => RecallSelectionSetSlot(0)));
            stack.Children.Add(memoryRow);
            _selectionMemoryText = new TextBlock
            {
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(2, 2, 0, 8)
            };
            stack.Children.Add(_selectionMemoryText);
            _panelLocalizationBindings.BindAction(
                _selectionMemoryText,
                "SelectionMemory.Status",
                UpdateSelectionMemoryIndicator);

            var hotkeys = new TextBlock
            {
                FontSize = 11,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 4, 0, 0)
            };
            _panelLocalizationBindings.BindText(
                hotkeys,
                "Panel_Model_TreeHotkeys_Help");
            stack.Children.Add(hotkeys);

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
                FontSize = 10,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(120, 120, 120)),
                Margin = new Thickness(0, 0, 0, 6)
            };
            _panelLocalizationBindings.BindText(
                settingsHint,
                "Panel_Colors_Ai_ReadyHint");
            topStack.Children.Add(settingsHint);

            // --- Цветовая схема ---
            topStack.Children.Add(BindPanelText(
                new TextBlock { FontSize = 12, Margin = new Thickness(0, 4, 0, 4) },
                "Panel_ColorScheme"));

            _schemeListBox = new ListBox { Height = 220, Margin = new Thickness(0, 0, 0, 6), FontSize = 12 };
            foreach (ColorSchemeType scheme in Enum.GetValues(typeof(ColorSchemeType)))
                _schemeListBox.Items.Add(CreateSchemeListItem(scheme));

            try { _schemeListBox.SelectedIndex = (int)AIConfig.Instance.GetColorSchemeType() - 1; }
            catch { _schemeListBox.SelectedIndex = 0; }

            _schemeListBox.SelectionChanged += OnSchemeSelectionChanged;
            topStack.Children.Add(_schemeListBox);

            topStack.Children.Add(BindPanelText(
                new TextBlock { FontSize = 12, Margin = new Thickness(0, 0, 0, 4) },
                "Panel_PalettePreview"));
            _previewPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            topStack.Children.Add(_previewPanel);
            UpdateColorPreview();

            topStack.Children.Add(CreateSeparator());

            // --- Кнопка запуска ---
            _aiApplyButton = new Button
            {
                Content = MakeLocalizedButtonContent(
                    "ai_color",
                    "\U0001F3A8",
                    "Panel_Colors_AiApply_Action"),
                Height = 36, FontSize = 13,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 6), Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(ButtonKind.Primary)
            };
            _panelLocalizationBindings.BindToolTip(
                _aiApplyButton,
                "Panel_Colors_Ai_Apply_ToolTip");
            _aiApplyButton.Click += OnApplyColorScheme;
            topStack.Children.Add(_aiApplyButton);

            _localPaletteButton = new Button
            {
                Height = 32,
                FontSize = 12,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(ButtonKind.Neutral)
            };
            _panelLocalizationBindings.BindContent(
                _localPaletteButton,
                "Panel_Colors_Ai_LocalPalette_Action");
            _panelLocalizationBindings.BindToolTip(
                _localPaletteButton,
                "Panel_Colors_Ai_LocalPalette_ToolTip");
            _localPaletteButton.Click += OnApplyLocalPalette;
            topStack.Children.Add(_localPaletteButton);

            var dataNotice = new TextBlock
            {
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(
                    WpfColor.FromRgb(120, 120, 120)),
                Margin = new Thickness(0, 0, 0, 6)
            };
            _panelLocalizationBindings.BindText(
                dataNotice,
                "Panel_Colors_Ai_DataNotice");
            topStack.Children.Add(dataNotice);

            topStack.Children.Add(CreateSeparator());

            // --- Заголовок + кнопки лога ---
            var logHeader = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            logHeader.Children.Add(BindPanelText(
                new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center },
                "Panel_ModelResponse"));

            var logBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(logBtns, Dock.Right);

            var copyBtn = new Button
            {
                FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(4, 0, 0, 0), Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(ButtonKind.Neutral)
            };
            _panelLocalizationBindings.BindContent(copyBtn, "Panel_Copy");
            _panelLocalizationBindings.BindToolTip(
                copyBtn,
                "Panel_Colors_Ai_CopyResponse_ToolTip");
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
                            MessageBox.Show(
                                UiLocalizationService.Current.Format(
                                    "Panel_Common_CopyFailed_Format",
                                    threadEx.Message),
                                PanelUi("Panel_Colors_Ai_Title"));
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        UiLocalizationService.Current.Format(
                            "Panel_Common_CopyFailed_Format",
                            ex.Message),
                        PanelUi("Panel_Colors_Ai_Title"));
                }
            };
            logBtns.Children.Add(copyBtn);

            var saveBtn = new Button
            {
                FontSize = 10, Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(4, 0, 0, 0), Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(ButtonKind.Neutral)
            };
            _panelLocalizationBindings.BindContent(saveBtn, "Panel_Save");
            _panelLocalizationBindings.BindToolTip(
                saveBtn,
                "Panel_Colors_Ai_SaveResponse_ToolTip");
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
            _panelLocalizationBindings.BindAction(
                _aiResponseLog,
                "Colors.AiOutcome",
                RefreshAIResponseOutcome);
            Grid.SetRow(_aiResponseLog, 1);
            grid.Children.Add(_aiResponseLog);

            return grid;
        }
    }
}

