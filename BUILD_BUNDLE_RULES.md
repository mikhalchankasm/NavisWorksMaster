# Build And Bundle Rules

Этот файл фиксирует правила сборки и обновления bundle для `NavisHelper`.

## Актуальная матрица версий

- `Debug` / `Release` -> Navisworks 2026
- `Debug2024` / `Release2024` -> Navisworks 2024
- `Debug2025` / `Release2025` -> Navisworks 2025
- `Debug2026` / `Release2026` -> Navisworks 2026
- `Debug2027` / `Release2027` -> Navisworks 2027

## Bundle: что обязательно поддерживать

При любых изменениях плагина нужно локально пересобрать четыре bundle-сборки перед упаковкой, установкой или публикацией:

- `NavisHelper.bundle/Contents/2024/NavisHelper.dll`
- `NavisHelper.bundle/Contents/2024/NavisHelper.Contracts.dll`
- `NavisHelper.bundle/Contents/2024/ru/NavisHelper.resources.dll`
- `NavisHelper.bundle/Contents/2025/NavisHelper.dll`
- `NavisHelper.bundle/Contents/2025/NavisHelper.Contracts.dll`
- `NavisHelper.bundle/Contents/2025/ru/NavisHelper.resources.dll`
- `NavisHelper.bundle/Contents/2026/NavisHelper.dll`
- `NavisHelper.bundle/Contents/2026/NavisHelper.Contracts.dll`
- `NavisHelper.bundle/Contents/2026/ru/NavisHelper.resources.dll`
- `NavisHelper.bundle/Contents/2027/NavisHelper.dll`
- `NavisHelper.bundle/Contents/2027/NavisHelper.Contracts.dll`
- `NavisHelper.bundle/Contents/2027/ru/NavisHelper.resources.dll`

Это обязательное правило для всех дополнительных действий, связанных со сборкой, выкладкой и обновлением bundle.

Скомпилированные DLL/PDB внутри `NavisHelper.bundle/Contents/<version>/` не отслеживаются git. В репозитории остаются структура bundle, `PackageContents.xml`, `.dll.config`, `icons/` и `ICONS.md`. Бинарники появляются локально после build matrix и попадают к пользователям только через `artifacts/` и GitHub Releases.

## Как это работает в проекте

- Конфигурации `2024`, `2025`, `2026` и `2027` добавлены в `NavisHelper.sln`
- Версионные ссылки на SDK выбираются в `NavisHelper/NavisHelper.csproj`
- После сборки выполняется `CopyBundleArtifacts`
- `Release2024` копирует DLL в `Contents/2024`
- `Release2025` копирует DLL в `Contents/2025`
- `Release` и `Release2026` копируют DLL в `Contents/2026`
- `Release2027` копирует DLL в `Contents/2027`
- `NavisHelper.Contracts.dll` копируется рядом с `NavisHelper.dll`, потому что основной плагин и MCP-сервер используют общий контрактный проект `NavisHelper.Contracts`
- русская satellite assembly копируется в `Contents/<version>/ru/NavisHelper.resources.dll`
- общий .NET 9 worker OpenRouter размещается один раз в `Contents/AiWorker`; его нельзя дублировать по каталогам версий Navisworks

Важно: сборка обновляет только bundle внутри репозитория: `NavisHelper.bundle`. NavisHelper поддерживает только пользовательскую установку, поэтому Navisworks должен загружать bundle из `%APPDATA%\Autodesk\ApplicationPlugins\NavisHelper.bundle`. После локальной сборки нужно отдельно выполнить install/update шага, иначе Navisworks продолжит грузить старую установленную DLL. Старая системная копия в `ProgramData` или `Program Files` должна быть удалена перед установкой.

`tools/package_distribution.ps1` проверяет наличие всех поддерживаемых bundle DLL и падает с понятной ошибкой, если package запускается из свежего клона без build matrix или с `-SkipBuild` до сборки.

## Установка bundle для локальной проверки

Основной dev-путь без прав администратора:

```powershell
powershell -ExecutionPolicy Bypass -File tools\install_local_bundle.ps1
```

Эта команда устанавливает свежий `NavisHelper.bundle` в пользовательский Autodesk ApplicationPlugins root:

```text
%APPDATA%\Autodesk\ApplicationPlugins\NavisHelper.bundle
```

Перед живой проверкой (L3) сверьте, что Navisworks загрузит именно то, что вы собрали:

```powershell
python scripts/check_installed_bundle_drift.py
```

