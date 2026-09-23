# External executors: fetching and running the Avox launcher

This page covers the mechanics of running an external executor from a
NavisHelper checkout: how to fetch the launcher, how to run it, and what its
record tells you. Who may be an executor and in what role is
[AGENTS.md section 6](../AGENTS.md#6-roles); it is not restated here.

The launcher belongs to Avox (`https://github.com/mikhalchankasm/Avox.git`), a
separate repository NavisHelper does not vendor. NavisHelper pins one Avox
commit and fetches five files from it; the pin and the five SHA-256 hashes live
in `scripts/fetch_executor_launcher.py`.

## Fetch and run

```console
python scripts/fetch_executor_launcher.py --avox <path-to-an-Avox-clone>
cd artifacts/executor-launcher
python -m scripts.external_worktree_executor --provider {glm|qwen|codex} --worktree <path> --brief <file> --timeout 1800 --record <json>
```

`--avox` may also come from the `AVOX_REPO` environment variable. The fetch
reads committed bytes only (`git show <pin>:<path>`): it never reads or changes
the Avox working tree and never fetches. If the pin is missing from the clone,
the script exits non-zero, writes nothing, and tells you to run
`git -C <avox> fetch origin`. The default destination
`artifacts/executor-launcher` is under the git-ignored `artifacts/` directory,
so a fetched launcher is never committed by accident. A destination that already
holds any file other than the five pinned ones (and `__pycache__` left by an
earlier run) is refused: the script names the extra files, writes nothing, and
deletes nothing.

The launcher finds its config at `config/external-executors.json` relative to
its own `scripts/` folder, and runs as a module from the fetched root — hence
the `cd` and `python -m`.
Because the command runs from the launcher folder, give `--worktree`, `--brief`
and `--record` as absolute paths.

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

Two lines every brief carries:

- "the preamble's Avox is the launcher's name" — the launcher's preamble says
  Avox, and an executor that meets the word unexplained may go looking for a
  repository it will not find;
- "Do not run scripts/review/*" — review is the curator's job; an executor has
  once started a nested review from inside its own run.

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

## Codex's sandbox

Codex can write only inside its worktree, so anything it must produce — a
baseline file, a measured result — goes under the git-ignored `artifacts/`
directory, not into the committed tree.

## After the run

The curator reruns the task's acceptance check against their own baseline, not
the executor's report, and states the provenance (which provider, which pin,
which record) in the PR. An executor's claim of success is input, not evidence.

## Bumping the pin

Change the pinned SHA and the five hashes together in one reviewed PR, and say
in that PR what changed upstream in Avox. A pin bumped without its hashes fails
the fetch loudly; hashes edited to match an unpinned upstream commit are a
review problem, which is why the two travel in one diff.
