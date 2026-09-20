#!/usr/bin/env python3
"""Every truncation flag on a response contract must be documented.

A tool that returns a partial answer says so with a `*Truncated` boolean. That is a
reasonable contract for a machine client -- better than a warning string it would have to
parse -- but only if the client knows the field exists. A flag nobody documented is a
partial answer delivered silently.

Measured when this guard was written: 9 of 23 truncation flags across 16 response types
appeared nowhere under `docs/`, including `SelectionExportPropertiesResponse.RowsTruncated`
on a tool that writes a file, where the consequence is a report short of rows with nothing
in the response text to say so.

The check is deliberately cheap and blunt: the flag's name must appear somewhere in
`docs/`, in PascalCase, camelCase or snake_case. It does not try to judge whether the
prose is good. It exists so that adding a truncation flag forces a sentence about it, and
so this cannot regress quietly.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path


def repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


def pascal_to_snake(value: str) -> str:
    return re.sub(r"(?<!^)(?=[A-Z])", "_", value).lower()


def collect_flags(contracts_dir: Path) -> dict[str, set[str]]:
    """Maps a truncation flag name to the response types that declare it."""
    flags: dict[str, set[str]] = {}
    class_pattern = re.compile(r"public\s+(?:sealed\s+)?class\s+(\w+)")
    flag_pattern = re.compile(r"public\s+bool\??\s+(\w*Truncated)\s*\{")

    for path in sorted(contracts_dir.rglob("*.cs")):
        text = path.read_text(encoding="utf-8-sig")
        current_class = "<unknown>"
        for line in text.splitlines():
            class_match = class_pattern.search(line)
            if class_match:
                current_class = class_match.group(1)
                continue
            flag_match = flag_pattern.search(line)
            if flag_match:
                flags.setdefault(flag_match.group(1), set()).add(current_class)
    return flags


def documentation_text(docs_dir: Path) -> str:
    return "\n".join(
        path.read_text(encoding="utf-8-sig")
        for path in sorted(docs_dir.rglob("*.md"))
    )


def main() -> int:
    root = repo_root()
    contracts = root / "NavisHelper.Contracts"
    docs = root / "docs"

    if not contracts.is_dir():
        print(f"{contracts} is missing; re-point this guard.")
        return 1
    if not docs.is_dir():
        print(f"{docs} is missing; re-point this guard.")
        return 1

    flags = collect_flags(contracts)
    if not flags:
        print("No truncation flags found in the contracts; re-point this guard.")
        return 1

    text = documentation_text(docs)
    undocumented: list[tuple[str, set[str]]] = []
    for name, owners in sorted(flags.items()):
        camel = name[0].lower() + name[1:]
        if name in text or camel in text or pascal_to_snake(name) in text:
            continue
        undocumented.append((name, owners))

    if undocumented:
        print("Truncation flags that appear in no document under docs/:")
        for name, owners in undocumented:
            print(f"  - {name}   on {', '.join(sorted(owners))}")
        print()
        print("A caller cannot handle a partial answer it does not know it can receive.")
        print("Document the flag where its tool is documented, then re-run this guard.")
        return 1

    print(f"All {len(flags)} truncation flags are documented.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
