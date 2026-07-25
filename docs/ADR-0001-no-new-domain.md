# ADR-0001: Do not create `NavisHelper.Domain` yet

## Status

Accepted for the current technical-debt program.

## Decision

Keep pure helpers and protocol-adjacent contracts in the existing `NavisHelper.Contracts` assembly. Do not create a new `NavisHelper.Domain` project as a directory or namespace cleanup.

## Evidence

- The current Contracts assembly is already consumed by both the Navisworks plugin and the MCP server.
- The recent physical split preserved the existing namespace and assembly boundary while improving file-level navigation.
- No candidate component has yet been shown to need an independent versioning, ownership or deployment boundary.
- A new assembly would add bundle/package/load requirements for all supported Navisworks versions without changing user-visible behavior.

## Reconsideration criteria

Reconsider only when one pure component is used by at least two independent consumers, is not a wire DTO, has a stable API, and benefits from separate testing/versioning/ownership. Any reconsideration requires a dependency graph, package/install validation and the full Release2024–2027 x64 plus live Navisworks matrix.

## Explicitly out of scope

Namespace renames, mass helper relocation, MCP DTO migration and assembly splitting for aesthetic structure alone.
