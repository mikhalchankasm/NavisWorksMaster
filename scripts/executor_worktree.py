#!/usr/bin/env python3
"""What an executor run is allowed to be, before anything is launched.

Half of executor mode from AGENTS.md section 7. This module answers "is this a
legal executor task?" - which worktree, which brief, which command. The module
that answers "run it and record what happened" is external_worktree_executor.

A note on what the worktree check does and does not buy. Refusing the main
checkout and refusing master means the run *starts* somewhere safe and always
has its own branch. It does not confine the process: cwd selects a starting
directory, it is not a filesystem sandbox, and a provider that writes to an
absolute path outside the worktree will succeed. Contamination of the main
checkout is therefore detected rather than prevented, which is what
main_checkout and fingerprint are for.
"""

from __future__ import annotations

import re
import hashlib
import json
import os
import stat
import subprocess
from pathlib import Path

from scripts.agent_home_isolation import (
    CODEX_DISABLED_FEATURES,
    CODEX_REVIEWED_ENABLED_FEATURES,
    EXECUTOR_PROFILES,
)
from scripts.executor_primitives import MAX_TIMEOUT_SECONDS, ExecutorError, _canonical_path, _launcher

# docs/TASK_BRIEF.md caps a brief at 2 KB, and AGENTS.md repeats it. A launcher
# that accepted more would quietly widen the task context past the boundary the
# owner approved, so the limit is the documented one rather than a roomier one.
MAX_BRIEF_BYTES = 2048

PREAMBLE = """You are running as a NavisHelper executor in a dedicated git worktree.

Read AGENTS.md and the brief below. Change only what the brief puts in scope,
and write only inside this worktree. Do not weaken or delete a test to make one
pass. If the brief is impossible as written - it contradicts itself, or the
acceptance criterion cannot hold - stop and say so instead of working around
it. Report the acceptance criterion and its measured result. Do not merge
anything.

Never stop, kill or signal any process from a command you write (Stop-Process,
taskkill, kill, .Kill()). Running a test suite which stops only children the test
itself started is allowed. If a lock or a running process blocks you,
report the lock and stop work.

BRIEF:
"""

ADVISOR_PREAMBLE = """You are advising the NavisHelper owner, not changing the repository. You have no
write access: your sandbox is read-only, and nothing you say is applied
automatically.

Read AGENTS.md and the question below. Answer it. Where the answer depends on
something you could not read or verify, say so plainly instead of guessing. Do
not propose a patch unless the question asks for one, and do not report an
acceptance result - there is nothing here to accept.

QUESTION:
"""

PREAMBLES = {"executor": PREAMBLE, "advisor": ADVISOR_PREAMBLE}

# The one MSBuild mode proved inside the unelevated sandbox (#1274): with the default worker nodes restore stops
# with no error; with one node it restores offline, builds and tests. dotnet --info is not proved either.
CODEX_SANDBOX_NOTE = """In this sandbox there is no network and MSBuild works with one node only: pass
-m:1 to every dotnet build and dotnet test (restore is offline, from a read-only
package folder). Do not run dotnet --info. Do not set TEMP, DOTNET_CLI_HOME,
NUGET_* or any other environment variable to get past a failure - report it.
Do not commit: leave your changes in the tree, the coordinator commits.

"""

PROTECTED_BRANCHES = frozenset({"master", "main", "HEAD"})

# git status -z terminates each record with this instead of quoting the path.
NUL = chr(0)


def _git(*arguments: str, strip: bool = True) -> str:
    """strip=False for porcelain output, whose first column is a significant space.

    Decoded as UTF-8 rather than by locale: git emits paths as UTF-8, and on a
    machine whose locale is not, a non-ASCII filename would come back mangled
    and never match anything on disk.
    """
    try:
        result = subprocess.run(
            ["git", *arguments],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="surrogateescape",
            timeout=60,
            check=True,
        )
        return result.stdout.strip() if strip else result.stdout
    except (OSError, subprocess.SubprocessError) as exc:
        raise ExecutorError("SANDBOX_UNSAFE", "A git query for the executor worktree failed") from exc


def resolve_worktree(path: Path, provider: str | None = None) -> Path:
    """Accept only a linked worktree on a branch allowed for this provider."""
    resolved = _canonical_path(path)
    if not resolved.is_dir():
        raise ExecutorError("SANDBOX_UNSAFE", "The worktree path is not a directory")
    common = _git("-C", str(resolved), "rev-parse", "--path-format=absolute", "--git-common-dir")
    own = _git("-C", str(resolved), "rev-parse", "--path-format=absolute", "--git-dir")
    if _canonical_path(Path(common)) == _canonical_path(Path(own)):
        raise ExecutorError("SANDBOX_UNSAFE", "Executor mode needs a linked worktree, not the main checkout")
    branch = _git("-C", str(resolved), "rev-parse", "--abbrev-ref", "HEAD")
    if branch in PROTECTED_BRANCHES:
        raise ExecutorError("SANDBOX_UNSAFE", f"The worktree is on {branch}; give the executor its own branch")
    if provider == "codex-plan" and not branch.startswith("advice/"):
        raise ExecutorError("SANDBOX_UNSAFE", "codex-plan requires an advice/ worktree branch")
    return resolved


