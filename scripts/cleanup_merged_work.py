#!/usr/bin/env python3
"""Remove local branches and linked worktrees whose work is already in main.

Dry-run by default: it prints a plan with a reason per item. `--apply` removes
only what the plan marks for removal, and never forces:

- a local branch goes when its pull request is merged and the branch tip is
  exactly that PR's head, or when every file it changed since its merge-base with
  `origin/main` is, byte for byte, a version `origin/main` has held since then (its
  work reached main through other PRs, as executor, restack and stacked branches
  do). A branch with an open PR is
  always kept. The repository squash-merges, so `git branch -d` cannot tell any
  of this; `git branch -D` is used only after these checks, and never on `main`
  or a branch checked out anywhere;
- a linked worktree goes only when it is clean, is not the main checkout or the
  one this runs from, is not kept by `--keep`, and its commit is in `origin/main`,
  is the head of a merged pull request, or carries only content already in
  `origin/main`. `git worktree remove` runs without `--force`, so git itself still
  refuses a dirty tree.

Worktrees under another tool's folder (`.codex`) are always kept.

usage: python scripts/cleanup_merged_work.py [--apply] [--keep PATH ...]
"""
import argparse
import json
import os
import subprocess
import sys
from dataclasses import dataclass

PROTECTED_BRANCHES = frozenset({"main", "master"})
FOREIGN_TOOL_MARKERS = ("/.codex/",)

KEEP = "keep"
REMOVE = "remove"


@dataclass(frozen=True)
class PullRequest:
    state: str
    head_oid: str


@dataclass(frozen=True)
class Worktree:
    path: str
    head: str
    branch: str
    is_main: bool
    is_current: bool
    dirty: bool


def normalize_path(path):
    return os.path.normcase(os.path.abspath(path)).replace("\\", "/")


def branch_decision(name, tip, pull_request, checked_out, content_in_main=False):
    """(action, reason) for one local branch.

    checked_out holds branch names any worktree has; content_in_main says every
    file the branch changed is a version origin/main has held.
    """
    if name in PROTECTED_BRANCHES:
        return KEEP, "protected branch"
    if name in checked_out:
        return KEEP, "checked out in a worktree"
    if pull_request is not None and pull_request.state == "OPEN":
        return KEEP, "pull request is open"
    if pull_request is not None and pull_request.state == "MERGED" and tip == pull_request.head_oid:
        return REMOVE, "pull request merged and the branch tip is its head"
    if content_in_main:
        return REMOVE, "every file it changed is a version origin/main has held"
    if pull_request is None:
        return KEEP, "no pull request for this branch"
    if pull_request.state != "MERGED":
        return KEEP, "pull request is " + pull_request.state.lower()
    return KEEP, "branch has commits beyond the merged pull request"


def worktree_decision(worktree, keep_paths, commit_in_main, branch_removable, content_in_main=False):
    """(action, reason) for one worktree; commit_in_main and branch_removable are booleans."""
    path = normalize_path(worktree.path)
    if worktree.is_main:
        return KEEP, "main checkout"
    if worktree.is_current:
        return KEEP, "the worktree this runs from"
    if any(marker in path + "/" for marker in FOREIGN_TOOL_MARKERS):
        return KEEP, "another tool's worktree"
    if path in {normalize_path(keep) for keep in keep_paths}:
        return KEEP, "kept by --keep"
    if worktree.dirty:
        return KEEP, "has uncommitted changes"
    if commit_in_main:
        return REMOVE, "clean, and its commit is in origin/main"
    if branch_removable:
        return REMOVE, "clean, and it sits on a merged pull request's head"
    if content_in_main:
        return REMOVE, "clean, and every file it changed is a version origin/main has held"
    return KEEP, "its commit is not in origin/main"


def git(repo, *args, check=True):
    result = subprocess.run(["git", "-C", repo, *args], capture_output=True, encoding="utf-8", errors="replace")
    if check and result.returncode != 0:
        raise RuntimeError("git " + " ".join(args) + ": " + result.stderr.strip())
    return result


def blob(repo, commit, path):
    """The blob id of path at commit, or "" when the path does not exist there."""
    result = git(repo, "rev-parse", "--verify", "--quiet", commit + ":" + path, check=False)
    return result.stdout.strip() if result.returncode == 0 else ""


