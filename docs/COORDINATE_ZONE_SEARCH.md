# Coordinate and Zone Search (v1)

## Scope

`find_items_by_bbox` is a read-only spatial search over the active Navisworks document. It accepts an axis-aligned bounding box and returns a normal match handle. The handle can be passed to `select_items`, preview, zoom, or dry-run visibility tools.

The v1 coordinate space is `document_global`: raw global document coordinates in the model's active Navisworks units. There is no implicit millimetre conversion, local-origin transformation, grid lookup, georeferencing, or rotation. This avoids silently selecting the wrong location when appended source models use different survey/local conventions.

## Request shape

```json
{
  "min": { "x": 100.0, "y": 200.0, "z": 0.0 },
  "max": { "x": 125.0, "y": 240.0, "z": 15.0 },
  "matchMode": "intersects",
  "sourceFileContains": "MEP",
  "maxScannedItems": 100000,
  "maxResults": 5000
}
```

`matchMode` values:

- `intersects` (default): item bounding box overlaps the zone.
- `contains`: the whole item bounding box lies inside the zone.
- `center`: the item bounding-box centre lies inside the zone.

By default only leaf model items are returned. `includeContainers=true` additionally returns aggregate/container nodes; it can duplicate a physical location at several tree levels and should be used deliberately.

## Safety and completeness

The tool is bounded to 100,000 scanned items and ten seconds by default, and retains at most 5,000 matches. If a limit is reached, `traversalTruncated` or `resultsTruncated` is true and the returned match handle represents only the bounded subset. Narrow the zone, use `sourceFileContains`, or increase an explicit limit within the documented maximum before applying follow-up changes.

## Deferred work

Named zones, project-local origin/rotation, grid-cell references, coordinate-system metadata, and CSV zone import require an explicit project coordinate convention. They are intentionally out of v1 rather than inferred from model geometry.
