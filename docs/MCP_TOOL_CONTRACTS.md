# NavisHelper MCP Tool Contracts

This document records the agent-facing input and output contract for the NavisHelper Clash Detective MCP tools.

Source of truth in code:

- MCP tool input parameters: `NavisHelper.McpServer/Tools/*.cs`
- Host request/response DTOs: `NavisHelper.Contracts/*.cs`

Field names below use the MCP/client-facing lower camel case convention. The C# DTO classes use PascalCase internally.

`mcp_diagnostics` and `mcp_health_check` expose `protocolVersion`, `mcpServerVersion`, and host `pluginVersion`. `protocolVersion` is the additive MCP-server to in-process-host wire contract version. Host status/health/discovery surfaces expose `hostLogFilePath` when the host plugin can compute the NavisHelper log path. If `mcpServerVersion` and `pluginVersion` differ, reinstall/update the NavisHelper bundle and MCP server from the same distribution package before running write tools.

## Common Rules

- `apply=false` means dry-run/preview for write-capable tools.
- Use `apply=true` only after reviewing the dry-run response scope.
- `instanceId` and `navisworksVersion` are optional on every Navisworks-hosted tool. Use `instanceId` when more than one Navisworks process is running.
- Host discovery/status surfaces may include `protocolVersion`; absence means an older host record or plugin build.
- Use `hostLogFilePath` from `host_status`, `mcp_health_check`, or `list_navisworks_hosts` for the in-process NavisHelper plugin log; use `logFilePath` from `mcp_diagnostics` / `mcp_recent_calls` for the external MCP server JSONL log.
- Host and MCP logs rotate at 5 MB with three suffix backups (`.1`, `.2`, `.3`). `mcp_recent_calls` reads across the current MCP JSONL log and its rotated backups.
- Every MCP tool call appends automatic `navishelper_timing` metadata to the tool result. The same timing is also appended as a text content block so agents can see it without reading logs. Fields: `toolName`, `status`, `startedAtUtc`, `completedAtUtc`, `elapsedMs`, `elapsedHuman`, `shouldReportToUser`, `userMessage`.
- `testHandle` values such as `clash-test:1` are traversal handles for the current document/session. Refresh them with `clash_list_tests` after deleting, moving, or sorting tests.
- `resultHandle` values such as `clash-result:1` are traversal handles for clash results in the current document/session.
- Empty `testName` plus empty `testNames` usually means all tests for read-only/report/viewpoint tools.
- Destructive operations should always be dry-run first: `delete`, `reset`, large matrix creation, large report/viewpoint batches.
- `host_busy` is retryable short-term MCP host contention. `interactive_busy` means Navisworks is busy with a manual UI operation and should not be auto-retried until the user operation finishes.
- Thrown MCP/host errors use the text convention `<error_code>: <message>` where possible. Prefer structured `errorCode` / `error_code` fields when present, and treat the prefix as a fallback for clients that only surface plain text.

### `navishelper_timing.status` is the transport's verdict, not the tool's

A schema violation or a failed host call sets `status` to `error`. A **structured refusal**
does not: it answers with `status: "ok"` while the payload carries `ok: false`, `applied:
false` and an `errorCode`. `delete_scenario` without `expectedSha256` is the measured
example — a client that read only the envelope recorded it as a successful 0 ms call.

So: read `ok` and `errorCode` from the payload whenever a tool has them, and treat
`status` as "the call arrived and came back", not "the thing you asked for happened".

### An `_export` / `_import` pair is not necessarily a round trip

Two pairs in this catalogue share a name and not a format, and in both the export cannot
feed the import:

| export | writes | its `_import` sibling reads |
| --- | --- | --- |
| `saved_viewpoints_export` | csv, json or md | Navisworks-authored Saved Viewpoints **XML** |
| `clash_tests_export` | `navishelper_json` | `nw-exchange-12.0` XML from Clash Detective |

Both exports exist for review — duplicate names, paths, indices before a bulk rename —
and both imports exist to ingest what Navisworks itself wrote. Neither is a defect on its
own; the trap is the naming symmetry, and it was measured three ways on the viewpoint pair
(its own json, that json renamed `.xml`, and the default csv all fail with `Failed to read
saved viewpoints XML`). If you need a round trip, there is not one: re-create from the
export's contents with the create tools.

### Truncation flags: a partial answer always says so, in a boolean

A tool that cannot return everything sets a `*Truncated` boolean rather than failing. A
boolean is the right contract for a machine client -- better than a warning string it
would have to parse -- but only if the client knows the field exists, so **every one of
them is listed here**. `scripts/check_truncation_flags_documented.py` fails the build when
a new truncation flag appears on a contract without being documented.

**The answer itself is short.** Handle these or the data is wrong, not merely abridged:

| flag | on | meaning |
| --- | --- | --- |
| `truncated` | every paged listing -- `list_root_items`, `list_item_children`, `list_selection_sets`, `list_saved_viewpoints`, `selection_copy_names`, `selected_items_preview`/`_ancestry`/`_tree`, the clash listings and reports, viewpoint build and import | fewer rows returned than exist. `dump_subtree_names` does **not** have this flag: its job status reports state and progress counts instead |
| `itemsTruncated`, `resultsTruncated`, `rowsTruncated` | `selection_property_report`, `selection_export_properties`, clash listings and reports | fewer rows than exist. `selection_export_properties` writes a file, so a truncated export is a report short of rows |
| `propertiesTruncated` | `item_properties_by_handle`, `selection_property_report`, `selection_export_properties`, `model_color_scheme` item facts | fewer properties per item than the item has |
| `valuesTruncated` | `selection_distinct_property_values` | fewer distinct values than exist, so the set is not the full domain |
| `depthTruncated` | `selected_items_tree` | the tree was cut at `maxDepth`; deeper nodes exist and are absent |
| `traversalTruncated` | `find_items_by_bbox`, `isolate_by_box`, clash matrix traversal | the walk stopped at a scan or time limit, so items were never examined. See *find_items_by_bbox* for why narrowing the zone does not help |
| `responseTruncated` | `last_operation_status` | the command completed but its response had to be reduced to fit the named-pipe frame |
| `groupsTruncated`, `pairsTruncated`, `candidatePairsTruncated`, `plannedTestsTruncated`, `analysisTruncated`, `classificationTruncated`, `rootItemsTruncated` | clash planning, grouping and root listings | fewer groups, pairs, planned tests or roots than the operation found |

**Only a preview is short.** The operation's own counts are complete; what is abridged is
the human-readable sample beside them:

| flag | on |
| --- | --- |
| `previewTruncated` | `find_items` and other match previews |
| `affectedRootSummariesTruncated` | the visibility tools. Only the `affectedRootSummaries` sample is short: `affectedRootCount` is taken from the full root count and stays complete |
| `affectedItemsPreviewTruncated` | `hide_selected`, `hide_unselected`, `unhide_selected`, `reveal_selected`, `isolate_selected`, `show_all`, `isolate_by_box` |
| `preservedUnclassifiedPreviewTruncated` | `isolate_by_box` |
| `previewItemNamesTruncated` | selection-viewpoint cluster info |
| `previewRowsTruncated` | clash cluster summaries |
| `previousTestsPreviewTruncated` | `clash_create_matrix_from_selection` |

A truncated preview is not a truncated result: trust the counts, not the sample.

<!-- BEGIN GENERATED TRUNCATION FLAG INVENTORY -->

Generated by `scripts/check_truncation_flags_documented.py`. Every flag below is
described in the two tables above; this block exists so that adding one to a
contract cannot skip that description.

| flag | declared on |
| --- | --- |
| `AffectedItemsPreviewTruncated` | `HideSelectedResponse`, `HideUnselectedResponse`, `IsolateByBoxResponse`, `IsolateSelectedResponse`, `RevealSelectedResponse`, `ShowAllResponse`, `UnhideSelectedResponse` |
| `AffectedRootSummariesTruncated` | `HideSelectedResponse`, `HideUnselectedResponse`, `IsolateSelectedResponse`, `RevealSelectedResponse`, `ShowAllResponse`, `UnhideSelectedResponse` |
| `AnalysisTruncated` | `ModelColorSchemeResponse` |
| `CandidatePairsTruncated` | `ClashBboxPairPlanResponse` |
| `ClassificationTruncated` | `ModelColorSchemeResponse` |
| `DepthTruncated` | `SelectedItemsTreeResponse` |
| `GroupsTruncated` | `ClashGroupResultsResponse`, `SelectionColorByPropertyResponse` |
| `ItemsTruncated` | `ItemPropertiesHandleResult`, `ModelColorSchemeResponse`, `SelectionColorByPropertyResponse`, `SelectionDistinctPropertyValuesResponse`, `SelectionExportPropertiesResponse`, `SelectionPropertyReportResponse` |
| `PairsTruncated` | `ClashRootMatrixResponse` |
| `PlannedTestsTruncated` | `ClashCreateMatrixFromSelectionResponse` |
| `PreservedUnclassifiedPreviewTruncated` | `IsolateByBoxResponse` |
| `PreviewItemNamesTruncated` | `SelectionViewpointClusterInfo` |
| `PreviewRowsTruncated` | `ClashClusterSummary` |
| `PreviewTruncated` | `ClashBboxPairPlanResponse`, `FindItemsMatch` |
| `PreviousTestsPreviewTruncated` | `ClashCreateMatrixFromSelectionResponse` |
| `PropertiesTruncated` | `ItemPropertiesHandleResult`, `ModelColorSchemeItemFacts`, `SelectionExportPropertiesResponse`, `SelectionPropertyReportResponse` |
| `ResponseTruncated` | `LastOperationStatusResponse` |
| `ResultsTruncated` | `ClashGroupByProximityResponse`, `ClashGroupResultsResponse`, `ClashListClustersResponse`, `FindItemsByBboxResponse` |
| `RootItemsTruncated` | `ClashBboxPairPlanResponse` |
| `RowsTruncated` | `SelectionExportPropertiesResponse`, `SelectionPropertyReportResponse` |
| `TraversalTruncated` | `FindItemsByBboxResponse`, `IsolateByBoxResponse` |
| `Truncated` | `BuildMtrViewpointsResponse`, `ClashGenerateReportResponse`, `ClashGroupResultsResponse`, `ClashListClustersResponse`, `ClashListResultsResponse`, `ClashListTestsResponse`, `ClashRenumberResultsResponse`, `ClashRootMatrixResponse`, `ClashSaveViewpointsResponse`, `ListItemChildrenResponse`, `ListRootItemsResponse`, `ListSavedViewpointsResponse`, `ListSelectionSetsResponse`, `SavedViewpointsImportResponse`, `SelectedItemsAncestryResponse`, `SelectedItemsPreviewResponse`, `SelectedItemsTreeResponse`, `SelectionCopyNamesResponse`, `SelectionSetsBuildViewpointsResponse`, `VisibilityRootSummaryResult` |
| `ValuesTruncated` | `SelectionDistinctPropertyValuesResponse` |

