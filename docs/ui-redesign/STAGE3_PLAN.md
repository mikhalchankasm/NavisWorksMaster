# Редизайн UI панели NavisHelper — Этап 3 (доводка: коллизии, иконки, disabled-состояния, контролы)

> План для агента-исполнителя. Реализуется поверх этапа 2 (ветка `ui-redesign-stage2`).
> Перед началом прочитать `CLAUDE.md`, `BUILD_BUNDLE_RULES.md`, `AGENTS.md`, а также
> `docs/ui-redesign/STAGE1_PLAN.md` (там же — процедура слияния/проверки).
> Все номера строк ориентировочные — перед правкой перечитывать актуальный файл.

## Контекст

Этапы 1–2 (влиты/готовы) дали: оболочку из 5 вкладок с сегментами, вкладку «⚙ Настройки»,
переработанные «Коллизии» и **стилевое ядро `NavisHelper/WPF/UiTheme.cs`** с плоскими кнопками
(`enum ButtonKind {Neutral,Primary,Destructive}`, `UiTheme.ButtonStyle(kind)`). Небутонные вкладки
уже на новом стиле. Остались 4 независимых задачи (можно делать по одной, отдельными PR):

1. **Коллизии** — единственная вкладка, чьи кнопки ещё на дефолтном сером WPF-хроме.
2. **Реальные PNG/векторные иконки** — панель целиком на эмодзи, ни один `iconName` не резолвится.
3. **Disabled-по-контексту** — кнопки, требующие выборки, гаснут, когда ничего не выделено.
4. **Лёгкий рестайл** комбобоксов/слайдеров/чекбоксов/текстбоксов под плоскую эстетику.

Рекомендуемый порядок: **1 → 4 → 3 → 2** (1 замыкает редизайн; 4 — быстрые победы через implicit-стили;
3 требует аккуратности; 2 — самый крупный и требует решения по формату иконок).

---

## Задача 1 — Унификация кнопок вкладки «Коллизии»

Ни одна clash-кнопка не задаёт `Style` — все на дефолтном хроме. Перевести на `UiTheme.ButtonStyle(kind)`.

### Фабрики (`NavisHelper/WPF/NavisHelperPanel.Clash.GroupingEngine.cs`)
- `ClashActionButton(string text, string tooltip, Action action, double minWidth = 0)` (**L287**) → добавить
  последним параметром `ButtonKind kind = ButtonKind.Neutral`, применить `btn.Style = UiTheme.ButtonStyle(kind)`.
- `ClashTopBarButton(string text, string tooltip, Action action)` (**L328**) → добавить `ButtonKind kind = Neutral`,
  применить стиль. (Сигнатуры обратно-совместимы — `kind` в конце.)

### Сырые `new Button`
- `_applyClashGroupingButton` «Объединить по уровню» (GroupingEngine.cs **L79-92**) → `Style = ButtonStyle(Primary)`.
  Сохранить `IsEnabled=false` и `ToolTipService.SetShowOnDisabled(...)` (L91) — флэт-шаблон их не ломает.
- `reset` «Сброс группировки» (GroupingEngine.cs **L94-104**) → `ButtonStyle(Destructive)`.
- Overlay-close «✕» (Clash.Shell.cs **L491-499**) → `ButtonStyle(Neutral)`.

### Карта kind (простановка на вызовах ClashTopBarButton/ClashActionButton)
Топ-бар (Shell.cs L71-110): `🔄 Тесты` **Neutral**; `▶ Выбр.`, `⚠ Все`, `✓ Проверка` **Primary**; `🗑 0` **Destructive**.
Группировка: «Объединить по уровню» **Primary**, «Сброс группировки» **Destructive**.
Ряд действий (Shell.cs L446-474): «Сброс» (`ResetClashView`) **Destructive**; всё остальное
(`Section`, `Плоскость`, `Метки`, `Сохр. VP`, `GIF`, `Assigned to`, `BCF`, `Контекст`, `Маркер`) — **Neutral**.
Обоснование: главные позитивные действия (запуск проверок) уже в топ-баре как Primary; дублировать акцент
в плотном ряду утилит не нужно (правило «один Primary на функциональную группу»).

