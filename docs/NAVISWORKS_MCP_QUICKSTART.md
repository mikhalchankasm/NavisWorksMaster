# Navisworks MCP Quickstart

## Статус

На текущем этапе подтверждён рабочий сценарий:

- `Claude Code -> MCP stdio server -> Named Pipe -> Navisworks 2027`
- поиск по модели через `ITEM / NAME / CONTAIN`
- выделение найденных элементов
- `hide_unselected`
- `hide_selected`
- `unhide_selected`
- `isolate_selected`
- `show_all`
- `fit_all`
- `zoom_to_selection`
- `focus_on_selection`
- `selected_items_tree`
- `clash_list_tests`
- `clash_list_results`
- `clash_generate_report` как implemented MVP для HTML/JSON clash report artifacts

Это уже не прототип на словах, а рабочий MVP на живой модели.

## Как это устроено

Схема взаимодействия такая:

```text
LLM client (Claude Code / Codex / другой MCP client)
    ->
NavisHelper.McpServer.exe
    ->
local named pipe
    ->
NavisHelper plugin inside Navisworks
    ->
typed Navisworks commands
```

Ключевой принцип:

- LLM не управляет UI напрямую
- LLM не исполняет произвольный код внутри Navisworks
- модель вызывает заранее определённые typed tools
- вся бизнес-логика живёт внутри plugin-layer

## Пользовательские сценарии

MCP-сервер хранит явно подтверждённые сценарии в `%APPDATA%\NavisHelper\Scenarios`. Это пользовательские данные: installer/update/uninstall их не удаляет.

Доступны tools:

- `list_scenarios` — найти сохранённые сценарии и оценить совпадение контекста;
- `get_scenario` — прочитать один сценарий и его SHA-256;
- `save_scenario` — проверить или атомарно сохранить template/exactReplay;
- `delete_scenario` — preview и удаление одного файла с SHA-защитой;
- `resolve_scenario` — получить упорядоченные preview-вызовы существующих MCP tools без автоматического исполнения.
- `scenario_capabilities` — получить полный allowlist, версии контрактов, формат `stepId`, синтаксис параметров и валидный пример.

Для поиска внутри большого RVM-узла сначала вызывайте `find_items` с
`preflight=true`, затем задавайте `scope=current_selection`, `under_handle` или
`under_named_node`. `matchDepth=first` оставляет только самое верхнее совпадение
на каждой ветке, а `countOnly=true` быстро возвращает количество и
`depthHistogram`, не создавая большой match handle. Вызовы без новых аргументов
сохраняют старое поведение `whole_model + all`.

Параметр сценария подставляется только объектом вида
`{"$parameter":"targetModel"}` в значении `steps[].arguments`. Запись
`"{{targetModel}}"` не поддерживается. `stepId` начинается с латинской буквы и
содержит только латинские буквы, цифры и `_`.

Обычный шаблон сначала показывает разрешённый план и требует текущего подтверждения перед apply. Для точного повтора пользователь прямо говорит, например: `полностью повтори сценарий «Отчёт АР-КР»`. Клиент вызывает `resolve_scenario` с `executionIntent=exact_replay`, проверяет строгий контекст и safety-envelope, затем выполняет каждый существующий tool через preview → apply без уточняющих вопросов. При несовместимости процесс останавливается с причиной и не переключается молча в template mode.

Сценарии не хранят `apply/confirm` flags, model handles, instance/document IDs, credentials или MCP transcripts. Schema v2 добавляет `select_by_search` с областями `whole_model`/`direct_children_of`/`descendants_of`, шаблоны имён пар, bbox по текущему выделению, типизированные параметры, белые ссылки `$stepResult`, ограниченный `foreach` и явно проверяемые `reviewedWrites`. Полный allowlist, выходные проекции, грамматика и рабочий пример возвращает `scenario_capabilities`; mutating tools при разрешении сценария принудительно переводятся в preview/dry-run. Старые сценарии schema v1 продолжают читаться без миграции.

Для bbox → clash workflow один параметр типа `filePath` можно подставить одновременно
в `clash_bbox_pair_plan.outputPath` и
`clash_pair_tests_create.planOutputPath`. Первый шаг пишет файл только при
`apply=true`. `onlyWithTotal=0` учитывает только уже запускавшиеся Clash Tests:
тесты без `LastRun` исключаются, чтобы случайно не удалить весь только что созданный
набор до прогона.

