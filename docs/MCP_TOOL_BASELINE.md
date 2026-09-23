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
| tools covered | **100 of 104** advertised tools carry a measured number. The denominator and the gap list are checked in CI by `scripts/check_baseline_coverage.py` against the tool list discovered from source and against this section's own arithmetic, so landing a tool without updating this row fails the build rather than leaving a stale claim. They were measured across six windows — 35 in the read-only pass below, 28 clash tools across two L3 windows, 28 more in a third, 15 cases covering 7 tools in a fourth, the synchronous `dump_subtree_names` in a fifth, and `start_navisworks` / `close_navisworks` stated in prose rather than tabulated. (`delete_scenario` was listed here as prose-only too, wrongly -- it has a row of its own under the scenario library.) Those parts sum to more than 100 because some tools were measured in more than one window; the figure above counts distinct tools, which is why it is not their total. The remaining **4** are named in [What still has no number](#what-still-has-no-number), with the reason for each. |

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

## The fourth window: the eight that were one window away

Measured in an agreed L3 window on **2026-09-22**, Navisworks Manage 2027, on
`D:\Downloads\6501.5.nwd` — the same model as every other window here. The plugin the host
reported: `pluginAssemblyLength` **1593344**, written `2026-09-21T22:14:29Z`,
`pluginVersion` 2.9.0.0, built from `main` at `6ebd168`. `check_installed_bundle_drift.py`
passed before the window and again after it, so the numbers belong to that build.

The selection-scoped tools ran against **everything under `/STORE`: 5376 items**, selected
by `find_items` with `scope=under_handle`. The document was never saved and the close
reported `mode=discard`, so nothing below survived the window.

| tool | case | first | warm |
| --- | --- | --- | --- |
| `open_latest_navisworks_file` | opened `6501.5.nwd`, `outcome=host_ready` | **18 526** | — |
| `create_selection_set` | `apply=false` | 139 | 155 |
| `create_selection_set` | `apply=true`, 5376 items | 396 | 509 |
| `create_search_set` | `apply=false` | 145 | 107 |
| `create_search_set` | `apply=true` | 120 | 110 |
| `create_viewpoint` | `apply=false` | 16 | 10 |
| `create_viewpoint` | `apply=true` | 13 | 14 |
| `selection_export_properties` | `apply=false` | 84 | 101 |
| `selection_export_properties` | `apply=true`, wrote 1 096 615 bytes | 146 | 70 |
| `model_color_scheme` | `operation=analyze`, `scope=selection` | **3 069** | **2 879** |
| `model_color_scheme` | `operation=apply`, `apply=false` | **1 203** | **1 343** |
| `model_color_scheme` | `operation=apply`, `apply=true`, 5376 items | **1 925** | — |
| `model_color_scheme` | `operation=reset`, `apply=true` | 96 | — |
| `selection_color_by_property` | `apply=false`, `itemLimit=100` (default) | 151 | 205 |
| `selection_color_by_property` | `apply=false`, `itemLimit=5000` | 648 | 720 |
| `selection_color_by_property` | `apply=true`, `itemLimit=100`, coloured 44 | 510 | — |

Every `apply=true` was checked for an **effect**, not just a return: both sets appear in
`list_selection_sets`, the viewpoint in `list_saved_viewpoints`, the export file is
1 096 615 bytes on disk, and the colour calls reported `applied: true` with a non-zero
`coloredItemCount`. A fast number from a call that did nothing is the trap this document
records for `delete_scenario`, and it is worth re-checking each time rather than trusting
the envelope.

**`model_color_scheme` is the third slow tool.** Analyze reads up to
`maxPropertiesPerItem` properties per eligible item and 3 s is the honest cost of that on
5376 items; the dry-run is cheaper because it classifies rather than surveys. Nothing here
is pathological, and among the tools that act on an already-open document it is the only
one in this window above a second.

`open_latest_navisworks_file` is higher still at 18 526 ms, and is not a counter-example to
that: it starts a Navisworks process and loads a 39 MB federated model, so it belongs with
`start_navisworks` rather than with the tools above. Read it as the cost of a window, not
of a call — which is the same reason the attach path exists and is worth its round trip.

### `selection_color_by_property` scales with `itemLimit`, not with the selection

151 ms at the default `itemLimit=100` and 648 ms at 5000, against the same 5376-item
selection — 44 groups against 654. The default inspects 100 items and sets
`itemsTruncated: true`, so **the default is a sample, not the selection**. A caller reading
`selectedItemCount: 5376` beside `coloredItemCount: 44` and no raised limit is reading a
partial answer.

### Two traps this window walked into

