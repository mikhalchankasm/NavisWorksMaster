# AI Color Objects: краткое описание реализации

## Компоненты

- `AIColorObjects.cs` — entry point плагина Navisworks.
- `AIColorUtils.cs` — фильтрация выбора, извлечение имён объектов и применение
  цветов.
- `AIColorObjects.cs` / `LocalColorBridge` — IPC через временные файлы и
  локальный fallback цветов.
- `ColorService/Program.cs` — внешний процесс запросов к OpenRouter.
- `AIColorService.cs` — legacy-клиент прямых запросов к OpenRouter; текущий
  путь команды `AIColorObjects` его не вызывает.
- `AIConfig.cs` и `AIModels` — несекретная конфигурация, сопоставление моделей
  и значения по умолчанию.
- `ColorSchemes.cs` — детерминированные локальные палитры, используемые при
  недоступности внешнего пути.

## Текущее поведение

`AIColorObjects` читает отображаемые имена выбранных объектов, для которых
можно изменить цвет. Команда пытается запустить `ColorService.exe` рядом с
плагином и обменивается JSON-запросом и ответом через уникальные файлы в
`%TEMP%`.

Distribution и installer, собираемые `tools/build_installer.ps1`, не содержат
`ColorService.exe`. Поэтому установка из этих артефактов использует локальный
fallback, если исполняемый файл не предоставлен отдельно рядом с плагином.

`ColorService.exe` использует endpoint chat completions OpenRouter:

```text
https://openrouter.ai/api/v1/chat/completions
```

API-ключ предоставляется пользователем по схеме bring-your-own-key и читается
только из:

```text
OPEN_ROUTER_NW_KEY
```

Ключ не записывается в `%APPDATA%\NavisHelper\ai_config.json`. Этот файл хранит
endpoint, timeout, количество попыток, выбранную модель, temperature, лимит
токенов, цветовую схему и настройку режима thinking.

## Модели и значения по умолчанию

Текущий состав `AIModels.Available`:

- `claude-sonnet-4.6` → `anthropic/claude-sonnet-4.6`
- `claude-opus-4.6` → `anthropic/claude-opus-4.6`
- `glm-5-turbo` → `z-ai/glm-5-turbo`
- `gpt-5.4` → `openai/gpt-5.4`
- `gemini-3-flash` → `google/gemini-3-flash-preview`

Значения по умолчанию из `AIConfig.cs`:

- модель: `claude-sonnet-4.6`;
- request timeout: 60000 ms;
- максимальное количество попыток: 2;
- максимальное количество токенов ответа: 2000;
- temperature: 0.3;
- цветовая схема: 8, Architectural;
- режим thinking: включён.

## Локальный fallback

Если `ColorService.exe` отсутствует, не запускается или не получает
`OPEN_ROUTER_NW_KEY`, реализация генерирует цвета локально из выбранной палитры
`ColorSchemes`. На этом пути внешний API не вызывается.

## Передача данных во внешний API

Если `ColorService.exe` предоставлен отдельно и доступен
`OPEN_ROUTER_NW_KEY`, запрос OpenRouter содержит имена выбранных объектов и имя
выбранной цветовой схемы. Геометрия модели в запрос не входит. OpenRouter
направляет запрос выбранному провайдеру модели с использованием ключа
пользователя.

MCP-сервер не использует этот внешний AI-путь.
