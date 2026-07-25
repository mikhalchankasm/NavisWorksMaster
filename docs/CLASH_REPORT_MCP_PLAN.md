# Clash Report MCP Implementation Plan

Date: 2026-06-17

## Scope

Build an MCP workflow for existing Navisworks Clash Detective results:

1. Select a clash scope by test name and optional status filters.
2. Optionally run all Clash Detective tests or only the matched report scope before reporting.
3. For each returned clash result, create a section box around the clash center or result item bounding box with a default 1500 mm offset.
4. Apply standard clash colors to the two clashing sides and optional context transparency.
5. Zoom the active view to the clash box.
6. Create one saved viewpoint per clash.
7. Capture a screenshot when the public API/runtime supports it.
8. Write a self-contained artifact folder containing `report.html`, `manifest.json`, and `images/`.

## Voice Input Assumptions

- "Clash Box" and "флэш боксы" mean a section box around the clash point/result.
- "опустить все проверки" means run or process all Clash Detective tests.
- "стандартный цвет, который выбран в системе" is implemented as the current NavisHelper clash convention: side A red and side B blue, with request parameters for overrides.

## MVP Contract

Add write-capable host/MCP commands:

- `clash_generate_report`
- `clash_manage_tests`

Default behavior is dry-run. It returns resolved test/result counts and the planned output folder without changing the model or writing files. With `apply=true`, it creates viewpoints, attempts screenshots, and writes report artifacts.

Key arguments:

- `testName`: empty means all tests.
- `testNames`: optional list of test names; empty with `testName` empty means all tests.
- `statusFilters`: default `New,Active`.
- `includeAllStatuses`: false by default; when true, include all Clash Detective statuses in the selected scope.
- `limit`: bounded result limit for large reports.
- `resultOffset`: zero-based offset into the sorted filtered result set for batched full reports.
- `append`: append this batch to existing report artifacts in `outputDirectory`; first batch should use `overwrite=true`, later batches use `append=true`. Append batches cannot use `runTests=true`.
- `confirmLargeReport`: required for `apply=true` when the filtered report scope exceeds 10000 clashes. Agents must ask the user before setting it.
- `outputDirectory`: optional explicit artifact directory.
- `runTests`: false by default; when true and `apply=true`, run only matched `testName`/`testNames` if a scope is provided; otherwise call `TestsRunAllTests()` before reading results.
- `boxOffsetMm`: default 1500.
- `boxMode`: default `point`; use `items` for the legacy item-bounds box.
- `contextTransparency`: default 0.5.
- `useFullBoxTransparency`: false by default; when true, apply `contextTransparency` to all non-clashing objects inside each clash box once before saved viewpoints/screenshots. This reuses the UI "Прозр. бокса" behavior and is slower than parent-context transparency.
- `colorAHex`, `colorBHex`: optional `#RRGGBB` overrides.
- `includeClashPointMarker`: optional redline target marker at the clash point for screenshots/viewpoints; default false.
- `captureTopViewScreenshots`: optional second orthographic top-view screenshot per clash; default false.
- `screenshotProfile`: `compact` by default, producing JPEG screenshots capped at 1280x720; `fullhd` caps at 1920x1080; `large` caps at 2560x1440; `source` keeps legacy full-size BMP output.
- `screenshotFormat`: optional `jpg`, `png`, or `bmp` override.
- `screenshotMaxWidth`, `screenshotMaxHeight`, `screenshotJpegQuality`: optional screenshot export overrides.
- `excludeItemNameContains`: optional text filters; if either clashing side name/path contains any filter, the result stays in Clash Detective but is skipped in report rows, viewpoints, and screenshots. The response reports total and per-filter exclusion counts.
- `artifactGranularity`: `result` by default; `cluster` creates one shared viewpoint and screenshot
  set for every cluster. Cluster artifacts require `groupMode` other than `none`, `resultOffset=0`,
  `append=false`, and the complete filtered scope within the request `limit`.
