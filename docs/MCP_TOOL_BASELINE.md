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
| tools covered | **35 of 104** advertised tools |

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
  owner's document or its saved items.
- **The clash surface** — 29 tools, of which only `clash_bbox_pair_plan` (a dry-run
  plan) is measured. Runs, imports and matrix creation mutate clash tests and take
  minutes; they need their own window.
- **Long-running jobs** — `dump_subtree_names` and its status/cancel pair, which write a
  file and are designed to be polled.
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

Measured in an agreed L3 window on 2026-09-20, plugin `20bb4356…`. These 29 tools are not
reachable from a session started with a narrowed tool profile, so they were driven through a
server built with the default `all` profile.

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
| `clash_manage_tests` `operation=delete` `apply=true` | — | 4 tests deleted |

Nothing here is slow. `clash_run_status` at about a second is the run itself finishing, not
overhead. Every write-capable clash tool defaults to `apply=false`, so the call an agent
makes first is a dry run, and the dry runs cost between 30 and 99 ms.

**Not measured, and why.** The `apply=true` paths of `clash_group_by_proximity`,
`clash_group_custom`, `clash_ungroup`, `clash_set_status`, `clash_renumber_results`,
`clash_generate_report`, `clash_save_viewpoints` and `clash_export_points` all write into the
document or onto disk; `clash_batchtest_import`, `clash_tests_from_sets` and
`clash_pair_tests_create` create tests from external input; `clash_isolate_result` and
`clash_reset_isolation` change visibility; `cancel_clash_run`, `cancel_clash_report` and
`clash_run_resume` only make sense mid-operation. Nine of 29 tools remain unmeasured.

### What the window actually found

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
