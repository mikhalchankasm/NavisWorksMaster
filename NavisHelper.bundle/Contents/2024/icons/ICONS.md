# Иконки NavisHelper

Основные кнопки панели используют встроенные монохромные векторные символы из системного шрифта
`Segoe MDL2 Assets`. Цвет символа наследуется от кнопки: белый на Primary, тёмный на Neutral,
красный на Destructive и серый в disabled-состоянии.

## Каталог идентификаторов

- Модель: `parent`, `child`, `sibling`, `leaf`, `all_under`, `selection_invert`,
  `selection_isolate`, `selection_unhide`, `selection_set_prop`, `selection_search_set`,
  `copy_names`, `selection_bounds_info`, `filter`, `selection_save`, `selection_recall`.
- Данные: `csv_import`, `import_ps`, `save_hierarchy`, `save_nwd2018`,
  `export_selected_props`.
- Цвета: `colors_by_name`, `color_by_property`, `override_pdms`, `reset_color_overrides`,
  `match_color_manual`, `match_color_pick`, `match_color_apply`, `export_colors`,
  `import_colors`, `history_select`, `history_apply`, `ai_color`.
- Виды: `markup_viewpoint`, `top_view`, `top_view_bbox`, `top_view_hatch`,
  `selection_center_dot_marker`, `selection_hatch_marker`, `selection_bounds_hatch_marker`,
  `sort_viewpoints`, `save_viewpoints`, `selection_section_show`, `selection_section_reset`.
- Прочее: `dev_run`.

## Обратная совместимость

Для неизвестного идентификатора панель по-прежнему ищет `<id>.png` в этой папке, а при отсутствии
файла показывает переданный Unicode/emoji fallback. PNG должен быть квадратным, с прозрачностью;
рекомендуемый исходный размер — 40×40 px для HiDPI.
