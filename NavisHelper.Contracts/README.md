# NavisHelper.Contracts contribution boundary

`NavisHelper.Contracts` is the existing dependency-free shared core used by the
Navisworks plugin, MCP server, configurator, and automated tests. Its assembly
name is retained for deployment and compatibility; the project is not limited
to serialization DTOs.

Files belong here only when they:

- target `netstandard2.0` and do not reference Navisworks, WPF, WinForms, MCP
  transport, filesystem/process adapters, or another project;
- define wire DTOs/constants or deterministic shared rules, parsers, formatters,
  policies, and state machines that can be tested without Autodesk runtime;
- have a concrete consumer outside the implementation file and a focused test
  boundary when they contain behavior;
- preserve existing wire names, JSON field semantics, and assembly versioning.

Do not add Navisworks document mutations, UI lifecycle, named-pipe transport,
deployment logic, long-lived mutable coordinators that own active runtime
operations, or helpers created only to make the directory tree look symmetrical.
Pure deterministic transition rules may still live here; the runtime object
that owns mutable operation identity and lifecycle belongs to its host project.
A separate `NavisHelper.Domain` project requires the evidence and deployment
criteria recorded in `docs/ADR-0001-no-new-domain.md`.
