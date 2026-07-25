# План MCP/LLM-управления для Navisworks (v3)

## Оглавление

1. Статус
2. Цель
3. Ключевые решения
4. Отношение к `ClaudeNavisworksMCP`
5. Архитектура
6. Совместимость рантаймов и контракты
7. Transport, discovery и security
8. Threading и lifecycle host-а
9. Поисковая модель
10. Session store и match handles
11. Wire-контракт host <-> MCP
12. MCP tools v1
13. Этапы
14. Критерии успеха MVP
15. Что не входит в MVP
16. Короткий вывод

## Статус

Это третья редакция плана после двух раундов критики.

Текущее практическое состояние реализации поверх этого плана:

- single-instance MCP flow стабилизирован и подтверждён на живой модели
- host стартует без обязательного открытия панели `NavisHelper`
- visibility/navigation пакет подтверждён на живой модели
- search-v2 подтверждён для `equals`, `not_equals`, `contains`, `wildcard`, grouped `AND`, grouped `OR`
- broad `defined / not_defined` для `Item/Name` и короткие одиночные `contains / wildcard` сейчас intentionally guarded через быстрый `query_too_ambiguous`, чтобы не уводить Navisworks в timeout/full-scan
- grouped `AND` сначала выполняет селективный якорь, затем фильтрует accumulator с runtime budget; inherited `defined / contains / wildcard` имеет cap после раскрытия потомков
- `find_items` намеренно принимает только один logical search/query за RPC; батчи `_mcp_remaining_partNN.json` выполняются последовательно, по одному файлу
- для raw regression smoke есть прямой runner: `scripts\navishelper_host_smoke.ps1`

Следующий рабочий шаг для новой сессии:

1. `section_box_selection`
2. `clear_section_box`

В этой версии исправлены главные дыры второй редакции:

- кросс-рантайм ограничения для контрактов;
- недетерминированный `OpenForms[0].Invoke(...)`;
- переоценка security-смысла nonce;
- singleton-discovery для нескольких процессов Navisworks;
- конфликт между нормализацией и `SearchCondition.EqualValue`;
- неполные контракты для 4 из 5 MVP-tools.

И дополнительно закрыты дешёвые контрактные вопросы перед первым PR:

- явный cap для variant-set;
- canonical naming policy для JSON;
- единый `ErrorCodes` catalog;
- framing сообщений в pipe;
- namespace-правило для anchor в logical match.

## Цель

Сделать для `Navisworks` надёжный агентный интерфейс, в котором:

- LLM не кликает по UI;
- MCP не содержит бизнес-логику Navisworks;
- плагин выполняет только заранее определённые доменные команды;
- одна и та же command-layer логика пригодна и для MCP, и для будущего внутреннего UI.

Ключевой сценарий первой версии:

- найти список кодов;
- выделить найденное;
- скрыть остальное;
- при необходимости создать selection set.

## Ключевые решения

### Что оставляем

- `NavisHelper` остаётся основным plugin-layer;
- mainline transport между внешним MCP server и Navisworks host: `Named Pipe`;
- mainline MCP runtime: `C# / .NET 9`;
- UI-панель остаётся клиентом, а не хозяином логики.

### Что не делаем на MVP

- не форкаем вслепую чужой MCP-репозиторий;
- не добавляем `execute_csharp` или другой generic code execution;
- не строим chat UI внутри Navisworks как основной интерфейс;
- не делаем file-IPC основным production transport.

## Отношение к `ClaudeNavisworksMCP`

`ClaudeNavisworksMCP` нужно учитывать, но не копировать автоматически.

Практическое решение:

- использовать его как reference implementation;
- использовать как источник идей по tool catalog;
- использовать как benchmark для проверки, что мы не изобретаем заново очевидные вещи;
- не брать его как жёсткую runtime-зависимость;
- не делать его submodule до отдельной ревизии совместимости.

Причина не в том, что идея плохая, а в том, что нам нужен plugin-layer, привязанный к текущему `NavisHelper`, его bundle-логике и его локальным сервисам.

## Архитектура

Оставляем 3 логических слоя, но на MVP не раздуваем solution.

### 1. Command layer внутри `NavisHelper`

Этот слой знает:

- `Application.ActiveDocument`;
- `ModelItem`;
- `SearchCondition`;
- selection;
- visibility;
- selection sets;
- later: clashes и viewpoints.

Этот слой не знает:

