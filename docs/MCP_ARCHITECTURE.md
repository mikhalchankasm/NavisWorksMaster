# NavisHelper UI And MCP Architecture

This document records the target direction for keeping the NavisHelper WPF panel and the NavisHelper MCP automation surface aligned without duplicating command logic.

## Current State

The repository already contains the live MCP bridge:

- `NavisHelper/Agent/Host/AgentHostService.cs` runs inside the Navisworks plugin process.
- The host exposes a named pipe named `navishelper-mcp-<pid>`.
- Requests are JSON frames with a 4-byte length prefix.
- Requests are marshalled onto the Navisworks UI thread before touching `Autodesk.Navisworks.Api`.
- Running Navisworks instances are discovered through `%LOCALAPPDATA%\NavisHelper\Mcp\instances`.
- `NavisHelper.McpServer` is the external MCP server and talks to the plugin through the named pipe.
- `NavisHelper.Contracts` is the shared contract assembly used by both the Navisworks plugin and the MCP server.
- `HostCommandNames`, request/response DTOs, and `DocumentCommandService` are the command layer for existing MCP commands.

This means the next step is not to build a second bridge. The work is to make UI actions and MCP tools converge on the same command services where it is practical.

## Target Shape

```text
MCP client / agent
        |
        v
NavisHelper.McpServer
        |
        v
NavisHelper.Contracts
        |
        v
Named pipe bridge
        |
        v
AgentHostService inside Navisworks
        |
        v
NavisHelper.Contracts
        |
        v
DocumentCommandService / SearchService
        |
        v
Autodesk.Navisworks.Api

NavisHelperPanel UI
        |
        v
DocumentCommandService / existing plugin commands
        |
        v
Autodesk.Navisworks.Api
```

The UI should not call the named pipe. It is already in-process and should call service methods directly when a command has been moved into the shared service layer.

The MCP server should not directly open or mutate `.nwd` files. Commands that need the active document, selection, viewpoints, clashes, or model properties must execute inside the Navisworks plugin process.

## Naming Rules

MCP host commands use stable snake_case names from `HostCommandNames`.

Use names such as:

- `selection_copy_names`
- `selection_property_report`
- `selection_export_properties`
- `selection_distinct_property_values`
- `selection_color_by_property`
- `clash_list_tests`
- `clash_list_results`

Do not introduce a parallel dotted namespace such as `selection.copyNames`.

## Command Availability

Commands are MCP-accessible when they are exposed through:

- `HostCommandNames`
- `AgentHostService.HandleRequest`
- `HostBridgeClient`
- one of the thematic MCP tool containers registered in `Program.cs`:
  `NavisworksTools`, `NavisworksStartupTools`, `NavisworksSelectionReportTools`,
  `NavisworksClashTools`, or `NavisworksScenarioTools`

The tool containers share `NavisworksToolContext` and `NavisworksToolBase`. Keep a
new tool in the container that owns its feature family; do not grow a new partial
of the general container.

Commands that only exist in `NavisHelperPanel` remain UI-only.

Avoid adding `[McpSafe]` or `[UiOnly]` attributes for now. The dispatch surface is the source of truth.

## MCP-Safe Now

These commands are good candidates for MCP because they are read-only or have bounded, explicit side effects:

- `selection_copy_names`
- `selection_property_report`
- `selection_export_properties`
- `selection_distinct_property_values`
- `selection_color_by_property`
- `clash_list_tests`
- `clash_list_results`
- `clash_generate_report`
- `clash_save_viewpoints`
- `clash_bbox_pair_plan`
- `clash_pair_tests_create`
- `clash_create_matrix_from_selection`
- `clash_manage_tests`
- `clash_report_status`
- `cancel_clash_report`
- `selected_items_preview`
- `selected_items_ancestry`
- `selected_items_tree`
- `selection_status`
- `item_properties_by_handle`
- `list_saved_viewpoints`
- `saved_viewpoints_export`
- `saved_viewpoints_manage`
- `saved_viewpoints_reorder`
- `list_selection_sets`
- `activate_saved_viewpoint`
- `select_selection_set`
- `create_viewpoint`
- `zoom_to_selection`
- `focus_on_selection`
- `fit_all`

## MCP-Safe After Parameterization

These are useful, but should be exposed only after replacing UI dialogs, MessageBox-only flows, or implicit file choices with explicit request DTO fields:

- `clash_export_bcf`
- `clash_assign_to`
- `colors_by_property`
- `colors_by_name`
- `view_markup`
- `view_top_section`
- `view_bounding_rect`

## UI-Only For Now

Keep these in the WPF panel until they have a clear non-interactive contract:

- About and Dev menu actions.
- Commands that require interactive picking in the 3D viewport.
- Commands that open file dialogs without parameters.
- Commands whose only output is a MessageBox.

## Implementation Rule

For each new MCP command:

1. Add request/response DTOs in `NavisHelper.Contracts/HostContracts.cs`.
2. Add a command name in `NavisHelper.Contracts/Statuses.cs` / `HostCommandNames`.
3. Add implementation to `DocumentCommandService` or `SearchService`.
4. Add dispatch in `AgentHostService.HandleRequest`.
5. Add bridge method in `HostBridgeClient`.
6. Add a public MCP tool in the appropriate thematic tool container registered in
   `Program.cs`.
7. Update docs or smoke scripts when the command becomes part of the supported surface.

For UI convergence:

1. Do not mass-rewrite the panel.
2. Pick one command at a time.
3. Move the Navisworks API logic into `DocumentCommandService`.
4. Keep UI-specific work in the panel: labels, status text, file dialogs, clipboard, MessageBox.
5. Verify the same handler is reachable from UI and MCP.

## Completed First Milestone

The first split milestone is complete:

- Add `selection_copy_names` end-to-end.
- Add `clash_list_tests` end-to-end as the first Clash Detective read-only command.
- Add `clash_list_results` end-to-end as the first Clash Detective result listing command.
- Add `clash_generate_report` end-to-end as the first write-capable Clash Report workflow with dry-run/apply behavior and external report artifacts.
- Add `selection_property_report` end-to-end as the first structured report command.
- Add `selection_export_properties`, `selection_distinct_property_values`, and `selection_color_by_property` as practical reporting/color automation commands.
- Use these commands as patterns for future selection/reporting commands.