## Закрытие Navisworks

`close_navisworks` адресуется конкретному запущенному экземпляру через
`instanceId` или через версию, если экземпляр этой версии только один.
Доступны режимы `prompt` (обычное закрытие с нативным диалогом), `save`
(сначала проверенное сохранение) и `discard` (безвозвратный сброс несохранённых
изменений). По умолчанию возвращается только preview; реальное закрытие требует
одновременно `apply=true` и `confirmClose=true`.

## Как запустить

### 1. Подготовить Navisworks

Нужно:

- запустить `Navisworks 2027`
- открыть нужную модель

Важно:

- для работы MCP панель `NavisHelper` больше не обязательна
- панель можно открывать отдельно, если нужен UI NavisHelper
- текущий рабочий сценарий предполагает, что открыт один экземпляр `Navisworks`

### 2. Подключить MCP client

MCP server:

- packaged per-user install: `%LOCALAPPDATA%\NavisHelper\McpServer-<version>\NavisHelper.McpServer.exe`
- developer build: `NavisHelper.McpServer\bin\Release\net9.0\NavisHelper.McpServer.exe`

Пример `stdio`-конфига:

```json
{
  "mcpServers": {
    "navishelper": {
      "command": "<REPO_ROOT>\\NavisHelper.McpServer\\bin\\Release\\net9.0\\NavisHelper.McpServer.exe"
    }
  }
}
```

### 3. Проверить, что сервер виден

В `Claude Code`:

- открыть `/mcp`
- убедиться, что `navishelper` имеет статус `Connected`

Codex and several other clients read MCP configuration when the process/session starts. If `McpConfigurator` was just run, restart the client or use its MCP reload command before checking tools in a new chat.

## Как это работает при следующем запуске

### Если открыт один Navisworks

Ручная перепривязка не нужна.

Ожидаемый сценарий:

1. открыть `Navisworks`
2. открыть модель
3. открыть `Claude Code` или другой MCP client
4. отправить команду

`McpServer` должен автоматически взять единственный живой экземпляр `Navisworks`.

### Если открыто несколько Navisworks

Это пока не доведено до удобного UX.

Текущая ожидаемая логика:

- если экземпляров несколько, клиент должен получить ошибку уровня `multiple_hosts_detected`
- после этого нужно добавить явный механизм выбора instance

Это следующий этап, но он не блокирует сценарий с одним `Navisworks`.

## Уже готовые tools

### `find_items`

Назначение:

- найти элементы по строке поиска

Текущее фактическое поведение:

- простой режим по умолчанию по-прежнему идёт по сценарию `Category: ITEM`, `Property: NAME`, `Condition: CONTAIN`
- дополнительно теперь поддерживаются операторы `equals`, `not_equals`, `contains`, `wildcard`, `defined`, `not_defined`
- для смешанных условий и нескольких полей добавлен grouped-режим `searches[]` с `combine_operator: all | any`; в user-facing prompts лучше использовать слова `AND / OR`
- для property search использовать display `category` + `property` + `dataType`; `categoryInternal` / `propertyInternal` являются fallback-only и не должны ломать display-поиск
- один вызов `find_items` теперь ограничен ровно одним logical search/query; не склеивать `partNN` в один RPC
- tool всё так же в первую очередь работает как `matched / not_found`, но для слишком широких existence-запросов по `Item/Name` или коротких одиночных `contains/wildcard` теперь может быстро вернуть `query_too_ambiguous`
- на живой модели уже подтверждены `equals`, `not_equals`, `contains`, `wildcard`, grouped `AND` и grouped `OR`

Пример пользовательского запроса:

```text
Use the navishelper MCP tools. Run find_items for exactly one query: 240000-АС17.rvm. Return the raw tool result only. Do not make any model changes.
```

Примеры расширенного вызова:

Если нужно найти два независимых имени, это два отдельных `find_items` вызова. Не объединять их в один payload.

Вызов 1:

```text
Use only the navishelper find_items tool. Run find_items for exactly one query with comparison equals: "240000-ГП4.dwg". Return the raw tool result only.
```