### ToggleButton'ы (важное ограничение)
`_clashTreePanelToggle` «🌲 Дерево», `_clashSettingsToggle` «⚙» (Shell.cs L112-136) и `ClashActionToggle`
«Только пара» (GroupingEngine.cs L314) — это `ToggleButton`, а `UiTheme.ButtonStyle` таргетит `Button` и к ним
**не применится**. Варианты: (а) оставить на дефолтном хроме — приемлемо, т.к. checked-состояние тумблера
дефолт показывает наглядно; (б) добавить в UiTheme `ToggleButtonStyle` (флэт-шаблон с визуалом checked через
триггер `IsChecked`). **Рекомендация: (а) в рамках этой задачи**, (б) — опционально, если нужен полный визуальный паритет.

### Disabled-механизм — не трогать
`SetClashInteractiveControlsEnabled` (GroupingEngine.cs L380-390) переключает `IsEnabled` у зарегистрированных
кнопок; `RegisterClashInteractiveButton` (L360) продолжает работать (фабрика по-прежнему возвращает `Button`).
После перехода на флэт-стиль disabled-вид даёт триггер шаблона (`UiTheme.cs`, `BtnDisabledBg/Text/Border`) —
это плюс: единый вид «занято» вместо системного серого. Ничего в логике busy-гейтинга менять не нужно.

### Проверка задачи 1
Топ-бар: run-кнопки синие, «🗑 0» красная; во время прогона теста они гаснут единым флэт-disabled.
«Объединить по уровню» синяя и корректно disabled без выбранного уровня; «Сброс группировки»/«Сброс» красные.
Контекстные меню/грид-биндинги/GIF/BCF работают как раньше.

---

## Задача 2 — Реальные иконки

Инфраструктура (`Resources.cs`): `MakeButtonContent(iconName, emoji, text)` → `LoadIcon(name)` грузит
`icons/<name>.png` рядом с DLL (`IconsDir`, L43-54), декодит `DecodePixelHeight=20`, при отсутствии → `null` →
эмодзи-фолбэк. Рендер в `Image{Width=20,Height=20}`. Сейчас PNG нет нигде → панель на эмодзи.

