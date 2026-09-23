#!/usr/bin/env python3
"""Check that AGENTS.md's list of files over 80 KB matches the repository.

AGENTS.md section 3 says which files are over 80 KB and must only get targeted
patches, never a whole rewrite. Nothing checked that sentence: a file can grow
past the limit unnoticed, and a file that gets split stays listed forever. Over
80 KB an agent cannot read and repair a file in one session, so the list is a
working constraint, not prose.

Size is the working-tree file's bytes with each CRLF counted as one byte --
what git stores, so a Windows and a Linux checkout of the same commit measure
the same. The limit is 81 920 bytes (80 KiB, what `find -size +80k` means).

The bullet's backticked tokens name the covered files: a token ending in `/`
names a directory whose over-limit files are covered as a class; every other
token is a path or `*` glob relative to the repository root, matched against
`git ls-files`. A path or glob token must match at least one tracked file, and
every tracked file it matches must be over the limit. A missing bullet fails
loudly, so the guard can never pass by reading an empty list.

Run with --selftest to exercise the checker against fixtures.
"""

from __future__ import annotations

import argparse
import fnmatch
import re
import subprocess
import sys
from pathlib import Path

BULLET_MARKER = "- Files over 80 KB"

LIMIT = 80 * 1024

TOKEN_RE = re.compile(r"`([^`]+)`")

GLOB_CHARS = set("*?[")


def repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


def extract_tokens(text: str) -> tuple[list[str], list[str]]:
    """The bullet's backticked tokens, plus problems found while parsing it."""
    problems: list[str] = []
    lines = text.splitlines()
    bullet: list[str] = []
    for index, line in enumerate(lines):
        if line.lstrip().startswith(BULLET_MARKER):
            bullet.append(line)
            after = index + 1
            while after < len(lines) and lines[after].startswith("  "):
                bullet.append(lines[after])
                after += 1
            break
    if not bullet:
        problems.append(
            f"AGENTS.md has no '{BULLET_MARKER}' bullet, so there is no list to check "
            "against -- this guard refuses to pass by reading an empty list"
        )
        return [], problems
    tokens = [token.strip() for token in TOKEN_RE.findall("\n".join(bullet))]
    tokens = [token for token in tokens if token]
    if not tokens:
        problems.append(
            f"the '{BULLET_MARKER}' bullet names no backticked paths, globs or directories"
        )
    return tokens, problems


def token_matches(token: str, path: str) -> bool:
    if token.endswith("/"):
        return path.startswith(token)
    if GLOB_CHARS & set(token):
        return fnmatch.fnmatchcase(path, token)
    return path == token


def check(tokens: list[str], tracked: list[str], sizes: dict[str, int],
          problems: list[str]) -> list[str]:
    problems = list(problems)
    tracked = sorted(tracked)

    for path in tracked:
        if sizes.get(path, 0) > LIMIT and not any(token_matches(t, path) for t in tokens):
            problems.append(
                f"{path} is {sizes[path]} bytes, over the {LIMIT}-byte limit, but nothing "
                "in AGENTS.md's list covers it -- add it to the bullet"
            )

    for token in tokens:
        if token.endswith("/"):
            continue
        matched = [path for path in tracked if token_matches(token, path)]
        if not matched:
            problems.append(
                f"AGENTS.md lists `{token}`, which matches no tracked file -- remove it "
                "from the bullet"
            )
            continue
        for path in matched:
            if sizes.get(path, 0) <= LIMIT:
                problems.append(
                    f"AGENTS.md lists `{token}`, but {path} is {sizes.get(path, 0)} bytes, "
                    "at or under the limit -- remove it from the list"
                )
    return problems


def tracked_sizes(root: Path) -> tuple[list[str], dict[str, int], list[str]]:
    """Tracked paths from `git ls-files` and their stored sizes, plus problems."""
    problems: list[str] = []
    listed = subprocess.run(["git", "ls-files", "-z"], cwd=root, capture_output=True)
    if listed.returncode != 0:
        detail = listed.stderr.decode("utf-8", errors="replace").strip()
        problems.append(f"git ls-files failed: {detail}")
        return [], {}, problems
    tracked = [path.decode("utf-8", errors="replace")
               for path in listed.stdout.split(b"\0") if path]
    sizes: dict[str, int] = {}
    for path in tracked:
        on_disk = root / path
        if on_disk.is_file():
            sizes[path] = len(on_disk.read_bytes().replace(b"\r\n", b"\n"))
    return tracked, sizes, problems


