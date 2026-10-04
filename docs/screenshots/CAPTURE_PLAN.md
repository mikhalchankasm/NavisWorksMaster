# Сценарий съёмки NavisHelper

Три подлинных кадра и короткий GIF показывают результат команд: раскраску
модели, сохранённую коллизию и Section Box viewpoint. GIF — монтаж состояний
реального viewport, а не запись длительности выполнения команд.

## Подготовка модели и окна съёмки

Согласовать окно живой проверки. Использовать отдельный экземпляр Navisworks
Manage 2027 с английским UI; существующие окна и документы владельца не трогать.
Сначала выполнить `python scripts/check_installed_bundle_drift.py`.

Создать модель вне репозитория:

```powershell
python -m pip install ezdxf==1.4.4
python tools/create_demo_model.py "$env:TEMP\NavisHelperDemo\NavisHelper_Demo.dxf"
```

Генератор отказывается заменять существующий файл. Он создаёт 24 замкнутых
box mesh: по восемь HVAC, Electrical и Structure, с единицами mm и нейтральными
именами блоков. Системы представлены слоями DXF, а не свойством `System`.
После записи проверяются имена, вершины, размеры и два задуманных пересечения:
HVAC-01/STRUCTURE-02 и HVAC-02/STRUCTURE-03. Это проверка геометрии генератора;
фактические результаты Clash Detective проверяются отдельно.

Открыть этот DXF в созданном для съёмки экземпляре, сохранить как
`NavisHelper_Demo.nwd` во временном каталоге. Через `list_navisworks_hosts`
зафиксировать `instanceId` и передавать его всем последующим вызовам. Проверить
`mcp_health_check` и `active_model_context`: все root filenames должны быть
нейтральными. Не использовать клиентские модели, чужие логотипы, имена людей,
реальные коды проекта или историю Recent Files.

Через ограниченные `find_items`/`list_item_children` проверить реальную структуру
импортера. Создать статические selection sets `NavisHelper Demo/HVAC` и
`NavisHelper Demo/Structure`, по восемь именованных объектов. В set
`NavisHelper Demo/Section Items` включить два пересекающихся воздуховода и две
соответствующие колонны. Проверять preview и результат каждой мутации.

Создать тест `DEMO - HVAC vs Structure` через `clash_tests_from_sets`: стороны —
эти два набора, `testType=hard`, `toleranceMm=0`, `ignoreRules.sameFile=false`.
Разрешается запуск только этого синтетического теста. Дождаться завершения,
проверить два результата и их участников, сохранить NWD. Эта новая fixture
не подменяет согласованную владельцем модель в других задачах.

## 1. Раскраска модели

Запрос: «Раскрась HVAC голубым, Electrical жёлтым, Structure серым».
Поставить изометрическую камеру и показать модель целиком. Для
`model_color_scheme` использовать `operation=apply`, `scope=model`, лимит
обхода 1000 и следующие правила, после проверки фактических путей импортера:

```json
[
  { "name": "HVAC", "colorHex": "#55BDEB", "pathContains": ["HVAC"] },
  { "name": "Electrical", "colorHex": "#FFD84D", "pathContains": ["Electrical"] },
  { "name": "Structure", "colorHex": "#9AA0A6", "pathContains": ["Structure"] }
]
```

Сначала `apply=false`: у каждой системы восемь geometry items, truncation
отсутствует. Затем `apply=true`; проверить результат. Снять selection highlight.
Через preview/apply `capture_current_view` сохранить
`01-model-color-scheme.png`: профиль `fullhd`, формат `png`, нейтральный серый
фон. В кадре — вся модель и три различимые системы, без обрезанных объектов.

## 2. Сохранённая коллизия и отчёт

Запрос: «Покажи сохранённые коллизии HVAC со Structure и создай отчёт».
Через `clash_list_tests`/`clash_list_results` проверить точный тест и участников.
Вызвать `clash_generate_report` сначала с `apply=false`, проверить scope и пути,
затем с `apply=true`:

```json
{
  "testName": "DEMO - HVAC vs Structure",
  "limit": 2,
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

Дополнить запрос абсолютным `outputDirectory` вне репозитория и `instanceId`.
Не заменять существующий отчёт без проверки его происхождения. Открыть
`report.html`, проверить два результата, нейтральность названий и файлы снимков.
Лучший штатный screenshot сохранить как `02-clash-report.png`. Проверить
красную/синюю стороны, границы сечения и видимость маркера точки.

## 3. Section Box viewpoint

Запрос: «Сохрани вид выбранного узла с запасом 500 mm и меткой».
Выбрать set `NavisHelper Demo/Section Items`; через `selected_items_preview`
проверить четыре ожидаемых объекта. Выполнить `section_box_viewpoint`
preview/apply с `name=DEMO - Section Box`, `folderPath=NavisHelper Demo`,
`boxOffsetMm=500`, `markStyle=target`, `targetCrosshair=true`, красной меткой
и `thickness=3`. Активировать точное saved viewpoint через preview/apply
`activate_saved_viewpoint`, проверить его и clipping box чтением состояния.

Через `capture_current_view` сохранить `03-section-box-viewpoint.png`. В кадре —
выбранный узел целиком, сечение и метка; скрытие модели не заменяет Section Box.

## Проверка и публикация

Открыть все три PNG перед commit: каждый не больше 1920×1080 без upscale,
геометрия читаема, нет приватных идентификаторов или абсолютных путей.
Собрать `navishelper-demo.gif` из этих подлинных кадров в том же порядке,
уменьшив до ширины не больше 960 px с сохранением пропорций, по четыре секунды
на состояние. Не дорисовывать результат или элементы UI. Подпись в EN/RU README
должна прямо называть GIF монтажом состояний, а не замером скорости.

Общий бюджет четырёх media-файлов — 8 MB. README содержит GIF, ссылки на три
исходных кадра и [подробные сценарии](../USER_WORKFLOWS.md). В PR фиксируются
версия/host, проверки модели, реальные counts, команды и ограничения L3.
Не коммитить DXF/NWD/NWF, временный HTML/JSON отчёт и журналы. Сохранить demo NWD,
затем закрыть только созданный для съёмки экземпляр Navisworks.
