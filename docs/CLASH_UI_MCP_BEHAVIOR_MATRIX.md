# Clash UI/MCP behavior matrix

This document records the Stage 6 verification for the technical-debt roadmap. It is an evidence log, not a proposal to merge the UI and MCP workflows.

## Scope and invariants

- The MCP wire protocol, MCP tool names, host command names, response DTOs and plugin IDs are unchanged.
- UI code remains responsible for WPF lifecycle, user interaction, virtual groups and UI-specific exports.
- MCP code remains responsible for request validation, operation tracking, cancellation and wire response shaping.
- A shared helper is a candidate for further extraction only when the same inputs produce the same observable result at both call sites.

## Verified boundaries

| Capability | UI path | MCP path | Existing shared code | Decision |
|---|---|---|---|---|
| Persistent group lookup/create/move/remove | `NavisHelper/WPF/NavisHelperPanel.Clash.cs:258-291`, with UI transaction/status handling | `NavisHelper/Agent/Services/DocumentCommandService.Clash.Workflow.cs:64-82, 162-354` | `ClashGroupMutationService`, `ClashGroupNameHelper` | Already shared. Do not add a second facade or change UI transaction semantics. |
| Result enumeration and metadata updates | `NavisHelper/WPF/NavisHelperPanel.Clash.cs:173-238` | `NavisHelper/Agent/Services/DocumentCommandService.Clash.Workflow.cs:173-270` and listing paths | `ClashWorkflowService` | Already shared. Keep orchestration separate. |
| Group-name normalization | UI has `GetUserClashGroupName` and `BuildPersistentClashGroupName` for `Clash-A`/`Clash-B` labels and visible virtual-group names | MCP uses `ClashGroupNameHelper` for match keys, side tags and deterministic planning names | `ClashGroupNameHelper` is shared by MCP mutation/planning code | Similar intent, different input/output rules. No merge without differential fixtures. |
| Virtual groups and grouping tree | `_virtualClashGroups`, cache/restore, tree rows and WPF event handlers in `NavisHelper/WPF/NavisHelperPanel.Clash.cs:1377-2405, 2899-2998` | No equivalent runtime-only UI model; MCP works with persistent Clash Detective groups and request-scoped plans | None | UI-only state. Do not expose it as MCP DTO or move it to Contracts. |
| Preview and selection state | `ClashPreviewManager` is coordinated by panel selection/preview actions | `ClashPreviewManager` is coordinated by document command/report workflows | `ClashPreviewManager`, lower-level state helpers | The manager is already shared; coordination and restore order differ. No new abstraction justified. |
| Run/state preservation | UI invokes `ClashRunPreservationService` around interactive test runs | MCP invokes the same preservation/state services around document commands and reports | `ClashRunPreservationService`, `ClashDocumentStateService` | Already shared. Preserve separate error/status handling. |
| Reports and viewpoints | UI-specific BCF/GIF/XML/CSV actions and dialogs in `NavisHelper/WPF/NavisHelperPanel.Clash.cs` | MCP report generation, screenshots, saved viewpoints and artifact manifest in `DocumentCommandService.Clash.cs` and `ClashReport*` services | Selected report/geometry helpers | Outputs, cancellation and lifecycle differ. No common UI/MCP DTO layer. |
| Cancellation and busy state | WPF `Progress`, `InteractiveOperation` and panel status | `ClashReportOperationTracker`, request-gate bypass and MCP status/cancel commands | Only lower-level operation primitives | Different contracts. Do not unify through a wire-facing service. |

## Result

The audit's broad statement that “Clash logic is duplicated” is only partially true. There are two orchestration paths, but the main persistent mutation, result traversal, metadata, preview and state-preservation primitives are already shared. The remaining large regions are behaviorally distinct UI/MCP workflows. A mechanical merge would either change behavior or introduce an unwanted dependency from WPF into MCP contracts.

No Stage 6 code change is justified by the current evidence. Future work requires a focused characterization fixture for one exact rule, followed by unit/differential tests and a live regression on the same test NWD before any extraction.
