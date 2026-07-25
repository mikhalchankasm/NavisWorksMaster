# NavisHelper MCP Agent Setup

This guide is for using NavisHelper MCP without relying on the NavisHelper WPF form.

## Runtime Model

NavisHelper MCP has two parts:

- `NavisHelper.dll` loaded inside Autodesk Navisworks. It owns the Navisworks API access and starts the local host bridge.
- `NavisHelper.McpServer.exe` started by an MCP client or agent. It can start Navisworks through `start_navisworks`/`open_latest_navisworks_file`, then talks to the running Navisworks host through local discovery files and named pipes.

For an already open model, start Navisworks manually and let the plugin host be available first. For the common "open the last model" workflow, call `open_latest_navisworks_file`; it reads the current user's Navisworks Recent File List registry entries and waits for the NavisHelper host discovery record by default.

## Requirements

- Autodesk Navisworks Manage with the NavisHelper bundle installed.
- `.NET 9` runtime for `NavisHelper.McpServer`.
- Matching bundle DLL for the Navisworks version being used, for example `Contents/2027/NavisHelper.dll`.

## Current Local Development Server

After building:

```json
{
  "mcpServers": {
    "navishelper": {
      "command": "D:\\GitHub\\NavisWorksMaster\\NavisHelper.McpServer\\bin\\Release\\net9.0\\NavisHelper.McpServer.exe",
      "args": []
    }
  }
}
```

For a packaged user install, replace the command path with the installed `NavisHelper.McpServer.exe` path.

## Packaged User Install and Recovery

Close `Roamer.exe` before running the packaged installer:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-NavisHelperBundle.ps1 -ConfigureMcp
```

The package installs the bundle to `%APPDATA%\Autodesk\ApplicationPlugins\NavisHelper.bundle`, the MCP server to `%LOCALAPPDATA%\NavisHelper\McpServer-<version>\NavisHelper.McpServer.exe`, and the configurator to `%LOCALAPPDATA%\NavisHelper\McpConfigurator\NavisHelper.McpConfigurator.exe`. It configures missing client files when requested and then runs detection against that versioned executable. Restart or reload the MCP client after configuration.

The affected `v2.6.3.0` ZIP could leave `%LOCALAPPDATA%\NavisHelper\McpServer` without a version. Current packages remove that directory only after verifying that it is an inactive NavisHelper MCP runtime; otherwise they warn and leave it untouched. To recover manually, verify the directory contents first, remove only the confirmed stale NavisHelper runtime, then rerun the installer and:

```powershell
& "$env:LOCALAPPDATA\NavisHelper\McpConfigurator\NavisHelper.McpConfigurator.exe" --detect
```

For a private repository, authenticate with Git Credential Manager, `gh auth login`, or GitHub device flow. Never paste personal access tokens, browser cookies, or other secrets into prompts, source files, or logs.

## First Checks

After connecting an agent:

1. Call `list_navisworks_hosts`.
2. Call `host_status`.
3. Call `mcp_health_check`.
4. If multiple Navisworks windows are open, pass `instanceId` from `list_navisworks_hosts`.
5. If exactly one host of a specific version is open, `navisworksVersion` can be used, for example `2027`.

## Read-Only Workflows

Selection:

- `selection_status`
- `selection_copy_names`
- `dump_subtree_names` (`csv` or `jsonl`, hard-limited synchronous small root subtree name dump)
- `start_subtree_names_dump` / `dump_subtree_names_status` / `cancel_subtree_names_dump` (chunked large root subtree name dumps)

For large external name/position lookup dumps, prefer `includePath=false` for speed. Enable full paths only when the output needs hierarchy context.
- `selection_property_report`
- `selection_export_properties` (`csv` or `xlsx`, dry-run by default)
- `selection_distinct_property_values`
- `selection_color_by_property` (dry-run by default)
- `selected_items_preview`
- `selected_items_ancestry`
- `selected_items_tree`

Clashes:

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

Startup/timing:

- `list_recent_navisworks_files`
- `open_latest_navisworks_file`
- `start_navisworks`
- `mcp_task_timer_start` / `mcp_task_timer_finish` (optional cross-tool workflow timer; every individual MCP tool call already returns automatic `navishelper_timing`)

Model navigation/context:

- `host_status`
- `list_root_items`
- `find_root_items_by_name`
- `find_items`
- `current_viewpoint_info`
- `list_saved_viewpoints`
- `saved_viewpoints_export`
- `saved_viewpoints_manage`
- `saved_viewpoints_reorder`
- `list_selection_sets`

For `find_items` property searches, prefer display `category` + `property` names with `dataType`. `categoryInternal` and `propertyInternal` are fallback-only. Run exactly one logical query/search per call; multiple targets must be separate sequential `find_items` calls.

## View-Changing, Non-Destructive Workflows

These commands change the current Navisworks view or selection context but do not modify model data:

- `select_items`
- `select_selection_set`
- `activate_saved_viewpoint`
- `zoom_to_selection`
- `focus_on_selection`
- `fit_all`

## Write-Safe / Visibility Workflows

Commands with model side effects must keep `apply=false` by default and require explicit `apply=true`.

Examples:

- `hide_unselected`
- `hide_selected`
- `unhide_selected`
- `reveal_selected`
- `isolate_selected`
- `show_all`
- `create_selection_set`
- `create_viewpoint`
- `markup_selection`

## Troubleshooting

If the agent cannot see Navisworks:

1. Confirm Navisworks is running.
2. Confirm the NavisHelper bundle for that Navisworks version is installed.
3. Open any model and call the NavisHelper command at least once if the host was not initialized automatically.
4. Check `%LOCALAPPDATA%\\NavisHelper\\Mcp\\instances` for discovery files.
5. Call `mcp_diagnostics` and `mcp_recent_calls`.

If tools exist but calls fail:

- Use `mcp_error_contract` for stable error meanings.
- Use `mcp_recent_calls` to inspect target host, elapsed time, status, and error code.
- Restart the MCP client after rebuilding `NavisHelper.McpServer`.

## Smoke Test

Use the repository smoke test for local validation:

```powershell
python tools\mcp_smoke_test.py --version 2027 --launch --nwd-dir "<folder-with-nwd-files>"
```

If Navisworks is already running with a model:

```powershell
python tools\mcp_smoke_test.py --version 2027
```

## Packaging Target

The intended external distribution should contain:

- NavisHelper bundle for supported Navisworks versions.
- `NavisHelper.McpServer` published self-contained or with documented `.NET 9` runtime requirement.
- A short MCP client config snippet.
- A command guide generated from `MCP_COMMAND_OWNERSHIP.md`.

See `MCP_DISTRIBUTION_PLAN.md` for the concrete publish command and package layout.
