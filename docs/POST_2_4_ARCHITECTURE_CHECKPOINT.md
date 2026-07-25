# Post-2.4 Architecture Checkpoint

Date: 2026-07-07
Repository: `<repo>`
Branch: `main`

## Publication Baseline

- Previous published release: `v2.4.1.0`
- Previous release commit/tag: `v2.4.1.0`
- Previous release URL: `https://github.com/mikhalchankasm/NavisWorksMaster/releases/tag/v2.4.1.0`
- Publication target: `v2.4.2.0`
- Target release scope: the completed post-2.4.1 Clash pipeline service extractions (test mutation, matrix mutation, cluster construction, and report/viewpoint DTO shaping), plus a full release-candidate validation pass.

The `v2.4.1.0` release is published. The `v2.4.2.0` target is the clean release slice for the subsequent Clash pipeline architecture work.

## Local Build And Install State

- Build matrix `Release2024`, `Release2025`, `Release2026`, `Release2027`: passed after the latest Clash scope/page core cleanup.
- Known warning remains: `RibbonLoader.cs(170,35): CS0067 CanExecuteChanged never used`.
- Bundle artifacts under `NavisHelper.bundle/Contents/2024`, `2025`, `2026`, and `2027` are current from the latest build.
- Per-user bundle installed to `%APPDATA%\Autodesk\ApplicationPlugins\NavisHelper.bundle`.
- Machine-wide bundle at `%ProgramData%\Autodesk\ApplicationPlugins\NavisHelper.bundle`: absent.
- Local MCP server installed to `%LOCALAPPDATA%\NavisHelper\McpServer`.
- Latest per-user install was refreshed from `artifacts\distribution\NavisHelper-distribution-rc-check` after the final static/follow-up review fixes.
- During MCP server install, stale `NavisHelper.McpServer` processes had to be stopped before reinstall.

## Post-Release Clash/MCP Work Completed

- Added MCP `clash_list_clusters` for read-only grouping/cluster analysis.
- Added MCP `clash_group_results` for real Clash Detective `ClashResultGroup` creation/rebuild, default dry-run.
- Added MCP `clash_renumber_results` for top-level or recursive renumbering of Clash Detective groups/results, default dry-run.
- Added MCP `last_operation_status`/operation history support so timed-out host operations remain observable after the client disconnects.
- Added MCP startup/recent-file/timing work; current dirty MCP tool-list smoke reports 68 tools.
- Added grouping safeguards:
  - explicit test scope required;
  - `apply=false` by default;
  - large grouping requires `confirmLargeGrouping=true`;
  - existing groups are conflicts unless `overwriteExisting=true` or `ungroupExistingFirst=true`.
- Fixed `includeAllStatuses=true` in `clash_group_results` so it really bypasses status filtering.
- Fixed existing-group matching after renumbering by comparing logical group keys:
  - remove `[NH:A]` / `[NH:B]`;
  - strip leading numbering;
  - normalize whitespace and case;
  - keep side tag as part of the match key.
- Fixed `groupNamePrefix` matching after renumbering for `ungroupExistingFirst=true`.
- Added cleanup of empty top-level NavisHelper groups left after regrouping with a new naming scheme.
- Added `clash_list_results` paging fields (`resultOffset`, `nextResultOffset`, `hasMoreResults`) and `includeAllStatuses`.
- Added scalar `query` for `find_items`; legacy host DTO `queries` remains accepted below the MCP tool layer.
- Added `AgentRuntime.BeginInteractiveOperation` coverage for panel Clash grouping around `PumpDispatcherOnce`.
- Added abandoned-dispatch watchdog recovery for deferred request gate when the UI dispatcher control is no longer available.
- Added warnings/logging for key Clash rollback/restore failures.
- Added public `McpConfigurator --remove`, Inno `[UninstallRun]`, and fixed the literal `<UNPACKED_PACKAGE_DIR>` placeholder in package config output.
- Added TTL and overflow cleanup for `McpTaskTimerService` without changing the public MCP timer contract.
- Added additive `protocolVersion=1` diagnostics/wire metadata:
  - MCP host request/response envelopes include `protocol_version`;
  - host discovery records and `host_status` include `protocolVersion`;
  - `mcp_diagnostics` and `mcp_health_check` expose `protocolVersion`.
- Added additive host log path diagnostics:
  - `Logger.GetLogFilePath()` is public and reused by the WPF panel;
  - host discovery records, `host_status`, and `mcp_health_check` expose `hostLogFilePath`.
- Added bounded log rotation:
  - in-process NavisHelper host log rotates at 5 MB with three backups;
  - MCP JSONL call log rotates at 5 MB with three backups;
  - `mcp_recent_calls` reads across the current MCP log and rotated backups.
- Fixed host error response timing:
  - `AgentHostService.WriteError` now emits measured `elapsed_ms` instead of a hard-coded zero for host error envelopes.
- Removed silent catch blocks from `DocumentCommandService.Clash.cs` by adding `Logger.Error(...)` diagnostics for reflection/fallback and view cleanup/restore failures.
- Removed silent catch blocks from `DocumentCommandService.Viewpoints.cs` by adding `Logger.Error(...)` diagnostics for viewpoint import fallback and restore failures.
- Removed silent catch blocks from `DocumentCommandService.SubtreeDump.cs` by adding `Logger.Error(...)` diagnostics for job cleanup/path fallback failures.
- Removed remaining silent catch blocks from `SearchService.cs` by adding `Logger.Error(...)` diagnostics for VariantData conversion fallback failures.
- Removed remaining empty catch blocks from the core/plugin/MCP paths scanned for release prep:
  - `ClashSettings`, `SelectionBoxSettings`, `KeyboardHook`, `SectionBoxHelper`, and `ViewpointCameraHelper` now log best-effort fallback failures through `Logger.Error(...)`;
  - `rg "catch\s*\{\s*\}" NavisHelper\Agent NavisHelper\Core NavisHelper.McpServer NavisHelper.McpConfigurator` returns no matches.
- Hardened manual wildcard matching:
  - `SearchService.WildcardMatches` now catches `RegexMatchTimeoutException`, logs it through `SearchMcp`, and returns `false` instead of surfacing a generic `command_failed`.
- Capped Clash preview geometry expansion:
  - `ClashPreviewManager.AddGeometryItems` now limits per-side geometry descendant expansion to 2000 items and logs cap/expansion failures through `ClashPreview`;
  - this reduces the high-level-node performance/scope risk called out by final external review while preserving ordinary descendant-based coloring for normal clash side items.
- Added COM RCW release discipline around screenshot exporters:
  - MCP Clash report screenshots release COM options/property RCWs after `DriveIOPlugin`;
  - WPF Clash orbit GIF frame capture uses the same options/property release pattern;
  - the shared `ComApiBridge.State` RCW is intentionally not released by screenshot helpers because live Navisworks testing showed that releasing it can detach the shared COM wrapper and break following exports with "COM object that has been separated from its underlying RCW" errors.
- Improved WPF batch Clash Viewpoint UX:
  - repeated batch starts now show a visible status message and are logged instead of silently returning;
  - per-row viewpoint creation failures are aggregated into the final status/log summary with row context;
  - failed restore after a batch is logged instead of being swallowed.
- Added generated MCP command catalog coverage:
  - `scripts/check_mcp_command_catalog.py` extracts implemented tools from `[McpServerTool]` and `[Description]` attributes;
  - `docs/NAVISWORKS_MCP_COMMAND_CATALOG.md` now contains a generated implemented-tool index covering all 68 current MCP tools;
  - `python scripts/check_mcp_command_catalog.py` fails when implemented tools are missing from the catalog.
