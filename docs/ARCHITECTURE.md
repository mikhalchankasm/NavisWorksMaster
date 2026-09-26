# NavisHelper Architecture

Domain reference for the Navisworks plugin and the MCP server. This file is not
required reading before a task: read it when you are changing the area it
describes. [AGENTS.md](../AGENTS.md) is the only required document.

Build configurations, the bundle layout and local install are owned by
[BUILD_BUNDLE_RULES.md](../BUILD_BUNDLE_RULES.md); they are not repeated here.

## Project Overview

NavisHelper is a C# plugin suite for Autodesk Navisworks Manage (2024/2025/2026/2027). It automates model manipulation tasks: bulk color assignment, attribute loading from CSV, clash detection, viewpoint management, and AI-driven object coloring via external API. The active NavisHelperPanel and phase-one standalone UI surfaces use neutral English resources with matching Russian values in a satellite assembly; most end-user documentation remains Russian.

## Architecture

### Plugin System

Entry point is `RibbonLoader.cs` — a `CommandHandlerPlugin` decorated with `[Plugin]`, `[RibbonLayout]`, `[RibbonTab]`, and `[Command]` attributes. It routes ribbon button clicks to the corresponding `AddInPlugin` implementations via `Application.Plugins.ExecuteAddInPlugin()`.

The ribbon UI is defined in `CustomRibbon.xaml` (embedded resource) using Autodesk's AdWindows ribbon framework.

### Key Plugins (each is an `AddInPlugin` with its own `.addin` manifest)

- **ColorsByName** (`ColorsByName.cs`) — Core plugin. Reads a text file with `name;R,G,B;transparency` lines and applies colors to matching model items. Uses a 3-tier search fallback: internal property name → display name property → recursive display name matching.
- **AIColorObjects** (`AIColorObjects.cs`) — Thin plugin entry point for OpenRouter-powered coloring. It delegates to `AIColorWorkflow`, which uses the separate .NET 9 `NavisHelper.AiWorker` process for OpenRouter HTTPS; failed API calls never return local fallback colors as AI results.
- **AIColorSchemeSelector** (`AIColorSchemeSelector.cs`) — UI for selecting from 10 predefined color schemes defined in `ColorSchemes.cs`.
- **CsvAttributeLoader** (`CsvAttributeLoader.cs`) — Bulk loads attributes from semicolon-delimited CSV files. Builds an indexed lookup via `SearchCondition`-based queries.
- **MarkupViewpoint** (`MarkupViewpoint.cs`) — Creates a saved viewpoint with red ellipse markups around each selected element from the current orthographic or perspective camera. It reuses the MCP `MarkupSelection` workflow and `View.ProjectPoint()` projection. Prompts for viewpoint name via WinForms dialog (with clipboard auto-fill).
- **ShortestDistanceMarker** (`ShortestDistanceMarker.cs`) — Compatibility command that opens the `Высоты Z` tab. The active workflow reads every selected item's bounding-box `Max Z`, then creates persistent vector labels or dimension lines from the top-face center to a configurable global Z level.
- **TopViewSection** (`TopViewSection.cs`) — Switches to orthographic top-down view, zooms to selected elements (`ZoomBox`), and enables section plane. Uses reflection to call internal `LcRmFrameworkInterface.ExecuteCommand()` for section toggle.
- **TopViewBoundingRect** (`TopViewBoundingRect.cs`) — Draws a bounding rectangle (4 `RedlineLine` segments) around all selected elements on the current view. Uses its own `View.ProjectPoint()`-based redline projection. Prompts for viewpoint name via WinForms dialog (with clipboard auto-fill).
- **AboutNavisHelper** (`AboutDialog.cs`) — Shows version and plugin information dialog.

### AI Integration Layer

`AIColorObjects` uses OpenRouter as a bring-your-own-key integration. The Settings tab validates the key through the .NET 9 `NavisHelper.AiWorker` before storing it in the user-scoped `OPEN_ROUTER_NW_KEY`, then updates the current process and runtime key provider so no restart is needed. `OpenRouterKeyStore` is the sole runtime key source; the key is never serialized to `%APPDATA%\NavisHelper\ai_config.json`. NavisHelper passes it only through the child worker environment; protocol JSON, arguments, diagnostics, and temporary files never contain the key. The command is single-flight and asynchronous: Navisworks data is captured on the UI thread, worker IPC/HTTPS runs off-thread with timeout/cancellation, and application returns through the dispatcher only after an active-document identity guard. A user-filtered dynamic catalog must confirm the selected exact full ID and `structured_outputs` support before chat; an unavailable catalog blocks the paid request. Color requests use strict JSON Schema without reasoning or automatic retry. Failures never invoke a silent fallback. Local palette coloring is a separate explicit action with typed provenance. `ColorService.exe` and temporary-file IPC are absent from the active compiled path; the retained `ColorService/` source project is legacy reference material outside the solution. Registered MCP tools do not use this external AI path.

