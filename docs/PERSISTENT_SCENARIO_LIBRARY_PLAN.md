# Persistent User Scenario Library Plan

## Status

Implemented as an MCP-server-only follow-up after `v2.7.0.0`. The implementation adds `list_scenarios`, `get_scenario`, `save_scenario`, `delete_scenario`, and `resolve_scenario` without changing the Navisworks host wire protocol, existing tool names, or plugin IDs. Export/import remains a later slice. The detailed schema, tool, migration, and authorization contract is in [PERSISTENT_SCENARIO_LIBRARY_CONTRACT.md](PERSISTENT_SCENARIO_LIBRARY_CONTRACT.md).

## Goal

Let a user explicitly save a useful long-running NavisHelper workflow as a reusable scenario. On a later task, the assistant may suggest a compatible saved scenario before planning work, but it must not execute the scenario automatically.

## Storage boundary

- Store scenarios in `%APPDATA%\NavisHelper\Scenarios`, outside `NavisHelper.bundle`, `%LOCALAPPDATA%\NavisHelper\McpServer-<version>`, and installer-owned directories.
- The installer and uninstaller must never delete this directory.
- A normal product reinstall therefore preserves the scenarios for the same Windows user profile. Export/import remains the explicit backup and migration mechanism.
- Store one UTF-8 JSON document per scenario and use atomic replacement for writes.

## Explicit consent and privacy

- The assistant may offer saving only after a meaningful task completes, for example a multi-step selection, report, export, or review workflow.
- The user chooses the name, confirms the saved parameters, and may decline without creating any file.
- Never persist model item handles, open-document IDs, raw MCP transcripts, file contents, API keys, tokens, client configuration, or machine-specific absolute paths unless a path is deliberately represented as a reviewed variable.
- A saved scenario is an intent/template, not an audit log and not autonomous automation.

## Proposed data model

Each scenario should have a stable `schemaVersion`, a display name, optional Russian description, creation/update timestamps, safe model-context hints, parameter definitions, and ordered declarative steps. A step names an allowed operation and references declared parameters; it must not embed arbitrary executable code.

Model-context hints may include Navisworks version, a root/source-file pattern, and optional user-provided project label. Matching is advisory: a scenario with weak or mismatched context may be shown, but requires a stronger confirmation before execution.

Portable paths use named variables such as `${projectRoot}` or `${reportDirectory}`. The user supplies or confirms their values for each run.

## Proposed interaction

1. At the start of a related task, read only scenario metadata and offer at most a bounded number of compatible suggestions.
2. If the user accepts, show the resolved plan and all write operations before execution.
3. Execute each existing MCP operation through its normal validation, dry-run, confirmation, and audit paths.
4. After a successful long task, offer an explicit “save as scenario” action with a redacted preview.

The user may instead save a reviewed workflow as an exact-replay scenario. A later direct command to replay that uniquely named scenario runs the same saved steps and settings without follow-up questions when strict context, tool-contract, path, canonical-plan-fingerprint, and saved safety-envelope checks succeed. Otherwise it stops and reports the blocker; it never silently falls back to template mode.

## Protocol boundary

The reviewed design adds only the five scenario-library MCP tools listed above. It does not add `run_scenario`, change existing tool names/contracts, or change the plugin/host wire protocol. Exact replay is resolved into ordered calls to existing tools; the MCP client follows the returned `agentInstruction` and all existing preview/apply safety paths.
