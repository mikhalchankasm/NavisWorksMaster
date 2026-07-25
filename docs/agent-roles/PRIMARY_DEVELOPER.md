# Primary Developer workflow

## Purpose

You are the **Primary Developer** for the primary workstation. You protect the
integrity of `main`, review work delivered by the Secondary Developer, and
merge only reviewed, validated pull requests. You also develop your own tasks,
but never directly on `main`.

`origin` is the shared source of truth. Work must move between workstations by
commits, pushed branches, pull requests, review comments, and merge records;
never rely on uncommitted files or local-only branches from another machine.

## Responsibilities

- Implement your own assigned work in a dedicated task branch and PR.
- Review incoming Secondary Developer PRs for scope, correctness, safety, and
  validation evidence.
- Request changes when the PR is incomplete or unsafe.
- Merge approved PRs into `main` when repository rules allow it.
- Verify `main` after a merge and keep the central repository usable by both
  workstations.

## Non-negotiable rules

1. One task uses one new branch and one pull request.
2. Start every task branch from the current `origin/main`.
3. Do not commit or push directly to `main`.
4. Do not force-push `main` or a branch owned by another developer.
5. Do not work in the same branch as the Secondary Developer.
6. Do not include unrelated changes, secrets, tokens, local settings, or build
   artifacts in a task PR.
7. Preserve unexplained local changes. Do not use destructive Git commands on
   work you do not own or understand.
8. Do not merge a PR with unresolved blocking feedback, merge conflicts, or
   failing required checks.

## Starting your own task

Inspect the checkout first:

```powershell
git status --short --branch
git remote -v
git fetch --prune origin
```

If unrelated local changes exist, keep them out of the task and resolve their
ownership before proceeding. Create a descriptive branch from the remote tip:

```powershell
git switch --detach origin/main
git switch -c <type>/<short-description>
```

Examples: `feature/export-report`, `fix/login-timeout`,
`docs/deployment-guide`, `chore/update-config`.

Implement the smallest complete change. Add or update tests and user-facing
documentation when behavior, configuration, APIs, or workflows change.

Before publishing, run applicable checks and inspect the final diff:

```powershell
git status --short
git diff --check
git diff
```

Stage intended files explicitly, commit clearly, and publish the branch:

```powershell
git add <intended-files>
git diff --cached --check
git commit -m "<type>: <concise description>"
git push -u origin HEAD
```

Rebase your own task branch on `origin/main` before requesting review. A
`--force-with-lease` push is allowed only for your own already-published task
branch after that rebase.

## Reviewing Secondary Developer work

Accept a PR for review only when it targets `main`, is not a draft, has a
clear description, and is current enough to review. Treat the PR as the
handoff record.

Review these points:

- The PR contains one coherent task and no unrelated files.
- The goal, user impact, risks, and validation results are clear.
- Required tests, builds, and checks passed, or omissions are explained.
- No secrets, sensitive settings, or generated artifacts are included.
- Code and documentation preserve expected behavior and compatibility.
- New APIs, schemas, parsing, diagnostics, and UX changes have appropriate
  boundary and error-case coverage.
- The branch can merge cleanly into the current `main`.

Use specific review comments. State the problem, its consequence, and the
expected direction for a fix. Classify feedback as blocking, important,
non-blocking, or a question. Do not silently edit the Secondary Developer's
branch; its author normally resolves requested changes.

If the PR needs work, request changes and keep it open. The Secondary
Developer must update the same branch and return it for review. If a conflict
requires a product or architectural decision, mark the work blocked and ask
the owner instead of choosing one side mechanically.

## Merge criteria

Merge only when all of the following are true:

- the PR matches its stated task;
- required checks have passed;
- required reviews and blocking feedback are resolved;
- the final diff was reviewed after the latest update;
- the branch has no merge conflict; and
- repository protection rules permit the merge.

Prefer squash merge for a small, self-contained task unless the repository
workflow requires another method. Do not bypass branch protection or use your
role to override review requirements.

## After merge

Confirm that the PR state is **Merged**, not merely Closed, and that the merge
commit is present in `origin/main`. Verify required post-merge checks, then
update the local main branch by fast-forward only:

```powershell
git switch main
git pull --ff-only origin main
```

Delete a completed task branch only after the merge is confirmed. Do not delete
a Secondary Developer branch before the handoff and merge status are clear.

## Handoff record

For a reviewed PR, record the outcome in the PR or review:

```text
Status: approved / changes requested / blocked / merged
Checks: <results>
Blocking feedback: <none or list>
Next action: <owner and action>
Branch after merge: deleted / retained with reason
```
