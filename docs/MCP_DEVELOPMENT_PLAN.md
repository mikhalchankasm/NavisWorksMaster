# NavisHelper MCP Development Plan

## Post-2.4 Checkpoint

Current publication/build/install state and the next architecture plan are captured in `docs/POST_2_4_ARCHITECTURE_CHECKPOINT.md`. Read that checkpoint before starting architecture work; it is the boundary between the published `v2.4.0.0` baseline and the `v2.4.1.0` Clash/MCP release slice.

## Current State

- MCP server uses stdio transport and exposes Navisworks tools through `NavisHelper.McpServer`.
- Navisworks plugin hosts a named-pipe bridge in-process and executes commands on the UI thread.
- Direct host pipe and MCP stdio have both been smoke-tested against Navisworks Manage 2027.
- Generic `find_items` accepts exactly one logical query/search per call; display category/property names are the primary property-search path and internal names are fallback-only.
- Fast root-level search is available through `find_root_items_by_name`.
- Root model discovery is available through `list_root_items`.
- `find_items_by_bbox` provides read-only AABB search in active-document global coordinates. It uses explicit `min`/`max` bounds, `intersects`/`contains`/`center` modes, bounded traversal, and returns a match handle without changing selection. Named zones, local/grid coordinate transforms, and project-specific coordinate conventions remain a separate design item.
- Compact active model context is available through `active_model_context`; it combines host status, root filename preview, saved item counts, and search workflow guidance for MCP clients.
- Host diagnostics are available through `host_status`.
- Current selection diagnostics are available through `selection_status`, `selected_items_preview`, `selected_items_ancestry`, and `selected_items_tree`.
- Read-only model inspection is available through `item_properties_by_handle`, `current_viewpoint_info`, `list_saved_viewpoints`, and `list_selection_sets`.
- Large root subtree name dumps are available through `start_subtree_names_dump -> dump_subtree_names_status`, with `cancel_subtree_names_dump` for cancellation and synchronous `dump_subtree_names` kept only for hard-limited small subtrees.
- Existing selection sets can be resolved and previewed/applied through `select_selection_set`.
- Static selection sets and dynamic search sets are separate product concepts. `create_selection_set` stores concrete model items from either the current selection or `matchHandles`; `create_search_set` stores a reusable native Navisworks Search Set from persistable search conditions.
- Selection Sets tree maintenance is available through `selection_sets_manage` and `selection_sets_reorder`: create folders, delete folders/sets, rename, move, and natural-sort folders/sets with dry-run by default.
- Existing saved viewpoints can be resolved and previewed/applied through `activate_saved_viewpoint`.
- Saved viewpoint listing includes whether each saved viewpoint contains visibility or appearance overrides.
- Running Navisworks host discovery is available through `list_navisworks_hosts`.
- MCP tools accept optional `instanceId` and `navisworksVersion` arguments for explicit host targeting.
- MCP diagnostics are available through `mcp_diagnostics`, including the JSONL call log path.
- Recent MCP calls are available through `mcp_recent_calls`.
- Read-only health verdicts are available through `mcp_health_check`; use it after long runs, timeouts, or suspected host hangs.
- Stable error handling guidance is available through `mcp_error_contract`.
- Write-oriented commands use `apply=false` dry-run behavior by default; only explicit `apply=true` mutates model visibility, current selection, selection sets, current view, or saved viewpoints.
- MCP host bridge calls are written as JSONL records with command, target instance, elapsed time, status, error code, and response summary.
- Repeatable test scripts:
  - `scripts/navishelper_host_stress.ps1`
  - `scripts/navishelper_mcp_smoke.py`
  - `scripts/navishelper_mcp_mixed_stress.py`
  - `scripts/navishelper_mcp_failure_modes.py`
  - `scripts/navishelper_mcp_failure_modes.ps1`
  - `scripts/navishelper_mcp_regression.ps1`
  - `scripts/navishelper_mcp_soak.ps1`
  - `scripts/start_navisworks.ps1`
- Client usage guide:
  - `docs/MCP_CLIENT_GUIDE.md`

## Verified Scenario

Test model:

`<PATH_TO_TEST_MODEL.nwd>`

Observed results:

