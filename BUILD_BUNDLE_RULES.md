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
5. Обновить этот файл, `README.md` и `CLAUDE.md`
6. Проверить сборку старых поддерживаемых версий, чтобы не словить регрессию
