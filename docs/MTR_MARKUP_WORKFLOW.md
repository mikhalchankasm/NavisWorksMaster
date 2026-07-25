# MTR search sets and marked viewpoints

Use this sequential workflow for each MTR position after the target Navisworks model is open. Do not send requests in parallel to the same Navisworks host.

1. Before generating viewpoints, create a self-contained working copy for the MTR handoff:

```text
save_document_as(
  path="D:/MTR/Порт Бухта-Север_МТР.nwd"
)
```

`save_document_as` accepts only an absolute `.nwd` or `.nwf` path and refuses to replace an existing file unless `overwrite=true` is explicit. Use `.nwd` for a handoff containing geometry, Search Sets, and Saved Viewpoints.

2. Create the dynamic Search Set with display property names and select its current matches:

```text
create_search_set(
  name="28245 (КП820)",
  folder_path="MTR/240103-ТХ",
  conditions=[
    { category:"AVEVA", property:"Наименование", operator:"contains", value:"КП820" },
    { category:"Элемент", property:"Файл источника", operator:"equals", value:"240103-ТХ.rvm" }
  ],
  combine_operator="all",
  select_after_create=true,
  apply=true
)
```

Do not send AVEVA custom `category_internal` / `property_internal` values when display names are known: internal IDs can differ between model builds. Inspect `matched_item_count`; when it is zero, do not create a viewpoint until the search condition is corrected.

3. Create the marked plan viewpoint from the resulting current selection:

```text
markup_selection(
  name="28245 (КП820) — план",
  folder_path="MTR/240103-ТХ",
  source="current_selection",
  auto_top_view=true,
  fit_to_selection=true,
  fit_margin_factor=0.10,
  mark_style="target",
  arrow_callout=true,
  arrow_length_mm=0,
  target_crosshair=false,
  mark_solo_min_size_mm=1500,
  mark_merge_gap_mm=1000,
  cluster_max_distance_mm=10000,
  apply=true
)
```

`markup_selection` sets an orthographic top view when `auto_top_view=true`, fits each cluster box with a 10% frame by default (`fit_margin_factor=0.10`), and persists redline markup in the Saved Viewpoint. With `auto_top_view=false`, it preserves the current orthographic or perspective camera and any enabled section box. `mark_style` selects `rectangle` (default), `target` (ellipse), `arrow` (compatibility mode that emits only a three-line arrow), or `hatch` (rectangle plus parallel line fill). Set `arrow_callout=true` to add one three-line arrow to every mark without replacing the selected shape; `target_crosshair=true` opts into the legacy crosshair. `RedlineArrow` is intentionally not emitted because Navisworks `View.SetRedlines` rejects it; XML `<rlarrow>` values are expanded to three supported `RedlineLine` primitives during import. `arrow_length_mm=0` chooses 8% of the final camera `HeightField`; explicit lengths are clamped to 5-15% of the frame height. Sixteen candidate directions are scored against neighbouring marks and the 3% camera safe zone. `min_mark_size_mm` controls the minimum full mark size; `hatch_angle_deg`, `hatch_spacing_mm`, and `hatch_thickness` tune the hatch. Orthographic markup is calculated from the saved camera in document units using the Navisworks quaternion layout `A/B/C=x/y/z`, `D=w`; perspective markup uses `View.ProjectPoint()`, so top, rotated ISO, and perspective cameras remain stable after reopening the model at a different window size. Items whose horizontal bbox is at least `mark_solo_min_size_mm=1500` receive individual marks. Smaller items whose horizontal bbox gaps are at most `mark_merge_gap_mm=1000` are merged into connected components with one mark per component. It never hides or isolates items. Set `fit_margin_factor=0` for a tight fit; the option affects only the plan and does not change `section_box_viewpoint`. Both tools share `cluster_by`: `none`, `distance` with `cluster_max_distance_mm`, `count` with `cluster_target_size`, or plan-grid `grid` with `cluster_grid_size_mm`. `max_clusters` caps the result and `max_items_for_clustering` guards expensive distance clustering. Use `apply=false` first when only the folder/name/selection plan should be reviewed; dry-run deliberately does not alter the current camera or calculate projection geometry.

Run `scripts/navishelper_redline_live_smoke.ps1 -Latest` with Navisworks open and a non-empty selection to verify the real `SetRedlines` path. Add `-SectionBox` to exercise the final-camera clipping workflow. The smoke creates a uniquely named Saved Viewpoint under `NavisHelper Live Smoke`.

If the response reports `skipped_item_count`, those items had no usable bbox or were outside the final camera projection. The saved viewpoint is still created when at least one frame was produced.

4. Create the section-box viewpoint from the same current selection:

```text
section_box_viewpoint(
  name="28245 (КП820) — бокс",
  folder_path="MTR/240103-ТХ",
  source="current_selection",
  box_offset_mm=1500,
  mark_style="target",
  arrow_callout=true,
  arrow_length_mm=0,
  target_crosshair=false,
  cluster_max_distance_mm=10000,
  apply=true
)
```

`section_box_viewpoint` creates the matching cluster set with an enabled Navisworks clipping box plus the requested context. When `mark_style` or `arrow_callout` is supplied, redlines are calculated after the final ISO camera and clipping box are active, and the response reports `mark_count`, `ellipse_count`, and `arrow_count`. The box, camera, and redlines are persisted together in each Saved Viewpoint. It uses no hide or isolation operation; activating the saved viewpoint in a clean session restores the clipping state.

## Bulk generation for an existing MTR folder

When all Search Sets already exist, prefer the neutral `selection_sets_build_viewpoints` tool. It accepts an explicit `folder_prefix`, a `name_template` containing `{set}` and `{step}`, and independent `overview`, `markup`, and `sectionBox` steps. Markup fields including `markStyle`, `arrowCallout`, `arrowLengthMm`, `targetCrosshair`, color, thickness, sizes, and hatch settings are valid on `sectionBox` steps too. The old call below remains available only as a deprecated compatibility alias:

```text
build_mtr_viewpoints(
  folder_prefix="MTR",
  cluster_max_distance_mm=10000,
  fit_margin_factor=0.10,
  mark_style="rectangle",
  mark_solo_min_size_mm=1500,
  mark_merge_gap_mm=1000,
  box_offset_mm=1000,
  apply=false
)
```

For runtime QA, `live_markers(style="target", apply=true)` shows hybrid-group overlay markers that follow the 3D model while the camera moves. Use `live_markers(visible=false, apply=true)` to turn them off. Overlay markers are never persisted; deliverable viewpoints must use `markup_selection`/`build_mtr_viewpoints`.

Review `skipped_empty_set_count`, `truncated`, and each item warning. Then repeat with `apply=true`. The batch runs sequentially in one Navisworks host operation, skips zero-match sets, preserves the original selection, and never hides or isolates model items. It skips an output type if any viewpoint with the planned name already exists, avoiding partial duplicate clusters.

Finish with `save_document()` and verify one `— план` and one `— бокс` after reopening the saved `.nwd`.
