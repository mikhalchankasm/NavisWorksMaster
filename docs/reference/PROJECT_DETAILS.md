# NavisHelper: project details

[Русский](PROJECT_DETAILS.ru.md) | **English** · Back to the [main page](../../README.md)

This page keeps the detailed product description, the MCP comparison and the last verification snapshot that used to live on the main page.

## Requirements

| Requirement | Current scope |
|---|---|
| Host | Autodesk Navisworks Manage 2024, 2025, 2026, or 2027 on Windows x64. |
| Runtime | .NET 9 Runtime for the framework-dependent MCP server included by the current package. |
| Installation | Current-user profile; administrator rights are not required for the packaged installer. |
| MCP client | A client that can start a local stdio MCP server. The packaged configurator supports the clients listed in the distribution guide. |
| Active work | Most model tools require a running Navisworks host and an open model; view tools also require an active view. |
| External AI | Not required for MCP. The separate OpenRouter color action is optional and uses a user-provided key. |

## Product part one: plugin suite

The compiled `[Plugin]` registrations run inside Navisworks Manage and add a NavisHelper ribbon and panel. The [source inventory guard](../../scripts/check_navishelper_compile.py) checks their current surface. Main workflows include:

- model search, selection, visibility, color overrides, and CSV attribute loading;
- selection and search sets, saved viewpoints, section-box views, and persistent markups;
- property exports and model-tree name exports;
- Clash Detective test, grouping, isolation, viewpoint, screenshot, and report workflows;
- an optional OpenRouter-powered color action plus a separate local-palette action.

The bundle manifest declares Windows x64 support for Navisworks Manage **2024–2027**. It does not declare Navisworks Simulate. Autodesk's own [Navisworks system-requirements index](https://www.autodesk.com/support/technical/article/caas/sfdcarticles/sfdcarticles/System-requirements-for-Autodesk-Navisworks-products.html) covers those host releases; NavisHelper compatibility is defined by this repository's bundle and build configuration, not by Autodesk.

## Product part two: local MCP server

The MCP client starts `NavisHelper.McpServer.exe` over stdio. The server discovers the in-process Navisworks host and communicates through a local Windows named pipe; it does not expose an HTTP port.

The current tool count and how it is verified are on the [main page](../../README.md#for-developers). The [curated status tables](../NAVISWORKS_MCP_COMMAND_CATALOG.md) are a partial guide with historical validation notes and proposed names.

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

Start with the [MCP quickstart](../NAVISWORKS_MCP_QUICKSTART.md). The [client guide](../MCP_CLIENT_GUIDE.md) gives practical workflows, and the [tool contracts](../MCP_TOOL_CONTRACTS.md) document detailed inputs, outputs, limits, and error behavior.

### Example requests

See [three complete workflows (Russian)](../USER_WORKFLOWS.md) for prerequisites, tool sequences, result checks, and limits.

- “Find items whose name contains `pump`, select the matches, and frame them.”
- “Preview an export of the current selection properties to an XLSX file.”
- “List active clashes in `HVAC vs Structure`, then preview isolation of the first result.”
- “Create a saved section-box viewpoint around the current selection.”
- “Show the planned color scheme before applying any permanent overrides.”

### Codex operator skill

Copy the whole [navishelper-operator folder](../../skills/navishelper-operator) into
`$CODEX_HOME/skills`, or `%USERPROFILE%\.codex\skills` when `CODEX_HOME` is unset;
create the parent directory if needed. Back up an existing same-name folder
before replacing it. Restart Codex to discover `$navishelper-operator`.
The skill uses your existing MCP connection and works outside this checkout.

## Safety model

See [security and compatibility boundaries](../SECURITY_AND_COMPATIBILITY.md)
for local access, read-only limits, AI data flow, timeout recovery and what each
verification level proves.

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

Measured on **2026-09-22** for the `2.10.0.0` release, based on `main` commit `0d98cc8`:

- source inventory guard: passed; **215** tracked C# files, **213** real compile entries, and **2** explicit non-compile exceptions;
- MCP catalog guard: passed and covers all **104** registered tools;
- host router guard: passed; **87** command names and **80** typed routes;
- panel localization, truncation-flag documentation, agent-instruction surface, baseline-coverage arithmetic and product-version consistency guards: all passed;
- automated MCP-server test run: **1,776 passed, 0 failed, 1,776 total**;
- release build matrix: `Release2024`, `Release2025`, `Release2026`, and `Release2027` passed for x64; all 12 required bundle assemblies report version `2.10.0.0`;
- distribution validation and ZIP fresh/reinstall/legacy-`v2.6.3.0`-upgrade smoke: passed. Client MCP configs were not modified — the smoke writes only into an isolated temporary install, checked afterwards;
- latency baseline: **99 of 104** tools carry a measured number across four live windows, the remaining 5 named with a reason each;
- live rig, Navisworks Manage 2027: attaching to a ready host holding the requested file measured **66 ms** against roughly 15 500 ms for a launch; and the `find_items_by_bbox` model prune was compared against a build with pruning removed over nine queries on a document whose walk completes — identical match counts, so pruning loses nothing.

Not verified, and not claimed: runtime smoke covers Navisworks Manage 2027 only; 2024–2026 are build-validated here. No public installer asset existed at the time of these checks, so no download or SHA-256 comparison was made. See [docs/releases/v2.10.0.0.md](../releases/v2.10.0.0.md) for the full list.

That runtime evidence covers Navisworks Manage 2027 only. The 2024–2026 targets are build-validated here, not runtime-smoked. No release asset was replaced by this change.

Automated helper tests do not replace validation inside Autodesk Navisworks Manage. Most host behavior depends on the Autodesk runtime and a user-provided model.
