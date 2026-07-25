# Section Box Research for Navisworks 2026

Date: 2026-03-28

## Goal

Нужно программно расширять `Section Box` после `FIT_SELECTION`, чтобы в превью коллизии был виден контекст вокруг пересечения, а не только сами конфликтующие элементы.

## Confirmed Findings

### 1. Менять ribbon textbox напрямую не нужно

Подход с `NWRibbonFloatTextBox` для `RoamerGUI_OM_SECTION_X_SIZE`, `RoamerGUI_OM_SECTION_Y_SIZE`, `RoamerGUI_OM_SECTION_Z_SIZE` оказался тупиковым:

- изменение `Text` меняет UI-состояние, но не гарантирует применение значения к 3D-сцене;
- публичного и стабильного API уровня "commit like Enter" для этих контролов не найдено;
- даже если такой internal путь удастся найти, он будет хрупким и version-specific.

Вывод: ribbon надо использовать только для `Mode_Box` и `FIT_SELECTION`, но не для численного resize.

### 2. В managed API реально есть путь через clipping planes

Локальная проверка установленных сборок Navisworks 2026 подтвердила наличие:

- `Autodesk.Navisworks.Api.View.GetClippingPlanes()`
- `Autodesk.Navisworks.Api.View.SetClippingPlanes(string)`
- `Autodesk.Navisworks.Api.View.TrySetClippingPlanes(string)`

Это главный рабочий путь на стороне .NET API для чтения и записи состояния section/clipping.

### 3. В COM API тоже есть прямые объекты для section/clipping

Локальная рефлексия `Autodesk.Navisworks.Interop.ComApi.dll` подтвердила:

- `InwOpState10.CurrentView`
- `InwOpAnonView.ClippingPlanes()`
- `InwClippingPlaneColl2.GetRange()`
- `InwClippingPlaneColl2.SetRange(...)`
- `InwClippingPlaneColl2.CreatePlane()`
- `InwOaClipPlane.Alignment`
- `InwOaClipPlane.Enabled`
- `InwOaClipPlane.Plane`
- `InwOaClipPlane.BaseDistance`
- `InwLBox3f.min_pos / max_pos`

Вывод: COM fallback существует и пригоден для более устойчивого управления box/range, если string-based JSON путь станет недостаточно надёжным.

## Final Repository Status

### Что реализовано

Текущее решение в репозитории использует следующий pipeline:

1. internal command для `Mode_Box`;
2. internal command для `FIT_SELECTION`;
3. чтение clipping state через `GetClippingPlanes()`;
4. расширение `Box` на заданный offset;
5. применение через `TrySetClippingPlanes()` с fallback на `SetClippingPlanes()`.

Сопутствующие доработки:

- `Disable()` стал идемпотентным и больше не использует `Toggle()` как fallback;
- offset конвертируется из миллиметров в units активного документа через `MmToDocUnits()`;
- preview использует только один механизм расширения box, без добавления соседей в selection;
- `SetSectionBox()` пытается сохранить текущий `Rotation`;
- для preview добавлены выбор цветов A/B и опциональная прозрачность контекста;
- reset восстанавливает прежний визуальный цвет и прозрачность затронутых элементов.

### Что считается закрытым

Следующие ранее найденные баги закрыты:

- некорректный fallback у `TrySetClippingPlanes`;
- reset, который мог включить section box через `Toggle()`;
- жёсткая предпосылка "мм -> метры";
- двойное расширение контекста;
- использование `GetHashCode()` как identity-маркера;
- потеря rotation при `SetSectionBox()`;
- неснятая прозрачность контекста при reset.

## Remaining Tradeoff

### Override-state vs visual restore

Единственный оставшийся спорный момент связан не с ошибкой реализации, а с ограничениями Navisworks API.

Для permanent overrides в текущем контуре практически доступны:

- `OverridePermanentColor(...)`
- `OverridePermanentTransparency(...)`
- глобальный `ResetAllPermanentMaterials()`

При этом нет точечного API уровня:

- `RemovePermanentColorOverride(item)`
- `RemovePermanentTransparencyOverride(item)`

Следствие:

- невозможно точно вернуть элемент в семантическое состояние "override отсутствует" без глобального reset по всей модели;
- можно только восстановить прежний визуальный результат точечным повторным override.

