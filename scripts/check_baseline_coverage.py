#!/usr/bin/env python3
"""Check that docs/MCP_TOOL_BASELINE.md's coverage arithmetic still holds.

The baseline claims "N of M advertised tools carry a measured number" and names the
M - N that do not. Both halves rot silently:

- M is a denominator. Land a new tool and the claim is wrong the same day, without
  anything in the document changing.
- the gap list is prose. It said "the first seven of those are one short window away"
  when the list held eight of them, and they were the last eight rather than the first
  seven -- a count of rows, and a position in a table, are both wrong as soon as a row
  moves.

So this compares the document against the tool list discovered from source, and against
itself. It does not check whether any measurement is *correct*; only that the document
does not contradict the code or its own tables.

Deliberately not checked: whether a tool named in the gap list also appears in a
measured row elsewhere. `saved_viewpoints_import` legitimately does -- its refusal path
was measured and its import path was not -- so that check would fail on a true
statement.

Run with --selftest to exercise the parser against fixtures.
"""

from __future__ import annotations

import argparse
import importlib
import re
import sys
from pathlib import Path

BASELINE_PATH = Path("docs") / "MCP_TOOL_BASELINE.md"
GAP_HEADING = "## What still has no number"
OUT_OF_REACH_LABEL = "- **out of reach**"
REACHABLE_LABEL = "- **one short window away**"

COVERAGE_RE = re.compile(r"\*\*(\d+) of (\d+)\*\* advertised tools")
REMAINING_RE = re.compile(r"The remaining \*\*(\d+)\*\* are named in")
REACHABLE_COUNT_RE = re.compile(r"the remaining \*\*(\d+)\*\*")
OUT_OF_REACH_COUNT_RE = re.compile(r"\*\*(\d+)\*\* of those are not reachable")
BACKTICKED = re.compile(r"`([a-z0-9_]+)`")


def repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


def advertised_tool_names(root: Path) -> set[str]:
    """The tool list as the catalogue guard discovers it from source."""
    sys.path.insert(0, str(root / "scripts"))
    try:
        catalog = importlib.import_module("check_mcp_command_catalog")
    finally:
        sys.path.pop(0)
    return {tool.tool_name for tool in catalog.extract_tools(root)}


def gap_section(text: str) -> str:
    if GAP_HEADING not in text:
        raise ValueError(f"{BASELINE_PATH} has no {GAP_HEADING!r} section")
    after = text.split(GAP_HEADING, 1)[1]
    # Up to the next second-level heading, so a later section cannot leak in.
    return after.split("\n## ", 1)[0]


def gap_table_tools(section: str) -> list[str]:
    """Tool names from the first cell of every table row in the gap section."""
    names: list[str] = []
    for line in section.splitlines():
        if not line.startswith("| `"):
            continue
        first_cell = line.split("|")[1]
        names.extend(BACKTICKED.findall(first_cell))
    return names


def bullet_block(section: str, label: str) -> str:
    """One bullet's text, including its wrapped continuation lines."""
    if label not in section:
        raise ValueError(f"the gap section has no {label!r} bullet")
    after = section.split(label, 1)[1]
    lines = [after.split("\n", 1)[0]]
    for line in after.split("\n")[1:]:
        if line.startswith("- ") or not line.strip():
            break
        lines.append(line)
    return "\n".join(lines)


def check(text: str, advertised: set[str]) -> list[str]:
    problems: list[str] = []

    coverage = COVERAGE_RE.search(text)
    if coverage is None:
        return [f"{BASELINE_PATH}: no '**N of M** advertised tools' claim found"]
    measured, total = int(coverage.group(1)), int(coverage.group(2))

    if total != len(advertised):
        problems.append(
            f"the baseline says {total} advertised tools; source discovery finds "
            f"{len(advertised)}. A tool landed or left without the baseline being "
            f"updated -- fix the '**{measured} of {total}**' row."
        )

    section = gap_section(text)
    listed = gap_table_tools(section)
    duplicates = sorted({name for name in listed if listed.count(name) > 1})
    if duplicates:
        problems.append(f"the gap table names these tools more than once: {duplicates}")

    unknown = sorted(set(listed) - advertised)
    if unknown:
        problems.append(
            f"the gap table names tools that no longer exist in source: {unknown}"
        )

    remaining = REMAINING_RE.search(text)
    if remaining is None:
        problems.append(f"{BASELINE_PATH}: no 'The remaining **N** are named in' claim")
    elif int(remaining.group(1)) != len(listed):
        problems.append(
            f"the baseline says {remaining.group(1)} tools have no number, but the gap "
            f"table lists {len(listed)}"
        )

    if measured + len(listed) != total:
        problems.append(
            f"{measured} measured + {len(listed)} listed as unmeasured != {total} "
            f"advertised; the coverage row and the gap table disagree"
        )

    out_of_reach = set(BACKTICKED.findall(bullet_block(section, OUT_OF_REACH_LABEL)))
    reachable_block = bullet_block(section, REACHABLE_LABEL)
    reachable = set(BACKTICKED.findall(reachable_block))

    both = sorted(out_of_reach & reachable)
    if both:
        problems.append(f"these tools are called both reachable and out of reach: {both}")

    split_total = out_of_reach | reachable
    missing = sorted(set(listed) - split_total)
    if missing:
        problems.append(
            f"the gap table lists these, but neither bullet says which they are: {missing}"
        )
    extra = sorted(split_total - set(listed))
    if extra:
        problems.append(
            f"the bullets name these, but the gap table does not list them: {extra}"
        )

    stated = REACHABLE_COUNT_RE.search(reachable_block)
    if stated is None:
        problems.append(
            "the 'one short window away' bullet states no count; write it as "
            "'the remaining **N**' so it can be checked"
        )
    elif int(stated.group(1)) != len(reachable):
        problems.append(
            f"the bullet says {stated.group(1)} tools are one window away, but names "
            f"{len(reachable)}"
        )

    # Both sides of the split, not just one. Checking the reachable count while leaving
    # the out-of-reach count as unchecked prose lets a tool move between the bullets and
    # leave the sentence above them stale -- the same rot this guard exists to catch.
    stated_out = OUT_OF_REACH_COUNT_RE.search(section)
    if stated_out is None:
        problems.append(
            "the gap section states no out-of-reach count; write it as "
            "'**N** of those are not reachable' so it can be checked"
        )
    elif int(stated_out.group(1)) != len(out_of_reach):
        problems.append(
            f"the section says {stated_out.group(1)} tools are out of reach, but the "
            f"bullet names {len(out_of_reach)}"
        )

    return problems


