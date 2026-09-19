#!/usr/bin/env python3
"""Guard the agent instruction surface against regrowth and duplication.

Before this guard existed, AGENTS.md and CLAUDE.md were both loaded on every
session and 59% of AGENTS.md was duplicated verbatim in CLAUDE.md. The copies had
already drifted: CLAUDE.md described a retired ShortestDistanceMarker workflow as
current. Nothing caught it, because nothing checked.

The rules below are the boundaries that keep that from coming back.
"""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path


def repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


# Byte ceilings for the files every agent loads on every session. Raising one is a
# deliberate act: say in the PR why the always-loaded context should cost more.
SIZE_LIMITS = {
    "AGENTS.md": 9000,
    "CLAUDE.md": 1500,
}

# Sections that belong to exactly one document. A heading listed here must not
# appear in AGENTS.md or CLAUDE.md, whatever wording it arrives in.
OWNED_SECTIONS = {
    "Architecture": "docs/ARCHITECTURE.md",
    "Plugin System": "docs/ARCHITECTURE.md",
    "Key Plugins": "docs/ARCHITECTURE.md",
    "AI Integration Layer": "docs/ARCHITECTURE.md",
    "Core Utilities": "docs/ARCHITECTURE.md",
    "Navisworks API Patterns": "docs/ARCHITECTURE.md",
    "Redline (Markup) JSON Format": "docs/ARCHITECTURE.md",
    "WorldToRedline Projection": "docs/ARCHITECTURE.md",
    "Internal API Access via Reflection": "docs/ARCHITECTURE.md",
    "Solution Structure": "docs/ARCHITECTURE.md",
    "Common Pitfalls": "docs/ARCHITECTURE.md",
    "Conditional Compilation": "docs/ARCHITECTURE.md",
    "Build Commands": "BUILD_BUNDLE_RULES.md",
}

# Sections the task brief template must keep. A brief missing one of these is the
# shape of task that grows into a rewrite.
REQUIRED_BRIEF_SECTIONS = ["Outcome", "Boundaries", "Acceptance", "Files", "Verify", "Links"]

BRIEF_LIMIT_BYTES = 2048

# Rules that must survive verbatim in AGENTS.md. These are safety boundaries, not
# style: each one exists because ignoring it cost something.
REQUIRED_SAFETY_LINES = [
    "Do not use `ANTHROPIC_API_KEY`.",
    "Do not allow Claude to edit files, run tools/commands, commit, push, publish, or perform release actions.",
    "Direct development on `main` and direct push to `main` are prohibited.",
    "`scripts/check_navishelper_compile.py` enforces this reachability guard.",
]


def headings(text: str) -> list[str]:
    return [line.lstrip("#").strip() for line in text.splitlines() if line.startswith("#")]


def check_sizes(root: Path, failures: list[str]) -> None:
    for name, limit in SIZE_LIMITS.items():
        path = root / name
        if not path.exists():
            failures.append(f"{name} is missing; it is required agent instruction surface")
            continue
        size = len(path.read_bytes())
        if size > limit:
            failures.append(
                f"{name} is {size} bytes, over the {limit}-byte limit. "
                "Move reference material into the document that owns it instead of growing "
                "the file every session loads."
            )


def check_owned_sections(root: Path, failures: list[str]) -> None:
    for name in SIZE_LIMITS:
        path = root / name
        if not path.exists():
            continue
        found = headings(path.read_text(encoding="utf-8-sig"))
        for heading in found:
            for owned, owner in OWNED_SECTIONS.items():
                if heading.startswith(owned):
                    failures.append(
                        f"{name} has a '{heading}' section, which belongs to {owner}. "
                        "Link to it instead of copying it."
                    )


def check_safety_lines(root: Path, failures: list[str]) -> None:
    path = root / "AGENTS.md"
    if not path.exists():
        return
    text = path.read_text(encoding="utf-8-sig")
    for line in REQUIRED_SAFETY_LINES:
        if line not in text:
            failures.append(f"AGENTS.md no longer contains the safety rule: {line!r}")


def check_brief(root: Path, failures: list[str]) -> None:
    path = root / "docs" / "TASK_BRIEF.md"
    if not path.exists():
        failures.append("docs/TASK_BRIEF.md is missing")
        return
    text = path.read_text(encoding="utf-8-sig")
    for section in REQUIRED_BRIEF_SECTIONS:
        if f"## {section}" not in text:
            failures.append(f"docs/TASK_BRIEF.md has no '## {section}' section")

    match = re.search(r"```markdown\n(.*?)```", text, re.S)
    if match is None:
        failures.append("docs/TASK_BRIEF.md has no ```markdown template block to copy")
    else:
        size = len(match.group(1).encode("utf-8"))
        if size > BRIEF_LIMIT_BYTES:
            failures.append(
                f"the brief template is {size} bytes, over the {BRIEF_LIMIT_BYTES}-byte limit "
                "the template itself states"
            )