FIXTURES = {
    "agreeing": (
        ["NavisHelper/Properties/Resources*.resx",
         "NavisHelper.McpServer/Services/ScenarioLibraryService.cs", "docs/"],
        ["NavisHelper/Properties/Resources.resx", "NavisHelper/Properties/Resources.ru.resx",
         "NavisHelper.McpServer/Services/ScenarioLibraryService.cs",
         "docs/reference/README_FULL.md", "scripts/host_command_names_baseline.txt"],
        {"NavisHelper/Properties/Resources.resx": 138006,
         "NavisHelper/Properties/Resources.ru.resx": 165528,
         "NavisHelper.McpServer/Services/ScenarioLibraryService.cs": 110844,
         "docs/reference/README_FULL.md": 81965,
         "scripts/host_command_names_baseline.txt": 2048},
    ),
    "a file grown past the limit unnoticed": (
        ["NavisHelper/Properties/Resources*.resx", "docs/"],
        ["NavisHelper/Properties/Resources.resx", "NavisHelper/Properties/Resources.ru.resx",
         "docs/reference/README_FULL.md", "scripts/host_command_names_baseline.txt"],
        {"NavisHelper/Properties/Resources.resx": 138006,
         "NavisHelper/Properties/Resources.ru.resx": 165528,
         "docs/reference/README_FULL.md": 81965,
         "scripts/host_command_names_baseline.txt": LIMIT + 1024},
    ),
    "a listed file that no longer exists": (
        ["NavisHelper/Properties/Resources*.resx", "NavisHelper/NoSuchFile.cs"],
        ["NavisHelper/Properties/Resources.resx", "NavisHelper/Properties/Resources.ru.resx"],
        {"NavisHelper/Properties/Resources.resx": 138006,
         "NavisHelper/Properties/Resources.ru.resx": 165528},
    ),
    "a listed file back under the limit": (
        ["NavisHelper.McpServer/Services/ScenarioLibraryService.cs"],
        ["NavisHelper.McpServer/Services/ScenarioLibraryService.cs"],
        {"NavisHelper.McpServer/Services/ScenarioLibraryService.cs": 1024},
    ),
}

AGENTS_WITHOUT_THE_BULLET = """# Agents

- Files under 80 KB: rewrite freely.
"""


def selftest() -> int:
    cases = [
        ("a tree where the list matches reality", "agreeing", ""),
        ("a file grown past the limit unnoticed", "a file grown past the limit unnoticed",
         "scripts/host_command_names_baseline.txt"),
        ("a listed file that no longer exists", "a listed file that no longer exists",
         "NoSuchFile.cs"),
        ("a listed file back under the limit", "a listed file back under the limit",
         "remove it from the list"),
    ]
    failures = 0
    for name, fixture, expected in cases:
        tokens, tracked, sizes = FIXTURES[fixture]
        problems = check(tokens, tracked, sizes, [])
        ok = (not problems) if expected == "" else any(expected in p for p in problems)
        print(f"  {'ok  ' if ok else 'FAIL'}  {name}")
        if not ok:
            failures += 1
            print(f"        expected {expected!r}, got: {problems or '(none)'}")

    tokens, problems = extract_tokens(AGENTS_WITHOUT_THE_BULLET)
    ok = not tokens and any("refuses to pass" in p for p in problems)
    print(f"  {'ok  ' if ok else 'FAIL'}  AGENTS.md with the bullet removed")
    if not ok:
        failures += 1
        print(f"        expected a loud failure, got: {problems or '(none)'}")

    total = len(cases) + 1
    print(f"{total - failures} of {total} self-test cases passed")
    return 1 if failures else 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--selftest", action="store_true",
                        help="exercise the checker against fixtures and exit")
    args = parser.parse_args(argv)

    if args.selftest:
        return selftest()

    root = repo_root()
    tokens, problems = extract_tokens((root / "AGENTS.md").read_text(encoding="utf-8"))
    tracked, sizes, found = tracked_sizes(root)
    problems = check(tokens, tracked, sizes, problems + found)
    if problems:
        print("AGENTS.md's list of files over 80 KB does not match the repository:")
        for problem in problems:
            print(f"  - {problem}")
        return 1

    over = sum(1 for path in tracked if sizes.get(path, 0) > LIMIT)
    print(f"AGENTS.md's list of files over 80 KB matches the repository: "
          f"{over} tracked file(s) over the {LIMIT}-byte limit, "
          f"covered by {len(tokens)} token(s).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
