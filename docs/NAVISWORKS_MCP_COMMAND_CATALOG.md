# Navisworks MCP Command Catalog

## Легенда статусов

- `validated` — реализовано и проверено на живой модели
- `implemented` — реализовано в коде, но ещё не проверено на живой модели после последнего изменения
- `planned` — запланировано, но tool ещё не реализован

## Текущий принцип

Пользователь может писать команду на русском языке в свободной форме.

LLM должен:

- понять намерение пользователя
- выбрать подходящий MCP tool
- подставить параметры
- вернуть структурированный результат

Важно:

- русский текст не обязан совпадать с именем tool
- но tool должен существовать в typed command catalog
- точные входные параметры, дефолты и ключевые поля ответов для Clash Detective MCP tools зафиксированы в [docs/MCP_TOOL_CONTRACTS.md](docs/MCP_TOOL_CONTRACTS.md)

## Версия 2.4.3.0: заметки для MCP-клиентов

- Clash workflows теперь включают не только read-only summaries, но и создание matrix tests, bbox pair planning, создание pair tests, управление существующими tests и генерацию saved viewpoints.
- Для `clash_create_matrix_from_selection` по умолчанию не добавляется служебный `[NH-MATRIX]` prefix, если пользователь явно не просит generated prefix (`useGeneratedPrefix=true`) или свой `namePrefix`.
- Handles clash tests/results возвращаются в стабильном формате (`clash-test:N`, `clash-result:N`) и предпочтительны для повторных операций, если UI names могут совпадать.
- UI-группы clash results сохраняются как реальные `ClashResultGroup`. Пользовательское имя остаётся чистым; техническая сторона группировки хранится suffix ` [NH:A] / [NH:B]`, а legacy names `Clash-A: ...` / `Clash-B: ...` распознаются при загрузке.
- Для saved viewpoints не использовать прозрачность вообще: сохраняются цвета, section box, метки и обязательный `0000 Базовый вид`. Старые `useFullBoxTransparency` / `useRootContextTransparency` в clash viewpoint workflow игнорируются.
- Вкладка NavisHelper `Виды` не является MCP tool, но её section box controls теперь auto-apply после первого показа preview: изменение offset/transparency/checkboxes перерисовывает текущий preview без повторного нажатия основной кнопки.
- После 2.4.1.0 внутренний Clash pipeline разделён на отдельные границы управления tests, matrix-apply, построения кластеров и формирования report/viewpoint DTO; публичные MCP tool names и contracts не изменились.

## Команды по статусам

### Запуск, диагностика и тайминг

| Tool | Статус | Что делает | Русские формулировки |
|---|---|---|---|
| `list_recent_navisworks_files` | `implemented` | Читает последние `.nwd/.nwf/.nwc` из registry текущего пользователя: `HKCU\Software\Autodesk\Navisworks Manage\<version>\Recent File List`. Navisworks может быть закрыт | `покажи последние файлы`, `какой последний navisworks файл`, `найди последние открытые модели` |
| `open_latest_navisworks_file` | `implemented` | Запускает Navisworks Manage с последним существующим файлом из Recent File List и по умолчанию ждёт появления NavisHelper MCP host, возвращая `instanceId` | `запусти navisworks и открой последний файл`, `открой последнюю модель`, `стартуй навис и последний файл` |
| `start_navisworks` | `implemented` | Запускает Navisworks Manage пустым, с явным `filePath`, либо с `openLatestRecentFile=true`; можно задать версию `2024..2027` и ожидание host discovery | `запусти navisworks`, `открой этот nwd`, `запусти navisworks 2027 с файлом` |
| `last_operation_status` | `implemented` | Возвращает host-side итог недавнего `request_id`: `running/completed/failed`, ошибку, elapsed и признак `responseTruncated`. Использовать после `request_timeout`, обрыва pipe или подозрения на ответ >4 МБ | `проверь выполнилась ли команда`, `что с request id`, `команда после таймаута завершилась?` |
| `mcp_task_timer_start` | `implemented` | Опционально запускает общий таймер для пользовательской операции из нескольких MCP tools; отдельные tool calls уже автоматически возвращают `navishelper_timing` | `засеки весь workflow`, `начни общий таймер задачи` |
| `mcp_task_timer_finish` | `implemented` | Завершает общий multi-tool таймер и возвращает `elapsedMs`, `elapsedHuman`, `shouldReportToUser`, `userMessage` | `заверши общий таймер`, `сколько занял workflow` |

### Пользовательские сценарии

| Tool | Статус | Что делает | Русские формулировки |
|---|---|---|---|
| `list_scenarios` | `implemented` | Читает только metadata сохранённых сценариев из `%APPDATA%\NavisHelper\Scenarios`, сортирует до трёх рекомендаций по совпадению версии/root files/project label; ничего не запускает | `покажи мои сценарии`, `есть подходящий сохранённый сценарий` |
| `get_scenario` | `implemented` | Возвращает один сценарий, его SHA-256 и validation warnings без разрешения параметров или запуска шагов | `покажи сценарий`, `что сохранено в сценарии` |
| `save_scenario` | `implemented` | Строго проверяет и атомарно сохраняет template/exactReplay; по умолчанию preview, для записи нужны `apply=true` и `confirmSave=true`, для exactReplay также `confirmExactReplay=true` | `сохрани этот процесс как сценарий`, `запомни это как точный сценарий` |
| `delete_scenario` | `implemented` | Удаляет ровно один сценарий после preview, SHA-проверки и `confirmDelete=true`; папку библиотеки рекурсивно не удаляет | `удали сценарий`, `забудь сохранённый процесс` |
| `resolve_scenario` | `implemented` | Подставляет template-параметры или fixed exactReplay values и возвращает упорядоченные preview-вызовы обычных MCP tools. Сам шаги не выполняет. Exact replay требует прямой текущей команды пользователя и строгого контекста | `используй сценарий`, `полностью повтори сценарий без вопросов` |

Сценарий — это inert template, а не фоновая автоматизация. Он не запускается при старте MCP, открытии модели, по таймеру или изменению файла. Актуальный deny-by-default allowlist, версии контрактов и валидный пример возвращает `scenario_capabilities`; сохранение runtime-хэндлов запрещено.

### Поиск и выбор

Текущее примечание по `find_items`:

- простой режим по умолчанию остаётся выровненным под `ITEM / NAME / CONTAIN`
- теперь дополнительно поддерживаются стандартные операторы `equals`, `not_equals`, `contains`, `wildcard`, `defined`, `not_defined`
- для смешанных условий добавлен grouped-режим `searches[]` с `combine_operator: all | any`; в user-facing prompts лучше использовать слова `AND / OR`
- для property search основной путь: display `category` + `property` + `dataType`; `categoryInternal` / `propertyInternal` считаются fallback-only и не должны подменять UI-имена
- для одного простого поиска используйте scalar `query`; legacy host DTO `queries[0]` остаётся совместимым для низкоуровневых клиентов, но MCP tool schema его больше не экспонирует
- один вызов `find_items` ограничен ровно одним logical search/query; для больших списков вызывать tool последовательно, без склейки `partNN`
- user-facing поведение остаётся практическим: `matched / not_found`; для слишком широких existence-запросов по `Item/Name` и коротких одиночных `contains/wildcard` допустим быстрый guarded-ответ `query_too_ambiguous`
- старые exact/variant/ambiguous ветки не являются основной семантикой текущего поиска
- на живой модели подтверждены `equals`, `not_equals`, `contains`, `wildcard`, grouped `AND` и grouped `OR`