def check_retired_docs(root: Path, failures: list[str]) -> None:
    """The retired role documents must stay stubs that point at AGENTS.md."""
    for name in ("PRIMARY_DEVELOPER.md", "SECONDARY_DEVELOPER.md"):
        path = root / "docs" / "agent-roles" / name
        if not path.exists():
            continue
        text = path.read_text(encoding="utf-8-sig")
        if len(text.encode("utf-8")) > 1200:
            failures.append(
                f"docs/agent-roles/{name} has grown past a stub. Role-specific instructions "
                "were retired; put the rule in AGENTS.md or delete it."
            )
        if "AGENTS.md" not in text:
            failures.append(f"docs/agent-roles/{name} no longer points at AGENTS.md")

    agents = (root / "AGENTS.md")
    if agents.exists() and "agent-roles" in agents.read_text(encoding="utf-8-sig"):
        failures.append(
            "AGENTS.md links to docs/agent-roles again. Those documents are retired stubs; "
            "no task should be told to read them."
        )


def check_permissions(root: Path, failures: list[str]) -> None:
    """The allowlist must stay read-only, with dangerous forms denied explicitly."""
    path = root / ".claude" / "settings.json"
    if not path.exists():
        failures.append(".claude/settings.json is missing")
        return

    try:
        settings = json.loads(path.read_text(encoding="utf-8-sig"))
    except json.JSONDecodeError as error:
        failures.append(f".claude/settings.json is not valid JSON: {error}")
        return

    permissions = settings.get("permissions") or {}
    allow = permissions.get("allow") or []
    deny = permissions.get("deny") or []

    # A bare `git branch *` allow rule would auto-approve `git branch -D`, which
    # deletes an unmerged branch. Only reading forms may be allowed.
    if any(rule.startswith(("Bash(git branch ", "PowerShell(git branch ")) and
           not re.match(r"^\w+\(git branch (--list|-r|-a|-vv|--merged|--contains)", rule)
           for rule in allow):
        failures.append(
            "an allow rule covers `git branch` beyond its reading forms. Allow only "
            "--list, -r, -a, -vv, --merged and --contains: a prefix rule on `git branch` "
            "also auto-approves `git branch -D`."
        )

    for forbidden in ("push", "reset --hard", "clean", "stash", "worktree remove", "rebase"):
        if any(f"git {forbidden}" in rule for rule in allow):
            failures.append(f"`git {forbidden}` must never be on the allowlist")

    required_denies = [
        # A flat `git push` deny cannot be cleared by approving the prompt, and it
        # blocks the ordinary one-branch-one-PR step this repository runs on. Deny
        # the forms that lose work or bypass review; let a plain branch push ask.
        "Bash(git push --force*)",
        "Bash(git push -f*)",
        "Bash(git push --delete*)",
        "Bash(git push origin main*)",
        "Bash(git branch -D*)",
        "Bash(git reset --hard*)",
        "Bash(git worktree remove*)",
        "Bash(scripts/start_navisworks.ps1*)",
        "Bash(scripts/navishelper_redline_live_smoke.ps1*)",
    ]
    for rule in required_denies:
        if rule not in deny:
            failures.append(f"the deny list is missing {rule!r}")

    # Every live-system script must be denied by name. Relying on the allowlist
    # simply not matching is how a live run gets auto-approved by a wider rule
    # added later.
    live_scripts = sorted(
        path.name
        for path in (root / "scripts").glob("*")
        if path.is_file()
        and path.suffix in {".ps1", ".py"}
        and ("smoke" in path.name or "stress" in path.name or "soak" in path.name
             or "regression" in path.name or "start_navisworks" in path.name
             or path.name.startswith("test_"))
        and "scenario_library" not in path.name
    )
    for script in live_scripts:
        if not any(script in rule for rule in deny):
            failures.append(
                f"scripts/{script} can touch a live system or installer but is not denied by name"
            )


def main() -> int:
    root = repo_root()
    failures: list[str] = []

    check_sizes(root, failures)
    check_owned_sections(root, failures)
    check_safety_lines(root, failures)
    check_brief(root, failures)
    check_retired_docs(root, failures)
    check_permissions(root, failures)

    if failures:
        print("Agent instruction surface check failed:")
        for failure in failures:
            print(f"  - {failure}")
        return 1

    sizes = ", ".join(
        f"{name} {len((root / name).read_bytes())}B" for name in SIZE_LIMITS if (root / name).exists()
    )
    print(f"Agent instruction surface check passed: {sizes}; brief, role stubs and allowlist intact.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
