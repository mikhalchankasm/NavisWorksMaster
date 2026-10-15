# Development cycle

How a feature goes from the owner's request to a release, with the owner touching
it as little as possible. Each stage links the document that owns its rules; this
page adds only the owner's touchpoints and the pre-release tech-debt pass.

## Stages

| # | Stage | What happens | Owning rules |
| --- | --- | --- | --- |
| 1 | Intake | The owner states the need in plain words. The curator writes the Issue brief and asks only what is genuinely the owner's decision. | [AGENTS.md](../AGENTS.md) §2, [TASK_BRIEF.md](TASK_BRIEF.md) |
| 2 | Plan | A design note under `docs/` when the change is not obvious; the split into PRs is decided against the size limits. | [AGENTS.md](../AGENTS.md) §3 |
| 3 | Implement | Code goes to the external executor; the curator writes only security-relevant code (launcher, allowlist, guards) and what is cheaper than delegating. | [EXECUTOR_SETUP.md](EXECUTOR_SETUP.md) |
| 4 | Verify | Acceptance test, L0, external review (`scripts/review/*`), L1 in CI, L2 on the final SHA for plugin code. | [AGENTS.md](../AGENTS.md) §1, §4 |
| 5 | Merge | Green CI on the final SHA, then the curator merges under branch protection. Stacked PRs: merge the lower one without deleting its branch, retarget the next to `main`, merge `main` into it. | [EXECUTOR_SETUP.md](EXECUTOR_SETUP.md) §7 |
| 6 | Live acceptance (L3) | Batched across tasks in a window the owner agrees to; the latest validated `main` may be installed without asking again. | [AGENTS.md](../AGENTS.md) §1, §4 |
| 7 | Clean up | Archive finished sessions; `python scripts/cleanup_merged_work.py`, then `--apply` (and `--keep <path>` for a release's source worktree). | [cleanup_merged_work.py](../scripts/cleanup_merged_work.py) |
| 8 | Tech-debt pass | Before every release; see below. | this page |
| 9 | Release | Version bump and notes, tool-less external review, assets from the merge commit, re-acceptance of the published EXE, then publishing on the owner's word. | [releases/TEMPLATE.md](releases/TEMPLATE.md), the release record Issue (for example #296) |

## The owner's touchpoints

| When | The owner | Never needed |
| --- | --- | --- |
| Intake and plan | states the need, answers the decisions the curator puts as questions | writing briefs or code |
| Live windows | agrees to a window when Navisworks must run | starting or closing Navisworks, installing builds |
| Release | says "публикуй" and approves the one `gh release create` prompt | building, hashing or uploading assets |
| Claude Desktop | installs a new `.mcpb` from **Settings → Extensions** (the app's own UI) | — |

CI needs no message: the curator reads PR checks on its next turn. Publishing is an
`ask` rule in `.claude/settings.json` and prompts in every permission mode;
`scripts/check_agent_docs.py` fails if that rule is removed.

## Pre-release tech-debt pass

Before the release-preparation PR, the curator:

1. **Runs every guard** (`python scripts/check_*.py`, except the live
   `check_installed_bundle_drift.py`) and the full test suite; a test that failed
   once and passed alone during the cycle gets an Issue, not a rerun.
2. **Reviews open tech-debt Issues** and [TECHNICAL_DEBT_ROADMAP.md](TECHNICAL_DEBT_ROADMAP.md):
   closes what is done, re-prioritizes the rest with the owner.
3. **Looks for drift:** stale counts and size tables, dangling links, process rules
   in two documents, `docs/` that still describe a retired behaviour.
4. **Checks the executor:** a Codex CLI minor that is not yet reviewed
   ([EXECUTOR_SETUP.md](EXECUTOR_SETUP.md#upgrading-the-cli)).
5. **Cleans up** (stage 7) so the release starts from a clean `main`.

Findings become Issues with briefs; fixes that block the release go in before it,
the rest are listed in the release record.