def main_checkout(worktree: Path) -> Path:
    """The checkout the owner works in - the one a run must leave alone."""
    common = _canonical_path(Path(_git("-C", str(worktree), "rev-parse", "--path-format=absolute", "--git-common-dir")))
    return common.parent


def worktree_head(worktree: Path) -> str:
    """The commit a disposable worktree must return to after its run."""
    return _git("-C", str(worktree), "rev-parse", "HEAD")


def _changed_worktree_paths(worktree: Path, baseline: str) -> set[str]:
    """Tracked changes from baseline plus every untracked or ignored path."""
    tracked = _git(
        "-C", str(worktree), "diff", "--name-only", "-z", baseline, "--", strip=False
    )
    untracked = _git(
        "-C", str(worktree), "ls-files", "--others", "--exclude-standard", "-z", strip=False
    )
    ignored = _git(
        "-C", str(worktree), "ls-files", "--others", "--ignored", "--exclude-standard", "-z", strip=False
    )
    return {path for path in (tracked + untracked + ignored).split(NUL) if path}


def restore_disposable_worktree(provider: str, worktree: Path, baseline: str) -> dict[str, object]:
    """Discard an advisor's writes and return the fields for its run record.

    Executor worktrees are deliverables, so all other providers are deliberately
    no-ops. The before/after comparison keeps a partial cleanup from claiming it
    discarded paths that still exist.
    """
    if provider != "codex-plan":
        return {}

    problems: list[str] = []
    before: set[str] | None = None
    after: set[str] | None = None
    try:
        before = _changed_worktree_paths(worktree, baseline)
    except ExecutorError as exc:
        problems.append(f"could not inspect the advisor worktree before restore: {exc}")

    for arguments, description in (
        (("reset", "--hard", baseline), "revert tracked files"),
        (("clean", "-ffdx"), "remove untracked files"),
    ):
        try:
            _git("-C", str(worktree), *arguments)
        except ExecutorError as exc:
            problems.append(f"could not {description}: {exc}")

    try:
        after = _changed_worktree_paths(worktree, baseline)
    except ExecutorError as exc:
        problems.append(f"could not verify the advisor worktree after restore: {exc}")

    discarded = sorted(before - after) if before is not None and after is not None else []
    restored = not problems and after == set()
    result: dict[str, object] = {
        "worktreeRestored": restored,
        "discardedPaths": discarded,
    }
    if not restored:
        remaining = ", ".join(sorted(after or ()))
        if remaining:
            problems.append(f"paths remain after restore: {remaining}")
        result["worktreeRestoreProblem"] = "; ".join(problems) or "the worktree is not clean"
    return result


def _dirty_paths(status: str) -> list[str]:
    """Paths from NUL-delimited porcelain, which emits them raw.

    The default format C-quotes any path needing it, so a filename containing a
    quote or a backslash comes back escaped and never matches a real file. With
    -z there is no quoting; a rename or copy is followed by its source path in
    the next record, which is consumed and discarded - the destination is what
    exists on disk.
    """
    fields = [field for field in status.split("\x00") if field]
    paths: list[str] = []
    index = 0
    while index < len(fields):
        entry = fields[index]
        index += 1
        if len(entry) < 4:
            continue
        code, path = entry[:2], entry[3:]
        paths.append(path)
        if "R" in code or "C" in code:
            index += 1
    return paths


def _frame(digest: "hashlib._Hash", tag: bytes, *parts: bytes) -> None:
    """Length-prefix every part, so two different states cannot serialize alike.

    Appending path and content back to back is ambiguous: untracked files a=""
    and b="bX" produce the same byte stream as a="b" and b="X", and an executor
    could rewrite both while the check stayed quiet.
    """
    digest.update(tag)
    for part in parts:
        digest.update(len(part).to_bytes(8, "big"))
        digest.update(part)


