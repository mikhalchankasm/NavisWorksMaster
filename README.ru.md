<div align="center">

# NavisHelper

**Панель и AI-помощник для Autodesk Navisworks Manage 2024–2027**

Скажите Claude, Codex или Cursor, что нужно сделать в модели, и NavisHelper выполнит это прямо в открытом Navisworks.

[![Последний выпуск](https://img.shields.io/github/v/release/mikhalchankasm/NavisWorksMaster?label=%D0%B2%D1%8B%D0%BF%D1%83%D1%81%D0%BA)](https://github.com/mikhalchankasm/NavisWorksMaster/releases/latest)
[![Navisworks](https://img.shields.io/badge/Navisworks_Manage-2024%E2%80%932027-0696D7)](#требования)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**[⬇ Скачать установщик](https://github.com/mikhalchankasm/NavisWorksMaster/releases/latest)** · [Что умеет](#что-умеет) · [Установка](#установка) · [English](README.md)

</div>

<!-- Промо-ролик: вставить сюда. -->

## Что умеет

**В панели Navisworks, без AI**
- поиск, выделение, скрытие и раскраска элементов;
- наборы выборки и поиска, сохранённые виды, разрезы и разметка;
- выгрузка свойств и имён из дерева модели;
- работа с Clash Detective: группы, изоляция, виды, отчёты.

**Вместе с AI** (Claude, Codex, Cursor и другие)
- скажите задачу обычными словами, и AI выполнит её через 117 инструментов NavisHelper;
- каждое изменение AI сначала показывает и делает только после подтверждения;
- в **защищённом режиме** AI не изменит документ, пока вы не разрешите это в панели.

## Примеры запросов к AI

> «Найди все насосы, выдели их и приблизь вид».

> «Выгрузи свойства выделенных элементов в Excel».

> «Покажи активные коллизии в тесте `HVAC vs Structure` и изолируй первую».

> «Сделай сохранённый вид с разрезом вокруг выделения».

> «Раскрась модель по системам, но сначала покажи, что получится».

Три сценария целиком, с результатом каждого шага, описаны в [пользовательских сценариях](docs/USER_WORKFLOWS.md).

## Установка

Скачайте `NavisHelperSetup-<версия>.exe` из [последнего выпуска](https://github.com/mikhalchankasm/NavisWorksMaster/releases/latest) и запустите его. Сначала закройте Navisworks. Права администратора не нужны.

1. **Откройте Navisworks.** На ленте появится вкладка **NavisHelper**, кнопка **NavisHelper** на ней открывает панель.
2. **Перезапустите AI-программы**, которыми пользуетесь. Если флажок на последней странице установщика остался включён, NavisHelper уже к ним подключён.
3. **Claude Desktop:** откройте Settings → Extensions, выберите установку расширения из файла и укажите `NavisHelper.mcpb`. Папку с ним открывает ярлык «Расширение NavisHelper для Claude Desktop» в меню «Пуск».

Другие способы установки (ZIP, установка AI-агентом, ручная настройка клиентов) описаны в [руководстве по установке](docs/MCP_DISTRIBUTION_PLAN.md).

### Требования

| | |
|---|---|
| Navisworks | Autodesk Navisworks Manage 2024, 2025, 2026 или 2027, Windows x64 |
| Компонент Microsoft | .NET 9 Runtime; если его нет, установщик предложит скачать |
| AI-программа | любая, которая умеет подключать MCP-серверы: Claude Code, Claude Desktop, Codex, Cursor и другие. Для работы с панелью без AI не нужна |

### Проверка файла

Для тех, кто хочет убедиться, что установщик не повреждён и совпадает с опубликованным.

<details>
<summary>Скачать, проверить SHA256 и запустить из PowerShell</summary>

Выполните три блока в одной сессии PowerShell:

```powershell
$release = Invoke-RestMethod `
  "https://api.github.com/repos/mikhalchankasm/NavisWorksMaster/releases/latest"
$installerAsset = $release.assets |
  Where-Object name -Like "NavisHelperSetup-*.exe" |
  Select-Object -First 1
$checksumsAsset = $release.assets |
  Where-Object name -Like "SHA256SUMS*.txt" |
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
  Where-Object { $_ -match "^\s*[0-9A-Fa-f]{64}\s+\*?$([regex]::Escape($installerAsset.name))\s*$" } |
  Select-Object -First 1
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

</details>

## Безопасность

- **Всё локально.** NavisHelper не открывает сетевых портов, AI-программа связывается с Navisworks только на вашем компьютере.
- **Сначала показ, потом действие.** Большинство изменяющих команд по умолчанию показывают, что будет сделано, и выполняются только с явным подтверждением.
- **Защищённый режим.** Если включить его в настройках панели, любое изменение документа агентом ждёт вашего «Разрешить».

Подробнее — в [безопасности и совместимости](docs/SECURITY_AND_COMPATIBILITY.md).

## Документация

| Если нужно | Читайте |
|---|---|
| Начать работу с AI | [Быстрый старт](docs/NAVISWORKS_MCP_QUICKSTART.md) |
| Готовые сценарии | [Пользовательские сценарии](docs/USER_WORKFLOWS.md) |
| Настроить AI-программы | [Руководство по клиентам](docs/MCP_CLIENT_GUIDE.md) |
| Что нового | [Выпуски](https://github.com/mikhalchankasm/NavisWorksMaster/releases) |
| Подробности о продукте, сравнение, проверки | [Подробности о проекте](docs/reference/PROJECT_DETAILS.ru.md) |

## Для разработчиков

В исходниках зарегистрировано **117 разных MCP-инструментов**. `scripts/check_mcp_command_catalog.py` выводит их имена из 117 методов `[McpServerTool]` и проверяет сгенерированный индекс из 117 строк. Входы, выходы и ограничения каждого описаны в [контрактах инструментов](docs/MCP_TOOL_CONTRACTS.md), устройство — в [архитектуре](docs/MCP_ARCHITECTURE.md). Сборка и правила вклада — в [Contributing](CONTRIBUTING.md) и [правилах сборки](BUILD_BUNDLE_RULES.md).

## Лицензия и статус

Код распространяется по [MIT License](LICENSE), сторонние уведомления — в [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Проект поддерживается с низкой активностью: pull requests приветствуются, ответы на issues не гарантируются.

<sub>Autodesk и Navisworks являются зарегистрированными товарными знаками или товарными знаками Autodesk, Inc. и/или её дочерних и аффилированных компаний. NavisHelper — независимый проект; он не связан с Autodesk, не авторизован, не одобрен и не спонсируется Autodesk, Inc. См. официальный [список товарных знаков](https://www.autodesk.com/company/legal-notices-trademarks/intellectual-property/trademarks) и [правила для совместимых продуктов](https://www.autodesk.com/company/legal-notices-trademarks/trademarks/guidelines-for-use).</sub>