| Tool | Статус | Что делает | Русские формулировки |
|---|---|---|---|
| `find_items` | `validated` | Ищет элементы по scalar `query` или одному grouped-поиску `searches[0]` через `equals / not_equals / contains / wildcard`; для широких `defined / not_defined` и коротких одиночных `contains / wildcard` может быстро вернуть `query_too_ambiguous` вместо тяжёлого full-scan. Не считать это готовым сценарием "по списку кодов": отдельной кодовой семантики пока нет | `найди`, `найди элементы`, `найди по имени`, `найди по маске`, `найди где имя равно`, `найди где имя не равно`, `найди по нескольким условиям` |
| `find_items_by_bbox` | `implemented` | Read-only поиск leaf-элементов по AABB зоне `min/max` в глобальных координатах активного документа. Режимы: `intersects` (default), `contains`, `center`; ответ ограничен safety-limits и возвращает `matchHandle`. Эта v1 не преобразует локальные/сеточные координаты и не меняет selection | `найди в зоне X/Y/Z`, `найди объекты в прямоугольной области`, `найди элементы внутри габарита`, `найди пересекающие заданную зону` |
| `list_item_children` | `implemented` | Возвращает непосредственных детей одного узла модели без full-scan дерева. Быстрые входы: `parentMatchHandle` из `find_items`/`find_root_items_by_name`, либо exact `parentPath` с `comparison=equals`; путь `/100000-XXX1-YY-01` проверяет только model roots и прямых детей root, multi-segment path проходит строго по сегментам. `parentName`/`sourceFile` ищутся только по root/top-level index. Для глубокого неизвестного узла сначала вызвать `find_items`, затем передать один `parentMatchHandle`. Ответ даёт `Children[]` с точными `Path` и общий `childrenMatchHandle` для последующего `select_items`/изоляции/zoom | `покажи непосредственных подчиненных`, `дай детей уровня`, `возьми прямые элементы под узлом`, `что лежит внутри /100000-XXX1-YY-01 одним уровнем` |
| `dump_subtree_names` | `validated` | Синхронно выгружает имена элементов одного небольшого корневого поддерева в `csv` или `jsonl`; жёстко ограничен, для больших корней использовать job tools ниже | `выгрузи имена из корня`, `сделай дамп поддерева`, `экспортируй поддерево в csv` |
| `start_subtree_names_dump` | `validated` | Запускает порционную выгрузку имён поддерева в `.partial` файл и сразу возвращает `jobId`; финальный файл появляется после статуса `done`. В пользовательских формулировках говорить "корневой файл/узел", а не только RVM: на первом уровне часто лежат `.rvm`, но это не контракт | `начни выгрузку имён`, `запусти дамп корневого файла`, `стартуй экспорт имён в csv` |
| `dump_subtree_names_status` | `validated` | Продвигает dump job ограниченным куском и возвращает прогресс: `state`, `itemCount`, `processedItemCount`, `pendingItemCount`, `outputPath` | `проверь статус выгрузки`, `продолжи дамп`, `сколько уже выгружено` |
| `cancel_subtree_names_dump` | `validated` | Отменяет запущенную выгрузку, закрывает writer, очищает очередь обхода и удаляет `.partial` файл | `отмени выгрузку`, `останови дамп`, `прерви экспорт имён` |
| `select_items` | `validated` | Выделяет найденные элементы по `matchHandle` | `выдели найденное`, `выдели эти элементы`, `выдели результат поиска` |
| `selected_items_tree` | `implemented` | Возвращает текущее выделение без изменения selection: дерево общих родителей или flat list с цепочками до root model item; рассчитан на выборки больше 100 элементов | `покажи дерево выбранного`, `выгрузи структуру выбора`, `получи выбранные объекты и родителей`, `экспортируй выделение списком` |

### Видимость

| Tool | Статус | Что делает | Русские формулировки |
|---|---|---|---|
| `hide_unselected` | `validated` | Скрывает всё, кроме текущего выбора | `скрой остальное`, `оставь только выбранное`, `изолируй выбор` |
| `show_all` | `validated` | Показывает всё ранее скрытое | `покажи всё`, `сними скрытие`, `верни всё обратно` |
| `hide_selected` | `validated` | Скрывает текущий выбор | `скрой выбранное`, `спрячь выбранное` |
| `unhide_selected` | `validated` | Снимает hidden только с самих выбранных элементов и их внутренних элементов, без раскрытия скрытых предков | `покажи выбранное`, `сними скрытие с выбранного` |
| `reveal_selected` | `implemented` | Делает выбранные элементы реально видимыми, раскрывая при необходимости скрытых предков | `раскрой выбранное`, `сделай выбранное видимым` |
| `isolate_selected` | `validated` | Показывает всё скрытое и потом скрывает всё кроме текущего выбора | `изолируй выбранное` |

### Наборы

| Tool | Статус | Что делает | Русские формулировки |
|---|---|---|---|
| `list_selection_sets` | `implemented` | Показывает дерево Selection Sets/Search Sets: папки, статические наборы и динамические поисковые наборы, включая `itemId`, path, parentPath, index, childCount, explicit/static count и признаки `hasSearch`; для больших деревьев поддерживает `offset`, `pathPrefix`, `nameContains` | `покажи наборы`, `покажи search sets`, `выведи дерево поисковых наборов` |
| `select_selection_set` | `validated` | Выбирает существующий static/dynamic set по `itemId`, path/name; папка в dry-run больше не разворачивается в элементы без `allowFolderExpansion=true` | `выбери набор`, `примени selection set`, `выдели search set` |
| `create_selection_set` | `validated` | Создаёт обычный static selection set из текущего выбора или из `matchHandles` после поиска, опционально в папку: это статический снимок конкретных элементов, а не поисковое правило | `создай набор`, `сохрани выбор как набор`, `найди и сохрани как набор`, `создай selection set` |
| `create_search_set` | `implemented` | Создаёт dynamic Search Set из условий поиска и сохраняет его в папку; поддерживает `equals`, `contains`, `wildcard`, `defined`, сейчас только `combineOperator=all` для сохраняемого правила. Любое display-условие резолвится по реально существующему свойству: чистые internal names сохраняются нативно, RVM-имена с `U+FFFD` — через runtime ID. Фантомные дубли категорий не создаются | `создай поисковый набор`, `создай search set`, `сохрани поиск как набор` |
| `selection_sets_manage` | `implemented` | Создаёт/удаляет папки, удаляет static/dynamic sets, переименовывает и перемещает элементы дерева Selection Sets. По умолчанию dry-run; для дубликатов использовать `itemId` или `occurrence` | `создай папку наборов`, `удали поисковый набор`, `переименуй набор`, `перемести набор в папку` |
| `selection_sets_reorder` | `implemented` | Сортирует папки и static/dynamic sets natural order, чтобы `1`, `2`, `11` шли по числам; может сортировать всю вложенную структуру рекурсивно. По умолчанию dry-run | `отсортируй наборы`, `пересортируй search sets`, `сделай умную сортировку папок наборов` |

### Навигация и вид

| Tool | Статус | Что делает | Русские формулировки |
|---|---|---|---|
| `zoom_to_selection` | `validated` | Зумирует активный вид по bounding box текущего выбора | `приблизь к текущему`, `приблизь к выбору` |
| `focus_on_selection` | `validated` | Центрирует активный вид на текущем выборе без bbox-зумирования | `сфокусируй на выбранном`, `центрируй на выбранном` |
| `zoom_to_match_handles` | `planned` | Приближает вид к найденным match group; низкий приоритет, потому что текущая явная цепочка `select_items -> zoom_to_selection` уже покрывает основной сценарий | `приблизь к найденному`, `приблизь к результату поиска` |
| `fit_all` | `validated` | Показывает всю модель | `покажи всю модель`, `вписать всё`, `fit all` |
| `look_at_selection` | `planned` | Центрирует камеру на выборе | `посмотри на выбранное`, `центрируй на выборе` |
| `set_view_orientation` | `planned` | Устанавливает заранее заданную ориентацию вида | `вид сверху`, `вид спереди`, `вид слева`, `переключи вид` |

### Сечения и обрезка

| Tool | Статус | Что делает | Русские формулировки |
|---|---|---|---|
| `section_box_selection` | `planned` | Строит section box по текущему выбору | `обрежь по выбранному`, `сделай section box по выбору` |
| `clear_section_box` | `planned` | Убирает section box | `сними обрезку`, `убери section box` |
| `enable_sectioning` | `planned` | Включает sectioning | `включи сечение` |
| `disable_sectioning` | `planned` | Выключает sectioning | `выключи сечение` |
| `top_view_section` | `planned` | Переключает в вид сверху и включает сечение | `вид сверху с сечением` |

### Классификация и окраска модели