### Core Utilities

- `Core/Logger.cs` — Static file-based logger writing to temp directory or alongside the model file.
- `Core/ColorParser.cs` — Parses `#AARRGGBB`, `#RRGGBB`, and `R,G,B` color formats.

### Navisworks API Patterns

Plugins access models via `Application.ActiveDocument`. Key API operations:
- **Selection:** `doc.CurrentSelection`, `ModelItemCollection`
- **Search:** `Search` class with `SearchCondition` (by internal name or display name)
- **Color override:** `doc.Models.OverridePermanentColor()` / `OverridePermanentTransparency()`
- **Progress:** `Application.BeginProgress()` / `EndProgress()` for long operations
- **Bounding box:** `selection.BoundingBox()` returns combined `BoundingBox3D` for a `ModelItemCollection`. Note: `BoundingBox3D.Copy()` does NOT exist.
- **Saved viewpoints:** `doc.SavedViewpoints.InsertCopy()` + `ReplaceFromCurrentView()` to save current view with redlines.
- **Search pruning:** `Search.PruneBelowMatch` defaults to **true** — a `new Search()` that never sets it skips descendants of every match. `SearchService.ExecuteSearchQuery` assigns it explicitly via `FindItemsNativeSearchPolicy.PruneBelowMatch`; do not drop that assignment. `whole_model + matchDepth=all` is pruned by contract, scoped `matchDepth=all` is not, and the pruned path emits a warning when a match has children. See `docs/MCP_TOOL_CONTRACTS.md`.
- **Scoped native search:** scoped `find_items` with `matchDepth=first` is answered by `SearchService.NativeScoped.cs` — a native `Search` rooted at the scope with `PruneBelowMatch = true`, because engine pruning *is* `first`. Eligibility lives in `FindItemsNativeScopedPolicy`; everything else (`matchDepth=all`, `countOnly`, OR semantics, negation, inherited properties, `starts_with`/`ends_with`) stays on the manual traversal, which remains the reference behaviour. Never disable pruning to widen this path — that is what made the earlier attempt 2000x slower. `NAVISHELPER_FIND_ITEMS_NATIVE_SCOPE=0` forces manual.
- **A condition without a category means different things to the two search routes.** Measured live on `6501.5.nwd`, Navisworks 2027, one build, same property, value, `matchDepth` and `countOnly`:

  | condition | route | matches |
  | --- | --- | --- |
  | `AVEVA/DN equals "150mm"` | `whole_model`, native | **135** in 126 ms, `depthHistogram {5:135}` |
  | `/DN equals "150mm"` (no category) | `whole_model`, native | **0**, `status: not_found` |
  | `/DN equals "150mm"` (no category) | `under_named_node`, manual traversal | **1** — the scope root itself, which carries `AVEVA/DN = 150mm` |

  The engine sees a display-name-only property perfectly well when the category is given: `CreateSearchCondition` in `SearchService.Rules.cs` prefers `SearchCondition.HasPropertyByDisplayName` and only falls back to `HasPropertyByName`. What diverges is the **category-less** case. The native condition resolves to a category that does not hold the property and returns nothing; the manual matcher finds the property in whatever category it lives in. So the same request answers 1 when scoped and 0 when not, and the caller is told `not_found` with no hint that the category is the problem — while `WholeModelCountOnlyWarning` states that `matchedItemCount` is exact. Pass the category. Until the two routes agree, a category-less condition is not a safe thing to compare across scopes, and a `not_found` from a whole-model search does not establish absence.

