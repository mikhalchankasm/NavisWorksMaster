# NavisHelper

**Русский** | [English](README.md)

NavisHelper — набор локальных инструментов и MCP-сервер для координации моделей в Autodesk Navisworks Manage под Windows.

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

> Снимок проекта: цифры и внешние метаданные на этой странице проверены **2026-08-08**. В репозитории, его Git-истории и текущих release-assets нет собственного demo GIF, поэтому здесь нет заглушки или чужой демонстрации.

## Установка

Текущий [GitHub Release](https://github.com/mikhalchankasm/NavisWorksMaster/releases/latest) содержит пользовательский Windows installer. Закройте Navisworks, откройте PowerShell и выполните три команды в одной сессии:

```powershell
$release = Invoke-RestMethod `
  "https://api.github.com/repos/mikhalchankasm/NavisWorksMaster/releases/latest"
$installerAsset = $release.assets |
  Where-Object name -Like "NavisHelperSetup-*.exe" |
  Select-Object -First 1
$checksumsAsset = $release.assets |
  Where-Object name -Like "SHA256SUMS-*.txt" |
  Select-Object -First 1
if (-not $installerAsset -or -not $checksumsAsset) {
  throw "Required release assets were not found."
}
```

```powershell
$installer = Join-Path $env:TEMP $installerAsset.name
$checksums = Join-Path $env:TEMP $checksumsAsset.name
Invoke-WebRequest $installerAsset.browser_download_url -OutFile $installer
Invoke-WebRequest $checksumsAsset.browser_download_url -OutFile $checksums
$line = Get-Content $checksums |
  Where-Object { $_ -match "\*$([regex]::Escape($installerAsset.name))$" }
$expected = ($line -split "\s+")[0]
$actual = (Get-FileHash $installer -Algorithm SHA256).Hash
if (-not $expected -or $actual -ne $expected) {
  throw "Installer checksum verification failed."
}
```

```powershell
if (Get-Process Roamer -ErrorAction SilentlyContinue) {
  throw "Close Navisworks before installing."
}
Start-Process $installer -Wait
```

Installer всегда размещает плагин и MCP binaries в профиле текущего пользователя. Необязательное действие настройки MCP-клиентов на странице завершения по умолчанию выключено. Если его выбрать, installer может создать или обновить файл конфигурации в существующем пользовательском каталоге каждого обнаруженного клиента; отсутствующие приложения и их корневые каталоги конфигурации будут пропущены. Перезапускайте только тот клиент, конфигурация которого была изменена. Поздняя или намеренная настройка с `--create-missing` описана в разделе [Client Config](docs/MCP_DISTRIBUTION_PLAN.md#client-config). Другие варианты приведены в [distribution guide](docs/MCP_DISTRIBUTION_PLAN.md) и [agent setup guide](docs/MCP_AGENT_SETUP.md).

### Требования

| Требование | Текущий охват |
|---|---|
| Host | Autodesk Navisworks Manage 2024, 2025, 2026 или 2027 под Windows x64. |
| Runtime | .NET 9 Runtime для framework-dependent MCP-сервера в текущем пакете. |
| Установка | Профиль текущего пользователя; packaged installer не требует прав администратора. |
| MCP client | Клиент, который умеет запускать локальный stdio MCP server. Поддерживаемые configurator-клиенты перечислены в distribution guide. |
| Активная работа | Большинству model tools нужен запущенный Navisworks и открытая модель; view tools также требуют active view. |
| Внешний AI | Для MCP не нужен. Отдельная OpenRouter-раскраска необязательна и использует ключ пользователя. |

## Часть первая: набор плагинов

Compile inventory находит **30 скомпилированных регистраций `[Plugin]`**. Они работают внутри Navisworks Manage и добавляют ribbon и панель NavisHelper. Основные сценарии:

- поиск по модели, выделение, видимость, цветовые overrides и загрузка атрибутов из CSV;
- selection/search sets, saved viewpoints, section-box виды и постоянная разметка;
- экспорт свойств и имён из дерева модели;
- запуск, группировка, изоляция, viewpoints, скриншоты и отчёты Clash Detective;
- необязательная раскраска через OpenRouter и отдельная локальная палитра.

Bundle manifest объявляет Windows x64 поддержку Navisworks Manage **2024–2027**. Navisworks Simulate не объявлен. Официальный [индекс системных требований Navisworks](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/System-requirements-for-Autodesk-Navisworks-products.html) охватывает эти версии хоста; совместимость NavisHelper определяется manifest и build-конфигурацией этого репозитория, а не Autodesk.

## Часть вторая: локальный MCP-сервер

MCP-клиент запускает `NavisHelper.McpServer.exe` по stdio. Сервер находит хост внутри Navisworks и связывается с ним через локальный Windows named pipe; HTTP-порт не открывается.

В исходниках зарегистрировано **100 разных MCP-инструментов**. `scripts/check_mcp_command_catalog.py` выводит их snake_case-имена из 100 методов `[McpServerTool]` и проверяет сгенерированный индекс из 100 строк на **2026-08-08**. Отдельная курируемая таблица статусов — меньший справочник, а не счётчик зарегистрированных tools: сейчас в ней 51 строка `implemented`, 16 проверенных на живой модели `validated`, 15 `planned` и одна `deprecated alias`.

### Семейства инструментов

| Семейство | Типичные задачи |
|---|---|
| Диагностика и lifecycle | Найти экземпляры, проверить здоровье, посмотреть последние вызовы, запустить или закрыть Navisworks. |
| Модель и иерархия | Прочитать контекст, корни, дочерние элементы, свойства, bounding boxes и результаты поиска. |
| Выделение и видимость | Выбрать найденное, проверить selection, скрыть, показать, изолировать и приблизить вид. |
| Наборы и viewpoints | Создать и управлять selection/search sets, viewpoints, папками, порядком и активацией. |
| Разметка и сечения | Создать persistent redlines, live markers, section-box viewpoints и снимки вида. |
| Отчёты, экспорт и цвет | Экспортировать свойства и имена, свести значения, preview/apply цветовых правил. |
| Clash Detective | Читать, группировать, запускать, переименовывать, изолировать и экспортировать clash-данные. |
| Сценарии | Проверять, сохранять, читать, разрешать и удалять согласованные многошаговые процессы. |

Начните с [MCP quickstart](docs/NAVISWORKS_MCP_QUICKSTART.md). Практические процессы есть в [client guide](docs/MCP_CLIENT_GUIDE.md), а точные входы, выходы и ограничения — в [tool contracts](docs/MCP_TOOL_CONTRACTS.md).

### Примеры запросов

- «Найди элементы с `pump` в имени, выбери найденное и приблизь вид».
- «Покажи preview экспорта свойств текущего выделения в XLSX».
- «Покажи активные коллизии в `HVAC vs Structure`, затем preview изоляции первой».
- «Создай saved section-box viewpoint вокруг текущего выделения».
- «Покажи план цветовой схемы до применения permanent overrides».

## Модель безопасности

- MCP-трафик остаётся в локальных stdio и named pipes; сетевой listener не открывается.
- Работа с Autodesk API переводится в UI thread хоста и защищена от busy-state.
- Большинство изменяющих инструментов сначала показывают preview и требуют apply; close/discard требует усиленного подтверждения.
- Match handles и tree item IDs относятся только к текущим host, document и session.
- OpenRouter-раскраска — отдельное opt-in действие плагина, не MCP-зависимость; оно отправляет display names под ключом пользователя.

## Альтернативы для MCP

Сравнение относится только к MCP-интеграции, не ко всему UI или BIM-функционалу. Утверждения взяты из связанных репозиториев; GitHub stars измерены **2026-08-08**.

| Проект | Заявленный охват Navisworks | Связь MCP/host | Отличие от NavisHelper | Stars |
|---|---|---|---|---:|
| **NavisHelper** | Manage 2024–2027 | .NET stdio server → per-process named pipe; пользовательский installer | Curated Navisworks operations и dry-run-oriented writes; нет Simulate и других host-продуктов | 0 |
| [Aitology/Navisworks_MCP](https://github.com/Aitology/Navisworks_MCP) | Заявлены Manage и Simulate 2025–2027 | Python stdio server → localhost HTTP add-in | Поддерживает Simulate; NavisHelper поддерживает 2024 и поставляет объединённый installer | 14 |
| [General-Soju/BimOnMcp](https://github.com/General-Soju/BimOnMcp) | Заявлены Navisworks 2025–2027, Revit и AutoCAD | Self-contained stdio bridge → per-process named pipes | Несколько Autodesk hosts и выполнение скриптов; NavisHelper вместо этого даёт более широкий curated Navisworks surface и поддерживает 2024 | 8 |

Первичные источники: [архитектура, prerequisites и tool list Aitology](https://github.com/Aitology/Navisworks_MCP#readme), а также [таблицы версий, архитектуры и MCP tools BimOn](https://github.com/General-Soju/BimOnMcp#readme). В default branch Aitology найдено **39** деклараций `Tool(name=...)`, хотя заголовок README говорит **40**, поэтому это число не используется здесь как подтверждённый tool count. В source и README BimOn разделены **11** Navisworks-specific и **6** общих script tools.

## Снимок проверки

Локальное измерение этой task-ветки на основе commit `main` `b54f8e3` от **2026-08-09**:

- source inventory guard: passed; **207** tracked C# files, **205** реальных compile entries и **2** явных исключения;
- MCP catalog guard: passed и покрывает все **100** зарегистрированных tools;
- host router guard: passed; **83** command names и **76** typed routes;
- automated MCP-server tests: baseline `main` после исправления newline-sensitive source-structure regression — **1 305 passed, 0 failed, 1 305 total**; в этой ветке после добавления трёх installer-semantics regressions — **1 308 passed, 0 failed, 1 308 total**;
- release build matrix: `Release2024`, `Release2025`, `Release2026` и `Release2027` для x64 прошли; все 12 обязательных bundle assemblies имеют version `2.9.0.0`;
- distribution validation, ZIP fresh/reinstall/legacy-upgrade smoke, Inno Setup compilation и изолированный installer bundle-upgrade smoke: прошли;
- публичный installer `v2.9.0.0`: SHA-256 скачанного файла совпал с опубликованными checksum-файлами и GitHub asset digest; установка с выключенной настройкой MCP сохранила SHA пяти проверенных клиентских конфигов, а проверенные установленные NavisHelper bundle/MCP assemblies имели version `2.9.0.0`; live Navisworks smoke и замена release asset здесь не заявляются.

Automated helper tests не заменяют проверку внутри Autodesk Navisworks Manage. Большая часть host-поведения зависит от Autodesk runtime и пользовательской модели.

## Документация

| Тема | Документ |
|---|---|
| Первый MCP-сеанс | [Navisworks MCP quickstart](docs/NAVISWORKS_MCP_QUICKSTART.md) |
| Настройка клиентов и процессы | [MCP client guide](docs/MCP_CLIENT_GUIDE.md) |
| Входы и выходы инструментов | [MCP tool contracts](docs/MCP_TOOL_CONTRACTS.md) |
| Архитектура | [MCP architecture](docs/MCP_ARCHITECTURE.md) |
| Упаковка и установка | [MCP distribution plan](docs/MCP_DISTRIBUTION_PLAN.md) |
| Сборка и вклад в проект | [Contributing](CONTRIBUTING.md) и [build/bundle rules](BUILD_BUNDLE_RULES.md) |
| Полный прежний README | [Архив на английском](docs/reference/README_FULL.md) |
| Полный прежний русский README | [Архив на русском](docs/reference/README_FULL.ru.md) |

## Статус и лицензия

Проект поддерживается с низкой активностью. Pull requests приветствуются; ответы на issues не гарантируются.

Код распространяется по [MIT License](LICENSE). Сторонние уведомления — в [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Уведомление Autodesk

Autodesk и Navisworks являются зарегистрированными товарными знаками или товарными знаками Autodesk, Inc. и/или её дочерних и аффилированных компаний. NavisHelper — независимый проект; он не связан с Autodesk, не авторизован, не одобрен и не спонсируется Autodesk, Inc. См. официальный [список товарных знаков](https://www.autodesk.com/company/legal-notices-trademarks/intellectual-property/trademarks) и [правила для совместимых продуктов](https://www.autodesk.com/company/legal-notices-trademarks/trademarks/guidelines-for-use).
