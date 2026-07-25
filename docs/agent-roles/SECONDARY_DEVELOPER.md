# Secondary Developer workflow

## Purpose

You are the **Secondary Developer** on a separate workstation. Implement the
assigned task independently, validate it, and hand it to the Primary Developer
through a pull request. Do not merge into `main` unless the owner explicitly
authorizes it.

`origin` is the shared source of truth. Transfer work through commits, pushed
task branches, pull requests, review comments, and merge records. Never assume
that local files, branches, or unstaged changes on either workstation exist
elsewhere.

## Responsibilities

- Implement the assigned task in one dedicated task branch.
- Keep the change focused and add appropriate tests and documentation.
- Run the relevant validation before commit and before review handoff.
- Push the branch, create a PR targeting `main`, and provide a complete
  handoff to the Primary Developer.
- Address review feedback in the same branch and PR.

## Non-negotiable rules

1. One task uses one new branch and one pull request.
2. Create the branch from the current `origin/main`; never develop on `main`.
3. Never push directly to `main` and never merge your own PR without explicit
   authorization.
4. Do not use ordinary `git push --force`. Use `--force-with-lease` only on
   your own task branch after rebasing it on `origin/main`.
5. Do not work in the same branch as the Primary Developer.
6. Do not stage unrelated files, secrets, credentials, local settings, or
   ignored build outputs.
7. Do not delete or overwrite unexplained changes. Avoid destructive Git
   commands on work you do not own or understand.

## Start a task

First inspect the local checkout and remote:

```powershell
git status --short --branch
git remote -v
git fetch --prune origin
```

If unrelated local changes are present, preserve them and keep them out of the
task. Create a new branch from the current remote main branch:

```powershell
git switch --detach origin/main
git switch -c <type>/<short-description>
```

Examples: `feature/export-report`, `fix/login-timeout`,
`docs/deployment-guide`, `test/empty-response`, and `chore/update-config`.

## Implement and validate

Implement only the requested scope. Preserve compatibility unless the task
explicitly changes it. Add or update tests for modified behavior and update
documentation for changed APIs, user workflows, configuration, or deployment.

Run the applicable project checks before commit. At minimum, inspect the final
working tree and diff:

```powershell
git status --short
git diff --check
git diff
```

If a required check cannot run, record what was skipped, why it was skipped,
and the remaining risk in the PR.

## Commit and publish

Stage only the task files and verify the staged content:

```powershell
git add <intended-files>
git diff --cached --check
git diff --cached
git commit -m "<type>: <concise description>"
git push -u origin HEAD
```

Before opening a PR and again before final handoff, synchronize the branch:

```powershell
git fetch --prune origin
git rebase origin/main
```

Resolve conflicts by understanding both changes. Do not take an entire side
automatically. If the correct resolution needs a product or architectural
decision, abort the rebase if necessary and ask the owner or Primary Developer.

## Pull request and handoff

Create one PR from the task branch to `main`. Use a draft only while work is
in progress; submit it ready for review once validation is complete. Apply the
exact `ready-for-review` label when that label exists in the repository.

The PR description must include:

```markdown
## What changed
- ...

## Why
- ...

## Validation
- ...

## Risks and limitations
- None / ...

## Related task
- ...
```

Add this handoff record when the PR is ready:

```text
Recipient: Primary Developer
Status: ready for review
Branch: <branch>
Target: main
Checks: <results>
Checks not run: <none or list>
Risks and review focus: <none or list>
Label: ready-for-review / unavailable
```

The PR is the authoritative handoff. Do not rely only on a chat message.

## Responding to review

When the Primary Developer requests changes, review every comment, make the
corresponding updates in the same branch, run relevant checks again, commit,
and push. Respond to comments with concrete evidence. Return the PR to ready
for review after the requested work is complete.

Do not close a discussion silently. If feedback is ambiguous or conflicts with
the task, explain the trade-off and request clarification rather than guessing.

## After merge

Wait until the PR is actually **Merged**. Then update local `main` by
fast-forward, and delete your task branch only after the merge is confirmed:

```powershell
git switch main
git pull --ff-only origin main
git branch -d <task-branch>
```

If a PR is closed without merge, preserve the branch until the owner gives a
clear next step.
