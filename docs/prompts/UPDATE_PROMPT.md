# Промт для ИИ-агента: обновить MCP `navishelper`

Используй этот prompt, когда NavisHelper уже установлен и нужно подтянуть свежую версию.

Если агенту уже передан готовый ZIP `NavisHelper-full-*.zip`, используй его первым: это основной сценарий без прав администратора. `NavisHelperSetup-*.exe` также всегда устанавливает компоненты для текущего пользователя без повышения прав. Репозиторий/GitHub Release нужны только если готового package нет.

```text
=== НАЧАЛО ИНСТРУКЦИИ ДЛЯ АГЕНТА ===

Задача: обновить установленный MCP-сервер "navishelper" из репозитория:
https://github.com/mikhalchankasm/NavisWorksMaster

Действуй автономно. Не удаляй существующие MCP-серверы из конфигов. Сохрани путь к server, если он уже настроен и новая установка использует тот же путь.
Репозиторий может быть private. Не читай `raw.githubusercontent.com` как источник инструкции: сначала получи локальную копию через `git clone` или обнови существующий clone через `git pull`.
Если найдена старая системная установка NavisHelper в `Program Files` или `ProgramData`, удали её из PowerShell с правами администратора: `tools\remove_machinewide_bundle.ps1 -Force` (из распакованного ZIP или clone). Новый installer/package не изменяет защищённые legacy-файлы и остановится до копирования, чтобы не оставить конфликтующие bundle-версии.

ШАГ 1. Определи текущую установку.
Проверь:
- %LOCALAPPDATA%\NavisHelper\McpServer-<версия>\NavisHelper.McpServer.exe
- %LOCALAPPDATA%\NavisHelper\McpConfigurator\NavisHelper.McpConfigurator.exe
- %APPDATA%\Autodesk\ApplicationPlugins\NavisHelper.bundle

Если configurator найден, выполни:

& "$env:LOCALAPPDATA\NavisHelper\McpConfigurator\NavisHelper.McpConfigurator.exe" --detect

ШАГ 2. Проверь, есть ли готовый ZIP или installer.
- Если есть `NavisHelper-full-*.zip`, распакуй его в отдельную папку, перейди в распакованную папку и выполни без прав администратора:
  powershell -ExecutionPolicy Bypass -File .\Install-NavisHelperBundle.ps1 -ConfigureMcp
- Если есть только `NavisHelperSetup-*.exe`, запусти его обычным способом без повышения прав.
- После ZIP/package установки MCP config должен указывать на `%LOCALAPPDATA%\NavisHelper\McpServer-<версия>\NavisHelper.McpServer.exe`, а не на временную папку распаковки.
- ZIP `v2.6.3.0` мог оставить устаревшую папку `%LOCALAPPDATA%\NavisHelper\McpServer` без версии. Новый ZIP удаляет её только если это подтверждённый неработающий runtime NavisHelper; неизвестную папку не удаляй, а сообщи пользователю предупреждение.
- Для private repo используй Git Credential Manager, `gh auth login` или GitHub device flow. Никогда не вставляй PAT, cookies или другие секреты в prompt, чат или лог.
- Если installer/ZIP успешно установлен, переходи сразу к ШАГУ 5.

ШАГ 3. Если готового installer/ZIP нет, получи локальную копию репозитория.
- Найди существующий clone `NavisWorksMaster`, если он уже есть на компьютере.
- Если clone найден, выполни в нём:
  git pull --ff-only
- Если clone не найден, склонируй:
  git clone https://github.com/mikhalchankasm/NavisWorksMaster
- Если репозиторий private и clone/download требует авторизацию, используй уже настроенный `git`/`gh` login. Если авторизации нет, попроси пользователя выполнить GitHub login и повтори.
- Дальше работай из локальной папки репозитория. Не используй raw URL как обязательный источник.

ШАГ 4. Закрой Navisworks перед обновлением.
Если запущен Roamer.exe, попроси пользователя закрыть Navisworks или закрой только явно тестовый процесс, если ты сам его запускал.

ШАГ 4А. Обнови из последнего release.
- Через `gh release view --repo mikhalchankasm/NavisWorksMaster` или страницу release проверь latest release:
  https://github.com/mikhalchankasm/NavisWorksMaster/releases/latest
- Скачай самый новый полный ZIP-пакет `NavisHelper-full-*.zip`.
- Предпочитай ZIP/package, потому что он обновляет bundle, MCP server и configurator согласованно в per-user директориях без прав администратора.
- Если используешь ZIP/package из этого репозитория, распакуй его и запусти без прав администратора:
  powershell -ExecutionPolicy Bypass -File .\Install-NavisHelperBundle.ps1 -ConfigureMcp
- `NavisHelperSetup-*.exe` скачивай и запускай обычным способом: он устанавливает bundle и MCP-компоненты в пользовательские каталоги.
- После ZIP/package установки MCP config должен указывать на `%LOCALAPPDATA%\NavisHelper\McpServer-<версия>\NavisHelper.McpServer.exe`, а не на временную папку распаковки. Не останавливай активный MCP process: новая версия ставится в соседнюю versioned directory, а клиент переключится после restart/reload.

Fallback для dev-установки без release:
- собери:
  powershell -ExecutionPolicy Bypass -File tools\package_distribution.ps1
- установи bundle и MCP binaries из нового package;
- настрой MCP через новый McpConfigurator.

ШАГ 5. Повторно настрой MCP clients.
Выполни:

& "$env:LOCALAPPDATA\NavisHelper\McpConfigurator\NavisHelper.McpConfigurator.exe" --configure --clients all --create-missing

ШАГ 6. Проверь обновление.
Выполни:

& "$env:LOCALAPPDATA\NavisHelper\McpConfigurator\NavisHelper.McpConfigurator.exe" --detect

Проверь наличие:
- MCP server exe;
- NavisHelper.bundle;
- Contents\2024\NavisHelper.dll;
- Contents\2025\NavisHelper.dll;
- Contents\2026\NavisHelper.dll;
- Contents\2027\NavisHelper.dll;
- Contents\2024\NavisHelper.Contracts.dll;
- Contents\2025\NavisHelper.Contracts.dll;
- Contents\2026\NavisHelper.Contracts.dll;
- Contents\2027\NavisHelper.Contracts.dll.

ШАГ 7. Отчёт.
Кратко напиши:
- где находится локальный clone репозитория;
- какая версия/asset установлены;
- какой путь MCP server прописан;
- какие клиенты обновлены;
- нужен ли restart MCP-клиента или Navisworks. Codex и часть других клиентов читают MCP config только при старте текущей сессии.

=== КОНЕЦ ИНСТРУКЦИИ ДЛЯ АГЕНТА ===
```

Короткая команда для агента:

```text
Склонируй или обнови https://github.com/mikhalchankasm/NavisWorksMaster,
прочитай docs/prompts/UPDATE_PROMPT.md из репозитория и обнови MCP-сервер navishelper
для моего MCP-клиента.
```

Не используйте `raw.githubusercontent.com` как обязательный источник: для private repo
он часто возвращает `404 Not Found` без авторизованного доступа.
