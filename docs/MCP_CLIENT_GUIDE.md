# NavisHelper MCP Client Guide

This guide is for MCP clients such as Claude Code. It describes the safe default workflow for large Navisworks models.

For exact input/output fields of Clash Detective MCP tools, see [MCP_TOOL_CONTRACTS.md](MCP_TOOL_CONTRACTS.md).

## First Calls

1. If the user asks to open the last Navisworks model, call `open_latest_navisworks_file`.
2. Call `mcp_health_check`.
3. If `ok=false`, call `mcp_recent_calls` and inspect `recommendedActions`. If health reports an MCP server/plugin version mismatch, update both from the same NavisHelper package before write operations.
4. Call `active_model_context` before searching a large model or when the user gives root-level appended file names such as `.rvm`, `.dwg`, `.nwd`, or similar source nodes.
5. If multiple Navisworks windows are open, call `list_navisworks_hosts` and pass `instanceId` to every following tool.

## Starting Navisworks

Use `list_recent_navisworks_files` to inspect the current Windows user's recent `.nwd/.nwf/.nwc` files from `HKCU\Software\Autodesk\Navisworks Manage\<version>\Recent File List`.

Use `open_latest_navisworks_file` for the common prompt "start Navisworks and open the last file". It opens the newest existing recent file and waits for the NavisHelper MCP host by default. Use `start_navisworks` when the user provides a specific `filePath`, a specific `navisworksVersion`, or wants a blank Navisworks session.

After launch, use the returned `host.instanceId` for follow-up tools when more than one Navisworks process may be running.

If `NAVISHELPER_INSTANCE_ID` is set, the MCP server treats it as a strict target and fails with `instance_not_found` when that host is absent. `NAVISHELPER_INSTANCES_DIR` is honored by both the Navisworks plugin host and MCP server; set it in both processes only for isolated test runs.

## Task Timing

Every MCP tool result includes automatic `navishelper_timing` in the primary JSON result with `elapsed_ms`, `elapsed_human`, `should_report_to_user`, `user_message`, and `agent_instruction`. If `should_report_to_user=true`, include `user_message` in the user-facing answer.

For larger user-visible workflows that span several MCP tool calls, call `mcp_task_timer_start` before the workflow and `mcp_task_timer_finish` before the final answer to get one elapsed time for the whole workflow. If the finish result has `shouldReportToUser=true`, include `userMessage` in the final answer.

Every host call is also written to `mcp_recent_calls` with `requestId`, `elapsedMs`, `elapsedHuman`, and `reportElapsedToUser`; use those fields for diagnostics after failures or long runs.

After `request_timeout`, client cancellation, broken pipe, or a suspected oversized response, copy the `requestId` from `mcp_recent_calls` and call `last_operation_status`. Treat `completed` as evidence that the Navisworks-side command already ran; do not blindly retry write tools. Treat `running` as "wait and poll again", and `failed` as the authoritative host-side failure.

## Root Filename Search

For top-level appended model filenames or source nodes, prefer:

1. `active_model_context` or `list_root_items`
2. `find_root_items_by_name` with `comparison=equals`; if it returns `not_found`, inspect bounded `suggestions` before retrying with the exact filename or an explicit broader comparison
3. `select_items`
4. `selected_items_preview`
5. `zoom_to_selection`, `fit_all`, or dry-run visibility tools

Use generic `find_items` only for property/category searches. Prefer display `category` + `property` names with `dataType`; treat `categoryInternal` / `propertyInternal` as optional fallback fields. Hard limit is exactly one logical query/search per call; run multiple targets as separate sequential calls and check `host_status` before retrying after `request_timeout`.

Navisworks search conditions do not support parentheses, and AND binds more strongly than OR. Therefore `A OR B OR C AND D` means `A OR B OR (C AND D)`. To express `(A OR B OR C) AND D`, distribute the shared condition: `(A AND D) OR (B AND D) OR (C AND D)`. Use the per-condition `logicalOperator` flags to encode those branches; do not rely on `combineOperator=any` as a grouping mechanism.

## Selection-set viewpoint batches