Вызов 2:

```text
Use only the navishelper find_items tool. Run find_items for exactly one query with comparison equals: "240000-АС17.rvm". Return the raw tool result only.
```

```text
Use only the navishelper find_items tool. Run a wildcard search for "240000-*.dwg" in Item/Name and return the raw tool result only.
```

```text
Use only the navishelper find_items tool. Run an advanced grouped search with combine_operator all:
- Item / Name / equals / 240000-ГП4.dwg
- Item / Name / contains / ГП4
Return the raw tool result only.
```

```text
Use only the navishelper find_items tool. Run one advanced grouped search for one logical target with combine_operator "OR" and conditions:
1. category "Item", property "Name", operator "contains", value "ГП4"
2. category "Item", property "Name", operator "contains", value "GP4"
Return the raw tool result only.
```

Примечание по широким запросам:

- для широкого `Item/Name` такие запросы могут соответствовать слишком большой доле модели
- в таком случае текущая реализация возвращает быстрый guarded-ответ `query_too_ambiguous`
- это сделано намеренно, чтобы не уводить Navisworks в долгий full-scan и не ломать UI-сеанс
- одиночные `contains/wildcard` с менее чем 3 буквенно-цифровыми символами также guarded; добавьте более длинное значение или селективное `equals`/`Source File` условие в `AND`
- в grouped `AND` селективные условия (`Source File`/наследуемые свойства и `equals`) выполняются первыми; остальные условия фильтруют уже найденный набор, чтобы не запускать несколько широких `FindAll()` по всей модели
- ручная фильтрация accumulator ограничена примерно 10 с на один search; если якорь всё равно даёт слишком большой набор, tool вернёт `query_too_ambiguous`
- inherited `defined/contains/wildcard` дополнительно ограничен после раскрытия потомков; для `Source File` на больших NWD используйте `equals`, а не `contains "*.rvm"`
- если нужно выгрузить все имена из одного большого `.rvm`, используйте `start_subtree_names_dump` и затем опрашивайте `dump_subtree_names_status`: `find_items` всё равно должен сформировать большой match handle внутри timeout-окна, а синхронный `dump_subtree_names` может быть слишком долгим
- для серийных property/category-поисков запускать `_mcp_remaining_part01.json`, затем `part02` и далее строго по одному `find_items`; для полного перечисления имён `.rvm` использовать dump job ниже, а не part-batching

### Большой дамп имён поддерева

Назначение:

- порционно записать все имена элементов из одного root `.rvm/.dwg` поддерева в файл
- не возвращать большой payload через MCP pipe
- не держать один MCP-запрос несколько минут
- использовать для одноразовых CSV/JSONL дампов вроде `example-model.rvm`
- для быстрого поиска по позициям обычно ставить `includePath=false`; включать `includePath=true` только когда нужен полный путь/контекст

Пример:

```text
Use navishelper MCP tools only. Start a subtree names dump for rootName "example-model.rvm" to "D:\Temp\example-model_names.csv" with format="csv", includePath=false, includeHidden=true, overwrite=true. Then poll dump_subtree_names_status with the returned jobId until state is "done", "failed", or "cancelled". Return each raw tool result.
```

Для маленьких поддеревьев допустим старый синхронный `dump_subtree_names`, но он теперь жёстко ограничен и сам вернёт ошибку с рекомендацией перейти на job workflow, если корень слишком большой. Для больших NWD/RVM по умолчанию выбирать job-пару `start_subtree_names_dump` -> `dump_subtree_names_status`.

### 3a. Прямой smoke-test без GUI-клиента

Для raw regression-проверок можно вообще не использовать `OpenCode/Claude`, а ходить напрямую в host:

- `scripts\navishelper_host_smoke.ps1`

Это полезно, когда нужно:

- проверить точный JSON-ответ host-а
- исключить ошибки конкретной GUI-сессии LLM-клиента
- быстро прогнать smoke после пересборки plugin-а

## С чего продолжать в следующей сессии

Стартовая точка следующей сессии:

1. реализовать и проверить `section_box_selection`
2. затем реализовать и проверить `clear_section_box`

То есть поиск, visibility и navigation сейчас не являются блокером для продолжения.

### `select_items`

Назначение:

