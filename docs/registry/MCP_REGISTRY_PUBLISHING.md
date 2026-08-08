# Official MCP Registry publishing status

Status date: 2026-08-08.

`server.json` is intentionally metadata-only. It describes the latest existing
NavisHelper release, `2.8.9.0`, but does not claim that the current installer or
ZIP is an MCP Registry package.

## Official sources

The Registry sources were retrieved on 2026-08-08 from
[`modelcontextprotocol/registry` commit `f36b7dd`](https://github.com/modelcontextprotocol/registry/tree/f36b7dd4afe2d540a4ceb9b64d3627085bf5db03)
and from the official publisher
[`v1.8.1`](https://github.com/modelcontextprotocol/registry/releases/tag/v1.8.1),
published on 2026-08-06.

- The current schema is
  [`2025-12-11/server.schema.json`](https://static.modelcontextprotocol.io/schemas/2025-12-11/server.schema.json).
- The official Registry accepts local packages from npm, PyPI, Cargo, NuGet,
  OCI, or MCPB. Its
  [package-type guide](https://github.com/modelcontextprotocol/registry/blob/f36b7dd4afe2d540a4ceb9b64d3627085bf5db03/docs/modelcontextprotocol-io/package-types.mdx)
  defines the ownership requirements for each type.
- The
  [official Registry requirements](https://github.com/modelcontextprotocol/registry/blob/f36b7dd4afe2d540a4ceb9b64d3627085bf5db03/docs/reference/server-json/official-registry-requirements.md)
  allow MCPB artifacts only from GitHub or GitLab releases. An MCPB entry must
  use an MCP-containing URL and provide `fileSha256`.
- The
  [generic `server.json` specification](https://github.com/modelcontextprotocol/registry/blob/f36b7dd4afe2d540a4ceb9b64d3627085bf5db03/docs/reference/server-json/generic-server-json.md)
  permits metadata-only records: `packages` and `remotes` are optional.

## Inspected NavisHelper distribution

The existing public release is
[`v2.8.9.0`](https://github.com/mikhalchankasm/NavisWorksMaster/releases/tag/v2.8.9.0),
published on 2026-07-29. It contains:

- `NavisHelperSetup-2.8.9.0.exe`;
- `NavisHelper-full-win-x64-framework-dependent-installer-source.zip`;
- `SHA256SUMS-v2.8.9.0.txt`.

The repository packaging scripts build a Windows x64, framework-dependent
`.NET 9` stdio MCP executable and install it together with the Navisworks
ApplicationPlugin bundle. The bundle supports Autodesk Navisworks Manage
2024, 2025, 2026, and 2027. Neither existing release asset is an npm, PyPI,
Cargo, NuGet, OCI, or MCPB package, and NavisHelper has no hosted HTTP/SSE MCP
endpoint.

Consequently, `server.json` omits both `packages` and `remotes`. Labeling the
existing ZIP or installer as `registryType: "mcpb"` would misrepresent the
artifact even if a superficial URL check passed.

## Manifest fields

- Name: `io.github.mikhalchankasm/navishelper`.
- Version: `2.8.9.0`; no unreleased version is referenced.
- Repository: the public GitHub repository, immutable GitHub repository ID
  `1315141444`, with MCP source under `NavisHelper.McpServer`.
- Website: the existing `v2.8.9.0` release page.
- Platform requirements: Windows x64, Navisworks Manage 2024-2027, and .NET 9
  Runtime for the framework-dependent package. These are publisher-provided
  metadata because the base schema has no platform-requirements field.
- Package/remote: omitted because no supported public artifact or remote
  transport currently exists.

## Publication blocker and minimum packaging change

Registry publication was not attempted. The current GitHub ZIP/installer is
not one of the official Registry package types, so package ownership cannot be
validated through the Registry's supported mechanisms. Publishing a
metadata-only entry would expose a server that clients cannot install or
connect to.

The smallest compatible future release change is to create a genuine MCPB
artifact for the Windows x64 stdio server, attach it to a GitHub release under
an MCP-containing filename, and record its lowercase SHA-256 in `fileSha256`.
The manifest may then add a `registryType: "mcpb"` package with `stdio`
transport. This requires release/package work and a new published artifact, so
it is outside this change.

## Validation record

Validation was performed with the official `mcp-publisher` `v1.8.1` Windows
amd64 release against `https://registry.modelcontextprotocol.io` on
2026-08-08. The downloaded publisher archive matched the release digest
`399ad0d6e00a50812b563a71d8bfbff5160c085e6b13aac6ec083d98d5ff7c45`.

```text
2026/08/08 11:33:07 mcp-publisher 1.8.1 (commit: f52dc8525a441a3abf5fedc9912152d95af5aab1, built: 2026-08-06T23:36:13Z)
mcp-publisher --version exit code: 0
Validating against https://registry.modelcontextprotocol.io...
✅ server.json is valid
mcp-publisher validate server.json exit code: 0
```