<!-- END GENERATED TRUNCATION FLAG INVENTORY -->

Common values:

- Clash statuses: `New`, `Active`, `Reviewed`, `Approved`, `Resolved`.
- Clash test types: `hard` / `intersection`, `hard_conservative` / `conservative`, `clearance`, `duplicate`.
- Clash box modes: `point`, `items`.
- Sort directions: `asc`, `name`, `natural`, `desc`.

## `find_items`

`find_items` contract v2 retains legacy `whole_model + matchDepth=all` behavior
when the new fields are omitted.

Scope inputs:

| Parameter | Default | Notes |
| --- | --- | --- |
| `scope` | `whole_model` | `whole_model`, `current_selection`, `under_handle`, or `under_named_node`. |
| `scopeHandle` | empty | Runtime match handle for `under_handle`; never persist it in a scenario. |
| `scopeNodePath` | empty | Fast exact tree path for `under_named_node`. Pass the `path` a tool printed, verbatim — the segment separator is `" / "` and node names may themselves contain `/`. |
| `scopeNodeName` | empty | Exact display-name fallback. It may need a bounded model traversal; prefer path/handle. |
| `matchDepth` | `all` | `first` returns the shallowest match on each branch and prunes that item's descendants; `all` preserves legacy behavior, which is pruned on `whole_model` and unpruned when scoped — see *Pruning* below. |
| `countOnly` | `false` | Returns counts, depth histogram, and sample model values without creating a handle or preview. |
| `preflight` | `false` | Returns the interpreted request and clarification questions without running a search. |

Simple comparisons are `equals`, `not_equals`, `contains`, `starts_with`,
`ends_with`, `wildcard`, `defined`, and `not_defined`. `ignoreCase`,
`ignoreDiacritics`, and `ignoreCharWidth` apply to simple mode; advanced
conditions retain their per-condition flags. Inside a grouped `searches`
condition, `comparison` and `operator` are interchangeable names for the same
field; `operator` wins when both are set.

Scoped traversal starts from the selected/handled/named roots and does not run a
global native search first. The output adds `matchedItemCount`,
`scannedItemCount`, `depthHistogram`, `sampleValuesFromModel`, and `warnings`.
With `countOnly=true`, no match handle is registered.

### A condition must name its property's category on the whole-model route

`SearchCondition` takes a category with every property — the surface is
`HasPropertyBy{CombinedName,DisplayName,Name}`, with no property-only factory — so a
condition that gives no category is **inexpressible** to the engine rather than
ambiguous. It used to be sent with `string.Empty`, asking for the property in the
category literally named `""`, which matches nothing and does not error. Measured
live on `6501.5.nwd`: `/DN equals "150mm"` returned **0** from the engine and **135**
from a traversal over the same nodes, while `AVEVA/DN equals "150mm"` returned 135
from both.

An empty category means "any category" to the manual matcher and "the category named
empty-string" to the engine, so the traversal can express something the engine
structurally cannot. The two routes are now split by which of them can answer:

| request | before | after |
| --- | --- | --- |
| `whole_model`, no category | `0`, `not_found`, warning claiming the count is exact | `schema_violation` naming the constraint and both ways out, 87 ms |
| `whole_model`, category given | 135 | 135, unchanged |
| scoped `matchDepth=all`/`countOnly`, no category | answered by the traversal | unchanged |
| scoped `matchDepth=first`, no category | native, so `0` | **answered by the traversal**, because a category-less condition is no longer eligible for the scoped engine path |

The rule behind that split, because it decides the next case of its kind too:
**refuse only where no route can answer, and fall back wherever one can.** The
boundary is not "native versus manual", which is an implementation fact; it is
whether anything can answer the request. The whole-model route refuses because its
traversal cannot finish on a model of any size — falling back there would swap a
confident zero for a 45-second failure whose message talks about narrowing the scope
rather than naming the category. Every scoped route falls back because its traversal
can finish.

A caller planning a client should read the asymmetry off the table above rather than
discover it from an error: **a condition needs its category on `whole_model` and does
not when scoped.** Every scoped route falls back and answers, so no caller that gets a
correct answer today loses it — the only behaviour that changes is the one that was
returning a zero it called exact.

The refusal also covers the case where the property genuinely exists in no category
and `0` was the right answer. That is accepted deliberately: a caller cannot today
distinguish "no such property" from "property in a category you did not name", and
that indistinguishability is the defect. An error stating the constraint is truthful
under both readings; a `0` is truthful under only one.

### Pruning: `whole_model + matchDepth=all` is pruned, scoped `all` is not

`scope=whole_model` with `matchDepth=all` (and no `starts_with`/`ends_with`
condition) is answered by the native Navisworks
`Search`, which runs with `PruneBelowMatch = true`: the engine **does not return
descendants of a matching item**. Every other routing — any non-`whole_model`
scope, `matchDepth=first`, or a `starts_with`/`ends_with`
condition — is answered by the manual traversal, and `matchDepth=all` there
returns nested matches as well. The one exception is an eligible scoped
`matchDepth=first` request, which the engine answers with pruning on because
pruning and `first` mean the same thing there; see the next section for what
makes a request eligible and what that costs in `scannedItemCount`.

This asymmetry is the long-standing whole-model contract and is kept
deliberately; `PruneBelowMatch` is now assigned explicitly rather than inherited
from the SDK default. It is not silent: when a pruned whole-model search returns
at least one match that has children, the response carries a warning naming the
pruning and pointing at the scoped alternative. When every match is childless,
pruning cannot have changed the result and no warning is emitted.

Measured on `6501.5.nwd` (~88k nodes, Navisworks 2027), `Item/Name contains
"Copy-of-"`:

| Call | Result |
| --- | --- |
| `scope=whole_model, matchDepth=all` (pruned) | 57 |
| `scope=under_handle` over `/STORE` only, `matchDepth=all` (unpruned) | 1626 |
| `scope=under_handle` over `/STORE` only, `matchDepth=first` | 11 |

For a complete subtree answer, scope the search and use `matchDepth=all`.

### The printed path is the path `scopeNodePath` accepts

Every tool that reports an item reports a `path` joined with `" / "`, and
`scopeNodePath` takes that path back. This did not hold until the split stopped
using the bare `/` character: node names in a plant model begin with `/`, so
`"6501.5.nwd / /STORE"` became `["6501.5.nwd", "STORE"]` and matched nothing.
Measured on `6501.5.nwd`:

| Call | Before | Now |
| --- | --- | --- |
| `find_items`, `under_named_node`, `scopeNodePath` from `list_root_items` | `schema_violation: resolved 0` | 11 matches in 75 ms |
| `list_item_children` with `parentPath` from its own output | `Parent item was not found by fast path` | 3 children in 71 ms |
| a six-segment path with Cyrillic and slashes | `resolved 0` | 8 matches in 26 ms |

Each result is identical to the same scope addressed by handle, so the documented
fast option no longer costs an extra round trip. A plain `a/b/c` or `a\b\c` path
still splits on the character, for callers that never had a printed path.

### `whole_model` + `countOnly=true` is answered by the engine

`countOnly` used to force the manual traversal on every scope so that
`scannedItemCount` could be reported. For `scope=whole_model` that meant walking
the entire model, which on any real model exceeds the 45 second budget and returns
`command_failed` rather than a count. Measured on `6501.5.nwd`:

| Call | Before | Now |
| --- | --- | --- |
| `Item/Name contains "of BRANCH /Copy-of-15.15-…"`, `countOnly` | `command_failed` after 45 264 ms | **115 in 404 ms** |
| `Item/Name contains "GASKET 1 of BRANCH"`, `countOnly` | `command_failed` | **3616 in 762 ms** |

`matchedItemCount`, `depthHistogram` and `sampleValuesFromModel` are exact, and the
count now agrees with the same query run without `countOnly` — previously the two
could not be compared, because one of them never answered. No match handle is
registered, as before.

`scannedItemCount` is **`0`**, because the engine reports matches rather than how
many nodes it walked, and the response carries a warning saying so, so that a zero
is not read as "nothing was scanned". Scoped `countOnly` is unchanged and still
reports a real `scannedItemCount`: `scope=under_handle` over `/STORE` with
`matchDepth=all` returns 1626 matches and 6457 scanned nodes in 119 ms.

Being a `whole_model` + `matchDepth=all` call, a `countOnly` count is pruned by the
contract above, and carries the pruning warning on the same condition as the
non-`countOnly` form.

### Scoped `matchDepth=first` is answered by the engine

A scoped search (`current_selection`, `under_handle`, `under_named_node`) with
`matchDepth=first` is handed to the native Navisworks search rooted at the scope,
with `PruneBelowMatch = true`. Engine pruning *is* `matchDepth=first` — stop at
the shallowest match on each branch — so the two paths return the same set, and
the engine does the walking.

A request falls back to the manual traversal, which stays the reference
behaviour, when any of these hold:

- `matchDepth=all` — pruning is the wrong semantics;
- `countOnly=true` — a *scoped* count still traverses, because the engine reports
  matches rather than nodes walked and `scannedItemCount` is what that call is
  asking for. A scoped subtree is small enough to walk; a whole-model count is
  not, which is why `scope=whole_model` with `countOnly=true` is answered by the
  engine instead (see below);
- the search combines with `any`, or a condition carries `logicalOperator=or` —
  native conditions are ANDed;
- a comparison other than `equals`/`contains`/`wildcard`;
- `negate` or `inheritFromAncestor` on any condition.

`scannedItemCount` is `0` when the engine answered; `matchedItemCount`,
`depthHistogram` and `sampleValuesFromModel` are filled as usual. Setting
`NAVISHELPER_FIND_ITEMS_NATIVE_SCOPE=0` forces every scoped search back onto the
manual traversal.

Measured on `6501.5.nwd` (~88k nodes, Navisworks 2027), scope = the nine
discipline roots under `/6501.5`, `matchDepth=first`:

| condition | engine | manual traversal |
| --- | --- | --- |
| `Item/Name contains "6501.5."` | 9 matches, 148 ms | 9 matches, 21 ms |
| `Item/Name contains "насос"` | 1 match, 253 ms | `command_failed` after 45 018 ms |
| `Item/Name contains "zzzz-no-such-item"` | 0 matches, 257 ms | `command_failed` after 45 029 ms |

The manual traversal is competitive when matches are shallow and plentiful, and
cannot answer at all once it has to walk deep into a large scope.

### Result identity

Matches are deduplicated by model item identity, not by the displayed tree path.
A Navisworks model can contain several genuinely different siblings that share a
display name, so two rows in `preview` may look identical while referring to
different items; `itemCount` counts them separately and the match handle holds
them all. Text warnings flag mixed
Cyrillic/Latin input or Latin letters that commonly resemble Cyrillic ones.
For numeric equality, pass `dataType="double"` (or another explicit numeric
type). Decimal values accept invariant dot syntax and, for persisted Search
Sets, comma syntax is normalized as well. Without `dataType`, a numeric-looking
value may be interpreted as display text and fail to match the native numeric
property.