- выделить найденные элементы по `matchHandle`

Пример:

```text
Use the navishelper MCP tools. Select the matched items from the previous result and return the raw tool result only.
```

### `selected_items_tree`

Назначение:

- прочитать текущее выделение без изменения selection
- вернуть все выбранные элементы до `maxItems`, по умолчанию 10000
- вернуть либо объединённое дерево родителей (`format=tree`), либо плоский список (`format=flat`)

Пример дерева:

```text
Use only the navishelper selected_items_tree tool. Return the current selection hierarchy with format=tree, maxItems=10000, includeBoundingBoxes=false. Return the raw tool result only.
```

Пример плоской выгрузки:

```text
Use only the navishelper selected_items_tree tool. Return the current selection as format=flat with maxItems=10000. Return the raw tool result only.
```

### `hide_unselected`

Назначение:

- скрыть всё, кроме текущего выбора

Поддерживает два режима:

- preview: `apply=false`
- apply: `apply=true`

Пример preview:

```text
Use the navishelper MCP tools. Preview hide_unselected and return the raw tool result only. Do not apply changes.
```

Пример apply:

```text
Use the navishelper MCP tools. Apply hide_unselected and return the raw tool result only.
```

### `hide_selected`

Назначение:

- скрыть текущий выбор

Текущий статус:

- подтверждён на живой модели

Пример preview:

```text
Используй navishelper MCP tools. Preview hide_selected and return the raw tool result only. Do not apply changes.
```

Пример apply:

```text
Используй navishelper MCP tools. Apply hide_selected and return the raw tool result only.
```

### `unhide_selected`

Назначение:

- снять hidden только с самих выбранных элементов и их внутренних элементов

Текущий статус:

- подтверждён на живой модели для локального сценария, без раскрытия скрытых предков

Пример:

```text
Используй navishelper MCP tools. Apply unhide_selected and return the raw tool result only.
```

### `reveal_selected`

Назначение:

- сделать выбранные элементы реально видимыми, включая снятие hidden с необходимых предков

Текущий статус:

- реализован в MCP и ждёт отдельной проверки на сценарии со скрытым предком

Пример:

```text
Используй navishelper MCP tools. Apply reveal_selected and return the raw tool result only.
```

### `isolate_selected`

Назначение:

- показать всё скрытое и потом скрыть всё кроме текущего выбора

Текущий статус:

- подтверждён на живой модели

Пример preview:

```text
Используй navishelper MCP tools. Preview isolate_selected and return the raw tool result only. Do not apply changes.
```

Пример apply:

```text
Используй navishelper MCP tools. Apply isolate_selected and return the raw tool result only.
```

### `zoom_to_selection`

Назначение:

- зумировать активный вид по bounding box текущего выбора

Текущий статус:

- подтверждён на живой модели

Пример:

```text
Use only the navishelper zoom_to_selection tool. Return the raw tool result only.
```

### `focus_on_selection`

Назначение:

- центрировать активный вид на текущем выборе без bbox-зумирования

Текущий статус:

- подтверждён на живой модели

Практическое замечание:

- это вспомогательная команда центрирования, а не замена `zoom_to_selection`
- она может выставлять более дефолтный угол обзора и не приближает объект так же агрессивно, как bbox-based zoom

Пример:

```text
Use only the navishelper focus_on_selection tool. Return the raw tool result only.
```

### `show_all`

Назначение:

- показать всё ранее скрытое

Пример:

```text
Use the navishelper MCP tools. Run show_all with apply=true and return the raw tool result only.
```

### `create_selection_set`

Назначение:

- создать static selection set из текущего выбора
- или создать static selection set напрямую из `matchHandles`, возвращённых `find_items` / `find_root_items_by_name`, без промежуточного изменения текущего выбора

Пример:

```text
Use the navishelper MCP tools. Create a selection set named MCP_TEST_01 from the current selection and return the raw tool result only.
```

Пример "найди и сохрани":

```text
Use the navishelper MCP tools. Run find_items for the requested objects, then create_selection_set with the returned matchHandles, name "MCP_FOUND_01", folderPath "MCP", apply=false first. Return the raw tool results only.
```

### `fit_all`

Назначение:

- вписать в активный вид всю модель

Текущий статус:

