#!/usr/bin/env python3
"""Check that every place declaring the product version agrees.

The version is written in eleven files and twenty-six places: four project files with a
`Version`/`AssemblyVersion`/`FileVersion` triplet each, two `AssemblyInfo.cs` pairs, two
`PackageContents.xml` manifests, the Inno Setup script, the installer script's default
argument, and the command the distribution plan tells you to run.

Nothing checked them against each other. `tools/validate_distribution.ps1` compares the
bundle manifest against three assemblies, but only after a package exists, and it cannot
see a source file that was missed -- so a release could be built from a half-bumped tree
and the mismatch would surface as a packaging failure at the end of the checklist, or not
at all for the files it does not read.

`NavisHelper.bundle/PackageContents.xml` is the authority here, because that is the
manifest Autodesk itself reads.

Run with --selftest to exercise the parser against fixtures.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

VERSION_RE = re.compile(r"\b\d+\.\d+\.\d+\.\d+\b")

AUTHORITY = "NavisHelper.bundle/PackageContents.xml"

# Every file that declares the *current* version, with the pattern that finds each
# declaration in it. A file listed here must have at least one match, so a renamed or
# restructured declaration fails loudly instead of being silently skipped.
DECLARATIONS: dict[str, list[str]] = {
    "NavisHelper.bundle/PackageContents.xml": [
        r'AppVersion="([\d.]+)"', r'FriendlyVersion="([\d.]+)"', r'\bVersion="([\d.]+)"',
    ],
    "NavisHelper/PackageContents.xml": [r'AppVersion="([\d.]+)"'],
    "NavisHelper.AiWorker/NavisHelper.AiWorker.csproj": [
        r"<Version>([\d.]+)</Version>", r"<AssemblyVersion>([\d.]+)</AssemblyVersion>",
        r"<FileVersion>([\d.]+)</FileVersion>",
    ],
    "NavisHelper.Contracts/NavisHelper.Contracts.csproj": [
        r"<Version>([\d.]+)</Version>", r"<AssemblyVersion>([\d.]+)</AssemblyVersion>",
        r"<FileVersion>([\d.]+)</FileVersion>",
    ],
    "NavisHelper.McpConfigurator/NavisHelper.McpConfigurator.csproj": [
        r"<Version>([\d.]+)</Version>", r"<AssemblyVersion>([\d.]+)</AssemblyVersion>",
        r"<FileVersion>([\d.]+)</FileVersion>",
    ],
    "NavisHelper.McpServer/NavisHelper.McpServer.csproj": [
        r"<Version>([\d.]+)</Version>", r"<AssemblyVersion>([\d.]+)</AssemblyVersion>",
        r"<FileVersion>([\d.]+)</FileVersion>",
    ],
    "NavisHelper/Properties/AssemblyInfo.cs": [
        r'AssemblyVersion\("([\d.]+)"\)', r'AssemblyFileVersion\("([\d.]+)"\)',
    ],
    "NavisHelper.Dev/Properties/AssemblyInfo.cs": [
        r'AssemblyVersion\("([\d.]+)"\)', r'AssemblyFileVersion\("([\d.]+)"\)',
    ],
    "installer/NavisHelper.iss": [r'#define AppVersion "([\d.]+)"'],
    "tools/build_installer.ps1": [r'\$AppVersion = "([\d.]+)"'],
    "docs/MCP_DISTRIBUTION_PLAN.md": [r"-AppVersion ([\d.]+)"],
}

# Files that mention a version and must NOT be dragged along, each for a stated reason.
# Listed so the exclusion is a decision rather than an oversight.
HISTORICAL = {
    "docs/MCP_TOOL_BASELINE.md": "records the plugin a measurement window actually ran",
    "docs/MCP_CLIENT_GUIDE.md": "records the surface a figure was measured on",
    "README.md": "verification snapshot: it names the version it measured, dated in place, "
                 "rather than tracking whatever the current one is",
    "README.ru.md": "same snapshot, Russian",
    "NavisHelper.McpServer.Tests/McpHealthVersionCompatibilityTests.cs":
        "version strings are fixtures for the comparison logic, not the product version",
}


def repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


def declared_versions(root: Path) -> tuple[dict[str, set[str]], list[str]]:
    """Every declared version per file, plus problems found while reading them."""
    found: dict[str, set[str]] = {}
    problems: list[str] = []
    for relative, patterns in DECLARATIONS.items():
        path = root / relative
        if not path.exists():
            problems.append(f"{relative} is missing, but it declares the product version")
            continue
        text = path.read_text(encoding="utf-8")
        versions: set[str] = set()
        for pattern in patterns:
            matches = re.findall(pattern, text)
            if not matches:
                problems.append(
                    f"{relative}: nothing matched {pattern!r}. The declaration was renamed or "
                    f"restructured, so this guard stopped checking it -- fix the pattern."
                )
            versions.update(matches)
        found[relative] = versions
    return found, problems


def check(found: dict[str, set[str]], problems: list[str]) -> list[str]:
    problems = list(problems)
    authority = found.get(AUTHORITY)
    if not authority:
        problems.append(f"{AUTHORITY} declares no version, so there is nothing to check against")
        return problems

    if len(authority) > 1:
        problems.append(
            f"{AUTHORITY} declares more than one version {sorted(authority)}; the manifest "
            f"Autodesk reads has to agree with itself first"
        )
        return problems

    expected = next(iter(authority))
    for relative, versions in sorted(found.items()):
        wrong = sorted(v for v in versions if v != expected)
        if wrong:
            problems.append(
                f"{relative} declares {wrong} where {AUTHORITY} says {expected}"
            )
    return problems


FIXTURES = {
    "agreeing": {AUTHORITY: {"2.10.0.0"}, "installer/NavisHelper.iss": {"2.10.0.0"}},
    "one file left behind": {AUTHORITY: {"2.10.0.0"}, "installer/NavisHelper.iss": {"2.9.0.0"}},
    "the authority disagrees with itself": {AUTHORITY: {"2.10.0.0", "2.9.0.0"}},
    "no authority at all": {"installer/NavisHelper.iss": {"2.10.0.0"}},
}


def selftest() -> int:
    cases = [
        ("a tree where every declaration agrees", "agreeing", [], ""),
        ("a single file left on the old version", "one file left behind", [],
         "installer/NavisHelper.iss declares ['2.9.0.0']"),
        ("the authority contradicting itself", "the authority disagrees with itself", [],
         "has to agree with itself first"),
        ("no authoritative manifest", "no authority at all", [],
         "nothing to check against"),
        ("a declaration whose pattern stopped matching", "agreeing",
         ["installer/NavisHelper.iss: nothing matched"],
         "nothing matched"),
    ]
    failures = 0
    for name, fixture, seeded, expected in cases:
        problems = check(dict(FIXTURES[fixture]), list(seeded))
        ok = (not problems) if expected == "" else any(expected in p for p in problems)
        print(f"  {'ok  ' if ok else 'FAIL'}  {name}")
        if not ok:
            failures += 1
            print(f"        expected {expected!r}, got: {problems or '(none)'}")
    print(f"{len(cases) - failures} of {len(cases)} self-test cases passed")
    return 1 if failures else 0


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--selftest", action="store_true",
                        help="exercise the comparison against fixtures and exit")
    args = parser.parse_args(argv)

    if args.selftest:
        return selftest()

    root = repo_root()
    found, problems = declared_versions(root)
    problems = check(found, problems)
    if problems:
        print("The product version is not declared consistently:")
        for problem in problems:
            print(f"  - {problem}")
        return 1

    version = next(iter(found[AUTHORITY]))
    places = sum(len(v) for v in found.values())
    print(f"Version {version} agrees across {len(found)} files "
          f"({places} distinct declared values, all equal).")
    print("Historical mentions deliberately untouched: " + ", ".join(sorted(HISTORICAL)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