- Hardened distribution package output:
  - `tools/package_distribution.ps1` removes `.pdb` debug symbols from generated packages before writing `manifest.json` and ZIP;
  - package manifest records `debug_symbols_excluded=true` and the removed debug-symbol count;
  - package README now marks Python smoke tests as optional validation, not an installation/runtime requirement;
  - `tools/publish_mcp_server.ps1` now writes literal `<INSTALL_DIR>` instead of JSON-escaped `\u003cINSTALL_DIR\u003e` and also marks standalone smoke as optional.
- Added the first non-Navisworks unit-test project:
  - `NavisHelper.McpServer.Tests` is included in `NavisHelper.sln`;
  - `ElapsedTimeFormatterTests` cover MCP elapsed-time formatting, user-message output, and report threshold behavior;
  - `NavisHelper.McpServer` exposes internals to the test assembly only.
- Extracted testable Clash group-name helper logic:
  - `ClashGroupNameHelper` in `NavisHelper.Contracts` now owns NavisHelper side tags, leading-number stripping, group match keys, prefix matching, group source selection, clean group-name shaping, side-tagged final names, existing-group logical matching, and cleanup decisions for prefix-aware/empty groups;
  - `DocumentCommandService.Clash.cs` delegates the previously private group-name matching/name-shaping/cleanup predicates to the shared helper;
  - unit tests cover numbered-group matching, side-tag canonicalization, prefix-aware matching after renumbering, owner-name/path source fallback, sanitized prefixes, fallback group names, canonical side tags, exact-before-logical existing matches, different-side rejection, prefix cleanup, and planned-group preservation after renumbering.
- Extracted testable Clash status-filter helper logic:
  - `ClashStatusFilterHelper` in `NavisHelper.Contracts` now owns all-status markers, include-all override detection, default `New/Active` behavior, and status matching;
  - `clash_list_results` preserves its backward-compatible empty-filter behavior while grouping/report/viewpoint/cluster workflows preserve default `New/Active`;
  - unit tests cover `*`/`All`/`Any`, explicit filters with `includeAllStatuses`, trimming/deduplication, and the list-results vs report defaults.
- Extracted testable Clash renumber name-shaping logic:
  - `ClashRenumberNameHelper` in `NavisHelper.Contracts` now owns number formatting, renumber name construction, side-tag preservation, and name-part sanitization;
  - `DocumentCommandService.Clash.cs` keeps thin wrappers so the production call sites stay stable;
  - unit tests cover minimum width formatting, preserving or stripping old numbers, number-only names, default separator behavior, side-tag preservation after truncation, and existing sanitizer behavior.
- Extracted testable Clash renumber plan/options logic:
  - `ClashRenumberPlanHelper` in `NavisHelper.Contracts` now normalizes renumber `scope`, `orderBy`, `startNumber`, `numberWidth`, and `limit`; it also builds `ClashRenumberPlanItem` rows from pure metadata sources and owns planned/skipped count calculation;
  - `DocumentCommandService.Clash.cs` still owns Navisworks `SavedItem` enumeration, sorting, and `TestsEditDisplayName` apply behavior, but delegates row construction and unchanged/planned status decisions to the helper;
  - unit tests cover option aliases/defaults/clamping/invalid values, sequential numbering, metadata propagation, unchanged-item skipped counts, and null-source tolerance.
- Hardened Clash report output overwrite safety:
  - `ClashReportOutputHelper` in `NavisHelper.Contracts` now owns the NavisHelper report marker name, protected report filenames, marker-required overwrite policy, standard report artifact paths, and screenshot file/relative-path construction;
  - `ClearClashReportOutputDirectory` refuses to overwrite `images`, `report.html`, `manifest.json`, or `clash_boxes.json` unless the output directory carries `.navishelper_clash_report`;
  - `ClashGenerateReport` now delegates `report.html`, `manifest.json`, `clash_boxes.json`, `images`, and `clash_000001[_top].ext` path construction to the helper while preserving public response paths;
  - unit tests cover marker-required decisions for images and protected report files, case-insensitive filename detection, non-report files, artifact path construction, padded screenshot filenames, and relative/absolute screenshot paths.
- Extracted testable Clash result paging logic:
  - `ClashResultPagingHelper` in `NavisHelper.Contracts` now owns fixed-limit paging for list/cluster responses and returned-count paging for report/viewpoint batches;
  - `ClashListResults`, `ClashListClusters`, `ClashGenerateReport`, and `ClashSaveViewpoints` now delegate `nextResultOffset`/`hasMore` calculations to the helper while preserving existing offset semantics;
  - unit tests cover normal pages, final partial pages, `offset > total`, and cancellation-forced continuation behavior.
- Extracted testable Clash cluster key logic:
  - `ClashClusterKeyHelper` in `NavisHelper.Contracts` now owns whitespace/case normalization for model-derived cluster association keys, typed `id`/`path`/`source`/`leaf` key construction, cluster mode/report-mode aliases, and cluster list/preview limit clamping;
  - `ResolveClashAssociationSide` now delegates stable-id, named-path, source-file, and leaf-path key construction to the helper while preserving existing key prefixes and fallback `unknown:` behavior;
  - `ClashListClusters` and `ClashGenerateReport` now delegate cluster `groupMode` normalization and cluster list/preview limit clamping to the helper while preserving existing MCP schema-violation messages;
  - unit tests cover whitespace collapse, lower-casing, prefix normalization, empty typed-key values, mode aliases, report `none` aliases, and cluster limit clamping.
- Extracted testable Clash report option logic:
  - `ClashReportOptionHelper` in `NavisHelper.Contracts` now owns report limit clamping, cluster-members-in-HTML clamping, report box-mode normalization, screenshot profile aliases, screenshot format overrides, screenshot dimension validation, JPEG quality validation, and post-process detection;
  - `DocumentCommandService.Clash.cs` delegates these rules through thin wrappers while preserving existing MCP schema-violation messages;
  - unit tests cover report/member limits, box-mode aliases, screenshot profile defaults, format overrides, dimension/quality overrides, invalid profile/format/dimension/quality messages, and source/BMP post-process behavior.
- Extracted testable Clash report accumulation logic:
  - `ClashReportAccumulationHelper` in `NavisHelper.Contracts` now owns append/report-file accumulation for report DTOs: row merge by global index, accumulated result counts, returned status counts, warning merge/deduplication, accumulated viewpoint/screenshot/transparency counters, and cluster carry-forward;
  - `DocumentCommandService.Clash.cs` still owns the JSON clone used for report file serialization, then delegates pure accumulation to the helper;
  - unit tests cover null current responses, previous/current row merging with current-row overwrite, sorted global indexes, accumulated counters, warning merge behavior, current-vs-previous cluster precedence, and status-count normalization.
- Extracted testable Clash handle logic:
  - `ClashHandleHelper` in `NavisHelper.Contracts` now owns canonical `clash-test:n` and `clash-result:t:r` handle construction plus existing `clash-test` handle parsing rules;
  - `DocumentCommandService.Clash.cs` keeps the existing private wrapper methods so MCP call sites and invalid-handle error messages remain stable;
  - unit tests cover canonical handle construction, empty handles for invalid indexes, prefixed and raw integer test handles, case/whitespace tolerance, and rejected result/blank handles.
- Extracted testable Clash bbox/matrix option logic:
  - `ClashBboxOptionHelper` in `NavisHelper.Contracts` now owns bbox root-mode aliases, bbox refine-depth validation, bbox root/candidate/preview limit clamping, pair-test creation limit clamping, and matrix selected-item limit clamping;
  - `DocumentCommandService.Clash.cs` delegates through existing private wrappers while preserving current `rootMode` and `refineDepth` schema-violation messages;
  - unit tests cover root-mode aliases, invalid root mode, refine-depth bounds, default/min/max clamping for root items, candidate pairs, preview rows, pair-test creation limit, and matrix selected items.