For grouped `AND` searches, the runtime planner evaluates selective exact and
inherited anchors first. When creating a persistent Search Set, keep exact
`Source File` as the first condition, followed by expensive inherited/material
properties. Navisworks preserves the saved condition order, and this can reduce
resolution time substantially.

Match handles (`mh_*`) are session traversal references, not stable model
identities. They can expire or be invalidated by intervening document/search
changes; on `stale_match_reference`, repeat the originating search. For durable
scope, prefer `scopeNodePath` or a Selection Set `itemId` freshly obtained from
`list_selection_sets`.

## `clash_tests_from_sets`

Creates one Clash Detective test for each pair of static Selection Sets or
dynamic Search Sets. Both sides are stored as native Navisworks
`SelectionSource` references (`sideBinding="selection_source"`), not snapshots.
Dynamic Search Sets are therefore re-evaluated whenever Navisworks runs the
test.

- Dry-run is the default and returns resolved `itemId`, path, set type, current
  A/B member counts, planned test name, empty-set warnings, and name conflicts.
- An inline set reference accepts document-local `itemId`, full `path`, or
  unique `name` plus optional one-based `occurrence`. `itemId` is not portable
  between documents. In a versioned transfer plan, the exact full set-tree
  path is authoritative and `itemId` is retained only as source diagnostics.
- Input is either inline `pairs` or `planPath`; the JSON file may be a legacy
  pair array, `{ "pairs": [...] }`, or `navishelper.clash-test-transfer` v1.
  Transfer definitions preserve each test's exact name, type, explicit tolerance,
  self-intersection flags, and supported same-file ignore rule. The default
  limit is 200 and the hard maximum is 500.
- `overwriteExisting=true` replaces a same-name test. `pairNameTemplate` uses
  the same tokens/transforms as matrix creation.
- `runAfterCreate=true` returns `runOperationId` and starts the asynchronous
  runner using `runBatchSize` and `perTestTimeoutSeconds`.
- `continueOnError=false` enables operation-level rollback of tests created or
  replaced earlier in the same call when a later resolution/conflict/mutation
  fails. Legacy callers keep `continueOnError=true` by default.

For ordinary new `set × set`, `root × set`, or `root × root` definitions, call
`clash_tests_from_sets` directly. Model roots accept exact `rootName` and/or
`sourceFile`; set sides remain live native Selection Sources.

## Clash Test transfer between documents

The portable JSON schema is:

- `schema: "navishelper.clash-test-transfer"`;
- `version: 1`;
- `tests[]` with exact `name`, `testType`, `toleranceMm`, sides `a`/`b`,
  supported ignore rules, warnings, and unsupported settings;
- set sides identified primarily by exact full Selection Sets tree `path`;
- model-root sides identified by `rootName` and/or `sourceFile`.

`toleranceMm` is nullable: an explicit source tolerance is preserved in
millimeters, while an omitted XML/JSON tolerance remains unset so the target
Navisworks default is retained. XML omission is reported as a warning.

`clash_tests_export` is read-only with respect to the Navisworks document.
With `apply=false`, it builds the full preview but never creates an output
file: `outputWritten=false`, `artifactStatus="not_written_dry_run"`, and the
requested absolute path appears only as `calculatedOutputPath`. With
`apply=true`, `outputPath` is required. The tool serializes to
`outputPath + ".partial"`, flushes, atomically completes the target, verifies
existence/readability/size, and returns `outputWritten=true`, `bytesWritten`,
and SHA-256. Existing targets require `overwriteExisting=true`.

`clash_batchtest_import` accepts the supported subset of Autodesk
`nw-exchange-12.0` `<batchtest>` XML. The adapter accepts only exact
`lcop_selection_set_tree/<full path>` locators confirmed by the Autodesk XSD,
then routes the common transfer plan through `SelectionSetReferenceResolver`
and `clash_tests_from_sets`. It does not guess undocumented model/file locator
syntax. JSON transfer plans cover `root × set` and model-root references.

The XML reader prohibits DTDs and external entities, sets `XmlResolver=null`,
does not load XSD/schemaLocation resources, enforces a 10 MB input bound and a
bounded test count, and matches elements by local name for namespace-tolerant
parsing. Dry-run resolves both exact paths and reports current member counts,
type/tolerance, conflicts, unsupported settings, and side-specific failures.
`apply=true` never runs the created tests. Clash results, result viewpoints,
comments, calculation state, and saved historical results are never imported.
With `apply=false`, fail-fast controls never truncate preview: every test is
still resolved so the operator can review all conflicts and missing sides.

## Asynchronous clash runs

`clash_run_batch` resolves exact names, current test handles, and/or a name
prefix, then returns immediately with `operationId`. It runs one native test per
UI callback and pauses after `batchSize`. Continue with `clash_run_resume`.
`clash_run_status` is read-only and bypasses `host_busy`;
`cancel_clash_run` is cooperative and also bypasses `host_busy`.

Navisworks exposes a synchronous per-test calculation API. Consequently,
`perTestTimeoutSeconds` is an advisory timebox: an overrun is reported after
that test returns, and cancellation takes effect before the next test. The
current native test is never force-aborted because that is unsafe for the
document.

## `find_items_by_bbox`

Read-only spatial search over model-item axis-aligned bounding boxes. It never changes the Navisworks selection, visibility, or saved state. Coordinates are raw global coordinates in the active document's native units; this v1 contract deliberately does not apply local-origin, grid, rotation, or unit transforms.

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `min`, `max` | `{x,y,z}` | required | Opposite corners of an axis-aligned zone. All six values must be finite and each `min` component must be less than or equal to its corresponding `max` component. |
| `matchMode` | string | `intersects` | `intersects` returns overlapping item boxes, `contains` returns boxes wholly inside the zone, `center` returns boxes whose center is inside the zone. `overlaps`, `inside`, and `centre` are accepted aliases. |
| `includeHidden` | bool | `true` | Include hidden model items. |
| `includeContainers` | bool | `false` | Include non-leaf hierarchy/container items. |
| `sourceFileContains` | string | `""` | Optional case-insensitive source-file filter. |
| `maxScannedItems` | int | `100000` | Traversal safety limit, clamped to `1..500000`. The host also has a ten-second runtime budget. |
| `maxResults` | int | `5000` | Returned-match safety limit, clamped to `1..10000`. |
| `previewLimit` | int | `10` | Bounded item preview length, clamped to `1..50`. |
| `instanceId`, `navisworksVersion` | string | `""` | Optional host targeting. |

Outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `coordinateSpace` | string | Always `document_global` for this version. |
| `min`, `max`, `matchMode` | scalar/object | Echo normalized spatial query. |
| `scannedItemCount`, `matchedItemCount`, `returnedItemCount` | int | Scan count, unique matches observed, and bounded returned matches. |
| `traversalTruncated`, `resultsTruncated` | bool | Safety/runtime or result-limit truncation indicators. Do not treat a truncated result as exhaustive. |
| `matchHandle` | string | Present when at least one match is returned; pass to `select_items`, visibility tools, or `create_selection_set`. |
| `preview[]` | array | Bounded `displayName`, `path`, `sourceFile`, `min`, and `max` data. |
| `warnings[]` | string[] | Non-fatal unreadable-bbox or truncation warnings. |

## Host Diagnostics Tools

### `last_operation_status`

Returns authoritative in-process Navisworks host status for a recent `requestId`. Use it after `request_timeout`, client cancellation, broken pipe, or when a command may have completed but the MCP client did not receive the response.

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `requestId` | string | `""` | Request id from `mcp_recent_calls` / MCP server logs. **Leave it empty to ask about the most recent host operation.** A client whose transport dropped the reply never received a request id, because the id arrives with the reply -- so requiring one made this tool unusable in the case it exists for. Measured live: a `find_items` call reported `request_timeout` at the client while the host log recorded that same call completing `ok` in 118 ms. |
| `instanceId` | string | `""` | Optional explicit Navisworks host. |
| `navisworksVersion` | string | `""` | Optional version filter. |

Outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `found` | bool | `false` if the bounded in-memory host history no longer has the request id or Navisworks restarted. |
| `state` | string | `running`, `completed`, `failed`, or `not_found`. |
| `ok` | bool? | `true` for completed, `false` for failed, null while running/not found. |
| `errorCode`, `errorMessage` | string | Populated for failed requests. |
| `responseTruncated` | bool | `true` when the command completed but the response had to be reduced to fit the named-pipe frame limit. |
| `resolvedFromMostRecent` | bool | `true` when `requestId` was empty and the host answered about the most recent operation. **Compare the returned `command` against the call you lost before trusting the rest** -- the host answers about the newest operation it *has*, which is not necessarily yours. |

Status polls -- `last_operation_status`, `clash_report_status`, `clash_run_status` -- are never written to the operation history: all three `RecordOperation*` methods return early for them, so that a polling loop cannot evict the real operation from a bounded history. Two consequences:

- Repeated no-argument asks are idempotent. The second ask still names the lost call rather than the first ask.
- **If a status poll is itself the call whose reply was lost, a no-argument ask answers about an older, unrelated operation.** It cannot do better; the poll was never recorded. This is why `command` is in the response and why checking it is not optional.
| `responseType`, `startedAtUtc`, `completedAtUtc`, `elapsedMs`, `message` | scalar | Execution diagnostics. |

The history is process-local and bounded; it is reset when Navisworks exits. A timeout entry can initially show `failed/request_timeout` and later be overwritten to `completed` if the UI callback finishes after the client timed out; a completed timeout record is not overwritten back to failed.

## Startup And Timing Tools

### `list_recent_navisworks_files`

Reads the current Windows user's Navisworks Recent File List registry entries. Navisworks does not need to be running.

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `navisworksVersion` | string | `""` | Optional `2024`, `2025`, `2026`, or `2027`; empty means all supported versions. |
| `limit` | int | `10` | Maximum files returned, capped at `100`. |
| `existingOnly` | bool | `true` | Exclude recent entries whose path no longer exists. |

Outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `returnedFileCount` | int | Number of returned files. |
| `files[]` | array | `navisworksVersion`, `registryVersion`, `slot`, `displayName`, `path`, `lastOpenedUtc`, `lastOpenedLocal`, `exists`. |
| `warnings[]` | string[] | Non-fatal registry/version warnings. |

### `open_latest_navisworks_file`

Convenience startup tool for "start Navisworks and open the last file".

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `navisworksVersion` | string | `""` | Optional version scope; empty opens the globally latest existing recent file. |
| `waitForHost` | bool | `true` | Wait for the NavisHelper in-process MCP host discovery record. |
| `waitTimeoutSeconds` | int | `90` | Host wait timeout, capped at `300`. |

Outputs are the same as `start_navisworks`.

### `start_navisworks`

Starts Navisworks Manage blank, with an explicit file, or with the latest recent file.

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `navisworksVersion` | string | `""` | Optional `2024`, `2025`, `2026`, or `2027`; empty means latest installed version unless opening a recent file. |
| `filePath` | string | `""` | Explicit `.nwd/.nwf/.nwc` path. |
| `openLatestRecentFile` | bool | `false` | When `filePath` is empty, open the latest existing recent file. |
| `waitForHost` | bool | `true` | Wait for MCP host discovery. |
| `waitTimeoutSeconds` | int | `90` | Host wait timeout, capped at `300`. |

Outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `started`, `processCreated`, `processExited`, `exitCode`, `processId` | scalar | Process result. `started=false` when an early exit is confirmed; `processCreated` remains true to show that `Process.Start` succeeded. |
| `navisworksVersion`, `roamerPath`, `filePath` | scalar | Selected executable and file. |
| `openedRecentFile`, `recentFile` | scalar/object | Present when the file came from Recent File List. |
| `waitedForHost`, `hostReady`, `host` | scalar/object | `host.instanceId` should be used for follow-up tools when multiple hosts may exist. |
| `outcome`, `failureReason` | scalar | `host_ready`, `process_exited`, `host_timeout`, `process_created`, or `attached_to_existing_host`. `process_created` is the immediate snapshot used when `waitForHost=false`; it does not claim later host readiness. `attached_to_existing_host` means no process was started -- see *Attaching* below. |
| `startupElapsedMs`, `elapsedMs`, `elapsedHuman`, `message`, `warnings[]` | scalar/array | `startupElapsedMs` covers process creation and monitoring; legacy `elapsedMs` also includes request preparation such as version/file resolution. |

#### Attaching instead of starting a second process

**A request naming a file that a ready host of the requested version is proven to have
open attaches to that host and starts nothing.** The response carries
`processCreated: false`, `outcome: attached_to_existing_host`, `processId` set to that
host's pid, and the host itself. Measured live on `6501.5.nwd`, Navisworks 2027:
**123 ms** against roughly 15 500 ms for a launch.

*Proven* is load-bearing. Discovery reports a document **title**, which is a file name,
so `C:\A\model.nwd` and `D:\B\model.nwd` are indistinguishable in the host list.
A name match is therefore only a candidate: the server confirms it with `host_status`
on that instance and compares the full `documentFileName`, and **launches instead when
the path cannot be confirmed**, saying so in a warning. Attaching on a name alone would
report success without opening the requested file, and the next write tool would act on
a different model. That one extra round trip is the difference between 42 ms and 123 ms
and it is worth paying.

**Every** name-matching host of the requested version is a candidate, not only the most
recently started one; newest first is the order they are checked in, and the first whose
path is confirmed is attached to. With `D:\A\model.nwd` open in an older host and
`D:\B\model.nwd` in a newer one, checking only the newest fails its path proof and
starts a third process while the requested file is already open. Each further candidate
costs another `host_status` round trip of the same order as the first, against the
roughly 15 500 ms of the launch it avoids.

This exists because the alternative was a broken session. Observed live on
2026-09-20: `start_navisworks` produced a ready 2027 host, `open_latest_navisworks_file`
produced a **second** process, and every following tool call failed with
`multiple_hosts_detected` until one instance was closed.

Three cases still start a process, deliberately:

- **A different version.** No host of the requested version can serve the request.
- **A host that is running but has not registered yet.** Discovery is the readiness
  record, so an unregistered instance is invisible and cannot be attached to.
- **A request naming a file no running host has open** — including
  `start_navisworks` with no file at all, because "start Navisworks" is not "give me
  whatever is running". **There is no command that opens a document in a running
  instance**, so this cannot be satisfied by attaching. Making it possible means
  adding that capability, not changing this decision.

That last case is the reported sequence, and it still ends with two hosts. What
changed is that the response now says so, in a warning naming
`multiple_hosts_detected`, `list_navisworks_hosts` and `close_navisworks` — rather than
leaving the caller to discover it on its next call as an error about a situation this
call created.

That warning is derived from the host count **after** the launch, not predicted from the
count before it. `Roamer.exe` can hand a file off into an instance that is already
running, in which case the launch adds no host, and a prediction would have told the
caller to close an instance that does not exist.

A separate warning fires when the discovered host runs in a different process than
the one that was started. `SelectHost`'s last resort matches on document title
without excluding hosts that were already running, so a launch can report a
pre-existing host beside the pid it just created — `processId 42284` with
`host.pid 57488` was observed. The fallback is kept, because it is the only thing
that finds the host when Navisworks serves the file from another process, but the
mismatch is now stated instead of left to be noticed.

With the default `waitForHost=true`, the server monitors both host discovery and
the child process. A nonzero or unavailable early process exit returns
`process_exited` without waiting for the full host timeout (`exitCode` is
included when available). `host_timeout` means no
NavisHelper host was discoverable at the timeout; the process may still be alive,
or a clean zero-exit launcher may have failed to complete its handoff. Check
`mcp_recent_calls` for safe environment-source facts; the log intentionally
records only the model file name, not its full path. If the process starts but
health reports different MCP server and plugin versions, reinstall both from
the same NavisHelper package before using write tools.

A normal zero-exit launcher handoff to a different Navisworks PID remains
`host_ready`; in that case `processExited=true` and `exitCode=0` describe the
short-lived launcher while `host` identifies the live target process. After a
clean launcher exit, host discovery continues for the remaining requested wait
timeout so delayed handoff registration is not reported as a crash. If no host
appears, the full remaining wait is consumed and the result is `host_timeout`
with `processExited=true` and `exitCode=0`.

### `close_navisworks`

Closes exactly one running Navisworks instance selected through `instanceId`, or
through `navisworksVersion` when exactly one host of that version is running.
The tool defaults to preview and never closes an ambiguous target.

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `mode` | string | `prompt` | `prompt` requests normal Navisworks exit, `save` saves first, `discard` permanently drops unsaved changes. |
| `savePath` | string | `""` | Optional absolute `.nwd`/`.nwf` path for `save`; empty uses the current document path. |
| `overwrite` | bool | `false` | Permit replacement of `savePath`. |
| `apply` | bool | `false` | Preview unless `true`. |
| `confirmClose` | bool | `false` | Required with `apply=true` for every mode. |
| `instanceId` | string | `""` | Recommended exact target from `list_navisworks_hosts`. |
| `navisworksVersion` | string | `""` | Version target allowed only for one matching host. |

The host writes the MCP response before scheduling application exit. In
`prompt` mode a native save dialog can keep Navisworks open. In `save` mode the
host verifies that the document is no longer modified before scheduling exit.
In `discard` mode the document is cleared before exit, so unsaved changes cannot
trigger a save prompt. Application exit is requested through the main-window
`WM_CLOSE` message on every supported Navisworks version; no private Autodesk
exit command is required.

Outputs include `mode`, `apply`, `exitScheduled`, `documentWasModified`,
`documentPath`, `savedPath`, `discardedUnsavedChanges`,
`nativePromptExpected`, and `message`.

### `mcp_task_timer_start` / `mcp_task_timer_finish`

Required cross-tool timer for larger user-visible workflows that span multiple MCP calls. Individual MCP tool calls return automatic `navishelper_timing` in their primary JSON result; use this timer when the agent needs one elapsed time for the whole multi-step workflow.

`mcp_task_timer_start` inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `taskName` | string | `""` | Optional short workflow name, for example `clash report` or `open latest Navisworks file`. |

`mcp_task_timer_start` outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `timerId`, `taskName`, `startedAtUtc`, `message` | scalar | Pass `timerId` to `mcp_task_timer_finish`. |

`mcp_task_timer_finish` inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `timerId` | string | required | ID returned by `mcp_task_timer_start`. |
| `taskName` | string | `""` | Optional final workflow name override. |

`mcp_task_timer_finish` outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `timerId`, `taskName`, `startedAtUtc`, `completedAtUtc` | scalar | Timer identity and timestamps. |
| `elapsedMs`, `elapsedHuman`, `shouldReportToUser`, `userMessage` | scalar | Human/reporting timing fields for the whole multi-tool workflow. If `shouldReportToUser=true`, include `userMessage` in the user-facing answer. |

## `clash_list_tests`

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `limit` | int | `200` | Maximum returned tests. |
| `includeStatusCounts` | bool | `true` | Include per-status result counts. |
| `instanceId` | string | `""` | Optional explicit Navisworks host. |
| `navisworksVersion` | string | `""` | Optional version filter, for example `2027`. |

Outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `totalTestCount` | int | Tests in the Clash Detective tree. |
| `returnedTestCount` | int | Tests returned after `limit`. |
| `truncated` | bool | `true` when not all tests were returned. |
| `tests[]` | array | `testIndex`, `handle`, `testHandle`, `name`, `total`, `new`, `active`, `statusCounts`. |

## `clash_list_results`

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `testName` | string | `""` | Empty means all tests; exact match is preferred, then contains-match. |
| `limit` | int | `500` | Maximum returned result rows. |
| `statusFilters` | string[] | `[]` | Optional statuses such as `New`, `Active`. Empty means no status filter. |
| `includeAllStatuses` | bool | `false` | Explicitly include all statuses and ignore `statusFilters`; empty `statusFilters` also returns all statuses for backward compatibility. |
| `resultOffset` | int | `0` | Zero-based offset for paging through sorted filtered results. |
| `includeItemNames` | bool | `true` | Include side A/B display names. |
| `includeAssignedTo` | bool | `true` | Include assignee. |
| `instanceId` | string | `""` | Optional explicit Navisworks host. |
| `navisworksVersion` | string | `""` | Optional version filter. |

Outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `requestedTestName` | string | The requested `testName`. |
| `matchedTestCount` | int | Tests matched by the request. |
| `totalResultCount` | int | Results before status filtering. |
| `matchedResultCount` | int | Results after status filtering. |
| `returnedResultCount` | int | Result rows returned after `limit`. |
| `resultOffset`, `nextResultOffset`, `hasMoreResults` | scalar | Paging state. |
| `truncated` | bool | `true` when more result rows remain after this page. |
| `warnings[]` | string[] | Non-fatal contract/offset warnings. |
| `results[]` | array | Includes `resultHandle`, `groupHandle`, `clashPoint`, `distanceMm`, `groupPath`, `status`, `assignedTo`, short item names, and `ignored`. |

Ignored results are excluded by default. Pass `includeIgnored=true` to include results tagged by `clash_ignore_rules`.

## `clash_list_clusters`

Groups existing Clash Detective results into read-only problem clusters. It does not create viewpoints, screenshots, reports, tests, or Clash Detective groups.

