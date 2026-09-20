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
from dataclasses import dataclass
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

# The powershell invocation the repository's own documentation prescribes for live
# scripts (docs/MCP_DEVELOPMENT_PLAN.md, docs/MCP_CLIENT_GUIDE.md). A deny rule on the
# bare `scripts/<x>` path never matches this form.
DOCUMENTED_PS_PREFIX = "powershell -NoProfile -ExecutionPolicy Bypass -File"

# Scripts that can reach a live Navisworks, an installer, or the user's machine state.
LIVE_SCRIPT_NAMES = (
    "navishelper_host_smoke",
    "navishelper_host_stress",
    "navishelper_mcp_failure_modes",
    "navishelper_mcp_regression",
    "navishelper_mcp_soak",
    "navishelper_mcp_extended_soak",
    "navishelper_mcp_smoke",
    "navishelper_mcp_mixed_stress",
    "navishelper_redline_live_smoke",
    "start_navisworks",
    "test_installer_upgrade",
    "test_package_install",
    "live-smoke",
)

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

    # Interpreters and shells must never be on the allowlist. Permission patterns are
    # matched against the command prefix, so no deny list can enumerate every way of
    # spelling `powershell ... -File scripts\<live script>`. What IS enforceable is
    # that nothing auto-approves an interpreter in the first place, which is why this
    # check matters more than the deny entries below.
    for launcher in ("powershell", "pwsh", "cmd", "cmd.exe", "bash -c", "sh -c", "Start-Process"):
        if any(launcher in rule for rule in allow):
            failures.append(
                f"an allow rule mentions {launcher!r}. An interpreter on the allowlist "
                "auto-approves any script it is pointed at, including the live ones, and "
                "prefix-matched denies cannot enumerate every spelling of that command."
            )

    for script in LIVE_SCRIPT_NAMES:
        if any(script in rule for rule in allow):
            failures.append(f"an allow rule mentions {script!r}; live-system scripts must ask")

    # Accepting a change is the owner's, and that is expressed by never
    # auto-approving it: absence from the allowlist already means every merge stops
    # and asks. A hard deny would add no safety - it would only stop the owner from
    # delegating the keystroke on a decision they had already made.
    for irreversible in ("gh pr merge", "gh release", "gh repo delete", "gh api -X PUT",
                         "gh api --method PUT"):
        if any(irreversible in rule for rule in allow):
            failures.append(
                f"an allow rule mentions {irreversible!r}. Accepting or publishing a change "
                "must ask the owner every time; it may never be auto-approved."
            )

    required_denies = [
        # A flat `git push` deny cannot be cleared by approving the prompt, and it
        # blocks the ordinary one-branch-one-PR step this repository runs on. Deny
        # the forms that lose work or bypass review; let a plain branch push ask.
        "Bash(git push --force*)",
        "Bash(git push -f*)",
        "Bash(git push --delete*)",
        "Bash(git push origin main*)",
        "Bash(git push origin refs/heads/main*)",
        "Bash(git push origin HEAD:refs/heads/main*)",
        "Bash(git branch -D*)",
        "Bash(git reset --hard*)",
        "Bash(git worktree remove*)",
        "Bash(scripts/start_navisworks.ps1*)",
        "Bash(scripts/navishelper_redline_live_smoke.ps1*)",
        # The invocation the repository's own docs prescribe. A deny on `scripts/<x>`
        # alone does not match it, because patterns match the command prefix.
        "Bash(" + DOCUMENTED_PS_PREFIX + " scripts\\navishelper_redline_live_smoke.ps1*)",
        "Bash(" + DOCUMENTED_PS_PREFIX + " scripts\\start_navisworks.ps1*)",
    ]
    for rule in required_denies:
        if rule not in deny:
            failures.append(f"the deny list is missing {rule!r}")

    # Every live-system script must be denied, and the guard has to be able to see
    # it in order to say so. Relying on the allowlist simply not matching is how a
    # live run gets auto-approved by a wider rule added later.
    #
    # rglob, not glob: scripts/live-smoke/ already existed while a non-recursive
    # glob read nothing about it and passed. That is the failure shape that stays
    # green, so when a pin is repointed the answer to "does it read more or less
    # now?" has to be more. At the time of this change it reads 12 scripts where
    # the non-recursive glob read 11.
    live_scripts = sorted(
        path.relative_to(root).as_posix()
        for path in (root / "scripts").rglob("*")
        if path.is_file()
        and path.suffix in {".ps1", ".py"}
        and ("smoke" in path.name or "stress" in path.name or "soak" in path.name
             or "regression" in path.name or "start_navisworks" in path.name
             or path.name.startswith("test_"))
        and "scenario_library" not in path.name
    )
    deny_rules = [_parse_permission_rule(rule) for rule in deny]
    for script in live_scripts:
        covering = [rule for rule in deny_rules if _rule_covers(rule, script)]
        if not covering:
            failures.append(
                f"{script} can touch a live system or installer but no deny rule "
                "covers it"
            )
            continue

        # Each type is required in the form the documentation actually runs it,
        # because another form is a different command prefix and therefore a
        # different rule.
        if script.endswith(".ps1"):
            if not any(rule.is_bare_path for rule in covering):
                failures.append(f"{script} is not denied as a bare path")
            if not any(rule.has_file_flag for rule in covering):
                failures.append(
                    f"{script} is denied only as a bare path; the documented "
                    "powershell -File form is not covered"
                )
        elif script.endswith(".py"):
            if not any(rule.is_python_form or rule.is_bare_path for rule in covering):
                failures.append(
                    f"{script} is denied, but not in the `python {script}` form it "
                    "is actually run with"
                )


@dataclass(frozen=True)
class PermissionRule:
    """One deny pattern, reduced to the path it blocks and how it invokes it.

    Permission patterns match a **command prefix**, so the path a rule blocks is
    the last token of the command it names with the trailing wildcard removed.
    A rule may therefore block a whole directory: `Bash(scripts/live-smoke/*)`
    covers every script under it, more broadly than naming the file would. The
    live-script check has to accept that instead of demanding a redundant
    per-file rule, which is why coverage is a prefix test and not a substring
    search for the file name.
    """

    path: str
    is_bare_path: bool
    has_file_flag: bool
    is_python_form: bool


def _parse_permission_rule(rule: str) -> PermissionRule:
    inner = rule
    if "(" in rule and rule.endswith(")"):
        inner = rule[rule.index("(") + 1 : -1]
    inner = inner.rstrip("*")
    tokens = inner.split()
    if not tokens:
        return PermissionRule("", False, False, False)

    path = tokens[-1].replace("\\", "/")
    if path.startswith("./"):
        path = path[2:]
    return PermissionRule(
        path=path,
        is_bare_path=len(tokens) == 1,
        has_file_flag="-File" in tokens,
        is_python_form=tokens[0] in {"python", "python.exe", "py"},
    )


def _rule_covers(rule: PermissionRule, script: str) -> bool:
    if not rule.path:
        return False
    if rule.path.endswith("/"):
        return script.startswith(rule.path)
    return script == rule.path


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
