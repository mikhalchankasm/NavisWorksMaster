# MCP tool latency baseline

A measured starting point for "every tool is fast", so a later change can be compared
against something instead of against an impression. Until this existed, the repository
had precise numbers for four tools and none for the rest.

## What was measured

| | |
| --- | --- |
| date | 2026-09-20 |
| model | `D:\Downloads\6501.5.nwd` — federated plant model, 3 root items, ~908 MB working set |
| Navisworks | Manage 2027 |
| plugin | host-reported `pluginAssemblyLength` 1586688, `pluginAssemblyLastWriteUtc` 2026-09-20T09:09:49Z, sha256 `af60b1b9…` |
| server | built from `main` at the same commit |
| scope of this row | the read-only pass only — the two clash windows ran a **different** plugin (`20bb4356…`) and a separately launched server, and the `rootName` message was checked later still on the branch build (`pluginAssemblyLength` 1588736). Latency is comparable only within one window, so each section states its own build instead of inheriting this one. |
| tools covered | **92 of 104** advertised tools carry a measured number, counted against `tools/list` and against this document's own tables rather than by hand — 35 in the read-only pass below, 28 clash tools across two L3 windows, 28 more in a third, and `start_navisworks` / `close_navisworks` / `delete_scenario` stated in prose. The remaining **12** are named in [What still has no number](#what-still-has-no-number), with the reason for each. |

Every number is `navishelper_timing.elapsed_ms`, which is the **MCP server's** measure
of the whole call, not the Navisworks host's internal time. `McpToolTimingFilter` starts
a stopwatch before invoking the tool and stops it after, so the figure includes the
server's own work and the named-pipe round trip to the host, and excludes only the MCP
client. That is the right number for "how long does a tool take from a client's point of
view" and the wrong one for "how long does the host spend"; the host logs its own elapsed
separately, in `navishelper_log.txt`.

The warm wall-clock column is the client's view of the same call, so the gap between the
two columns is client-side transport and deserialization.

Each tool was called **twice**. The first call pays whatever index or cache warm-up it
needs; the second is the steady-state number. Both are reported because they are
different problems: a tool whose first call is much worse than its second has a cold
cost, and a tool that never improves is doing the same work every time.

## What was deliberately not measured

A baseline must not be the thing that changes the model. Left out, and why:

- **Document-mutating tools** — `create_viewpoint`, `create_selection_set`,
  `create_search_set`, `save_document`, `save_document_as`, `saved_viewpoints_*`,
  `markup_*`, `model_color_scheme`, `selection_color_by_property`. These write the
  owner's document or its saved items. **Most were measured later**, in the third window
  below, where everything created carries one prefix and is deleted again; `save_document`
  and `save_document_as` remain unmeasured on purpose, because the point of these windows
  is that the document is never saved.
