# Clash Zones Plan

Date: 2026-10-14. Status (2026-10-15): PRs 1–7 merged (#320–#330); the tools are
`clash_zones_export` and `clash_zones_markers`, used as in the client guide's "Clash
zones" section. L3 of the two tools is open (#326, #328). Evidence for the design is the code, the SDK
XML and reference assemblies, the installed loaders, an offline file-size spike and one
L3 format check in Navisworks 2027 (section 3). Claims still marked **L3** are open.

## Goal

Turn Clash Detective results into something a coordinator can see in the model and
track across model iterations:

1. read the clash results of an NWD (optionally re-running its tests first);
2. aggregate the raw clash points into **zones**;
3. write an **external marker file** with one sphere per zone (or per clash point),
   append it to the open document and make it semi-transparent;
4. write an **Excel workbook per run**;
5. **compare two runs** and say which zones disappeared, which are new, and which
   moved, shrank or grew.

Terms:

- **raw clash** — one `ClashResult`.
- **zone** — a set of raw clashes merged by model-tree association and distance, drawn
  as one bounding sphere. A zone is a clash cluster from
  [CLASH_CLUSTERING_PLAN.md](CLASH_CLUSTERING_PLAN.md) plus a sphere and an identity
  that survives into the next run.
- **run** — one extraction from one NWD iteration: a folder with the workbook and the
  marker file.
- **track** — the identity of one physical problem location across runs (`trackId`).

## 1. What exists

Paths relative to the repository root, as of `main` 9ed9a1c (113 tools).

| Piece | Where | Verdict |
| --- | --- | --- |
| Run tests from the plugin | `clash_manage_tests operation=run`; `clash_run_batch`/`_status`/`_resume`, `cancel_clash_run` (`NavisworksClashSetTools.cs`) | Reuse as-is. |
| Result traversal | `EnumerateClashReportWorkItems`, `DocumentCommandService.Clash.ReportDataSupport.cs` | Reuse; reads `Center`, `Item1/2`, `Selection1/2`, `Status`, `Distance` (mm). **Does not read `ClashResult.Guid`.** |
| Clustering | `ClashClusterConstructionService.cs`: 3D grid with cell = distance, 27 neighbours, union-find (single linkage); `hybrid` = pair key then spatial split | Reuse the algorithm, but it uses Navisworks `Point3D`, lives in the plugin and **has no unit tests**. |
| Association key | `ReportDataSupport.cs` `FindAssociationNode`: nearest parent with a usable name, then key `id:`InstanceGuid → `path:` → `source:` (weak) → `leaf:` (weak); sides sorted, so the pair key is symmetric | Reuse. Only this one mode exists; `fixed_depth` is in `clash_group_results` (`ancestorLevelsUp`), root/source-file grouping via `ModelItem.Model`. |
| Read-only zones preview | `clash_list_clusters` (default 300 mm) | Reuse. `centroid` is the mean of the points in document units; `boundingBox` is the union of the **item** boxes, not of the points. |
| Clusters written as Clash Detective groups | `clash_group_by_proximity` (groups named `Зона N…`, default 500 mm); `clash_group_results` (by side, `ancestor`/`root`/`source_file`) | Reuse as an optional side output; not the zone identity. |
| `clusterId` | hash of mode, keys, distance, count, rounded centroid and positional result handles | Deterministic for an identical run, **not stable across runs**. |
| Cross-run result matching | `ClashRunPreservationService.cs`: item-identity pair (InstanceGuid) plus nearest point within 10 mm | Private, used to keep review state through a re-run. The idea carries over to zone matching. |
| Point export | `clash_export_points` → `ClashPointExportWriter.cs`: CSV or XLSX (`Коллизии`, `По этажам`, `Сетка x этаж`) | Extend or supersede: coordinates are raw document units, items are first display name only, no GUID/path/distance, every cell a string. |
| XLSX writing | two hand-written OpenXML zips (`ClashPointExportWriter.cs`, `DocumentCommandService.PropertyReport.cs`), no library | Extract one multi-sheet writer with numeric cells instead of a third copy. |
| Markers | `world_markers_*` (`WorldMarkerOverlayRenderer`, 2D overlay, not saved, cap 500 in `WorldMarkerOverlayPlanner.MaxStoredMarkers`); `live_markers`; `markup_selection` (redlines in viewpoints) | Not suitable for zones: 2D, transient or per-viewpoint, capped. `world_markers_set` is still the right tool to check a sphere's position at L3. |
| Colour/transparency | permanent overrides in `ClashPreviewManager`, `DocumentCommandService.Color.cs`, `ModelColorSchemeService.cs` | Reuse for the appended marker model. |
| Append/remove a file | — | Not present anywhere. |
| Geometry file writer | — | Not present (no STL/OBJ/STEP/IFC/FBX/DWF/NWC). |
| Run comparison | — | Not present. |
| Saved scenario for tree/proximity grouping | — | Not present. The local scenario library holds a clash-matrix scenario and a box-isolation replay; the `review_clashes` prompt uses `clash_list_clusters`. The owner's recollection matches the **tools** above, not a scenario. |

Pure, testable code goes to `NavisHelper.Contracts` (netstandard2.0, tested from
`NavisHelper.McpServer.Tests`), the way `WorldMarkerOverlayPlanner` does. The clustering
and export code to reuse sits in `DocumentCommandService` partials, already at the
limit of 32 in `scripts/navishelper_partial_limits.txt`; zone code gets its own types
and calls extracted helpers.

## 2. Navisworks API feasibility

All members below were checked against the SDK reference assemblies for 2024 and
2025 and the shipped `Autodesk.Navisworks.Api.xml` for 2026 and 2027.

| Need | API | Notes |
| --- | --- | --- |
| Tests and results | `DocumentClash.TestsData.Tests`, `ClashTest.Children` (`ClashResult` / `ClashResultGroup`, recursive) | Manage only; already wrapped by `ClashWorkflowService`. |
| Clash point | `ClashResult.Center` (`Point3D`) | Document units, world space. Convert with the existing `DocUnitsToMm`; today's point export and cluster centroid do not. |
| Penetration, status, identity | `Distance`, `Status`, `Guid`, `CreatedTime`, `ApprovedTime`, `BoundingBox` | `Guid` is expected to survive a re-run of the same test in the same document and not a re-import into a new NWD (**L3**). The matching rule in section 5 does not depend on it. |
| Clashing items | `Item1`/`Item2`, `CompositeItem1`/`CompositeItem2` | The composite items honour the test's composite-object setting; either may be null for stale results. |
| Ancestry | `ModelItem.Parent`, `Ancestors`, `ModelItem.Model` | Display-name paths are not identities ([navisworks-api-notes.md](research/navisworks-api-notes.md)). |
| Run tests | `DocumentClashTests.TestsRunTest`, `TestsRunAllTests` | Already exposed: `clash_manage_tests operation=run`, `clash_run_batch` with status/resume/cancel. |
| Append a file | `Document.AppendFile` / `TryAppendFile` / `AppendFiles` | Present in all four versions. Throws `DocumentFileException` on reader failure. |
| Remove it again | `Document.RemoveFile(int)` / `TryRemoveFile(int)` | Present in all four versions. The index is the position in `Document.Models`; find it by `Model.FileName`/`SourceFileName`. **L3**: removal when the host document was opened as an NWD. |
| Units / placement fix-up | `Model.Units`, `Model.Transform`, `DocumentModels.SetModelUnitsAndTransform` | Lets the tool correct a reader that guessed the wrong units, without re-writing the file. |
| Semi-transparent, coloured | `DocumentModels.OverridePermanentTransparency`, `OverridePermanentColor` on `model.RootItem` | Applies to all geometry descendants; saved with the document. The colour can also be baked into the file (section 3). |
| Live preview without a file | `RenderPlugin` + `Graphics.Sphere`, `Graphics.Color` with alpha | Not saved, visible only where the plugin runs. Useful as a preview, not as the deliverable. |

What the API cannot do: create model geometry inside the document. The marker file is
therefore not a workaround to be retired later; it is the only persistent path.

Saving: appending turns the open NWD into a multi-model document. Saving as NWF keeps
a reference to the marker file (refresh after it is re-written); saving as NWD embeds
the markers. The tools never save; the coordinator chooses when saving (section 7).

## 3. Marker file format

### Readers that ship with Navisworks

Read from the installed loaders (`lcld*.dll`) on this machine. Only 2026 and 2027 are
full installs here; 2024 and 2025 are SDK reference assemblies, so their reader list is
**unverified** and must be checked before a format is promised for them.

| Format | 2026 | 2027 | Names per object | Properties per object | Units in file | Sphere |
| --- | --- | --- | --- | --- | --- | --- |
| IFC (`lcldifc`) | yes | yes | yes | yes (property sets) | yes | mesh, or mapped unit mesh |
| STEP (`lcldstep`) | yes | yes | yes | no | yes | exact `SPHERICAL_SURFACE`, tessellated on load |
| OBJ (`lcldobj`) | yes | yes | yes (`o`/`g`) | no | no | mesh |
| STL (`lcldstl`) | yes | yes | no, one node | no | no | mesh |
| FBX (`lcldfbx`) | yes | yes | yes | limited | yes | mesh; binary writer needs the FBX SDK |
| RVM (`lcldrvm`) | yes | yes | yes | yes, via `.att` | yes | native sphere primitive; undocumented binary |
| VRML (`lcldvrml`) | yes | **no** | yes | no | no | native `Sphere` node |
| DWF/DWFx (`lclddwf`) | yes | yes | yes | yes | yes | mesh; heavy container format |
| NWC via NWcreate | yes | yes | yes | yes | yes | needs the C++ NWcreate SDK in the build matrix |

VRML was dropped in 2027, which rules it out despite its native sphere.

### Size spike

A throwaway generator (not in the repo) wrote N spheres on a 200 × 200 × 40 m site in
each candidate format; its output files are the input for the L3 check below. Write times are Python; a C# writer
will be faster. Load time in Navisworks is **L3** and not measured.

| N | STL, 80 tri | STL, 320 tri | OBJ, 320 tri | IFC4 mapped, 320 tri | STEP exact |
| --- | --- | --- | --- | --- | --- |
| 100 | 391 KB | 1.5 MB | 0.98 MB | 87 KB | 39 KB |
| 1 000 | 3.9 MB | 15.6 MB | 10.8 MB | 0.8 MB | 0.4 MB |
| 10 000 | 39 MB | 156 MB | 117 MB | 8.3 MB | 4.2 MB |

Per-sphere meshes grow linearly with triangle count; IFC with one shared unit sphere
(`IfcRepresentationMap` + scaled `IfcMappedItem`) and STEP with analytic spheres stay
small because each zone costs a fixed handful of entities. 10 000 spheres × 320
triangles is 3.2 M triangles at render time whatever the file format — acceptable for
Navisworks, but per-point mode should use the 80-triangle sphere.

### Recommendation

**IFC4, one mapped unit sphere, one `IfcBuildingElementProxy` per zone.**

- Each zone becomes a named node (`ZONE-00042`) with a property set `NH_ClashZone`
  (`zoneId`, `trackId`, `runId`, `clashCount`, `radiusMm`, `compareState`, side A/B
  association names). In Navisworks this is visible in Properties and usable in
  Search Sets, so "select all new zones" needs no plugin.
- Colour and transparency per compare state are baked in with `IfcSurfaceStyle`
  (red new, orange persisting, yellow moved, grey disappeared — carried over from the
  previous run). `OverridePermanentTransparency` on the root is the fallback if the
  reader drops the style.
- Units are explicit (`IfcSIUnit` millimetre); coordinates are written in document
  world coordinates converted to mm; no `IfcMapConversion`, site at the origin.
- The writer is plain text and testable without Navisworks.

Fallback: **STEP AP214 exact spheres** — smallest file, but no properties, so the zone
id lives only in the solid name. Keep STL/OBJ out: no units, and STL has no names.

### L3 result, 2026-10-14

Navisworks Manage 2027, plugin `main` 65e83e4, host model
`GCC-NAG-PM-12470-3D-MOD-00003.nwd` (E3D RVM, document units metres, site about 2 km
from the origin). Marker files were written by the spike from the 446 clash points
of the project's own Clash Detective XML export (metres → mm), radius 300 mm, and
appended through the UI Append command. The document was not saved.

| Check | Result |
| --- | --- |
| IFC4 `IfcTriangulatedFaceSet` + `IfcMappedItem` with uniform `Scale` | Loads. `ZONE-00001` bounding box centre equals the clash point to 1 mm, size 0.600 m; the mapped item stays an instance with transform `Scale 300` and the clash point as translation. |
| Units | `IfcSIUnit` millimetre converted to the metre document without any prompt. |
| Tree and properties | `clashpts.ifc / Clash zones / Clash zones run / IfcBuildingElementProxy / ZONE-nnnnn`; category `NH_ClashZone` shows `ZoneId`, `ClashCount` (Int64), `RadiusMm` (Double), `CompareState`. |
| Colour and transparency | Baked `IfcSurfaceStyleShading` red with transparency 0.6 renders red and see-through; no override needed. |
| Load time | 446 zones under 2 s; 10 000 zones about 12 s. Process working set went from 1.2 to 2.0 GB over all three test appends together. |
| STEP fallback | 446 exact spheres in about 5 s, named `ZONE-nnnnn`, same position (tessellated extent 0.598 m). |

Not covered: 2024–2026 (only 2027 was run), survey-scale coordinates (≥ 10⁶ m), and
`Document.RemoveFile` — no tool calls it yet; PR 7 must check it at L3.

## 4. Clustering design (option 2)

### Inputs per raw clash

`resultGuid`, `testName`, `status`, `centerMm`, `distanceMm`, result bounding box,
and for each side the item path, the association key at the chosen level, the source
model root, and the best stable item id available (`Item > GUID`, element id, PDMS
name) — the last one is what lets runs be compared at item level.

### Pipeline

1. **Filter** by test, status and name exclusions — the existing request fields.
2. **Partition** by the object-pair key `(ancestorA, ancestorB)` — today's `hybrid`,
   one zone per object pair (decided, section 7). A per-object key of one side was
   considered and rejected: one long object clashing many others would become one zone.
   The level is the existing nearest-named-parent resolver; a fixed number of levels up
   (as in `clash_group_results`) stays a later option. Keys with the `id:` prefix
   (InstanceGuid) are what make a zone recognisable in the next NWD.
3. **Split spatially** inside each partition: single linkage with distance `epsMm`
   (grid hash with cell `epsMm` plus union-find — what `clash_list_clusters` already
   does). This is DBSCAN with `minPts=1`; higher `minPts` would discard isolated clashes
   as noise, which is wrong here because every clash is a finding.
4. **Cap the zone size.** Single linkage chains along long objects (a pipe clashing a
   row of beams becomes one 40 m zone). If the enclosing sphere radius exceeds
   `maxZoneRadiusMm`, split the zone recursively at the median of its longest axis.
5. **Enclosing sphere**: exact minimum enclosing sphere of the member points
   (Welzl, move-to-front, expected linear), then `radius = max(minRadiusMm,
   mes + padMm)`. Seed the point order from the sorted member GUIDs so the output is
   deterministic. The sphere encloses the clash points only, not the clashing item
   boxes; `minRadiusMm` keeps a one-clash zone visible.
6. **Number** zones `Z-0001…` by partition key then centroid, so the same input gives
   the same ids.

### Parameters

Defaults accepted by the owner on 2026-10-14.

| Parameter | Default | Meaning |
| --- | --- | --- |
| association level | nearest named parent | Which tree level is "one object". Not a request field in v1. |
| `epsMm` | 300 | Max distance between neighbouring clash points in one zone. |
| `maxZoneRadiusMm` | 3000 | Split chained zones above this. |
| `minRadiusMm` | 150 | So a single-clash zone is visible. |
| `padMm` | 100 | Margin around the points. |
| `statuses` | New, Active, Reviewed | Approved/Resolved excluded from zones but counted. |
| `pointRadiusMm` | 50 | Sphere size in per-point mode. |

## 5. Excel layout and run comparison

A run folder holds three files: `zones.json` (the machine record), `zones.xlsx` (for
people) and `zones.ifc` (markers). The comparison reads the JSON, not the workbook:
people sort, filter and annotate workbooks, and the comparison must not depend on that.

Per-point mode (option 1) is the same pipeline with clustering switched off: every
raw clash is a zone of one with `pointRadiusMm`.

### Run workbook `zones.xlsx`

| Sheet | Rows | Columns |
| --- | --- | --- |
| `Run` | key/value | `schemaVersion`, `runId`, timestamp, NWD path, size, modified time, document units, plugin version, tests with last-run time and tolerance, every clustering parameter, raw/zone counts, marker file name. |
| `Zones` | one per zone | `zoneId`, `trackId`, `compareState`, `prevZoneId`, `clashCount`, status counts, `centerX/Y/Z` (mm), `radiusMm`, bbox min/max (mm), `minDistanceMm` (deepest penetration), tests, association A/B name and path, weak-association flag, source files A/B, representative clash name. |
| `Clashes` | one per raw clash | `resultGuid`, clash name, test, status, `zoneId`, `x/y/z` (mm), `distanceMm`, item A/B path, item A/B stable id, created time. |

The `Run` sheet carries every parameter so a comparison can refuse to compare runs
made with different clustering settings, or say that it is doing so.

### Comparison workbook `clash_zones_compare_<prev>_<curr>.xlsx`

`Summary` (zone and clash counts per state, net change), `Matches`, `New`,
`Disappeared`. Each match row has both zone ids, `trackId`, shift (mm), radius change,
clash-count change and the evidence used (`items`, `guid`, `geometry`).

### Matching rule

Zones are compared, not raw clashes, but the evidence is taken from the members:

1. **Candidates**: pairs whose spheres intersect (`d ≤ rPrev + rCurr`) and
   `d ≤ maxShiftMm` (default 2000). Runs made with different clustering parameters
   are compared only with an explicit flag, and the summary says so.
2. **Score**, strongest evidence first:
   - item overlap — Jaccard of the members' `(stableIdA, stableIdB)` pairs; survives a
     new NWD as long as element ids are stable;
   - geometry only — `1 − d / (rPrev + rCurr)`, with the same association key required.

   Clash `Guid` is not used: every iteration is a new NWD (owner, 2026-10-14), so
   result GUIDs do not carry over. `zones.json` still records them for diagnostics.
3. **Assign** one-to-one, greedily by score, ties broken by zone id (deterministic).
   A previous zone overlapping several current zones keeps its `trackId` on the best
   one; the others are `new` with `splitFrom`. The mirror case is `mergedFrom`.
4. **Classify**:
   - unmatched current → `new`; unmatched previous → `disappeared`;
   - matched and `d ≤ stillMm` (default `max(100 mm, 0.25 · rPrev)`) → `persisting`,
     otherwise `moved`;
   - independent flags: `shrunk` / `grew` when the clash count or radius changes by
     more than 20 %.

`trackId` is assigned once (`T-0001…`) and inherited through matches, so a zone can be
followed over many iterations by comparing each run with the previous one.

## 6. Proposed split

Two new MCP tools. Fewer tools keep `tools/list` small, so compare is a parameter of
the export rather than its own tool.

- `clash_zones_export` — host, effect `Files`, dry-run by default. Dry-run returns the
  zone summary (counts, largest zones, weak associations); `apply=true` writes the run
  folder. `mode=zones|points`, the clustering parameters, optional `previousRunPath`
  (assigns `trackId`/`compareState`, bakes state colours, adds the comparison
  workbook).
- `clash_zones_markers` — host, effect `Document`, dry-run by default.
  `operation=append|replace|remove|style`; finds its own model by file name, never
  touches other models; `style` sets colour/transparency on the marker root.

PRs in order. Sizes are against AGENTS.md §3; "new" means new self-contained code with
its tests (≤ 800 lines, ≤ 5 files), "change" means existing code (≤ 400 lines, ≤ 10
files, tool guard edits excluded).

| # | PR | Kind | Verification |
| --- | --- | --- | --- |
| 1 | Move the grid + union-find clustering into `Contracts` on plain coordinates; `ClashClusterConstructionService` delegates; characterization tests pin today's output. | change | L0/L1, L2; no runtime change claimed. |
| 2 | Zone geometry in `Contracts`: size cap with axis split, Welzl enclosing sphere, deterministic numbering. | new | L0/L1. |
| 3 | IFC4 marker writer in `Contracts` (mapped unit sphere, property set, state styles). The format passed L3 on 2027 (section 3). | new | L0/L1. |
| 4 | One multi-sheet XLSX writer with numeric cells in `Contracts`; existing writers migrate later, not here. | new | L0/L1. |
| 5 | Run comparer in `Contracts` (matching, classification, `trackId`) over `zones.json`. | new | L0/L1. |
| 6 | `clash_zones_export`: plugin `ClashZoneExportService`, contracts DTOs, `HostBridgeClient.ClashZones.cs`, tool type `NavisworksClashZoneTools`, guard edits. Adds `ClashResult.Guid` and item stable ids to the extracted rows. | new + guard edits | L1, L2, L3. |
| 7 | `clash_zones_markers`: `AppendFile`/`RemoveFile`, find-by-file-name, permanent style. | new + guard edits | L1, L2, L3. |
| 8 | Client guide workflow and skill section. | change | L1. |

PRs 1–5 need no Navisworks and can go to external executors with the brief template;
2, 4 and 5 are independent of each other. 6 and 7 touch the plugin and need the
live window.

## 7. Decisions and open items

Decided by the owner on 2026-10-14: IFC markers, the object-pair zone key, and the
proposed defaults for everything else. That fixes:

| Topic | Decision |
| --- | --- |
| Marker format | IFC4 as in section 3; STEP only if the L3 check rejects IFC. |
| Zone key | Object pair `(ancestorA, ancestorB)`, nearest named parent. |
| Clustering numbers | `epsMm` 300, `maxZoneRadiusMm` 3000, `minRadiusMm` 150, `padMm` 100; sphere around the clash points only. |
| Statuses | Zones from New/Active/Reviewed; Approved/Resolved counted, not drawn. |
| Iterations | Every iteration is a new NWD; runs are linked by element ids and geometry only. |
| Comparison | `maxShiftMm` 2000, still `max(100 mm, 25 % r)`, shrink/grow 20 %; disappeared zones written grey with `compareState=disappeared`, so a Search Set can hide them. |
| Compare surface | A parameter of `clash_zones_export`; no separate `clash_zones_compare` until asked for. |
| Persistence | The tools never save the document; the coordinator saves as NWF or NWD. Run folders go to `<NWD folder>\clash_zones\<runId>\` unless `outputDirectory` is given. |
| Scale | At most 50 000 analysed results per run, the existing `maxResults` cap; per-point mode uses the 80-triangle sphere. |
| Workbook language | Sheet names (`Run`, `Zones`, `Clashes`) and column titles are the camelCase `zones.json` field names: technical identifiers under the AGENTS.md language policy, so no resource strings (changed in #324). |

Still open: nothing blocks PRs 1–5. The format check passed on 2027 (section 3);
2024–2026 and `RemoveFile` are checked with PRs 6–7.
