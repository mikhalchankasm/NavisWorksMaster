# External executors: running the executor launcher

This page covers the mechanics of running an external executor from a
NavisHelper checkout: how to run the launcher and what its record tells you.
Who may be an executor and in what role is
[AGENTS.md section 6](../AGENTS.md#6-roles); it is not restated here.

The launcher lives in this repository: `scripts/external_worktree_executor.py`
with its modules (`executor_worktree.py`, `agent_home_isolation.py`,
`executor_primitives.py`, `executor_host_guard.py`, `executor_msbuild_nodes.py`,
`executor_event_journal.py`), the route config `config/external-executors.json`
and the tests in `scripts/tests/`. Start an executor only through it: never run
a provider CLI by hand or through a provider's plugin or IDE integration, which
would inherit the operator's MCP servers, hooks and configuration.

## Run

From the repository root (the imports are `scripts.<module>`):

```console
python -m scripts.external_worktree_executor --provider codex --worktree <path> --brief <file> --timeout 1800 --record <json>
```

`--provider` also accepts `glm`, `glm-flash`, `qwen`, `claude`, `kimi` and
`codex-plan`; `codex` is the main route. Pass `--worktree`, `--brief` and
`--record` as absolute paths, and keep the brief and the record outside every
checkout. A refused launch exits 2 with a JSON `{error, message}` on stderr
(`CLI_MISSING`, `AUTH_MISSING`, `CONFIG_INVALID`, `SANDBOX_UNSAFE`) and spends
no quota.

Before every `codex` run the launcher runs `codex features list` in the
isolated home and refuses unless the CLI version is in
`CODEX_REVIEWED_VERSIONS` and the account features (connectors, browser,
plugins, web search) are off. A new codex minor release therefore stops the
executor until it is reviewed; see "Upgrading the CLI" in the Avox guide below.

## Origin and updates

The launcher was copied from Avox
(`https://github.com/mikhalchankasm/Avox.git`) at commit
`bcada2588e819f8650c06ddbaa8ff18d58f7b46f`. The only local change is the
preamble in `executor_worktree.py`, which names NavisHelper instead of Avox,
and the matching assertion in `test_executor_worktree.py`. The `AVOX_*`
environment names, the `avox-*/v1` record schemas and the `.avox-context-*.txt`
pattern keep their Avox names so later Avox fixes copy over unchanged.
Avox's `docs/EXECUTOR_SETUP.md` is the full reference for providers, sign-in,
isolated homes and known traps.

To take an Avox fix, copy the same files from one Avox commit, keep the
preamble change, run the tests below, and name the Avox commit here and in the
PR. Launcher changes ship with this page in the same PR.

```console
python -m unittest scripts.tests.test_external_worktree_executor scripts.tests.test_executor_worktree scripts.tests.test_agent_home_isolation scripts.tests.test_executor_host_guard scripts.tests.test_executor_msbuild_nodes scripts.tests.test_executor_event_journal scripts.tests.test_executor_escape_guard
```

## The executor's worktree

The launcher runs the executor in a linked worktree on its own task branch,
never on `main`. This is the same one-task-one-branch-one-PR discipline
AGENTS.md applies to every agent; the launcher mechanicalizes it. Create the
worktree from the current `main`, outside the main checkout:

```console
git worktree add -b <provider>/<task> <path> origin/main
```

## The brief

The brief's shape is [TASK_BRIEF.md](TASK_BRIEF.md). Its size is measured on
disk, and the limit is 2048 bytes. Beware CRLF: Python's `write_text` on
Windows writes CRLF, one extra byte per line, so a brief that fits as LF can
be over the limit as written. Over the limit the launcher exits 2 and writes
no record.

Write the brief in English: Cyrillic doubles the byte count. Facts that do not
fit go into `.avox-context-<id>.txt` in the worktree root; `.gitignore` keeps
it out of commits, and it is deleted before the worktree is removed. Executors
cannot read GitHub, so issue facts belong in that file, never live model data.

Every brief carries the line "Do not run scripts/review/*" — review is the
curator's job; an executor has once started a nested review from inside its
own run.

## The main checkout during a run

While a run is in flight, do not commit in the main checkout. The launcher
fingerprints it before and after the run; a change shows up as
`mainCheckoutContaminated: true` in the record, and the run is no longer
evidence about a single task.

## The record

The launcher writes a JSON record with:

| field | meaning |
| --- | --- |
| `provider` | which executor ran (`glm`, `qwen`, `codex`) |
| `model` | the model the provider used |
| `exitCode` | the executor's process exit code |
| `timedOut` | whether the `--timeout` cut the run short |
| `durationSeconds` | wall-clock length of the run |
| `mainCheckoutContaminated` | whether the main checkout changed during the run |
| `stdoutTail` | the tail of the executor's stdout |
| `host` | what changed on the machine outside the worktree (next section) |
| `hostViolations` | present only when `host` shows a change; the run then failed |

## What the run may not change on the machine

Moving the provider's home moves files only. It does not move the registry,
local accounts or the file rights a sandbox grants, and on another machine
those three cost a Windows profile: dotnet appended hundreds of temporary
folders to the real user `PATH`, and Codex's elevated sandbox granted its own
accounts read access to all of `AppData`. So the pinned launcher:

- writes `[windows] sandbox = "unelevated"` into Codex's isolated config,
  because that config does not inherit the operator's and Codex's default
  mode is not the operator's choice;
- sets `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0` and its three neighbours for every
  provider, and points `TEMP`/`TMP` into the run's own home;
- snapshots the machine before and after the run and records in `host` the
  names of changed `HKCU\Environment` values, any new `CodexSandbox*` account,
  changed rights on profile folders, and for Codex the sandbox log's grants
  into the profile.

Any of those, or a snapshot that could not be taken, is a `hostViolations`
entry and exit code 6, whatever the executor produced. Do not launch another executor until you have read the
record's `host` section and undone what it names.

## Codex's sandbox

Codex can write only inside its worktree, so anything it must produce — a
baseline file, a measured result — goes under the git-ignored `artifacts/`
directory, not into the committed tree.

Codex also cannot commit. A linked worktree keeps its git directory in the
main checkout (`.git/worktrees/<name>/`), outside the sandbox's writable root,
so `git commit` fails on `index.lock`. Write the brief's Deliver line as "leave
the change uncommitted", and the curator commits the reviewed diff.

## After the run

The curator reruns the task's acceptance check against their own baseline, not
the executor's report, and states the provenance (which provider, which pin,
which record) in the PR. An executor's claim of success is input, not evidence.