| Tool | Статус | Что делает | Русские формулировки |
|---|---|---|---|
| `model_color_scheme` | `implemented` | Анализирует повторяющиеся имена, исходные файлы и значения свойств элементов и их предков по модели/выделению; затем выполняет явную приоритетную схему классификации геометрических листьев. По умолчанию возвращает компактный ответ, а host-side бюджет останавливает слишком большой анализ до MCP timeout с честным флагом неполноты. `sourceFileContains` резолвится также через унаследованное свойство «Файл источника». `operation=apply, apply=false` возвращает dry-run; `apply=true` запрещён для обрезанного scope/свойств/классификации и требует `confirmLargeApply` свыше 25000 элементов. После применения по умолчанию очищает selection-слой, который может маскировать цвета, запрашивает полную перерисовку и проверяет `PermanentColor`/`ActiveColor` на выборке до 100 элементов. `reset` пакетно восстанавливает материалы и сохранённое выделение | `проанализируй модель и предложи цветовую схему`, `найди типовые системы`, `покрась электрику жёлтым`, `покажи план раскраски`, `сбрось эту цветовую схему` |

### Точки обзора

| Tool | Статус | Что делает | Русские формулировки |
|---|---|---|---|
| `list_saved_viewpoints` | `implemented` | Показывает дерево Saved Viewpoints: папки и точки обзора, `itemId`, путь, parentPath, index, childCount и признаки overrides | `покажи точки обзора`, `выведи дерево viewpoints`, `покажи папки точек обзора` |
| `saved_viewpoints_export` | `implemented` | Выгружает полное дерево Saved Viewpoints во внешний `csv`, `json` или `md`; CSV можно открыть в Excel. Полезно перед массовым переименованием, потому что имена могут дублироваться | `выгрузи точки обзора в csv`, `экспортируй viewpoints`, `сделай список точек обзора для Excel` |
| `saved_viewpoints_import` | `implemented` | Импортирует стандартный XML Saved Viewpoints без ручного меню Navisworks: читает `viewfolder`/`view`, создаёт папки и точки обзора в указанной папке, переносит камеру и простые redlines (`rlellipse`, `rlline`). По умолчанию dry-run; `clipplanes`, visibility/material overrides и прочие redline-типы возвращаются как warnings | `импортируй viewpoints из xml`, `загрузи точки обзора из xml`, `добавь xml-точки в папку`, `вставь точку обзора из файла` |
| `saved_viewpoints_manage` | `implemented` | Создаёт/удаляет папки, удаляет одну точку (`delete`) или до 5000 точек одним атомарно спланированным вызовом (`delete_many`), переименовывает и перемещает. По умолчанию dry-run; для дубликатов использовать `itemId` или `occurrence` | `удали точку обзора`, `удали список viewpoints`, `переименуй точку`, `перемести viewpoint` |
| `saved_viewpoints_reorder` | `implemented` | Сортирует папки и точки обзора natural order, чтобы `1`, `2`, `11` шли по числам; может сортировать всю вложенную структуру рекурсивно. По умолчанию dry-run | `отсортируй точки обзора`, `пересортируй viewpoints по номерам`, `сделай умную сортировку папок` |
| `create_viewpoint` | `implemented` | Сохраняет текущий вид как viewpoint, опционально в папку; по умолчанию dry-run | `сохрани точку обзора`, `сохрани viewpoint` |
| `save_document` | `implemented` | Сохраняет активный документ Navisworks по текущему пути. Выполняется сразу, без dry-run | `сохрани модель`, `сохрани файл Navisworks` |
| `save_document_as` | `implemented` | Сохраняет активный документ в абсолютный путь `.nwd` или `.nwf`; существующий файл защищён, пока явно не передан `overwrite=true`. Для передачи модели использовать `.nwd` | `сохрани модель как NWD`, `сохрани как` |
| `selection_sets_build_viewpoints` | `implemented` | Пакетный конструктор: независимые `overview`, `markup`, `sectionBox`-шаги поддерживают `whenItemCountMin/Max`, exact `clusterCount`, overwrite и собственную маркировку. `verbosity=summary` по умолчанию убирает preview-списки. Cap возвращает dropped/uncovered counters и не включает стрелки вопреки `arrowCallout=false` | `построй разные точки для крупных и мелких наборов`, `сделай ровно 5 кластеров` |
| `build_mtr_viewpoints` | `deprecated alias` | Совместимый алиас старого сценария МТР. Сохраняет прежние русские имена «— план»/«— бокс» и дефолты, но новые интеграции должны вызывать `selection_sets_build_viewpoints` | `создай точки МТР по всем наборам` |
| `activate_saved_viewpoint` | `implemented` | Применяет существующий viewpoint по path или unique name; по умолчанию dry-run | `примени точку обзора`, `открой viewpoint` |
| `markup_selection` | `implemented` | Создаёт точки с `rectangle|target|arrow|hatch`; `overwrite=true` обновляет одноимённую точку на месте. Merge использует spatial sweep и safety limits. `clusterCount` задаёт точное число count-кластеров; cap возвращает dropped/uncovered counters и сохраняет явно заданный `arrowCallout` | `пометь выбранное`, `перезапиши markup viewpoint`, `создай ровно 5 планов` |
| `live_markers` | `implemented` | Показывает живые overlay-метки `rectangle|target|arrow` для гибридных групп текущего выделения. Метки следят за 3D-моделью при навигации, но не сохраняются в viewpoints или NWD/NWF. `visible=false, apply=true` отключает режим | `покажи живые метки`, `включи мишени на выделении`, `убери live markers` |
| `section_box_viewpoint` | `implemented` | Создаёт ортографический ISO-вид с включённым clipping box. Поддерживает те же persistent markup/arrow поля; redlines рассчитываются после финальной камеры и бокса и сохраняются вместе с clipping state. Скрытие и изоляция не используются | `создай section box viewpoint`, `сделай бокс с овалами и стрелками`, `создай вид с секущим боксом` |

### Clash