FIXTURE_GOOD = """# Baseline

| tools covered | **2 of 4** advertised tools carry a measured number. The remaining **2** are named in [What still has no number](#what-still-has-no-number), with the reason for each. |

## What still has no number

| tool | why |
| --- | --- |
| `alpha` | never run on purpose. |
| `beta` | needs an authored XML. |

**1** of those are not reachable in a window at all:

- **out of reach** — `alpha`, because every window depends on it not running.
- **one short window away** — the remaining **1**: `beta`, which needs
  nothing special.

## Re-running it comparably
"""


def selftest() -> int:
    advertised = {"alpha", "beta", "gamma", "delta"}
    cases: list[tuple[str, str, str]] = []

    cases.append(("a document that agrees with itself", FIXTURE_GOOD, ""))
    cases.append((
        "a tool landed without the baseline being updated",
        FIXTURE_GOOD.replace("**2 of 4**", "**2 of 3**"),
        "source discovery finds",
    ))
    cases.append((
        "the gap table grew but the prose count did not",
        FIXTURE_GOOD.replace("| `beta` | needs an authored XML. |",
                             "| `beta` | needs an authored XML. |\n| `gamma` | not reached. |"),
        "but the gap table lists 3",
    ))
    cases.append((
        "a listed tool no longer exists in source",
        FIXTURE_GOOD.replace("`beta`", "`renamed_away`"),
        "no longer exist in source",
    ))
    cases.append((
        "the reachable bullet miscounts what it names",
        FIXTURE_GOOD.replace("the remaining **1**: `beta`", "the remaining **7**: `beta`"),
        "but names 1",
    ))
    cases.append((
        "a listed tool is in neither bullet",
        FIXTURE_GOOD.replace(
            "- **out of reach** — `alpha`, because every window depends on it not running.\n",
            "- **out of reach** — nothing here.\n"),
        "neither bullet says which they are",
    ))
    cases.append((
        "a tool is called both reachable and out of reach",
        FIXTURE_GOOD.replace("the remaining **1**: `beta`",
                             "the remaining **2**: `alpha`, `beta`"),
        "both reachable and out of reach",
    ))
    cases.append((
        "the out-of-reach count went stale while the reachable one was updated",
        FIXTURE_GOOD.replace("**1** of those are not reachable",
                             "**4** of those are not reachable"),
        "says 4 tools are out of reach, but the bullet names 1",
    ))

    failures = 0
    for name, fixture, expected in cases:
        problems = check(fixture, advertised)
        joined = " | ".join(problems)
        if expected == "":
            ok = not problems
        else:
            ok = any(expected in problem for problem in problems)
        print(f"  {'ok  ' if ok else 'FAIL'}  {name}")
        if not ok:
            failures += 1
            print(f"        expected {expected!r}, got: {joined or '(no problems)'}")

    print(f"{len(cases) - failures} of {len(cases)} self-test cases passed")
    return 1 if failures else 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--selftest", action="store_true",
                        help="exercise the parser against fixtures and exit")
    args = parser.parse_args(argv)

    if args.selftest:
        return selftest()

    root = repo_root()
    path = root / BASELINE_PATH
    if not path.exists():
        print(f"{BASELINE_PATH} is missing")
        return 1

    problems = check(path.read_text(encoding="utf-8"), advertised_tool_names(root))
    if problems:
        print(f"{BASELINE_PATH} contradicts the code or itself:")
        for problem in problems:
            print(f"  - {problem}")
        return 1

    print(f"{BASELINE_PATH}: coverage arithmetic agrees with the tool list and itself.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
