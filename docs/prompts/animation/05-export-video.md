# Выгрузка и сжатие видео

Готовая анимация уже есть в документе (создана сценарием 1–4 или вручную). Этот сценарий выгружает её в нужном качестве и сжимает в MP4 или GIF.

Что подставить: имя анимации, пресет, формат результата.

```text
Выгрузи анимацию "<Облёт трассы 1>" из открытой модели Navisworks в видео.

1. Через viewpoint_animation_export без apply проверь анимацию и папку выгрузки.
2. Выгрузи в PNG с качеством <черновик 640×360, 10 кадров/с, сглаживание 2 | для показа 1280×720, 25 кадров/с, сглаживание 4 | высокое 1920×1080, 30 кадров/с, сглаживание 8>.
   Если кадры этой анимации в папке уже есть, спроси, можно ли их перезаписать.
3. Сообщи, сколько кадров, их общий размер и папку. Проверь, что формат файлов совпал (formatMatches) и что в ответе нет предупреждений; если есть, перескажи их.
4. Если есть ffmpeg, собери MP4: H.264, crf 23, yuv420p, faststart, с той же частотой кадров. Сообщи размер MP4.
   Если я попрошу превью, сделай ещё GIF шириной 640 и 10 кадров/с.
5. Кадры не удаляй, пока я не проверю видео.
```

```text
Export the animation "<Route 1 fly-through>" from the open Navisworks model to video.

1. Call viewpoint_animation_export without apply to check the animation and the output folder.
2. Export to PNG at <draft 640×360, 10 fps, anti-alias 2 | presentation 1280×720, 25 fps, anti-alias 4 | high 1920×1080, 30 fps, anti-alias 8>.
   If frames of this animation already exist in the folder, ask before overwriting them.
3. Report the frame count, total size and folder. Check that the file format matched (formatMatches) and relay any warnings.
4. If ffmpeg is available, encode an MP4: H.264, crf 23, yuv420p, faststart, at the export frame rate. Report the MP4 size.
   If I ask for a preview, also make a GIF 640 wide at 10 fps.
5. Do not delete the frames until I have checked the video.
```

## Как подстроить

- **Сразу AVI** (`format` = `avi`): один файл без сжатия, большой; для отправки его всё равно лучше пережать в MP4.
- **JPG** вместо PNG: кадры меньше, качество чуть ниже; для MP4 разницы почти нет.
- Команды ffmpeg — в [README](README.md#постобработка).
- После выгрузки в другом формате диалог Animation → Export Animation в Navisworks предложит этот формат: вернуть прежний через API нельзя, инструмент сообщает об этом предупреждением.