- **The clash surface** — 29 tools, of which only `clash_bbox_pair_plan` (a dry-run
  plan) appears in the table below. Runs, imports and matrix creation mutate clash tests,
  so they were measured in their own L3 windows; see [The clash surface](#the-clash-surface),
  which now covers 28 of the 29.
- **Long-running jobs** — `dump_subtree_names` and its status/cancel pair, which write a
  file and are designed to be polled. **Measured in the third window** against a real job,
  including the cancel: 106 ms to start, 319 ms for the first poll, 8 ms to cancel.
- **Lifecycle** — `start_navisworks`, `close_navisworks`,
  `open_latest_navisworks_file`. Measured separately while fixing the attach behaviour:
  a launch is roughly 15 500 ms, attaching to a ready host that already holds the
  requested file is 123 ms.
- **`selection_export_properties`** — writes a file; harmless but out of scope here.

Selection, camera and visibility **were** exercised, because their effect is transient,
and the run restores them (`show_all`, nothing selected).

## The table

Selection-dependent tools were measured with one root item selected, except
`select_by_search`, which selects four items under a parent with three children and
roughly 88 000 descendants.

| group | tool | status | first (host ms) | warm (host ms) | warm (wall ms) |
| --- | --- | --- | --- | --- | --- |
| diagnostics | `host_status` | ok | 36 | 23 | 24 |
| diagnostics | `mcp_health_check` | ok | 113 | 88 | 89 |
| diagnostics | `mcp_diagnostics` | ok | 2 | 1 | 1 |
| diagnostics | `mcp_error_contract` | ok | 0 | 0 | 1 |
| diagnostics | `mcp_recent_calls` | ok | 3 | 16 | 18 |
| diagnostics | `list_navisworks_hosts` | ok | 1 | 1 | 2 |
| diagnostics | `list_recent_navisworks_files` | ok | 10 | 3 | 4 |
| query | `active_model_context` | ok | 57 | **57** | 57 |
| query | `list_root_items` | ok | 10 | 12 | 12 |
| query | `find_root_items_by_name` | ok | 13 | 12 | 13 |
| query | `list_item_children` (2-level path) | ok | 17 | 13 | 13 |
| query | `list_item_children` (7-level path) | ok | 15 | 23 | 24 |
| query | `find_items` whole_model, all, countOnly | ok | 82 | 73 | 74 |
| query | `find_items` scoped, all, countOnly | ok | 18 | 17 | 17 |
| query | `find_items` scoped, first | ok | 19 | 20 | 20 |
| query | `find_items_by_bbox` 200³ zone, 100k scan cap | ok | 5614 | **6084** | 6085 |
| selection | `selection_status` (empty selection) | ok | 16 | 12 | 12 |
| selection | `select_items` | ok | 83 | 67 | 67 |
| selection | `selection_status` (1 item, with bbox) | ok | 46 | 36 | 37 |
| selection | `selected_items_preview` | ok | 14 | 12 | 13 |
| selection | `selected_items_tree` | ok | 16 | 12 | 12 |
| selection | `selected_items_ancestry` | ok | 15 | 13 | 14 |
| selection | `selection_copy_names` | ok | 14 | 11 | 11 |
| selection | `select_by_search` descendants_of | ok | 345 | 333 | 334 |
| reports | `selection_distinct_property_values` | ok | 34 | 13 | 13 |
| reports | `selection_property_report` | ok | 18 | 14 | 19 |
| properties | `item_properties_by_handle` | ok | 34 | 12 | 14 |
| view | `current_viewpoint_info` | ok | 14 | 13 | 13 |
| view | `list_saved_viewpoints` | ok | 12 | 11 | 12 |
| view | `zoom_to_selection` | ok | 12 | 14 | 15 |
| view | `focus_on_selection` | ok | 21 | 11 | 12 |
| view | `fit_all` | ok | 20 | 19 | 19 |
| sections | `get_current_section_box` | ok | 17 | 14 | 14 |
| sets | `list_selection_sets` | ok | 13 | 39 | 39 |
| scenarios | `list_scenarios` | ok | 38 | 2 | 2 |
| scenarios | `scenario_capabilities` | ok | 8 | 0 | 6 |
| clash | `clash_bbox_pair_plan` sourceMode=selection | ok | 27 | 13 | 14 |
| visibility | `hide_selected` apply=false | ok | 19 | 14 | 15 |
| visibility | `show_all` | ok | 85 | 77 | 78 |

Every probe returned `ok`. Two probes failed on the first attempt with
`Unknown parameter(s)` because the harness passed argument names the tools do not
have — worth recording because the strict unknown-argument check is what caught it,
and a client that guesses a parameter name gets told rather than silently ignored.

## What the table says

**Exactly two warm measurements exceed 100 ms**, and most of the read surface is
10-25 ms:

1. **`find_items_by_bbox` - 6 084 ms.** Two orders of magnitude slower than anything else
   measured. See the caveat below: repeated sampling later the same day showed this
   operation is bimodal on this machine, so 6 084 is one draw from a wide distribution
   rather than a stable value.
2. **`select_by_search` - 333 ms.** Two whole-model native searches, one for the
   condition and one to resolve the parent. That is inherent to the contract rather than
   waste; the scope test itself is bounded by the answer, see
   `docs/PERSISTENT_SCENARIO_LIBRARY_CONTRACT.md`.

Separately, and *not* a latency exception: **`active_model_context` costs 57 ms on both
calls.** It is the only tool that gains nothing from a second call while its neighbours
halve, which looked like a cache that was missing. It is not. The tool is a server-side
composite of four sequential host calls: `host_status`, `list_root_items`,
`list_saved_viewpoints`, `list_selection_sets`. There is nothing to warm up, and nothing is
being redone -- it makes four round trips because it reports four things.

The arithmetic does not close, and saying so is more useful than a tidy sum. This table's
warm figures for those four are 23, 12, 11 and **39** ms, which is 85 -- more than the 57
the composite measured. The discrepancy sits in `list_selection_sets`, whose two samples
here were 13 ms then 39 ms: its warm figure is the unreliable one, and at ~12 ms the four
would sum to about 58. So four round trips is the explanation for 57 ms; the component
figures are not precise enough to derive it.

Two ways of "fixing" it that would be wrong, recorded so nobody tries them:

- **Caching it.** The context includes host status and root items; a client calls it to
  find out what is true *now*.
- **Running the four calls concurrently.** `AgentHostService` takes its request gate with
  `Wait(0)` and **rejects** a concurrent request with `host_busy` rather than queueing it.
  Concurrency here does not make the tool faster, it makes it fail. That constraint
  applies to every composite tool, not just this one.

The only real reduction available is one host command that gathers all four in a single
dispatch, saving three round trips. That is a contract addition for roughly 37 ms on a
call made once per task, so it is recorded here rather than built.

`selection_status` costs 12 ms with nothing selected and 36 ms with one item, because it
computes a bounding box. `includeBoundingBox=false` is the cheap form.

### Caveat on `find_items_by_bbox`

Re-measured repeatedly on 2026-09-20, each as the first call in a fresh Navisworks
process, 100 000 items scanned, on one unchanged build: **5 701 ms, 1 575 ms, 6 457 ms.**
More than 4x spread under nominally identical conditions, which is larger than most
differences anyone would try to measure on this tool. Treat a single bbox number as an
order of magnitude rather than a value, and do not accept a before/after pair on it
without several samples per side.

### What a truncated bbox answer means, and what does not fix it

`find_items_by_bbox` walks `RootItemDescendantsAndSelf` and reads the zone only in
`MatchesSpatialBox`, **after** an item has been scanned and its bounding box computed. So
the zone filters results and never the scan: a whole-model zone on this model scans until
the cap and reports `traversalTruncated`, and a narrower zone truncates identically.

The cap counts *scanned* items and the counter increments before every filter, so
`sourceFileContains` makes each skipped item cheaper without letting a call reach further
into the model. Raising `maxScannedItems` is the only lever that extends coverage, and
the 10-second internal budget is the next wall behind it. On `6501.5.nwd`, with roughly
270 000 nodes, the default cap of 100 000 covers about 37% and a whole-model zone is
therefore always a partial answer.

An open question for the owner rather than a decision taken: the cap could count
*examined* items instead, which would let `sourceFileContains` extend coverage rather
than only cheapen it. That is a contract change and is not made here.

## The clash surface

Measured in two agreed L3 windows on 2026-09-20, plugin `20bb4356…`: a first pass over the
read surface and the dry runs, and a second over the write paths, run control and the
creators. The session used for this work advertises no clash tools, so they were driven through a separately launched server.
**That is a property of the profile that session was started with, not of narrowing:**
`McpToolProfile` defines a `clash` set enumerating all 29, so `--tools=clash` reaches them
and a reproducer does not need the full `all` catalog.

**The reference model contains no clash tests.** Every result-bearing tool therefore
measures an empty path unless tests exist, so one throwaway matrix was created from a
4-item selection, run, read, and deleted again. The document was never saved and ended the
window with the test count back at zero; `close_navisworks` reported
`documentWasModified: true` and `discardedUnsavedChanges: true`, which is the expected end
state for that sequence and the reason `mode=discard` was used.

**These are per-call overheads, not clash-engine throughput.** Four leaf items give one pair
per test, which runs in milliseconds. `clash_run_batch`'s own contract says an overrun is
reported only after Navisworks returns and "a single synchronous test cannot be force-aborted
safely", so a real test over a 270 000-node model has no reliable timebox inside a window and
was not run. Engine throughput on real geometry is **unmeasured**.

| tool | empty document | 4 tests, 1 pair each |
| --- | --- | --- |
| `clash_list_tests` | 13 | 27 |
| `clash_list_results` | 12 | 45 |
| `clash_list_clusters` | 18 | 69 |
| `clash_root_matrix` | `schema_violation`: no tests matched | 23 |
| `clash_report_status` | 4 | 7 |
| `clash_bbox_pair_plan` `sourceMode=selection` | 11 | — |
| `clash_tests_export` | 13 | 34 |
| `clash_ignore_rules` `action=list` | — | 30 |
| `clash_create_matrix_from_selection` `apply=false` | — | 60 |
| `clash_create_matrix_from_selection` `apply=true` | — | 72 |
| `clash_run_batch` `apply=false` | — | 30 |
| `clash_run_batch` `apply=true` | — | 20 |
| `clash_run_status` waiting for completion | — | **1 059** |
| `clash_renumber_results` `apply=false` | — | 33 |
| `clash_generate_report` `apply=false` | — | 99 |
| `clash_save_viewpoints` `apply=false` | — | 56 |
| `clash_manage_tests` `operation=delete` `apply=true` | — | not timed here — used as cleanup; timed in the second window |

Nothing here is slow. `clash_run_status` at about a second is the run itself finishing, not
overhead. Every write-capable clash tool defaults to `apply=false`, so the call an agent
makes first is a dry run, and the dry runs cost between 30 and 99 ms.

### What the first window found

Latency was not the useful result. Two contract problems were:

- **Four scope error messages named parameters their tool rejects.** The shared
  `ResolveClashTests` guard offered `testName, testNames, testHandles, namePrefix, or firstN`
  to every caller, but four of the five callers that require a scope pass `null` for
  `namePrefix` and `firstN` because their tools do not expose them. Following the advice from
  `clash_export_points` produced `Unknown parameter(s) for tool 'clash_export_points':
  'namePrefix'` from the tool that had just asked for it. Hit twice while measuring. Each
  message now lists only what its own tool accepts and names the tool, matching
  `clash_group_results` and `clash_renumber_results`, which already did.
- **The scope vocabulary differs per tool with no visible rule.** `clash_manage_tests` takes
  all five, `clash_run_batch` takes four but not `testName`, `clash_tests_export` takes three,
  `clash_export_points` two, `clash_generate_report` and `clash_save_viewpoints` only
  `testName`/`testNames`, `clash_list_results` only `testName`. Not changed here -- widening a
  scope surface is a contract decision -- but recorded, because a caller that learns one
  tool's scoping cannot carry it to the next.

Also worth keeping: every wrong argument in this window was refused with an actionable
message, including `Unknown parameter(s) for tool 'clash_manage_tests': 'action' (did you mean
'operation'?). The command was not executed.` Eight guesses, eight refusals, nothing silently
ignored and nothing half-applied.

### The second window: the write paths, run control and the creators

The first window measured 15 of the 29 and left 14, which an earlier revision of this
document miscounted as "20 of 29 measured, nine remaining". The corrected figures: the
first window covered 15, a second L3 window on the same day covered the remaining 13, and
one tool is unmeasurable from inside the product. **28 of 29.**

Same method: one throwaway matrix created from a 4-item selection, run to completion so the
result-bearing tools have something to read, then deleted. The document ended the window
with `totalTestCount: 0` and `list_selection_sets` empty, and was never saved.

Run control is only honest against an operation that is actually paused. `clash_run_batch`
with `batchSize=1` against four tests reports `state: "running"` immediately and reaches
`paused` only at the batch boundary, so the probe polls `clash_run_status` until it sees
`paused` before resuming. Measuring `clash_run_resume` against a finished operation would
measure its refusal.

| tool | `apply=false` | `apply=true` |
| --- | --- | --- |
| `clash_group_results` | 42 | 13 |
| `clash_group_by_proximity` | 47 | 16 |
| `clash_group_custom` | 14 | 11 |
| `clash_ungroup` | 16 | 12 |
| `clash_set_status` `scope=results` | 19 | 11 |
| `clash_renumber_results` | 33 (first window) | 50 |
| `clash_reset_isolation` | 15 | 10 |
| `clash_export_points` | 16 | 14 |
| `clash_tests_from_sets` (two real sets) | 29 | 18 |
| `clash_manage_tests` `operation=delete` | 22 | 14 |
| `clash_manage_tests` `operation=rename` | — | 14 |
| `clash_manage_tests` `operation=set_settings` | — | 22 |
| `clash_tests_export` | 13 (empty) / 34 | 72 |
| `clash_bbox_pair_plan` writing a plan file | 11 (empty) | 135 |
| `clash_pair_tests_create` from that plan | 114 | — |
| `clash_isolate_result` (no dry run) | — | 42 |
| `clash_run_resume` on a paused operation | — | 14 |
| `cancel_clash_run` on a paused operation | — | 7 |
| `cancel_clash_report` with no active report | — | 14 |

Nothing in the write half is slow either. Every `apply=true` path that touches only the
document lands between 7 and 72 ms; the two three-figure numbers are the plan-file pair,
`clash_bbox_pair_plan` at 135 ms with `apply=true` and `clash_pair_tests_create` at 114 ms
reading that plan back, and those two also write and read a file on disk. A dry run is not reliably
cheaper than the change it describes — `clash_group_results` costs 42 ms to plan and 13 ms
to apply — because the dry run does the same matching and then stops.

**`clash_batchtest_import` is the one that stays unmeasured**, and not for want of a window:
it requires a Navisworks-authored `nw-exchange-12.0` XML, while `clash_tests_export` writes
`navishelper_json`. There is no round trip inside the product that produces valid input for
it, so measuring it needs a file exported by Navisworks' own Clash Detective UI.

### What the second window found

- **`clash_tests_from_sets` needs selection sets to exist, and says so properly.** The
  reference model has none, so the first attempt planned zero tests. That is not a silent
  zero: the response carried `skippedTestCount: 1`, a per-pair `status: "failed"`, and
  `Selection Set/Search Set exact name was not found: …` in both `errorMessage` and
  `warnings`. Two sets were created from real leaf items to measure the tool properly, and
  removed afterwards.
- **`rootName` meant something other than what `list_root_items` returns.** A
  `SelectionSetReference` resolves `rootName` against `document.Models`, so on this model
  only `6501.5.nwd` works; `/STORE` and `/6501.5` — the two federated branches a caller
  actually wants — were refused with a bare `Model root/source file was not found.` The
  model reports `modelCount: 1` and `rootItemCount: 3`, so two of the three names a caller
  is handed cannot be used here. **Fixed in this branch**: the message now says that
  `rootName` names a model root rather than a tree root item below one, and lists the model
  roots that exist. The resolver's behaviour is unchanged — widening it to accept tree root
  items is a contract decision, not a message fix. Verified live on plugin
  `pluginAssemblyLength: 1588736`, written `2026-09-20T13:47:19Z`: `/STORE` and `/6501.5`
  now answer `rootName must name a model root, not a tree root item below one. Model roots
  in this document: '6501.5.nwd'.`, and `6501.5.nwd` still plans its test.

  An external Codex review then found that the first version of that message could itself
  name a root the resolver rejects — it fell back to a model's file name when the model had
  no `RootItem`, and `rootName` is compared against `RootItem.DisplayName`, so that name
  would have been refused in turn. The same fallback could also drop a model whose
  `SourceFileName` is blank but whose `FileName` is set, making the message claim the
  document has no model roots at all. The shipped version lists `rootName` candidates and
  `sourceFile`-only models separately. **Neither case exists on this model** — its single
  model has a root item — so the live string quoted above is what the shipped code produces
  here, and the two corrected branches are covered by construction rather than by
  measurement. They need a document with an unnamed model root to exercise.
- **`list_item_children` gives no per-child handle.** `ItemChildInfo` carries `path` but no
  `matchHandle`; the single `childrenMatchHandle` covers the whole returned page. Building a
  selection set from one named child therefore takes a second call (list *that* child's
  children, or search its path). Recorded, not changed.
- **Every wrong argument was refused, again.** `clash_manage_tests` rejected an extra
  `testHandle` with `did you mean 'testHandles'?`, and nothing was half-applied.

## The third window: everything the first two left

Measured in an agreed L3 window on 2026-09-20, on the plugin the host itself reported:
`pluginAssemblyLength` 1590272, written `2026-09-20T20:41:41Z`, sha256 `684ac7ff…`, built
from `main` at `86d243b`. The document started with **0 clash tests, 0 selection sets and
0 saved viewpoints** and ended the same way; it was never saved, and the close reported
`discardedUnsavedChanges: true`.

Everything created carried one prefix and was deleted by name. The scenario library was
redirected with `NAVISHELPER_SCENARIO_DIR` to a temporary directory, so the operator's own
two saved scenarios were not read, rewritten or counted — verified before and after.

| tool | case | ms |
| --- | --- | --- |
| `mcp_task_timer_start` | start | 14 |
| `mcp_task_timer_finish` | finish | 1 |
| `last_operation_status` | most recent operation | 75 |
| `isolate_selected` | apply=false / apply=true | 125 / 296 |
| `hide_unselected` | apply=false / apply=true | 21 / 72 |
| `unhide_selected` | apply=true | 25 |
| `reveal_selected` | apply=true | 26 |
| `capture_current_view` | png to a temp path | 27 |
| `start_subtree_names_dump` | `/STORE`, csv | 106 |
| `dump_subtree_names_status` | first poll, 5 000 items processed | 319 |
| `cancel_subtree_names_dump` | a genuinely running job | 8 |
| `isolate_by_box` | apply=false / apply=true | **2 953 / 5 761** |
| `save_scenario` | apply=true | 37, 60 |
| `get_scenario` | by id | 4 |
| `resolve_scenario` | with parameter values | 9 |
| `delete_scenario` | refused without the guard / applied with it | 0 / 12 |
| `select_selection_set` | apply=false / apply=true | 31 / 21 |
| `selection_sets_manage` | rename, apply=false / apply=true | 26 / 26 |
| `selection_sets_reorder` | apply=false / apply=true | 35 / 13 |
| `selection_sets_build_viewpoints` | one overview step, false / true | 53 / 19 |
| `activate_saved_viewpoint` | apply=false / apply=true | 19 / 22 |
| `saved_viewpoints_export` | json to a temp path | 26 |
| `saved_viewpoints_manage` | rename, apply=true | 33 |
| `saved_viewpoints_reorder` | apply=false / apply=true | 33 / 14 |
| `markup_selection` | one selection, fitToSelection | 73 |
| `live_markers` | apply=false / on / off | 29 / 15 / 15 |
| `section_box_viewpoint` | from the selection | 49 |
| `build_mtr_viewpoints` | createPlan=true, no section box | 28 |
| `saved_viewpoints_import` | every input available in-product | refused, see below |

### What the third window found

**`isolate_by_box` is the second slow tool, and bimodal like the first.** Five samples on
one unchanged build: **2 500, 2 953, 5 761, 7 560, 9 590 ms.** Same shape as
`find_items_by_bbox` and the same warning applies — treat a single number as an order of
magnitude, and do not accept a before/after pair without several samples per side. Nothing
else in this window exceeded 320 ms.

**An `_export` / `_import` pair here is not a round trip, and that is now two instances.**
`saved_viewpoints_export` writes csv, json or md — "use before bulk rename/reorder work so
duplicate names can be reviewed". `saved_viewpoints_import` reads *Navisworks-authored
Saved Viewpoints XML*. So the export cannot feed the import, which was confirmed three
ways: its own json, that json renamed `.xml`, and the default csv all fail with
`Failed to read saved viewpoints XML`. The other instance is `clash_tests_export`
(`navishelper_json`) against `clash_batchtest_import` (`nw-exchange-12.0`). Both pairs are
individually documented and correct; it is the naming symmetry that misleads. Recorded
here so the next reader does not spend the window discovering it a third time.

**A structured refusal arrives with `navishelper_timing.status: "ok"`.** `delete_scenario`
without `expectedSha256` answered `ok: false`, `applied: false`,
`errorCode: "scenario_conflict"` in the payload — while the timing envelope said `ok`, and
a harness that trusted the envelope recorded it as a successful 0 ms call. That is how this
window nearly reported a refusal as a measurement. Schema violations and command failures
*do* set the envelope to `error`, so the split is between transport-level failure and a
structured domain refusal. Not changed here, because the mapping is shared by every tool
and narrowing it is a contract decision; but any client checking only the envelope will
read some refusals as successes.

**The scenario guard blamed a change that had not happened.** Missing `expectedSha256` and
a *stale* one produced one message: "Сценарий изменился после чтения" — and for delete, no
mention of `expectedSha256` at all. Nothing had changed; the caller had simply not supplied
the guard, and the advice to re-read and retry cannot fix that. **Fixed in this branch** at
both sites, save and delete: a missing guard now names the parameter and where to get it
(`get_scenario` and `list_scenarios` both return `sha256`), and a stale one keeps the
original wording. Covered by a test that was proved to bite — reverting the delete site
alone fails exactly one assertion.

## What still has no number

Twelve tools, and the reason for each, so the gap is a decision rather than an oversight:

| tool | why |
| --- | --- |
| `save_document`, `save_document_as` | never run on purpose. Every window depends on the document not being saved. |
| `clash_batchtest_import` | needs a Navisworks-authored `nw-exchange-12.0` XML; no tool in the product writes one. |
| `saved_viewpoints_import` | needs Navisworks-authored Saved Viewpoints XML, for the same reason. Its refusal path was measured; the import path was not. |
| `create_selection_set`, `create_viewpoint` | exercised as scaffolding in the third window — the sets and viewpoints the other tools needed — but their own timings were not recorded, so they are not claimed here. |
| `create_search_set` | not exercised; it needs a search condition rather than handles. |
| `model_color_scheme`, `selection_color_by_property` | write display overrides across the model; they need a window of their own with a stated restore. |
| `selection_export_properties` | writes a report file; harmless, simply not reached. |
| `dump_subtree_names` | the synchronous variant. Its asynchronous trio was measured instead, which is the form the contract recommends for a subtree this size. |
| `open_latest_navisworks_file` | a lifecycle tool measured only indirectly, through the launch figure. |

The first seven of those are one short window away. `save_document*` and the two import
tools are not, and saying which is which matters more than the count.

## Re-running it comparably

A number here is only comparable to a number taken the same way:

- same model, and stated — `6501.5.nwd` is federated and its RVM branch has empty
  internal property names, which changes what the native search can match;
- **a fresh Navisworks process**, because an abandoned traversal leaves hundreds of
  thousands of `ModelItem` wrappers reachable and makes every later search slower;
- the plugin identified by the host's own startup record, not by inspecting the bundle
  afterwards;
- host-side `elapsed_ms`, not wall clock;
- first and warm reported separately.
