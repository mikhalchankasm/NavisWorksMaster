# Обход с подрезами

Каждый кадр показывает элемент внутри секущей рамки: всё вокруг на заданном расстоянии отрезано. Подходит, чтобы показать узлы внутри плотной компоновки: оборудование в помещении, узел обвязки, проходку через перекрытие.

Что подставить: какие элементы, отступ рамки, камера.

```text
Сделай обход узлов с подрезами в открытой модели Navisworks.

1. Найди элементы: <категория/свойство> <условие> "<значение>". Покажи количество и первые 10 имён; если больше 30, спроси, какие оставить.
2. Через viewpoints_per_item сделай по точке обзора на каждый элемент:
   папка "<Подрезы/Узлы>", сечение box с отступом 1000 мм, камера <iso или top>, запас вокруг 1500 мм, отметка target.
   Сначала пробный прогон, запись — после моего подтверждения.
3. Через viewpoint_animation создай анимацию "<Обход узлов>" из этой папки: остановка 3 с на каждом узле, сглаживание sync.
4. Выгрузи через viewpoint_animation_export в PNG, качество «для показа»: 1280×720, 25 кадров/с, сглаживание 4.
5. Если есть ffmpeg, предложи собрать MP4.

Документ не сохраняй. Ничего не перезаписывай без моего согласия.
```

```text
Make a section-box tour of nodes in the open Navisworks model.

1. Find the items: <category/property> <condition> "<value>". Show the count and the first 10 names; if more than 30, ask which to keep.
2. With viewpoints_per_item save one viewpoint per item:
   folder "<Sections/Nodes>", box section with 1000 mm offset, camera <iso or top>, 1500 mm context, target mark.
   Preview first; save after I confirm.
3. With viewpoint_animation create "<Node tour>" from that folder: hold 3 s at each node, smoothing sync.
4. Export with viewpoint_animation_export to PNG, presentation quality: 1280×720, 25 fps, anti-alias 4.
5. If ffmpeg is available, offer to encode an MP4.

Do not save the document. Do not overwrite anything without my consent.
```

## Как подстроить

- **Рамка теснее или шире:** `boxOffsetMm`. Плоскость над элементом вместо рамки: сечение `plane` и `planeOffsetMm`.
- **Группа элементов в одной рамке.** `viewpoints_per_item` делает рамку вокруг каждого элемента. Для рамки вокруг группы есть `section_box_viewpoint` (по текущему выделению, с кластеризацией), её виды тоже можно собрать в анимацию: см. [04-from-my-viewpoints.md](04-from-my-viewpoints.md).
- При проигрывании сечение каждого кадра сохраняется: это проверено в Navisworks 2027.
