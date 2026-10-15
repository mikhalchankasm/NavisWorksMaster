# External executors: running the executor launcher

This page covers the mechanics of running an external executor from a
NavisHelper checkout: how to run the launcher and what its record tells you.
Who may be an executor and in what role is
[AGENTS.md section 6](../AGENTS.md#6-roles); it is not restated here.

The launcher lives in this repository: `scripts/external_worktree_executor.py`
with its modules (`executor_worktree.py`, `agent_home_isolation.py`,
`executor_primitives.py`, `executor_host_guard.py`, `executor_msbuild_nodes.py`,
`executor_event_journal.py`, `executor_process_control.py`), the route config
`config/external-executors.json` and the tests in `scripts/tests/`.
Start an executor only through it: never run
a provider CLI by hand or through a provider's plugin or IDE integration, which
would inherit the operator's MCP servers, hooks and configuration.

## Running a task

How to prepare a worktree, write the brief and context file, launch, read the
exit code and the record, and review the result is the operating guide,
[EXECUTOR_SETUP.md](EXECUTOR_SETUP.md). This page keeps the launcher's contract:
where it came from, what a run may not change on the machine, and Codex's sandbox.

## Origin and updates

The launcher was copied from Avox
(`https://github.com/mikhalchankasm/Avox.git`) at commit
`bcada2588e819f8650c06ddbaa8ff18d58f7b46f`. The first local change is the
preamble in `executor_worktree.py`, which names NavisHelper instead of Avox,
and the matching assertion in `test_executor_worktree.py`. The second is
process-control detection (#259): observing the run's Job Object PIDs,
recording Codex's process-stop attempts, and failing unproven stops.
The `AVOX_*` environment names, the `avox-*/v1` record schemas and the `.avox-context-*.txt`
pattern keep their Avox names so later Avox fixes copy over unchanged.
The operating guide (providers, sign-in, preparing and launching a run, exit
codes, the executor brief, after-run review and known traps) is
[EXECUTOR_SETUP.md](EXECUTOR_SETUP.md), ported from Avox's guide at 9e936647.

To take an Avox fix, copy the same files from one Avox commit, keep the
preamble and process-control changes, run the tests below, and name the Avox
commit here and in the PR. Launcher changes ship with this page in the same PR.

```console
python -m unittest scripts.tests.test_executor_process_control scripts.tests.test_external_worktree_executor scripts.tests.test_executor_worktree scripts.tests.test_agent_home_isolation scripts.tests.test_executor_host_guard scripts.tests.test_executor_msbuild_nodes scripts.tests.test_executor_event_journal scripts.tests.test_executor_escape_guard
```

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
  into the profile;
- records Codex's attempted process stops in `host.processControl`, one entry
  per command item in journal order (`itemId`, redacted `command`, redacted
  `output` tail capped at 2000 characters, ordered unique `pids`, `inRunTree`).
  Completed events replace started events; a started-only attempt still counts.

`inRunTree` is true only when every stop names literal PIDs and all of them
were observed in the run's Windows Job Object. A stop by name, pipeline or
variable is unproven, and so is every WMI/CIM stop (its predicate can select
more than the PID it names) and every remote or filtered one (`/S`, `/FI`,
`-ComputerName`). Detection reads the command text, so a command that only
mentions a stop - `rg 'Stop-Process'` - also fails the run; read the entry
before acting on the exit code. Any false entry adds `PROCESS_STOP_OUTSIDE_RUN` to
`hostViolations` and fails the run with exit 6, even if the stop itself failed;
outside Windows no run-tree PIDs are proven. The executor preamble prohibits
process stops from commands it writes and tells it to report locks and stop
work; test suites may stop only children they started themselves.

A PID observed in the job that exits and is reused by an outside process can
pass as in-tree, and a stop hidden inside a script file the executor wrote
rather than shown as a journal command is not detected.

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
The full after-run checklist is [EXECUTOR_SETUP.md section 7](EXECUTOR_SETUP.md#7-after-the-run).