Проверка закрывает три звена: плагин, который загрузит Navisworks; MCP-серверы, установленные
на этой машине; и конфиги клиентов, которые решают, какой из серверов запустится. Расходятся
они молча — ни сборка, ни установщики, ни handshake хоста, ни сам клиент об этом не сообщают:
каждый рапортует успех, пока под ним лежит старое.

Плагин сравнивается по sha256 в трёх копиях — вывод сборки, bundle в репозитории и
установленный bundle, — и скрипт называет, какое звено разошлось. Если установки нет вовсе,
это не дрейф, и скрипт говорит это отдельно. Аргументом можно передать другой корень,
например системный остаток в `ProgramData`, которого быть не должно.

Разные хэши не всегда значат разный код: пересборка меняет MVID сборки даже при неизменных
исходниках. Ответ от этого не меняется — переустановить.

### MCP-сервер: `MCP SERVER DRIFT`

Сервер ставится отдельно от bundle — в `%LOCALAPPDATA%\NavisHelper`, в каталоги `McpServer` и
`McpServer-<version>`. Скрипт инвентаризирует их все: версии читает из
`NavisHelper.McpServer.deps.json`, а равенство проверяет по sha256 каждого установленного
`NavisHelper.McpServer.dll` против сборки чекаута `NavisHelper.McpServer/bin/Release/net9.0`.
Версии остаются полезным инвентарём, но доказательством не являются: код меняется и без роста
`AppVersion`.

Если сервер в чекауте не собран, сравнивать не с чем — скрипт печатает `MCP SERVER NOTHING
VERIFIED` и отказывается подтвердить установку, ровно как в случае неподнятого плагина. Если
ни один установленный сервер не совпал по хэшу, печатается `MCP SERVER DRIFT` с разбором
каждого каталога: `code differs`, `DLL missing` и, когда `AppVersion` всё же сошлась,
`version matches, code differs` или `version matches, DLL missing`. Устаревшие установки рядом
с той, что совпала по хэшу, — примечание, а не дрейф.

### Конфиги клиентов: `MCP CLIENT DRIFT`

Инвентарь не отвечает, какой сервер запустит клиент: это выбирает конфиг клиента, а не
содержимое диска. Поэтому скрипт читает и конфиги — у пяти клиентов, которых пишет
`NavisHelper.McpConfigurator`: Claude Desktop, Codex, Cursor, OpenCode и Kimi Code. По каждому
он называет каталог сервера из конфига и выносит один из пяти вердиктов:

- `matches` — DLL в этом каталоге совпадает по хэшу со сборкой чекаута;
- `differs` — каталог есть, но DLL в нём другая;
- `missing` — каталога или его DLL нет;
- `not configured` — файл конфига есть, но сервер NavisHelper в нём не назван;
- `no config` — файла конфига у клиента нет.

Claude Code держит серверы в собственных настройках, а не в одном из этих файлов, и не
проверяется. Если корня конфига нет в окружении, про этого клиента не проверяется ничего, и
скрипт говорит это отдельно. Если не собран сервер в чекауте, `matches` невозможен в принципе,
а `differs` означает «не показано, что это сборка этого чекаута».

Вердикты описывают следующий запуск: клиент, который уже работает, держит процесс сервера,
который стартовал. Что отвечает прямо сейчас, говорит только `mcp_health_check`.

В конфиге клиента лежат и все остальные его серверы — с адресами и секретами. Файлы поэтому не
парсятся и не печатаются: из них вытаскивается только путь, заканчивающийся на
`NavisHelper.McpServer.exe` или `.dll`, и наружу выходит только каталог этого пути.

`differs` или `missing` складываются в `MCP CLIENT DRIFT` со списком клиентов. Ненулевой код
возврата проверка даёт и за дрейф bundle, и за дрейф сервера, и за дрейф конфига — и точно так
же за «ничего не проверено».

### Чем чинить сервер и клиентов

```powershell
dotnet build NavisHelper.McpServer/NavisHelper.McpServer.csproj -c Release
powershell -ExecutionPolicy Bypass -File tools\install_local_mcp_server.ps1
& "$env:LOCALAPPDATA\NavisHelper\McpConfigurator\NavisHelper.McpConfigurator.exe" --configure --clients all --mcp-server "$env:LOCALAPPDATA\NavisHelper\McpServer-<version>\NavisHelper.McpServer.exe"
```

Сборка нужна, если сервер в чекауте не собран (`MCP SERVER NOTHING VERIFIED`): установщик берёт
его из `NavisHelper.McpServer/bin/Release/net9.0` и без DLL сразу падает.

Если переустанавливается та же версия (`version matches, code differs`), а какой-то клиент
уже запустил сервер из этого `McpServer-<version>`, установщик откажется заменять каталог.
Тогда сначала закройте такие клиенты, потом установка и конфигуратор, потом запуск клиентов.
Новая версия ставится в свой каталог и работающему клиенту не мешает — ему хватит перезапуска
после конфигуратора: запущенный клиент держит старый процесс.

