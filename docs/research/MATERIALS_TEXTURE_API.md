# Materials and texture API spike (#361)

Evidence: the curator's installed-DLL reflection dump for Navisworks 2024-2027
and the known code facts supplied with #361. No live run was performed. Reflection
proves member presence, not usable document access or runtime behavior. Here,
**partly** means relevant members exist but the complete requested operation is
unproven; **not reachable** means no complete route is evidenced by this dump,
not proof that every possible Navisworks API lacks it.

`Autodesk.Navisworks.Api.Interop.*` types are unsupported internals, including
the COM types below. Depending on them risks version compatibility and native
state/ownership errors. Assumption to check live: suitable state, lock and native
objects can be obtained safely; `InternalFactory`/`InternalCreator` signatures
alone do not establish that route. No production support is established here.

## Reachable

The public appearance baseline is reachable in the supplied code facts:
`ModelItem.Geometry` exposes `OriginalColor`, `OriginalTransparency`,
`PermanentColor` and `PermanentTransparency`;
`Document.Models.OverridePermanentColor`, `OverridePermanentTransparency` and
`ResetPermanentMaterials` exist. `ItemAppearanceService` (#360) reads overrides.
These are colour/transparency operations, not a textured-material catalogue.
The 2026/2027 dump also shows public `AppearanceOverrides.MaterialOverrides`
and `MaterialOverride.Item`, `Color`, `Transparency`; it supplies no accessor
from a document to that collection. None of the five requested capabilities is
already implemented according to the supplied context.

| Requested capability | Reachability and candidate API | What only a live run can settle |
| --- | --- | --- |
| mapping per item | **Partly**, internal 2026/2027 members. `LcOpTextureSpaceElement.GetSelectedTextureSpaceMapping`, `CheckSelectedHasTextureCoordinates`, `GetSelectedTextureSpace`; `LcOpTextureSpace.GetTextureSpaceMapping`, `GetTranslation`, `GetScale`, `GetRotation`. Mapping enum includes plane (planar), box, cylinder, sphere and explicit. `LcOpTextureSpaceBox.GetDirection`, cylinder `GetCapDirection`, and `GetUDirection`/`GetVDirection` expose direction-related operations. | Assumption: selecting each resolved item permits an independent read and selection restoration. Mixed selections, inherited mappings and UV direction for every shape are unproven. Reproduce the planar panel versus box panels defect. |
| list materials | **Not reachable** as a document inventory with assigned items. `LcOglProteinAsset.GetLibraryId`, `GetDefinitionId`, `GetNumProperties`, `GetProperty`, `FindProperty` inspect an already obtained asset; `LcOglJsonProteinAsset.GetJsonData`/`GetSimpleMaterial` read an already obtained JSON asset. Public overrides only identify colour/transparency and items. | Whether assets can be obtained from document items at all, whether stable material identities exist, and whether unused materials and every assignment can be enumerated. |
| create a material | **Not reachable** end to end. COM `InwOaMaterial.Copy`/`Rename`, `AmbientColor`, `DiffuseColor`, `SpecularColor`, `EmissiveColor`, `Shininess`, `transparency` offer partial legacy material operations. `LcOpShadFragMaterialElement.OverrideProteinMaterial(state, paths, asset)` is an internal assignment candidate for an existing JSON asset, not a creation API. | Whether a Generic/library asset can be acquired or copied, edited and assigned to resolved match handles; image path, brightness, transparency and bump-off semantics; persistence and assignment scope. |
| texture transform | **Partly**, internal `LcOpTextureSpace.Clone`, `SetTranslation`, `SetScale`, `SetRotation`; plane/box/cylinder `GetDomain`/`SetDomain`; `LcOpTextureSpaceElement.Edit(state, lock)`, `ApplyTextureSpace`, `ApplyTextureSpaceAndTakeOwn(state, item, space)`. | Assumption: these can implement sample size W x H in metres, offset and rotation. Scale/domain-to-sample-size conversion, tiling and independent W/H (`keepAspect=false`) are unproven; no such named controls appear in the dump. |
| fit texture to item | **Partly**, an inferred calculation followed by the same texture-space setters and apply route. Inputs required by #361 are the item's bounding box and projection axis; the dump does not establish a bounding-box accessor. | Assumption: projected bbox extents give sample size and box UV uses world metres. Check the issue's `offset = -(bboxMin mod sampleSize)` per projected axis, coordinate conversion, negative coordinates, rotated items and visible alignment. |

## Not reachable

No supported public texture-mapping read/write API is evidenced here. The dump
also gives no complete document material enumeration/assignment lookup, Generic
factory, library enumeration/import, or editable Protein property-value route.
`LcOglProteinProperty.GetIdentifier` identifies a property; it does not expose
its value or a setter. JSON getters do not prove JSON creation or editing.
`InwOaMaterial.Copy` does not prove that a render-library material can be copied
or that a COM material can be converted to a JSON Protein asset.

No image-path, material brightness, bump enable/disable, tiling or aspect-lock
API is shown. `LcOpToneMapping` has exposure/highlights/midtones/shadows controls,
but the dump does not connect these to an individual material's brightness.
Appearance overrides must not be reported as successful creation of a textured
material. The selection-based texture reader is not a demonstrated arbitrary
item getter, and direction helpers do not prove complete UV access for all shapes.

## By Navisworks version

| Version | Evidence in the supplied dump | Support implication |
| --- | --- | --- |
| 2024 | Managed `Autodesk.Navisworks.Api.dll` dump is empty because the assembly could not be loaded for reflection in that process. COM `InwOaMaterial` members above are present. | Managed texture/Protein availability is unknown, not absent. Confirm inside a live 2024 process before offering any internal tool. COM member presence alone enables no complete requested workflow. |
| 2025 | Same managed reflection load failure; same listed COM material members. | Same unknown managed availability; confirm in live 2025. Do not infer a minimum supported version from the empty dump. |
| 2026 | Listed texture-space, Protein, material-override and tone-mapping internals; public override types; COM material members. | Candidate version for mapping diagnostics and then gated transforms/fit. Runtime access remains unverified; inventory/creation remain blocked by missing routes. |
| 2027 | Same relevant listed signatures as 2026, including COM material members. | Same candidates and blockers as 2026. Signature parity does not prove behavioral compatibility. |

The supplied public appearance facts have no per-version verification matrix.
Assumption to check live: the existing appearance baseline works across 2024-2027.

## Proposed tools

These are follow-up tool-issue candidates, not implemented contracts. Build the
smallest useful read first: the mapping diagnostic that could explain the
stretched pixel-row defect. Gate writes on that read succeeding. Defer material
inventory and creation until their missing acquisition/edit routes are proven;
among then-feasible work, follow with transform and fit preview before fit apply.

| Priority / candidate | Inputs | Outputs | Candidate versions and gate |
| --- | --- | --- | --- |
| 1. `get_item_texture_mapping` | Match handles, resolved one item at a time. | Per handle: mapping type, translation, scale, rotation, available UV directions/domain, coordinate availability and explicit unknown/error fields. | 2026/2027 after live selection/state checks; 2024/2025 only after in-process member confirmation and the same checks. |
| Deferred: `list_document_materials` | Document scope; optional item scope if a lookup route is found. | Material identities and assigned match handles, with explicit completeness/unused-material coverage. | No version established, 2024-2027; unblock asset acquisition, enumeration and reverse assignments first. Never substitute an override list. |
| Deferred: `create_and_assign_material` | Generic or library source; colour, image path, brightness, transparency, bump=false; target match handles. | Material identity and per-handle assignment/readback results. | No version established, 2024-2027; require creation/editing plus assignment proof. `OverrideProteinMaterial` is only a 2026/2027 assignment candidate in this dump. |
| 2. `set_item_texture_transform` | Match handles, sample width/height in metres, offset, rotation, tiling, `keepAspect=false`. | Per-handle before/after mapping and verified changes; unsupported fields explicitly reported. | 2026/2027 candidates after units/domain/aspect/tiling checks; 2024/2025 conditional on member and runtime confirmation. Offer only the verified subset. |
| 3. `fit_item_texture` | Match handles, projection axis, preview/apply choice. | Bbox basis, calculated W/H and offset; for apply, per-handle readback and failures. | 2026/2027 candidates after bbox/unit/transform checks; 2024/2025 conditional on member and runtime confirmation. Preview first; no automatic apply until visual validation. |

## Live checks

All points below are unresolved assumptions/checks, to be batched in an agreed
live window; this document reports no L3 result.

1. In each of 2024, 2025, 2026 and 2027, confirm loaded members and safe access to
   `LcOpState`, `LcOpLock`, texture elements and assets. Specifically resolve the
   2024/2025 managed reflection load failures rather than treating them as absent
   APIs. Check read-only status, ownership/lifetime and version-specific failures.
2. For mapping per item, compare reads to UI examples of planar/box/cylinder/sphere
   and explicit coordinates. Include the faulty panel, missing coordinates,
   single/mixed selections and inherited mappings. Verify UV directions per face
   where available, and that diagnostic reads restore selection without edits.
3. For list materials, establish the document/item-to-asset route and identities;
   compare inventory and reverse assignments with the UI, including unused
   materials and overrides. Close/defer the tool issue if no route is found.
4. For create a material, prove Generic/library acquisition, editable property
   values and a valid assignment path to resolved match handles. Check every
   requested field (especially image path, brightness and bump off), unrelated
   items, undo/reset and save/reopen. A COM copy or colour override is insufficient.
5. For texture transform, compare domain/scale/translation/rotation with the UI;
   establish model-unit-to-metre conversion and rotation conventions. Try unequal
   W/H with `keepAspect=false`, offsets and tiling. Confirm readback, visible result,
   ownership after `ApplyTextureSpaceAndTakeOwn`, undo and save/reopen.
6. For fit texture to item, establish bbox access and projection-axis conversion.
   Validate world-metre box UV and `offset = -(bboxMin mod sampleSize)` with a
   visible test image. Check negative coordinates/remainder convention, rotated
   items, zero projected extent and planar versus box behavior. Preview the
   calculation before any write; compare the applied result with UI and readback.