Use `selection_sets_build_viewpoints` for new batch workflows. Supply a required `folderPrefix`, a `nameTemplate` containing `{set}` and `{step}`, and independently configured `overview`, `markup`, or `sectionBox` steps. Use `whenItemCountMin`/`whenItemCountMax` to give large and small sets different recipes in one call. Count clustering accepts either approximate `clusterTargetSize` or exact `clusterCount`. `verbosity="summary"` is the default and omits per-cluster item previews; request `full` only for diagnostics. `maxClusters` reports `droppedClusterCount` and `uncoveredItemCount` and never overrides an explicit `arrowCallout=false`. Set `overwrite=true` on a step to update matching viewpoints in place. Start with `apply=false`.

For a full one-off name dump from a single root file/node, use `start_subtree_names_dump` instead of `find_items` by `Source File`. Then poll `dump_subtree_names_status` with the returned `jobId` until `state` is `done`, `failed`, or `cancelled`. The job writes to `outputPath.partial` while running, atomically replaces `outputPath` only after successful completion, and removes the partial file on failure/cancellation. This avoids a large match payload and long single-request timeouts.

For fastest external lookup by position/name, set `includePath=false`. On the live `example-model.rvm` test, full path generation was the dominant cost; use `includePath=true` only when the full Navisworks hierarchy is needed in the output.

Use the synchronous `dump_subtree_names` only for small subtrees. It is hard-limited and will tell the client to switch to the job workflow when the root is too large.

Do not present current search as a "list of codes" workflow. Until the project defines a real domain-code attribute, use name search, root filename/source-node search, or explicit property search. For a raw global-document zone, use `find_items_by_bbox` with explicit `min`/`max` points in the active document units; it does not transform local/grid coordinates. Named zones and project-specific coordinate systems need a separately agreed convention.

## Current Selection Hierarchy

When the user manually selects objects in Navisworks and asks what is selected, who owns those objects, or what their structure is up to the model root:

1. Call `selection_status` to confirm a non-empty selection.
2. If the user only needs copy-ready names, call `selection_copy_names`.
3. If the user needs properties of the selected items, call `selection_property_report` with category/property filters and bounded row limits.
4. If the user asks for unique systems/specs/marks in the selection, call `selection_distinct_property_values` with category/property filters.
5. If the user asks to color the current selection by property, call `selection_color_by_property` first with `apply=false`, then only call `apply=true` after reviewing groups and colors.
6. If the user needs a file artifact, call `selection_export_properties` first with `apply=false`, then with `apply=true`, an explicit `outputPath`, and `format=csv` or `format=xlsx`.
7. For compact hierarchy answers, call `selected_items_ancestry` with a bounded `limit`.
8. For full exports or selections larger than 100 items, call `selected_items_tree` with `format=tree` or `format=flat`.
9. Use each returned `chain` or tree branch as the authoritative root-to-selected hierarchy.

Use `selected_items_preview` only for a compact flat preview. It is limited to 100 items and does not return structured parent chains.

## Clash Detective

Use `clash_list_tests` when the user asks which Clash Detective tests exist or needs a compact clash count summary.

Default behavior:

- Returns test names, total results, new results, active results, and optional status counts.
- It is read-only.
- Keep `limit` bounded when many tests are expected.

Use `clash_list_results` after `clash_list_tests` when the user needs individual clash rows. Pass `testName` for a specific test or leave it empty for all tests, and use `statusFilters` for statuses such as `New` or `Active`.

`TotalResultCount` is the total number of results in the matched test scope before status filtering. `MatchedResultCount` is the count after `statusFilters`. `Item1Names` and `Item2Names` return up to 10 display names per clash side; `Item1ItemCount` and `Item2ItemCount` keep the full side counts.

`TestHandle` and `ResultHandle` are traversal handles for the current Clash Detective tree order. Use `TestHandle` values from `clash_list_tests` for selected-test write operations in the same document/session.

Use `clash_manage_tests` when the user asks to run or edit selected Clash Detective tests.

Safe workflow for selected tests:

1. Call `clash_list_tests` and identify the intended tests by exact name or `testHandle`.
2. Call `clash_manage_tests` with `apply=false`, `operation`, and `testNames` or `testHandles`.
3. Review `matchedTestCount` and the returned `tests`.
4. Call `clash_manage_tests` with `apply=true` only after the selected scope is clear.