- MCP;
- Codex/Claude/OpenCode;
- prompts;
- transport details.

### 2. Local host внутри `NavisHelper`

Этот слой знает:

- как принять команду снаружи;
- как маршалить её на UI STA-поток;
- как вернуть структурированный ответ;
- как держать session store для найденных match-групп.

### 3. `NavisHelper.McpServer`

Этот слой знает:

- MCP schema;
- tool handlers;
- преобразование tool-call <-> pipe-request;
- выбор целевого Navisworks instance.

Он не знает Navisworks API напрямую.

## Совместимость рантаймов и контракты

Это отдельное ограничение, а не деталь реализации.

### Фактические рантаймы

- `NavisHelper`: `.NET Framework 4.8.1`, `LangVersion 7.3`
- `NavisHelper.McpServer`: `.NET 9`

### Решение на MVP

На MVP контракты остаются в:

- `NavisHelper/Agent/Contracts/`

и могут подключаться в `NavisHelper.McpServer` как linked files.

Но это допустимо только при жёстких правилах.

### Обязательные ограничения для `Contracts/`

В `Contracts/` разрешены только serializer-agnostic POCO DTO:

- `class`, `enum`, `struct` простого вида;
- авто-свойства с `get/set`;
- типы уровня `string`, `bool`, `int`, `long`, `double`, `DateTime`, `Guid`;
- `List<T>`, массивы, `Dictionary<string, string>` только при явной необходимости.

В `Contracts/` запрещены:

- `record`;
- `init`;
- nullable reference annotations;
- `required`;
- `Span<T>`, `Memory<T>`;
- `IAsyncEnumerable<T>`;
- serializer-specific поведение как основа контракта;
- конструкции языка выше C# 7.3.

### Где живёт сериализация

Сериализация не зашивается в DTO.

Каждая сторона сама сериализует wire-format своим инструментом.

### Naming convention

Canonical wire-format использует:

- `snake_case` для JSON-ключей.

Shared DTO в C# сохраняют:

- `PascalCase` для имён свойств.

Это значит:

- имена свойств DTO не считаются wire-контрактом сами по себе;
- каждая сторона обязана на своём boundary маппить `PascalCase <-> snake_case`;
- если выбранный serializer не умеет это надёжно делать политикой, mapping выносится в runtime-specific adapter, а не в общий DTO-слой.

### Когда нужен отдельный `Protocol`-project

Если хотя бы одно условие наступит:

- появится второй внешний адаптер;
- DTO начнут обрастать версионированием;
- linked files станут мешать сборке;
- понадобится общий пакет схем и тестов сериализации,

тогда контракты нужно вынести в отдельную `netstandard2.0`-сборку.

На MVP это пока не обязательно.

## Transport, discovery и security

### Mainline transport

Для production MVP выбираем:

- `Named Pipe`

### Framing сообщений

Wire-format поверх pipe:

- length-prefixed UTF-8 JSON frames.

Правило кадра:

- `4-byte little-endian length`
- затем `N` байт UTF-8 JSON payload.

Мы не полагаемся на:

- newline-delimited JSON;
- `PipeTransmissionMode.Message` как на единственный framing-механизм.

### Threat model MVP

Нужно честно зафиксировать границы.

MVP защищает от:

- межпользовательского доступа;
- случайного подключения не того локального клиента;
- коллизий нескольких Navisworks instances.

MVP не обещает защиты от злонамеренного процесса того же пользователя.

Это важно: same-user hostile process считается out of scope для первой версии.

### Что является реальной защитой

Базовая защита:

- `PipeSecurity` с доступом только для current user SID.

### Что nonce больше не означает

Nonce в discovery-файле не считается security-границей.

Если мы оставим routing token или handshake token, он нужен только для:

- защиты от случайного клиента;
- устаревших discovery-записей;
- диагностики.

Он не должен продаваться как защита от same-user attacker.

### Усиление безопасности позже

Если позже понадобится trust-модель строже MVP, следующая ступень:

- `GetNamedPipeClientProcessId`;
- сверка пути процесса-клиента;
- optional allow-list по launcher/exe;
- optional signature/hash check для стандартизированного клиента.

Это не блокирует MVP, но и не должно быть скрыто.

### Discovery нескольких Navisworks instances

Singleton `instance.json` больше не используется.

Каждый host пишет свой discovery-файл в:

- `%LocalAppData%\NavisHelper\Mcp\instances\<instance_id>.json`