def content_matches_main(repo, ref, main_ref="origin/main"):
    """True when main_ref's history already holds ref's version of every file ref changed.

    For each file ref changed since its merge-base with main_ref, ref's version (or
    its absence) must equal main_ref's version at main_ref or at some commit after
    the merge-base. Stacked PRs are squash-merged one by one, so an intermediate
    branch matches an intermediate commit on main rather than its tip.
    """
    base = git(repo, "merge-base", ref, main_ref, check=False).stdout.strip()
    if not base:
        return False
    files = [name for name in git(repo, "diff", "--name-only", base, ref).stdout.splitlines() if name]
    for path in files:
        wanted = blob(repo, ref, path)
        if blob(repo, main_ref, path) == wanted:
            continue
        commits = git(repo, "log", "--format=%H", base + ".." + main_ref, "--", path).stdout.split()
        if not any(blob(repo, commit, path) == wanted for commit in commits):
            return False
    return True


def list_worktrees(repo, current):
    blocks = git(repo, "worktree", "list", "--porcelain").stdout.strip().split("\n\n")
    worktrees = []
    for index, block in enumerate(blocks):
        fields = dict(line.split(" ", 1) if " " in line else (line, "") for line in block.splitlines())
        if "worktree" not in fields or "prunable" in fields:
            continue
        path = fields["worktree"]
        branch = fields.get("branch", "").replace("refs/heads/", "")
        dirty = bool(git(path, "status", "--porcelain", check=False).stdout.strip())
        worktrees.append(Worktree(path, fields.get("HEAD", ""), branch, index == 0,
                                  normalize_path(path) == normalize_path(current), dirty))
    return worktrees


def merged_pull_requests():
    """Latest pull request per head branch name, from GitHub."""
    result = subprocess.run(["gh", "pr", "list", "--state", "all", "--limit", "1000",
                             "--json", "headRefName,state,headRefOid,number"],
                            capture_output=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        raise RuntimeError("gh pr list: " + result.stderr.strip())
    latest = {}
    for pr in sorted(json.loads(result.stdout), key=lambda item: item["number"]):
        latest[pr["headRefName"]] = PullRequest(pr["state"], pr["headRefOid"])
    return latest


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--apply", action="store_true", help="remove what the plan marks; default is a dry-run")
    parser.add_argument("--keep", action="append", default=[], help="a worktree path to keep; repeatable")
    args = parser.parse_args(argv)

    current = git(".", "rev-parse", "--show-toplevel").stdout.strip()
    git(current, "fetch", "--quiet", "origin")
    if args.apply:
        git(current, "worktree", "prune")
    pull_requests = merged_pull_requests()
    merged_heads = {pr.head_oid for pr in pull_requests.values() if pr.state == "MERGED"}
    worktrees = list_worktrees(current, current)
    repo = worktrees[0].path

    worktree_plan = []
    for wt in worktrees:
        in_main = git(repo, "merge-base", "--is-ancestor", wt.head, "origin/main", check=False).returncode == 0
        pr = pull_requests.get(wt.branch)
        # A detached worktree left by a finished session sits on a squash-merged PR's
        # head, which is not in main's history; that head is still proof of merge.
        branch_ok = wt.head in merged_heads or (
            bool(wt.branch) and pr is not None and pr.state == "MERGED" and pr.head_oid == wt.head)
        content_ok = not in_main and not branch_ok and bool(wt.head) and content_matches_main(repo, wt.head)
        worktree_plan.append((wt, *worktree_decision(wt, args.keep, in_main, branch_ok, content_ok)))

    # A branch stays checked out unless its worktree is planned for removal. If that
    # removal then fails, git itself refuses to delete a branch that is checked out.
    checked_out = {wt.branch for wt, action, _ in worktree_plan if wt.branch and action == KEEP}
    branch_plan = []
    for line in git(repo, "for-each-ref", "--format=%(refname:short) %(objectname)", "refs/heads").stdout.splitlines():
        name, tip = line.rsplit(" ", 1)
        branch_plan.append((name, *branch_decision(name, tip, pull_requests.get(name), checked_out,
                                                   content_matches_main(repo, tip))))

    for wt, action, reason in worktree_plan:
        print(f"worktree {action:6} {wt.path}  ({reason})")
    for name, action, reason in branch_plan:
        print(f"branch   {action:6} {name}  ({reason})")
    if not args.apply:
        print("dry-run; pass --apply to remove the items marked remove")
        return 0

    failures = 0
    for wt, action, _ in worktree_plan:
        if action == REMOVE:
            result = git(repo, "worktree", "remove", wt.path, check=False)
            failures += result.returncode != 0
            print(("removed " if result.returncode == 0 else "FAILED  ") + wt.path + " " + result.stderr.strip())
    for name, action, _ in branch_plan:
        if action == REMOVE:
            result = git(repo, "branch", "-D", name, check=False)
            failures += result.returncode != 0
            print(("deleted " if result.returncode == 0 else "FAILED  ") + name + " " + result.stderr.strip())
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
