# NavisHelper Agent Instructions

The only document you must read before a task. Every agent reads this file,
whichever tool it runs in.

## 1. Safety boundary

Branch discipline:

- One new task branch per task, created from the current `origin/main`.
- Direct development on `main` and direct push to `main` are prohibited.
- Merge under branch protection; bypassing it is forbidden.

The live Navisworks install:

- `%APPDATA%\Autodesk\ApplicationPlugins\NavisHelper.bundle` is the owner's
  working installation, not a build output. Ask before writing into it, back up
  what you replace, and restore it when the check is done.
What `.claude/settings.json` can and cannot do:

- Permission patterns match a **command prefix**. The file therefore cannot
  enumerate every spelling of a dangerous command, and it is defence in depth, not
  the boundary. The boundary is this section.
- It denies the live smoke, stress, soak and installer scripts both as a bare path
  and in the `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\...` form
  the docs prescribe. Another launcher, or another flag order, will not match.
- It cannot recognize a copy into the live bundle path at all, nor every refspec
  that updates `main`.
- What is actually enforced is the other side: no interpreter — `powershell`,
  `pwsh`, `cmd`, `bash -c` — and no live script may ever appear on the allowlist,
  so such a command always stops and asks. `scripts/check_agent_docs.py` fails the
  build if one does.

So: a live run, a push to `main`, and a write into the live bundle need the owner's
explicit go-ahead whatever the spelling. Do not read silence from the allowlist as
permission.

Structural ratchets:

- A new feature family must not be introduced as another partial of
  `NavisHelperPanel`, `DocumentCommandService`, or an MCP tool container.
  New behavior gets its own type; existing partial files may be changed only
  when maintaining their existing feature family.
- Do not raise `scripts/navishelper_partial_limits.txt` merely to make a new
  partial compile. A deliberate boundary migration must reduce or preserve the
  reviewed count and explain why. The automated partial-count ratchet currently
  covers the compatibility god-classes listed in that file; the separate-type
  rule for MCP tool containers is enforced by review plus MCP catalog/stdin
  smoke checks.
- Every compiled class derived from `AddInPlugin` must either have an active
  `[Plugin]` attribute or be referenced from another compiled source file.
  `scripts/check_navishelper_compile.py` enforces this reachability guard.

External Claude Code review. Before release prep, before risky
parser/diagnostic/UX changes, and whenever the user asks for an external review,
run a mandatory read-only Claude Code review. Prefer the installed Codex slash
command `/claude-review`.

- Prefer the repository wrapper `scripts/review/claude-review.ps1` when it exists. It keeps the review tool-less and avoids accidentally using `ANTHROPIC_API_KEY` instead of the local Claude Code subscription/session.
- If invoking Claude manually without the wrapper, collect the necessary repository context first and pipe that plain text bundle to Claude. Do not ask Claude to inspect the workspace itself.
- The review must be tool-less:
  `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/review/claude-review.ps1`
- Do not use `ANTHROPIC_API_KEY`.
- Do not pass `--bare`; local Claude Code is expected to work through the user's subscription/session.
- Do not allow Claude to edit files, run tools/commands, commit, push, publish, or perform release actions.
- Do not set a low `--max-budget-usd`; Claude Code may reject even short prompts.
- If Claude requests or implies a tool call, treat the external review as failed and continue with a Codex-only review.
- Treat Claude's output as review input. Apply fixes yourself after evaluating findings.
- Reverse direction, when Claude is the lead: `scripts/review/codex-review.ps1` -Base main. It pipes a locally built diff to Codex -- Codex inspects no workspace, runs no command and reaches no network; findings applied by the lead.

Documentation language policy:

- Neutral UI resources are written in English; matching values in
  `Properties/Resources.ru.resx` are written in Russian.
- New hard-coded user-facing UI strings are prohibited except for invariant
  product names and technical identifiers.
- MCP, protocol, and agent contracts keep their existing language and are not
  changed as part of UI localization work.