| Tool | Статус | Что делает | Русские формулировки |
|---|---|---|---|
| `clash_list_tests` | `implemented` | Показывает clash tests и counts; поддерживает `namePrefix`, `nameContains`, `offset`, `limit` | `покажи тесты коллизий`, `найди тесты КМ`, `следующая страница тестов` |
| `clash_list_results` | `implemented` | Возвращает адресуемые строки с `resultHandle`, `groupHandle`, `clashPoint`, `distanceMm`, `groupPath`, статусом и исполнителем; ignored-строки скрыты по умолчанию | `покажи коллизии с координатами`, `дай хэндлы clash results` |
| `clash_list_clusters` | `implemented` | Группирует существующие Clash Detective results в read-only кластеры без изменения модели. Default `groupMode=hybrid`: сначала группировка по связанной паре объектов/узлов, затем разделение по расстоянию между точками коллизий. Не требует чистого деления на архитектуру/конструктив/инженерию; discipline/source/level считаются дополнительными метками. Возвращает `clusterId`, raw/cluster counts, `weakAssociation`, уровни ассоциации сторон, centroid/bbox, status counts и bounded preview rows | `сгруппируй коллизии по проблемным зонам`, `покажи связанные объекты в коллизиях`, `схлопни raw clashes в кластеры`, `сгруппируй насосную и трубопроводы` |
| `clash_group_results` | `implemented` | Создаёт реальные `ClashResultGroup`; поддерживает независимую от глубины дерева группировку `groupBy=root/source_file`, честную пагинацию `groupsTruncated/nextGroupOffset` и компактный `aggregateOnly` | `сгруппируй коллизии по корневому файлу`, `создай реальные группы в clash detective` |
| `clash_root_matrix` | `implemented` | Строит постраничную матрицу `{(rootA, rootB): clashCount}` по нативной принадлежности `ModelItem.Model`, со статусами и исключением пар внутри одного файла | `построй межкорневую матрицу`, `дай число коллизий между файлами` |
| `clash_group_custom` | `implemented` | Атомарно создаёт одну реальную группу из явных `resultHandles`; dry-run, проверка принадлежности одному тесту, `overwriteExisting` | `сгруппируй выбранные коллизии`, `создай группу из этих хэндлов` |
| `clash_ungroup` | `implemented` | Расформировывает группы по `groupHandles` или префиксу, возвращая результаты в тест; dry-run по умолчанию | `разгруппируй эти группы`, `распусти группы Зона` |
| `clash_group_by_proximity` | `implemented` | Создаёт реальные группы по spatial/hybrid/object_pair кластеризации; кластеры сортируются по размеру, поддерживаются шаблоны имён и large-scope gate | `сгруппируй по близости 500 мм`, `создай группы по зонам` |
| `clash_set_status` | `implemented` | Каскадно меняет статус results/group/test; поддерживает исполнителя, комментарий, dry-run и подтверждение свыше 500 результатов | `утверди группу`, `пометь тест reviewed`, `назначь исполнителя` |
| `clash_ignore_rules` | `implemented` | Хранит правила в документе, ставит совпадениям Approved с причиной и повторно применяет правила после запуска теста | `добавь постоянное исключение`, `покажи ignore rules` |
| `clash_export_points` | `implemented` | Экспортирует CSV/XLSX с глобальной и локальной СК, этажом и ячейкой; XLSX содержит сводки по этажам и сетке | `выгрузи координаты коллизий`, `сделай xlsx по этажам` |
| `clash_renumber_results` | `implemented` | Нумерует реальные элементы Clash Detective внутри выбранных tests: по умолчанию верхний уровень `top_level`, то есть `ClashResultGroup` и одиночные `ClashResult` так, как они видны в стандартной форме после группировки. Формат задаётся `startNumber`, `numberWidth`, `prefix`, `suffix`, `separator`; старый ведущий номер можно снять через `stripExistingNumber=true`. По умолчанию dry-run; для записи нужны `apply=true` и `confirmRename=true`. Suffix ` [NH:A] / [NH:B]` у групп сохраняется в конце имени | `пронумеруй группы и коллизии`, `назначь номера clash results`, `переименуй все группы 0001`, `сделай нумерацию верхнего уровня clash test` |
| `clash_bbox_pair_plan` | `implemented` | Планировщик пар групп перед Clash Detective: возвращает явные `requestedRootNames`/`matchedRootNames`/`unmatchedRootNames`. Dry-run никогда не пишет файл (`outputWritten=false`); `apply=true` требует абсолютный `outputPath`, пишет через `.partial`, атомарно завершает и проверяет размер/SHA-256 | `найди пересекающиеся группы по bbox`, `построй план clash pairs`, `покажи ненайденные root names` |
| `clash_pair_tests_create` | `implemented` | Root/BBox-oriented создание tests. Стороны разрешаются строго: exact path → unique exact root name → unique exact source file. Ambiguous match не выбирает первый; diagnostics разделены для A/B. Для Selection Set/Search Set использовать `clash_tests_from_sets` или `clash_batchtest_import` | `создай clash tests из bbox пар`, `создай проверки по именам корневых файлов` |
| `clash_tests_from_sets` | `implemented` | Создаёт set × set, root × set и root × root tests через native Selection Sources/точные model roots. `planPath` совместим со старыми pair JSON и принимает `navishelper.clash-test-transfer` v1; переносимый set identity — полный путь, а `itemId` только локальная диагностика | `создай тест набор против набора`, `импортируй JSON план clash tests` |
| `clash_tests_export` | `implemented` | Экспортирует определения выбранных Clash Tests в versioned JSON plan. Preview ничего не пишет; apply выполняет проверенную атомарную запись. Results, viewpoints, comments и история расчёта не экспортируются | `экспортируй определения clash tests`, `подготовь план переноса проверок` |
| `clash_batchtest_import` | `implemented` | Безопасно парсит подтверждённое подмножество `nw-exchange-12.0` batchtest XML с `lcop_selection_set_tree/<полный путь>`, запрещает DTD/XXE и external schema loading, затем использует общий resolver/mutation path. По умолчанию dry-run и rollback-on-error | `импортируй batchtest XML`, `перенеси clash tests из XML` |
| `clash_create_matrix_from_selection` | `implemented` | Создаёт Clash Detective matrix tests из текущего выделения Navisworks или из явно переданных объектов по всему дереву модели (`matrixItemNames`, `matrixNameContains`, `matrixExcludeNameContains`): каждый входной элемент против каждого другого (`i<j`), без self-clash; ancestor/descendant пары пропускаются как self-overlap noise. По умолчанию dry-run; `apply=true` создаёт tests без служебного prefix, если `namePrefix` пустой. Prefix `[NH-MATRIX] yyyyMMdd_HHmmss` добавляется только при `useGeneratedPrefix=true` и пустом `namePrefix`; для своего prefix передать `namePrefix`. Опционально выставляет `toleranceMm` и `testType`, может `runAfterCreate=true` запустить только новые tests. Защита: `maxSelectedItems`, hard cap 10000 пар, `confirmLargeMatrix` для матриц больше 300 пар, `removePreviousGenerated=true` удаляет старые generated tests только при `apply=true` и по effective non-empty prefix; с пустым effective prefix удаление предыдущих generated tests запрещено | `создай clash matrix из выделения`, `создай матрицу по этим именам объектов`, `найди группы по имени и создай проверки`, `запусти clash матрицу с допуском 10 мм` |
| `clash_generate_report` | `implemented` | Создаёт Clash Report workflow по существующим results: section box вокруг clash point/result, подсветка сторон, опциональная прозрачность контекста, redline-маркер, второй скрин сверху, saved viewpoints и внешний `report.html` + `manifest.json` + `clash_boxes.json`. `groupMode=hybrid/object_pair/spatial` добавляет cluster summaries и cluster IDs; `artifactGranularity=cluster` создаёт один общий viewpoint/screenshot set на кластер, сохраняя raw member rows. Безопасная первая версия cluster artifacts требует полный filtered scope в одном вызове, `resultOffset=0` и `append=false`; `artifactGranularity=result` сохраняет прежний batching. `verbosity=compact` сокращает только MCP-ответ, убирая дубли длинных путей и preview rows; файлы отчёта остаются полными. `runTests=true` требует `apply=true`. Scope свыше 10000 требует `confirmLargeReport=true`. По умолчанию dry-run | `сделай clash report`, `создай один скрин на кластер коллизий`, `верни компактный ответ`, `создай viewpoints по проблемным зонам`, `исключи weld из отчета` |
| `clash_save_viewpoints` | `implemented` | Создаёт Saved Viewpoints из существующих Clash Detective results без отчёта и скриншотов. Поддерживает batching (`limit`, `resultOffset`), фильтры статусов, `boxMode`, цвета сторон, redline-маркер точки и `createOppositeViewpoints=true` для двух VP на clash: `(1)` стандартный диагональный верхний ISO и `(2)` противоположный диагональный ISO. Прозрачность для Saved Viewpoints отключена; старые `useFullBoxTransparency` и `useRootContextTransparency` игнорируются. В начале папки всегда создаётся `0000 Базовый вид`. По умолчанию dry-run | `создай viewpoints по clash results`, `сохрани две точки обзора по каждой коллизии`, `создай VP по коллизиям` |
| `clash_manage_tests` | `implemented` | Операции run/reset/compact/rename/`rename_batch`/delete/move/sort/set_settings. `run` сохраняет статусы, исполнителей, комментарии и верхнеуровневые группы по паре элементов+координате. `rename_batch` атомарно валидирует весь список `{testHandle,newName}` до записи | `запусти тесты без потери статусов`, `переименуй пакет тестов` |
| `open_clash_result` | `planned` | Открывает результат коллизии | `открой коллизию`, `покажи clash result` |
| `isolate_clash_result` | `planned` | Изолирует объекты конкретной коллизии | `изолируй коллизию` |
| `create_viewpoint_from_clash` | `planned` | Создаёт viewpoint по clash result | `сохрани viewpoint по коллизии` |
| `export_clash_photo_report` | `planned` | Отдельного alias-tool нет; использовать `clash_generate_report` | `экспортируй фото коллизии`, `сделай отчет по clash с картинкой`, `сфотографируй коллизию с описанием` |

### Трансформации и операции над объектами

| Tool | Статус | Что делает | Русские формулировки |
|---|---|---|---|
| `move_selected` | `planned` | Смещает выбранные объекты | `сдвинь выбранное`, `перемести выбранное` |
| `rotate_selected` | `planned` | Поворачивает выбранные объекты | `поверни выбранное` |
| `reset_transform_selected` | `planned` | Сбрасывает transform для выбранных объектов | `сбрось поворот`, `сбрось трансформацию` |

<!-- BEGIN GENERATED MCP TOOL INDEX -->
## Generated Implemented MCP Tool Index

This section is generated from `NavisHelper.McpServer/Tools/*.cs` by `scripts/check_mcp_command_catalog.py --update`.
It prevents the curated Russian command catalog from drifting behind the actual `[McpServerTool]` surface.

