# CLAUDE.md

Read [AGENTS.md](AGENTS.md). It is the only document you must read before a task,
and it applies to every agent regardless of tool.

This file used to carry its own copy of the project overview, build commands and
architecture notes. That copy drifted out of step with `AGENTS.md` — it still
described a retired `ShortestDistanceMarker` workflow as current — so the content
now lives in one place each:

| What | Where |
| --- | --- |
| process, safety boundary, verification levels | [AGENTS.md](AGENTS.md) |
| build configurations, bundle layout, local install | [BUILD_BUNDLE_RULES.md](BUILD_BUNDLE_RULES.md) |
| architecture, Navisworks API gotchas, redline JSON, projection | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) |

Do not reintroduce copies of that material here. `scripts/check_agent_docs.py`
fails the build if this file grows past its limit or starts duplicating the
sections that belong to the documents above.
