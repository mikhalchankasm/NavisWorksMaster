# AI Color Objects — техническая сводка

Активный путь состоит из отдельных типов:

- `AIColorObjects.cs` — тонкая точка входа Navisworks;
- `AI/AIColorOperationCoordinator.cs` — single-flight, timeout, отмена,
  document guard и возврат на UI dispatcher;
- `AI/AIColorWorkflow.cs` — UI snapshot, worker-стадия и применение;
- `AI/AIColorPanelState.cs` — семантическое локализуемое состояние результата;
- `AI/OpenRouterCatalogCache.cs` — cache успешного каталога и model policy;
- `AI/OpenRouterModelSelection.cs` — compatible filtering, restore policy и request limits;
- `AI/AiWorkerTransport.cs` — versioned stdin/stdout transport и валидация ответа;
- `AI/AiWorkerProcessRunner.cs` — безопасный lifecycle конкретного worker;
- `NavisHelper.AiWorker/` — .NET 9 executable с OpenRouter HTTPS-клиентом;
- `AI/OpenRouterKeyStore.cs` — пользовательская, process и runtime-сессия ключа;
- `NavisHelper.AiWorker/OpenRouterRequestFactory.cs` — strict JSON Schema payload;
- `NavisHelper.AiWorker/OpenRouterColorResponseParser.cs` — структурный разбор и RGB-валидация;
- `AI/OpenRouterContracts.cs` — результаты проверки, каталог и outcome раскраски.

Настройки находятся в `WPF/NavisHelperSettingsTabBuilder.cs`. Подключение
проверяет `/api/v1/key`, затем сохраняет ключ в `OPEN_ROUTER_NW_KEY` для
текущего пользователя и процесса. User-filtered каталог `/api/v1/models/user`
формирует динамический список моделей с подтверждённым `structured_outputs`.

Команда отправляет `/api/v1/chat/completions` только после наличия ключа.
Успешный outcome явно имеет источник `OpenRouter`; ошибки и некорректные ответы
не превращаются в локальные цвета. `ColorService.exe`, IPC через временные
файлы и `LocalColorBridge` в активном/скомпилированном пути отсутствуют.
Локальная палитра запускается только отдельным действием и имеет typed
provenance `LocalPalette`.

До первого `await` workflow считывает документ, выделение, имена и настройки
на UI-потоке. Worker IPC и HTTPS выполняются асинхронно вне UI-потока; результат
применяется через dispatcher только к исходному документу. Координатор
разрешает одну активную операцию, наблюдает все исключения и различает timeout,
отмену и смену документа. Каталог, совместимая выбранная модель и exact full ID
обязательны до chat. Запрос использует strict `json_schema`, не отправляет
reasoning и не выполняет автоматический retry/fallback.

MCP API, contracts, названия инструментов и их параметры этим workflow не
затрагиваются.

Worker поставляется один раз в `NavisHelper.bundle/Contents/AiWorker`, а каждая
версия плагина разрешает путь относительно своей загруженной assembly. Ключ
передаётся только в `OPEN_ROUTER_NW_KEY` окружения дочернего процесса и не
входит в arguments или protocol JSON.