| Tool | C# method | Source | Description |
|---|---|---|---|
| `activate_saved_viewpoint` | `ActivateSavedViewpoint` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Activates an existing saved viewpoint by exact path or unique name from list_saved_viewpoints. Defaults to dry-run and returns the resolved path without changing the view unless apply=true. |
| `active_model_context` | `ActiveModelContext` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Returns a compact read-only context package for the active Navisworks model: host status, root model filenames, saved viewpoint/selection set counts, and recommended MCP workflow. Call this before searching a large model or when the user gives .rvm/.dwg names. |
| `build_mtr_viewpoints` | `BuildMtrViewpoints` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Deprecated compatibility alias for selection_sets_build_viewpoints. Preserves the legacy MTR defaults and fixed markup/sectionBox pair. New callers should use the neutral tool. |
| `cancel_clash_report` | `CancelClashReport` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Requests cooperative cancellation of the active clash_generate_report operation. The current screenshot/viewpoint step may finish, then the report writes partial artifacts and stops before the next clash. |
| `cancel_clash_run` | `CancelClashRun` | `NavisHelper.McpServer/Tools/NavisworksClashSetTools.cs` | Requests cooperative cancellation of an asynchronous clash run. It bypasses host_busy. A currently executing native Navisworks test finishes first; no later test is started. |
| `cancel_subtree_names_dump` | `CancelSubtreeNamesDump` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Cancels a running subtree name dump job on the same instanceId returned by start_subtree_names_dump, closes its writer, clears queued ModelItem references, and removes its .partial file. |
| `capture_current_view` | `CaptureCurrentView` | `NavisHelper.McpServer/Tools/NavisworksClashIsolationTools.cs` | Captures the current Navisworks view exactly as displayed. Use after clash_isolate_result or after manually choosing any camera angle. Defaults to dry-run. |
| `clash_batchtest_import` | `ClashBatchtestImport` | `NavisHelper.McpServer/Tools/NavisworksClashTransferTools.cs` | Imports the supported subset of Autodesk Navisworks nw-exchange-12.0 <batchtest> XML by adapting exact lcop_selection_set_tree/full/path locators into the common versioned transfer plan and existing clash_tests_from_sets mutation path. DTD, external entities, external schema loading, unsupported locators, and oversized input are rejected. Dry-run by default; created tests are never run and old results are never imported. |
| `clash_bbox_pair_plan` | `ClashBboxPairPlan` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Plans candidate Clash Detective group pairs by intersecting bounding boxes from top-level roots or the current arbitrary selection. Does not mutate the document. Dry-run never writes outputPath and returns outputWritten=false plus requested/matched/unmatched rootNames. apply=true requires an absolute outputPath and returns only after verified atomic write. Never creates or runs tests. |
| `clash_create_matrix_from_selection` | `ClashCreateMatrixFromSelection` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Creates Clash Detective matrix tests from the current Navisworks selection or explicit matrix items: every item against every other item (i<j), with no self-clash. Defaults to dry-run and no generated name prefix unless useGeneratedPrefix=true. |
| `clash_export_points` | `ClashExportPoints` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Exports clash points to CSV or XLSX with global/local coordinates, level assignment, grid cells, and XLSX summary sheets. Dry-run by default. |
| `clash_generate_report` | `ClashGenerateReport` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Generates a NavisHelper Clash Report workflow from existing Clash Detective results. Defaults to dry-run; pass apply=true to create section-box viewpoints, screenshots when available, and HTML/JSON artifacts. |
| `clash_group_by_proximity` | `ClashGroupByProximity` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Clusters clash points and writes real ClashResultGroup folders per test. Supports spatial, hybrid, and object_pair modes. Dry-run by default. |
| `clash_group_custom` | `ClashGroupCustom` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Creates or rebuilds one real ClashResultGroup from explicit result handles. All handles are validated before mutation. Dry-run by default. |
| `clash_group_results` | `ClashGroupResults` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Creates real Clash Detective result groups from existing clash results by formula. The groups array is paged and reports plannedGroupCount, returnedGroupCount, groupsTruncated, and nextGroupOffset. Defaults to dry-run. |
| `clash_ignore_rules` | `ClashIgnoreRules` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Lists, adds, or removes document-persistent clash ignore rules. Added rules approve matching results with a reason comment and are re-applied after test runs. Dry-run for add/remove by default. |
| `clash_isolate_result` | `ClashIsolateResult` | `NavisHelper.McpServer/Tools/NavisworksClashIsolationTools.cs` | Previews and optionally isolates one existing Clash Detective result by resultHandle. Can highlight A/B, clip around the clash point or item bounds, hide everything except the pair, choose a preset or custom camera, and optionally capture a screenshot. Defaults to dry-run. |
| `clash_list_clusters` | `ClashListClusters` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Groups existing Clash Detective results into read-only clusters. Default groupMode=hybrid first groups by associated object pair, then splits by clash-point proximity; this does not require reliable discipline/architecture classification. Use to collapse many raw clashes into practical problem zones such as pump building vs pump pipelines. Does not mutate the document. |
| `clash_list_results` | `ClashListResults` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Lists Clash Detective results from all tests or a named test. Read-only. Use after clash_list_tests to inspect statuses, assignees, and clashing item names. |
| `clash_list_tests` | `ClashListTests` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Lists Clash Detective tests in the active Navisworks document and returns per-test clash counts. Read-only. |
| `clash_manage_tests` | `ClashManageTests` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Runs, deletes, renames, reorders, sorts, or edits selected Clash Detective tests by name, handle, prefix, or first-N scope. Defaults to dry-run; pass apply=true for run/reset/compact/rename/delete/move/sort/set_settings. operation=run only executes tests; it does not save the model, create reports, screenshots, or viewpoints. Use clash_list_tests first to get testHandles. |
| `clash_pair_tests_create` | `ClashPairTestsCreate` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Creates root/BBox-oriented Clash Detective tests from bbox candidate pairs. Each side resolves by exact full path, then unique exact root display name, then unique exact source-file identity; ambiguity is never resolved by choosing the first match. Use clash_tests_from_sets or clash_batchtest_import for Selection Set/Search Set sides. Dry-run by default and never runs tests. |
| `clash_renumber_results` | `ClashRenumberResults` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Renumbers Clash Detective groups and/or individual clash results inside selected tests. Defaults to dry-run and top-level scope, so existing groups and ungrouped results are numbered as the user sees them in the standard form. Pass apply=true and confirmRename=true only after reviewing the plan. |
| `clash_report_status` | `ClashReportStatus` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Returns status for the active or last clash_generate_report operation. This can be called while a large report is running. |
| `clash_reset_isolation` | `ClashResetIsolation` | `NavisHelper.McpServer/Tools/NavisworksClashIsolationTools.cs` | Restores the viewpoint, section box, appearance overrides, and temporary visibility changed by clash_isolate_result in the active document. Defaults to dry-run. |
| `clash_root_matrix` | `ClashRootMatrix` | `NavisHelper.McpServer/Tools/NavisworksClashRootMatrixTools.cs` | Builds a paged {(rootA, rootB): clashCount} coordination matrix from existing Clash Detective results. Root identity comes directly from each ModelItem.Model, so it does not depend on tree depth or parsing .rvm names from owner paths. |
| `clash_run_batch` | `ClashRunBatch` | `NavisHelper.McpServer/Tools/NavisworksClashSetTools.cs` | Starts an asynchronous Clash Detective run and returns immediately with operationId. Runs one Navisworks test per UI callback, pauses after batchSize, and keeps clash_run_status/cancel_clash_run available even while a test is calculating. Dry-run by default. |
| `clash_run_resume` | `ClashRunResume` | `NavisHelper.McpServer/Tools/NavisworksClashSetTools.cs` | Continues a paused asynchronous clash run for the next batch. |
| `clash_run_status` | `ClashRunStatus` | `NavisHelper.McpServer/Tools/NavisworksClashSetTools.cs` | Returns progress and per-test outcomes for an asynchronous clash run. Read-only and bypasses host_busy. |
| `clash_save_viewpoints` | `ClashSaveViewpoints` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Creates Saved Viewpoints from existing Clash Detective results only. Defaults to dry-run; pass apply=true to save viewpoints. This does not run tests, generate reports, write files, or capture screenshots. |
| `clash_set_status` | `ClashSetStatus` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Sets status on explicit results, all results in groups, or whole tests. Group/test scopes cascade to individual results. Dry-run by default. |
| `clash_tests_export` | `ClashTestsExport` | `NavisHelper.McpServer/Tools/NavisworksClashTransferTools.cs` | Exports portable Clash Detective test definitions to a versioned NavisHelper JSON transfer plan. Selection Set/Search Set sides use exact full tree paths; model-root sides use rootName/sourceFile. Results, viewpoints, comments, and calculation history are never exported. Dry-run by default and never writes a file unless apply=true. |
| `clash_tests_from_sets` | `ClashTestsFromSets` | `NavisHelper.McpServer/Tools/NavisworksClashSetTools.cs` | Creates one Clash Detective test per Selection Set/Search Set or model-root pair. Set sides use native live Navisworks SelectionSource bindings, so dynamic Search Sets are re-evaluated. Dry-run by default. Inline references accept document-local itemId, exact full path, unique name, rootName, or sourceFile. planPath also accepts navishelper.clash-test-transfer v1; there itemId is diagnostic-only and exact full set path is the portable identity. |
| `clash_ungroup` | `ClashUngroup` | `NavisHelper.McpServer/Tools/NavisworksClashTools.cs` | Ungroups explicit ClashResultGroup handles or groups matching a name prefix within one test. Dry-run by default. |
| `close_navisworks` | `CloseNavisworks` | `NavisHelper.McpServer/Tools/NavisworksStartupTools.cs` | Previews or closes one targeted Navisworks Manage instance. Modes: prompt requests normal exit and may show the native save dialog; save saves first and exits only after a verified save; discard permanently drops unsaved changes before exit. Defaults to preview and requires apply=true plus confirmClose=true. Target by instanceId, or by navisworksVersion only when exactly one matching host is running. |
| `create_search_set` | `CreateSearchSet` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Creates a dynamic Navisworks Search Set and optionally saves it inside a Selection Sets folder. Defaults to dry-run. Conditions use the same schema as find_items; persistable operators are equals, contains, wildcard, and defined. Each condition supports logicalOperator=and\|or, negate, ignoreCase (default true), ignoreDiacritics, and ignoreCharWidth. Navisworks has no parentheses and AND binds more strongly than OR; express (A OR B) AND D as (A AND D) OR (B AND D) by repeating D in each branch. |
| `create_selection_set` | `CreateSelectionSet` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Creates a static Navisworks Selection Set from the current selection or from find_items/find_root_items_by_name match handles, optionally inside a Selection Sets folder. This stores concrete model items, not a dynamic search rule. Defaults to dry-run. |
| `create_viewpoint` | `CreateViewpoint` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Saves the current Navisworks view as a saved viewpoint, optionally inside a folder path. |
| `current_viewpoint_info` | `CurrentViewpointInfo` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Returns read-only information about the current Navisworks viewpoint, including position, rotation, and common viewpoint properties when available. |
| `delete_scenario` | `DeleteScenario` | `NavisHelper.McpServer/Tools/NavisworksScenarioTools.cs` | Deletes exactly one saved NavisHelper scenario. Defaults to preview and uses SHA-256 optimistic concurrency. It never recursively deletes the scenario directory. |
| `dump_subtree_names` | `DumpSubtreeNames` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Synchronously streams item names from one small root .rvm/.dwg subtree to a CSV or JSONL file. Hard-limited to avoid long Navisworks UI hangs; for large roots use start_subtree_names_dump plus dump_subtree_names_status. |
| `dump_subtree_names_status` | `DumpSubtreeNamesStatus` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Advances and returns status for a subtree name dump job. Poll this until state is done/failed/cancelled using the same instanceId returned by start_subtree_names_dump. Each poll processes a bounded chunk on the Navisworks UI thread; keep maxElapsedMs near the 500 ms default when a user is actively working. |
| `find_items` | `FindItems` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Finds Navisworks items by property conditions. Supports whole-model or subtree/current-selection scope, shallowest-match pruning, count-only estimates, and a clarification preflight. Old calls remain whole_model + matchDepth=all. Use preflight=true before ambiguous natural-language searches; for repeated inherited names prefer a narrow scope plus matchDepth=first. |
| `find_items_by_bbox` | `FindItemsByBbox` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Finds leaf model items whose axis-aligned bounding boxes intersect a global document-coordinate zone. Coordinates use the active Navisworks document units; this v1 tool does not transform local/grid coordinates. Read-only: it returns a match handle for select_items or visibility tools and never changes the selection. |
| `find_root_items_by_name` | `FindRootItemsByName` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Fast path for finding top-level/root Navisworks model items by displayed root name or Source File filename. Use this instead of find_items for long lists of appended .rvm/.dwg model file names. It returns the same match handles as find_items, so select_items, isolate_selected, and zoom_to_selection can be used afterward. |
| `fit_all` | `FitAll` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Fits the current Navisworks view to the full model. |
| `focus_on_selection` | `FocusOnSelection` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Centers the current Navisworks view on the current selection without doing a bounding-box zoom. |
| `get_scenario` | `GetScenario` | `NavisHelper.McpServer/Tools/NavisworksScenarioTools.cs` | Reads one saved NavisHelper scenario by scenario_id. This is read-only and does not resolve parameters or execute any step. |
| `hide_selected` | `HideSelected` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Hides the current Navisworks selection. Dry-run returns affected root/source-file scope summaries before applying. |
| `hide_unselected` | `HideUnselected` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Hides every item except the current Navisworks selection. Dry-run returns affected root/source-file scope summaries before applying. |
| `host_status` | `HostStatus` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Returns current Navisworks MCP host status: active document, process id, memory use, model count, and indexed root item count. |
| `isolate_selected` | `IsolateSelected` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Shows all hidden items and then hides everything except the current Navisworks selection. Dry-run returns root/source-file scope summaries for the re-hide portion; review previouslyHiddenItemCount separately before applying. |
| `item_properties_by_handle` | `ItemPropertiesByHandle` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Returns a bounded read-only property preview for items referenced by match handles from find_items or find_root_items_by_name. Use categoryFilters to narrow large property sets. Defaults return up to 5 items per handle and 50 properties per item. |
| `last_operation_status` | `LastOperationStatus` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Returns host-side status for a recent request_id. Use after request_timeout, transport disconnect, or oversized-response suspicion to determine whether the Navisworks-side command eventually completed, failed, or is still running. |
| `list_item_children` | `ListItemChildren` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Lists the immediate children of one Navisworks model item by parentMatchHandle, fast exact parentPath, parentName, or sourceFile. Use this for direct subitems of a level/group such as '/100000-XXX1-YY-01'. It does not full-scan the model tree; for deep unknown nodes first call find_items and pass parentMatchHandle. |
| `list_navisworks_hosts` | `ListNavisworksHosts` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Lists running Navisworks MCP host instances. Use instance_id from this tool when multiple Navisworks windows are open, or navisworks_version when exactly one host of that version is running. |
| `list_recent_navisworks_files` | `ListRecentNavisworksFiles` | `NavisHelper.McpServer/Tools/NavisworksStartupTools.cs` | Lists recent Navisworks Manage model files from the current Windows user's HKCU Recent File List registry entries. Use this before opening the last/previous Navisworks file. Does not require Navisworks to be running. |
| `list_root_items` | `ListRootItems` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Lists top-level/root Navisworks model items and appended model file names visible near the root of the selection tree. Use this before searching when you need the available .rvm/.dwg names. |
| `list_saved_viewpoints` | `ListSavedViewpoints` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Lists saved viewpoints and folders in the active Navisworks document without changing the current view. Returns name, path, type, depth, and child count. |
| `list_scenarios` | `ListScenarios` | `NavisHelper.McpServer/Tools/NavisworksScenarioTools.cs` | Lists user-approved NavisHelper scenarios from the current Windows profile. Returns metadata and bounded context-match suggestions only; it never executes scenario steps. |
| `list_selection_sets` | `ListSelectionSets` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Lists selection/search sets and folders in the active Navisworks document without changing selection. Supports offset paging and path/name filtering. Returns duplicate-safe itemId, path, parentPath, type, index, explicit/static count, and dynamic-search flags. |
| `live_markers` | `LiveMarkers` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Plans or shows runtime-only overlay markers for hybrid groups in the current selection. The markers stay attached while the camera moves but are never stored in saved viewpoints or .nwd/.nwf files. Use markup_selection for persistent deliverables. |
| `markup_selection` | `MarkupSelection` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Creates one or more saved viewpoints with persistent rectangle, target, arrow, or hatch redline marks around hybrid groups in the current selection. Large items receive individual marks; nearby small items are merged. autoTopView=true creates a top view; false preserves the current orthographic or perspective camera and its section box. Defaults to rectangle and dry-run. |
| `mcp_diagnostics` | `McpDiagnostics` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Returns MCP diagnostics: JSONL log file path, discovery instances directory, and currently running Navisworks host records. |
| `mcp_error_contract` | `McpErrorContract` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Returns the NavisHelper MCP error contract: stable error codes, meanings, retryability, and recommended client actions. |
| `mcp_health_check` | `McpHealthCheck` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Runs a read-only MCP/Navisworks health check and returns a verdict instead of throwing on partial failures. Use after long runs, timeouts, or suspected host hangs. |
| `mcp_recent_calls` | `McpRecentCalls` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Returns the last MCP JSONL call log lines. Use after failures or long runs to confirm which tools were invoked, their target Navisworks instance, elapsed time, status, and error code. |
| `mcp_task_timer_finish` | `McpTaskTimerFinish` | `NavisHelper.McpServer/Tools/NavisworksStartupTools.cs` | Finishes a cross-tool MCP task timer and returns elapsedMs, elapsedHuman, shouldReportToUser, and userMessage. If shouldReportToUser=true, include userMessage in the final answer to the user. |
| `mcp_task_timer_start` | `McpTaskTimerStart` | `NavisHelper.McpServer/Tools/NavisworksStartupTools.cs` | Starts an optional cross-tool MCP task timer for a larger user-visible workflow that spans multiple tool calls. Call this at the beginning of a user task only when an explicit end-to-end task timer is useful, then call mcp_task_timer_finish before the final answer. Individual MCP tool calls also return automatic navishelper_timing in their primary JSON result. |
| `model_color_scheme` | `ModelColorScheme` | `NavisHelper.McpServer/Tools/NavisworksModelColorSchemeTools.cs` | Analyzes model naming/property patterns or applies an explicit ordered color-classification scheme. Rules use first-match-wins priority. Mutations require apply=true; reset restores only overrides captured by the active runtime scheme. |
| `open_latest_navisworks_file` | `OpenLatestNavisworksFile` | `NavisHelper.McpServer/Tools/NavisworksStartupTools.cs` | Convenience tool for the user request: 'start Navisworks and open the last file'. It opens the latest existing file from Navisworks Recent File List and waits for the NavisHelper MCP host by default, reporting an early process exit without waiting for the full timeout. |
| `resolve_scenario` | `ResolveScenario` | `NavisHelper.McpServer/Tools/NavisworksScenarioTools.cs` | Resolves a saved scenario into ordered existing MCP tool calls without executing them. For template mode, show the plan and obtain normal apply confirmation. For exactReplay, a direct user replay request is current authorization: follow agent_instruction, run each preview, enforce the saved safety envelope, then apply without follow-up questions; stop on the first mismatch. |
| `reveal_selected` | `RevealSelected` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Makes the current Navisworks selection actually visible by unhiding selected items and any hidden ancestors needed for visibility. Dry-run returns affected root/source-file scope summaries before applying. |
| `save_document` | `SaveDocument` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Saves the active Navisworks document to its current path. This writes model changes immediately and runs on the Navisworks UI thread. |
| `save_document_as` | `SaveDocumentAs` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Saves the active Navisworks document to a specified .nwd or .nwf path. Existing files are protected unless overwrite=true. Use .nwd to produce a self-contained deliverable with geometry. |
| `save_scenario` | `SaveScenario` | `NavisHelper.McpServer/Tools/NavisworksScenarioTools.cs` | Validates and saves a user-approved NavisHelper schema v1/v2 scenario under %APPDATA%\NavisHelper\Scenarios. Defaults to preview. Use {"$parameter":"name"}; schema v2 also supports allowlisted {"$stepResult":"step.output"}, bounded foreach, typed parameters, and per-tool reviewedWrites. stepId must match ^[A-Za-z][A-Za-z0-9_]{0,63}$. Call scenario_capabilities for the full grammar and example. Never store apply/confirm flags, handles, instance/document IDs, credentials, or raw transcripts. |
| `saved_viewpoints_export` | `SavedViewpointsExport` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Exports the full Saved Viewpoints tree to CSV, JSON, or Markdown on the Navisworks host machine. Use before bulk rename/reorder work so duplicate names can be reviewed with current-tree itemId, path, parentPath, and index. |
| `saved_viewpoints_import` | `SavedViewpointsImport` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Imports standard Navisworks Saved Viewpoints XML by parsing view/viewfolder nodes and creating folders/viewpoints in the active document. Defaults to dry-run. Supports camera/folder import and simple rlellipse/rlline redlines; unsupported XML details such as other redline types, clip planes, hide/material overrides are reported as warnings. |
| `saved_viewpoints_manage` | `SavedViewpointsManage` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Creates/deletes/renames/moves Saved Viewpoints folders or viewpoints. delete_many removes up to 5000 explicitly listed viewpoints in one atomic plan. Defaults to dry-run; pass apply=true only after reviewing list_saved_viewpoints/export output. Supports duplicate names via current-tree itemId or occurrence. |
| `saved_viewpoints_reorder` | `SavedViewpointsReorder` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Naturally sorts Saved Viewpoints folders/viewpoints so names containing numbers sort numerically (1, 2, 11). Defaults to dry-run. Can sort one folder or the full tree recursively. |
| `scenario_capabilities` | `ScenarioCapabilities` | `NavisHelper.McpServer/Tools/NavisworksScenarioTools.cs` | Returns supported scenario schema versions, complete tool allowlist, contract versions, reviewed writes, safe output projections, parameter/$stepResult syntax, foreach rules, pair-name grammar, and a complete valid example. Read-only. |
| `section_box_viewpoint` | `SectionBoxViewpoint` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Creates one or more saved viewpoints with an enabled Navisworks section box around the current selection plus context. Optional persistent markup and line-based arrow callouts are calculated after the final ISO camera and clipping box. It never hides or isolates model items. Defaults to dry-run. |
| `select_by_search` | `SelectBySearch` | `NavisHelper.McpServer/Tools/NavisworksScenarioWorkflowTools.cs` | Selects model items by Navisworks property conditions in the whole model, among one parent's direct children, or among all descendants. The search and selection happen in one call, so no runtime match handle is persisted. This changes only the current selection, not the model. |
| `select_items` | `SelectItems` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Selects previously matched Navisworks items by opaque match handles. |
| `select_selection_set` | `SelectSelectionSet` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Selects an existing Navisworks Selection Set/Search Set by itemId, exact path, or unique name from list_selection_sets. Folder dry-runs return metadata without expanding child sets by default; folder apply requires allowFolderExpansion=true because large folders can be slow. |
| `selected_items_ancestry` | `SelectedItemsAncestry` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Returns currently selected Navisworks items with their structured parent chain from model root to each selected item. Use when the user asks for selected objects, their owners, parents, hierarchy, or structure up to the top; the response is suitable for exporting to text or JSON. |
| `selected_items_preview` | `SelectedItemsPreview` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Returns a read-only preview of currently selected top-level Navisworks items: display name, class, path, source file, hidden state, child count, and optional per-item bounding boxes. It does not traverse descendants. |
| `selected_items_tree` | `SelectedItemsTree` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Returns the full current Navisworks selection as either a merged parent tree or a flat list. It reads Application.ActiveDocument.CurrentSelection without changing selection, supports more than 100 selected items, and includes selected counts/truncation flags. |
| `selection_color_by_property` | `SelectionColorByProperty` | `NavisHelper.McpServer/Tools/NavisworksSelectionReportTools.cs` | Auto-colors the current selection with a deterministic palette derived from property values. It does not accept explicit colors. For exact color mappings, source-file/name fragments, a one-color selection, and runtime reset, use model_color_scheme instead. Defaults to dry-run; pass apply=true to write permanent color overrides. |
| `selection_copy_names` | `SelectionCopyNames` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Returns display names for the current Navisworks selection in copy-ready order. Use this when the user asks to copy, list, export, or summarize selected object names. Optional path/source fields help disambiguate repeated names. |
| `selection_distinct_property_values` | `SelectionDistinctPropertyValues` | `NavisHelper.McpServer/Tools/NavisworksSelectionReportTools.cs` | Returns distinct property values in the current Navisworks selection with counts. Read-only helper for reporting and future color_by_property workflows. |
| `selection_export_properties` | `SelectionExportProperties` | `NavisHelper.McpServer/Tools/NavisworksSelectionReportTools.cs` | Exports the current Navisworks selection property report to an explicit CSV or XLSX file path. Defaults to dry-run; pass apply=true to write. |
| `selection_property_report` | `SelectionPropertyReport` | `NavisHelper.McpServer/Tools/NavisworksSelectionReportTools.cs` | Returns a structured property report for the current Navisworks selection. Read-only replacement for UI/Excel property quick reports. |
| `selection_sets_build_viewpoints` | `SelectionSetsBuildViewpoints` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Builds configurable saved-viewpoint steps for every non-empty Search Set or Selection Set below a required folder prefix. Each overview, markup, or sectionBox step has its own label, clustering strategy, and optional persistent markup including arrow callouts. Defaults to dry-run. |
| `selection_sets_manage` | `SelectionSetsManage` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Creates/deletes/renames/moves Selection Sets folders and static/dynamic selection/search sets. Defaults to dry-run; pass apply=true only after reviewing list_selection_sets output. Supports duplicate names via current-tree itemId or occurrence. |
| `selection_sets_reorder` | `SelectionSetsReorder` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Naturally sorts Selection Sets folders and sets so names containing numbers sort numerically (1, 2, 11). Defaults to dry-run. Can sort one folder or the full tree recursively. |
| `selection_status` | `SelectionStatus` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Returns read-only status of the current Navisworks selection: selected item count and optional combined bounding box. Use before visibility or view operations to verify what is selected. |
| `show_all` | `ShowAll` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Shows all currently hidden Navisworks items. Dry-run returns affected root/source-file scope summaries before applying. |
| `start_navisworks` | `StartNavisworks` | `NavisHelper.McpServer/Tools/NavisworksStartupTools.cs` | Starts Navisworks Manage. Pass filePath to open a specific .nwd/.nwf/.nwc, or set openLatestRecentFile=true to open the latest existing file from Navisworks Recent File List. When waiting, distinguishes host_ready, process_exited, and host_timeout; returns instanceId only when the MCP host is ready. |
| `start_subtree_names_dump` | `StartSubtreeNamesDump` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Starts a chunked CSV/JSONL dump job for all item names under one root .rvm/.dwg subtree. This returns quickly with jobId and instanceId and writes to outputPath.partial while running. Poll/cancel using the same instanceId. On success it atomically replaces outputPath; on failed/cancelled it removes the partial file. |
| `unhide_selected` | `UnhideSelected` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Shows the current Navisworks selection if it is hidden. Dry-run returns affected root/source-file scope summaries before applying. |
| `zoom_to_selection` | `ZoomToSelection` | `NavisHelper.McpServer/Tools/NavisworksTools.cs` | Zooms the current Navisworks view to the bounding box of the current selection. |

