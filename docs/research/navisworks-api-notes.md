# Navisworks API Notes

This file captures the small Autodesk SDK reference facts that NavisHelper currently depends on, without committing the ignored `api/` vendor sample tree.

## View.ProjectPoint

Autodesk's Clash Detective sample `ClashMarkersUtils.cs` uses `View.ProjectPoint(Point3D, bool, bool)` to project model coordinates into view/redline coordinates.

NavisHelper should prefer `View.ProjectPoint(...)` for precise 3D-to-2D projection, especially in perspective views, instead of relying on the older manual camera-quaternion projection.

The returned `ProjectionResult` exposes `X`, `Y`, and `Depth` values.