- Extracted testable Clash manage-operation normalization:
  - `ClashManageOperationHelper` in `NavisHelper.Contracts` now owns `clash_manage_tests` operation aliases for run, reset, compact, rename, delete, move, sort, and set_settings, including the existing Russian aliases;
  - `DocumentCommandService.Clash.cs` keeps the existing private wrapper so invalid operation schema-violation messages remain stable;
  - unit tests cover canonical operations, English aliases, Russian aliases, whitespace/case normalization, and invalid/blank operation handling.
- Extracted testable Clash test-type normalization:
  - `ClashTestTypeHelper` in `NavisHelper.Contracts` now owns canonical `testType` aliases for hard/intersection, hard_conservative/conservative, clearance, and duplicate, including the existing Russian aliases;
  - `DocumentCommandService.Clash.cs` keeps the Autodesk `ClashTestType` enum mapping and existing invalid `testType` schema-violation message;
  - unit tests cover English aliases, Russian aliases, punctuation/whitespace normalization, empty values, and invalid values.
- Extracted testable Clash scope-label formatting:
  - `ClashScopeLabelHelper` in `NavisHelper.Contracts` now owns `RequestedTestName` scope label construction for Clash grouping, renumbering, manage-tests, reports, and saved-viewpoint batches;
  - `DocumentCommandService.Clash.cs` delegates through the existing private wrapper so response field names and call sites stay stable;
  - unit tests cover empty scopes, ordering, trimming, blank filtering, case-insensitive deduplication while preserving first casing, prefix labels, and `firstN` formatting.
- Extracted testable Clash report color parsing:
  - `ClashReportColorHelper` in `NavisHelper.Contracts` now owns optional `#RRGGBB`/`RRGGBB` parsing for report side color overrides while `DocumentCommandService.Clash.cs` keeps Autodesk `Color` construction and fallback defaults;
  - existing invalid color schema-violation messages remain stable (`colorAHex`/`colorBHex` must be `#RRGGBB`);
  - unit tests cover optional hash, trimming, upper/lowercase hex, blank-as-no-override, invalid length/non-hex values, and the legacy permissive `NumberStyles.HexNumber` behavior for whitespace at the start of a hex pair.
- Extracted testable Clash numeric option normalization:
  - `ClashNumericOptionHelper` in `NavisHelper.Contracts` now owns non-negative integer clamping, positive double validation, non-negative double validation, and unit-range double validation used by Clash report/viewpoint/tolerance options;
  - `DocumentCommandService.Clash.cs` keeps parameter-specific schema-violation messages for `boxOffsetMm`, `clusterDistanceMm`, `contextTransparency`, and `toleranceMm`;
  - unit tests cover defaults, minimum bounds, infinity/NaN behavior, and intentionally preserve the existing behavior where positive/unit validators accept `NaN` while non-negative double validation rejects `NaN`/infinity.
- Extracted testable Clash test-name prefix normalization:
  - `ClashTestNamePrefixHelper` in `NavisHelper.Contracts` now owns `NH-BBOX` pair-test prefix defaults/sanitization and matrix prefix generation with deterministic timestamp-token replacement;
  - `DocumentCommandService.Clash.cs` still passes `DateTime.Now` at runtime, preserving generated `[NH-MATRIX] yyyyMMdd_HHmmss ` behavior;
  - unit tests cover pair prefix defaults, trim/sanitize/truncation behavior, generated matrix prefix timestamps, custom prefixes, CR/LF and double-space collapse, empty-prefix behavior, and required trailing spaces.
- Extracted testable Clash report HTML/status formatting:
  - `ClashReportHtmlFormatHelper` in `NavisHelper.Contracts` now owns report HTML escaping, report attribute escaping, status-count formatting, and Clash status sort ordering;
  - `DocumentCommandService.Clash.cs` delegates through existing wrappers so report HTML call sites remain stable;
  - unit tests cover empty status counts, Clash status ordering, custom status ordering, HTML text escaping, and path/backslash attribute normalization.
- Extracted testable Clash report screenshot sizing:
  - `ClashReportOptionHelper` now owns aspect-preserving target-size calculation for post-processed Clash report screenshots;
  - `DocumentCommandService.Clash.cs` delegates through the existing `CalculateImageTargetSize` wrapper so image export flow remains stable;
  - unit tests cover disabled bounds, no-upscale behavior, width-only and height-only bounds, choosing the smaller scale when both bounds apply, minimum 1px output, and invalid source dimensions preserving legacy output.
- Extracted testable Clash cluster stable utility logic:
  - `ClashClusterKeyHelper` now owns the stable 64-bit FNV-1a hex hash used by cluster ids and invariant spatial cell key formatting;
  - `DocumentCommandService.Clash.cs` delegates through the existing wrappers so cluster id and spatial grouping call sites remain stable;
  - unit tests cover known hash values, null/empty equivalence, and colon-separated positive/negative spatial cell coordinates.
- Extracted testable Clash report value logic:
  - `ClashReportValueHelper` in `NavisHelper.Contracts` now owns normalized text filters, status-count increments, and first-value fallback formatting used by Clash report/cluster flows;
  - `DocumentCommandService.Clash.cs` delegates through existing wrappers so report filtering, counters, and cluster item-name call sites remain stable;
  - unit tests cover null/blank filter handling, trim/dedupe behavior, null-safe status counters, `Unknown` status fallback, and null/empty first-value handling.
- Split read-only/planning Clash methods out of `DocumentCommandService.Clash.cs`:
  - `DocumentCommandService.Clash.Listing.cs`: `ClashListTests`, `ClashListResults`, `ClashListClusters`;
  - `DocumentCommandService.Clash.Planning.cs`: `ClashBboxPairPlan`.
- Verified by external tester:
  - grouping dry-run/apply on test 002;
  - renumber dry-run/apply;
  - repeated grouping after renumbering does not create duplicates;
  - `overwriteExisting=true` reuses numbered groups;
  - `groupNamePrefix=NHX-` plus regrouping removes empty old groups and warns;
  - document was not saved after live tests.

## Important Dirty Files

- `NavisHelper/Agent/Services/DocumentCommandService.Clash.cs`: core Clash Detective grouping, renumbering, cluster/report logic.
- `NavisHelper/Agent/Services/DocumentCommandService.Clash.Listing.cs`: read-only Clash test/result/cluster listing.
- `NavisHelper/Agent/Services/DocumentCommandService.Clash.Planning.cs`: read-only/file-output Clash bbox pair planning.
- `NavisHelper.McpServer/Tools/NavisworksClashTools.cs`: MCP surface for clash tools.
- `NavisHelper.Contracts/HostContracts.cs`: request/response DTOs.
- `NavisHelper.Contracts/Statuses.cs`: host command names.
- `NavisHelper/Agent/Host/AgentHostService.cs`: host command dispatch.
- `NavisHelper.McpServer/Services/HostBridgeClient.cs`: MCP-to-host bridge.
- `NavisHelper/WPF/NavisHelperPanel.cs`: Clash UI refresh/stale row behavior.
- `NavisHelper/ClashPreviewManager.cs`: clash preview fallback behavior.
- `NavisHelper/Core/ScreenUpdateSuppressor.cs`: experimental screen update suppression.
- `NavisHelper.McpServer/Services/*Timing*`, `NavisHelper.McpServer/Services/NavisworksLaunchService.cs`, `NavisHelper.McpServer/Services/NavisworksRecentFilesService.cs`, `NavisHelper.McpServer/Tools/NavisworksStartupTools.cs`: startup/recent-files/timing work.
- `docs/MCP_TOOL_CONTRACTS.md`, `docs/MCP_CLIENT_GUIDE.md`, `docs/NAVISWORKS_MCP_COMMAND_CATALOG.md`: tool documentation.

## Current Risks And Release Gates

