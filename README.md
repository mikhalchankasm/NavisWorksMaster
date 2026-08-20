# NavisHelper

[Русский](README.ru.md) | **English**

NavisHelper is a Windows plugin suite and local MCP server for Autodesk Navisworks Manage model coordination.

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

> Project snapshot: measurements and external repository metadata in this page were checked on **2026-08-08**. No project-owned demo GIF exists in the repository, its Git history, or the current release assets, so this page does not show a placeholder or another project's media.

## Install

The current [GitHub Release](https://github.com/mikhalchankasm/NavisWorksMaster/releases/latest) provides a per-user Windows installer. Close Navisworks, open PowerShell, and run these three commands in the same session:

```powershell
$release = Invoke-RestMethod `
  "https://api.github.com/repos/mikhalchankasm/NavisWorksMaster/releases/latest"
$installerAsset = $release.assets |
  Where-Object name -Like "NavisHelperSetup-*.exe" |
  Select-Object -First 1
$checksumsAsset = $release.assets |
  Where-Object name -Like "SHA256SUMS-*.txt" |
  Select-Object -First 1
if (-not $installerAsset -or -not $checksumsAsset) {
  throw "Required release assets were not found."
}
```

```powershell
$installer = Join-Path $env:TEMP $installerAsset.name
$checksums = Join-Path $env:TEMP $checksumsAsset.name
Invoke-WebRequest $installerAsset.browser_download_url -OutFile $installer
Invoke-WebRequest $checksumsAsset.browser_download_url -OutFile $checksums
$line = Get-Content $checksums |
  Where-Object { $_ -match "\*$([regex]::Escape($installerAsset.name))$" }
$expected = ($line -split "\s+")[0]
$actual = (Get-FileHash $installer -Algorithm SHA256).Hash
if (-not $expected -or $actual -ne $expected) {
  throw "Installer checksum verification failed."
}
```

```powershell
if (Get-Process Roamer -ErrorAction SilentlyContinue) {
  throw "Close Navisworks before installing."
}
Start-Process $installer -Wait
```

The installer always places the plugin and MCP binaries in the current user's profile. Its optional Finish-page action for MCP client configuration is unchecked by default. Selecting it may create or update a config file inside each detected client's existing user directory; missing client applications and their config roots are skipped. Restart only a client whose configuration was changed. For later or deliberate `--create-missing` setup, see [Client Config](docs/MCP_DISTRIBUTION_PLAN.md#client-config). Package, manual, and recovery paths are covered by the [distribution guide](docs/MCP_DISTRIBUTION_PLAN.md) and [agent setup guide](docs/MCP_AGENT_SETUP.md).

### Requirements

| Requirement | Current scope |
|---|---|
| Host | Autodesk Navisworks Manage 2024, 2025, 2026, or 2027 on Windows x64. |
| Runtime | .NET 9 Runtime for the framework-dependent MCP server included by the current package. |
| Installation | Current-user profile; administrator rights are not required for the packaged installer. |
| MCP client | A client that can start a local stdio MCP server. The packaged configurator supports the clients listed in the distribution guide. |
| Active work | Most model tools require a running Navisworks host and an open model; view tools also require an active view. |
| External AI | Not required for MCP. The separate OpenRouter color action is optional and uses a user-provided key. |

## Product part one: plugin suite

The compile inventory finds **30 compiled `[Plugin]` registrations**. They run inside Navisworks Manage and add a NavisHelper ribbon and panel. Main workflows include:

- model search, selection, visibility, color overrides, and CSV attribute loading;
- selection and search sets, saved viewpoints, section-box views, and persistent markups;
- property exports and model-tree name exports;
- Clash Detective test, grouping, isolation, viewpoint, screenshot, and report workflows;
- an optional OpenRouter-powered color action plus a separate local-palette action.

The bundle manifest declares Windows x64 support for Navisworks Manage **2024–2027**. It does not declare Navisworks Simulate. Autodesk's own [Navisworks system-requirements index](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/System-requirements-for-Autodesk-Navisworks-products.html) covers those host releases; NavisHelper compatibility is defined by this repository's bundle and build configuration, not by Autodesk.

## Product part two: local MCP server

The MCP client starts `NavisHelper.McpServer.exe` over stdio. The server discovers the in-process Navisworks host and communicates through a local Windows named pipe; it does not expose an HTTP port.

The source registers **102 distinct MCP tools**. `scripts/check_mcp_command_catalog.py` derives their snake_case names from the 102 `[McpServerTool]` methods and verifies the generated 102-row index on **2026-08-19**. The separate curated status table is a smaller guide, not the registered-tool count: it currently contains 54 `implemented`, 16 live-`validated`, 15 `planned`, and one `deprecated alias` rows.

### Tool families

| Family | Typical work |
|---|---|
| Diagnostics and host lifecycle | Discover instances, check health, inspect recent calls, start or close Navisworks. |
| Model query and hierarchy | Inspect model context, roots, children, properties, bounding boxes, and search results. |
| Selection and visibility | Select matched items, inspect selection, hide, reveal, isolate, show all, and frame a view. |
| Sets and viewpoints | Create and manage selection/search sets, saved viewpoints, folders, ordering, and activation. |
| Markup and section views | Create persistent redlines, live markers, section-box viewpoints, and captured views. |
| Reports, exports, and color | Export properties or names, summarize values, and preview/apply color rules. |
| Clash Detective | List, group, run, rename, isolate, report, export, and manage existing clash data. |
| Reusable scenarios | Validate, save, inspect, resolve, and delete reviewed multi-step workflows. |

Start with the [MCP quickstart](docs/NAVISWORKS_MCP_QUICKSTART.md). The [client guide](docs/MCP_CLIENT_GUIDE.md) gives practical workflows, and the [tool contracts](docs/MCP_TOOL_CONTRACTS.md) document detailed inputs, outputs, limits, and error behavior.

### Example requests

- “Find items whose name contains `pump`, select the matches, and frame them.”
- “Preview an export of the current selection properties to an XLSX file.”
- “List active clashes in `HVAC vs Structure`, then preview isolation of the first result.”
- “Create a saved section-box viewpoint around the current selection.”
- “Show the planned color scheme before applying any permanent overrides.”

## Safety model

- MCP traffic stays on local stdio and named pipes; the server opens no network listener.
- Navisworks API work is marshalled to the host UI thread and guarded against busy host state.
- Most mutating tools default to preview and require an explicit apply flag; close/discard paths require stronger confirmation.
- Match handles and tree item IDs are scoped to the current host, document, and session and must not be reused blindly.
- OpenRouter coloring is a separate opt-in plugin action, not an MCP dependency; it sends selected display names under the user's own key.

## MCP alternatives

This comparison covers only the MCP integration, not each project's broader UI or BIM features. Claims come from the linked repositories; stars are live GitHub metadata measured on **2026-08-08**.

| Project | Declared Navisworks scope | MCP/host design | Where it differs from NavisHelper | Stars |
|---|---|---|---|---:|
| **NavisHelper** | Manage 2024–2027 | .NET stdio server to per-process named pipe; packaged per-user installer | Curated Navisworks operations and dry-run-oriented writes; no Simulate or cross-product host support | 0 |
| [Aitology/Navisworks_MCP](https://github.com/Aitology/Navisworks_MCP) | Claims Manage and Simulate 2025–2027 | Python stdio server to a localhost HTTP add-in | Supports Simulate, which NavisHelper does not; NavisHelper supports 2024 and ships a combined installer | 14 |
| [General-Soju/BimOnMcp](https://github.com/General-Soju/BimOnMcp) | Claims Navisworks 2025–2027 alongside Revit and AutoCAD | Self-contained stdio bridge to per-process named pipes | Covers multiple Autodesk hosts and exposes script execution; NavisHelper instead provides a larger curated Navisworks-specific tool surface and supports 2024 | 8 |

Primary comparison sources: the [Aitology architecture, prerequisites, and tool list](https://github.com/Aitology/Navisworks_MCP#readme) and the [BimOn supported-version, architecture, and MCP-tool tables](https://github.com/General-Soju/BimOnMcp#readme). Source inspection found **39** `Tool(name=...)` declarations in Aitology's default branch although its README heading says **40**; this page therefore does not use that heading as a verified tool count. BimOn's source and README separate **11** Navisworks-specific tools from **6** common script tools.

## Verification snapshot

Measured on **2026-08-09** against this change, based on `main` commit `b54f8e3`:

- source inventory guard: passed; **207** tracked C# files, **205** real compile entries, and **2** explicit non-compile exceptions;
- MCP catalog guard: passed and covers all **100** registered tools;
- host router guard: passed; **83** command names and **76** typed routes;
- automated MCP-server test run: the `main` baseline is **1,305 passed, 0 failed, 1,305 total** after the newline-sensitive source-structure fix; this change is **1,308 passed, 0 failed, 1,308 total** after adding three installer-semantics regressions;
- release build matrix: `Release2024`, `Release2025`, `Release2026`, and `Release2027` passed for x64; all 12 required bundle assemblies report version `2.9.0.0`;
- distribution validation, ZIP fresh/reinstall/legacy-upgrade smoke, Inno Setup compilation, and isolated installer bundle-upgrade smoke: passed;
- public `v2.9.0.0` installer download: SHA-256 matched both published checksum files and the GitHub asset digest; installation with MCP configuration unchecked preserved five sampled client config hashes, and the inspected installed NavisHelper bundle/MCP assemblies were version `2.9.0.0`;
- public-install live smoke in Navisworks Manage 2027: the user confirmed the NavisHelper ribbon and panel; active-model context and representative subtree, property-report, distinct-value, color-preview, and temporary CSV/XLSX export operations passed; all 100 MCP tools and the required subset were present; the test model contained no clash tests in scope.

That runtime evidence covers Navisworks Manage 2027 only. The 2024–2026 targets are build-validated here, not runtime-smoked. No release asset was replaced by this change.

Automated helper tests do not replace validation inside Autodesk Navisworks Manage. Most host behavior depends on the Autodesk runtime and a user-provided model.

## Documentation

| Topic | Document |
|---|---|
| First MCP session | [Navisworks MCP quickstart](docs/NAVISWORKS_MCP_QUICKSTART.md) |
| Client setup and workflows | [MCP client guide](docs/MCP_CLIENT_GUIDE.md) |
| Tool inputs and outputs | [MCP tool contracts](docs/MCP_TOOL_CONTRACTS.md) |
| Architecture and extension points | [MCP architecture](docs/MCP_ARCHITECTURE.md) |
| Packaging and installation | [MCP distribution plan](docs/MCP_DISTRIBUTION_PLAN.md) |
| Building and contributing | [Contributing](CONTRIBUTING.md) and [build/bundle rules](BUILD_BUNDLE_RULES.md) |
| Full previous README | [Archived English reference](docs/reference/README_FULL.md) |
| Full previous Russian README | [Archived Russian reference](docs/reference/README_FULL.ru.md) |

## Project status and license

NavisHelper is maintained at low activity. Pull requests are welcome; responses to issues are not guaranteed.

The code is available under the [MIT License](LICENSE). Third-party notices are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Autodesk notice

Autodesk and Navisworks are registered trademarks or trademarks of Autodesk, Inc. and/or its subsidiaries and/or affiliates. NavisHelper is an independent project and is not affiliated with, authorized by, endorsed by, sponsored by, or otherwise approved by Autodesk, Inc. See Autodesk's [trademark list](https://www.autodesk.com/company/legal-notices-trademarks/intellectual-property/trademarks) and [guidelines for compatible products](https://www.autodesk.com/company/legal-notices-trademarks/trademarks/guidelines-for-use).