- подтверждён на живой модели

Пример:

```text
Используй navishelper MCP tools. Покажи всю модель и верни только raw tool result.
```

## Подтверждённый рабочий сценарий

На живой модели уже подтверждено:

1. `find_items("240000-АС17.rvm")`
2. `select_items`
3. `hide_unselected(apply=true)`
4. `show_all(apply=true)`
5. `fit_all`
6. `zoom_to_selection`
7. `hide_selected(apply=true)`
8. `isolate_selected(apply=true)` с последующим `zoom_to_selection`
9. `unhide_selected(apply=true)`
10. `focus_on_selection`

Фактически это означает, что базовый сценарий:

- найти
- выделить
- скрыть остальное
- вернуть всё обратно
- изолировать выбранное и потом приблизить к нему

уже работает.

## Практические ограничения

- `matchHandle` сейчас имеет ограниченный срок жизни; если слишком долго ждать между `find_items` и `select_items`, следующий вызов может вернуть `stale`
- в текущем MVP лучше выполнять `find_items -> select_items -> следующую команду` подряд, без длинной паузы

## Политика selection

Для текущих visibility-tools зафиксировано такое поведение:

- `hide_selected(apply=true)` скрывает выбор и потом очищает selection
- `hide_unselected(apply=true)` сохраняет selection
- `unhide_selected(apply=true)` сохраняет selection
- `reveal_selected(apply=true)` сохраняет selection
- `isolate_selected(apply=true)` сохраняет selection
- `show_all(apply=true)` не должен сбрасывать selection

Это сделано специально, чтобы команды навигации вроде `zoom_to_selection` можно было стабильно вызывать после `hide_unselected` и `isolate_selected`, но при этом скрытый объект после `hide_selected` не оставался визуально подсвеченным.

## Что делать дальше

Следующий шаг не в том, чтобы делать generic execution, а в том, чтобы наращивать typed command catalog.

### Приоритетный следующий набор команд

#### Навигация и вид

- `focus_on_selection`
- `zoom_to_match_handles`
- `set_view_orientation`
- `look_at_selection`

#### Видимость и изоляция

- `hide_selected`
- `unhide_selected`
- `isolate_selected`
- `isolate_match_handles`
- `show_only_selection_set`

#### Сечения и обрезка

- `section_box_selection`
- `clear_section_box`
- `enable_sectioning`
- `disable_sectioning`
- `top_view_section`

#### Наборы и точки обзора

- `create_selection_set`
- `create_search_set`
- `save_viewpoint`
- `apply_viewpoint`
- `markup_selection` — ортографическая точка с сохраняемыми `rectangle|target|arrow|hatch`-метками; `arrowCallout=true` добавляет стрелки из поддерживаемых линий, а `autoTopView=false` сохраняет текущий section box
- `live_markers` — живые overlay-метки для QA; следят за 3D-моделью, но не сохраняются в файл

#### Clash-сценарии

- `clash_list_tests`
- `clash_list_results`
- `clash_generate_report`
- `clash_manage_tests` для запуска/сброса/сжатия/переименования/удаления выбранных Clash Detective tests

### `clash_generate_report`

Назначение:

- обработать существующие Clash Detective results
- создать section box вокруг точки каждой коллизии с расстоянием по умолчанию 1500 мм (`boxMode=point`)
- подсветить стороны A/B стандартными цветами NavisHelper
- сделать контекст полупрозрачным, если это включено параметрами
- создать saved viewpoint на каждую коллизию
- попытаться сохранить изображение вида
- записать внешний `report.html`, `manifest.json`, `clash_boxes.json` и папку `images`

Пример dry-run:

```text
Use navishelper MCP tools. Run clash_generate_report with apply=false, testName="", testNames=[], statusFilters=["New","Active"], limit=20. Return the raw tool result only.
```

Пример apply:

```text
Use navishelper MCP tools. Run clash_generate_report with apply=true, testName="Coordination", statusFilters=["New","Active"], limit=50, boxOffsetMm=1500, boxMode="point", contextTransparency=0.5, includeClashPointMarker=true, captureTopViewScreenshots=true. Return reportPath, manifestPath, createdViewpointCount, screenshotCount, returnedStatusCounts, and warnings.
```