- `host_status` reports an active 2027 document and 437 root items.
- `active_model_context` reports the same active document/root index and returns bounded root filename context plus search guidance in one call.
- `list_root_items` returns readable `.rvm/.dwg/.nwd` filenames.
- `find_root_items_by_name -> select_items -> zoom_to_selection -> fit_all` works through MCP.
- `selection_status`, `selected_items_preview`, `selected_items_ancestry`, and `selected_items_tree` work after MCP selection and are covered by smoke/regression.
- `item_properties_by_handle`, `current_viewpoint_info`, `list_saved_viewpoints`, and `list_selection_sets` work through MCP and are covered by smoke/regression.
- `select_selection_set` and `activate_saved_viewpoint` resolve exact paths returned by the list tools; smoke/regression covers their default dry-run behavior.
- `select_selection_set apply=true` is covered in live regression and verified through `selection_status`.
- `activate_saved_viewpoint apply=true` is covered only in started/throwaway regression runs; `UseExisting` keeps it dry-run to avoid changing a user's active view.
- Explicit host targeting works through MCP by both `instanceId` and `navisworksVersion`.
- `mcp_diagnostics` reports `%LOCALAPPDATA%\NavisHelper\Mcp\logs\mcp-calls-YYYYMMDD.jsonl`; smoke/regression calls produce `host_call` log entries.
- `mcp_recent_calls` returns the recent JSONL records; `mcp_error_contract` returns retryability and recommended action for each known MCP/host error.
- `mcp_health_check` returns a healthy verdict before/after mixed MCP stress and includes host memory, document/root counts, per-check timings, and recommended actions if degraded.
- `create_selection_set`, `create_search_set`, `selection_sets_manage`, `selection_sets_reorder`, `create_viewpoint`, and `show_all` are smoke-testable without `apply`; each returns dry-run results and does not mutate the document.
- `select_selection_set` and `activate_saved_viewpoint` are smoke-tested without `apply`; each returns dry-run results and does not mutate current selection/view.
- Cached root search smoke: 20 repeated calls, 20 OK, average 7 ms on the tested model after warmup.
- Regression runner starts Navisworks 2027 with the quoted NWD path, waits for the MCP host, runs MCP smoke, and closes the process it started.
- Mixed MCP stress is available for stdio-level repeated calls across `host_status`, `mcp_health_check`, `active_model_context`, root search, properties, saved viewpoints, selection sets, current viewpoint, and optional selection/dry-run visibility. It writes JSON reports under `artifacts/mcp-stress`, including memory checkpoints and optional memory delta thresholds.
- Failure-mode runner verifies multiple hosts, empty/no-model Navisworks documents, and closed/stale hosts without manual intervention.
- Regression runner now fails on Python smoke non-zero exit codes instead of printing a false OK summary.
- Soak runner verified 2 repeated start/test/close cycles with 0 errors on Navisworks 2027.
- Chunked subtree name dump live-verified on a large composite test model / nested root file with 595,984 items. `start_subtree_names_dump -> dump_subtree_names_status` completed in 256,963 ms, produced a 62,118,822 byte CSV under a temporary output directory, ended with `state=done`, `pendingItemCount=0`, and post-run `mcp_health_check` remained healthy with no memory growth.
- `cancel_subtree_names_dump` was live-checked for cleanup behavior: cancelled job closed its writer and reported `fileSizeBytes=0`.
- Performance note from the live dump: `includePath=true` is much slower on deeply nested composite models because every row resolves a full Navisworks path. For external name lookup, prefer `includePath=false`; use full paths only when hierarchy context is required.
- Subtree dump smoke coverage is live-verified through both MCP stdio and direct host bridge smoke. On the standard smoke model, a root file completed with `state=done`, 1,339 items, 140,948 bytes, and 1,340 CSV lines.
- Mixed MCP stress verified 50 iterations / 505 tool calls after the subtree dump smoke changes; pre/post `mcp_health_check` remained healthy and post-stress smoke passed.
- Dedicated disposable-session dump smoke now verifies that two simultaneous `start_subtree_names_dump` calls targeting one `outputPath` produce one job plus one path-conflict rejection. It also measures `includePath=false` JSONL output and host Working Set before/after; on the standard `.nwd` smoke model it wrote 760 rows in 169 ms with a 1 MB Working Set delta and a healthy post-run check.
- Extended lifecycle stress baseline completed on the standard `.nwd` smoke model: 2/2 fresh Navisworks + MCP cycles passed with 25 direct host calls and 25 mixed MCP iterations per cycle, a 512 MB memory threshold, and 0 errors in 99 seconds. The reusable long profile defaults to 5 cycles with 100 host and 100 mixed iterations per cycle.