def _entry_digest(target: Path) -> tuple[bytes, bytes]:
    """What this path is, and a fixed-width summary of its content.

    A symlink is summarised by where it points, not by what is there. Following
    it would miss a retarget between two files with identical contents, and
    would read a file outside the checkout entirely.
    """
    try:
        if target.is_symlink() or (callable(getattr(target, "is_junction", None)) and target.is_junction()):
            return b"L", os.readlink(target).encode("utf-8", "replace")
        info = target.lstat()
    except OSError:
        return b"?", b""
    if not stat.S_ISREG(info.st_mode):
        return b"D" if stat.S_ISDIR(info.st_mode) else b"O", b""
    content = hashlib.sha256()
    try:
        with target.open("rb") as handle:
            while chunk := handle.read(1 << 20):
                content.update(chunk)
    except OSError:
        return b"?", b""
    return b"F", content.digest()


def fingerprint(checkout: Path) -> str:
    """HEAD, working-tree state, and the contents of everything already dirty.

    Hashing HEAD and the porcelain text alone is not enough. Porcelain reports
    that a file is modified, not what is in it, so overwriting an
    already-modified tracked file or an existing untracked file leaves the text
    identical and the run looks clean while the owner's work is gone. Every path
    git lists is summarised as well, which is exactly the set porcelain cannot
    tell apart.
    """
    head = _git("-C", str(checkout), "rev-parse", "HEAD")
    status = _git("-C", str(checkout), "status", "--porcelain", "-z", "--untracked-files=all", strip=False)
    digest = hashlib.sha256()
    _frame(digest, b"H", head.encode("utf-8"), status.encode("utf-8", "replace"))
    for path in _dirty_paths(status):
        kind, summary = _entry_digest(checkout / path)
        _frame(digest, kind, path.encode("utf-8", "replace"), summary)
    return digest.hexdigest()


def read_brief(path: Path) -> str:
    try:
        raw = path.read_bytes()
    except OSError as exc:
        raise ExecutorError("CONFIG_INVALID", "The brief could not be read") from exc
    if not raw:
        raise ExecutorError("CONFIG_INVALID", "The brief is empty")
    if len(raw) > MAX_BRIEF_BYTES:
        raise ExecutorError(
            "CONFIG_INVALID",
            f"The brief is {len(raw)} bytes; docs/TASK_BRIEF.md caps it at {MAX_BRIEF_BYTES}. Split the task.",
        )
    try:
        return raw.decode("utf-8")
    except UnicodeError as exc:
        raise ExecutorError("CONFIG_INVALID", "The brief is not valid UTF-8") from exc


def build_prompt(provider: str, brief: str) -> str:
    """Prefix a task according to the route's declared mode."""
    mode = str(EXECUTOR_PROFILES[provider].get("mode", "executor"))
    try:
        preamble = PREAMBLES[mode]
    except KeyError as exc:
        raise ExecutorError("CONFIG_INVALID", f"Unknown executor profile mode: {mode}") from exc
    if provider == "codex":
        preamble = preamble.replace("BRIEF:\n", CODEX_SANDBOX_NOTE + "BRIEF:\n")
    return preamble + brief


def codex_isolation_arguments() -> list[str]:
    """Hosted tools and account features that no config.toml declares and the isolated home cannot remove.

    web_search is network egress for an agent whose whole job is a local change. The features carry the
    signed-in ChatGPT account's connectors (codex_apps: e-mail, Notion, site deploys, platform keys) and browser,
    computer-use and plugin tools; agent_home_isolation.CODEX_DISABLED_FEATURES explains the list. The same
    switches are also written into the isolated config.toml.
    """
    arguments = [
        "-c", "tools.web_search=false",
        "-c", "tools.image_gen=false",
        "-c", 'web_search="disabled"',
    ]
    for feature in CODEX_DISABLED_FEATURES:
        arguments += ["-c", f"features.{feature}=false"]
    return arguments


# codex ignores an unknown feature name without a word (0.158, measured 2026-09-29), so a renamed feature would
# silently stay on. Besides the listed names, any enabled feature whose name carries one of these words is refused.
CODEX_RISKY_FEATURE_TOKENS = frozenset({"apps", "browser", "computer", "plugin", "plugins", "connector", "connectors"})
_FEATURE_ROW = re.compile(r"^(?P<name>\S+)\s+.*?(?P<enabled>true|false)$")


def unsafe_codex_features(listing: str) -> list[str] | None:
    """What in a `codex features list` output stops an executor run; None when nothing parses.

    Fail closed (PR #629 review): every switched-off feature must be listed and false - codex accepts an obsolete
    switch silently, so a missing name means it may live on under another one - and every enabled feature must be
    one reviewed in CODEX_REVIEWED_ENABLED_FEATURES. A risky word in an enabled name is refused even if reviewed.
    """
    rows = {}
    for line in (listing or "").splitlines():
        match = _FEATURE_ROW.match(line.strip())
        if match:
            rows[match.group("name")] = match.group("enabled") == "true"
    if not rows:
        return None
    problems = []
    for feature in CODEX_DISABLED_FEATURES:
        if feature not in rows:
            problems.append(feature + " (not listed)")
        elif rows[feature]:
            problems.append(feature)
    for name, enabled in rows.items():
        if not enabled or name in CODEX_DISABLED_FEATURES:
            continue
        if CODEX_RISKY_FEATURE_TOKENS.intersection(name.split("_")):
            problems.append(name)
        elif name not in CODEX_REVIEWED_ENABLED_FEATURES:
            problems.append(name + " (not reviewed)")
    return problems