### РЕШЕНИЕ ФОРМАТА (нужно выбрать до реализации)
Одна PNG не умеет перекрашиваться под kind: на **Primary** кнопке фон синий, текст белый — тёмная иконка
на синем будет плохо читаться. Варианты:
- **Вариант A (PNG, как ждёт текущая инфраструктура).** Проще всего. Авторить плоские иконки в тёмном
  slate (~#3A3A3A) — отлично читаются на Neutral/Destructive (светлый фон). Для немногих Primary-кнопок
  (Colors By Name, «Применить», run-кнопки коллизий и т.п.) контраст пострадает → митигировать: либо не
  показывать иконку на Primary, либо держать белый вариант иконки и выбирать его в фабрике по `kind` (удваивает
  часть ассетов). Требует: авторинг ~44 PNG + правка `LoadIcon` для HiDPI (см. ниже).
- **Вариант B (векторные иконки, наследующие Foreground) — рекомендуется для «правильной» темы.**
  Заменить растровый путь на геометрию: иконка как `Path`/`DrawingImage` с `Fill`, привязанной к `Foreground`
  кнопки (`{TemplateBinding}`/наследование) → автоматически белая на Primary, тёмная на Neutral, красная на
  Destructive. Убирает проблему перекраски полностью. Требует: расширить `MakeButtonContent` для векторных
  иконок (словарь `Geometry` по имени, либо icon-font), авторинг геометрий/подключение шрифта. Больше
  архитектурной работы, но единый ассет на иконку и идеальная интеграция с темой.

**Рекомендация: Вариант B** (векторные монохромные иконки, наследующие цвет). Если приоритет — минимум усилий и
допустима эмодзи-подобная цветность, брать Вариант A с тёмным slate и без иконок на Primary.

### Полный список имён (44, авторитетно из grep кода — НЕ из ICONS.md, он устарел)
Модель/Навигация: `parent`, `child`, `sibling`, `leaf`, `all_under`, `selection_invert`, `selection_isolate`,
`selection_unhide`, `selection_set_prop`, `selection_search_set`, `copy_names`, `selection_bounds_info`,
`filter`, `selection_save`, `selection_recall`.
Данные: `csv_import`, `import_ps`, `save_hierarchy`, `save_nwd2018`, `export_selected_props`.
Цвета/Ручная: `colors_by_name`, `color_by_property`, `override_pdms`, `reset_color_overrides`,
`match_color_manual`, `match_color_pick`, `match_color_apply`, `export_colors`, `import_colors`,
`history_select`, `history_apply`.
Цвета/AI: `ai_color`.
Виды/Разметка: `markup_viewpoint`, `top_view`, `top_view_bbox`, `top_view_hatch`,
`selection_center_dot_marker`, `selection_hatch_marker`, `selection_bounds_hatch_marker`,
`sort_viewpoints`, `save_viewpionts` (**опечатка в коде** Viewpoints.cs:82 — рекомендуется исправить на
`save_viewpoints` и назвать иконку так же), `selection_section_show`, `selection_section_reset`.
Прочее: `dev_run`. (Вкладка «Отметки» и «Настройки» иконки через этот путь не используют.)

### Развёртывание (для Варианта A)
- Класть PNG в `NavisHelper.bundle/Contents/2026/icons/` (git-tracked). Сборка (`csproj` target
  `CopyBundleArtifacts`, L738-759) копирует папку `icons` в 2024/2025/2027 из 2026; для 2026 используется
  на месте. То есть достаточно наполнить папку 2026 — остальные годы получат копию при сборке.
- Обновить `ICONS.md` в каждой папке под актуальные 44 имени (сейчас документ описывает 25 иконок и другую
  раскладку — стереть и перегенерировать из списка выше).
- Формат по ICONS.md: PNG с альфой, квадрат. **HiDPI:** `LoadIcon` жёстко декодит в 20px (Resources.cs:69) —
  на 150/200% будет мылить. Для чёткости авторить исходники 40×40 и снять/увеличить кап `DecodePixelHeight`
  (например, декодить в исходном разрешении или 40) — иначе больший PNG не поможет.

### Проверка задачи 2
После сборки в `NavisHelper.bundle/Contents/<year>/icons/` лежат PNG (или подключены векторы); кнопки на всех
вкладках показывают иконки вместо эмодзи; на Primary-кнопках иконка читается (белая — для Варианта B);
отсутствие файла всё ещё даёт эмодзи-фолбэк (обратная совместимость).

---

## Задача 3 — Disabled-состояния «по контексту»

Кнопки, которым нужна непустая выборка, гаснут, когда в модели ничего не выделено.

### Ключевой факт: подписки на смену выборки СЕЙЧАС НЕТ
Выборка читается только по клику (`doc.CurrentSelection.SelectedItems`). Событие
`doc.CurrentSelection.Changed` нигде не подписано. Единственная документная подписка —
`Application.ActiveDocumentChanged` (Clash.Lifecycle.cs:48/66). Нужно завести **новую** подписку.

### Реализация
1. Новый partial-файл, напр. `NavisHelper/WPF/NavisHelperPanel.SelectionGating.cs` (добавить `<Compile Include>` в
   `NavisHelper.csproj` — проект non-SDK!). В нём:
   - `List<Button> _selectionRequiredButtons` + `RegisterSelectionRequiredButton(Button)`.
   - `UpdateSelectionDependentButtons()` — **дёшево**: `var n = Application.ActiveDocument?.CurrentSelection?.SelectedItems?.Count ?? 0; bool has = n > 0;` затем `foreach → b.IsEnabled = has;` + `ToolTipService.SetShowOnDisabled(b,true)`.
     **Категорически не** вызывать `CollectModelItems`, пересчёт section-box или что-либо в `BeginInteractiveOperation`
     (CLAUDE.md: селекции >1000 крашили Navisworks; `Invert/Isolate` делают полный обход модели).
   - Подписка: в `OnPanelLoaded` (NavisHelperPanel.cs L475-497) подписаться на `CurrentSelection.Changed` активного
     документа и на `ActiveDocumentChanged` (переподписка при смене документа); в `OnPanelUnloaded` (L499-526) отписаться.
     Хендлер `Changed` → `Dispatcher.BeginInvoke(UpdateSelectionDependentButtons)` (опц. лёгкий дебаунс, т.к. событие
     часто летит при рамочном выделении). Null-guard на отсутствие документа. **Проверить точное имя/сигнатуру события
     `Selection.Changed` по установленному SDK** (в разных версиях API возможны нюансы).
2. Пометка кнопок: у фабрик `Btn/NavBtn/ActionBtn` (Resources.cs) добавить опциональный флаг
   `bool requiresSelection = false` (регистрировать в списке) ИЛИ регистрировать точечно на местах создания.
   Флаг у фабрики чище.

### Что гейтить (handler всё равно бэйлится при пустой выборке — это подтверждение)
Модель: Parent/Child/Sibling/Leaf/All Under (ShellTabs.cs L51-58), Invert (L63), Isolate (L64),
Select by Property (L69), Копировать имена (L78), Габариты (L79), Запомнить (L85).
Цвета: Match Color «Применить» (Colors.cs L1143), История «Применить» (L1235), Color by Property (ShellTabs.cs L25).
Виды: «В выделенные элементы» (Viewpoints.cs L89), Экспорт свойств в Excel (L51).
Отметки: «Отметка Z» (HeightMarks.cs L158), «Размерная линия до Z» (L162), «Graphics-метка» (L166),
«Добавить группу» (HeightMarks.cs L56).
НЕ гейтить (работают без выборки): Unhide All, Вернуть, Сброс section-box, Скрыть Graphics, Сохранить скрин.
Команды палитры (NavisHelperPanel.cs L691-726) вызывают те же хендлеры — они и так бэйлятся; отдельно гейтить не нужно.

### Проверка задачи 3
Пустая выборка → перечисленные кнопки disabled единым флэт-видом с tooltip-on-disabled; выбрал объект → включились;
переключение документа не ломает подписку; никаких лагов при рамочном выделении (хендлер дешёвый).

---

## Задача 4 — Лёгкий рестайл небутонных контролов

Сейчас в `UiTheme` есть только 3 бутонных `Style` (плюс `Segmented` использует `ToolBar.ToggleButtonStyleKey`).
Комбобоксы/слайдеры/чекбоксы/текстбоксы — дефолтный WPF-хром, шаблонов нет.

### Подход: implicit-стили на UserControl.Resources (минимум правок)
Панель — `UserControl`. Задать в `Resources` **implicit-стили без ключа** (только `TargetType`) → применятся ко
**всем** дочерним контролам автоматически, без правки десятков мест создания. В `UiTheme` добавить фабрики стилей
и один метод `InstallImplicitControlStyles(FrameworkElement root)`, вызвать его в конструкторе панели
(NavisHelperPanel.cs, после построения дерева) — кладёт стили в `root.Resources`.

### Объём (по принципу «лёгкий»)
Реализовать высокоценное и низкорисковое, тяжёлые шаблоны — отложить:
- **TextBox** — флэт: `BorderBrush=UiTheme.NeutralBtnBorder`, `CornerRadius`-обёртка/`Padding`, фокус-рамка
  `Accent` через триггер `IsKeyboardFocused`. Дёшево, заметно.
- **CheckBox / RadioButton** — флэт-бокс с акцентной галочкой/точкой (`Accent`) через шаблон. Умеренно, но окупается.
- **ComboBox** — **лёгкий** штрих: единые высота/бордер/фон без полного ретемплейта popup (полный шаблон комбобокса
  тяжёл и рисковый). Полный ретемплейт — опционально/отдельно.
- **Slider** — оставить или минимально тонировать thumb/selection в `Accent`. Полный шаблон трека — отложить.

### Осторожно с implicit-каскадом
Implicit-стиль уходит во ВСЕ потомки, включая контролы внутри DataGrid (`_clashGrid`, комбобоксы редактирования),
командную палитру и т.п. Проверить, что рестайл TextBox/CheckBox не ломает inline-редактирование гридов и фильтры.
Если где-то мешает — тому контролу задать локальный `Style=null` или отдельный ключ. Сложные контролы
(`_schemeListBox`, `MakeColorCombo`-свотчи) при полном ретемплейте комбобокса проверять отдельно — поэтому в рамках
«лёгкого» их не трогаем.

### Проверка задачи 4
Текстбоксы/чекбоксы/радио на всех вкладках в едином плоском виде с акцентным фокусом/галочкой; редактирование ячеек
грида коллизий и фильтры не сломаны; комбобоксы консистентны по высоте/бордеру.

---

## Общая сборка и проверка (все задачи)
- Сборка: `msbuild NavisHelper.sln /p:Configuration=Release2026 /p:Platform=x64` после каждого чекпоинта; перед
  финалом — Release2024/2025/2026/2027 (0 ошибок, бандл разложен по `Contents/<year>/`).
- Автотесты: `dotnet test NavisHelper.McpServer.Tests/NavisHelper.McpServer.Tests.csproj /p:Configuration=Release`.
- Ручной smoke в Navisworks (деплой бандла + **перезапуск** — DLL кэшируется). Пройти проверки из разделов задач.
- csproj — non-SDK: любой новый `.cs` добавить через `<Compile Include>`. Не добавлять `using System.Windows.Forms`
  (конфликт типа `View`). В `UiTheme.cs` имя `Border` занято кистью — тип WPF `Border` через алиас `WpfBorder`.

## Слияние
См. раздел «Передача» в `docs/ui-redesign/STAGE1_PLAN.md` — та же схема (ветки в origin → PR в main). Каждую из
4 задач допустимо мержить отдельным PR; они независимы.