## Near-Term Priorities

1. Productize the main already-working selection/search-set workflow:
   - search by name/property or root filename
   - `select_items`
   - `hide_unselected` / `isolate_selected`
   - `create_selection_set` from current selection or directly from `matchHandles`
   - `create_search_set`
   - `selection_sets_manage` / `selection_sets_reorder`
   - keep zoom as the explicit `zoom_to_selection` step, not necessarily a separate combined command
2. Maintain live regression coverage for native Search Set creation and Selection Sets tree operations:
   - create a dynamic Search Set in a new folder
   - rename/move/delete it through `selection_sets_manage`
   - verify `selection_sets_reorder` dry-run/apply plans
   - document that persisted `create_search_set` currently supports `combineOperator=all` only
3. Extend location/coordinate search only after a project coordinate convention is agreed:
   - define named-zone storage and descriptions
   - define local/grid coordinate transforms and unit conversion ownership
   - decide whether root/source-file grouping belongs in the result or a follow-up tool
4. Strengthen selected-properties workflows:
   - maintain compact property display for current selection
   - maintain disposable-process CSV/XLSX export regression for `selection_export_properties`
   - extend bounded large-selection behavior and clear row-limit coverage when larger test models are available
5. Keep Clash photo/zoom report regression coverage current:
   - `clash_generate_report` already scopes existing results, recreates a section-box/ISO view, applies A/B appearance, writes text artifacts, and captures screenshots when the Navisworks runtime permits.
   - Disposable regression runs a bounded dry-run against the first available Clash Detective test; models without tests report an explicit skip instead of a false pass.
   - Live apply coverage with screenshot output remains required after any report-pipeline change.
6. Keep installer/configurator release validation current:
   - package installation, update, and uninstall remain per-user and preserve a running versioned MCP runtime.
   - framework-dependent packages check for .NET 9 before installation; self-contained packages do not require it.
   - `tools/validate_distribution.ps1` verifies the 2024–2027 bundle payload, component-version alignment, debug-artifact exclusion, and the generated ZIP against `checksums.sha256`.
   - installer compilation rejects an `AppVersion` that differs from the bundled package version.
7. Complete the remaining dump document-identity live check:
   - active document switch while a dump job is running (the host-side file-name and active-document guards exist, but this exact scenario needs a safe non-UI switch harness)
8. Repeat the extended lifecycle stress profile on a larger representative `.nwd` and compare its artifact to the standard-model baseline:
   - `scripts\navishelper_mcp_extended_soak.ps1` repeats MCP startup/shutdown and Navisworks open/close, then runs higher-count host and mixed dry-run cycles with memory thresholds.
   - use its `summary.json` plus per-cycle reports for the next long-run baseline.
9. Keep visibility summaries grouped by root/source file current before hide/isolate/reveal operations.
10. Keep bounded root-name suggestions current for failed exact filename searches.
11. Keep the implemented opt-in persistent user scenario library safe and compatible:
   - `list_scenarios`, `get_scenario`, `save_scenario`, `delete_scenario`, and `resolve_scenario` are MCP-server-only and store user-approved JSON under `%APPDATA%\NavisHelper\Scenarios`;
   - template/exactReplay validation rejects model handles, runtime IDs, credentials, authorization flags, transcripts, and unreviewed paths;
   - exact replay requires a direct current user request, strict context, fixed values, a safety envelope, and preview-first existing tool calls; it never auto-runs from startup or model events;
   - export/import remains the next scenario-library slice and must preserve the existing schema, migration, archive-hardening, and authorization contract.

## Regression Commands

Run against an already open Navisworks instance:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\navishelper_mcp_regression.ps1 -FilePath "<PATH_TO_TEST_MODEL.nwd>" -Version 2027 -UseExisting -StressCount 20 -KeepNavisworksOpen
```

Start Navisworks, test it, and close only the started process:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\navishelper_mcp_regression.ps1 -FilePath "<PATH_TO_TEST_MODEL.nwd>" -Version 2027 -StressCount 20
```