Пример:

```json
{
  "instanceId": "nw-2026-12345-20260417T080000Z",
  "pipeName": "navishelper-mcp-12345",
  "pid": 12345,
  "navisworksVersion": "2026",
  "documentTitle": "model.nwf",
  "startedAtUtc": "2026-04-17T08:00:00Z"
}
```

### Выбор instance в MCP server

Правило:

- если найден один host, он выбирается автоматически;
- если найдено несколько host-ов и целевой не задан конфигом, tool-call возвращает:
  - `multiple_hosts_detected`
  - список доступных instances с `instanceId`, `pid`, `navisworksVersion`, `documentTitle`.

На MVP этого достаточно.

Отдельный admin-tool для выбора instance можно добавить позже.

### Ghost discovery-файлы

Discovery должен быть self-healing.

Правило для `NavisHelper.McpServer`:

- если у записи `instance_id` процесс с `pid` уже не существует, запись считается stale;
- если `pid` существует, но connect к `pipeName` стабильно не удаётся, запись считается stale;
- stale-запись удаляется из `instances/` до следующей попытки выбора host-а.

Host на штатном shutdown удаляет только свой собственный discovery-файл.

## Threading и lifecycle host-а

### Главное правило

Navisworks API вызывается только на главном STA-потоке процесса.

Ни одна команда из pipe thread не работает с `Application.ActiveDocument` напрямую.

### Как маршалим на UI thread

Правильный паттерн для MVP:

- при загрузке plugin-а на UI thread захватывается `SynchronizationContext.Current`;
- host сохраняет этот context как единственную точку маршалинга;
- все команды выполняются через этот захваченный context.

`OpenForms[0].Invoke(...)` не фиксируется как архитектурное правило и не используется как основной dispatcher.

### Fallback

Если при старте plugin-а UI `SynchronizationContext` не удалось захватить, host не стартует и логирует явную причину.

Ошибка для клиента:

- `host_ui_context_unavailable`

### Где захватывается context

На MVP захват делается в plugin bootstrap на UI thread при `OnLoaded()`.

В текущем репозитории естественная точка для этого:

- `RibbonLoader.OnLoaded()`

или другой ранний plugin bootstrap, если появится более подходящая точка инициализации.

### Execution model

1. Pipe listener принимает request на background thread.
2. Request попадает в последовательную очередь.
3. Queue consumer маршалит выполнение через сохранённый `SynchronizationContext`.
4. Команда выполняется на UI STA thread.
5. Ответ сериализуется и возвращается клиенту.

### Lifecycle

Host стартует автоматически при загрузке plugin-а.

Host не зависит от нажатия ribbon-кнопки.

### Модель клиентов на MVP

На MVP host поддерживает:

- один активный MCP client connection одновременно.

Если в момент работы уже есть активное подключение, второе подключение получает:

- `host_busy`

Это сознательное ограничение MVP.

Следствие:

- `match_handle` не нужно делать client-scoped;
- shared state между несколькими параллельными MCP-клиентами на MVP не поддерживается.

Если `ActiveDocument == null`:

- host остаётся жив;
- document-dependent команды отвечают `no_active_document`.

При закрытии Navisworks или выгрузке plugin-а host:

- останавливает listener;
- удаляет свой discovery-файл;
- очищает session store.

## Поисковая модель

### Актуальное состояние кода на текущем этапе

Текущая живая `v1`-реализация поиска уже сознательно упрощена относительно первоначального плана:

- рабочий путь поиска идёт через `ITEM / NAME / CONTAIN`;
- это сделано специально, чтобы поведение совпадало с реальным окном `Find Items` на модели;
- в текущем runtime-потоке это означает практические статусы `matched` и `not_found`;
- ветки `ambiguous` и `query_too_ambiguous` остаются зарезервированными для следующего этапа exact/variant-логики, а не обязательным поведением текущего `contains`-поиска.

Иными словами: архитектурный план ниже описывает расширяемую целевую модель, но текущая validated-реализация `v1` намеренно проще.

### V1 не вводит user-facing `mode`

В `find_items` v1 нет поля `mode`.

V1 решает одну задачу:

- exact code lookup с аккуратной нормализацией.

### Нормализация не заменяет native search

Критичный tradeoff нужно зафиксировать явно:

- `SearchCondition.EqualValue(...)` не знает о кирилло-латинских визуальных парах;
- значит "нормализация" не может магически работать внутри Navisworks search engine.