The default `groupMode=hybrid` is association-first: it groups by related object pair and then splits each pair by clash-point distance. This is intended for real project models where clean discipline metadata is missing and the useful statement is "this object is related to these objects".

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `testName` | string | `""` | Empty means all tests; exact match is preferred, then contains-match. |
| `testNames` | string[] | `[]` | Multiple tests; empty with `testName` empty means all tests. |
| `statusFilters` | string[] | `[]` | Empty means default `New,Active` unless `includeAllStatuses=true`. |
| `includeAllStatuses` | bool | `false` | Include all clash statuses. |
| `groupMode` | string | `hybrid` | `hybrid`, `object_pair`, or `spatial`. |
| `clusterDistanceMm` | double | `300` | Maximum point distance for spatial grouping/splitting. |
| `limit` | int | `100` | Maximum clusters returned, capped at 5000. |
| `resultOffset` | int | `0` | Zero-based cluster offset for paging. |
| `previewRowsPerCluster` | int | `5` | Raw clash preview rows per cluster, capped at 50. |
| `maxResults` | int | `500` | Maximum filtered raw results to analyze, capped at 50000. |
| `excludeItemNameContains` | string[] | `[]` | Exclude raw clashes before clustering when either side name/path contains a filter. |
| `instanceId` | string | `""` | Optional explicit Navisworks host. |
| `navisworksVersion` | string | `""` | Optional version filter. |

Outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `matchedTestCount`, `totalResultCount`, `matchedResultCount`, `rawResultCount` | int | Scope and raw row counts after status/exclude/max-result filtering. |
| `clusterCount`, `returnedClusterCount`, `resultOffset`, `nextResultOffset`, `hasMoreClusters` | scalar | Cluster paging. |
| `groupMode`, `clusterDistanceMm`, `previewRowsPerCluster`, `maxResults` | scalar | Effective settings. |
| `weakAssociationCount` | int | Raw rows where one side fell back to coarse source/root or leaf-path resolution. |
| `totalStatusCounts`, `matchedStatusCounts`, `returnedStatusCounts` | object | Status rollups. |
| `clusters[]` | array | `clusterId`, `clashCount`, `weakAssociation`, association keys/display names/levels for sides A/B, source files, centroid, bounding box, status counts, tags, and bounded `previewRows[]`. |
| `warnings[]` | string[] | Non-fatal warnings, including weak association and max-result truncation. |

Association fields are explanatory, not authoritative BIM classification. Treat `associationLevelA/B=source_root`, `leaf_path`, `unknown`, or `mixed` as low-confidence grouping.

`verbosity=compact` is the default and clears long `item1Path`/`item2Path` values from preview rows. Use `verbosity=full` only when full model paths are needed.

## `model_color_scheme`

Analyzes naming/property patterns across the full loaded model or the current selection, then applies an explicit ordered classification scheme. The LLM proposes rules; the host executes them deterministically.

Operations:

- `analyze`: read-only inventory of repeated `source_file`, `display_name`, and `property_value` candidates.
- `apply` with `apply=false`: dry-run classification plan.
- `apply` with `apply=true`: applies permanent color/transparency overrides.
- `reset` with `apply=false`: reports whether the current host session can restore the scheme.
- `reset` with `apply=true`: resets touched materials, then restores their captured effective permanent color/transparency in batches.

Rule semantics:

- Rules are ordered and use first-match-wins priority.
- `matchAll=true` creates an unconditional rule. Use it as the final catch-all or as the only rule to color the whole current selection one fixed color.
- Values inside one matcher list are OR.
- Populated matcher dimensions are AND.
- `categoryContains`, `propertyContains`, and `propertyValueContains` must match the same property.
- Every rule requires `colorHex` and either `matchAll=true` or at least one matcher.

Safety:

- `apply=true` is rejected when traversal is truncated; increase `maxItems` first.
- `maxItems` defaults to 100000 and is capped at 2000000; prefer `scope=selection` for very large federated models.
- `workBudgetSeconds` defaults to 40 and is capped at 45. Large analysis/classification stops inside the host before the 60-second MCP timeout and reports `analysisTruncated` or `classificationTruncated` plus `unprocessedItemCount`.
- `verbosity=compact` is the default: candidate sample paths are omitted and long candidate text is bounded. Use `full` only for diagnostics.
- More than 25000 matched items requires `confirmLargeApply=true`.
- Property facts are read from each geometry leaf and its ancestors. A property-truncated dry-run cannot be applied.
- `includeContainers=true` is analysis-only because container material overrides propagate to descendants.
- Reset state is runtime-only and document-bound. It is discarded when the document changes.
- Applying a replacement scheme first restores the previous active scheme.
- A stale session is cleared without touching a newly opened document.
- `clearSelectionAfterApply=true` is the default because Navisworks selection highlighting can mask permanent material colors. The cleared selection is captured and restored by reset when the user has not selected something else.
- Apply verifies up to 100 affected items through both `PermanentColor` and `ActiveColor`, returning `colorVerificationSampleCount`, `permanentColorMatchCount`, and `activeColorMatchCount`.
- `sourceFileContains` prefers the inherited Navisworks `Source File` / `Файл источника` property and falls back to the model API path.
- Analysis reserves preview space for every available candidate kind so frequent property values do not hide `source_file` and `display_name`.

Example plan:

```json
{
  "operation": "apply",
  "scope": "model",
  "apply": false,
  "maxItems": 100000,
  "rules": [
    {
      "name": "Electrical",
      "colorHex": "#FFD84D",
      "sourceFileContains": ["ЭОМ", "electrical"],
      "propertyValueContains": ["электрика", "power"]
    },
    {
      "name": "HVAC",
      "colorHex": "#55BDEB",
      "propertyContains": ["Система", "System"],
      "propertyValueContains": ["ОВ", "HVAC"]
    }
  ]
}
```

Review `ruleResults`, `matchedItemCount`, `unclassifiedItemCount`, and `itemsTruncated` before repeating the same request with `apply=true`.

## Clash workflow mutations

- `clash_group_custom` validates every `resultHandle` against one `testHandle` before creating/rebuilding a group.
- `clash_ungroup` accepts explicit `groupHandles` or `groupNamePrefix`; both tools are dry-run by default.
- `clash_set_status` supports `results`, `group`, and `test` scopes. Group/test scopes always update individual child results. More than 500 results requires `confirmLargeStatusChange=true` for apply.
- `clash_group_by_proximity` writes the same spatial/hybrid/object-pair clusters that `clash_list_clusters` previews. More than 1000 analyzed results requires `confirmLargeGrouping=true`.
- `clash_ignore_rules` persists its JSON payload inside the Navisworks document under `__NavisHelper_Data`, approves matches with a reason comment, and re-applies after test runs.
- `clash_export_points` writes `.csv` or `.xlsx`; XLSX includes collision rows, a level summary, and a grid-by-level matrix.

## `clash_group_results`

Creates real Clash Detective `ClashResultGroup` folders from existing clash results by formula. This mutates the Navisworks document only when `apply=true`; the default is a dry-run plan.

Typical request for the current workflow: group a specific test by the owner/parent of clashing side B with `groupBySide=B` and `ancestorLevelsUp=1`.

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `apply` | bool | `false` | Dry-run unless `true`. |
| `testName` | string | `""` | Required unless `testNames` or `testHandles` is provided; exact match is preferred, then contains-match. |
| `testNames` | string[] | `[]` | Explicit tests to group. |
| `testHandles` | string[] | `[]` | Handles from `clash_list_tests`, for example `clash-test:3`. |
| `statusFilters` | string[] | `[]` | Empty means default `New,Active` unless `includeAllStatuses=true`. |
| `includeAllStatuses` | bool | `false` | Include all clash statuses. |
| `groupBySide` | string | `B` | `A` or `B`; chooses which clash side drives the grouping formula. |
| `groupBy` | string | `ancestor` | `ancestor`, `root`, or `source_file`. `root` resolves the appended model directly and does not depend on tree depth. |
| `ancestorLevelsUp` | int | `1` | Number of model-tree parent levels to climb from the clashing side item. `0` groups by the exact clashing item. |
| `groupNameMode` | string | `owner_name` | `owner_name` or `owner_path`; use `owner_path` when visible owner names are duplicated. |
| `groupNamePrefix` | string | `""` | Optional prefix for generated group names. |
| `includeNavisHelperSideTag` | bool | `true` | Appends ` [NH:A]` or ` [NH:B]` so the NavisHelper UI can detect the grouping side. |
| `overwriteExisting` | bool | `false` | Existing group-name conflicts are skipped unless this is `true`; with `true`, matching groups are rebuilt to the new target result set. |
| `ungroupExistingFirst` | bool | `false` | Before applying, removes existing NavisHelper groups for the selected side and optional prefix, moving their children back to the test. Use carefully. |
| `minGroupSize` | int | `2` | Groups with fewer matching raw results are skipped. |
| `maxResults` | int | `500` | Maximum filtered raw results to analyze, capped at 50000. |
| `previewRowsPerGroup` | int | `5` | Bounded raw clash preview rows per planned group, capped at 50. |
| `groupOffset`, `groupLimit` | int | `0`, `500` | Stable pagination over planned groups; limit is capped at 5000. |
| `aggregateOnly` | bool | `false` | Clears preview rows and returns compact group aggregates. |
| `confirmLargeGrouping` | bool | `false` | Required for `apply=true` when the analyzed scope exceeds 1000 results. |
| `excludeItemNameContains` | string[] | `[]` | Exclude clashes before grouping when either side name/path contains a filter. |
| `instanceId` | string | `""` | Optional explicit Navisworks host. |
| `navisworksVersion` | string | `""` | Optional version filter. |

Outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `applied`, `message` | scalar | Whether the document was mutated and summary text. |
| `matchedTestCount`, `totalResultCount`, `matchedResultCount`, `analyzedResultCount`, `resultsTruncated`, `maxResults` | scalar | Scope and truncation counts. |
| `groupBySide`, `ancestorLevelsUp`, `groupNameMode`, `groupNamePrefix`, `includeNavisHelperSideTag`, `overwriteExisting`, `ungroupExistingFirst`, `minGroupSize` | scalar | Effective formula/settings. |
| `plannedGroupCount`, `returnedGroupCount`, `groupsTruncated`, `nextGroupOffset` | scalar | Honest group-array pagination. Never infer completeness from `resultsTruncated`. |
| `appliedGroupCount`, `movedResultCount`, `skippedResultCount`, `conflictGroupCount` | int | Grouping result counts. |
| `largeGroupingThreshold`, `largeGroupingConfirmationRequired` | scalar | Safety gate for large mutations. |
| `totalStatusCounts`, `matchedStatusCounts`, `returnedStatusCounts` | object | Status rollups. |
| `excludedByItemNameCount`, `excludedByItemNameCounts` | scalar/object | Exclusion counts by text filter. |
| `groups[]` | array | Planned/applied groups with `testIndex`, `testHandle`, `testName`, `groupName`, clean owner fields, result count, conflict/apply status, moved count, error message, and bounded `previewRows[]`. |
| `warnings[]` | string[] | Existing group conflicts, truncation warnings, and non-fatal apply errors. |

Safe workflow:

1. Call `clash_list_tests`.
2. Call `clash_group_results` with `apply=false` and explicit `testName` or `testHandles`.
3. Review `groups[]`, `plannedGroupCount`, duplicate-looking names, and `conflictGroupCount`.
4. If the plan is correct, call the same request with `apply=true`.
5. Use `overwriteExisting=true` only when rebuilding matching groups is intended. Use `ungroupExistingFirst=true` only when replacing old NavisHelper side groups for that side/prefix.

## `clash_root_matrix`