- **Result identity:** dedup search results by `ModelItem` (its `Equals` compares the underlying native object), never by a path built from `DisplayName`. Models contain genuinely distinct siblings that share a display name, so path-keyed accumulators silently drop real matches — measured live as 70 vs 115 hits on one `6501.5.nwd` query. Use `FindItemsMatchSet<ModelItem>` for find_items accumulation. Every dedup that decides **which items a tool returns or acts on** is keyed by item: `find_items`, `select_items`, `find_items_by_bbox`, the root search index behind `list_root_items` / `find_root_items_by_name` / `list_item_children`, the `list_item_children` fast-path parent resolution, `dump_subtree_names`, `clash_bbox_pair_plan`, `clash_create_matrix_from_selection`, `select_selection_set`, the selection-set save path, and the visibility collectors behind `hide_selected` / `unhide_selected` / `reveal_selected` / `hide_unselected`. A new `HashSet<string>` **or `Dictionary<string, …>`** fed from `BuildItemPath` is the regression to watch for — grepping only for `HashSet` misses the dictionary form, which is how the exception below survived a sweep.
- **One known path-keyed aggregation, reporting only:** `VisibilityRootSummaryAccumulator` groups affected items by `BuildItemPath(rootItem)`, so two appended files sharing a display name merge into a single `affectedRootSummaries` row with a combined count. No item is lost: the hide and reveal decisions themselves are identity-keyed, and only the per-root summary is affected. Left as is because it has never been demonstrated on a model with duplicate root names, and an invariant that lists its exceptions is worth more than one that overstates. Resolving a caller-supplied path *string* back to an item, as `TryResolveClashPairRoot` does, is a different problem — there is no item to key by, and the ambiguity it creates is handled where printed paths are parsed.
- **Two of those sets are not plain dedup**, and were converted on their own reasoning rather than by the rule above. `AddHiddenAncestors` uses its set as an already-processed marker: its `Contains` break is a short-circuit, not a correctness requirement, because `Add` already refuses the repeats higher up — deleting the break would produce identical output. That is what makes identity right rather than a judgement call, and keyed by path it did worse than dedup wrongly, leaving an ancestor hidden *and* skipping everything above it on that chain. `hide_unselected` tests membership across two traversals rather than within one: the set is built from the selection and tested against every node the hide walk reaches (it stops at selected items since #72), so a path key spared an unselected same-named twin from hiding and then descended into it.
- **An abandoned traversal poisons the session:** a scoped traversal that hits its 45 s budget or 1,000,000 item limit has materialized hundreds of thousands of `ModelItem` wrappers — 268 949 measured live on `6501.5.nwd`. While they stay reachable, every later search slows down sharply, and the cost lands in path building rather than in the engine: the same whole-model search returning 3616 matches took 554 ms in a fresh process and 7428 ms right after one such failure. `SearchService.AbandonScopedTraversal` drops the collections and collects before reporting the error, and `AbandonNativeScopedSearch` does the same for the native scoped path, which costs 96 ms on a call that already spent 45 000 and restores the next search to 519 ms. Raise the budget errors through that helper, never with a bare `throw`. Phase timings are in the `find_items search_phases` log line (`accumulate_ms`, `sort_ms`).
- **Completed walks leave wrappers behind too, more slowly:** every `ModelItem`, `ModelItemCollection` and `BoundingBox3D` is a `NativeHandle` holding a native weak reference that only `Dispose` or its finalizer releases (read from `Autodesk.Navisworks.Api.dll` by reflection), and walks dispose none. Identical whole-model `find_items_by_bbox` calls on `6513.nwd` grew from 1.0 to 5.5 s in one process, until the runtime happened to run a full collection. `HeavyWorkCollectionPolicy` fires once four generation-0 collections have passed since the last forced one: a 41 000-item walk causes about six, `host_status` none. It also fires after any command of 500 ms or more, regardless of its generation-0 count, because wrapper-heavy commands such as `hide_unselected` can slow later calls even without a generation-0 collection. The host then collects, drains finalizers and collects again on a pool thread, never under the request gate, which rejects rather than queues. The next gated request waits for that collection, capped at 10 s, before it starts. The wait matters: a collection that overlaps a walk slows both, and the first one after a file load took 5.2 s overlapping a walk against 70 ms alone. With this, the same calls stay at 1.0–1.3 s. Each collection costs about 0.1 s, of which the next request waits 50–75 ms. Numbers in `docs/MCP_TOOL_BASELINE.md`.
- **Bound a `ModelItem` set that is the only reference to its wrappers:** when a walk needs identity dedup, bound the set by what the call returns, or drop it when the walk cannot produce a duplicate. A set whose wrappers another collection already holds costs nothing measurable and is not worth changing. The measurements are in `docs/MCP_TOOL_BASELINE.md`, "What a `ModelItem` set actually costs".

### Redline (Markup) JSON Format

Redlines are set/get via `activeView.SetRedlines(json)` / `activeView.GetRedlines()`.

- **Collection wrapper:** `{"Type":"RedlineCollection","Version":1,"Values":[...]}`
- **Line:** `{"Type":"RedlineLine","Version":1,"Thickness":3,"Color":[1,0,0],"Start":[x1,y1],"End":[x2,y2]}`
- **Ellipse:** `{"Type":"RedlineEllipse","Version":1,"Thickness":3,"Color":[1.0,0.0,0.0],"MinPoint":[x1,y1],"MaxPoint":[x2,y2]}`

Important gotchas:
- `RedlineLine` requires `Start`/`End` fields. Using `MinPoint`/`MaxPoint` is silently discarded.
- `RedlineFreehand` type is NOT supported by `SetRedlines()` — silently discarded.
- `RedlineArrow` type is NOT supported by `SetRedlines()` — it throws `ArgumentException` and rejects the entire collection. Navisworks XML `<rlarrow>` is a storage primitive, not a writable JSON primitive; convert arrows to three `RedlineLine` values before calling the API.
- Never infer the writable `View.SetRedlines()` type set from Saved Viewpoints XML. XML is a storage format; the JSON writer accepts a narrower, independently verified set.
- Color format varies: `RedlineLine` uses integers `[1,0,0]`, `RedlineEllipse` uses floats `[1.0,0.0,0.0]`.

### WorldToRedline Projection

3D-to-2D projection for redline coordinates uses the Navisworks camera quaternion layout `Rotation3D(A=X, B=Y, C=Z, D=W)` and the transposed rotation matrix for world-to-camera conversion, then:
- **Orthographic:** `rx = camX`, `ry = camY` (camera-space offsets from position)
- **Perspective:** `rx = -projX / (2 * tan(HeightField))`, `ry = -projY / (2 * tan(HeightField))` where `projX = camX / (-camZ)`. Note: this manual formula has known accuracy issues for perspective views. The official `View.ProjectPoint()` API is the recommended approach for precise projection.

Top-down view quaternion: `Rotation3D(0, 0, 0, -1)` (X=0, Y=0, Z=0, W=-1), which is the identity orientation up to quaternion sign.

Projection regression tests must include a rotated camera, preferably with roll. A top-view-only test is invalid because `(0,0,0,-1)` can mask a broken quaternion layout. Never introduce a compensating sign or constant merely to match one camera; fix the coordinate model instead.

Large markup merges must go through `MarkupFrameGroupingHelper` safety limits and spatial sweep. Do not reintroduce an unconditional all-pairs scan for `markMergeGapMm`; selections above 1000 items have previously hard-crashed Navisworks on that path.

### Internal API Access via Reflection

Some Navisworks internal types (`LcRmFrameworkInterface`, `LcUCIPExecutionContext`) are not publicly accessible. Access them via reflection:
```csharp
Type FindType(string fullName) {
    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
        var type = asm.GetType(fullName);
        if (type != null) return type;
    }
    return null;
}
// Usage: FindType("Autodesk.Navisworks.Internal.ApiImplementation.LcRmFrameworkInterface")
```
Section enable command: `LcRmFrameworkInterface.ExecuteCommand("RoamerGUI_OM_SECTION_MASTER_ENABLE", LcUCIPExecutionContext.eTOOLBAR)`

### Solution Structure

- `NavisHelper/` — Main plugin DLL project (.NET Framework 4.8.1)
- `ColorService/` — Legacy standalone source project retained outside the solution; it is not part of the active compiled, installer, or distribution path
- `docs/research/navisworks-api-notes.md` — distilled Navisworks API notes from Autodesk SDK samples
- `NavisHelper.bundle/` — Bundle resources for deployment

**Important:** `NavisHelper/NavisHelper.csproj` is the main plugin project and is **non-SDK-style**. New `.cs` files must be explicitly added via `<Compile Include="NewFile.cs" />`. There is intentionally no root-level `NavisHelper.csproj`; build through `NavisHelper.sln` or the project paths documented above.

### Common Pitfalls

- **Type ambiguity:** Adding `using System.Windows.Forms;` causes conflict between `System.Windows.Forms.View` and `Autodesk.Navisworks.Api.View`. Fix: fully qualify as `Autodesk.Navisworks.Api.View activeView = doc.ActiveView;`
- **Deployment:** `NavisHelper.bundle/Contents/<version>/` is populated by the local build matrix. Do not commit DLL/PDB build artifacts; package/release only after the four supported configurations have rebuilt the local bundle.

### Navisworks ProjectPoint API

`View.ProjectPoint(Point3D, bool, bool)` returns a `ProjectionResult` with `X`, `Y`, `Depth` properties. This is the official Navisworks .NET API for 3D-to-2D projection; the local distilled note is `docs/research/navisworks-api-notes.md`. Prefer this over manual quaternion-based projection for perspective views.

### Conditional Compilation

The `.csproj` uses conditional `<ItemGroup>` blocks to select Navisworks API DLL paths based on the active configuration. Configurations containing "2024" reference Navisworks Manage 2024, "2025" reference 2025, "2026" reference 2026, and "2027" reference 2027. The default (plain Debug/Release) also references Navisworks 2026. All DLL paths follow the pattern `C:\Program Files\Autodesk\Navisworks Manage 20XX\<DllName>.dll`.