Supported operations are `run`, `reset`, `compact`, `rename`, `delete`, `move`, `sort`, and `set_settings`. `rename` and `move` require exactly one matched test; `move` uses a 1-based `targetIndex` inside the current Clash Detective folder/root. `sort` reorders matched tests by natural name order inside their current folder/root. `delete` is destructive; always dry-run first. Do not use this command with an empty test scope.

Use `operation=set_settings` when the user asks to mass-change Clash Detective test settings for selected tests. It accepts:

- `toleranceMm`: Clash tolerance in millimeters. The common default in metric projects is 1 mm; the host converts this to the active document units before editing the tests.
- `testType`: one of `hard`/`intersection`, `conservative`/`hard_conservative`, `clearance`, or `duplicate`.

Clash test type meaning:

- `hard` / intersection: reports actual geometry intersections between selection A and selection B.
- `hard_conservative`: intersection check with a more conservative algorithm; useful for catching difficult geometry, but it can be slower or produce more cautious results.
- `clearance`: reports objects whose distance is less than the configured tolerance/clearance, even when they do not physically intersect.
- `duplicate`: reports duplicate or overlapping identical geometry.

For settings changes, keep the same write-safety pattern: call `clash_manage_tests` with `operation=set_settings`, `apply=false`, selected `testNames`/`testHandles`, and the requested `toleranceMm`/`testType`; then call with `apply=true` only after reviewing the matched tests.

Use `clash_save_viewpoints` when the user asks only to create Saved Viewpoints from existing Clash Detective results. This command does not run tests, write report files, capture screenshots, or call `clash_generate_report`.

Safe workflow:

1. Call `clash_list_tests`.
2. Call `clash_save_viewpoints` with `apply=false`, bounded `limit`, and the intended `testName`/`testNames`/`statusFilters`. Leave `testName` and `testNames` empty for all tests.
3. Review `matchedTestCount`, `returnedResultCount`, `folderPath`, `truncated`, and `largeViewpointsConfirmationRequired`.
4. If `largeViewpointsConfirmationRequired=true`, tell the user the filtered clash count and ask for explicit confirmation before applying.
5. Call `clash_save_viewpoints` with `apply=true` only after the scope is clear. For large batches, pass `confirmLargeViewpoints=true` only after explicit confirmation.
6. Continue while `hasMoreResults=true` by calling the same scope/options with `resultOffset=nextResultOffset`. Use the same `folderPath` to keep batches together. The reset viewpoint is created only on the first batch.

`clash_save_viewpoints` uses existing results only. If the user first wants fresh Clash Detective results, run `clash_manage_tests` with `operation=run`, wait for it to finish, then call `clash_save_viewpoints`.

When the NavisHelper UI groups clash rows by side A or side B, those groups are persisted as real Clash Detective groups. The stored Navisworks group names may include the technical suffix ` [NH:A]` or ` [NH:B]`; treat that suffix as an implementation marker, not user-facing text. Viewpoint folders, viewpoint names, and chat responses should use the clean group label without ` [NH:A]` / ` [NH:B]`.

Use `clash_group_results` when the user asks to create real groups in the standard Clash Detective form by formula, for example "group this Clash Test by the owner of object B". This command is different from `clash_list_clusters`: `clash_list_clusters` is read-only analysis, while `clash_group_results` writes `ClashResultGroup` folders when `apply=true`.

Safe workflow:

1. Call `clash_list_tests`.
2. Call `clash_group_results` with `apply=false`, explicit `testName` or `testHandles`, and `groupBySide`. Prefer `groupBy=root` for source-model grouping; use `ancestorLevelsUp` only for deliberate tree-level grouping.
3. Review `plannedGroupCount`, `groups[].groupName`, `groups[].resultCount`, `conflictGroupCount`, and `largeGroupingConfirmationRequired`.
4. If `largeGroupingConfirmationRequired=true` or the scope is large, ask the user before applying.
5. Call `clash_group_results` with `apply=true` only after the dry-run plan is acceptable. Use `overwriteExisting=true` only when the user wants to rebuild matching existing groups.