Текущее решение выбирает именно этот компромисс:

1. перед preview сохранить видимый permanent color / transparency;
2. после preview записать эти значения обратно только для затронутых элементов.

Это не восстанавливает исходную "чистоту" override-state, но:

- восстанавливает то, что пользователь визуально видел до preview;
- не ломает цвета/материалы остальных элементов модели;
- избегает разрушительного глобального reset.

Для данного API это считается оптимальным инженерным компромиссом.

## What Can Still Be Improved

### 1. Парсинг clipping JSON остаётся строковым

Сейчас `Rotation`, `Enabled` и `Box` извлекаются через:

- `IndexOf`
- `Substring`
- `Replace`
- `Split`

Это уже рабочее решение, но оно остаётся чувствительным к изменению формата JSON serialization в будущих версиях Navisworks.

Возможные улучшения:

- DTO-модель для clipping JSON;
- переход на COM `GetRange()/SetRange()`.

Это уже hardening, а не blocker.

### 2. Solution build не всегда лучший индикатор для `NavisHelper.Dev`

Практически полезно держать отдельную проверку `NavisHelper.Dev.csproj`, потому что solution build не всегда явно отражает этот проект в выводе, а иногда может упираться в временный file lock в `obj`.

## Recommended Implementation Strategy

### Recommended Path A: managed clipping API

Использовать `View.GetClippingPlanes()` и `TrySetClippingPlanes()` как основной путь.

Практические правила:

1. `Mode_Box` + `FIT_SELECTION` оставить.
2. Считать clipping JSON.
3. Аккуратно увеличить `Box` на offset.
4. Если `TrySetClippingPlanes(...) == false`, вызвать `SetClippingPlanes(...)`.
5. Выполнить `RequestDelayedRedraw(ViewRedrawRequests.All)`.

Это лучший баланс между простотой и контролем.

### Recommended Path B: COM range API

Если JSON-формат окажется нестабилен или неудобен для поддержки, перейти на COM:

1. взять `ComApiBridge.State`;
2. привести к `InwOpState10`;
3. получить `CurrentView`;
4. получить clipping collection;
5. прочитать текущий range через `GetRange()`;
6. расширить `min_pos/max_pos`;
7. записать назад через `SetRange(...)`.

Плюсы:

- меньше зависимости от string JSON shape;
- ближе к модели данных Navisworks;
- легче отлаживать как состояние section planes.

Минусы:

- выше стоимость interop-кода;
- больше ручной обвязки.

## Suggested Next Cleanup in This Repo

1. Если helper остаётся на JSON-path, переписать парсинг clipping JSON на DTO вместо `Substring/IndexOf`.
2. При необходимости усилить устойчивость через COM `GetRange/SetRange`.
3. Решить, должен ли `SetSectionBox(BoundingBox3D box)` быть публичным helper API или внутренней utility-функцией.

## Verification Notes

Проверено локально в установленном окружении Navisworks 2026:

- `Autodesk.Navisworks.Api.dll`
- `Autodesk.Navisworks.ComApi.dll`
- `Autodesk.Navisworks.Interop.ComApi.dll`
- `navisworks.gui.roamer.dll`

На финальном состоянии:

- `dotnet build NavisHelper.sln -c Debug -p:Platform=x64` проходит;
- отдельная сборка `NavisHelper.Dev.csproj` может упираться в временный file lock в `obj`, но это не выглядело как ошибка кода.

## Sources

- Autodesk forum: https://forums.autodesk.com/t5/navisworks-api-forum/enable-section-box-by-using-the-api/td-p/4966628
- Autodesk forum: https://forums.autodesk.com/t5/navisworks-api-forum/sectioning-in-navisworks-manage-2024-api-not-available/td-p/13368952
- Autodesk forum: https://forums.autodesk.com/t5/navisworks-api/get-current-section-plane-or-section-box/td-p/13695354
- Navisworks API docs mirror: https://apidocs.co/apps/navisworks/2017/M_Autodesk_Navisworks_Api_View_SetClippingPlanes_1_bb3a7a4f.htm
- Local reflection of installed assemblies in `C:\Program Files\Autodesk\Navisworks Manage 2026\`