Builds the coordination artifact `{(rootA, rootB): clashCount}` from existing results. Root IDs, names, and source files come from `ModelItem.Model`; no `.rvm` parsing or fixed `ancestorLevelsUp` value is used. Same-source-model clashes are excluded by default. The response is paged with `plannedPairCount`, `returnedPairCount`, `pairsTruncated`, and `nextOffset`.

`clash_tests_from_sets` also accepts `rootName`/`sourceFile` in either pair reference. Pass `ignoreRules: { "sameFile": true }` to enable the native Clash Detective same-file ignore rule before calculation.

## `clash_renumber_results`

Renumbers real Clash Detective groups/results inside selected tests. This is intended as the final cleanup after tests were run and grouping was created manually or through `clash_group_results`.

Default scope is `top_level`: direct test children only. That means real `ClashResultGroup` folders and ungrouped `ClashResult` rows are numbered exactly as the user sees them in the standard Clash Detective form. Use `recursive` only when nested group contents must also be renamed.

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `apply` | bool | `false` | Dry-run unless `true`. |
| `testName` | string | `""` | Required unless `testNames` or `testHandles` is provided; exact match is preferred, then contains-match. |
| `testNames` | string[] | `[]` | Explicit tests to renumber. |
| `testHandles` | string[] | `[]` | Handles from `clash_list_tests`, for example `clash-test:3`. |
| `scope` | string | `top_level` | `top_level` or `recursive`. |
| `orderBy` | string | `current` | `current` uses Clash Detective tree order; `name` uses natural display-name order. |
| `startNumber` | int | `1` | First assigned number. |
| `numberWidth` | int | `4` | Minimum zero-padded width. `1` with width `4` becomes `0001`; larger numbers expand naturally. |
| `separator` | string | ` - ` | Text between number and old clean name when `preserveExistingName=true`. |
| `prefix` | string | `""` | Text before the number, for example `C-`. |
| `suffix` | string | `""` | Text after the number, before the preserved old name. |
| `preserveExistingName` | bool | `true` | Keep the old clean name after the generated number. |
| `stripExistingNumber` | bool | `true` | Remove an existing leading number before adding the new one. |
| `includeGroups` | bool | `true` | Include `ClashResultGroup` folders. |
| `includeResults` | bool | `true` | Include individual `ClashResult` rows. |
| `includeEmptyGroups` | bool | `false` | Include empty groups. |
| `confirmRename` | bool | `false` | Required with `apply=true`; renaming is a document mutation. |
| `limit` | int | `5000` | Maximum items to plan/rename, capped at 50000. |
| `instanceId` | string | `""` | Optional explicit Navisworks host. |
| `navisworksVersion` | string | `""` | Optional version filter. |

Outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `applied`, `message`, `confirmationRequired` | scalar | Apply state and safety gate. |
| `matchedTestCount`, `plannedRenameCount`, `renamedCount`, `skippedCount`, `limit`, `truncated` | scalar | Scope and rename counts. |
| `scope`, `orderBy`, `startNumber`, `numberWidth`, `separator`, `prefix`, `suffix`, `preserveExistingName`, `stripExistingNumber`, `includeGroups`, `includeResults` | scalar | Effective settings. |
| `items[]` | array | Per-item plan: `index`, `number`, `numberText`, `testHandle`, `testName`, `itemType`, `groupPath`, `oldName`, `newName`, `status`, `errorMessage`. |
| `warnings[]` | string[] | Non-fatal warnings and per-item rename errors. |

Safe workflow:

1. Call `clash_renumber_results` with `apply=false`, explicit test scope, and the desired number format.
2. Review `items[].oldName` and `items[].newName`.
3. Call the same request with `apply=true` and `confirmRename=true` only after the dry-run plan is acceptable.

## `clash_bbox_pair_plan`

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `rootMode` | string | `top_level_files` | Uses model roots and direct children, matching `list_root_items`. |
| `rootNames` | string[] | `[]` | Exact root names/paths/source files to include. |
| `nameContains` | string | `""` | Contains filter for root name/path/source file. |
| `excludeNameContains` | string[] | `[]` | Contains filters to exclude roots. |
| `refineDepth` | int | `1` | `0` root bbox only; `1` children; `2` grandchildren. |
| `bboxToleranceMm` | double | `0` | Bbox expansion in millimeters. |
| `maxRootItems` | int | `500` | Maximum roots evaluated. |
| `maxCandidatePairs` | int | `50000` | Stop after this many candidate pairs. |
| `previewLimit` | int | `200` | Inline roots/candidates/rejected preview limit. |
| `includeRejected` | bool | `false` | Include skipped/rejected pair preview. |
| `outputPath` | string | `""` | Exact absolute JSON/CSV plan path. Dry-run does not write it and returns it as `calculatedOutputPath`; `apply=true` requires it. JSON can feed `clash_pair_tests_create.planOutputPath`. |
| `instanceId` | string | `""` | Optional explicit Navisworks host. |
| `navisworksVersion` | string | `""` | Optional version filter. |

Outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `rootMode`, `refineDepth`, `bboxToleranceMm` | scalar | Effective planner settings. |
| `totalRootItems`, `returnedRootItems`, `rootPairCount`, `candidatePairCount`, `skippedPairCount` | int | Planner counts. |
| `rootItemsTruncated`, `candidatePairsTruncated`, `previewTruncated` | bool | Scope/preview truncation flags. |
| `elapsedMs`, `outputPath`, `calculatedOutputPath` | scalar | Runtime, verified written path, and dry-run-only calculated path. |
| `outputWritten`, `artifactStatus`, `bytesWritten`, `sha256` | scalar | Verified artifact outcome. Dry-run always reports `outputWritten=false`. |
| `requestedRootNames`, `matchedRootNames`, `unmatchedRootNames`, `notEvaluatedRootNames` | string[] | Explicit outcome for every exact `rootNames` input. |
| `skippedReasonCounts` | object | Counts by rejection reason. |
| `rootItems[]` | array | `index`, `name`, `path`, `sourceFile`, `childCount`, `boundingBox`. |
| `candidatePairs[]` | array | `index`, `a`, `b`, `checkedChildPairCount`, `childIntersectingPairCount`, `reason`. |
| `rejectedPairs[]` | array | `index`, `a`, `b`, `reason`, `warning`. |
| `warnings[]` | string[] | Non-fatal warnings. |

## `clash_pair_tests_create`

This remains a BBox/model-root-oriented tool. Each side resolves in strict
order: exact full path, unique exact root display name, then unique exact
source-file identity. It never uses contains/fuzzy matching and never chooses
the first ambiguous candidate. `tests[].aResolution` and `bResolution` report
the side, supplied fields, strategy/status, match count, and compact candidates.
If a side matches a Selection Set/Search Set, the diagnostic directs the caller
to `clash_tests_from_sets` or `clash_batchtest_import`.

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `apply` | bool | `false` | Dry-run unless `true`. |
| `pairs` | object[] | `[]` | Candidate pairs returned by `clash_bbox_pair_plan`; prefer `planOutputPath` for large plans. |
| `planOutputPath` | string | `""` | JSON artifact from `clash_bbox_pair_plan`. |
| `testNamePrefix` | string | `NH-BBOX` | Generated test name prefix. |
| `limit` | int | `200` | Create/preview only first N pairs. |
| `toleranceMm` | double | `-1` | `-1` leaves Navisworks default; non-negative sets tolerance. |
| `testType` | string | `hard` | See common test type values. |
| `overwriteExisting` | bool | `false` | Replace tests with same generated names. |
| `instanceId` | string | `""` | Optional explicit Navisworks host. |
| `navisworksVersion` | string | `""` | Optional version filter. |

Outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `applied`, `testNamePrefix`, `message` | scalar | Apply state and summary. |
| `inputPairCount`, `plannedTestCount`, `createdTestCount`, `skippedTestCount`, `conflictTestCount` | int | Test creation counts. |
| `tests[]` | array | `pairIndex`, `testName`, `aName`, `aPath`, `bName`, `bPath`, `selectionAItemCount`, `selectionBItemCount`, `applied`, `status`, `errorMessage`. |
| `warnings[]` | string[] | Non-fatal warnings. |

## `clash_create_matrix_from_selection`

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `apply` | bool | `false` | Dry-run unless `true`. |
| `namePrefix` | string | `""` | Empty means no prefix unless `useGeneratedPrefix=true`; supports `yyyyMMdd_HHmmss` token. |
| `useGeneratedPrefix` | bool | `false` | If true with empty `namePrefix`, uses `[NH-MATRIX] yyyyMMdd_HHmmss `. |
| `toleranceMm` | double | `-1` | `-1` leaves Navisworks default; non-negative sets tolerance. |
| `testType` | string | `hard` | See common test type values. |
| `runAfterCreate` | bool | `false` | Run only newly created tests after creation. |
| `removePreviousGenerated` | bool | `false` | Deletes previous generated tests only when `apply=true` and effective prefix is non-empty. |
| `matrixItemNames` | string[] | `[]` | Exact item names, paths, or source files; empty uses current selection unless filters are set. |
| `matrixNameContains` | string | `""` | Contains filter over model-tree name/path/source file. |
| `matrixExcludeNameContains` | string[] | `[]` | Contains filters to exclude matrix items. |
| `maxSelectedItems` | int | `100` | Maximum matrix input items. |
| `confirmLargeMatrix` | bool | `false` | Required above the large matrix threshold. |
| `includePairNames` | bool | `true` | Include selected item names and planned/created pair rows. |
| `instanceId` | string | `""` | Optional explicit Navisworks host. |
| `navisworksVersion` | string | `""` | Optional version filter. |

Outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `applied`, `namePrefix`, `useGeneratedPrefix`, `message` | scalar | Apply state and naming summary. |
| `selectedItemCount`, `plannedPairCount`, `plannedTestCount`, `createdTestCount`, `ranTestCount`, `removedPreviousTestCount`, `skippedTestCount` | int | Matrix/test counts. |
| `largeMatrixConfirmationRequired`, `largeMatrixThreshold` | bool/int | Large matrix guard. |
| `toleranceMm`, `testType`, `runAfterCreate`, `removePreviousGenerated`, `matrixInputSource`, `elapsedMs`, `plannedTestsTruncated` | scalar | Effective settings/runtime. |
| `selectedItems[]` | array | `selectionIndex`, `name`, `path`, `sourceFile`. |
| `tests[]` | array | `pairIndex`, `aSelectionIndex`, `bSelectionIndex`, `handle`, `testHandle`, `testName`, `aName`, `aPath`, `bName`, `bPath`, `applied`, `ran`, `status`, `errorMessage`. |
| `warnings[]` | string[] | Non-fatal warnings. |

