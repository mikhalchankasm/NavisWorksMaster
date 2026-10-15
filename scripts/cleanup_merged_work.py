#!/usr/bin/env python3
"""Remove local branches and linked worktrees whose work is already in main.

Dry-run by default: it prints a plan with a reason per item. `--apply` removes
only what the plan marks for removal, and never forces:

- a local branch goes only when its pull request is merged and the branch tip is
  exactly that PR's head, so nothing was added after the merge. The repository
  squash-merges, so `git branch -d` cannot tell this; `git branch -D` is used
  only after this check, and never on `main` or a branch checked out anywhere;
- a linked worktree goes only when it is clean, is not the main checkout or the
  one this runs from, is not kept by `--keep`, and its commit is in `origin/main`
  or is the head of a merged pull request. `git worktree remove` runs without
  `--force`, so git itself still refuses a dirty tree.

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


def branch_decision(name, tip, pull_request, checked_out):
    """(action, reason) for one local branch; checked_out holds branch names any worktree has."""
    if name in PROTECTED_BRANCHES:
        return KEEP, "protected branch"
    if name in checked_out:
        return KEEP, "checked out in a worktree"
    if pull_request is None:
        return KEEP, "no pull request for this branch"
    if pull_request.state != "MERGED":
        return KEEP, "pull request is " + pull_request.state.lower()
    if tip != pull_request.head_oid:
        return KEEP, "branch has commits beyond the merged pull request"
    return REMOVE, "pull request merged and the branch tip is its head"


def worktree_decision(worktree, keep_paths, commit_in_main, branch_removable):
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
    return KEEP, "its commit is not in origin/main"


def git(repo, *args, check=True):
    result = subprocess.run(["git", "-C", repo, *args], capture_output=True, encoding="utf-8", errors="replace")
    if check and result.returncode != 0:
        raise RuntimeError("git " + " ".join(args) + ": " + result.stderr.strip())
    return result


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
        worktree_plan.append((wt, *worktree_decision(wt, args.keep, in_main, branch_ok)))

    # A branch stays checked out unless its worktree is planned for removal. If that
    # removal then fails, git itself refuses to delete a branch that is checked out.
    checked_out = {wt.branch for wt, action, _ in worktree_plan if wt.branch and action == KEEP}
    branch_plan = []
    for line in git(repo, "for-each-ref", "--format=%(refname:short) %(objectname)", "refs/heads").stdout.splitlines():
        name, tip = line.rsplit(" ", 1)
        branch_plan.append((name, *branch_decision(name, tip, pull_requests.get(name), checked_out)))

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