Пример отчёта с предварительным запуском только выбранных проверок:

```text
Use navishelper MCP tools. Run clash_generate_report with apply=true, runTests=true, testNames=["001. 240000-ЭК-vs-240136-AC1","002. 240000-ЭК-vs-240101-АТХ4"], includeAllStatuses=true, limit=200, boxOffsetMm=3500, boxMode="point", captureTopViewScreenshots=true. Return reportPath, returnedResultCount, screenshotCount, and warnings.
```

Пример полного отчёта по всем проверкам и всем статусам:

```text
Use navishelper MCP tools. Run clash_generate_report with apply=true, runTests=true, testName="", includeAllStatuses=true, limit=1000, boxOffsetMm=3500, boxMode="point", contextTransparency=0.8, includeClashPointMarker=true, captureTopViewScreenshots=true. Return reportPath, manifestPath, totalResultCount, returnedResultCount, totalStatusCounts, returnedStatusCounts, screenshotCount, and warnings.
```

Практические ограничения:

- по умолчанию команда читает уже существующие results; `runTests=true` требует `apply=true`
- при `runTests=true` и заданном `testName`/`testNames` запускаются только matched tests; без test scope запускаются все tests
- screenshot export best-effort; если Navisworks image exporter недоступен, report всё равно содержит viewpoint names и metadata
- saved viewpoints сохраняют камеру/section state; устойчивые A/B цвета гарантированы в screenshots/report metadata, не как per-viewpoint material overrides
- команда выполняется внутри UI-потока Navisworks; для больших наборов начинать с небольшого `limit`
- для больших наборов обязательно задавать разумный `limit` и проверять `truncated`

### `clash_manage_tests`

Назначение:

- выполнить выбранные Clash Detective tests (`operation=run`);
- сбросить results выбранных tests (`reset`);
- выполнить compact для выбранных tests (`compact`);
- переименовать один test (`rename` + `newName`);
- удалить выбранные tests (`delete`).

Пример dry-run запуска выбранных:

```text
Use navishelper MCP tools. Run clash_manage_tests with apply=false, operation="run", testHandles=["clash-test:1","clash-test:3"]. Return the raw tool result only.
```

Пример apply после проверки dry-run:

```text
Use navishelper MCP tools. Run clash_manage_tests with apply=true, operation="run", testHandles=["clash-test:1","clash-test:3"]. Return affectedTestCount, matchedTestCount, tests, and warnings.
```

## Что делать осторожно

Есть соблазн сделать "динамическое выполнение любой команды без предварительной выверки".

Для production-сценария это плохая идея.

Правильное разделение такое:

- LLM может свободно интерпретировать естественный язык пользователя
- LLM может динамически выбирать комбинацию из уже известных tools
- LLM не должен выполнять произвольный код или произвольные UI-действия внутри Navisworks

То есть допустима динамика на уровне:

- "какие tools вызвать"
- "в каком порядке вызвать"
- "какие параметры подставить"

Но не допустима динамика на уровне:

- `execute_csharp`
- `run_script`
- "нажми что-то в неизвестной форме и посмотри, что выйдет"

## Практическое правило

Новые команды добавляются так:

1. сначала руками проверяется, как пользователь делает это в Navisworks
2. потом находится детерминированный API-способ
3. потом делается typed tool
4. потом проверяется на реальной модели
5. только потом команда считается годной для LLM

Это особенно важно для:

- поворота камеры
- section/clip операций
- создания viewpoint
- clash-переходов

Потому что эти операции легко сделать "вроде бы работающими", но недетерминированными.

## Рекомендованный вектор развития

Для ближайших итераций лучше идти не в "полный свободный агент", а в "расширяемый безопасный command set".

Практически это означает:

- сначала закрыть полный рабочий набор частых действий пользователя
- потом добавить multi-instance workflow
- и только после этого думать о более свободном агентном поведении

## Связанные документы

- [NAVISWORKS_MCP_PLAN.md](NAVISWORKS_MCP_PLAN.md)
- [NAVISWORKS_MCP_COMMAND_CATALOG.md](NAVISWORKS_MCP_COMMAND_CATALOG.md)
- [BUILD_BUNDLE_RULES.md](BUILD_BUNDLE_RULES.md)