For a whole-model coordination matrix, create/run the broad test with `clash_tests_from_sets` and `ignoreRules.sameFile=true`, then call `clash_root_matrix`. Continue while `pairsTruncated=true` using `offset=nextOffset`. For large group listings, continue `clash_group_results` while `groupsTruncated=true` using `groupOffset=nextGroupOffset`; `resultsTruncated` and `groupsTruncated` describe different limits.

Use `clash_renumber_results` after grouping when the user wants every visible group/result in a Clash Test to receive an individual ordered number. Default `scope=top_level` is the usual mode: it numbers real groups and ungrouped results as standard Clash Detective shows them. Always run dry-run first, then apply only with `confirmRename=true`.

Use `clash_generate_report` when the user asks for a Clash Report with screenshots, report files, or report-managed viewpoints/section boxes.

Safe workflow:

1. Call `clash_list_tests`.
2. Call `clash_generate_report` with `apply=false`, bounded `limit`, and the intended `testName`/`testNames`/`statusFilters`. Leave `testName` and `testNames` empty for all tests.
3. If the user wants problem-zone grouping, first call `clash_list_clusters` with the same scope and `groupMode=hybrid`, then repeat the report dry-run with the same `groupMode` and `clusterDistanceMm`.
4. Review `matchedTestCount`, `returnedResultCount`, `truncated`, `clusterCount`, and `outputDirectory`.
5. If `largeReportConfirmationRequired=true`, tell the user the filtered clash count and ask for explicit confirmation before any apply call.
6. Call `clash_generate_report` with `apply=true` only after the user intent is clear. For large reports, pass `confirmLargeReport=true` only after that explicit confirmation.
7. During long reports, call `clash_report_status` to monitor progress. If the user asks to stop, call `cancel_clash_report`; it requests cooperative cancellation and the current screenshot/viewpoint step may finish before the partial report is written.
8. After long reports, call `mcp_health_check`.

For large full reports, use batched generation instead of one huge call. The first apply call should use an explicit `outputDirectory` and `overwrite=true`. Continue while `hasMoreResults=true` by calling the same scope/options with `resultOffset=nextResultOffset`, `append=true`, and the same `outputDirectory`. Do not use `runTests=true` on append batches; run tests before reporting or only on the first overwrite batch. `manifest.json`, `clash_boxes.json`, and `report.html` are accumulated across batches; the MCP response returns only the current batch plus `accumulatedResultCount`.

If `cancel_clash_report` is accepted, do not launch the next batch automatically. Wait for the active `clash_generate_report` call to return a cancelled partial batch, inspect `nextResultOffset`, `outputDirectory`, and the written partial `report.html`, then ask the user before continuing.

When the user says "make a clash report" without enough details, ask for or infer these report options:

- Scope: selected test, all tests, or current/known result scope.
- Statuses: default `New,Active`, all statuses, or a specific set such as `Reviewed/Approved/Resolved`.
- Box mode: `point` for a box around the clash point, or `items` for the old behavior based on the clashing item bounds.
- Box size: `boxOffsetMm`; in `point` mode it is the half-size distance from the clash point.
- Object colors: default NavisHelper A/B colors or explicit `colorAHex` / `colorBHex`.
- Context transparency: keep it disabled unless the user explicitly needs it. `useFullBoxTransparency` is a legacy name; when true, NavisHelper now uses safe owner-level context transparency and does not scan every object inside the clash box.
- Screenshots per clash: normal view only, normal + top view, or a later custom camera set.
- Screenshot quality: use `screenshotProfile=compact` for large reports, or `screenshotProfile=fullhd` when the user needs a larger image. Avoid `source` unless the user explicitly needs legacy full-size BMP files.
- Marker: whether to include the clash point marker in screenshots/viewpoints.
- Exclusions: optional `excludeItemNameContains`, for example `["Weld"]`, when clashes containing that text in either side's item name or path should be skipped in the final report.
- Output: temp folder by default, or explicit user folder when requested.

If the user does not answer and the task is not destructive, use the safe default: existing results, `New,Active`, `limit` bounded, `boxMode=point`, `boxOffsetMm=1500`, normal screenshot only, dry-run first.

Defaults:

