# Редизайн UI панели NavisHelper — Этап 1 (ядро)

> **Статус: РЕАЛИЗОВАНО** (2026-07-21) в ветке `ui-redesign-stage1`, коммиты `ef1f5c3`…`e0cd64f` и финальный review-fix после синхронизации с `main`.
> Сборка Release2024/2025/2026/2027 зелёная. Smoke в Navisworks НЕ проводился.
> Инструкция по слиянию и проверке — в конце файла, раздел «Передача».

## Контекст

Панель NavisHelper (WPF-in-code, без XAML/стилей, 7 вкладок) плохо работает на HD/Full-HD: вкладка «Коллизии» требует ≈507px минимальной ширины (грид 220 + панель деревьев 280–340), Expander настроек выдавливает таблицы по высоте, деревья A/B не виртуализированы, вложенные GroupBox съедают место. Настройки AI (ключ `OPEN_ROUTER_NW_KEY`) не обнаруживаемы, версия нигде не видна, overflow-меню «⋯» в шапке прячет сервисные функции. Прошлый редизайн (`docs/ui-redesign/`) уже реализован — это второй, более глубокий раунд.

**Утверждено пользователем:** 5 групп вкладок (Модель / Цвета / Виды / Коллизии / ⚙ Настройки), сворачиваемая панель деревьев в Коллизиях, отдельная вкладка «Настройки», поэтапная реализация. Этап 1 = стилевая система + новая оболочка (5 вкладок с сегментами) + Коллизии + Настройки + шапка/версия. Этап 2 (отдельный план) = глубокая переработка кнопок/тултипов/индикации внутри Модель/Цвета/Виды.

## Шаг 0 — обязательный

В рабочем дереве незакоммиченная работа (HeightMarks, ShellTabs, NavisHelperPanel.cs, csproj + новые файлы). **Сначала закоммитить её отдельным коммитом** (спросив у пользователя или просто commit as-is), затем создать ветку `ui-redesign-stage1`.

## Новые файлы (все добавить в `NavisHelper/NavisHelper.csproj` через `<Compile Include>` — проект non-SDK!)