- Working tree is intentionally dirty on `main`; do not reset or revert.
- The current release slice is being versioned and published as `v2.4.1.0`.
- Initial external architecture review results were captured in `<temp>`; the first stabilization items from that review have been addressed.
- Final read-only Claude review was run after the stabilization/refactor/package checks. Static verdict: no new code/static blocker that should stop RC on its own; RC remains blocked by mandatory live Navisworks/WPF/installer gates listed below.
- Focused follow-up Claude review was run after the post-review `WildcardMatches` timeout fix and `ClashPreviewManager` geometry cap. Follow-up verdict: no new static blockers or obvious regressions from those changes.
- Live Navisworks 2027 MCP regression was run from the installed per-user bundle/MCP server against a recent `.nwd` model:
  - `start_navisworks` opened the model and the MCP host reported the installed bundle assembly under `%APPDATA%\Autodesk\ApplicationPlugins\NavisHelper.bundle\Contents\2027`;
  - `clash_manage_tests operation=run` on test `002. 240000-ЭК-vs-240101-АТХ4` produced 470 clash results;
  - `clash_group_results` dry-run/apply planned 38 groups, applied 38 groups, and moved 465 results;
  - `clash_renumber_results` dry-run/apply and repeated grouping after renumbering were rechecked; regrouping moved 465 results and cleaned up 38 empty old NavisHelper groups;
  - `clash_generate_report apply=true` for 2 results created HTML/JSON artifacts, 3 saved viewpoints, and 4 screenshots (`clash_000001.jpg`, `clash_000001_top.jpg`, `clash_000002.jpg`, `clash_000002_top.jpg`) without the previous COM RCW-separated warnings;
  - `clash_save_viewpoints apply=true` for 1 result with `createOppositeViewpoints=true` created 2 saved viewpoints without warnings;
  - live artifacts are under `artifacts\live-gate\live_regression_fixed.json`, `artifacts\live-gate\live_viewpoint_smoke.json`, and `artifacts\live-gate\clash-report-smoke-fixed`.
- WPF panel manual smoke found a real `Сохр. VP` defect: the folder/viewpoint was created, but activating it did not restore the clash section box/zoom state expected from the current clash preview. The likely root cause was that `SavedViewpointAppearanceHelper.SaveCurrentViewWithAppearanceOverrides` could fail to refresh the COM-inserted saved viewpoint through the current .NET folder tree before `SavedViewpoints.ReplaceFromCurrentView`, leaving a COM-created viewpoint without the complete current view/clip state.
- The WPF `Сохр. VP` defect was fixed by resolving the refreshed saved-viewpoint folder from `document.SavedViewpoints.RootItem` after COM insertion and before `ReplaceFromCurrentView`. The fixed bundle/MCP artifacts were rebuilt, packaged, and reinstalled per-user from `artifacts\distribution\NavisHelper-distribution-rc-check`.
- Manual WPF smoke also exposed a stale native-handle reload defect: selecting/loading clash results in the panel could fail with `Object has been Disposed (WeakRef) | NativeHandle` after Navisworks invalidated cached Clash Detective objects. `OnClashTestSelected` now detects this specific disposed-handle failure, resolves the current `ClashTest` again by display name from the active document, updates the row reference, and retries loading once instead of leaving the panel in an error state. Follow-up logging showed the first fix still re-read a disposed `_activeClashTest` inside `SaveActiveClashGroupsToCache`; cache-key fallback and selected-test capture now read Clash test display names through a safe helper and skip cache preservation when the old native handle is already disposed.
- WPF panel stale-row/status aggregation behavior was validated manually after reinstall: the panel loaded 372 tests and showed test `002. 240000-ЭК-vs-240101-АТХ4` with 470 results. The refreshed clash result loading and `Сохр. VP` saved-viewpoint flow now work correctly in the panel, including preserved viewpoint appearance/section state. Orbit GIF UI behavior remains a separate panel-driven smoke item.
- Manual WPF smoke also identified a lower-priority reset UX gap: after activating a saved clash viewpoint with saved appearance overrides, the Clash panel `Сброс` button clears the current preview manager state, section box, redlines, and selection, but it does not necessarily remove colors that were re-applied by the saved viewpoint's appearance overrides. The SDK-level full reset path is `Document.Models.ResetAllPermanentMaterials()`, which is intentionally broader and may remove unrelated permanent color/transparency overrides from other NavisHelper workflows. Do not wire this into the ordinary Clash `Сброс` button without an explicit UX decision/confirmation.
- Test data was mutated only in open Navisworks sessions and not saved; preserve any future reproducible regression case as a copy if needed.
- `DocumentCommandService.Clash.cs` is smaller but still large. Do not start a broad refactor without a mechanical migration boundary and build matrix after each slice.
- `ClashCreateMatrixFromSelection`, `ClashPairTestsCreate`, `ClashManageTests`, `ClashGenerateReport`, and `ClashSaveViewpoints` still contain mutation/report workflows and should not be split casually because they carry apply/rollback/progress semantics.
- Inno installer compilation is now closed locally. `ISCC.exe` was resolved from the user Inno Setup install, the incompatible `[UninstallRun]` `runasoriginaluser` flag was removed, and `tools\build_installer.ps1 -AppVersion 2.4.1.0 -Runtime win-x64 -PackageName NavisHelper-distribution-rc-check` produced `artifacts\installer\NavisHelperSetup-2.4.1.0.exe`.
- Before any new release or release candidate:
  - run external Claude review per `AGENTS.md`;
  - address final external-review quick wins that are chosen for RC scope;
  - run full build matrix again;
  - install bundle and MCP server from final artifacts;
  - rerun live MCP regression on a test NWD with saved Clash Detective results if code changes after this checkpoint;
  - rerun live screenshot/export smoke for `clash_generate_report` if code changes after this checkpoint;
  - run a live WPF panel smoke for Clash orbit GIF and batch Clash Viewpoint UI status aggregation with one successful batch and one forced/observed failure path;
  - run `tools/package_distribution.ps1` and, on a machine with Inno Setup, `tools/build_installer.ps1`;
  - verify `McpConfigurator --remove --dry-run` and installer uninstall behavior on a disposable client config;
  - decide whether to commit dirty post-release work as one checkpoint or split into focused commits.

## Current Clean Slice Verification

Latest verified local checks after the stabilization and extraction slices:

- `git diff --check`: passed.
- `dotnet build NavisHelper.McpServer -c Release --no-restore`: passed after MCP contract updates.
- MCP stdio schema smoke: `tools/list` passed; `find_items` exposes scalar `query` and no list-style `queries`; `clash_list_results` exposes `includeAllStatuses`, `resultOffset`, and `statusFilters`; current dirty tool count is 68.
- MCP stdio timer smoke: `mcp_task_timer_start` and `mcp_task_timer_finish` passed after timer TTL cleanup.
- MCP stdio diagnostics smoke: `mcp_diagnostics` returned `protocolVersion=1`; current dirty tool count remains 68.
- MCP stdio recent-calls smoke: `mcp_recent_calls` returned recent JSONL lines after log rotation support; current dirty tool count remains 68.
- `NavisHelper.McpConfigurator.exe --remove --clients claude-desktop,codex,opencode --dry-run`: passed.
- `tools/package_distribution.ps1 -SkipBuild -Runtime win-x64 -PackageName NavisHelper-distribution-config-clean-check`: passed.
- `tools/package_distribution.ps1 -SkipBuild -Runtime win-x64 -PackageName NavisHelper-distribution-rc-check`: passed.
- Generated distribution check:
  - package root `mcp-client-config.example.json` contains literal `<UNPACKED_PACKAGE_DIR>`;
  - nested `McpServer\mcp-client-config.example.json` contains literal `<INSTALL_DIR>`;
  - package folder and ZIP contain zero `.pdb` files;
  - manifest records `debug_symbols_excluded=true` and `debug_symbol_file_count_removed=11`;
  - generated README marks Python smoke tests as optional validation.
