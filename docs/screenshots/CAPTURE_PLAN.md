# Сценарий съёмки NavisHelper

Этот файл нужен владельцу для получения подлинных изображений продукта. На
2026-08-08 скриншоты не создавались: в доступном Navisworks Manage 2027 была
открыта модель с негенерическим именем, которую нельзя считать безопасной
тестовой моделью без подтверждения владельца.

## Privacy gate

Использовать только синтетическую модель `NavisHelper_Demo.nwd`:

- 20–40 простых объектов с нейтральными именами `HVAC-01`, `PIPE-01`,
  `STRUCTURE-01` и свойством `System = HVAC | Electrical | Structure`;
- 2–3 заранее созданные коллизии в тесте `DEMO - HVAC vs Structure`;
- selection set `NavisHelper Demo/Section Items` из 3–6 объектов;
- никаких клиентских моделей, логотипов, имён людей, адресов, реальных кодов
  проекта, сетевых путей или истории Recent Files.

Перед съёмкой закрыть все клиентские документы, открыть только
`NavisHelper_Demo.nwd` и вызвать read-only `active_model_context`. Продолжать,
только если каждый root filename нейтрален. Если в ответе или заголовке окна
есть другое имя, остановиться. Окно Navisworks развернуть, масштаб Windows
поставить 100%, оставить нейтральный серый фон. Формат всех кадров — PNG,
профиль `fullhd`. Это бюджет съёмки, а не измеренная производительность:
четыре кадра и финальная проверка рассчитаны суммарно на 6 минут; privacy
gate выполняется отдельно.

## 1. Раскраска модели — 60 секунд

Поставить изометрическую камеру и показать всю модель. Сначала выполнить
dry-run `model_color_scheme`, проверить ненулевые `matchedItemCount` и
отсутствие truncation, затем повторить с `apply=true`:

```json
{
  "operation": "apply",
  "scope": "model",
  "apply": false,
  "rules": [
    { "name": "HVAC", "colorHex": "#55BDEB", "propertyContains": ["System"], "propertyValueContains": ["HVAC"] },
    { "name": "Electrical", "colorHex": "#FFD84D", "propertyContains": ["System"], "propertyValueContains": ["Electrical"] },
    { "name": "Structure", "colorHex": "#9AA0A6", "propertyContains": ["System"], "propertyValueContains": ["Structure"] }
  ]
}
```

Сделать preview `capture_current_view`, затем повторить с `apply=true`:

```json
{
  "outputPath": "D:\\GitHub\\NavisWorksMaster-public\\docs\\screenshots\\01-model-color-scheme.png",
  "screenshotProfile": "fullhd",
  "screenshotFormat": "png",
  "overwrite": true,
  "apply": false
}
```

В кадре: вся тестовая модель, три хорошо различимые системы, без selection
highlight и без обрезанных крайних объектов.

## 2. Clash report — 120 секунд

Read-only вызовами `clash_list_tests` и `clash_list_results` убедиться, что
тест `DEMO - HVAC vs Structure` содержит только синтетические объекты. Затем
вызвать `clash_generate_report` с `apply=false`, проверить scope и повторить с
`apply=true`:

```json
{
  "apply": false,
  "testName": "DEMO - HVAC vs Structure",
  "limit": 3,
  "outputDirectory": "D:\\Temp\\NavisHelperDemo\\clash-report",
  "overwrite": true,
  "runTests": false,
  "boxMode": "items",
  "boxOffsetMm": 500,
  "colorAHex": "#FF2626",
  "colorBHex": "#2666FF",
  "createViewpoints": true,
  "captureScreenshots": true,
  "includeClashPointMarker": true,
  "screenshotProfile": "fullhd",
  "screenshotFormat": "png"
}
```

Открыть созданный `report.html`, проверить нейтральность всех названий и
скопировать лучший штатный screenshot в
`docs/screenshots/02-clash-report.png`. В кадре: красная и синяя стороны
коллизии, видимый section box, маркер точки и немного контекста; сама точка
коллизии не должна быть закрыта панелью или selection highlight.

## 3. Section Box viewpoint — 60 секунд

Выбрать set `NavisHelper Demo/Section Items`. Выполнить
`section_box_viewpoint` сначала с `apply=false`, проверить planned viewpoint,
затем повторить с `apply=true`:

```json
{
  "name": "DEMO - Section Box",
  "folderPath": "NavisHelper Demo",
  "boxOffsetMm": 500,
  "markStyle": "target",
  "targetCrosshair": true,
  "ellipseColor": [1, 0, 0],
  "thickness": 3,
  "overwrite": true,
  "apply": false
}
```

Активировать созданную точку через `activate_saved_viewpoint` (сначала
preview, затем apply) и записать вид через `capture_current_view` в
`docs/screenshots/03-section-box-viewpoint.png`. В кадре: ортографический ISO,
границы сечения, выбранный узел целиком и красный target; скрытие или
изоляция модели не должны подменять section box.

## 4. Persistent markup viewpoint — 45 секунд

На тех же синтетических объектах выполнить `markup_selection` dry-run, затем
apply:

```json
{
  "name": "DEMO - Markup",
  "folderPath": "NavisHelper Demo",
  "autoTopView": true,
  "fitToSelection": true,
  "fitMarginFactor": 0.15,
  "markStyle": "rectangle",
  "arrowCallout": true,
  "ellipseColor": [1, 0, 0],
  "thickness": 3,
  "overwrite": true,
  "apply": false
}
```

Активировать saved viewpoint и вызвать `capture_current_view` для
`docs/screenshots/04-persistent-markup-viewpoint.png`. В кадре: строгий top
view, все выбранные элементы, красные прямоугольные frames и line-based arrow
callouts. Стрелки не должны перекрывать геометрию.

## Финальная проверка — 75 секунд

Для каждого PNG проверить: 1920×1080 или меньше без upscale, читаемую модель,
отсутствие клиентских идентификаторов и абсолютных путей, а также соответствие
реальному результату команды. Открыть все четыре изображения локально перед
commit. Коммитить только выбранные PNG; временный clash report, HTML/JSON,
NWD/NWF и build artifacts в PR не добавлять. Demo GIF в этот проход не делать.
