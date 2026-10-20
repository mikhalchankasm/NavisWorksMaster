# Executor setup: running an external coding agent from a curator

The operating guide for the executor launcher (`scripts/external_worktree_executor.py`).
[EXTERNAL_EXECUTORS.md](EXTERNAL_EXECUTORS.md) holds the launcher's contract: what it
isolates, what the run may not change on the machine, process-stop detection, and
where the code came from. This page is the procedure around it and does not repeat
that contract. It is ported from Avox's `docs/EXECUTOR_SETUP.md` at 9e936647 and
adapted to this repository.

## 1. Purpose and routes

- The curator (Claude or Codex in the owner's session) writes the brief and the
  acceptance test, launches the executor, reviews, verifies and opens the PR. The
  executor writes the code in its own git worktree. Curator tokens go to briefs,
  criteria and review.
- The curator writes code itself only when the change is security-relevant (the
  launcher, the allowlist, safety guards), cheaper to do than to delegate, or
  blocked on something the executor cannot resolve.
- An executor holds no MCP, no live Navisworks access and no merge authority, and
  never receives licensed, customer or vendor model data.

| `--provider` | Route (`config/external-executors.json`) | Use |
| --- | --- | --- |
| `codex` | `gpt-6.1-sol`, effort `high`, `serviceTier: priority` | Main executor. The only route with a real filesystem sandbox. Its refusals often find defects in the brief. |
| `qwen` | `qwen3.8-max`, ModelStudio Token Plan | Executor. Slow, and can reach the 1800 s cap on large tasks, but its reports are unusually honest. |
| `codex-plan` | `gpt-6.1-sol`, effort `high`, `mode: advisor` | Answers questions. Needs an `advice/` branch, and the worktree is reset after the run. |
| `glm`, `glm-flash`, `claude`, `kimi` | `providers` in the config | Accepted by the launcher; not in this repository's rotation. |

## 2. Prerequisites

- Windows 10/11 (the host guard and the MSBuild cleanup are Windows-only), Python
  3.10+ (standard library only), git with worktree support, Node.js on `PATH` for
  the npm-installed CLIs.
- Sign in to each CLI once, interactively, in its normal home and outside the
  launcher. Never copy a credential store into git.
- `python -m scripts.external_worktree_executor --help` lists the accepted
  providers. There is no doctor command: the launcher validates everything before
  it starts, and a refusal (exit 2) spends no quota. Entitlement and quota are
  proven only by a real run, for example the fixture smoke in section 8.
- A CLI in a non-standard place is named by `AVOX_EXTERNAL_<PROVIDER>_CLI`, for
  example `AVOX_EXTERNAL_CODEX_CLI`; the file stem must still be the CLI's name.

### Codex CLI (`codex`, `codex-plan`)

- **Install** the npm package `@openai/codex`. The launcher finds `codex` on `PATH`
  and runs `codex.js` through `node` rather than the npm shim.
- **Auth:** `codex login` with the ChatGPT account. `%USERPROFILE%\.codex\auth.json`
  must hold `auth_mode: chatgpt`; an API-key login is refused with `AUTH_MISSING`,
  because it would bill a different account.
- **Account features are off and proven off.** Before every run the launcher runs
  `codex features list` in the isolated home and refuses (`SANDBOX_UNSAFE`) if a
  feature from `CODEX_DISABLED_FEATURES` is missing or enabled, if an enabled
  feature is outside `CODEX_REVIEWED_ENABLED_FEATURES`, or if the version is not in
  `CODEX_REVIEWED_VERSIONS` (all in `scripts/agent_home_isolation.py`).
- **Patch releases** of a reviewed minor run without a review; the feature check
  still applies to them.

### Upgrading the CLI

A new Codex minor (for example 0.162 to 0.163) stops the executor until it is
reviewed. In one PR:

1. Upgrade, then run `codex features list` with an empty `CODEX_HOME` and the
   executor switches, and compare the enabled features with the reviewed set.
2. Add any new account, browser, connector or plugin feature to
   `CODEX_DISABLED_FEATURES`, the reviewed harmless ones to
   `CODEX_REVIEWED_ENABLED_FEATURES`, and the version string to
   `CODEX_REVIEWED_VERSIONS`; update the test pins.
3. Run one probe task that asks the executor to list its MCP servers and tools.
   The expected answer is "only `clock`, no web tool".
4. Record the version and date in the list below, and take the same change from
   Avox when it already exists there (EXTERNAL_EXECUTORS.md, "Origin and updates").

Reviewed here: 0.158.0, 0.159.0, 0.159.2, 0.160.0, 0.161.0, 0.162.1 (copied
from Avox with the launcher); 0.163.0 (2026-10-20, ported from Avox `3bda6b8f`):
`code_mode_interrupt` reviewed harmless (`code_mode` stays off),
`api_key_cyber_access_programs` and `cli_daybreak` switched off.

### Qwen Code (`qwen`)

A static ModelStudio Token Plan key in `%USERPROFILE%\.qwen\settings.json`
(`security.auth.selectedType: "openai"`, `env.BAILIAN_TOKEN_PLAN_API_KEY`, the
model and exactly one `modelProviders.openai[]` route to the endpoint in the
config). The launcher rebuilds that file inside the isolated home with only the
key, the model and the route.

## 3. Prepare a run

1. **Worktree.** Off the current `origin/main`, in a folder outside the main
   checkout, on its own branch:
   `git fetch origin && git worktree add -b <provider>/<task> <path> origin/main`.
   The launcher refuses the main checkout, `main`, `master` and a detached `HEAD`.
2. **Acceptance test first.** Commit the curator-owned test on that branch before
   the launch and measure it failing on the unchanged tree (section 5).
3. **Brief.** Write it (section 4) to a scratch folder outside every checkout:
   non-empty UTF-8, at most 2048 bytes on disk. CRLF adds a byte per line.
4. **Context file.** Everything that does not fit goes into
   `.avox-context-<id>.txt` in the worktree root: issue facts, where the code is,
   exact error texts. Executors cannot read GitHub. Use synthetic or sanitized
   examples, never live model data. `.gitignore` keeps the file out of commits;
   check `git status --short` before the launch, and delete it before
   `git worktree remove`.

## 4. The executor brief

The Issue brief follows [TASK_BRIEF.md](TASK_BRIEF.md). The executor brief is its
launch form, in English (Cyrillic doubles the byte count):

```markdown
## Outcome
<One sentence: what exists after this task that does not exist now.>
## Read first
`.avox-context-<id>.txt` in the worktree root. Never commit it.
## Boundaries
- In scope: <files, types>
- Out of scope: <what not to touch or generalize into>
## Acceptance
- `<curator-owned test>` green (curator-owned, do not edit).
- `dotnet test NavisHelper.McpServer.Tests/NavisHelper.McpServer.Tests.csproj -c Release` green.
- At most <N> changed lines and <M> files.
**Measured on the unchanged tree:** <result and why it fails>
## Verify
<one filtered dotnet test command>
## Deliver
Leave the change uncommitted; the curator commits. Do not run scripts/review/*.
Print only the tails of build and test output.
If you stop, leave your edits in the tree and list what is missing.
Final message: JSON with files, tests, results, open questions.
```

- **Leave it uncommitted.** Codex cannot commit in a linked worktree
  (EXTERNAL_EXECUTORS.md, "Codex's sandbox").
- **Output tails only.** Codex's JSON stream echoes every command's output, and the
  8 MB cap ends the run with exit 125.
- **Refusals are output.** Codex stops when the acceptance cannot be measured;
  most such stops have found a defect in the brief. Fix the brief rather than
  pushing the executor through.

## 5. Acceptance and limits

- The test must fail on the unchanged tree for the reason the task exists. A
  missing member that does not compile is acceptable; a check that can never
  match is not.
- A check is good once it has rejected a plausible wrong implementation: break it
  on purpose and watch it fire.
- Assert exact values. Never depend on wall-clock timing, UI language or terminal
  width. When a test asserts on a message, quote the exact substring in the brief.
- **Count every file** against AGENTS.md section 3; executors stop at the file
  limit. A new MCP tool touches, besides its own type and contract:
  `NavisHelper.Contracts/Statuses.cs` and `HostCommandEffects.cs`,
  `AgentHostService.CommandRouter.cs`, `NavisHelper.csproj`, a `HostBridgeClient`
  partial, `NavisHelper.McpServer/Program.cs`, `McpToolProfile.cs`,
  `scripts/host_command_names_baseline.txt` and `check_host_command_router.py`,
  the count asserts in `HostRequestPolicyTests`, `McpCatalogResourceTests`,
  `McpReadOnlyModeTests`, `McpToolAnnotationsTests` and `ToolCapabilitiesTests`,
  the generated capability table and catalog (`--update` on their check scripts),
  `MCP_TOOL_BASELINE.md`, the `tools/list` size table in `MCP_CLIENT_GUIDE.md`,
  and the counts and rows in `README.md`, `README.ru.md` and both `README_FULL`
  files. The guard-required ones are exempt from the 10-file limit (#142); name
  them in the brief.
- Files over 80 KB get targeted patches only (AGENTS.md section 3).
- If the brief forbids touching tests, first grep the tests for what the task
  moves: a test that pins a path by name makes the move impossible.

## 6. Launch and read the result

Run from the root of a checkout that holds the current launcher (`python -m`
resolves `scripts.*` from the current directory):

```bash
S=C:/path/to/scratch            # outside every checkout
( python scripts/build_lock.py -- python -m scripts.external_worktree_executor \
    --provider codex \
    --worktree 'D:\GitHub\NH-i123' \
    --brief "$S/brief123.md" \
    --timeout 1800 \
    --record "$S/run123.json" > "$S/run123.out" 2>&1
  echo "exit $?" > "$S/run123.done" ) > /dev/null 2>&1 &
```

- Pass `--worktree`, `--brief` and `--record` as absolute paths, and keep the brief
  and the record outside every checkout.
- `--timeout` is 10–1800 s; always pass it. At the deadline the whole process tree
  is killed through a kill-on-close Job Object.
- A refused launch exits 2 with `{error, message}` on stderr: `CLI_MISSING` /
  `CLI_INVALID` (CLI not found or misnamed), `AUTH_MISSING` (not signed in, wrong
  auth mode), `CONFIG_INVALID` (route drift, a bad brief or timeout),
  `SANDBOX_UNSAFE` (bad worktree, branch or home, or an unreviewed Codex version or
  feature).
- A background job sends no notification; poll for the `.done` file.
- During a run do not commit, pull, build or switch branches in the main checkout.
- On one machine run at most one MSBuild-heavy job at a time (an executor that
  builds, an L2 run, an installer build), across all sessions: start each through
  `python scripts/build_lock.py -- <command>`. The lock spans every session of the Windows
  account (all agent sessions here run under the owner's), and Windows
  releases it when its holder exits; a second job waits and says who holds it, and
  gives up with exit 75 after `--timeout` seconds (default 3600). Script-only tasks
  may skip it and run in parallel in separate worktrees.

### Exit codes

When several apply, the first in this order wins: 4, 6, 5, 3, then 1 or 0.

| Exit | Meaning | Action |
| --- | --- | --- |
| 0 | The record was written and the provider exited 0 | Review the diff. Exit 0 does not prove any work was done. |
| 1 | The provider exited non-zero: 124 timeout, 125 stream over 8 MB | Read `failure` in the record. |
| 2 | Launch refused: `{error, message}` on stderr, no record | Fix the input and relaunch. |
| 3 | `mainCheckoutContaminated` | Inspect the main checkout before trusting the run. |
| 4 | The runtime directory, which holds a credential copy, was not removed | Delete `runtimeDirectory` now. |
| 5 | The advisor worktree was not restored | Clean it by hand. |
| 6 | `hostViolations` | Read `host` and undo what it names before the next run. |

### The record (`avox-worktree-run/v1`)

| Field | Content |
| --- | --- |
| `provider`, `model`, `effort` | The route that ran. |
| `exitCode`, `timedOut`, `outputTooLarge`, `durationSeconds` | The provider process outcome. |
| `failure` | `TIMED_OUT`, `OUTPUT_TOO_LARGE`, `AUTH_MISSING`, `QUOTA_EXCEEDED`, `MODEL_UNAVAILABLE`, `PROVIDER_FAILURE`, or a host category. |
| `mainCheckout`, `mainCheckoutContaminated` | The main checkout and whether its fingerprint (HEAD, status, dirty-file content) changed. |
| `host`, `hostViolations` | What changed on the machine outside the worktree, including `host.processControl`; see EXTERNAL_EXECUTORS.md. Categories: `SANDBOX_PROFILE_GRANT`, `SANDBOX_ACCOUNTS_CREATED`, `OPERATOR_ACL_MUTATED`, `OPERATOR_ENV_MUTATED`, `EXECUTOR_TELEMETRY_INCOMPLETE`, `HOST_SNAPSHOT_UNAVAILABLE`, `PROCESS_STOP_OUTSIDE_RUN`. |
| `msbuildOrphanNodes` | `{found, terminated}` for this run's orphaned MSBuild nodes. |
| `home`, `credentialsCarriedBack`, `runtimeDirectoryRemoved` | The isolated home (override names, never values), rotated credentials written back, and whether cleanup succeeded. |
| `stdoutTail`, `stdoutTruncated`, `stderr` | The last 4000 characters of each stream, credentials redacted. |
| `eventJournal` | `codex` runs: `{path, bytes, sha256, lines, problems}` of `<record>.events.jsonl`, every captured line in order. |
| `stdoutPath`, `worktreeRestored`, `discardedPaths` | Advisor runs only. |

The final message: for Codex, the `text` of the last `agent_message` item (a JSON
string inside an event, so decode it twice); for Qwen, plain text at the end of
`stdoutTail`. If it was cut, the worktree (`git diff`) is the ground truth.

## 7. After the run

1. **Read the result:** `.done`, then the record's exit code, `timedOut`,
   `outputTooLarge`, `mainCheckoutContaminated`, `hostViolations` (including
   `host.processControl`), `runtimeDirectoryRemoved`, `msbuildOrphanNodes` and the
   final message.
2. **Review the diff:** `git -C <wt> status --short` and `git -C <wt> diff --stat`.
   The acceptance test is untouched and no test was weakened; only in-scope files
   changed, within the limit; destructive paths are safe.
3. **Remove Codex litter:** its sandbox leaves empty untracked temp directories in
   the worktree root. Remove the empty ones only.
4. **Re-run the checks yourself:** the acceptance test, new tests three times (an
   order-dependent assertion once passed half the time), then the whole L0 project
   and the builds the change touches, strictly one MSBuild job at a time. Do not
   take the executor's suite numbers: its sandbox runs PowerShell in constrained
   language mode.
5. **Commit, push, open the PR.** The curator commits the reviewed diff, states the
   provenance (provider, CLI version, record) and the acceptance result in the PR.
   Never force-push; to update a branch, merge `main` into it.
6. **Review before merge:** `scripts/review/codex-review.ps1 -Base origin/main`
   after a fetch (a bare `main` is the local branch and can be stale; the script
   warns), green CI, and L2 on the final SHA for plugin code (AGENTS.md section 4).
7. **Parallel tool PRs:** git merges identical count edits without a conflict and
   still leaves them wrong. After merging one into the other, set every count by
   hand.
8. **Merge and clean up** under branch protection; executors never merge. Delete
   the context file, then `git worktree remove`.

**Stop rule:** two attempts, the implementation plus one correction pass
(AGENTS.md section 3). After that, stop and record the blocker; do not open a third
PR or widen the task. A same-account review is a report, not an approval.

## 8. Known traps

| Trap | What happened | Fix in place |
| --- | --- | --- |
| npm `.cmd` shim | It cut a multi-line argument at the first newline; the agent got line one and exited 0 having done nothing. | The launcher runs the native entry point. |
| `PATH` over 8191 characters | cmd.exe treated it as empty; the CLI died at once. | The child `PATH` is pruned; `home.childPath` records what was done. |
| Refresh-token rotation | A refresh inside a disposable home retired the operator's token. | Rotated credentials are carried back (`credentialsCarriedBack`). |
| dotnet's first run in a fresh home | It appended hundreds of dead entries to the real user `PATH`. | `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0`; the host guard exits 6. |
| Codex elevated sandbox | Its accounts got read access to the whole profile; a Windows profile was lost. | `sandbox = "unelevated"`, a redirected `TEMP`, and the host guard. |
| MSBuild orphan nodes | 131 nodes, about 4 GB, slowed the PC. | Node reuse off; after each run the launcher stops this run's orphans. Never kill processes by image name. |
| Parallel MSBuild | A crash dialog on the owner's screen; two sessions each built "one at a time". | `scripts/build_lock.py` serializes MSBuild-heavy jobs across sessions (#312). |
| Stream overflow | Codex died at the old 256 KB cap. | The cap is 8 MB (exit 125); briefs ask for output tails only. |
| A process stop from the executor | A stop by name can hit the owner's Navisworks. | The preamble forbids stops; `PROCESS_STOP_OUTSIDE_RUN` fails the run (EXTERNAL_EXECUTORS.md). |
| A nested review | An executor started `scripts/review/*` inside its own run. | Every brief says "Do not run scripts/review/*". |
| Stale main checkout | A brief was written against code already fixed. | Read source from `origin/main`; do not pull during a run. |
| Windows text decoding | `text=True` decoded with cp1251, raised on byte 0x98, and a test turned flaky. | Capture output with `encoding="utf-8", errors="replace"`. |
| Git Bash path conversion | `git show origin/main:path` and `/X/Y` arguments were mangled. | Prefix the command with `MSYS_NO_PATHCONV=1`. |

## 9. Verify a setup

1. Unit tests, from the repository root: the command in EXTERNAL_EXECUTORS.md,
   "Origin and updates".
2. Refusals, none of which calls a provider: the main checkout as `--worktree`
   gives exit 2 `SANDBOX_UNSAFE`; `--timeout 5` gives `CONFIG_INVALID`; a 2049-byte
   brief against a real linked worktree gives `CONFIG_INVALID`.
3. One fixture task, with the owner's authorization because it spends quota:
   - in a throwaway worktree on its own branch, copy
     `scripts/tests/fixtures/external_executor/fix/tiny_math.py` and
     `test_tiny_math.py` into `executor-smoke/` and commit them;
     `python -m unittest test_tiny_math` there shows 2 of 3 failing;
   - brief from `fix/task.json`: fix `clamp`, change only `tiny_math.py`, keep the
     signature; launch with `--timeout 300`;
   - PASS when the launcher exits 0, `mainCheckoutContaminated` is false, the `host`
     lists are empty, `runtimeDirectoryRemoved` is true, your own re-run shows 3 of 3,
     and the diff touches only `tiny_math.py`. Record the provider, CLI version and
     date, then remove the worktree and the branch.