- Packaged `McpConfigurator\NavisHelper.McpConfigurator.exe --remove --clients claude-desktop,codex,opencode --dry-run`: passed.
- `rg "catch\s*\{\s*\}" NavisHelper\Agent NavisHelper\Core NavisHelper.McpServer NavisHelper.McpConfigurator`: no matches.
- Final read-only Claude review: completed through `scripts\review\claude-review.ps1` with plain-text stdin context and tool-less Claude settings; static review found no new code/static RC blocker, and identified live Navisworks/WPF/installer gates as remaining RC blockers.
- Focused follow-up Claude review after post-review quick wins: completed through `scripts\review\claude-review.ps1`; no new static blocker or obvious regression found.
- `SearchService.WildcardMatches` timeout quick win from final review: addressed; regex timeout is logged and treated as no-match.
- `ClashPreviewManager` high-level-node geometry expansion risk from final review: mitigated with a per-side cap and logging; live visual/performance smoke remains required.
- Per-user install from `artifacts\distribution\NavisHelper-distribution-rc-check\Install-NavisHelperBundle.ps1 -User -ConfigureMcp`: passed; bundle, MCP server, MCP configurator, and supported MCP client configs were updated.
- Installed MCP server stdio tool-list smoke: passed; 68 tools, including `clash_group_results`, `clash_renumber_results`, `start_navisworks`, and `mcp_diagnostics`.
- Live Navisworks 2027 latest-file/startup smoke: passed for the most recent `.nwf` host startup path; the opened `.nwf` had Clash Detective tests but no saved results in the inspected scope, so the regression was continued on the recent `.nwd` model.
- Live Navisworks 2027 `.nwd` Clash regression from installed artifacts: passed for run/group/renumber/regroup/report screenshot export/viewpoint creation on test `002. 240000-ЭК-vs-240101-АТХ4`.
- Live `clash_generate_report` screenshot/export smoke after the COM RCW fix: passed for 2 results, with 4 screenshot files and no COM RCW-separated warnings.
- Live `clash_save_viewpoints` smoke: passed for 1 result with `createOppositeViewpoints=true`, creating 2 saved viewpoints without warnings.
- `python scripts/check_mcp_command_catalog.py`: passed after generated catalog update; catalog covers all 68 implemented MCP tools.
- `dotnet test NavisHelper.McpServer.Tests\NavisHelper.McpServer.Tests.csproj --configuration Release --no-restore`: passed, 409 tests.
- `dotnet build NavisHelper.McpServer\NavisHelper.McpServer.csproj --configuration Release --no-restore`: passed.
- Full build matrix `Release2024`, `Release2025`, `Release2026`, `Release2027` with `Platform=x64`: passed after each plugin-affecting slice, including the latest Clash report value helper extraction.
- Full build matrix `Release2024`, `Release2025`, `Release2026`, `Release2027` with `Platform=x64`: passed again after the WPF saved-viewpoint refreshed-folder fix.
- `tools/package_distribution.ps1 -SkipBuild -Runtime win-x64 -PackageName NavisHelper-distribution-rc-check`: passed again after the WPF saved-viewpoint refreshed-folder fix.
- Per-user install from the refreshed `artifacts\distribution\NavisHelper-distribution-rc-check`: passed again after the WPF saved-viewpoint refreshed-folder fix.
- Installed MCP server stdio tool-list smoke after reinstall: passed; 68 tools, no missing required clash/startup/diagnostics tools.
- Full build matrix, package generation, per-user install, and installed MCP tool-list smoke also passed after the WPF stale `WeakRef/NativeHandle` Clash test reload fix.
- Full build matrix, package generation, per-user install, installed 2027 DLL hash verification, installed MCP tool-list smoke, `dotnet test`, `git diff --check`, and MCP catalog check passed for the versioned `v2.4.1.0` publication build. The installed per-user `Contents\2027\NavisHelper.dll` matches the repo bundle by SHA-256 `2925B8549975C7664D374CEC99002F725AD535B257D5AA91823D925AC4784EEF`, has timestamp `2026-07-07T14:38:15.5907864+03:00`, and reports file/product version `2.4.1.0`.
- `tools\build_installer.ps1 -AppVersion 2.4.1.0 -Runtime win-x64 -PackageName NavisHelper-full-win-x64-framework-dependent-2.4.1.0`: passed with Inno Setup 6.7.3; ZIP artifact `artifacts\distribution\NavisHelper-full-win-x64-framework-dependent-2.4.1.0.zip`, SHA-256 `541581BB6662E96A200687AB52CC19D300A8570D10AA5BE6D6785BA1EB306606`; installer artifact `artifacts\installer\NavisHelperSetup-2.4.1.0.exe`, SHA-256 `9F966946B49F6033B8E2DDB281F87036068EDFF75D5E9E9FFA32AB264D282CEE`.
- Packaged `McpConfigurator --remove --clients all --dry-run` and `--configure --clients all --create-missing --dry-run` both passed after the final installer review. `--create-missing` is intentionally present in the installer postinstall configure command and Start Menu configure shortcut so fresh client config files can be created.
- Known warning remains: `RibbonLoader.cs(170,35): CS0067 CanExecuteChanged never used`.

## External Review Follow-up Status

From `<temp>`, the short next-fix list is now mostly addressed:

