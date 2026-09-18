# Native geometry and lossless model-node access

This slice implements the requests in the September 18, 2026 NavisHelper wish
list for equipment reconstruction from NWD. It exports the tessellation already
stored in the model; it cannot recover a unique original CAD/BREP construction
or increase the precision of the source tessellation.

## Geometry export

`export_selection_geometry` reads Navisworks COM primitives on the host UI
thread, without changing selection, visibility, model geometry or saved files.
Start with a dry run:

```json
{
  "outputPath": "D:\\Exports\\V-525.obj",
  "scope": "match_handle",
  "matchHandles": ["<opaque handle from this host>"],
  "instanceId": "<issuing host instanceId>",
  "format": "obj",
  "coordinateSpace": "world",
  "groupBy": "fragment",
  "itemLimit": 1000,
  "maxTriangles": 1000000,
  "apply": false
}
```

Repeat with `apply=true` to write. `overwrite=true` explicitly permits atomic
replacement. The path is absolute and belongs to the machine running Navisworks.
Its extension must match `format`. Dry runs enumerate/count the mesh but create
no files or directories. Applied exports spool binary doubles to disk, then write
the chosen format to a sibling staging file and publish only after completion.
Failures remove staging files and leave any existing destination untouched.

Supported formats:

| Format | Geometry and node metadata |
| --- | --- |
| OBJ | Original triangles as vertices/faces; named groups with an encoded path. `# fragment` JSON comments preserve the exact Unicode name/path, fragment ID, triangle count and transforms. |
| JSONL | A metadata record, fragment records, and individual triangle records containing `itemId`, `fragmentId`, `path`, and three vertices. |
| PLY | ASCII double vertices and triangle faces with `item_id`/`fragment_id`. JSON `comment fragment` records preserve exact paths/transforms. |
| STL | ASCII facets and separate solids per fragment. Solid names contain URL-encoded JSON metadata; generic readers may discard names, so prefer the other formats for reconstruction. |

No vertex welding, smoothing, decimation, analytic fitting or source-normal
interpolation occurs. Callback triangle vertex order is preserved, including
under reflected transforms; `reversesOrientation` records the COM flag. STL
facet normals are calculated from the emitted vertex order. Non-triangle lines,
points and snap points are counted and reported as omitted.

Selected/matched containers expand to descendants. SDK item identity deduplicates
overlapping input scopes. A fragment is accepted only when its COM owner equals
the visited geometry item; parent paths cannot duplicate descendant fragments.
Different nodes with the same display path remain different `itemId` values.
IDs are scoped to one export and are not persistent node identifiers.

`world` coordinates use the COM local-to-world matrix and document units.
Geometry numbers and JSON matrix elements use invariant `G17` formatting for
.NET Framework x64 round-trip precision. PLY metadata headers can be large for
fragment-heavy selections; JSONL is preferable for streaming those models.

Matrices are column-major, with translation at offsets 12вЂ“14. `item_local` means
the first owned fragment's native frame for each geometry item, **not** an inferred
AVEVA equipment frame. Every fragment includes `fragmentToWorld` and
`outputToWorld`; applying the latter to exported coordinates reconstructs world
coordinates. Local units are `item_native`, with `documentUnits` separately
recorded; source scale is part of the matrix. A singular local frame fails.

`groupBy` controls OBJ grouping (`item` or `fragment`, default `fragment`). Other
formats always retain fragment identity. `itemLimit` bounds both input items and
geometry owners (default 1000, maximum 10000). `maxTriangles` defaults to 1,000,000
and is capped at 5,000,000. Further limits are 20,000 fragments, 1,000,000 traversed
nodes and a 40-second operation deadline. Exceeding a limit fails; partial meshes
are never published. Native COM calls themselves are synchronous and cannot be
preempted while the API is not invoking callbacks.

## Addressing and properties

- `list_item_children(parentPath=...)` and `find_items(scopeNodePath=...)` consume
  complete display-name segments. `6501.5.nwd / /6501.5 / /6501.5.РђРњ` round-trips.
  If more than one node has that path, resolution fails as ambiguous; use a
  single-node handle instead. Slash-only node names are literal names.
- `list_item_children` accepts zero-based `offset`, returns `nextOffset` when
  more filtered children remain, and supplies `children[].matchHandle`. These
  single-child handles reference their page entry, so one page does not evict
  itself from the handle cache. Offsets apply after hidden-item filtering; child
  `index` is one-based within that filtered list. Tree changes can invalidate
  pagination; handles are the safer continuation mechanism.
- Search set union/intersection and `select_items` deduplicate by SDK item
  identity rather than display path. Selection UI behavior may still depend on
  the Navisworks runtime; property export can bypass it entirely.
- `selection_export_properties` accepts `scope=match_handle` and `matchHandles`.
  It reads all referenced nodes directly and never changes current selection.
  CSV/XLSX append `ItemIndex` to distinguish otherwise identical display paths.
- `cleanValues=true` serializes numeric VariantData through typed accessors with
  invariant decimal points, strings without synthetic type prefixes, booleans
  as `true`/`false`, and dates in ISO format. Strings that naturally contain a
  colon are preserved. `ValueType` reports the actual SDK data kind. Default
  display-value formatting remains unchanged.
- `selected_items_tree(format=flat, includeChain=false)` omits populated ancestor
  chains while retaining names, paths, depth and optional selected-item bounds.
  `includeChain` defaults to true for compatibility; tree format is unaffected.
- Item/Name `equals` and `contains` use the native internal Name property and
  preserve string identifiers such as `001`. Scoped, `first` and `countOnly`
  requests use native candidate search too, followed by the same name predicate.
  First-match pruning stops at scope roots. Other conditions retain their prior
  traversal behavior. Name searches with diacritic/character-width folding use
  the bounded manual predicate to avoid native-normalization false negatives.
  Native `scannedItemCount` counts inspected candidates, not
  the engine's internal traversal; no fixed performance factor is promised.
- New opaque handles contain the issuing instance and session. Cross-host use
  produces a stale result with the correct `instanceId` in the diagnostic instead of accidentally
  selecting another host's identically numbered match. TTL remains 10 minutes
  since last access, with 100 page/search entries; document changes invalidate
  handles. Older handles have no recoverable origin. Re-query after upgrading.
- `list_navisworks_hosts` probes live host status with a bounded timeout. It marks
  `documentTitleSource=live_host_status` when refreshed; a busy/unreachable host
  retains its discovery record with an explicit refresh error.

## Validation and runtime acceptance

Pure tests cover one-based COM arrays, transformed/reflected coordinate
round-trips, all four writers, invariant numbers, exact metadata, and ambiguous
slash-containing display paths. Protocol/router checks include the new command.
Build the full 2024/2025/2026/2027 matrix before packaging.

Runtime acceptance on a fresh deployed assembly must compare an exported mesh's
world bounds with the host API bounds, check parent-plus-child scopes do not
duplicate triangles, verify per-fragment ownership, compare Name search counts
across `all`/`first`/scoped modes, and confirm selection is unchanged by read-only
operations. For the customer model, additionally confirm all 37 matched property
nodes and 12 V-525 nozzles, and inspect a rotated/translated fragment. A small
fixture smoke does not replace that customer-model acceptance.
