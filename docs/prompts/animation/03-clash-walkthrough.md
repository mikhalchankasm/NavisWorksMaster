# Проход по коллизиям

Вид на каждую коллизию теста Clash Detective (рамка вокруг точки коллизии, стороны A и B разными цветами), затем анимация с паузой на каждой коллизии. Подходит для разбора на совещании.

Что подставить: имя теста, статусы, сколько коллизий взять.

```text
Сделай видео-проход по коллизиям в открытой модели Navisworks.

1. Покажи тесты Clash Detective и число коллизий New/Active в тесте "<имя теста>".
2. Через clash_save_viewpoints сохрани виды для первых <20> коллизий этого теста со статусами New и Active:
   папка "<Коллизии/Тест 1>", рамка вокруг точки коллизии 1500 мм, маркер точки коллизии.
   Сначала пробный прогон: покажи имена видов. Запись — после моего подтверждения.
3. Через viewpoint_animation создай анимацию "<Коллизии тест 1>" из этой папки: остановка 4 с на каждой коллизии, сглаживание sync.
   Первый вид в папке — общий вид модели, который clash_save_viewpoints создаёт сам; оставь его как начало ролика.
4. Выгрузи через viewpoint_animation_export в PNG, 1280×720, 25 кадров/с, сглаживание 4.
5. Если есть ffmpeg, предложи собрать MP4.

Тесты не перезапускай, статусы коллизий не меняй, документ не сохраняй.
```

```text
Make a video walkthrough of clashes in the open Navisworks model.

1. List the Clash Detective tests and the New/Active count in "<test name>".
2. With clash_save_viewpoints save viewpoints for the first <20> New and Active clashes of that test:
   folder "<Clashes/Test 1>", 1500 mm box around the clash point, clash point marker.
   Preview the names first; save after I confirm.
3. With viewpoint_animation create "<Test 1 clashes>" from that folder: hold 4 s at each clash, smoothing sync.
   The first viewpoint in the folder is the overall view that clash_save_viewpoints adds; keep it as the opening shot.
4. Export with viewpoint_animation_export to PNG, 1280×720, 25 fps, anti-alias 4.
5. If ffmpeg is available, offer to encode an MP4.

Do not rerun tests, change clash statuses or save the document.
```

## Как подстроить

- **Две стороны каждой коллизии:** `createOppositeViewpoints` даёт по два вида на коллизию с противоположных сторон.
- **Рамка по элементам, а не по точке:** `boxMode` = `items`.
- **Другие статусы:** `statusFilters`, например только `Active`.
- **Больше коллизий:** `limit` и постраничный `resultOffset`. Длинный ролик — это много кадров: сначала проверьте черновик 640×360.