- Code comments, MCP tool descriptions, `docs/MCP_*`, and new agent-facing
  documents are written in English by default.
- Existing documents do not need to be translated in bulk. User-facing README
  sections, prompts, release notes, and end-user workflow documentation remain
  Russian unless an existing file clearly uses English.

## 2. What to read before a task

Only these:

1. this file;
2. the task brief in the Issue (template: `docs/TASK_BRIEF.md`, max 2 KB);
3. the source files you are changing.

No brief means the task is not ready: return it and say what is missing.

**If the brief cannot hold, stop and say so instead of working around it.** A brief
that contradicts itself — move this file, do not touch tests, keep CI green, while
eight tests hard-code the path — is not a puzzle to solve quietly. Escalate it.
Disclose your own shortcuts the same way: state what you did, that it is a
shortcut, and how to close it.

Read one of these only when you are changing what it describes, never as
background:

| Area | Document |
| --- | --- |
| build configurations, SDK version bindings, bundle deployment | [BUILD_BUNDLE_RULES.md](BUILD_BUNDLE_RULES.md) |
| plugin/MCP architecture, Navisworks API gotchas, redline JSON, projection | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) |
| MCP tool input/output fields | [docs/MCP_TOOL_CONTRACTS.md](docs/MCP_TOOL_CONTRACTS.md) |
| how an MCP client should drive the tools | [docs/MCP_CLIENT_GUIDE.md](docs/MCP_CLIENT_GUIDE.md) |

## 3. Running a task

- One task = one branch = one PR = one session. When it ends, close the session.
- At most three Issue comments: taken / result with evidence / blocker.
  Work-in-progress lives in the PR description and is overwritten, not appended.
- Two attempts: implementation plus one correction pass. No result after that —
  stop, return the card, record the blocker. Do not open a third PR.
- Timebox: 2 hours or 10M tokens. Past that, stop and ask.
- PR size, two numbers rather than one, because a modification and a new file do
  not cost the same to review:
  - changes to existing code: at most 400 changed lines and 10 files;
  - new, self-contained code shipped with its tests: at most 800 lines and 5 files;
  - a move-only refactor whose equivalence is proven by a mechanical criterion
    stated in its brief, which must check that the goal was reached and not only
    that nothing broke.

  Do not reach for the larger number because the smaller one is inconvenient. Use
  it only when you can say what makes that review cheaper.
- Files over 80 KB: targeted patches only, never a whole rewrite. Today these are
  `NavisHelper/Properties/Resources*.resx`,
  `NavisHelper.McpServer/Services/ScenarioLibraryService.cs` and the large
  `docs/` files.

## 4. Verification levels

| Level | When | What |
| --- | --- | --- |
| L0 | while working | `dotnet test NavisHelper.McpServer.Tests/NavisHelper.McpServer.Tests.csproj -c Release --filter <the affected area>` |
| L1 | before the PR | nothing locally — CI runs the `scripts/check_*.py` guards, the non-Navisworks builds and the full test suite |
| L2 | before merge | once, on the final SHA: the full plugin matrix `Release2024`/`Release2025`/`Release2026`/`Release2027` at `-p:Platform=x64`, which CI cannot run because runners have no Navisworks SDK |
| L3 | live Navisworks | only in an agreed window, batched across tasks; `scripts/check_installed_bundle_drift.py` first |

Never repeat a level on an unchanged SHA to get a different result.

Most Navisworks-plugin behavior can only be confirmed at L3, because it depends
on the Autodesk runtime. A change that claims a runtime effect and has not
reached L3 says so in the PR.

## 5. Where things live

Task and acceptance criteria — its Issue. Queue — the Project board. Diff and
review — its PR. Durable decisions — the owning doc under `docs/`.

No second backlog, no local status log, no chat archive. A fact belongs to
exactly one document; if you find the same fact in two, delete one and link it.

## 6. Roles

The owner assigns the lead and accepts the result. External agents are read-only
unless the owner names them executors, each on its own task branch.
A same-account review of one's own work is a report, not an approval.
