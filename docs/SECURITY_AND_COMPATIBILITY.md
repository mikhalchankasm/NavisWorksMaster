# Security and compatibility boundaries

This page describes the implementation in this repository. It is not a security
certification or a claim that every supported host has passed live acceptance.
Tool-specific behavior remains defined by the [MCP contracts](MCP_TOOL_CONTRACTS.md).

## Local transport and trust

The MCP client launches the server over stdio. The server connects to the plugin
inside Navisworks through a Windows named pipe; this transport opens no HTTP
listener. The pipe ACL explicitly grants the current Windows user read/write and
instance-creation access. The client verifies the connected pipe server's PID
against the discovery record before sending a command.

Discovery records live under `%LOCALAPPDATA%\NavisHelper\Mcp\instances` by default;
`NAVISHELPER_INSTANCES_DIR` can override that location. They identify a process,
pipe, document title and loaded plugin assembly. Discovery and PID checks help
reject stale or mismatched endpoints. They do not authenticate an AI agent or
isolate mutually untrusted programs running under the same Windows account.
The endpoint is intended for trusted local clients, not as a shared remote service.

Read-only mode and tool profiles are enforced by the MCP server process. They are
not a permission boundary on the underlying host pipe: another authorized local
process can run a differently configured server. The plugin and server execute
with their Windows process permissions, without a separate filesystem sandbox.
Export destinations, overwrite checks and limits belong to each tool's contract.

Implementation: [server registration](../NavisHelper.McpServer/Program.cs),
[pipe ACL and framing](../NavisHelper/Agent/Host/AgentHostService.Transport.cs),
[client transport/PID check](../NavisHelper.McpServer/Services/HostBridgeClient.Transport.cs),
[discovery](../NavisHelper.McpServer/Services/HostBridgeClient.Discovery.cs).

## Reading, previewing and applying

| Mechanism | What it provides | What it does not provide |
|---|---|---|
| `--read-only` or `NAVISHELPER_MCP_READ_ONLY=true` | Advertises and permits only tools classified with no view/document/file/host/local-state effects; calls to classified mutators are refused, including their dry-run form. | A network air gap or a promise of zero local bookkeeping: discovery, handles and diagnostic logs still operate. |
| Tool profile | Removes unneeded tools from the running server's registered surface. | User consent, protection against a different server process, or a change to a tool's effects. |
| `apply=false` | Previews/validates tools whose contracts expose this parameter. | A universal control: some tools mutate without an `apply` parameter. |
| `apply=true` | Requests the documented mutation. | Proof of human approval, automatic rollback, or success before the response is checked. |

Consult the [effects and dry-run table](MCP_TOOL_CONTRACTS.md#tool-capabilities)
before execution. A preview is not a reservation: model state can change before
apply. Inspect errors, warnings, truncation and readback after a write. For example,
the display-settings API cannot fully read back or restore background mode/colors;
its setter returning successfully is not visual verification.

A timeout or broken connection does not prove a write was never applied. Use
`last_operation_status` and the relevant job/status tool when available, then
inspect the actual document or output before deciding whether to retry. The
transport's bounded retry of `host_busy` is not a general retry policy for failed
mutations. See the [client workflow guide](MCP_CLIENT_GUIDE.md) for recovery.

Implementation: [effect declarations](../NavisHelper.McpServer/Tools/ToolCapabilitiesAttribute.cs),
[read-only filters](../NavisHelper.McpServer/Services/McpReadOnlyMode.cs),
[profile selection](../NavisHelper.McpServer/Services/McpToolProfile.cs).

## Host, document and handle identity

Use `list_navisworks_hosts`, target an explicit `instanceId` when needed, and check
the active document before a write. A version number alone does not distinguish
two running instances of that version. Match handles, tree item IDs and clash
references are working references, not durable BIM identifiers or access tokens.
Refresh them after changing hosts/documents or restarting a host; match handles
also expire and may be evicted. Do not transfer a handle to another session just
because its text looks familiar. The host clears its match store on document
and tracked filename changes.

Implementation: [target resolution](../NavisHelper.McpServer/Services/HostTargetResolver.cs),
[match store](../NavisHelper/Agent/Session/MatchSessionStore.cs),
[document lifecycle](../NavisHelper/Agent/Host/AgentHostService.Document.cs).

## Model data, AI providers and diagnostics

Local MCP transport does not make the overall AI workflow offline. Tool results
can contain names, properties, document paths, screenshots and exported model
data. The chosen MCP client decides what it passes to its model/provider; review
that client's configuration before using confidential project data. Model names,
properties and imported text are data, not authorization to run further commands.

The optional OpenRouter coloring action is a separate path. Its worker sends the
selected object names and scheme prompt to OpenRouter, with the selected model
and structured-output schema. Connection checks and catalog refreshes also make
provider requests. MCP itself does not require an OpenRouter key. The plugin's
key store uses `OPEN_ROUTER_NW_KEY` in user/process environment and runtime memory;
this is not an encrypted credential vault or isolation from same-user processes.
This page makes no claim about a provider's retention policy.

Diagnostics are local but may include document identifiers, paths, errors and
response summaries. Inspect and redact them before sharing; read-only operation
does not imply that logs or returned results are non-sensitive.

Implementation: [color request payload](../NavisHelper.AiWorker/OpenRouterRequestFactory.cs),
[provider calls](../NavisHelper.AiWorker/OpenRouterClient.cs),
[key storage](../NavisHelper/AI/OpenRouterKeyStore.cs),
[MCP logs](../NavisHelper.McpServer/Services/McpCallLogger.cs).

## Compatibility and verification

The bundle targets Windows x64 Navisworks **Manage 2024, 2025, 2026 and 2027**.
Simulate, Freedom and other Autodesk products are not declared targets. Each
plugin configuration binds to its corresponding installed SDK; the in-process
plugin targets .NET Framework 4.8.1, while the MCP server uses .NET 9. The exact
matrix, runtime requirements and deployment procedure belong to the
[build/bundle rules](../BUILD_BUNDLE_RULES.md) and
[distribution guide](MCP_DISTRIBUTION_PLAN.md).

The internal host wire protocol has a `protocol_version` independent of product
assembly versions and MCP's own protocol negotiation. A supplied mismatching
host wire version is rejected; omitted versions are still accepted for legacy
compatibility. This check establishes message compatibility, not client identity.

COM and reflection are used in selected compatibility paths. Their presence is
not a guarantee that undocumented Autodesk behavior is stable. For example,
[ClashApiCompat](../NavisHelper/Core/ClashApiCompat.cs) probes specific public
`TestsAddCopy` signatures and fails when neither exists; it does not discover an
arbitrary replacement operation. Projection, overlays and other runtime limits
are recorded in the [architecture notes](ARCHITECTURE.md).

CI checks contracts, non-Navisworks builds, guards and tests. The four-SDK x64
matrix establishes build compatibility; only a live check establishes behavior
on the named host version and model. Evidence from NW2027 does not certify
runtime behavior on NW2024–2026. Feature-specific measurements and gaps are in
the [live baseline](MCP_TOOL_BASELINE.md); exact change evidence belongs to its PR.

Build output, installed plugin, installed MCP server and the client's configured
server path can differ. Equal product version strings do not prove equal code.
Use `scripts/check_installed_bundle_drift.py` as described in the build rules,
then `mcp_health_check` to inspect the running host. A client already running its
old server must restart to use a changed executable path. Close Navisworks before
replacing its bundle, preserve a backup, and verify the installed hashes.