## `clash_manage_tests`

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `apply` | bool | `false` | Dry-run unless `true`. |
| `operation` | string | `run` | `run`, `reset`, `compact`, `rename`, `delete`, `move`, `sort`, `set_settings`; aliases include `execute`, `clear`, `remove`, `reorder`, `sort_by_name`, `settings`. |
| `testName` | string | `""` | Single test name; exact match preferred, then contains-match. |
| `testNames` | string[] | `[]` | Multiple test names. |
| `testHandles` | string[] | `[]` | Handles from `clash_list_tests`, for example `clash-test:1`. |
| `namePrefix` | string | `""` | Prefix scope, useful for generated tests such as `NH-BBOX`. |
| `firstN` | int | `0` | Optional first N tests from matched scope; bare `firstN` cannot delete/reset with `apply=true`. |
| `newName` | string | `""` | Required for `rename`; exactly one matched test. |
| `targetIndex` | int | `0` | Required for `move`; 1-based index inside current folder/root. |
| `sortDirection` | string | `asc` | `asc`, `name`, `natural`, or `desc`. |
| `toleranceMm` | double | `-1` | For `set_settings`; `-1` leaves unchanged. |
| `testType` | string | `""` | For `set_settings`; empty leaves unchanged. |
| `instanceId` | string | `""` | Optional explicit Navisworks host. |
| `navisworksVersion` | string | `""` | Optional version filter. |

Outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `applied`, `operation`, `requestedTestName`, `message` | scalar | Operation summary. |
| `matchedTestCount`, `affectedTestCount` | int | Scope and affected counts. |
| `tests[]` | array | `testIndex`, `handle`, `testHandle`, `name`, `operation`, `applied`, `status`, `errorMessage`, `oldIndex`, `newIndex`, `oldToleranceMm`, `newToleranceMm`, `oldTestType`, `newTestType`. |
| `warnings[]` | string[] | Non-fatal warnings. |

## `clash_save_viewpoints`

Creates Saved Viewpoints from existing Clash Detective results. It does not run tests, generate reports, write external report files, or capture screenshots.

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `apply` | bool | `false` | Dry-run unless `true`. |
| `testName` | string | `""` | Empty means all tests; exact match preferred, then contains-match. |
| `testNames` | string[] | `[]` | Multiple tests; empty with `testName` empty means all tests. |
| `statusFilters` | string[] | `[]` | Empty means default New/Active unless `includeAllStatuses=true`. |
| `includeAllStatuses` | bool | `false` | Include all clash statuses. |
| `limit` | int | `100` | Batch size. |
| `resultOffset` | int | `0` | Zero-based offset for batching. |
| `confirmLargeViewpoints` | bool | `false` | Required for very large scopes. |
| `folderPath` | string | `""` | Saved Viewpoints folder path; empty creates timestamped folder. |
| `createResetViewpoint` | bool | `true` | Legacy input; the host always creates `0000 Базовый вид` at the start of the folder. |
| `boxOffsetMm` | double | `1500` | Section box distance in millimeters. |
| `boxMode` | string | `point` | `point` or `items`. |
| `contextTransparency` | double | `0.5` | 0..1 transparency for context items. |
| `useFullBoxTransparency` | bool | `false` | Deprecated for saved viewpoints; ignored because Saved Viewpoints are saved without transparency. |
| `useRootContextTransparency` | bool | `false` | Deprecated for saved viewpoints; ignored because Saved Viewpoints are saved without transparency. |
| `createOppositeViewpoints` | bool | `false` | Save two viewpoints per clash: standard and opposite diagonal ISO. |
| `colorAHex`, `colorBHex` | string | `""` | Optional side colors as `#RRGGBB`; empty uses defaults. |
| `includeClashPointMarker` | bool | `false` | Draw redline target marker at clash point. |
| `excludeItemNameContains` | string[] | `[]` | Exclude result if either side name/path contains any filter. |
| `instanceId` | string | `""` | Optional explicit Navisworks host. |
| `navisworksVersion` | string | `""` | Optional version filter. |

Outputs:

| Field | Type | Notes |
| --- | --- | --- |
| `applied`, `requestedTestName`, `folderPath`, `message` | scalar | Apply state and destination. |
| `matchedTestCount`, `totalResultCount`, `matchedResultCount`, `returnedResultCount`, `createdViewpointCount` | int | Scope/result/viewpoint counts. |
| `resultOffset`, `nextResultOffset`, `hasMoreResults`, `truncated` | scalar | Batching state. |
| `largeViewpointsThreshold`, `largeViewpointsConfirmationRequired` | scalar | Large scope guard. |
| `totalStatusCounts`, `matchedStatusCounts`, `returnedStatusCounts` | object | Status count summaries. |
| `excludeItemNameContains`, `excludedByItemNameCount`, `excludedByItemNameCounts` | scalar/object | Exclusion filter summary. |
| `boxOffsetMm`, `boxMode`, `resetViewpointCreated`, `resetViewpointName`, `fullBoxTransparencyItemCount` | scalar | Effective viewpoint settings/counts. |
| `items[]` | array | `index`, `testIndex`, `resultIndex`, `testName`, `groupPath`, `resultName`, `status`, `assignedTo`, `distance`, `boxOffsetMm`, `boxMode`, `clashPoint`, `clashBox`, `item1Name`, `item2Name`, `item1ItemCount`, `item2ItemCount`, `viewpointName`, `viewpointPath`, `viewpointCreated`, `fullBoxTransparencyItemCount`, `errorMessage`. |
| `warnings[]` | string[] | Non-fatal warnings. |

## `clash_generate_report`

Creates report artifacts from existing Clash Detective results; can optionally run tests first.

Inputs share the same test/status/batch, box, color, marker, and exclusion fields as `clash_save_viewpoints`. `clash_generate_report` does not accept `folderPath`, `createResetViewpoint`, `useRootContextTransparency`, or `createOppositeViewpoints`; those are Saved Viewpoints-only controls. Report-specific inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `outputDirectory` | string | `""` | Empty creates timestamped output folder near the active model or under Documents. |
| `overwrite` | bool | `false` | Allow writing into a non-empty output folder. |
| `append` | bool | `false` | Append batch to existing report artifacts. |
| `confirmLargeReport` | bool | `false` | Required for very large report scopes. |
| `runTests` | bool | `false` | Requires `apply=true`; runs matched tests before reading results. |
| `createViewpoints` | bool | `true` | Create report-managed Saved Viewpoints. |
| `captureScreenshots` | bool | `true` | Capture screenshots when Navisworks export is available. |
| `captureTopViewScreenshots` | bool | `false` | Capture additional orthographic top view images. |
| `screenshotProfile` | string | `compact` | `compact`, `fullhd` / `standard`, `large`, or `source`. |
| `screenshotFormat` | string | `""` | `jpg`/`jpeg`, `png`, or `bmp`; empty uses profile default. |
| `screenshotMaxWidth`, `screenshotMaxHeight` | int | `0` | 0 uses profile default; images are not upscaled. |
| `screenshotJpegQuality` | int | `0` | 1..100; 0 uses profile default. |
| `groupMode` | string | `none` | Optional cluster mode: `none`, `hybrid`, `object_pair`, or `spatial`. |
| `artifactGranularity` | string | `result` | `result` creates one visual artifact per raw clash. `cluster` creates one shared viewpoint/screenshot set per cluster and requires a non-`none` `groupMode`, `resultOffset=0`, `append=false`, and the complete filtered scope in one call. |
| `verbosity` | string | `full` | `compact` omits duplicated long paths, descriptions, association keys, and cluster preview rows from the MCP response only. Generated report files always retain full data. |
| `clusterDistanceMm` | double | `300` | Maximum point distance for spatial cluster grouping/splitting. |
| `includeClusterMembers` | bool | `true` | Include bounded raw member previews inside cluster summaries. |
| `maxMembersPerClusterInHtml` | int | `25` | Member preview rows per cluster in manifest/HTML, capped at 200. |

Additional outputs beyond `clash_save_viewpoints`:

| Field | Type | Notes |
| --- | --- | --- |
| `operationId`, `cancelled`, `runTestsRequested`, `testsRun` | scalar | Operation/run status. |
| `accumulatedResultCount` | int | Accumulated count across appended batches. |
| `largeReportThreshold`, `largeReportConfirmationRequired` | scalar | Large scope guard. |
| `outputDirectory`, `reportPath`, `manifestPath`, `clashBoxesPath` | string | Written artifact paths. |
| `screenshotCount`, `screenshotProfile`, `screenshotFormat`, `screenshotMaxWidth`, `screenshotMaxHeight`, `screenshotJpegQuality` | scalar | Screenshot output summary. |
| `groupMode`, `artifactGranularity`, `clusterDistanceMm`, `clusterCount`, `returnedClusterCount` | scalar | Effective cluster/artifact settings and counts when report clustering is enabled. |
| `verbosity`, `responseCompacted`, `compactOmittedFields[]` | scalar/array | Declares whether transport compaction was applied and lists fields omitted only from the MCP response. |
| `clusters[]` | array | Same `ClashClusterSummary` shape as `clash_list_clusters`; included in `manifest.json` and rendered in `report.html` when `groupMode != none`. |
| `items[]` | array | Same clash fields as viewpoint items plus `description`, `item1Path`, `item2Path`, `screenshotPath`, `screenshotCaptured`, `topViewScreenshotPath`, `topViewScreenshotCaptured`, `clusterIndex`, `clusterId`, and `clusterName`. |

Report clustering is metadata-only in this phase. Use it to inspect cluster counts and raw-to-cluster assignment while keeping the existing one screenshot/viewpoint per raw clash behavior.

## `clash_isolate_result`

Creates a transient interactive preview for one existing Clash Detective result addressed by `resultHandle`. It does not create a Saved Viewpoint or change clash test data. Dry-run is the default.

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `resultHandle` | string | required | Handle from `clash_list_results`, for example `clash-result:1:1`. |
| `boxMode` | string | `point` | `point` clips around the clash point; `items` clips around combined A/B bounds. |
| `boxOffsetMm` | double | `1000` | Positive point-box half-size or non-negative item-box padding. `0` with `boxMode=items` uses the combined A/B bounds; only zero-thickness axes receive the minimum Navisworks-safe extent. |
| `useSectionBox` | bool | `true` | Enables temporary clipping. |
| `isolatePair` | bool | `false` | Temporarily hides branches outside sides A/B while preserving enough hierarchy to reveal both sides. |
| `useContextTransparency`, `contextTransparency` | scalar | `false`, `0.7` | Optional nearby-context transparency. |
| `colorAHex`, `colorBHex` | string | red, blue | Accept `#RRGGBB`, RAL, or `R,G,B`. |
| `cameraMode` | string | `current` | `current`, `iso`, `iso_opposite`, `top`, `front`, `back`, `left`, `right`, or `custom`. |
| `cameraPosition` | point | `null` | Required for `custom`; exact document-coordinate camera position. |
| `cameraTarget`, `cameraUp` | point/vector | clash point, global +Z | Optional custom target and up vector. |
| `projection` | string | `current` | `current`, `orthographic`, or `perspective`. `current` preserves the active projection for ISO/custom cameras; orthogonal side/top presets default to orthographic. |
| `screenshotPath` | string | `""` | Optional absolute image path captured after isolation and camera setup. |
| screenshot controls | scalar | report defaults | Same profile, format, size, and JPEG quality controls as `clash_generate_report`. |
| `overwriteScreenshot` | bool | `false` | Allows replacing an existing image. |
| `apply` | bool | `false` | Apply changes to the transient active view. |

