# Анимация из своих видов

Когда точки обзора уже есть (сохранены вручную, импортированы или созданы другими инструментами), из них собирается анимация в нужном порядке, с паузами и повторами.

Что подставить: папку или список видов, паузы.

```text
Собери анимацию из моих сохранённых точек обзора в открытой модели Navisworks.

1. Покажи дерево сохранённых точек обзора (list_saved_viewpoints) и найди папку "<Моя папка>".
2. Через viewpoint_animation создай анимацию "<Мой ролик>" <из всех видов папки "<Моя папка>" по порядку | из видов в таком порядке: "<вид 1>", "<вид 2>", "<вид 3>">.
   Остановка <2> с на каждом виде; если вид повторяется подряд, это пауза <3> с. Сглаживание sync.
   Сначала покажи план кадров и пауз, создай после моего подтверждения.
3. Выгрузи через viewpoint_animation_export в PNG, черновик 640×360, 10 кадров/с.
4. Если есть ffmpeg, предложи собрать MP4.

Документ не сохраняй. Анимацию с тем же именем не перезаписывай без моего согласия.
```

```text
Build an animation from my saved viewpoints in the open Navisworks model.

1. Show the saved viewpoint tree (list_saved_viewpoints) and find the folder "<My folder>".
2. With viewpoint_animation create "<My clip>" <from every viewpoint of "<My folder>" in order | from these viewpoints in this order: "<view 1>", "<view 2>", "<view 3>">.
   Hold <2> s at each view; a view repeated in a row is a <3> s pause. Smoothing sync.
   Show the frame and pause plan first; create it after I confirm.
3. Export with viewpoint_animation_export to PNG, draft 640×360, 10 fps.
4. If ffmpeg is available, offer to encode an MP4.

Do not save the document. Do not overwrite an animation with the same name without my consent.
```

## Как подстроить

- **Пауза на виде:** повторите вид в списке два раза подряд, это даст паузу длиной `pauseSeconds`. Или задайте `holdSeconds` для остановки на каждом виде.
- **Повтор ролика по кругу:** `loops`.
- **Вложенные папки** в `sourceFolder` не попадают: берутся только виды самой папки. Для видов из разных папок используйте список `viewpoints`.
- Каждый кадр — копия вида: сечение, цвета и пометки сохраняются, исходные виды не меняются. Скрытые элементы вид хранит, только если в Navisworks включено сохранение скрытия в точках обзора (Options → Interface → Viewpoint Defaults → Save Hide/Required Attributes); иначе при проигрывании скрытие не восстанавливается.