Both cost a measurement and are recorded so the next person does not repeat them:

- **`model_color_scheme` clears the selection.** `clearSelectionAfterApply` defaults to
  `true`, so any selection-scoped tool measured *after* an `apply=true` measures an empty
  selection. The first `selection_color_by_property` numbers taken here were 11–25 ms of
  nothing.
- **A category filter that matches no property returns quickly and truthfully.** With
  `categoryFilters: ["Item"]` the tool reported `matchedItemCount: 0` and
  `status: "ok"` — correct, and useless as a timing. This model's RVM branch carries its
  properties under Cyrillic category names plus an `AVEVA` category; `AVEVA`/`Ref` is what
  the numbers above used.

### `dump_subtree_names`: the synchronous variant cannot be measured on this model

All three roots refuse. The limit is **5000 items** (`MaxSynchronousDumpItems`, with a 25 s
companion budget), and `/STORE`, `/6501.5` and `6501.5.nwd` each exceed it:

| root | ms | outcome |
| --- | --- | --- |
| `/STORE` | 586 | `status: "error"` — *Synchronous dump limit exceeded* |
| `/6501.5` | 504 | same |
| `6501.5.nwd` | 600 | same |

The refusal is not free — it walks to the limit before giving up — and it arrives as a
transport-level `error`, so a caller cannot mistake it for a dump. Measuring the success
path needs a model with a root under 5000 items, which is a different model rather than
another window, so this tool stays in [What still has no number](#what-still-has-no-number)
with that reason.

### `find_items_by_bbox` pruning, observed live

The prune added in #47 measured on the same document. `6501.5.nwd` holds **one** appended
model, so pruning here is all-or-nothing; a query that cannot match now costs milliseconds
instead of the full budget:

| query | scanned | matched | pruned | ms |
| --- | --- | --- | --- | --- |
| a zone covering everything | 70 574 | 64 261 | 0 | 10 135 (truncated at the budget) |
| a zone far outside the model | **0** | 0 | **1** | **32** |
| `sourceFileContains` matching nothing | **0** | 0 | **1** | **19** |
| `sourceFileContains` naming the real file | 43 666 | 40 073 | 0 | 10 019 (truncated) |

**19–32 ms against roughly 10 000 ms**, and the scan count is zero rather than at the cap.
Before the prune both of those queries walked the model to the ten-second budget to return
nothing.

One limit, stated rather than implied: this document has **one** `Model`, so the window
shows the prune firing and not partial pruning across several appended files — that needs
an NWF with several.

The equality check the prune's correctness rests on — that pruning never loses a match —
**was run**, later the same day and on `6513.nwd`, because that model's walk completes
where this one runs out of budget and a truncated zero would prove nothing. Nine queries
against a build with pruning removed returned identical match counts, including six slabs
placed immediately outside the extents the prune itself reported, each of which walked all
41 016 items and found nothing. The full table is in
`docs/MCP_TOOL_CONTRACTS.md` under *find_items_by_bbox*.

## The fifth window: a federated model

Measured in an agreed L3 window on **2026-09-23**. The plugin the hosts reported was
`pluginVersion` 2.10.0.0, `pluginAssemblyLength` **1593344**, written
`2026-09-22T09:29:04Z` for 2027 and `09:29:01Z` for 2026. The installed bundle had drifted
by rebuild noise only (`NavisHelper.dll` matched), so it was backed up, reinstalled from
`main` at `af403f9`, and `check_installed_bundle_drift.py` passed for all four year folders
before the first call.

**The MCP server was not current, and the drift check could not see it.** `mcp_diagnostics`
reported `mcpServerVersion` **2.9.0.0**, and `mcp_health_check` answered
`verdict: "degraded"` with "MCP server version (2.9.0.0) differs from NavisHelper plugin
version (2.10.0.0)". The server runs from its own install under
`%LOCALAPPDATA%\NavisHelper\McpServer`, which `install_local_bundle.ps1` does not touch and
`check_installed_bundle_drift.py` does not read. So every number below measures the 2.10
plugin, and anything the MCP server adds was 2.9's. That is why `prunedModelCount`, added
to the response in 2.10, is absent below, while the plugin's own warning still printed the
count. The product's own handshake caught the mismatch. The pre-window guard did not.

The model was `D:\Yandex.Disk\Model_PORT\Порт_Бухта-Север.nwf`: **438** appended RVM
models and 872 root items, 5 GB working set. Nothing was changed in either document, and
both closes reported `documentWasModified: false`.

### `dump_subtree_names`: the synchronous success path

The fourth window could only measure the refusal, because every root of `6501.5.nwd` is
over the 5000-item limit. This federation has small roots:

| tool | root | items | ms | outcome |
| --- | --- | --- | --- | --- |
| `dump_subtree_names` | `/240000-ЛТ3` | 2 465 | **1 052** | CSV, 595 206 bytes |
| `dump_subtree_names` | `/240000-ОС2` | 45 | 209 | CSV, 7 828 bytes |
| `dump_subtree_names` | `240000-СЭО-01.rvm` | 1 | 210 | CSV, 119 bytes |
| `dump_subtree_names` | `/240000-ГТМ1` | over 5000 | 1 079 | refused, *Synchronous dump limit exceeded* |
| `dump_subtree_names` | `/240000-НВК1` | over 5000 | 819 | refused, same |

Every file was read back afterwards. Its data rows equal the reported `itemCount` (2 465,
45, 1), and its size on disk equals `fileSizeBytes`. A root named twice (`240000-ГТМ1.rvm`
matches both the file node and `/240000-ГТМ1`) is refused with both matches listed, which
is the right answer.

### `find_items_by_bbox`: partial pruning across several models

The fourth window could show pruning fire only all-or-nothing, on one model. Here, with 438
models, a zone at the west end of the site (x −2700…−2600, y −530…−430, z 0…60) pruned
most of them and scanned the rest, and the walk **completed**. `isolate_by_box` on the same
box, for comparison:

| tool | scanned | intersecting | ms |
| --- | --- | --- | --- |
| `find_items_by_bbox`, `includeContainers=true` | 152 200 | **45 760** | 8 643 |
| `isolate_by_box`, `apply=false`, same box | 56 563 | **45 760** | 7 039 |

**The counts are equal.** Here is what that shows, and what it does not.

`isolate_by_box` reaches its answer by a different walk: hierarchical, pruning subtrees by
parent bounds, classifying with a separating-axis test. The equality therefore shows that
the multi-model part of the prune agrees with a walk that never skips a model as a whole.
That part is the model-by-model decision, and the scan of every model the prune keeps.

What it does **not** re-test is the assumption both walks share. Both start from the same
model roots and trust each root's `BoundingBox()` to enclose every descendant. If a root's
box excluded an intersecting descendant, both would miss it and still agree. That
assumption was tested once, on `6513.nwd`, against a build with pruning removed (see the
fourth window). Testing it on this federation needs the same kind of build.

The same table carries the clearest speed opportunity in this document.
`find_items_by_bbox` scanned **2.7×** more items than `isolate_by_box` for the same answer,
because inside a model that is not pruned it still reads every item. Its own warning says
so: "narrowing the zone does NOT help inside a model". Zones in the middle of this site
stopped at the 10-second budget: 337 290 and 428 668 items scanned, 20 and 45 models
pruned. Parent-bounds pruning of the kind `isolate_by_box` already does would let them
finish. The equality above is part of an acceptance for that change, and a build with
pruning removed is the other part.

### `isolate_by_box`: identical work, 8 to 25 seconds

`6501.5.nwd`, one unchanged box (centre 3134, 1760.5, 118; half-extents 20, 20, 10),
`apply=false`, ten calls. Every call returned identical counts: 59 253 scanned, 30 332
intersecting, 2 590 pruned subtree roots.

| samples | ms |
| --- | --- |
| while two executors were building and testing on the same machine | 10 934, 10 426, 18 577, 18 176 |
| after both had finished | 25 228, 24 401, 12 851, 12 934, 8 098, 9 113 |

What these samples support, and what they do not:

- **The quiet group was not faster in this sample.** That does not show executor
  contention played no part. The two conditions were not interleaved, and a spread this wide
  can hide a contention penalty. Settling it needs interleaved samples, or load telemetry
  recorded alongside.
- **For the last three calls, the Navisworks process's CPU time grew by about as much as the
  wall time**: 12.98, 8.16 and 9.11 s against 12 934, 8 098 and 9 113 ms. That is
  consistent with the host computing rather than blocking. But process CPU time sums every
  thread, so it does not prove the UI thread never waited: a GC thread could burn CPU while
  it did. And three calls between 8 and 13 s say nothing about the 18–25 s ones.
- The machine: AMD Ryzen 9 5900HX, which is not a hybrid part. It was on mains power, with
  21.8 GB of 60 GB RAM free and commit at 65.8 of 70.8 GB.

The next step is instrumentation rather than another sample: per-phase timings and GC
collection counts in the response, or per-thread CPU time.

### Runtime smoke on other versions

| version | start | health checks | `find_items` countOnly | `find_items_by_bbox` |
| --- | --- | --- | --- | --- |
| 2026 | 18 137 | 3 of 3 ok | 352 | 1 439 |
| 2027 | 33 603 | 2 of 2 ok | — | used throughout this window |

Both answered `verdict: "degraded"` only because of the MCP server version above. **2024
and 2025 are not installed on this machine.** Their folders under `Program Files` hold 8
files and no `Roamer.exe`, and `start_navisworks` refused with "Navisworks Manage 2025 was
not found". Their runtime smoke needs another machine.

## The sixth window: subtree pruning in `find_items_by_bbox`, two builds

2026-09-23, Navisworks Manage 2027. Two builds of the same branch, installed in turn:

- **base**: `main` at `b2bcaa4`, `NavisHelper.dll` sha256 `e323f112…`, 1 593 344 bytes;
- **head**: `1fe74af`, which skips any subtree whose own box misses the zone, `05920a6d…`,
  1 593 856 bytes.

Each build was measured through an MCP server built from its own worktree, so the plugin
and the server in front of it came from one commit, and the new `outsideItemCount`
reached the client. The installed bundle was snapshotted first, and restored afterwards
byte for byte (46 files).

Every query used `maxScannedItems=500000`. A cell is "truncated" when every run hit the
10-second budget:

| query | base matched / scanned | head matched / scanned | outside items, not descended | base ms | head ms |
| --- | --- | --- | --- | --- | --- |
| `6513.nwd`, whole model, leaves | 26762 / 41016 | 26762 / 41016 | 0 | 3323, 4340 | 3213, 4022 |
| `6513.nwd`, whole model, containers | 41016 / 41016 | 41016 / 41016 | 0 | 7715, 9598 | 7879, 10001 |
| `6513.nwd`, south-west quadrant | 110 / 41016 | 110 / **394** | 147 | 8293, 1564 | **394, 394** |
| `6513.nwd`, north-east quadrant | 26652 / 41016 | 26652 / 41016 | 110 | 3614 | 3108, 4198, 7805 |
| `6513.nwd`, 100 m column | 0 / 41016 | 0 / **394** | 257 | 7273, 2406 | **251, 234** |
| `6513.nwd`, 10 m cube | 0 / 41016 | 0 / **394** | 257 | 1247, 1764 | **230, 233** |
| `6513.nwd`, 5 m floor slab | 110 / 41016 | 110 / **394** | 147 | 797, 737 | **28, 30** |
| `6513.nwd`, outside the extents | 0 / 0 | 0 / 0 | 0 | 13, 15 | 14, 13 |
| NWF, west zone, containers | 45760 / 152200 | 45760 / **56137** | 10377 | 9347, 9279 | 6654 |
| NWF, west zone, leaves | 39062 / 152200 | 39062 / **56137** | 10377 | 9927 | 6417, 9044 |
| NWF, middle zone, containers | truncated | **2273** / 55381 | 53108 | 10013, 9273 | 7743, 2164 |
| NWF, middle zone, leaves | truncated | **1610** / 55381 | 53108 | 9478, 9898 | 2564, 2483 |
| NWF, raised zone, containers | truncated | **376** / 49144 | 48768 | 10016, 10014 | 2507, 2385 |

**Where both builds completed, every match count is equal**, ten queries of ten. The head
never found a different answer. It found the same answer after walking less — the
outside-items column counts items whose box missed the zone, leaves included, and none
of their descendants were walked — or a complete answer where the base had run out of
budget.

For the three zones the base could not finish, `isolate_by_box` on the same boxes gave
2 273 and 376 intersecting items, equal to the head. That oracle starts from the same model
roots, so it shares the assumption that an item's box encloses its children, as the fifth
window records. The two-build equality above is the part that does not share it: the base
walks every item of every kept model and checks no parent box at all.

**The trade-off.** The head reads every container's box, which the base skipped when
`includeContainers=false`. On a zone that prunes nothing, the whole-model rows, the time
is within the run-to-run spread in both directions (3.2–4.0 s against 3.3–4.3 s). The
cost is real in principle and not visible at this resolution.

### Throughput falls call after call inside one Navisworks process

Consecutive identical calls in one process got slower, with nothing else changing. On the
head, `6513.nwd`, the whole-model leaf query scanned 40 452, 23 598, 10 665 and then 9 566
items inside its 10-second budget, four calls in a row. After a restart, the same process
state gave the north-east quadrant in 3 108, 4 198 and 7 805 ms. The process held 0.8 GB
private memory and 2 547 handles at the slow end, so this is no obvious leak.

This is the most likely source of the bimodal timings every window has recorded,
`isolate_by_box` in the fifth. They were drawn from long runs of calls in one process, and
a call's number was partly its position in that run. Until this is understood, **compare
timings only between fresh processes, first call against first call**.

**Retained results are not the only cause.** Same day, same base build (`e323f112…`),
`6513.nwd`, the whole-model leaf query, five calls back to back in a fresh process per
arm. The arms differed only in how many results each call kept in `MatchSessionStore`:

| arm | ms, calls 1 to 5 | every call |
| --- | --- | --- |
| `maxResults=10000`: up to 10 000 items kept per call | 3 397, 4 132, 7 855, 7 413, 8 310 | 41 016 scanned, 26 762 matched |
| `maxResults=1`: one item kept per call | 1 248, 2 035, 2 622, 3 486, 4 341 | the same |

Both arms slow down call after call, the second about 0.8 s per call with almost nothing
retained. So retention is not *required* for the slowdown. Whether it adds to it is not
settled: the arm that kept 10 000 items grew by 4.9 s from its first call to its fifth, the
one that kept one item by 3.1 s, and one run of each cannot separate that difference from
noise or from the larger response it also builds. The same table also carries a separate
cost: returning 10 000 results, with their paths, source files and sort, took about 2 s
of the first call (3.4 s against 1.2 s).

An idle pause helps only in part. The fifth call of the second arm took 4.3 s. After a
short gap the sixth took 2.5 s, after a further 90 s idle the seventh took 2.6 s, and the
eighth, straight after it, took 4.1 s.

**A plausible mechanism, tested and refuted for the boxes.** In the Navisworks 2027 API
`ModelItem` and `BoundingBox3D` derive from `NativeHandle`, which implements `IDisposable`
and declares a finalizer; this was read from the assembly by reflection. A walk creates
tens of thousands of each and disposed none, so finalizer pressure fitted all three
observations. It was tested the same evening with a build that disposes every
`BoundingBox3D` it reads (`893d5ab9…`), against `main` at `5204c10` (`1eee7eda…`). The two
builds were interleaved over two rounds, each arm in a fresh process, with five identical
whole-model calls in a row:

| `maxResults` | round | build | ms, calls 1 to 5 |
| --- | --- | --- | --- |
| 1 | 1 | base | 2448, 1277, 2459, 2756, 2968 |
| 1 | 1 | disposes boxes | 2496, 1252, 2481, 2756, 3225 |
| 1 | 2 | base | 2520, 1250, 2521, 2741, 2938 |
| 1 | 2 | disposes boxes | 2474, 1277, 2425, 2720, 3028 |
| 10000 | 1 | base | 3144, 3970, 6613, 6644, 8579 |
| 10000 | 1 | disposes boxes | 8590, 10038, 10023, 10032, 10029 |
| 10000 | 2 | base | 8584, 2960, 7200, 10022, 10021 |
| 10000 | 2 | disposes boxes | 3134, 3921, 6645, 6819, 8448 |

With one result kept, the curves of the two builds match within about 0.3 s in both rounds.
Disposing the boxes does not change the slowdown, so they are not its cause, and that
change was not merged. With 10 000 kept, base and head swap places between rounds, so the
build has no visible effect there either. Even a first call in a fresh process ranged from
3.1 to 8.6 s, which is noise on this machine on top of the cost of building a large
response.

Still untested: the `ModelItem` wrappers themselves, which the walk also creates and never
disposes. Disposing those is not safe without knowing whether Navisworks hands the same
wrapper to other holders, and it needs in-process evidence first: GC collection counts, or
per-thread CPU, across a run of calls.

## What still has no number

Four tools, and the reason for each, so the gap is a decision rather than an oversight:

| tool | why |
| --- | --- |
| `save_document`, `save_document_as` | never run on purpose. Every window depends on the document not being saved. |
| `clash_batchtest_import` | needs a Navisworks-authored `nw-exchange-12.0` XML; no tool in the product writes one. |
| `saved_viewpoints_import` | needs Navisworks-authored Saved Viewpoints XML, for the same reason. Its refusal path was measured; the import path was not. |

**4** of those are not reachable in a window at all, and saying which is which matters
more than the count:

- **out of reach** — `save_document` and `save_document_as`, because every window depends on
  the document not being saved; `clash_batchtest_import` and `saved_viewpoints_import`,
  because each needs a Navisworks-authored XML that no tool in the product writes.
- **one short window away** — the remaining **0**: none. The eight that were one window away
  were measured on 2026-09-22; see [The fourth window](#the-fourth-window-the-eight-that-were-one-window-away).

Listed by name rather than by position in the table above, because a count of rows is
wrong as soon as a row moves.

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
