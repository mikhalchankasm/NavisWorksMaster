# NavisHelper MCP Distribution Plan

This document defines the practical path for giving users MCP access independently from the WPF form.

## Deliverables

The user-facing distribution should have two installable parts:

- `NavisHelper.bundle`: Autodesk ApplicationPlugin bundle loaded by Navisworks. This contains the in-process host bridge and Navisworks API access.
- `NavisHelper.Contracts.dll`: shared command contract assembly deployed both with the Navisworks bundle and with the MCP server.
- `NavisHelper.McpServer`: standalone MCP stdio server used by Claude, Codex, Cursor, or another MCP-capable agent.
- `NavisHelper.McpConfigurator`: Windows helper that detects supported MCP clients and idempotently adds/updates the `navishelper` server entry.
- `docs/prompts/SETUP_PROMPT.md` and `docs/prompts/UPDATE_PROMPT.md`: copy-ready AI-agent instructions for installing/updating the MCP server and refreshing client config.

The MCP server is not a replacement for Navisworks. It connects to a running Navisworks process through the local host bridge.

## Publish Command

Full package with bundle + MCP server:

```powershell
powershell -ExecutionPolicy Bypass -File tools\package_distribution.ps1
```

Self-contained full package:

```powershell
powershell -ExecutionPolicy Bypass -File tools\package_distribution.ps1 -SelfContained
```

Output:

- `artifacts\distribution\NavisHelper-full-win-x64-framework-dependent-<timestamp>`
- `artifacts\distribution\NavisHelper-full-win-x64-framework-dependent-<timestamp>.zip`

The ZIP package contains `Install-NavisHelperBundle.ps1`. It always installs for the current user without administrator rights:

```powershell
powershell -ExecutionPolicy Bypass -File .\Install-NavisHelperBundle.ps1 -ConfigureMcp
```

That script installs the Autodesk bundle to `%APPDATA%\Autodesk\ApplicationPlugins\NavisHelper.bundle`, copies the MCP runtime to `%LOCALAPPDATA%\NavisHelper\McpServer-<version>`, and optionally runs `McpConfigurator` against that new per-user server path. Existing stdio processes remain on their previous version until their MCP client restarts or reloads.

`package_distribution.ps1` runs `scripts\test_package_install.ps1` against the final ZIP. The smoke test expands the archive into an isolated test root and covers fresh install, same-version reinstall, and cleanup of the managed unversioned runtime left by the affected `v2.6.3.0` package. It deliberately skips while Navisworks is running, because the packaged installer must refuse bundle replacement in that state; CI runs the complete smoke test on a clean worker.

When a legacy NavisHelper bundle or installation root is present under `ProgramData` or `Program Files`, the ZIP script and EXE installer stop before copying files. NavisHelper supports per-user installation only. Remove the legacy paths from elevated PowerShell with `tools\remove_machinewide_bundle.ps1 -Force`, then run the installer again.

`-ConfigureMcp` writes MCP config for the Windows profile that runs the script.

For local developer verification after a build, use `tools\install_local_bundle.ps1` from the repository instead of manually copying files into Autodesk folders. It installs only to `%APPDATA%\Autodesk\ApplicationPlugins\NavisHelper.bundle` and does not require admin rights. Remove any legacy system-wide copy before installing.

For local MCP server verification after a build, use `tools\install_local_mcp_server.ps1`. It installs only to `%LOCALAPPDATA%\NavisHelper\McpServer-<version>` without admin rights and never stops a running `NavisHelper.McpServer.exe`; a same-version reinstall with an active process fails with an explicit restart instruction.

## Windows Installer

The first user-facing installer target is Inno Setup:

```powershell
powershell -ExecutionPolicy Bypass -File tools\build_installer.ps1 -AppVersion 2.9.0.0
```

This script:

- builds a full distribution package;
- compiles `installer\NavisHelper.iss`;
- installs the Autodesk bundle into `%APPDATA%\Autodesk\ApplicationPlugins\NavisHelper.bundle`;
- installs MCP binaries into `%LOCALAPPDATA%\NavisHelper`;
- offers an unchecked Finish-page action to configure detected MCP clients for the current user.

The plugin and MCP binaries are fully installed when that action remains unchecked. If the user selects it, the installer runs `McpConfigurator --configure --clients all` without `--create-missing`: it may create or update a config file inside each detected client's existing user directory, while missing client applications and their config roots are skipped. The same safe command is available later from the `Configure detected MCP clients` Start-menu shortcut. Creating missing client config roots requires this deliberate manual command:

```powershell
& "$env:LOCALAPPDATA\NavisHelper\McpConfigurator\NavisHelper.McpConfigurator.exe" --configure --clients all --create-missing
```

Inno Setup 6 must be installed on the release machine. Longer term, a WiX/MSI package is still the better enterprise target, but Inno is the fastest native Windows `.exe` path for the first external users.

The Inno uninstaller removes `%APPDATA%\Autodesk\ApplicationPlugins\NavisHelper.bundle` wholesale. Do not place user-edited files inside that bundle directory.

Release-quality installer requirements:

- make install/update/uninstall behavior explicit and repeatable;
- keep MCP binaries outside the Autodesk bundle so user configuration and plugin deployment stay separable;
- refuse to replace the bundle while Navisworks is running, because loaded plugin assemblies cannot be reliably refreshed in place;
- detect or clearly report missing .NET runtime prerequisites;
- validate that Navisworks 2024, 2025, 2026, and 2027 bundle contents are present when packaging a full release;
- run `McpConfigurator --detect` or equivalent validation after packaging so supported clients point at the packaged server;
- keep the installer script small enough to maintain, with product/version paths centralized in build scripts instead of duplicated across docs and Inno code.