- `statusFilters` defaults to `New,Active`.
- `includeAllStatuses=true` includes every Clash Detective status in the selected scope, including `Reviewed`, `Approved`, and `Resolved`.
- `testName` selects one named test; `testNames` selects several named tests. Exact match is preferred, otherwise contains-match is used.
- `limit` defaults to `100` and is capped at `5000`. For screenshot/viewpoint reports, prefer batches of `500-1000`.
- `resultOffset` defaults to `0`; use `nextResultOffset` from the previous response for pagination.
- `append=false` by default. Use `append=true` for the second and later batches in the same `outputDirectory`; do not combine `append=true` with `overwrite=true`.
- `append=true` cannot be combined with `runTests=true`, because rerunning Clash Detective can change result offsets between batches.
- `confirmLargeReport=false` by default. When the filtered report scope exceeds `10000` clashes, `apply=true` fails until the user confirms and the agent retries with `confirmLargeReport=true`.
- `boxOffsetMm` defaults to `1500`.
- `boxMode` defaults to `point`: `boxOffsetMm` is the half-size distance from the clash point. Use `boxMode=items` only when the user wants the old behavior: the combined bounds of both clash sides plus padding.
- `contextTransparency` defaults to `0.5`.
- `useFullBoxTransparency` defaults to `false`. It is a legacy flag; when true for reports, `contextTransparency` is applied only to nearby owner items of the current clash, without full-model or full-box scanning. If `contextTransparency=0`, no context objects are changed. Saved Viewpoints are always saved without transparency.
- side colors default to NavisHelper clash colors: red for A and blue for B; pass `colorAHex` / `colorBHex` as `#RRGGBB` to override.
- `includeClashPointMarker` defaults to `false`; pass `true` when screenshots should show a red target marker at the clash point.
- `captureTopViewScreenshots` defaults to `false`; pass `true` when each clash should include both the normal camera shot and an orthographic top-view shot.
- `screenshotProfile` defaults to `compact`, which writes JPEG screenshots capped at 1280x720 with JPEG quality 72. This is the recommended profile for large reports.
- `screenshotProfile=fullhd` writes JPEG screenshots capped at 1920x1080 with JPEG quality 82. Use it when the user wants a larger readable report image.
- `screenshotProfile=large` writes JPEG screenshots capped at 2560x1440 with JPEG quality 88.
- `screenshotProfile=source` preserves the legacy full-size BMP behavior and can create very large reports.
- `screenshotFormat` can override the profile format with `jpg`, `png`, or `bmp`; `screenshotMaxWidth`, `screenshotMaxHeight`, and `screenshotJpegQuality` can override profile defaults.
- `runTests=false`; if set to true, it requires `apply=true` because it changes Clash Detective state. With `testName` or `testNames`, it runs only those matched tests; with no test scope, it runs all tests.
- `excludeItemNameContains` defaults to empty. When provided, matching clashes remain in Clash Detective but are excluded from `report.html`, `manifest.json` item rows, screenshots, and viewpoints. The response includes `excludedByItemNameCount` and per-filter `excludedByItemNameCounts`; tell the user how many clashes were filtered.
- `clash_generate_report` returns `operationId` for apply calls; `clash_report_status` and `cancel_clash_report` can use it, or omit it to target the active report. Cancel is cooperative: it stops before the next clash row after the current Navisworks image/viewpoint operation completes, then writes partial artifacts and returns `cancelled=true`.

Artifacts:

- `report.html`
- `manifest.json`
- `clash_boxes.json`
- `images/clash_000001.jpg`, etc. by default when Navisworks image export is available
- `manifest.json` and `report.html` include status summaries for the selected test scope and returned report rows.
- When `groupMode` is not `none`, `manifest.json` and `report.html` include `clusters[]`, and each raw clash row includes `clusterIndex`, `clusterId`, and `clusterName`. Report screenshots and viewpoints are still generated per raw clash.
- In append mode, `manifest.json`, `clash_boxes.json`, and `report.html` contain all accumulated report rows. Screenshot names use six-digit global indexes such as `images/clash_000001.jpg` so later batches do not overwrite earlier images.

Screenshot capture is best-effort through the Navisworks image export plugin. If capture is unavailable, the command still creates viewpoints and report metadata and returns warnings.