def build_argv(provider: str, executable: Path, prompt: str, env: dict[str, str]) -> tuple[list[str], str | None]:
    """The command for one executor run, plus anything fed on stdin.

    These arguments carry the isolation the home cannot express on its own: the
    home controls what is on disk, these control what the CLI loads anyway.
    """
    profile = EXECUTOR_PROFILES[provider]
    model, effort = str(profile["model"]), str(profile["effort"])
    if provider == "codex":
        # Codex is the only one of the four with a real filesystem sandbox.
        # #1052: a run escaped the sandbox through an approved request. Never use
        # --approve-for-me here: in codex 0.161 it appends approvals_reviewer=
        # "auto_review" and approval_policy="on-request" after every -c override
        # (utils/cli/src/shared_options.rs, take_auto_review_config_overrides), so a
        # -c approval_policy="never" beside it is void. Without it, `exec` keeps
        # approval_policy Never unless the reviewer resolves to auto_review
        # (exec/src/lib.rs, run_main); the isolated config.toml sets no reviewer.
        # A command the sandbox blocks then fails instead of escalating.
        # The isolated CODEX_HOME carries a config.toml with no MCP servers.
        return (
            _launcher(
                executable,
                [
                    "exec",
                    "--model", model,
                    "-c", f'model_reasoning_effort="{effort}"',
                    # Fast mode: the pinned profile names the service tier.
                    "-c", f'service_tier="{profile["serviceTier"]}"',
                    *codex_isolation_arguments(),
                    "-c", 'approval_policy="never"',
                    "--sandbox", "workspace-write",
                    "--skip-git-repo-check",
                    "--json",
                    prompt,
                ],
            ),
            None,
        )
    if provider == "codex-plan":
        # The advisor shares Codex's isolated home, hosted-tool restrictions,
        # and workspace-write sandbox; its worktree is disposable.
        return (
            _launcher(
                executable,
                [
                    "exec",
                    "--model", model,
                    "-c", f'model_reasoning_effort="{effort}"',
                    *codex_isolation_arguments(),
                    "-c", 'approval_policy="never"',
                    "--approve-for-me",
                    "--skip-git-repo-check",
                    "--json",
                    prompt,
                ],
            ),
            None,
        )
    if provider == "qwen":
        # Qwen exposes no effort selector. The pinned profile therefore names
        # its model-mandated thinking instead of declaring an ignored level.
        # Isolation comes from the separate HOME and git worktree, not an
        # approval prompt. This run is non-interactive, so a prompt is a denial.
        # safe-mode and bare still remove ambient capability and discovery.
        return (
            _launcher(
                executable,
                [
                    "--safe-mode",
                    "--bare",
                    "--approval-mode", "yolo",
                    "--auth-type", str(profile["authType"]),
                    "--openai-base-url", str(profile["endpoint"]),
                    "--model", model,
                    # This CLI's JSON stream carries every turn's reasoning and
                    # reaches 256 KB before the final answer; text records it.
                    "--output-format", "text",
                    "--chat-recording", "false",
                    "--max-subagent-depth", "1",
                    "--max-wall-time", f"{MAX_TIMEOUT_SECONDS}s",
                    "--max-tool-calls", "1000",
                    "--exclude-tools", "web_fetch", "web_search",
                    "-p", prompt,
                ],
            ),
            None,
        )
    if provider == "claude":
        return (
            _launcher(
                executable,
                [
                    "-p",
                    "--input-format", "text",
                    "--model", model,
                    "--effort", effort,
                    "--strict-mcp-config",
                    "--disable-slash-commands",
                    "--permission-mode", "acceptEdits",
                    "--output-format", "json",
                ],
            ),
            prompt,
        )
    if provider == "kimi":
        return _launcher(executable, ["-p", prompt, "--model", model, "--output-format", "stream-json"]), None
    env["OPENCODE_CONFIG_CONTENT"] = json.dumps(
        {
            "$schema": "https://opencode.ai/config.json",
            "model": model,
            "mcp": {},
            "permission": {"*": "allow"},
            "subagent_depth": 0,
        }
    )
    return _launcher(executable, ["run", "--pure", "--model", model, "--variant", effort, prompt]), None