### Что делаем в v1

Для code-like запросов строим ограниченный набор exact-вариантов ключа:

- исходная форма;
- upper-case форма;
- форма с унифицированным дефисом и разделителями;
- ограниченный набор кирилло-латинских confusable-вариантов.

Важно:

- число вариантов жёстко cap-нуто;
- exact native search запускается по этим вариантам;
- результаты дедуплицируются по item path.

### Явный cap на variant-set

Для одного query допускается не более:

- `16` exact variants.

Варианты генерируются в порядке приоритета:

1. исходная форма;
2. нормализация регистра и разделителей;
3. одношаговые letter-letter confusable substitutions;
4. одношаговые digit-letter confusable substitutions;
5. двухшаговые substitutions только если лимит ещё не исчерпан.

Если до завершения генерации требуется больше 16 вариантов, query не расширяется дальше и получает статус:

- `query_too_ambiguous`

### Конфузабельные пары для MVP

- `А/A`
- `В/B`
- `С/C`
- `Е/E`
- `К/K`
- `М/M`
- `Н/H`
- `О/O`
- `Р/P`
- `Т/T`
- `Х/X`
- `У/Y`

Отдельно, с меньшим приоритетом:

- `0/O/О`
- `1/I/І`

### Порядок поиска v1

1. exact search по internal name для bounded variant-set;
2. exact search по display name для bounded variant-set;
3. grouping raw hits в logical matches;
4. только если это явно потребуется позже:
   - bounded wildcard candidate search;
   - post-filter в памяти по нормализованному ключу;
   - рекурсивный обход `DisplayName` как последний fallback.

На MVP шаги 4+ не являются обязательными.

### Что такое logical match

Logical match должен быть определён операционально.

Правило v1:

- raw hit несёт namespace происхождения:
  - `internal_name`
  - `display_name`
- anchor ищется как ближайший `ancestor-or-self`, у которого совпадение проверяется в том же namespace, из которого пришёл raw hit;
- все raw hits с одинаковым anchor path образуют один logical match.

Следствия:

- несколько `ModelItem` под одним anchor = один `matched` result;
- несколько разных anchor paths = `ambiguous`.

## Session store и match handles

Публичный протокол упрощается.

### Что видит клиент

Клиент видит только:

- `match_handle`

Это непрозрачный токен.

### Что прячется внутри host-а

Host может внутренне учитывать:

- session;
- document generation;
- sequence id;
- reference на найденные items.

Но эти поля не навязываются LLM-клиенту отдельно.

### Жизненный цикл `match_handle`

`match_handle` валиден только пока:

- не сменился документ;
- не выгружен host;
- запись не вытеснена из store.

### Eviction policy MVP

На MVP `MatchSessionStore` обязан иметь:

- TTL: 10 минут;
- hard cap: 100 live handles;
- LRU eviction;
- полную очистку при смене активного документа.

Если handle устарел или вытеснен, команда отвечает:

- `stale_match_reference`

## Wire-контракт host <-> MCP

Это не MCP tool schema, а внутренний контракт между `NavisHelper.McpServer` и host-ом в Navisworks.

### Request envelope

```json
{
  "request_id": "req-001",
  "instance_id": "nw-2026-12345-20260417T080000Z",
  "command": "find_items",
  "timeout_ms": 60000,
  "payload": {}
}
```

### Response envelope

```json
{
  "request_id": "req-001",
  "ok": true,
  "error_code": null,
  "error_message": null,
  "elapsed_ms": 125,
  "payload": {}
}
```

### Cancellation и timeout

- `request_id` обязателен;
- `timeout_ms` задаётся MCP server-ом;
- host имеет собственный hard timeout guard;
- отмена поддерживается best-effort:
  - до входа в очередь;
  - между фазами поиска;
  - перед mutating-командой.

Нельзя обещать мгновенную отмену внутри долгого вызова Navisworks API, если сам API её не поддерживает.

### Duplicate `request_id`

На одной живой client connection:

- повторный `request_id` считается protocol error;
- host отвечает `duplicate_request_id`.

Между разными соединениями dedup не гарантируется.

Следствие:

- retry после transport failure должен использовать новый `request_id`;
- mutating-команды не считаются idempotent по одному только `request_id`.

### Единый error catalog

Авторитетный перечень кодов ошибок живёт в:

- `NavisHelper/Agent/Contracts/ErrorCodes.cs`

