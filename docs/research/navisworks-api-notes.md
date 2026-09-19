# Navisworks API Notes

This file captures the small Autodesk SDK reference facts that NavisHelper currently depends on, without committing the ignored `api/` vendor sample tree.

## View.ProjectPoint

Autodesk's Clash Detective sample `ClashMarkersUtils.cs` uses `View.ProjectPoint(Point3D, bool, bool)` to project model coordinates into view/redline coordinates.

NavisHelper should prefer `View.ProjectPoint(...)` for precise 3D-to-2D projection, especially in perspective views, instead of relying on the older manual camera-quaternion projection.

The returned `ProjectionResult` exposes `X`, `Y`, and `Depth` values.

## Search.PruneBelowMatch

`Autodesk.Navisworks.Api.xml` documents `Search.PruneBelowMatch` as:

> When value is true, search ignores descendants of any matching model items. Default is true.

A `new Search()` that never assigns the property therefore runs pruned. NavisHelper
assigns it explicitly in `SearchService.ExecuteSearchQuery` so the whole-model
`find_items` contract is a decision rather than an inherited SDK default. See
`docs/MCP_TOOL_CONTRACTS.md` for the resulting `matchDepth` semantics.

## ModelItem identity vs display path

`ModelItem.Equals(object)` is documented as determining "whether the specified
object and the current object refer to the same underlying native object", so
`HashSet<ModelItem>` / `Dictionary<ModelItem, …>` key by node identity across
separate `Search.FindAll` calls.

A path built by joining `ModelItem.DisplayName` up the ancestor chain is *not* an
identity. Navisworks models routinely contain several distinct siblings that share
a display name — measured on `6501.5.nwd`, one PDMS branch node has 142 children in
which most names appear twice, with different child counts. Keying search results
by that path silently drops the duplicates. Dedup search results by `ModelItem`,
never by the display path.