Start Navisworks, test it, run mixed MCP stdio stress, and close only the started process:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\navishelper_mcp_regression.ps1 -FilePath "<PATH_TO_TEST_MODEL.nwd>" -Version 2027 -StressCount 20 -MixedStressCount 50
```

Run mixed stress with memory checkpoints and a warning/failure threshold:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\navishelper_mcp_regression.ps1 -FilePath "<PATH_TO_TEST_MODEL.nwd>" -Version 2027 -StressCount 20 -MixedStressCount 300 -MemoryCheckpointInterval 25 -MaxMemoryDeltaMb 500
```

Run repeated open/test/close cycles:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\navishelper_mcp_soak.ps1 -FilePath "<PATH_TO_TEST_MODEL.nwd>" -Version 2027 -Cycles 3 -StressCount 20
```

Run failure-mode checks:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\navishelper_mcp_failure_modes.ps1 -FilePath "<PATH_TO_TEST_MODEL.nwd>" -Version 2027
```

## Operating Guidance For Claude

- Use `host_status` first to confirm Navisworks is running and a document is active.
- Follow `docs/MCP_CLIENT_GUIDE.md` for the default safe workflow.
- Use `active_model_context` early when working with a large model: it gives document status, root filename context, saved item counts, and the recommended search flow in one call.
- If multiple Navisworks windows may be open, use `list_navisworks_hosts` first and pass the desired `instanceId` to subsequent tools.
- Use `mcp_diagnostics` when debugging timeouts or host discovery; inspect the returned `logFilePath` for per-call timings and errors.
- Use `mcp_recent_calls` after a failed or suspicious run to confirm which tools were actually invoked and how long they took.
- Use `mcp_health_check` after long sequences or errors to verify host responsiveness, active document/view state, root index availability, and memory snapshot.
- Use `mcp_error_contract` to decide whether an error should be retried, retargeted with `instanceId`, or fixed by changing arguments.
- Use `selection_status` and `selected_items_preview` after `select_items` and before hide/isolate/view commands to confirm the active selection.
- Use `selected_items_ancestry` when the user manually selected objects and asks for owners, parents, or structure up to the model root; each item returns a root-to-selected `chain` suitable for text/JSON export.
- Use `selected_items_tree` for full current-selection exports, especially when the selection may exceed 100 items; choose `format=tree` to merge common parents or `format=flat` for a row-like export.
- Use `item_properties_by_handle` after search when you need bounded property details without traversing the whole model.
- Use `current_viewpoint_info` and `list_saved_viewpoints` before creating or changing viewpoints; check saved viewpoint override flags before applying a saved viewpoint if visibility state matters.
- Use `activate_saved_viewpoint` with an exact `path` from `list_saved_viewpoints` when the user asks to switch to an existing saved view.
- Use `list_selection_sets` before creating or modifying a set, or when the user refers to an existing named selection. Prefer returned `itemId` for duplicated names.
- Use `select_selection_set` with an exact `path` from `list_selection_sets` when the user asks to reuse an existing selection set or folder.
- Use `create_selection_set` with `matchHandles` when the user says "find these objects and save them" and wants a static snapshot of the found items. Use `create_search_set` when the user wants a saved native Navisworks Search Set rather than a static snapshot. Use `selection_sets_manage` for folders, delete, rename, and move; use `selection_sets_reorder` for natural sorting.
- For any write-oriented tool, first call it without `apply` or with `apply=false`; only send `apply=true` after checking the preview counts and names.
- Use `list_root_items` when the task involves model file names near the root of the selection tree.
- Use `find_root_items_by_name` for root-level appended files or top-level model nodes. Do not describe this as RVM-only; `.rvm` is common in current tests but not a product contract.
- Use generic `find_items` only for property-level searches. Prefer display category/property names with `dataType`; send multiple targets as separate sequential calls, exactly one logical query/search per call.
- For large one-off name dumps under a selected root file/node, call `start_subtree_names_dump`, then poll `dump_subtree_names_status` until `state` is `done`, `failed`, or `cancelled`; avoid synchronous `dump_subtree_names` on large roots.
- For external search/indexing dumps, set `includePath=false` unless the user explicitly needs full hierarchy paths.
- Do not promise "search by code list" as a first-class workflow until the domain attribute semantics are implemented. Prefer name/root/property search today.
- Use `find_items_by_bbox` for a raw global-document AABB. Do not imply it understands local/grid coordinates or named project zones.
- After any long or failed request, run `host_status` or `fit_all` to confirm the host is responsive.
