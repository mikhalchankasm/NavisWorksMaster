# Clash Result Clustering Plan

This note captures the planned workflow for grouping many nearby Clash Detective results into one user-facing problem location. It is intentionally separate from the current raw clash report so the default report behavior remains stable.

## Problem

Large models often produce many raw clash results around one physical issue: small sub-parts of the same construction touch or intersect within a short distance. Reporting each raw result separately creates too many screenshots, viewpoints, and rows.

The workflow needs a second abstraction:

- **Raw clash result**: one Navisworks Clash Detective result.
- **Clash cluster**: one issue location made from multiple raw results that are close in space and/or belong to the same higher-level model objects.

## Grouping Modes

### Spatial Cluster

Group by clash point distance.

Inputs:

- `clusterDistanceMm`: maximum distance between connected clash points, default candidate `300`.
- `clusterAlgorithm`: initially `connected_distance`.

Behavior:

- Use the clash result center point when available.
- Two clashes are in the same cluster if their points are within `clusterDistanceMm`.
- Clustering is transitive: if A is close to B and B is close to C, all three are one cluster.
- Use a grid hash by cell size `clusterDistanceMm` to avoid an O(N^2) scan for large reports.
- No external math library is required for the MVP.

Cluster output:

- cluster id and stable display name;
- member result count;
- centroid point;
- bounding box around all member clash points and participating item bounds;
- status counts;
- test names included in the cluster;
- representative result for metadata when a single value is needed.

### Object/Ancestor Cluster

Group by model hierarchy, similar to Clash Detective grouping by selected tree level.

Inputs:

- `clusterByAncestor`: false by default.
- `clusterAncestorMode`: `none`, `source_file`, `nearest_named_parent`, `fixed_depth`, later `property`.
- `clusterAncestorDepth`: optional depth for `fixed_depth`.

Behavior:

- For each side A/B, resolve a stable ancestor path for each clashing item.
- Build a grouping key from `(ancestorA, ancestorB)` after normalizing path/name.
- Optionally combine with spatial clustering so only nearby clashes inside the same ancestor pair are merged.

Open question:

- Navisworks clash item sides can contain internal geometry, fragments, or empty display names. The resolver must fall back through item path, parent chain, source file, and display name instead of assuming leaf names are always useful.

### Association-First Hybrid Cluster

Recommended default:

- First group by associated object pair when a reliable parent/identity exists.
- Inside each associated pair, split by spatial distance so one long object pair with separate issues does not collapse into one huge cluster.
- Treat discipline, level, package, and source file as optional labels, not required grouping axes.
- If association resolution falls back to a coarse source/root or leaf path, mark the cluster as weak instead of pretending the assignment is authoritative.

This is important for real coordination models where a practical issue is "pump building vs pump pipelines" or "this object is related to those objects", even when the source models do not carry clean architectural/engineering discipline metadata.

## Report Semantics

For `clash_generate_report`, clustering is opt-in:

- `groupMode=none` keeps the existing behavior.
- `groupMode=spatial` adds spatial cluster metadata.
- `groupMode=object_pair` adds associated object-pair cluster metadata.
- `groupMode=hybrid` combines associated object pairs and spatial splitting.

Report clustering supports both metadata-only and true cluster artifacts. `artifactGranularity=result`
keeps one visual artifact per raw clash. `artifactGranularity=cluster` creates one shared saved
viewpoint/screenshot set per cluster, while preserving every raw member row in the manifest.

One-cluster screenshot/viewpoint behavior:

- Highlight all unique side A items and all unique side B items in the cluster.
- Use the normal A/B colors.
- Build the section box from all member clash points plus `boxOffsetMm`, or from all participant item bounds plus padding when `boxMode=items`.
- Context transparency should apply once per cluster, not once per raw clash member.
- The report row should show the cluster summary first and then allow expanding member clash rows.

## MCP Surface Draft

Implemented read-only tool:

- `clash_list_clusters`

Arguments:

- `testName`, `testNames`, `statusFilters`, `includeAllStatuses`;
- `excludeItemNameContains`;
- `clusterDistanceMm`;
- `groupMode`;
- `limit`, `resultOffset`;
- `previewRowsPerCluster`;
- `maxResults`.

Returns:

- total raw result count;
- returned cluster count;
- cluster summaries with member counts and representative metadata;
- deterministic `clusterId`;
- associated object pair keys, display names, per-side association levels, and `weakAssociation`;
- centroid, bounding box, status counts, optional tags, and bounded raw clash preview rows;
- warnings for weak association resolution.

Extension to `clash_generate_report`:

- same clustering arguments;
- `includeClusterMembers`: include raw member rows in `manifest.json` and expandable HTML sections;
- `maxMembersPerClusterInHtml`: keep huge clusters readable.

## Implementation Phases

1. Add read-only cluster analysis with dry-run output only. **Implemented as `clash_list_clusters`.**
2. Add cluster fields to `manifest.json` and HTML while still generating one screenshot per raw result. **Implemented for `clash_generate_report` with `groupMode != none`.**
3. Add true cluster screenshots/viewpoints: one visual artifact per cluster. **Implemented for
   `clash_generate_report` with `artifactGranularity=cluster`. The first safe version requires the
   complete filtered result scope in one call and rejects `append=true`/non-zero `resultOffset` to
   prevent duplicate or partial cluster artifacts.**
4. Add UI controls after MCP behavior is stable: grouping mode, distance slider, and expandable member table.

## Safety Notes

- Keep clustering disabled by default until it has been tested on large real models.
- Always report both raw result count and cluster count so the user sees how much was merged.
- Never delete or mutate Clash Detective results during grouping.
- Preserve status information; a cluster with mixed statuses must expose status counts, not one misleading status.