<!-- END GENERATED MCP TOOL INDEX -->

## Что уже реально подтверждено

На текущий момент на живой модели подтверждено:

1. `find_items`
2. `select_items`
3. `hide_unselected`
4. `show_all`
5. `create_selection_set`
6. `fit_all`
7. `zoom_to_selection`
8. `hide_selected`
9. `isolate_selected`
10. `unhide_selected`
11. `focus_on_selection`
12. search-v2 grouped `AND / OR`
13. `create_search_set`
14. `selection_sets_manage`
15. `selection_sets_reorder`

`selected_items_tree` реализован и покрыт smoke-проверкой, включая сценарий выбора больше 100 элементов; живой статус фиксируется после очередного regression-прогона.

`reveal_selected` реализован, но ещё требует отдельной проверки на сценарии, где выбранный элемент остаётся невидимым из-за скрытого предка.

Это уже позволяет делать рабочий сценарий:

- найти элементы по имени или корневому файлу
- выделить найденное
- скрыть остальное
- вернуть всё обратно
- сохранить выбор в набор
- выгрузить структуру выбранного корневого поддерева в CSV/JSONL
- показать или экспортировать свойства текущего выбора

## Что делать дальше

Рекомендуемый ближайший порядок проверки и расширения с учётом текущих продуктовых приоритетов:

