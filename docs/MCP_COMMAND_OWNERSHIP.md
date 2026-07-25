# NavisHelper MCP Command Ownership

This document fixes the boundary between the WPF form, the in-process Navisworks host, and the standalone MCP server.

## Rule

Every command that can be useful without the NavisHelper WPF form should exist behind the host command contract first:

`MCP tool -> HostBridgeClient -> NavisHelper.Contracts -> AgentHostService -> DocumentCommandService/search service -> Navisworks API`

The WPF form may continue to call plugins and UI-specific handlers directly. Over time, duplicated form logic should be moved behind the same host-side services when it is useful for MCP or automation.

## Current Shared/MCP Commands

These commands already have stable MCP contracts:

- `host_status`
- `selection_status`
- `selection_copy_names`
- `selection_property_report`
- `selection_export_properties`
- `selection_distinct_property_values`
- `selection_color_by_property`
- `dump_subtree_names`
- `start_subtree_names_dump`
- `dump_subtree_names_status`
- `cancel_subtree_names_dump`
- `selected_items_preview`
- `selected_items_ancestry`
- `selected_items_tree`
- `item_properties_by_handle`
- `find_items`
- `find_items_by_bbox`
- `find_root_items_by_name`
- `list_root_items`
- `list_saved_viewpoints`
- `saved_viewpoints_export`
- `saved_viewpoints_manage`
- `saved_viewpoints_reorder`
- `list_selection_sets`
- `select_items`
- `select_selection_set`
- `create_search_set`
- `selection_sets_manage`
- `selection_sets_reorder`
- `activate_saved_viewpoint`
- `hide_unselected`
- `hide_selected`
- `unhide_selected`
- `reveal_selected`
- `isolate_selected`
- `show_all`
- `create_selection_set`
- `create_viewpoint`
- `markup_selection`
- `current_viewpoint_info`
- `zoom_to_selection`
- `focus_on_selection`
- `fit_all`
- `clash_list_tests`
- `clash_list_results`
- `clash_generate_report`
- `clash_report_status`
- `cancel_clash_report`
- `clash_manage_tests`
- `clash_bbox_pair_plan`
- `clash_pair_tests_create`
- `clash_create_matrix_from_selection`
- `clash_save_viewpoints`

## Next Common-Layer Candidates

These should be implemented in the host/common layer before exposing them in MCP or reusing them from WPF:

- `clash_assign_to`: explicit `testHandle`/`resultHandle`, `assignee`, dry-run/apply.
- `clash_export_bcf`: explicit output directory/file, selected/all results mode, `testHandle`/`resultHandle` support, no SaveFileDialog.
- `selection_export_properties_excel`: covered by `selection_export_properties` with `format=xlsx`.
- `color_by_property`: selection-scoped implementation exists as `selection_color_by_property`; future work can add all-model scope after stronger safeguards.
- `colors_by_name`: explicit source file path, dry-run/apply summary.
- `ai_color_selection`: explicit scheme/model options and API config status checks.

## UI-Only for Now

These remain form concerns unless a concrete automation scenario appears:

- Command palette visual behavior and hotkeys.
- Tab layout and overflow menu.
- Color history list UI.
- Dev scripts menu.
- About dialog.
- Manual folder/file pickers.

## Split Status

The first physical split is complete:

- `NavisHelper.Contracts`: shared DTOs, statuses, command names, and error codes used by both the Navisworks plugin and `NavisHelper.McpServer`.

The remaining intended end state is:

- `NavisHelper.Core`: pure parsing, report shaping, color logic, DTO helpers.
- `NavisHelper.NavisworksHost`: Navisworks API adapter and named-pipe host.
- `NavisHelper.UI`: WPF panel, palette, tabs, user workflows.
- `NavisHelper.McpServer`: standalone MCP server that talks to the host.

The safe migration path remains command-by-command: move behavior into common/host services only when it becomes useful for MCP or automation.