When saved viewpoints are created, the first viewpoint in the report folder is `0000 Базовый вид`, captured before per-clash overrides are applied. Clash viewpoints use a fixed Iso1-style orthographic camera for every result, while still applying the requested clash box size, section box, colors, and markers. Saved Viewpoints are saved without transparency.

Saved viewpoints are created with local appearance overrides for clash A/B colors together with camera, section state, and optional redline markers. Context transparency is not saved into Saved Viewpoints. If Navisworks cannot create a saved view, the affected clash row is reported as failed instead of silently creating a plain viewpoint.

The apply workflow runs inside the Navisworks UI thread. For large clash sets, start with a small dry-run/apply `limit`, inspect `hasMoreResults` and `nextResultOffset`, then continue in batches.

Planned extensions:

- True clustered report generation where one screenshot/viewpoint represents one cluster instead of one raw clash.
- Column-level filtering and sorting for the external `report.html` table.
- Excel/XLSX export of clash rows and screenshot metadata.
- More screenshot camera presets beyond normal/top view.

## Saved Items

Use `list_selection_sets` and `list_saved_viewpoints` before referencing existing saved items.

Pass exact `path` values into:

- `select_selection_set`
- `activate_saved_viewpoint`

Use unique names only when the list output proves they are unique.

Saved viewpoint names and folder paths are not guaranteed to be unique in Navisworks. For Saved Viewpoints tree maintenance, prefer:

1. Call `list_saved_viewpoints` or `saved_viewpoints_export`.
2. Use returned `itemId` when renaming, moving, deleting, or sorting a specific duplicated folder/viewpoint.
3. If `itemId` is unavailable, pass an exact `pathOrName` plus a 1-based `occurrence`.
4. Call `saved_viewpoints_manage` with `apply=false` first, inspect `path`, `newPath`, `type`, and warnings.
5. Call `saved_viewpoints_manage` with `apply=true` only after the target is unambiguous.

`itemId` is generated from the current Saved Viewpoints tree position. Treat it as a read-then-write token for the current tree state, not as a permanent ID. If the user or another command changes the Saved Viewpoints tree between preview and apply, call `list_saved_viewpoints` or `saved_viewpoints_export` again before applying.

Paths use `/` as the folder separator. If a folder or viewpoint display name itself contains `/`, use `itemId` instead of path-based targeting for write operations.

`saved_viewpoints_manage` supports:

- `delete` for one viewpoint and `delete_many` for up to 5000 structured `{pathOrName|itemId, occurrence?}` targets. The complete batch resolves before any mutation.

- `create_folder`: create a Saved Viewpoints folder under `targetFolderPath`, or pass a full new folder path in `pathOrName`.
- `delete_folder`: delete an empty folder; pass `allowDeleteNonEmptyFolder=true` only after confirming children can be removed.
- `rename`: rename a folder or saved viewpoint.
- `move`: move a folder or saved viewpoint into another folder. Missing target folders are created only on `apply=true`.

Use `saved_viewpoints_reorder` to sort Saved Viewpoints in natural numeric order, so names like `1`, `2`, and `11` sort by number instead of plain text. It defaults to dry-run, recursive sorting, and folders-first ordering. Review the returned `Folders[].Before` and `Folders[].After` plans before applying.

Use `saved_viewpoints_export` when the user needs an external editable/auditable list. Supported formats are `csv`, `json`, and `md`; CSV is intended to open cleanly in Excel.

Keep the two Navisworks set concepts separate:

- `create_selection_set` stores concrete model items as a static Selection Set. With `matchHandles`, it saves search results directly without first changing the active Navisworks selection; without `matchHandles`, it saves the current selection. It can save into `folderPath` and overwrite an existing set in that folder when `overwrite=true`.
- `create_search_set` stores a reusable dynamic Search Set from persistable `find_items`-style conditions. It supports `equals`, `contains`, `wildcard`, and `defined`; every display `category` + `property` is resolved from a real model property before persistence, so Navisworks does not create a phantom duplicate category. Clean internal names are saved with `HasPropertyByName`; names containing replacement characters (common in RVM AVEVA properties) are saved through opaque runtime IDs. `Item/Name` and `Элемент/Имя` are still stored through Navisworks internal property names so those standard targets work across English/Russian UI languages. `runtime_resolved_condition_count` counts only the opaque runtime-ID bindings. `equals` uses display-string matching unless `dataType` is explicitly numeric/bool/datetime. Persisted search sets currently support `combineOperator=all` only.

