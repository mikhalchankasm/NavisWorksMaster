#!/usr/bin/env python3
"""Check or update the generated MCP tool index in docs/NAVISWORKS_MCP_COMMAND_CATALOG.md."""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path


BEGIN_MARKER = "<!-- BEGIN GENERATED MCP TOOL INDEX -->"
END_MARKER = "<!-- END GENERATED MCP TOOL INDEX -->"


@dataclass(frozen=True)
class ToolInfo:
    tool_name: str
    method_name: str
    source_path: str
    description: str


def repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


def pascal_to_snake(value: str) -> str:
    value = re.sub(r"(.)([A-Z][a-z]+)", r"\1_\2", value)
    value = re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", value)
    return value.lower()


def unescape_description(value: str) -> str:
    return (
        value.replace(r"\"", '"')
        .replace(r"\\", "\\")
        .replace("|", r"\|")
        .replace("\r", " ")
        .replace("\n", " ")
        .strip()
    )


def extract_tools(root: Path) -> list[ToolInfo]:
    tools_dir = root / "NavisHelper.McpServer" / "Tools"
    if not tools_dir.exists():
        raise FileNotFoundError(f"Tools directory not found: {tools_dir}")

    tools: list[ToolInfo] = []
    method_regex = re.compile(r"\bpublic\s+(?:async\s+)?[\w<>,\[\]\.\s]+\s+(?P<name>[A-Z]\w*)\s*\(")
    description_regex = re.compile(r'\[Description\("(?P<text>(?:\\.|[^"\\])*)"\)\]')

    for path in sorted(tools_dir.glob("*.cs")):
        lines = path.read_text(encoding="utf-8-sig").splitlines()
        index = 0
        while index < len(lines):
            if "[McpServerTool" not in lines[index]:
                index += 1
                continue

            description = ""
            method_name = ""
            for lookahead in range(index + 1, min(index + 30, len(lines))):
                description_match = description_regex.search(lines[lookahead])
                if description_match and not description:
                    description = unescape_description(description_match.group("text"))

                method_match = method_regex.search(lines[lookahead])
                if method_match:
                    method_name = method_match.group("name")
                    break

            if not method_name:
                raise ValueError(f"Could not find public tool method after [McpServerTool] in {path}:{index + 1}")

            relative_path = path.relative_to(root).as_posix()
            tools.append(
                ToolInfo(
                    tool_name=pascal_to_snake(method_name),
                    method_name=method_name,
                    source_path=relative_path,
                    description=description or "(no Description attribute)",
                )
            )
            index = lookahead + 1

    duplicate_names = sorted({tool.tool_name for tool in tools if sum(1 for item in tools if item.tool_name == tool.tool_name) > 1})
    if duplicate_names:
        raise ValueError("Duplicate MCP tool names after snake_case conversion: " + ", ".join(duplicate_names))

    return sorted(tools, key=lambda item: item.tool_name)


def render_index(tools: list[ToolInfo]) -> str:
    lines = [
        BEGIN_MARKER,
        "## Generated Implemented MCP Tool Index",
        "",
        "This section is generated from `NavisHelper.McpServer/Tools/*.cs` by `scripts/check_mcp_command_catalog.py --update`.",
        "It prevents the curated Russian command catalog from drifting behind the actual `[McpServerTool]` surface.",
        "",
        "| Tool | C# method | Source | Description |",
        "|---|---|---|---|",
    ]
    for tool in tools:
        lines.append(
            f"| `{tool.tool_name}` | `{tool.method_name}` | `{tool.source_path}` | {tool.description} |"
        )
    lines.extend(["", END_MARKER])
    return "\n".join(lines)


def replace_generated_section(catalog_text: str, generated: str) -> str:
    if BEGIN_MARKER in catalog_text or END_MARKER in catalog_text:
        pattern = re.compile(
            re.escape(BEGIN_MARKER) + r".*?" + re.escape(END_MARKER),
            flags=re.DOTALL,
        )
        return pattern.sub(lambda _match: generated, catalog_text)

    anchor = "\n## Что уже реально подтверждено"
    if anchor in catalog_text:
        return catalog_text.replace(anchor, "\n" + generated + "\n" + anchor, 1)

    return catalog_text.rstrip() + "\n\n" + generated + "\n"


def extract_catalog_tool_names(catalog_text: str) -> set[str]:
    return set(re.findall(r"\|\s*`([a-z][a-z0-9_]*)`\s*\|", catalog_text))


def extract_generated_tool_names(catalog_text: str) -> set[str]:
    if BEGIN_MARKER not in catalog_text or END_MARKER not in catalog_text:
        raise ValueError("Generated MCP tool index markers are missing from the catalog")
    start = catalog_text.index(BEGIN_MARKER)
    end = catalog_text.index(END_MARKER, start)
    return extract_catalog_tool_names(catalog_text[start:end])


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--update", action="store_true", help="rewrite the generated tool index in the catalog")
    args = parser.parse_args(argv)

    root = repo_root()
    catalog_path = root / "docs" / "NAVISWORKS_MCP_COMMAND_CATALOG.md"
    tools = extract_tools(root)
    generated = render_index(tools)
    catalog_text = catalog_path.read_text(encoding="utf-8-sig")

    if args.update:
        updated = replace_generated_section(catalog_text, generated)
        if updated != catalog_text:
            catalog_path.write_text(updated, encoding="utf-8", newline="\n")
            print(f"Updated {catalog_path} with {len(tools)} implemented MCP tools.")
        else:
            print(f"{catalog_path} is already up to date with {len(tools)} implemented MCP tools.")
        return 0

    catalog_names = extract_generated_tool_names(catalog_text)
    implemented_names = {tool.tool_name for tool in tools}
    missing = sorted(implemented_names - catalog_names)
    stale = sorted(catalog_names - implemented_names)
    if missing:
        print("Catalog is missing implemented MCP tools:")
        for name in missing:
            print(f"  - {name}")
        print("Run: python scripts/check_mcp_command_catalog.py --update")
        return 1
    if stale:
        print("Generated catalog contains stale MCP tools:")
        for name in stale:
            print(f"  - {name}")
        print("Run: python scripts/check_mcp_command_catalog.py --update")
        return 1

    print(f"Catalog covers all {len(tools)} implemented MCP tools.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