## MCP Server Only

Framework-dependent package:

```powershell
powershell -ExecutionPolicy Bypass -File tools\publish_mcp_server.ps1
```

Self-contained package:

```powershell
powershell -ExecutionPolicy Bypass -File tools\publish_mcp_server.ps1 -SelfContained
```

Output:

- `artifacts\mcp-server\NavisHelper.McpServer-win-x64-framework-dependent`
- `artifacts\mcp-server\NavisHelper.McpServer-win-x64-framework-dependent.zip`

## Client Config

The package contains `mcp-client-config.example.json`:

```json
{
  "mcpServers": {
    "navishelper": {
      "command": "<INSTALL_DIR>\\NavisHelper.McpServer.exe",
      "args": []
    }
  }
}
```

Replace `<INSTALL_DIR>` with the real folder where the MCP server was unpacked.
When editing JSON manually, use escaped Windows backslashes, for example `C:\\Tools\\NavisHelper\\McpServer\\NavisHelper.McpServer.exe`.

The repository README also exposes Cursor/VS Code MCP install buttons. If using per-user ZIP/package install, configure clients to this server path:

`%LOCALAPPDATA%\NavisHelper\McpServer-<version>\NavisHelper.McpServer.exe`

The package should include [MCP_TOOL_CONTRACTS.md](MCP_TOOL_CONTRACTS.md) so MCP clients can inspect stable input/output fields without reading C# DTOs.

Those buttons are config shortcuts only. For a fresh machine, users should run the AI-agent install prompt or installer first so the executable and Navisworks bundle exist.

The package also contains `McpConfigurator\NavisHelper.McpConfigurator.exe`.
It supports:

- `claude-desktop`: `%APPDATA%\Claude\claude_desktop_config.json`, `mcpServers`.
- `claude-code`: native `claude mcp add --scope user`.
- `codex`: `%USERPROFILE%\.codex\config.toml`, `[mcp_servers.navishelper]`.
- `cursor`: `%USERPROFILE%\.cursor\mcp.json`, `mcpServers`.
- `opencode`: `%APPDATA%\OpenCode\opencode.json`, `mcp`.
- `kimi`: `%USERPROFILE%\.kimi-code\mcp.json`, `mcpServers`.

Usage:

```powershell
.\McpConfigurator\NavisHelper.McpConfigurator.exe --detect
.\McpConfigurator\NavisHelper.McpConfigurator.exe --configure --clients all
.\McpConfigurator\NavisHelper.McpConfigurator.exe --configure --clients all --create-missing
.\McpConfigurator\NavisHelper.McpConfigurator.exe --configure --clients claude-desktop,cursor,opencode --dry-run
```

File-based adapters skip missing client config roots by default. Pass `--create-missing` when first-run client config directories should be created for the current Windows user. File-based adapters create a `.bak_navishelper_yyyyMMdd_HHmmss_fff` backup before writing.

Most MCP clients load server definitions at process or session startup. After `McpConfigurator` changes a client config, restart the client or use its MCP reload command before expecting the `navishelper` tools to appear in a chat.

## Verification

With Navisworks running and a model open:

```powershell
python <INSTALL_DIR>\mcp_smoke_test.py --version 2027
```

The smoke test checks:

- host discovery;
- active document/root items;
- selection report;
- CSV/XLSX property export;
- distinct property values;
- color-by-property dry-run;
- clash test listing;
- MCP stdio tool listing.

## Current MCP Tool Surface

Core diagnostics:

- `list_navisworks_hosts`
- `mcp_diagnostics`
- `mcp_recent_calls`
- `mcp_error_contract`
- `mcp_health_check`
- `host_status`

Selection/reporting:

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

Model navigation:

- `active_model_context`
- `list_root_items`
- `find_root_items_by_name`
- `find_items`
- `find_items_by_bbox`
- `select_items`
- `list_saved_viewpoints`
- `saved_viewpoints_export`
- `saved_viewpoints_manage`
- `saved_viewpoints_reorder`
- `activate_saved_viewpoint`
- `list_selection_sets`
- `select_selection_set`
- `create_search_set`
- `selection_sets_manage`
- `selection_sets_reorder`
- `current_viewpoint_info`
- `zoom_to_selection`
- `focus_on_selection`
- `fit_all`

`find_items` property searches should use display category/property names with `dataType`; internal names are fallback-only. The hard per-call limit is exactly one logical query/search; run multiple targets as separate sequential `find_items` calls. For raw global-document spatial search, use `find_items_by_bbox` with `min`/`max` AABB bounds in the active document units; it is read-only and does not transform local/grid coordinates.

Visibility and saved items:

- `hide_unselected`
- `hide_selected`
- `unhide_selected`
- `reveal_selected`
- `isolate_selected`
- `show_all`
- `create_selection_set`
- `create_viewpoint`

Clash Detective:

- `clash_list_tests`
- `clash_list_results`
- `clash_bbox_pair_plan`
- `clash_pair_tests_create`
- `clash_create_matrix_from_selection`
- `clash_generate_report`
- `clash_save_viewpoints`
- `clash_report_status`
- `cancel_clash_report`
- `clash_manage_tests`

## Current Full Package Contents

The full distribution package contains:

- MCP server package;
- MCP configurator package;
- MCP config example;
- NavisHelper bundle installation instructions;
- MCP tool contracts, client guide, command catalog, quickstart, and project README under `docs`;
- versioned bundle DLLs for 2024, 2025, 2026, and 2027;
- `NavisHelper.Contracts.dll` next to every binary that depends on it;
- smoke-test instructions.