Use `list_selection_sets` before modifying sets or folders. Its `itemId` is duplicate-safe for the current tree state; refresh the list after any create, move, delete, or reorder. For large trees, page with `offset` or narrow results with `pathPrefix` / `nameContains` before asking for an `itemId`. Use `selection_sets_manage` for `create_folder`, `delete_folder`, `delete_set`, `delete`, `rename`, and `move`; prefer `itemId` when cleaning up duplicate or mojibake names. Use `selection_sets_reorder` for natural numeric sorting of folders and sets.

`select_selection_set` is intended for concrete static Selection Sets or dynamic Search Sets. When a folder matches, dry-run returns metadata without expanding all child sets by default; pass `allowFolderExpansion=true` only when the user explicitly wants the folder expanded and accepts that it may be slow on large models.

## Write Safety

Write-oriented tools default to dry-run. First call without `apply` or with `apply=false`.

Only call `apply=true` after checking:

- affected counts
- `affectedRootSummaries`: up to 20 largest affected root/source-file groups, plus `affectedRootCount` and `affectedRootSummariesTruncated`; this safety summary has its own cap and does not use `previewLimit`
- preview item names
- selected item count
- saved viewpoint override flags when view visibility matters

For existing user sessions, avoid apply-regression behavior unless the user asked to change the current Navisworks state.

For `isolate_selected`, the root summary describes the re-hide portion of the operation. Review `previouslyHiddenItemCount` as the separate show-all portion before applying.

For the common "find and save these objects" workflow, call `find_items` or `find_root_items_by_name`, then pass returned `matchHandles` into `create_selection_set` to save a static snapshot. Use `create_search_set` only when the user explicitly wants a reusable dynamic search rule. After `create_search_set(..., select_after_create=true, apply=true)`, call `markup_selection(..., mark_style="target", arrow_callout=true, target_crosshair=false, source="current_selection", apply=true)` to save a viewpoint with persistent ellipse and line-based arrow redlines: large items are marked separately and nearby small items are grouped. With `auto_top_view=false`, the current orthographic or perspective camera and enabled clipping box are preserved. `live_markers(..., apply=true)` is a runtime-only QA overlay and must not be presented as saved deliverable content; hide it with `live_markers(visible=false, apply=true)`. For "show me only what was found", use `select_items`, then `hide_unselected` or `isolate_selected`. `zoom_to_selection` is available as a separate obvious step.

## After Long Runs

After many calls, timeouts, or suspicious latency:

1. Call `mcp_health_check`.
2. Call `mcp_recent_calls` with `lineCount=100`.
3. For any timed-out or disconnected write call, pass its `requestId` to `last_operation_status` before retrying.
4. If health is degraded or the host stopped responding, call `list_navisworks_hosts`.
5. If no host matches, restart Navisworks and reopen the model.

For local regression, use:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\navishelper_mcp_regression.ps1 -FilePath "<PATH_TO_TEST_MODEL.nwd>" -Version 2027 -StressCount 20 -MixedStressCount 100
```

Reports are written to `artifacts/mcp-stress` unless another `-ReportDirectory` is provided.

For long memory-oriented runs, add checkpoints and a threshold:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\navishelper_mcp_regression.ps1 -FilePath "<PATH_TO_TEST_MODEL.nwd>" -Version 2027 -StressCount 20 -MixedStressCount 300 -MemoryCheckpointInterval 25 -MaxMemoryDeltaMb 500
```

For expected failure behavior, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\navishelper_mcp_failure_modes.ps1 -FilePath "<PATH_TO_TEST_MODEL.nwd>" -Version 2027
```

The failure-mode runner checks:

- multiple hosts require explicit `instanceId`
- empty/no-model Navisworks sessions return degraded health instead of hanging
- closed/stale hosts return a stable targeting/connectivity error