1. Deferred request gate watchdog for dead UI controls: addressed in `AgentHostService`.
2. `PumpDispatcherOnce` in panel grouping under interactive scope: addressed in `NavisHelperPanel`.
3. Restore/rollback warnings in Clash service: addressed for the reviewed rollback/restore paths.
4. `--remove`, `[UninstallRun]`, and `<UNPACKED_PACKAGE_DIR>` placeholder: addressed and package-verified.
5. `clash_list_results` paging/`includeAllStatuses` and scalar `find_items query`: addressed and MCP schema-smoked.
6. `McpTaskTimerService` timer TTL/cleanup: addressed and MCP timer-smoked.
7. `protocol_version` wire/diagnostic metadata: addressed additively and MCP diagnostics-smoked.
8. `host_log_path` diagnostics: addressed additively through `hostLogFilePath`.
9. `Logger.cs` rotation: addressed for host log and MCP JSONL log.
10. `WriteError elapsed_ms=0`: addressed with measured connection elapsed time in host error envelopes.
11. Silent catches in `DocumentCommandService.Clash.cs`: addressed with `ClashMcp` logging.
12. Silent catches in `DocumentCommandService.Viewpoints.cs`: addressed with `ViewpointsMcp` logging.
13. Silent catches in `DocumentCommandService.SubtreeDump.cs`: addressed with `SubtreeDumpMcp` logging.
14. Silent catches in `SearchService.cs`: addressed with `SearchMcp` logging.
15. COM RCW release discipline around screenshots: addressed for MCP Clash report screenshots and WPF Clash orbit GIF frame capture; live MCP report export was rechecked after fixing shared `ComApiBridge.State` RCW release.
16. Batch-VP error aggregation/status-bar UX: addressed for WPF Clash Viewpoint batch creation. Manual WPF smoke found and fixed a related single-viewpoint persistence issue in `SavedViewpointAppearanceHelper` refreshed-folder handling; the fixed flow was rechecked in Navisworks after reinstall.
17. Full catalog generation from `[Description]` attributes: addressed with a generated implemented-tool index and check script.
18. Unit-test baseline: addressed with the first MCP-server pure-helper test project.
19. Clash group-name matching unit coverage: addressed for side tags, leading renumber prefixes, match keys, and prefix-aware grouping.
20. Clash status-filter normalization unit coverage: addressed for all-status markers, default status scopes, warning signal, and matching.
21. Clash renumber name-shaping unit coverage: addressed for number formatting, name construction, side-tag preservation, and sanitizer behavior.
22. Distribution package `.pdb`, placeholder, optional smoke wording, and packaged remove dry-run checks: addressed.
23. Clash report overwrite safety for fixed report artifacts: addressed by requiring `.navishelper_clash_report` before overwriting `images`, `report.html`, `manifest.json`, or `clash_boxes.json`.
24. Clash result paging helper extraction: addressed for list-results, clusters, report generation, and saved-viewpoint batches.
25. Clash group-name planning helper extraction: addressed for owner source selection, clean group names, side tags, and final MCP group names.
26. Clash existing-group matching/cleanup helper extraction: addressed for exact/logical group matching, prefix-aware ungrouping decisions, and empty-group cleanup decisions after renumbering.
27. Clash renumber plan-helper extraction: addressed for option normalization, pure plan-item construction, and planned/skipped counts; live rename apply was rechecked through `clash_renumber_results` on Navisworks 2027.
28. Clash cluster key helper extraction: addressed for model-derived cluster association key normalization and typed key construction.
29. Clash report artifact path helper extraction: addressed for standard report paths and screenshot filename/relative-path construction; live screenshot/export behavior was rechecked through `clash_generate_report` on Navisworks 2027.
30. Clash cluster option normalization helper extraction: addressed for `groupMode`/report `groupMode` aliases and cluster list/preview limit clamping.
31. Clash report option helper extraction: addressed for report/member limits, box mode, screenshot profile/format/dimension/quality normalization, and post-process detection; live screenshot/export behavior was rechecked through `clash_generate_report` on Navisworks 2027.
32. Clash report accumulation helper extraction: addressed for append/report-file row merge, accumulated counters, warning merge, returned status counts, and cluster carry-forward; live report generation was rechecked through `clash_generate_report` on Navisworks 2027.
33. Clash handle helper extraction: addressed for canonical test/result handle construction and existing test-handle parsing behavior.
34. Clash bbox/matrix option helper extraction: addressed for bbox root/refine option normalization and bbox/pair/matrix limit clamping.
35. Clash manage-operation helper extraction: addressed for `clash_manage_tests` operation alias normalization.
36. Clash test-type helper extraction: addressed for canonical `testType` alias normalization while keeping SDK enum mapping in the plugin service.
37. Clash scope-label helper extraction: addressed for `RequestedTestName` formatting across Clash write/report/viewpoint responses.
38. Clash report color helper extraction: addressed for optional `#RRGGBB`/`RRGGBB` report color override parsing while keeping Autodesk `Color` mapping in the plugin service.
39. Clash numeric option helper extraction: addressed for shared non-negative/result-offset clamping and double option validation while preserving existing edge-case behavior.
40. Clash test-name prefix helper extraction: addressed for `NH-BBOX` pair-test prefix normalization and `[NH-MATRIX] yyyyMMdd_HHmmss ` matrix prefix generation.
41. Clash report HTML/status formatting helper extraction: addressed for report HTML escaping, attribute escaping, status-count formatting, and status sort order.
42. Clash report screenshot sizing helper extraction: addressed for aspect-preserving post-process target-size calculation.
43. Clash cluster stable utility helper extraction: addressed for stable cluster-id hashing and spatial cell key formatting.
44. Clash report value helper extraction: addressed for normalized text filters, status counters, and first-value fallback formatting.
45. Final-review wildcard timeout quick win: addressed for manual/root wildcard matching by catching `RegexMatchTimeoutException`.
46. Final-review Clash preview geometry expansion risk: mitigated with per-side expansion cap and logging.

Remaining items from the review that are not yet closed:

- Final external review and focused follow-up review found no remaining static/code blockers. A final installer-gate Claude review flagged only configurator behavior checks; `--remove --clients all` and install-time `--create-missing` were verified with packaged dry-runs and the installer script keeps `--create-missing`.
- Live WPF validation is closed for clash result loading after the stale `WeakRef/NativeHandle` retry fix and for `Сохр. VP` after the refreshed-folder fix. Orbit GIF UI behavior remains open because it is panel-driven and not exposed through MCP.
- Deferred WPF UX item: decide whether to add a separate explicit "clear saved viewpoint appearance/permanent materials" action, likely with confirmation, instead of overloading the ordinary Clash `Сброс` button.
- Broader unit coverage remains open for Navisworks-dependent Clash group apply/move/delete mechanics, but the current apply/move/regroup/renumber path has live Navisworks 2027 coverage.
- Live `clash_generate_report` screenshot/export smoke is closed for the current installed artifacts; WPF orbit GIF screenshot/export smoke remains open because it is panel-driven and not exposed through MCP.
- Inno installer compilation is closed for the current artifacts; the only installer-script change was removing unsupported `runasoriginaluser` from `[UninstallRun]`.

## Post-2.4 Cleanup Progress

Current cleanup branch: `post-2.4-cleanup`.

Completed cleanup phases:

- Phase 0: the post-release dirty tree was captured on the cleanup branch in focused commits covering MCP host audit fixes, Clash panel interactive gating, MCP helper regression tests, and docs/installer/package updates. Gates passed: `dotnet test NavisHelper.McpServer.Tests/NavisHelper.McpServer.Tests.csproj /p:Configuration=Release`, full `Release2024`/`Release2025`/`Release2026`/`Release2027` x64 build matrix, and `git diff --check HEAD`.
- Phase 1: compiled `NavisHelper.bundle/Contents/<version>/*.dll` and `*.pdb` files were removed from git tracking without history rewrite. Bundle binaries are now ignored local build/release artifacts; source keeps bundle structure, configs, icons, and packaging rules. Gates passed: bundle DLL/PDB no longer appear in `git ls-files`, unit tests pass, and `tools/package_distribution.ps1 -Runtime win-x64 -PackageName NavisHelper-phase1-gate` produced a package.
- Phase 2: root documentation was reduced to `README.md`, `CLAUDE.md`, `AGENTS.md`, and `BUILD_BUNDLE_RULES.md`. MCP catalog/quickstart/plan moved to `docs/`, install/update prompts moved to `docs/prompts/`, section-box research moved to `docs/research/`, and the closed `TASK_find_items_source_file_support.md` was removed. `AGENTS.md` now records the documentation language policy.
- Phase 3: the unused root-level SDK-style `NavisHelper.csproj` was removed so the repository has a single `NavisHelper` project path, ignored `tmp_*`/reflection scratch files and intermediate phase-gate packages were cleaned from disk, and the ignored `api/` vendor tree was left untracked because it is about 26 MB. The only SDK sample fact currently referenced by agent docs is now captured in `docs/research/navisworks-api-notes.md`.
- Phase 4: `.github/workflows/ci.yml` now runs the non-Navisworks CI subset on Windows with .NET 9: `NavisHelper.Contracts`, `NavisHelper.McpServer`, `NavisHelper.McpConfigurator`, and `NavisHelper.McpServer.Tests`. The README includes the CI badge, and agent docs state that the full Navisworks plugin build matrix remains local-only because GitHub runners do not have the Autodesk SDK. Local workflow-equivalent gates passed.

Deferred cleanup phases:

- Phase 5: optional code-debt items, starting with contract serialization round-trip tests before broader host dispatch or pipe-write refactors.

