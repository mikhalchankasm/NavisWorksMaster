# Промт для ИИ-агента: установить или обновить MCP `navishelper`

**Как пользоваться:** открой чат своего ИИ-агента (Cursor, Claude Code, Codex, OpenCode, Kimi и т.п.) и вставь блок ниже. Агент должен установить или обновить NavisHelper, прописать MCP server в текущий клиент и проверить результат.

Если агенту уже передан готовый ZIP `NavisHelper-full-*.zip`, попроси его использовать этот ZIP как основной источник установки: распаковать архив, перейти в распакованную папку и запустить `Install-NavisHelperBundle.ps1 -ConfigureMcp`. ZIP и `.exe` installer всегда устанавливаются для текущего пользователя и не требуют прав администратора. Репозиторий/GitHub Release нужны только если готового package нет.

```text
=== НАЧАЛО ИНСТРУКЦИИ ДЛЯ АГЕНТА ===

Задача: установить или обновить MCP-сервер "navishelper" из переданного ZIP/installer или из репозитория:
https://github.com/mikhalchankasm/NavisWorksMaster

Действуй автономно. Спрашивай только если не можешь определить целевой MCP-клиент.

Контекст:
- Это Windows/Navisworks MCP, не uvx-пакет.
- Нужно установить Autodesk bundle и локальный MCP server.
- Если в чате/папке уже есть готовый `NavisHelper-full-*.zip`, используй его первым и не клонируй репозиторий без необходимости.
- `NavisHelperSetup-*.exe` и ZIP/package всегда устанавливаются для текущего пользователя без прав администратора.
- Если обнаружена старая системная установка NavisHelper в `Program Files` или `ProgramData`, удали её из PowerShell с правами администратора: `tools\remove_machinewide_bundle.ps1 -Force` (из распакованного ZIP или clone). Новый installer/package специально остановится до копирования файлов, чтобы не оставить конфликтующие bundle-версии.
- Репозиторий может быть private. Не читай `raw.githubusercontent.com` как источник инструкции: сначала получи локальную копию через `git clone` или обнови существующий clone через `git pull`.
- Ожидаемый путь после ZIP/package per-user установки:
  %LOCALAPPDATA%\NavisHelper\McpServer-<версия>\NavisHelper.McpServer.exe
- Configurator:
  %LOCALAPPDATA%\NavisHelper\McpConfigurator\NavisHelper.McpConfigurator.exe
- Bundle:
  %APPDATA%\Autodesk\ApplicationPlugins\NavisHelper.bundle

ШАГ 1. Проверь платформу.
- Если это не Windows, остановись: этот MCP требует Windows и Autodesk Navisworks.
- Попроси закрыть Navisworks, если запущен процесс Roamer.exe.

ШАГ 2. Проверь, есть ли готовый ZIP или installer.
- Если есть `NavisHelper-full-*.zip`, распакуй его в отдельную папку, перейди в распакованную папку и выполни без прав администратора:
  powershell -ExecutionPolicy Bypass -File .\Install-NavisHelperBundle.ps1 -ConfigureMcp
- Если есть только `NavisHelperSetup-*.exe`, запусти его обычным способом без повышения прав.
- После ZIP/package установки MCP config должен указывать на `%LOCALAPPDATA%\NavisHelper\McpServer-<версия>\NavisHelper.McpServer.exe`, а не на временную папку распаковки. Установка новой версии не завершает уже работающий stdio process; перезапусти или reload MCP-клиент после обновления.
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

ШАГ 4. Попробуй установить из последнего GitHub Release.
- Через `gh release view --repo mikhalchankasm/NavisWorksMaster` или страницу release проверь latest release:
  https://github.com/mikhalchankasm/NavisWorksMaster/releases/latest
- Предпочитай полный ZIP-пакет `NavisHelper-full-*.zip`, потому что он обновляет bundle, MCP server и configurator одним действием без прав администратора.
- Если ZIP-пакет есть, скачай и распакуй его в понятную папку, затем:
  1) установи bundle и MCP binaries через Install-NavisHelperBundle.ps1;
  2) если скрипт поддерживает -ConfigureMcp, используй его, чтобы сразу прописать MCP clients.
- `NavisHelperSetup-*.exe` также устанавливает компоненты в пользовательские каталоги и не требует повышения прав.
- Если release отсутствует, используй fallback для разработческой установки:
  1) в локальном clone собери package: powershell -ExecutionPolicy Bypass -File tools\package_distribution.ps1;
  2) установи bundle и MCP binaries из package;
  3) настрой MCP через McpConfigurator из package.

ШАГ 4А. Запусти установку.
- Для ZIP/package используй per-user установку без admin.
- Для `.exe` installer используй обычный запуск без повышения прав и дождись завершения.
- Per-user ZIP/package должен положить MCP server в `%LOCALAPPDATA%\NavisHelper\McpServer-<версия>` и bundle в `%APPDATA%\Autodesk\ApplicationPlugins`.
- Для ZIP/package из этого репозитория предпочитай:
  powershell -ExecutionPolicy Bypass -File .\Install-NavisHelperBundle.ps1 -ConfigureMcp
- После ZIP/package установки MCP config должен указывать на `%LOCALAPPDATA%\NavisHelper\McpServer-<версия>\NavisHelper.McpServer.exe`, а не на временную папку распаковки.

ШАГ 5. Настрой MCP-клиенты без удаления существующих серверов.
Выполни:

& "$env:LOCALAPPDATA\NavisHelper\McpConfigurator\NavisHelper.McpConfigurator.exe" --configure --clients all --create-missing

Если configurator сообщает об отсутствии server, подставь реальный новый путь `McpServer-<версия>\NavisHelper.McpServer.exe` из вывода install script.

ШАГ 6. Проверь результат.
Выполни:

& "$env:LOCALAPPDATA\NavisHelper\McpConfigurator\NavisHelper.McpConfigurator.exe" --detect

Проверь, что:
- нужный MCP-клиент найден;
- server path указывает на существующий NavisHelper.McpServer.exe;
- bundle существует в `%APPDATA%\Autodesk\ApplicationPlugins\NavisHelper.bundle`.

ШАГ 7. Если клиент поддерживает MCP refresh/reload, обнови список MCP servers.
Если нет, попроси пользователя перезапустить клиент. Codex и часть других клиентов читают MCP config только при старте текущей сессии, поэтому уже открытый чат может не увидеть только что добавленный server.

ШАГ 8. Кратко отчитайся:
- где находится локальный clone репозитория;
- откуда установил: release installer, release ZIP или fallback из исходников;
- какой config изменён;
- какой server path прописан;
- что показал --detect;
- нужно ли перезапустить клиент или Navisworks.

=== КОНЕЦ ИНСТРУКЦИИ ДЛЯ АГЕНТА ===
```

## Короткая команда

Универсальный вариант для public и private repo:

```text
Склонируй или обнови https://github.com/mikhalchankasm/NavisWorksMaster,
прочитай docs/prompts/SETUP_PROMPT.md из репозитория и выполни инструкцию по установке
или обновлению MCP-сервера navishelper для моего MCP-клиента.
```

Не используйте `raw.githubusercontent.com` как обязательный источник: для private repo
он часто возвращает `404 Not Found` без авторизованного доступа.
