# Редизайн NavisHelper — этап 2

## Результат

Этап 2 унифицирует кнопки внутри вкладок «Модель», «Цвета» и «Виды», не меняя бизнес-логику команд. Ветка реализации: `ui-redesign-stage2`, основана на `ui-redesign-stage1`.

## Чекпоинты

1. `UiTheme` содержит единый плоский `ControlTemplate` и семантику `ButtonKind`: `Neutral`, `Primary`, `Destructive`.
2. Фабрики `Btn`, `NavBtn`, `ActionBtn` и кнопки «Отметок» используют общий стиль.
3. Основные и опасные действия размечены по единой карте; разовые переопределения цвета и жирности удалены.
4. Кнопки действий автоматически подбирают ширину по подписи, а заголовки секций создаются через `CreateGroupHeader`.

## Карта семантики

### Primary

- «Colors By Name»;
- Match Color → «Применить»;
- перенос цветов → «Загрузить»;
- история покрасок → «Применить»;
- «Применить AI-окраску»;
- Section Box → «В выделенные элементы»;
- «Отметка Z» и «Размерная линия до Z».

Primary использует синюю заливку `#2569B4` и белый текст. Контраст текста — не ниже WCAG AA для обычного размера шрифта.

### Destructive

- «Сбросить overrides»;
- `Isolate`;
- Section Box → «Сброс»;
- «Удалить группы» и «Очистить».

Destructive использует красные текст и рамку со светло-красными состояниями hover/pressed. Остальные кнопки — Neutral.

## Осознанно вне этапа

- реальные PNG-иконки вместо эмодзи;
- тёмная тема;
- изменение компоновки и поведения вкладки «Коллизии»;
- изменение обработчиков команд и логики модели.

## Проверка

- `python scripts/check_navishelper_compile.py`;
- `python scripts/check_host_command_router.py`;
- `python scripts/check_mcp_command_catalog.py`;
- сборка `Release2024`, `Release2025`, `Release2026`, `Release2027`, платформа `x64`;
- `dotnet test NavisHelper.McpServer.Tests/NavisHelper.McpServer.Tests.csproj -c Release --no-restore`;
- установка свежего bundle и перезапуск Navisworks 2027;
- визуальная проверка трёх видов кнопок, hover/pressed/focus, длинных подписей и переноса строк на ширине панели около 400 px.

## Слияние

1. Обновить ветку относительно `origin/main`.
2. Запушить `ui-redesign-stage2`.
3. Открыть PR `ui-redesign-stage2` → `main`.
4. После прохождения GitHub Actions выполнить merge без изменения истории четырёх чекпоинтов этапа.