Минимальный каталог MVP:

- `no_active_document`
- `host_ui_context_unavailable`
- `multiple_hosts_detected`
- `instance_not_found`
- `host_busy`
- `stale_match_reference`
- `schema_violation`
- `query_too_ambiguous`
- `empty_match_handles`
- `no_selection`
- `selection_set_name_conflict`
- `duplicate_request_id`
- `request_timeout`
- `transport_connect_failed`

## MCP tools v1

### Общие правила

- все mutating-команды возвращают структурированный результат;
- если команда поддерживает preview, поле `apply` обязательно;
- отсутствие `apply` = `schema_violation`;
- ответы содержат только bounded preview, а не полный лист `ModelItem`;
- полный набор найденных items остаётся в host-е за `match_handle`.

### 1. `find_items`

Read-only команда.

#### Вход

Simple query:

```json
{
  "queries": ["240000-АС17"],
  "comparison": "equals",
  "category": "Item",
  "property": "Name",
  "preview_limit": 10
}
```

Advanced grouped search:

```json
{
  "searches": [
    {
      "query": "240000-АС17",
      "combine_operator": "all",
      "conditions": [
        {
          "category": "Item",
          "property": "Name",
          "operator": "equals",
          "value": "240000-АС17"
        }
      ]
    }
  ],
  "preview_limit": 10
}
```

Ограничения:

- один вызов = максимум один logical `query` или один logical `search`;
- `queries`: optional, не больше одной non-whitespace строки;
- `searches`: optional, не больше одного элемента; если `searches` задан, он перекрывает `queries`;
- внутри одного `searches[0]` может быть несколько `conditions` под `combine_operator`, но это один logical search;
- payload с несколькими независимыми именами или несколькими `searches[]` должен быть разбит на отдельные `find_items` RPC;
- `preview_limit`: optional, default `10`, max `20`.

#### Выход

```json
{
  "results": [
    {
      "query": "240000-АС17",
      "status": "matched",
      "matches": [
        {
          "match_handle": "mh_001",
          "item_count": 3,
          "preview": [
            { "display_name": "240000-АС17", "path": "/..." }
          ],
          "preview_truncated": false
        }
      ]
    }
  ],
  "summary": {
    "matched_queries": 1,
    "not_found_queries": 0,
    "ambiguous_queries": 0,
    "query_too_ambiguous_queries": 0,
    "total_items_in_matches": 3
  }
}
```

Правило:

- `status = matched`, если для запроса получен ровно один logical match;
- `status = ambiguous`, если logical matches больше одного;
- even for `ambiguous` каждая опция получает свой `match_handle`.

### 2. `select_items`

Mutating команда без preview-режима.

#### Вход

```json
{
  "match_handles": ["mh_001", "mh_002"]
}
```

#### Выход

```json
{
  "partial": false,
  "results": [
    {
      "match_handle": "mh_001",
      "status": "selected",
      "selected_item_count": 3
    },
    {
      "match_handle": "mh_002",
      "status": "selected",
      "selected_item_count": 5
    }
  ],
  "selected_handle_count": 2,
  "selected_item_count": 8
}
```

Если часть handle-ов устарела, ответ остаётся `ok`, но:

- `partial = true`
- per-handle `status` показывает:
  - `selected`
  - `stale`

Ошибки:

- `stale_match_reference`
- `no_active_document`
- `empty_match_handles`

### 3. `hide_unselected`

Работает от текущего selection state в Navisworks.

#### Вход

```json
{
  "apply": false
}
```

#### Выход

```json
{
  "apply": false,
  "selected_item_count": 8,
  "would_hide_item_count": 15420,
  "would_keep_visible_item_count": 8
}
```

При `apply: true`:

- действие реально применяется;
- поле `would_hide_item_count` заменяется на `hidden_item_count`.

Ошибки:

- `schema_violation`
- `no_active_document`
- `no_selection`

### 4. `show_all`

Восстанавливает видимость ранее скрытого.

#### Вход

```json
{
  "apply": false
}
```

#### Выход

```json
{
  "apply": false,
  "currently_hidden_item_count": 15420,
  "would_reveal_item_count": 15420
}
```

При `apply: true`:

- действие реально применяется;
- поле `would_reveal_item_count` заменяется на `revealed_item_count`.

Ошибки:

- `schema_violation`
- `no_active_document`

### 5. `create_selection_set`

V1 создаёт set из текущего selection state.