| Файл | Назначение |
|---|---|
| `NavisHelper/WPF/UiTheme.cs` | Статическая тема: frozen-кисти (палитра из существующих цветов: #333, #DDD, #CCC, #D7DEE8, #F0F0F0, #FFF4D6…), константы (FontSmall=11, ControlHeight=24, Pad=6, CornerRadius=4), фабрики: `SectionCard(header, content)` (Border-карточка вместо GroupBox), `Chip(text, ok)` (статус-чип), `ToolButton(text, tooltip, action)` (без фикс. Width, MinWidth=0, Padding 6,0), `HSplitter()/VSplitter()` (с hover-подсветкой), `Segmented(headers, contents, initialIndex, onChanged, out selectSegment)` |
| `NavisHelper/WPF/NavisHelperSettingsTabBuilder.cs` | Самостоятельный builder вкладки «⚙ Настройки»; не расширяет compatibility partial `NavisHelperPanel` |
| `NavisHelper/Core/PanelUiSettings.cs` | Персистентность UI (клон паттерна `Core/ClashSettings.cs`, key=value в `%APPDATA%\NavisHelper\panel_ui.ini`): `ColorsSegment`, `ViewsSegment`, `MainTabIndex` |

Композиция пяти вкладок размещена в существующем root-shell `NavisHelperPanel.cs`. Отдельные partial-файлы `NavisHelperPanel.Shell.cs` и `NavisHelperPanel.SettingsTab.cs` после release-review удалены: structural ratchet сохраняет максимум 18 compiled partial-файлов.

## Сегмент-контрол (в UiTheme)

Grid 2 строки: row0 — горизонтальный ряд `RadioButton` с общим GroupName и `Style = TryFindResource(ToolBar.ToggleButtonStyleKey)` (fallback — обычные RadioButton, если ресурс null в ElementHost); row1 — **все сегменты добавлены сразу**, переключение через `Visibility` (не ре-парентинг: ссылки на поля и состояние скролла сохраняются). `onChanged` пишет в `PanelUiSettings.Save()`. out-делегат `selectSegment` — для программного переключения.

## Изменения по файлам

### `WPF/NavisHelperPanel.cs` (конструктор L309-389)
- Удалить topBar с кнопкой «⋯» (L320-337) и её строку из root Grid → root: TabControl (row0, Star) + статус-бар (row1, Auto).
- L376-382 → 5 вкладок: `CreateModelTab(); CreateColorsTab(); CreateViewsTab(); CreateClashTab(); CreateSettingsTab();`. Восстановление `SelectedIndex` из `PanelUiSettings.MainTabIndex` + сохранение на `SelectionChanged`.
- Удалить `ShowOverflowMenu` (L704-734); `OpenDevScriptsMenu`, `GetModelLogPath`, `OpenFileInShell` — **оставить** (нужны вкладке Настройки).
- `RegisterPaletteCommands` (L586-647): добавить команды «Настройки» (выбор `_settingsTab`), «Открыть лог», «Dev: загрузить DLL», «О программе» — взамен overflow-меню. Существующие ~60 команд ссылаются на методы, не на вкладки — не ломаются (проверено).

### `WPF/NavisHelperPanel.cs` (root-shell composition)
- **Модель** = ScrollViewer + StackPanel: `CreateNavigationContent()` + разделитель + заголовок «Импорт / Экспорт» + `CreateImportExportContent()`.
- **Цвета** = `Segmented(["Ручная","AI"], …)`: сегмент 1 — `CreateToolsContent()` в своём ScrollViewer; сегмент 2 — `CreateAIColorsContent()` (у него свой Grid со star-строкой лога — схему скролла не менять).
- **Виды** = `Segmented(["Разметка","Отметки"], …)`: сегмент 1 — `CreateViewpointsContent()`; сегмент 2 — контент отметок: `var hmTab = CreateHeightMarksTab(); var c = (UIElement)hmTab.Content; hmTab.Content = null;` → в сегмент **без** ScrollViewer. `CreateHeightMarksTab` не редактируется (свежая незакоммиченная работа).

### `WPF/NavisHelperPanel.ShellTabs.cs`
- `CreateToolsTab()`→`CreateToolsContent()`, `CreateNavigationTab()`→`CreateNavigationContent()`, `CreateAIColorsTab()`→`CreateAIColorsContent()` — убрать `WrapInTab`, возвращать контент.
- Из `CreateAIColorsContent` удалить блок «AI модель» (L135-176: `_modelCombo`, `_thinkingCheck`, keyInfo) — переезжает в SettingsTab дословно. Читатели `_modelCombo?/_thinkingCheck?` в Colors.cs L99-100 null-safe — не ломаются.

### `WPF/NavisHelperPanel.Viewpoints.cs`
- `CreateImportExportTab()`→`CreateImportExportContent()`, `CreateViewpointsTab()`→`CreateViewpointsContent()`.

### `WPF/NavisHelperPanel.HeightMarks.cs` (минимум правок!)
- Единственная правка — тело `ShowHeightMarksTab()` (L194-202): `_mainTabControl.SelectedItem = _viewsTab; _selectViewsSegment?.Invoke(1);` (+ прежний EnsureHeightSessionDocument). Сигнатуру сохранить — вызывается из `ShortestDistanceMarker.cs:65` и палитры.

### `WPF/NavisHelperSettingsTabBuilder.cs` (новый отдельный type) — вкладка «⚙ Настройки»
ScrollViewer + StackPanel из `UiTheme.SectionCard`:
1. **AI**: чип статуса ключа («✅ Ключ найден» / «❌ Ключ не найден» по env `OPEN_ROUTER_NW_KEY`) + кнопка «Обновить»; Expander «Как настроить ключ» (openrouter.ai → `setx OPEN_ROUTER_NW_KEY …` → перезапуск Navisworks, кнопка «Копировать команду»); перенесённые `_modelCombo` (из `AIModels.Available`, пишет `AIConfig.Instance.ModelName`+`SaveConfig()`) и `_thinkingCheck`; кнопка «Проверить» — async HTTP-тест API (`Task.Run` + Dispatcher, таймаут 10с, результат зелёным/красным); «Открыть ai_config.json» → `OpenFileInShell`.
2. **Сервис**: «Открыть лог» → `OpenFileInShell(GetModelLogPath())`; «Dev: загрузить DLL» → `OpenDevScriptsMenu()`.
3. **О программе**: `NavisHelper {AppVersion.VersionString}` + Copyright (класс `NavisHelper.AppVersion` из AboutDialog.cs — полное имя, `using System.Windows.Forms` НЕ добавлять); «Подробнее…» → `ExecutePlugin("AboutNavisHelper.CBC")`.
4. Футер — мелкая серая версия.

### Коллизии — `WPF/NavisHelperPanel.Clash.Shell.cs`
**(a) Сворачиваемая панель деревьев A/B/Состав:**
- Топ-бар (L57) → DockPanel: справа два ToggleButton — «Дерево» (показ/скрытие панели группировки) и «⚙» (флайаут настроек); слева прежний ряд кнопок.
- Поля: `_clashSplitterColumn`, `_clashGroupSplitter`, `_clashTreePanelHost`, `_clashGroupPanelVisible`, `_clashGroupPanelSavedWidth`.
- `SetClashGroupPanelVisible(bool)`: hide — запомнить `ActualWidth` в `_clashGroupPanelSavedWidth`, `MinWidth=0`, ширины колонок дерева и сплиттера = 0, `Visibility.Collapsed`; show — восстановить. В конце `SaveClashSettings()`.
- Убрать MinWidth=340 у GroupBox «Дерево A/B» (`GroupingEngine.cs` L104).

**(b) Настройки из Expander → слайд-оверлей внутри вкладки** (не отдельное окно — проблемы owner/фокуса в ElementHost):
- Удалить `_clashSettingsExpander` (L411-422) и row5.
- Блок настроек (Подсветка A/B, Section Box+Габариты, Прозрачность, Точки обзора) → Border-оверлей: `Grid.SetRowSpan(overlay, все строки)`, `HorizontalAlignment=Right, Width≈340` (на узкой панели Stretch), заголовок + «✕», внутри ScrollViewer; GroupBox → `UiTheme.SectionCard`; слайдерам убрать фикс. Width 135/150 → MinWidth=120/Stretch/MaxWidth=260. Открывается toggle-кнопкой «⚙». Состояние транзиентно.
- Блок действий (Вид/Коллизия/Прозрачность — используются часто) остаётся внизу вкладки: один компактный WrapPanel `ClashActionButton` без GroupBox-хрома, группы разделены тонкими вертикальными Border; убрать фиксированные width.

**(c) Компактизация:** `ClashTopBarButton` (`GroupingEngine.cs` L304) — убрать параметр width (сейчас 70/66/58/40/82), `MinWidth=0, Padding=(6,0)`. GroupBox «Коллизии» (L241-247) → caption + тонкий Border (≈20px экономии).

### `WPF/NavisHelperPanel.Clash.GroupingEngine.cs`
- `MakeClashGroupingTree()` (L157-172): включить виртуализацию (`VirtualizingStackPanel.SetIsVirtualizing/SetVirtualizationMode(Recycling)` + ItemsPanel). Узлы строятся вручную, эффект частичный — честно; полный переход на ItemsSource = Этап 2.

### `Core/ClashSettings.cs` + `WPF/NavisHelperPanel.Clash.Settings.cs`
- Новое поле `bool ClashGroupPanelVisible = true` (Save/Load, обратная совместимость ini).
- **Guard в `SaveClashSettings()` (L161-197):** при скрытой панели НЕ читать `_clashGroupColumn.ActualWidth` (будет 0 — затрёт сохранённую ширину), брать `_clashGroupPanelSavedWidth`. `ClashSettingsExpanded` оставить в файле, игнорировать.

### `WPF/NavisHelperPanel.Resources.cs`
- `CreateGroupHeader/CreateSeparator/Btn/NavBtn/ActionBtn` — перевести инлайновые цвета/размеры на константы UiTheme (поведение и сигнатуры не менять).

## Риски
1. Незакоммиченная работа — шаг 0 (коммит до старта). Правка HeightMarks.cs — только 1 метод.
2. Извлекать контент отметок строго через `hmTab.Content = null` до вставки в сегмент (иначе «already the child of another element»).
3. `ToolBar.ToggleButtonStyleKey` может не резолвиться в ElementHost → fallback на обычные RadioButton.
4. 5 заголовков вкладок на 400px могут пойти в 2 строки — приемлемо; при необходимости «⚙ Настройки» → «⚙».
5. csproj локально изменён — вставлять Include точечно, не переформатируя.
6. Ни в один новый файл не добавлять `using System.Windows.Forms` (конфликт `View`).

## Порядок работ (коммиты-чекпоинты)
1. **C0** — коммит текущих локальных изменений, ветка `ui-redesign-stage1`.
2. **C1** — UiTheme.cs + PanelUiSettings.cs + csproj + рефакторинг Resources.cs. Сборка зелёная, поведение прежнее.
3. **C2** — оболочка: переименования Create*Tab→Create*Content, root-shell (5 вкладок + сегменты), конструктор, ShowHeightMarksTab.
4. **C3** — самостоятельный SettingsTab builder, удаление overflow-меню, новые палитра-команды.
5. **C4** — Коллизии: toggle дерева + ClashGroupPanelVisible + guard ширины + виртуализация TreeView.
6. **C5** — Коллизии: флайаут настроек, компактные действия и топ-бар, снятие GroupBox-хрома.
7. **C6** — полировка UiTheme по остатку, сборка всех конфигураций.

## Проверка
- Сборка: `msbuild NavisHelper.sln /p:Configuration=Release2026 /p:Platform=x64` после каждого чекпоинта; перед финалом — Release2024/2025/2026/2027 (бандл разложится по `NavisHelper.bundle/Contents/<v>/`).
- Smoke в Navisworks (пользователь / MCP-инструменты navishelper):
  1. 5 вкладок, «⋯» нет, статус-бар на месте; выбранная вкладка и сегменты переживают перезапуск (`panel_ui.ini`).
  2. Модель: навигация, Invert/Isolate, память выборки, Импорт/Экспорт.
  3. Цвета: сегмент Ручная⇄AI; AI: схема, превью, «Применить AI-окраску», лог.
  4. Виды: Разметка⇄Отметки; палитра «Height Marks» открывает Виды→Отметки; функционал отметок не задет.
  5. Настройки: чип ключа (с/без env-переменной), «Проверить» не вешает UI, смена модели пишется в ai_config.json, лог/Dev/О программе, версия видна.
  6. Коллизии: тесты грузятся/бегут, превью по дабл-клику; «Дерево» скрывает панель — грид во всю ширину; ширина панели восстанавливается после перезапуска (`clash_settings.ini`); «⚙» открывает флайаут, настройки из него влияют на превью; ряд действий работает; контекстные меню/GIF/BCF как раньше.
  7. Узкая панель 400px / высота 768: без горизонтального скролла в Коллизиях при скрытом дереве; Ctrl+Shift+P работает.

## Этап 2 (вне этого плана, следующий заход)
Глубокая переработка содержимого Модель/Цвета/Виды: единые тултипы, цветовая индикация кнопок (опасные/тихие/основные), сокращение подписей, ленивое обновление деревьев A/B при скрытой панели, переход деревьев на ItemsSource-виртуализацию.

---

## Передача: слияние с main и проверка (для выполняющего агента)

### Состояние веток на момент передачи

- `main` — релиз 2.8.4.0 (PR #44) + последующие коммиты.
- `codex/release-2.8.5.0` — подготовка релиза 2.8.5.0 (коммит `863248a`: отметки высот «Высоты Z», версия, installer). Работа владельца, редизайн её не трогал.
- `ui-redesign-stage1` — ответвлена **от** `codex/release-2.8.5.0`; сверху 5 коммитов редизайна (`ef1f5c3`, `0423ae2`, `b997020`, `3202878`, `e0cd64f`).

### Порядок слияния (рекомендуемый — повторяет историю PR #43/#44)

1. Если ветки ещё не в origin: `git push -u origin codex/release-2.8.5.0 ui-redesign-stage1`.
2. PR №1: `codex/release-2.8.5.0` → `main` — релиз 2.8.5.0 отдельно. Merge.
3. Обновить ветку редизайна: `git checkout ui-redesign-stage1 && git fetch origin && git merge origin/main` (после шага 2 конфликтов быть не должно — редизайн построен поверх релизной ветки).
4. PR №2: `ui-redesign-stage1` → `main` — в diff останутся только 5 UI-коммитов. Merge.

Альтернатива одним PR: `ui-redesign-stage1` → `main` сразу — принесёт и релиз 2.8.5.0, и редизайн одним мержем. Допустимо, но история релиза смешается с UI-работой.

### На что смотреть при конфликтах

- `NavisHelper/NavisHelper.csproj` — проект **non-SDK**; при конфликте обязательно сохранить новые `<Compile Include>`: `WPF\UiTheme.cs`, `WPF\NavisHelperSettingsTabBuilder.cs`, `Core\PanelUiSettings.cs`.
- `NavisHelper/WPF/NavisHelperPanel.cs` — конструктор переписан (2 строки root-грида, 5 вкладок, нет overflow-меню «⋯»); при конфликте предпочитать версию ветки редизайна.
- DLL/PDB в `NavisHelper.bundle/Contents/<версия>/` — артефакты, в коммиты не включать.

### Проверка после слияния

1. Автотесты: `dotnet test NavisHelper.McpServer.Tests/NavisHelper.McpServer.Tests.csproj /p:Configuration=Release` (то же гоняет CI).
2. Полная матрица: `msbuild NavisHelper.sln /p:Configuration=Release2024|2025|2026|2027 /p:Platform=x64` — 0 ошибок, DLL разложились по `NavisHelper.bundle/Contents/<версия>/`.
3. Деплой бандла по BUILD_BUNDLE_RULES.md, **перезапуск Navisworks** (DLL кэшируется), ручной smoke по чек-листу раздела «Проверка» выше. Критичные пункты:
   - Коллизии: тумблер «Дерево» скрывает панель A/B, ширина панели переживает перезапуск (`%APPDATA%\NavisHelper\clash_settings.ini`); тумблер «⚙» открывает оверлей настроек, изменения из него влияют на превью; контекстные меню/GIF/BCF работают.
   - Виды → Отметки: функциональность «Высоты Z» из 2.8.5.0 не задета (группы, Graphics-метки, Ctrl+Shift+H); палитра «Height Marks» открывает Виды→Отметки.
   - ⚙ Настройки: чип ключа, «Проверить» не вешает UI, версия отображается.
   - Узкая панель 400px: вкладка Коллизии без горизонтального скролла при скрытом дереве.
4. Частичная автоматизация smoke возможна MCP-инструментами `mcp__navishelper__*` (host_status, clash_list_tests, clash_run_batch и т.п.) — они проверяют работоспособность плагина, но вид панели проверяется только глазами.
