# Geometry and model access validation вЂ” 2026-09-18

Scope: native triangle export and the model-access corrections described in
[MCP_GEOMETRY_EXPORT.md](../MCP_GEOMETRY_EXPORT.md).

## Automated validation

- 1,592 MCP/helper tests pass, including 19 new path/geometry test cases.
- SDK solution builds pass for Release2024, Release2025, Release2026 and
  Release2027, x64. Modern numeric VariantData accessors are accessed by known
  names at runtime because the 2024/2025 SDKs do not define those members.
- Host router: 88 commands, 81 typed routes, one special route, six policy bypass
  commands. Geometry export remains under the normal UI request gate.
- MCP stdio discovery exposes 105 tools and the new geometry parameter schema.
- Compile inventory, localization audit, catalog parity and diff whitespace
  checks pass.

## Live runtime smoke

Two disposable processes, Navisworks Manage 2026 and 2027, opened the Autodesk
sample `High Visibility 01.nwd`. Tests verified the loaded assembly path under
the per-user AppData bundle, timestamp, length and deployed SHA-256:

| Runtime | Tested NavisHelper.dll SHA-256 |
| --- | --- |
| 2026 | `5AB83EDA934E7A2FF27EC5D67AA5E0D37324D2AE9B7EF06322781AE4A31DC3F8` |
| 2027 | `1255A2CEA69EEB942E9D3D00C2748EFA23C33FB91BB21701ED3B3A96DD92AF9D` |

Both produced 6,063 triangles, 19 owned fragments and 10 geometry items. All four
formats completed. JSONL export took approximately 185 ms / 198 ms respectively
on this small fixture; these timings are not performance claims for large NWDs.

The largest world-bounds deviation from `selection_status.boundingBox` was about
`4.4e-8` document meters. Both final runs additionally asserted every local vertex
round-trips through `outputToWorld` within `1e-10` tolerance. Overlapping
root-plus-child handles produced the same triangle count as the root alone.

The smoke also checked per-child handles and offset pagination, returned-path
resolution, native Name `equals` in whole-model and scoped-first modes, compact
selection trees, direct-handle property CSV export, live title refresh,
cross-instance diagnostics and unchanged selection after read-only operations.
Mixed valid/foreign handles retained the existing partial-result contract.
An intentional `maxTriangles=1` failure preserved a pre-existing destination and
removed temporary files.

Both processes reported `documentWasModified=false` when closed normally. The
previous per-user 2026/2027 installations were restored and their DLL hashes
checked. Generated meshes, logs and installation backups stay under ignored
`artifacts/geometry/`; they are not release artifacts or tracked source.

Reproduce against a disposable sample host with at least two root children:

```powershell
python scripts/navishelper_geometry_smoke.py --instance-id <sample-host-id> --output-dir artifacts/geometry/new-run
```

Use a new output directory. This script intentionally changes the disposable
host's selection and never saves its document.

## Remaining acceptance limits

The customer `6501.5.nwd` was not used for this smoke. Its 37 property-bearing
nodes, 12 V-525 nozzles and large-model timings still require owner acceptance.
2024/2025 received SDK build validation, not a live COM run. Native tessellation
does not guarantee a unique recoverable CAD primitive or original BREP.

## External advisory review and disposition

The repository's Claude Code wrapper reviewed a plain-text context bundle through
the local session with tools and MCP disabled. It returned advisory findings and
did not request tool execution. Codex verified findings against the actual code
before applying changes. Local review input/output are retained in ignored
`artifacts/geometry/claude-context.txt` and `claude-review.txt`.

Confirmed and fixed:

- COM callbacks retain failure and return quietly; the managed caller raises it
  after native traversal returns, so errors never cross the CCW boundary.
- Mesh coordinates and JSON matrices use invariant `G17`; clean numeric property
  values use it too. Microsoft documents the .NET Framework x64 `R` limitation
  in [standard numeric format strings](https://learn.microsoft.com/en-us/dotnet/standard/base-types/standard-numeric-format-strings#round-trip-format-specifier-r).
- Foreign handles return false from `TryGet`, preserving partial-result callers;
  diagnostics identify their origin. Child indices are parsed after the final
  handle-field delimiter.
- Host title refresh isolates all per-host failures while preserving explicit
  caller cancellation. The probe timeout includes the transport response margin.
- Native scoped candidates are checked against time/count limits before manual
  filtering and during ancestor pruning. Character-width/diacritic folding uses
  the bounded manual predicate, avoiding unverified native normalization.
- Geometry output requires a fully qualified drive/share path. Writers use LF
  consistently, numeric reflection accessors are cached, and the curated tool
  catalog describes the new surface. New precision/path tests and the repeated
  live smoke cover these changes.

Findings not accepted as new blockers:

- The review inferred a sub-40-second bridge default without its declaration.
  `HostBridgeClient.DefaultTimeoutMs` is actually 60000, above the 40-second
  export budget and transport margin; no increase to ten minutes was needed.
- Scoped broad-query guard exceptions predate this change. Their existing error
  contract was left intact rather than expanding this slice to all search errors.
- Triangle count and wall-clock caps are independent safety ceilings, not a
  throughput guarantee. Native COM calls cannot be preempted; callbacks become
  no-ops on failure and publication still fails. Large jobs must narrow scope.
- Manual `ReleaseComObject` was not added without an exclusive RCW ownership
  contract from the SDK. Shared wrappers must not be invalidated speculatively.

The final SDK matrix, tests and both live smokes were repeated after the accepted
fixes. Customer-model acceptance remains the limitation stated above.