The response includes resolved test/result metadata, clash point and planned box in both dry-run and apply responses, effective settings, isolation counts/timing, screenshot outcome, and `canReset`.

## `clash_reset_isolation`

Restores the original viewpoint, section box, A/B appearance overrides, and temporary pair visibility recorded by the most recent `clash_isolate_result` sequence in the active document. It defaults to dry-run and never resets unrelated isolation from another document/session.

## `capture_current_view`

Captures the current Navisworks view exactly as displayed, including a manually adjusted camera after `clash_isolate_result`.

`outputPath` is a required absolute `.png`, `.jpg`, `.jpeg`, or `.bmp` path. Screenshot profile/format/size/quality parameters match `clash_generate_report`; `overwrite=false` and `apply=false` are the defaults.

## `clash_report_status`

Inputs:

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `operationId` | string | `""` | Empty means active report, or last report if none active. |
| `instanceId` | string | `""` | Optional explicit Navisworks host. |
| `navisworksVersion` | string | `""` | Optional version filter. |

Key outputs:

`operationId`, `state`, `isRunning`, `cancelRequested`, `cancelAccepted`, `outputDirectory`, `reportPath`, `manifestPath`, `currentTestName`, `currentResultName`, `resultOffset`, `totalBatchCount`, `processedResultCount`, `createdViewpointCount`, `screenshotCount`, `startedAtUtc`, `updatedAtUtc`, `completedAtUtc`, `elapsedMs`, `message`.

## `cancel_clash_report`

Inputs are the same as `clash_report_status`.

Output is a `clash_report_status` response. Cancellation is cooperative: the current screenshot/viewpoint step may finish before the operation stops and writes partial artifacts.

## Persistent scenario library

Scenario files use schema version 1 and live under `%APPDATA%\NavisHelper\Scenarios`. The library is implemented only in the .NET 9 MCP server; it does not add or change a Navisworks host command.

### `list_scenarios`

Optional inputs are `query`, `navisworksVersion`, `rootFileNames`, `projectLabel`, and `limit` (default 3, maximum 20). The response contains bounded metadata, `scenarioId`, `executionMode`, SHA-256, and advisory `strong|partial|weak|mismatch` context grades. It never returns a resolved call plan.

### `get_scenario`

Input: `scenarioId`. Returns the validated persisted schema, SHA-256 concurrency token, and file path. It is read-only.

### `save_scenario`

Inputs: a schema-version-1 `scenario` draft, optional `scenarioId` plus `expectedSha256` for updates, `apply`, `confirmSave`, and `confirmExactReplay`. It defaults to preview. Apply writes one UTF-8 JSON file atomically and never stores `apply/confirm` fields, host/document/item identities, credentials, or transcripts.

`executionMode=template` declares runtime parameters. `executionMode=exactReplay` additionally requires unique name, fixed values, strict context, `repeatReviewedWrites`, a reviewed safety envelope, and the dedicated confirmation. The server replaces `safetyEnvelope.previewFingerprint` with the SHA-256 of the canonical resolved exact plan before writing; a later mismatch makes the file invalid.

### `delete_scenario`

Inputs: `scenarioId`, `expectedSha256`, `apply`, and `confirmDelete`. It previews by default and deletes exactly one matching scenario file after optimistic-concurrency validation.

### `resolve_scenario`

Inputs: `scenarioId`, optional template `parameterValues`, `executionIntent=preview|exact_replay`, and optional context hints. It returns ordered existing-tool preview arguments, apply overrides, per-step plan hashes, planned write categories, and an `agentInstruction`; it never calls Navisworks itself.

`exact_replay` is valid only after a direct current user request. It rejects parameter overrides and requires a strong strict-context match. A normal preview of an exact scenario returns no apply override and explicitly forbids execution. The initial operation allowlist is `selection_export_properties`, `selection_sets_build_viewpoints`, `clash_generate_report`, and `clash_save_viewpoints`.
## `get_current_section_box`

Read-only capture of the enabled Section/Clip Box in the active view. The host strictly parses the `View.GetClippingPlanes()` `ClipPlaneSet` payload and accepts only enabled `OrientedBox3D` version 1 data. Disabled boxes, plane mode, malformed JSON, non-finite values, and unsupported versions return typed MCP errors. The tool does not change the viewpoint, clipping state, selection, or visibility and never exposes raw clipping JSON as its executable contract.

The returned `box` is canonical replay geometry:

| Field | Type | Notes |
| --- | --- | --- |
| `formatVersion` | int | Currently `1`. |
| `coordinateSpace` | string | Always `document_global`. |
| `documentUnits` | string | Normalized active-document units. |
| `center` | vector3 | Absolute world center in document units. |
| `halfExtents` | vector3 | Positive half sizes in document units. |
| `axes[3]` | vector3[] | Right-handed orthonormal world axes. Navisworks converts the source Euler rotation before these axes are returned. |

## `isolate_by_box`

Classifies visited `ModelItem` nodes by intersection between their world axis-aligned bounding boxes and the explicit oriented volume. The test uses the full 15-axis separating-axis theorem; touching the boundary counts as intersection. Autodesk defines `ModelItem.BoundingBox()` as the bounding box of the item and its children. Therefore, when a readable parent box is strictly outside the oriented volume, its entire subtree is outside: the traversal records that parent as a pruned subtree root, stops descending that branch, and hides only the parent visibility target. Matching items and all ancestors required to keep matching descendants visible remain visible.

An unreadable node with no own geometry is treated explicitly as structure rather than a geometry failure: a container is preserved while its children are inspected, and an empty leaf is preserved without descent. A node with geometry, or whose geometry status cannot be read, remains a genuine classification error and is conservatively preserved with all ancestors. Genuine classification errors reject `apply=true`.

| Parameter | Type | Default | Notes |
| --- | --- | --- | --- |
| `box` | `SectionBoxGeometry` | required | Literal canonical geometry; units must match the active document. |
| `apply` | bool | `false` | Preview only unless true. |
| `maxScannedItems` | int | `500000` | Must be `1..500000`; reaching it before traversal finishes marks the result partial. Exact replay stores this value literally. |
| `maxDurationSeconds` | int | `60` | Must be `1..480`; bounds traversal, bounding-box/SAT classification, and visibility planning. Raise it explicitly on loaded workstations. Exact replay stores the literal value and repeats it in the step safety envelope. |
| `previewLimit` | int | `10` | Visibility-change preview limit (revealed or newly hidden items), maximum 50. |

The complete hierarchy-aware traversal and conservative visibility plan are calculated before any visibility write. Timeout or truncation rejects `apply=true` with `applyRejected=true`, `applyRejectionCode=incomplete_box_traversal`, and no visibility mutation. A genuine geometry classification error rejects apply with `applyRejectionCode=box_classification_errors`. The duration check happens before each remaining traversal/classification/planning unit. If the last unit finishes at or just after the boundary and no work remains, completion wins; a late final stopwatch read does not turn a complete plan into a timeout. The response reports the effective `maxDurationSeconds` and `elapsedMilliseconds`.

The default scan count intentionally equals the 500,000 hard maximum so ordinary federated models are governed by the explicit duration rather than the former 100,000-item truncation. Callers may lower `maxScannedItems` for a stricter deterministic scope; reaching that lower limit still rejects apply atomically.

`scannedItemCount` is the number of nodes actually visited and classified; it is not a claim that every model descendant was enumerated. `prunedSubtreeRootCount` counts readable outside nodes whose descendants were skipped, while `prunedDirectChildBranchCount` counts only their immediate skipped child branches. The total number of pruned descendants is deliberately unknown because enumerating it would defeat the optimization. Direct classification counts obey `scannedItemCount = intersectingItemCount + outsideItemCount + conservativeUnclassifiedItemCount + structuralContainerItemCount + emptyItemCount`. `wouldHideItemCount` counts explicit visibility targets; hiding a pruned outside parent also hides its unenumerated descendants through Navisworks hierarchy semantics. `wouldChangeVisibilityItemCount` counts only explicit reveal/hide mutations.

Genuine unreadable geometry nodes are reported as `conservativeUnclassifiedItemCount`, appear in `preservedUnclassifiedPreview`, and remain visible with their ancestors. Non-geometry invalid-bounds nodes are reported separately as `structuralContainerItemCount` or `emptyItemCount` and do not increment `classificationErrorCount`. Replay never reads or changes the active Section Box and preserves the current selection. Repeated successful apply calls converge on the same explicit parent visibility result; rollback restores only visibility targets actually written, so descendants retain their own prior flags.

All Autodesk `Document`/`ModelItem`/visibility access remains synchronous on the Navisworks UI thread; this implementation deliberately does not use `Task.Run` with Autodesk objects. A long traversal therefore blocks interactive Navisworks work until it completes or reaches its finite duration bound. The 60-second default covers the measured 73,579/111,599-item owner models (about 4–17 seconds) and extrapolates to roughly 25–46 seconds for 500,000 items at their unloaded rates. Contended multi-Navisworks smoke was materially slower, so callers can raise the explicit bound up to 480 seconds; no finite value guarantees completion.

Timeout layers are coordinated as follows. `isolate_by_box` gives HostBridge an effective budget of `maxDurationSeconds + 100` seconds: 90 seconds are reserved after planning for the two synchronous visibility writes, selection restore, redraw, response creation, and rollback if a write throws; 5 seconds cover bridge discovery/setup; and the named-pipe response margin is 5 seconds. Thus the default bridge/nominal-host budgets are 160/155 seconds and the hard-maximum budgets are 580/575 seconds, below the shared 600-second agent dispatcher cap. A client should allow an additional 5-second response margin: at least 165 seconds for the default or `maxDurationSeconds + 105` seconds in general (585 seconds at the hard maximum). The MCP stdio server adds no separate fixed request deadline, but an external MCP client may cancel earlier. Client cancellation cannot safely abort an already-running Navisworks UI callback; use `last_operation_status` after a disconnect or client timeout. Visibility writes are not split by the classification timer: if a Navisworks visibility call throws, the service attempts to restore the captured per-item visibility state, but forced process termination or an external client disconnect cannot make that synchronous Autodesk transaction universally atomic.

For Scenario Library exact replay: call `get_current_section_box` before authoring, preview/apply `isolate_by_box`, then save only `isolate_by_box` with the returned `box`, chosen `maxScannedItems`, and chosen `maxDurationSeconds` embedded as literals. The step safety envelope must repeat the same `maxDurationSeconds`; both the literal argument and safety value participate in the canonical fingerprint. Runtime references and argument/safety mismatches are rejected. Do not store capture, `$stepResult`, match handles, selection dependencies, or a fallback to the current Section Box. `get_current_section_box` is intentionally absent from the scenario allowlist; `isolate_by_box` is an allowlisted mutating tool with `apply`.