1. довести live-regression сценарии `find_items/find_root_items_by_name -> create_selection_set(matchHandles)` и `create_search_set -> selection_sets_manage/selection_sets_reorder` на тестовой модели
2. расширить `create_search_set`, если понадобится сохранение OR-групп; текущий native persisted режим намеренно ограничен `combineOperator=all`
3. после live-валидации `find_items_by_bbox` спроектировать именованные зоны и локальные/сеточные СК; не смешивать это с неготовой семантикой "по списку кодов"
4. развить property workflow: показать свойства выбранного, экспортировать свойства выбранного в CSV/XLSX, добавить минимальные regression/smoke проверки на экспорт
5. спроектировать Clash photo/zoom report: открыть result, изолировать/приблизить стороны, сделать изображение, приложить описание
6. довести installer/configurator до понятного поддерживаемого release-потока
7. после этого вернуться к `section_box_selection` / `clear_section_box`, если они нужны для clash/photo или пользовательских сценариев

## Практические ограничения

- `matchHandle` сейчас живёт около 10 минут; если между `find_items` и `select_items` проходит больше времени, handle может стать `stale`
- отдельный комбинированный `find_and_select_items` не является обязательным ближайшим шагом: явная цепочка tools понятнее и уже проверяется; к нему стоит вернуться только если TTL/UX реально мешают
- поиска "по списку кодов" как отдельного доменного сценария пока нет; использовать поиск по имени, root filename или property search. Для raw глобальной AABB-зоны доступен `find_items_by_bbox`; именованные зоны и локальные СК проектируются отдельно
- `focus_on_selection` полезен как вспомогательная команда центрирования, но он не заменяет `zoom_to_selection`: может выставлять более дефолтный угол обзора и не приближает к объекту так же агрессивно, как bbox-based zoom
- широкие `defined / not_defined` и одиночные короткие `contains / wildcard` сейчас сознательно не пытаются возвращать гигантский match-set; вместо этого fast-guard возвращает `query_too_ambiguous`
- в grouped `AND` поиск сначала использует селективные якоря (`Source File`/наследуемые свойства и `equals`), а последующие условия применяет как ручные фильтры по accumulator
- ручные accumulator-фильтры имеют runtime budget, а inherited `defined / contains / wildcard` ограничен после раскрытия потомков; для `Source File` на больших сценах предпочтителен `equals`
- нельзя отправлять несколько `_mcp_remaining_partNN.json` одним `find_items`; каждый файл должен быть отдельным RPC
- для raw regression-проверок есть прямой smoke-runner: `scripts\navishelper_host_smoke.ps1`

## Selection policy

Текущее правило для visibility-команд такое:

- `hide_selected` после `apply=true` очищает selection намеренно, чтобы скрытый объект не оставался визуально подсвеченным
- `hide_unselected` после `apply=true` сохраняет selection
- `unhide_selected` после `apply=true` сохраняет selection
- `reveal_selected` после `apply=true` сохраняет selection
- `isolate_selected` после `apply=true` сохраняет selection
- `show_all` не должен сбрасывать selection

Это нужно, чтобы цепочки вида `find -> select -> isolate -> zoom` оставались предсказуемыми.

Dry-run visibility responses also include `affectedRootCount` and up to 20 largest `affectedRootSummaries`. Each row identifies a root by display name/path, carries its `sourceFile` when available, and reports `affectedItemCount`. Review these groups before `apply=true`; `affectedRootSummariesTruncated=true` means more root groups exist than the fixed safety summary cap. This cap is independent from the item `previewLimit`.

## Практическое правило

Команда попадает в каталог только после одного из двух статусов:

- `implemented`
- `validated`

Если команды нет в этом каталоге, значит LLM не должен делать вид, что умеет её надёжно выполнять.