Политика конфликта имени в v1:

- только `fail`

`overwrite` или `replace` в MVP не поддерживаются.

#### Вход

```json
{
  "name": "LLM: AS block",
  "apply": false
}
```

#### Выход

```json
{
  "apply": false,
  "name": "LLM: AS block",
  "selected_item_count": 8,
  "name_conflict": false
}
```

При `apply: true`:

```json
{
  "apply": true,
  "name": "LLM: AS block",
  "selected_item_count": 8,
  "created": true
}
```

Ошибки:

- `schema_violation`
- `no_active_document`
- `no_selection`
- `selection_set_name_conflict`

## Этапы

### Предшаг: reuse audit

Это больше не отдельный этап roadmap.

Это короткая входная ревизия перед кодом:

- что заимствуем из `ClaudeNavisworksMCP`;
- что заимствуем из локальных reference-репозиториев;
- что сознательно не переносим.

Артефакт:

- одна таблица `берём / не берём / почему`.

### Этап 1. Bootstrap и Contracts

Сделать:

- `Agent/Contracts/` с зафиксированным C# 7.3-compatible подмножеством;
- request/response envelopes;
- error code catalog;
- `instance_id` discovery model.

### Этап 2. `SearchService` и grouping rules

Сделать:

- variant generation для code-like queries;
- exact search по internal/display name;
- grouping raw hits в logical matches;
- bounded preview generation.

### Этап 3. `MatchSessionStore`

Сделать:

- opaque `match_handle`;
- TTL + LRU + hard cap;
- invalidation на document change;
- stale handle diagnostics.

### Этап 4. `AgentHost` внутри `NavisHelper`

Сделать:

- startup при plugin load;
- `SynchronizationContext` capture;
- sequential command queue;
- pipe listener;
- multi-instance discovery file;
- graceful shutdown.

### Этап 5. `NavisHelper.McpServer`

Сделать:

- MCP tool schemas;
- host discovery;
- target instance selection;
- timeout propagation;
- structured error mapping.

### Этап 6. Диагностика

Сделать:

- correlation через `request_id`;
- host log;
- MCP server log;
- понятные ошибки:
  - `no_active_document`
  - `multiple_hosts_detected`
  - `host_ui_context_unavailable`
  - `stale_match_reference`
  - `schema_violation`

## Критерии успеха MVP

1. При одном запущенном Navisworks host обнаруживается автоматически без ручного шага.
2. При нескольких Navisworks processes MCP server не теряет instances и возвращает понятную ошибку выбора, если target не задан.
3. Все вызовы Navisworks API выполняются через заранее захваченный UI `SynchronizationContext`.
4. `find_items` возвращает только bounded preview и opaque `match_handle`, а не полный лист элементов.
5. `select_items` работает только через валидные `match_handle`.
6. `MatchSessionStore` очищается при смене документа и не растёт бесконечно.
7. `hide_unselected`, `show_all` и `create_selection_set` требуют явный `apply`.
8. `find_items` корректно находит хотя бы один реальный кейс с кириллическим `АС`, введённым латиницей `AC`.
9. Контракты host <-> MCP собираются и работают в обоих рантаймах без использования конструкций новее C# 7.3 в shared DTO.
10. Variant expansion не превышает 16 вариантов на query и детерминированно отвечает `query_too_ambiguous`, а не разрастается экспоненциально.

## Что не входит в MVP

- свободный чат внутри Navisworks;
- generic code execution;
- full parity с `ClaudeNavisworksMCP`;
- clash/viewpoint/section/export automation всем набором;
- strong security против hostile same-user process;
- приоритизация коротких mutating-команд поверх долгих read-only запросов в общей очереди.

## Короткий вывод

Правильная стратегия сейчас:

- учитывать `ClaudeNavisworksMCP`, но не копировать его механически;
- оставить два runtime-проекта на MVP;
- зафиксировать честную threat model;
- убрать недетерминированные UI-thread паттерны;
- упростить публичный протокол до opaque `match_handle`;
- полностью закрыть контракты всех 5 MVP-tools до начала кода.

Если этот v3-план принимается, первый практический шаг уже можно начинать с кода:

1. `Agent/Contracts/` с ограничениями C# 7.3;
2. `SearchService` с bounded exact-variant lookup;
3. `MatchSessionStore`;
4. `AgentHost` с `SynchronizationContext` и multi-instance discovery;
5. `NavisHelper.McpServer`.