Current Clash pipeline extraction status: slices 7-14 (PRs #11-#18) extracted the saved-viewpoint creation, marker/top-view handling, screenshot set orchestration, document-state restore, report/save-viewpoint scope paging, report item processing, and shared scope/page core paths. The fifteenth slice extracted shared Clash test mutation primitives, the sixteenth slice extracted matrix apply/rollback mutation behavior, the seventeenth slice extracted shared pure cluster partitioning, and the eighteenth slice extracted report/save-viewpoint response DTO shaping. The current state has build-matrix coverage; the next architecture step should choose another bounded response/contract or host-lifecycle domain and keep the same small-PR and build-matrix cadence, with tester smoke when Navisworks-visible behavior changes.

First Clash pipeline architecture slice after cleanup:

- `ClashGroupMutationService` now owns the Navisworks-dependent `ClashResultGroup` mutation path for MCP grouping: find/create group, rebuild group membership, ungroup existing NavisHelper groups, remove empty groups, and saved-item identity/location matching.
- `DocumentCommandService.Clash.cs` keeps request validation, dry-run planning, response shaping, transaction boundary, warnings, and MCP-visible status semantics.
- No public MCP tool names, parameters, or response fields changed in this slice.

Second Clash pipeline architecture slice:

- `ClashRenumberMutationService` now owns the Navisworks-dependent apply path for `clash_renumber_results`: `TestsEditDisplayName`, per-item `renamed`/`error` status updates, applied flags, rename count, and warning text.
- `DocumentCommandService.Clash.cs` keeps renumber request validation, scope enumeration, pure plan building, confirmation gate, transaction boundary, and final response message.
- No public MCP tool names, parameters, or response fields changed in this slice.

Third Clash pipeline architecture slice:

- `ClashReportArtifactWriter` now owns the final artifact write step for `clash_generate_report`: `manifest.json`, `clash_boxes.json`, and `report.html`.
- `DocumentCommandService.Clash.cs` still owns report scope selection, run-tests handling, batching, cancellation, screenshot/viewpoint generation, accumulation, and HTML rendering.
- No public MCP tool names, parameters, response fields, or report artifact shapes changed in this slice.

Fourth Clash pipeline architecture slice:

- `ClashReportScreenshotCaptureService` now owns the current-view image export path for Clash reports: Navisworks COM image export, optional BMP source cleanup, image resize/transcode, JPEG quality handling, and COM option RCW release.
- `DocumentCommandService.Clash.cs` still owns when screenshots are taken, default/top-view camera setup, marker handling, screenshot counters, warning prefixes, and report item fields.
- No public MCP tool names, parameters, response fields, screenshot filenames, or report artifact shapes changed in this slice.

Fifth Clash pipeline architecture slice:

- `NavisHelper/Agent/Services/ClashReportOperationTracker.cs` now owns the active/last `clash_generate_report` operation state machine: start guard, status response shaping, cooperative cancellation flag, progress counters, completion, and failure state. The mutable runtime coordinator lives in the Navisworks host project; its wire response DTO remains in Contracts.
- `DocumentCommandService.Clash.cs` now delegates status/cancel/progress bookkeeping to the tracker while preserving the existing `host_busy` adapter and MCP-visible response fields/messages.
- Unit tests source-link this SDK-independent host service and cover empty status, start/reject, current item/progress updates, cancellation, completion, failure, and cancel-after-complete behavior.

Sixth Clash pipeline architecture slice:

- `ClashReportHtmlRenderer` now owns `clash_generate_report` HTML generation: summary, warnings, status sections, cluster summary, item tables, screenshot placeholders/images, and lightbox markup.
- `DocumentCommandService.Clash.cs` now delegates report HTML rendering to the Contracts helper while preserving artifact writing and report response shaping.
- Unit tests cover summary/warning/status rendering, HTML/attribute escaping, screenshot placeholders, cluster preview rows, weak-association labels, and truncation markers.

Seventh Clash pipeline architecture slice:

- `ClashSavedViewpointCreationService` now owns Navisworks-dependent saved-viewpoint target-folder resolution, reset viewpoint creation/refresh, unique viewpoint naming, appearance-aware save calls, and viewpoint path construction.
- `clash_generate_report` and `clash_save_viewpoints` now delegate saved-viewpoint creation/bookkeeping to the service while keeping camera setup, marker handling, screenshot capture, response item shaping, and batching in `DocumentCommandService.Clash.cs`.
- No public MCP tool names, parameters, response fields, screenshot filenames, or report artifact shapes changed in this slice.

Eighth Clash pipeline architecture slice:

- `ClashReportMarkerViewService` now owns Navisworks-dependent clash-point redline marker projection, active-view redline cleanup, marker sizing, and top-view camera setup for report screenshots.
- `clash_generate_report` and `clash_save_viewpoints` now delegate marker/top-view operations to the service while keeping report batching, preview/color setup, screenshot capture, and response item shaping in `DocumentCommandService.Clash.cs`.
- No public MCP tool names, parameters, response fields, screenshot filenames, or report artifact shapes changed in this slice.

Ninth Clash pipeline architecture slice:

- `ClashReportScreenshotSetService` now owns `clash_generate_report` screenshot-set orchestration: main current-view capture, optional top-view capture, top-view marker refresh, screenshot relative paths, capture counts, and warning prefixes.
- `DocumentCommandService.Clash.cs` still owns report batching, preview/color setup, saved-viewpoint creation, response item shaping, artifact accumulation, and operation progress.
- No public MCP tool names, parameters, response fields, screenshot filenames, warning prefixes, or report artifact shapes changed in this slice.

Tenth Clash pipeline architecture slice:

- `ClashDocumentStateService` now owns Navisworks-dependent original view/selection/clipping snapshot capture, per-clash viewpoint restore, final state restore, clipping-plane restore fallback, and restore warning/log shaping.
- `clash_generate_report` and `clash_save_viewpoints` now delegate document-state capture/restore to the service while keeping preview reset, batching, saved-viewpoint/report item shaping, screenshots, and operation progress in `DocumentCommandService.Clash.cs`.
- No public MCP tool names, parameters, response fields, warning text, saved-viewpoint behavior, screenshot filenames, or report artifact shapes changed in this slice.

Eleventh Clash pipeline architecture slice:

- `ClashReportScopePageService` now owns `clash_generate_report` scope/page preparation: matched-test count, result enumeration counters, status filters, item-name exclusion counters, large-report confirmation warning, result-offset empty warning, sorted page selection, returned-status counts, and initial page metadata.
- `DocumentCommandService.Clash.cs` still owns Clash test resolution/rerun, the large-report apply guard exception, clustering, dry-run item shaping, report generation, artifact writing, and operation progress.
- No public MCP tool names, parameters, response fields, warning text, paging semantics, screenshot filenames, or report artifact shapes changed in this slice.

Twelfth Clash pipeline architecture slice:

- `ClashReportScopePageService` now also owns `clash_save_viewpoints` scope/page preparation: matched-test count, result enumeration counters, status filters, item-name exclusion counters, large-viewpoints confirmation warning, result-offset empty warning, sorted page selection, returned-status counts, and initial page metadata.
- `DocumentCommandService.Clash.cs` still owns Clash test resolution, the large-viewpoints apply guard exception, dry-run saved-viewpoint item shaping, saved-viewpoint creation, progress, cancellation, and final returned-status recalculation after cancellation.
- No public MCP tool names, parameters, response fields, warning text, paging semantics, saved-viewpoint names/paths, or marker behavior changed in this slice.

Thirteenth Clash pipeline architecture slice:

- `ClashReportItemProcessingService` now owns per-item `clash_generate_report` processing: original-view restore before each item, preview display, clash box/camera/section setup, clash-point marker warning, saved-viewpoint creation, optional context transparency, screenshot-set capture, marker cleanup, per-item warning/error capture, and per-item counter deltas.
- `DocumentCommandService.Clash.cs` still owns batch iteration, cancellation, operation progress, cluster assignment lookup, final `ClashReportItem` DTO shaping, response counter aggregation, restore, accumulation, and artifact writing.
- No public MCP tool names, parameters, response fields, warning text, screenshot filenames, saved-viewpoint names/paths, or report artifact shapes changed in this slice.

Fourteenth Clash pipeline architecture cleanup slice:

- `ClashReportScopePageService` now uses one shared internal `BuildCore` path for both `clash_generate_report` and `clash_save_viewpoints`, with small response adapters preserving the report-specific and saved-viewpoint-specific response fields and warning text.
- Removed stale `DocumentCommandService.Clash.cs` alias constants for report limits, cluster-member limits, and screenshot profile defaults that are already owned by `ClashReportOptionHelper`.
- No public MCP tool names, parameters, response fields, warning text, paging semantics, saved-viewpoint behavior, screenshot filenames, or report artifact shapes changed in this slice.

Fifteenth Clash pipeline architecture slice:

- `ClashTestMutationService` now owns the Navisworks-dependent Clash test mutation primitives shared by `clash_manage_tests`, pair-test replacement, and matrix cleanup/rollback: test operation apply, settings-copy apply, saved-test location resolution, move, delete, and delete-list stabilization polling.
- `DocumentCommandService.Clash.cs` keeps request validation, dry-run planning, move/sort response shaping, progress/cancellation handling, warnings, and MCP-visible status/message semantics.
- No public MCP tool names, parameters, response fields, warning text, or Clash test naming/scope semantics changed in this slice.

Sixteenth Clash pipeline architecture slice:

- `ClashMatrixMutationService` now owns the Navisworks-dependent apply path for `clash_create_matrix_from_selection`: existing-name conflict checks after optional generated-test cleanup, Clash Test construction/copy insertion, created-test handle assignment, rollback removal, and restoration of removed generated-test copies.
- `DocumentCommandService.Clash.cs` keeps matrix input resolution, request/confirmation validation, dry-run previews, pair and ancestor planning, response shaping, run-after-create progress/cancellation, and final messages.
- No public MCP tool names, parameters, response fields, warning text, generated test names, selection pairing, or run-after-create behavior changed in this slice.

Seventeenth Clash pipeline architecture slice:

- `ClashClusterConstructionService` now owns the shared pure object-pair, spatial, and hybrid cluster partitioning algorithm, including spatial grid lookup and union-find connectivity.
- `DocumentCommandService.Clash.cs` keeps Navisworks-dependent cluster-row/association extraction, summary and report-assignment shaping, and MCP response paging.
- No public MCP tool names, parameters, response fields, cluster ordering, cluster identifiers, or grouping semantics changed in this slice.

Eighteenth Clash pipeline architecture slice:

- `ClashReportResponseFactory` now owns field-by-field DTO shaping for `ClashReportItem` and `ClashSavedViewpointItem`, plus safe saved-viewpoint name formatting.
- `DocumentCommandService.Clash.cs` keeps Navisworks-derived value extraction for boxes, item names/counts, points, distances, viewpoints, screenshots, and cluster assignment.
- No public MCP tool names, parameters, response fields, response values, or saved-viewpoint naming semantics changed in this slice.

## Architecture Work Plan

Use this checkpoint as the boundary between feature stabilization and architecture work. Suggested next architecture track:

1. Stabilize command boundaries:
   - separate read-only analysis, document mutation, report generation, and UI refresh concerns;
   - document dry-run/apply invariants for all write tools;
   - keep destructive actions behind explicit confirmation flags.
2. Extract Clash domain services from `DocumentCommandService.Clash.cs`:
   - done as partial boundary: test/result/cluster listing;
   - done as partial boundary: bbox pair planning;
   - done as pure helper: status filtering, result paging, cluster key/mode normalization, cluster list/preview limit clamping, group-name matching/name-shaping, existing-group cleanup decisions, renumber option normalization/name-shaping/plan-item construction, report output overwrite marker policy, report artifact path construction, report option normalization, report accumulation, clash handle construction/parsing, bbox/matrix option normalization, manage-operation normalization, test-type normalization, scope-label formatting, report color parsing, numeric option normalization, test-name prefix normalization, report HTML/status formatting, report screenshot sizing, cluster stable utility logic, and report value normalization;
   - done as service boundary: Navisworks-dependent group apply/move/delete behavior for `clash_group_results`;
   - done as service boundary: Navisworks-dependent renumber apply behavior for `clash_renumber_results`;
   - done as service boundary: final report artifact write step for `clash_generate_report`;
   - done as service boundary: current-view screenshot capture/export for `clash_generate_report`;
   - done as service boundary: active/last report operation status, cancellation, progress, completion, and failure tracking for `clash_generate_report`;
   - done as pure renderer boundary: HTML generation for `clash_generate_report`;
   - done as service boundary: saved-viewpoint target folder/reset/current-view save creation for report and saved-viewpoint batches;
   - done as service boundary: clash-point redline marker cleanup/projection and top-view camera setup for report screenshots;
   - done as service boundary: main/top screenshot set orchestration for `clash_generate_report`;
   - done as service boundary: Navisworks view/selection/clipping capture and restore for report and saved-viewpoint batches;
   - done as service boundary: report scope/page preparation for `clash_generate_report`;
   - done as service boundary: scope/page preparation for `clash_save_viewpoints`;
   - done as service boundary: per-item processing for `clash_generate_report`;
   - done as cleanup boundary: shared internal scope/page core for report and saved-viewpoint batches;
   - done as service boundary: Navisworks-dependent Clash test operation/settings/move/delete primitives and delete stabilization;
   - done as service boundary: Navisworks-dependent matrix test apply, generated-test cleanup, and rollback/restore behavior;
   - done as pure service boundary: shared object-pair, spatial, and hybrid cluster partitioning;
   - done as DTO factory boundary: report/save-viewpoint item shaping and safe viewpoint names;
   - covered by earlier live validation: report artifact generation/export validation.
3. Maintain the focused non-Navisworks unit-test helper baseline:
   - logical group-name matching;
   - leading-number stripping;
   - side-tag extraction/preservation;
   - group-name source selection and clean-name shaping;
   - renumber plan-item construction;
   - status filter normalization;
   - result paging semantics;
   - cluster key/mode normalization;
   - report artifact path construction;
   - report option normalization;
   - report accumulation;
   - clash handle construction/parsing;
   - bbox/matrix option normalization;
   - manage-operation normalization;
   - test-type normalization;
   - scope-label formatting;
   - report color parsing;
   - numeric option normalization;
   - test-name prefix normalization;
   - report HTML/status formatting;
   - report screenshot sizing;
   - cluster stable utility logic;
   - report value normalization;
   - report operation status/cancellation lifecycle;
   - report HTML renderer output fragments.
4. Define host lifecycle architecture:
   - multiple Navisworks hosts;
   - stale host/process detection;
   - MCP server restart/install behavior;
   - startup tools and recent-file workflow.
   - partial boundary completed after v2.4.2.0: per-user ZIP and dev MCP updates install `McpServer-<version>` alongside the active runtime, configure future client sessions to that path, and never terminate an existing stdio session; machine-wide installer behavior remains separate.
   - partial boundary completed after v2.4.2.0: failed pipe connection now removes the exact unreachable discovery record even when its Roamer PID remains alive, avoiding a repeated stale-host retry loop.
5. Define UI synchronization architecture:
   - Clash Detective rerun/change events;
   - debounced refresh;
   - stale object references;
   - persistent real groups vs cached virtual groups.
   - partial boundary completed after v2.4.2.0: automatic Clash UI refresh and disposed-handle recovery preserve the selected test by its loaded index plus display name, with name-only fallback only after an index mismatch; duplicate display names no longer select every matching test after refresh.
6. Release packaging architecture:
   - per-user vs machine-wide bundle policy;
   - MCP server install/update/uninstall;
   - versioned artifact validation;
   - release-candidate checklist.

## Suggested Next Chat Prompt

```text
Continue NavisHelper from <repo>.

The v2.4.1.0 release is published. We are preparing v2.4.2.0 on main from the completed post-2.4.1 Clash pipeline cleanup. Public v2.4.1.0 is available at tag `v2.4.1.0`.

Read docs/POST_2_4_ARCHITECTURE_CHECKPOINT.md first. The completed pipeline series now also includes PR #20-#23: Clash test mutation, matrix mutation, shared cluster construction, and report/save-viewpoint response shaping. v2.4.2.0 is a compatibility-preserving release of these boundaries. Run the release-candidate checks, install the fresh bundle to the per-user `%APPDATA%` location before live testing, then publish only after the live MCP and WPF UI gates pass.

After v2.4.2.0, continue with a separately scoped host-lifecycle or UI-synchronization architecture domain; do not fold it into this release train.
```