`<version>` — каталог, который инвентарь назвал совпавшим по хэшу; в команду его подставляет
сам скрипт. `--clients all` у конфигуратора включает и Claude Code, которого проверка не читает.

### Чем `--detect` не помогает

`NavisHelper.McpConfigurator --detect` печатает путь, который он собирается прописать, — из
`--mcp-server` или из последней версии в `%LOCALAPPDATA%\NavisHelper`, — а не тот, что уже
лежит в конфиге. На вопрос «какой сервер запустит этот клиент» он не отвечает. 2026-09-24 так
и проглядели: свежий сервер поставился в `McpServer-2.10.0.0`, конфиг Claude Desktop указывал
на `McpServer-d28e8b7`, конфиг Codex — на `McpServer` (2.9), а `--detect` показывал новый
путь, так что в репозитории ничего не расходилось.

Сама проверка в CI не запускается — там нет установленного bundle, — но её логика да:
`python scripts/check_installed_bundle_drift.py --selftest` прогоняет каждую ветку на
временных каталогах, включая ту, ради которой всё и затевалось: чистый клон, где DLL
никогда не собирались, обязан отказаться подтверждать установку, а не молчать. Пять
клиентских вердиктов прогоняются там же, на фиктивных конфигах: на живой машине увидеть их
можно, только перенастроив настоящий клиент, чего проверка делать не должна.

Это единственный `scripts/check_*.py`, который проверяет не репозиторий, а машину, и одна
только сборка заставляет его упасть — справедливо, ведь сборка и есть то, что оставляет
установку позади. Поэтому в общий прогон гвардов перед коммитом он не входит и в CI не
включён: там нет установленного bundle, а проверка, которая не может упасть, — не проверка.

Системные установки NavisHelper не поддерживаются. Если остались `C:\ProgramData\Autodesk\ApplicationPlugins\NavisHelper.bundle` или `C:\Program Files\NavisHelper`, удалите их из elevated PowerShell с помощью `tools\remove_machinewide_bundle.ps1 -Force`. Перед install/update Navisworks должен быть закрыт.

## Команды сборки

```powershell
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' NavisHelper.sln /p:Configuration=Release2024 /p:Platform=x64
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' NavisHelper.sln /p:Configuration=Release2025 /p:Platform=x64
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' NavisHelper.sln /p:Configuration=Release2026 /p:Platform=x64
& 'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\amd64\MSBuild.exe' NavisHelper.sln /p:Configuration=Release2027 /p:Platform=x64
```

Если `msbuild` не находится через `PATH`, использовать полный путь, как выше.

## Проверка после изменений

После правок нужно проверить:

1. Сборка `Release2024|x64` проходит успешно
2. Сборка `Release2025|x64` проходит успешно
3. Сборка `Release2026|x64` проходит успешно
4. Сборка `Release2027|x64` проходит успешно
5. В локальном bundle после сборки реально появились/обновились `NavisHelper.dll`, `NavisHelper.Contracts.dll` и `ru/NavisHelper.resources.dll` для поддерживаемых bundle-версий
6. `NavisHelper.Contracts` для release-конфигураций собирается как `Release|Any CPU`, не `Debug|Any CPU`
7. `NavisHelper.bundle/PackageContents.xml` содержит блоки `2024`, `2025`, `2026` и `2027`
8. Для визуальной проверки в Navisworks установленный bundle обновлён через `tools\install_local_bundle.ps1` или release installer, а не только собран в репозитории

## Важный API-нюанс для 2027

В Navisworks 2027 `DocumentClashTests.Tests` больше недоступен.

Для совместимости `2026` и `2027` использовать helper:

- `NavisHelper/Core/ClashApiCompat.cs`

Он читает clash-тесты через общий путь:

```csharp
clash.TestsData.Value.TestsRoot.Children
```

Не возвращаться к прямому использованию `clash.TestsData.Tests`, иначе сборка 2027 снова сломается.

## Если в будущем добавляется новая версия Navisworks

Нужно сделать все пункты сразу:

1. Добавить конфигурации в `.sln`
2. Добавить version-specific references в `NavisHelper.csproj`
3. Добавить новый блок в `NavisHelper.bundle/PackageContents.xml`
4. Расширить copy-логику bundle
5. Обновить этот файл и `README.md` (сборка живёт здесь; `CLAUDE.md` её копию больше не держит)
6. Проверить сборку старых поддерживаемых версий, чтобы не словить регрессию
