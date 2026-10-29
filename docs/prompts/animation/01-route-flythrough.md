# Облёт вдоль трассы

Вид на каждый элемент трассы (прибор КИП, задвижку, участок трубы) с плоскостью сечения над ним, затем анимация, которая перелетает от элемента к элементу с остановками, и выгрузка в кадры или видео.

Что подставить: как найти элементы (свойство и значение), имя папки и анимации, качество выгрузки.

```text
Сделай облёт вдоль трассы в открытой модели Navisworks.

1. Найди элементы трассы: <категория/свойство, например Item / Name> <условие, например начинается с> "<значение, например TT-101>".
   Покажи, сколько нашлось и первые 10 имён. Если элементов больше 50 или они не похожи на трассу, остановись и спроси.
2. Сделай по одной точке обзора на каждый элемент через viewpoints_per_item:
   папка "<Маршрут/Трасса 1>", камера iso, сечение plane на 100 мм выше элемента, запас вокруг 2000 мм, отметка target.
   Сначала пробный прогон: покажи имена и конфликты. Запиши только после моего подтверждения.
3. Создай анимацию "<Облёт трассы 1>" через viewpoint_animation из этой папки: остановка 2 с на каждом элементе, сглаживание sync.
   Сначала покажи план кадров и пауз, потом создай.
4. Выгрузи анимацию через viewpoint_animation_export в PNG, черновик: 640×360, 10 кадров/с, сглаживание 2.
   Сообщи, сколько кадров и где они лежат.
5. Если есть ffmpeg, предложи собрать из кадров MP4 (H.264, yuv420p).

Документ не сохраняй. Ничего с тем же именем не перезаписывай без моего согласия.
```

```text
Make a fly-through along a route in the open Navisworks model.

1. Find the route items: <category/property, e.g. Item / Name> <condition, e.g. starts with> "<value, e.g. TT-101>".
   Show the count and the first 10 names. If there are more than 50 or they do not look like a route, stop and ask.
2. Save one viewpoint per item with viewpoints_per_item:
   folder "<Route/Route 1>", camera iso, plane section 100 mm above the item, 2000 mm context, target mark.
   Preview first (names and conflicts); save only after I confirm.
3. Create the animation "<Route 1 fly-through>" with viewpoint_animation from that folder: hold 2 s at each item, smoothing sync.
   Show the frame and pause plan first, then create it.
4. Export it with viewpoint_animation_export to PNG, draft quality: 640×360, 10 fps, anti-alias 2.
   Report the frame count and the folder.
5. If ffmpeg is available, offer to encode the frames to MP4 (H.264, yuv420p).

Do not save the document. Do not overwrite anything with the same name without my consent.
```

## Как подстроить

- **Порядок элементов.** Точки обзора идут в порядке, в котором их вернул поиск, а не по ходу трассы. Если порядок важен, попросите агента переставить их в папке (`saved_viewpoints_reorder`) до создания анимации, или перечислите имена по порядку и создайте анимацию из списка `viewpoints`.
- **Вид сверху вместо изометрии:** камера `top`. Фасад: `north`, `south`, `east`, `west`.
- **Без сечения:** сечение `none`. Рамкой вокруг элемента: `box` и `boxOffsetMm`.
- **Длиннее остановка:** `holdSeconds`; паузы выдерживаются точно.
- **Время перелёта** `stepSeconds` в Navisworks 2027 не соблюдается: перелёт идёт быстрее или медленнее, в зависимости от расстояния и поворотов ([#421](https://github.com/mikhalchankasm/NavisWorksMaster/issues/421)). Если нужна точная длительность, её подгоняют при сборке видео из кадров.
- **Качество:** пресеты в [README](README.md#пресеты-качества).