- `verbosity`: `full` by default for compatibility. `compact` removes duplicated long item paths,
  descriptions, association keys, and cluster preview rows only from the MCP response. The generated
  `manifest.json`, `report.html`, and `clash_boxes.json` remain complete.

Companion commands:

- `clash_report_status`: returns the active or last report operation state without waiting for the UI-thread report command to finish. It can target an `operationId` returned by `clash_generate_report`, or the active report when omitted.
- `cancel_clash_report`: requests cooperative cancellation for the active report or the provided `operationId`. The current Navisworks screenshot/viewpoint step may finish, then the report loop stops before the next clash and writes partial artifacts.

`clash_manage_tests` is a selected-test operation command with dry-run/apply behavior:

- `operation`: `run`, `reset`, `compact`, `rename`, `delete`, or `set_settings`.
- `testName` / `testNames` / `testHandles`: required scope. Empty scope is rejected.
- `newName`: required only for `rename`.
- `toleranceMm`: optional tolerance update for `set_settings`; MCP accepts millimeters and host converts to document units.
- `testType`: optional type update for `set_settings`; accepted standard values are `hard`/intersection, `hard_conservative`/conservative, `clearance`, and `duplicate`.
- `apply=false`: returns the planned matched tests without changing Clash Detective state.
- `apply=true`: applies the operation. `delete` should be dry-run first because it is destructive.

## Known Limits

- The safest API path is to process existing results. Running all tests is supported by the current codebase through `DocumentClash.TestsData.TestsRunAllTests()`, but it is a long-running write operation and remains opt-in.
- `manifest.json` and `report.html` include status summaries for the selected test scope and the rows returned in the report.
- Large full reports are generated in batches. Use `nextResultOffset` while `hasMoreResults=true`; append batches accumulate `manifest.json`, `clash_boxes.json`, and `report.html` in the same output folder.
- Scopes above 10000 filtered clashes require explicit user confirmation before `apply=true`.
- Cancellation is cooperative and checked between clash rows. It does not interrupt a single Navisworks image export or Clash Detective test run in the middle of the API call.
- Screenshot capture support varies by Navisworks runtime/API availability. The command records per-result screenshot status and still creates viewpoints/report metadata if capture is unavailable.
- Permanent material overrides cannot be removed precisely per item through the public API. The workflow restores the visual state for touched items using the same compromise already documented in `research/SECTION_BOX_RESEARCH.md`.

## UX Synchronization

The visual Clash tab and the MCP report workflow should expose the same core report semantics:

- `boxMode=point`: section box centered on the clash point; `boxOffsetMm` is half-size.
- `boxMode=items`: section box based on the two clashing sides' bounds plus `boxOffsetMm`.
- A/B colors, section box usage, context transparency, full-box capture transparency, and offset should be visible in UI and represented in MCP request parameters.

Current UI work includes a column-filter scaffold for clash rows by result name and clashing side names. Future report/export work should reuse the same field names for HTML table filtering and Excel/XLSX output.

Nearby clash grouping is tracked separately in [CLASH_CLUSTERING_PLAN.md](CLASH_CLUSTERING_PLAN.md). The first safe step is a read-only cluster analysis tool; true cluster screenshots/viewpoints should remain opt-in until tested on large models.

## Future Report UX

When an MCP client receives a broad request such as "make a clash report", it should collect or infer:

- test/result scope;
- status set;
- A/B colors;
- `boxMode` and `boxOffsetMm`;
- context transparency mode: parent-context or full clash-box scan for screenshots/GIF;
- screenshot count/camera presets;
- marker visibility;
- output folder.

Excel/XLSX export is planned after the HTML table filtering design is stable.

## Files To Update

- `NavisHelper.Contracts/HostContracts.cs`
- `NavisHelper.Contracts/Statuses.cs`
- `NavisHelper/Agent/Services/DocumentCommandService.Clash.cs`
- `NavisHelper/Agent/Host/AgentHostService.cs`
- `NavisHelper.McpServer/Services/HostBridgeClient.cs`
- `NavisHelper.McpServer/Tools/NavisworksClashTools.cs`
- MCP docs and command catalog
