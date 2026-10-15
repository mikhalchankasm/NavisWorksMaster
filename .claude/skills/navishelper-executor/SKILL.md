---
name: navishelper-executor
description: Use for any NavisHelper code task that has an Issue brief and that the curator can delegate to an external executor (Codex, Qwen) through scripts/external_worktree_executor.py, and whenever the owner says to run, launch or delegate to Codex or the executor. Not for changes to the launcher itself, security guards, docs-only tasks or reviews through scripts/review/*, which the curator does directly.
---

# Run a NavisHelper task through the external executor

The procedure is [docs/EXECUTOR_SETUP.md](../../../docs/EXECUTOR_SETUP.md); the
launcher's contract is [docs/EXTERNAL_EXECUTORS.md](../../../docs/EXTERNAL_EXECUTORS.md).
This skill is the checklist. Read the guide's section before each step; do not
work from memory of it.

1. **Issue brief** exists and fits [TASK_BRIEF.md](../../../docs/TASK_BRIEF.md);
   no brief means the task is not ready (AGENTS.md section 2).
2. **Worktree** off `origin/main` on its own branch, outside the main checkout
   (guide section 3).
3. **Acceptance test first:** commit it, measure it failing on the unchanged tree,
   break it once on purpose to see it fire (guide section 5).
4. **Count files** against AGENTS.md section 3; a new MCP tool has a fixed list of
   guard-required files (guide section 5).
5. **Executor brief** from the launch form, at most 2048 bytes on disk, English,
   "Leave the change uncommitted" and "Do not run scripts/review/*" (guide
   section 4). Facts that do not fit go into `.avox-context-<id>.txt`.
6. **Launch** in the background with an absolute `--record` and `--timeout`;
   one MSBuild-heavy job per machine; hands off the main checkout (guide
   section 6).
7. **Read the record:** exit code first (4, 6, 5, 3 win over 1/0), then
   `hostViolations` and `host.processControl`. Exit 6 or 3: stop and report to
   the owner before anything else (guide section 6).
8. **Review and re-run** yourself: diff in scope, acceptance test untouched, new
   tests three times, whole L0 project (guide section 7).
9. **Commit, PR with provenance** (provider, CLI version, record path) and the
   measured acceptance result; `pwsh -File scripts/review/codex-review.ps1`,
   green CI, L2 for plugin code; merge under branch protection (guide section 7).
10. **Two attempts** at most. Then stop, record the blocker, do not widen the task.

Never start a provider CLI by hand or through its plugin, and never stop a
process yourself: report a lock instead.
