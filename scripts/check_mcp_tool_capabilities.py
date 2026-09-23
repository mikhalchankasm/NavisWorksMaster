#!/usr/bin/env python3
"""Generate or check the MCP tool capability table in MCP_TOOL_CONTRACTS.md."""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path


BEGIN = "<!-- BEGIN GENERATED TOOL CAPABILITIES -->"
END = "<!-- END GENERATED TOOL CAPABILITIES -->"
EFFECTS = ("None", "View", "Document", "Files", "Host", "LocalState")
TOOL = re.compile(r'\[McpServerTool(?:\(Name\s*=\s*"([^"]+)"\))?\]')
METHOD = re.compile(r'\bpublic\s+(?:async\s+)?[\w<>,\[\].?\s]+?\s+(\w+)\s*\(')
CAPABILITY = re.compile(r'\[ToolCapabilities\(([^\]]*)\)\]')


@dataclass(frozen=True)
class ToolInfo:
    name: str
    effects: tuple[str, ...]
    host: bool
    document: bool
    dry_run: bool


def snake(name: str) -> str:
    name = re.sub(r"(.)([A-Z][a-z]+)", r"\1_\2", name)
    return re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", name).lower()


def parameter_end(source: str, opening: int) -> int:
    depth = 0
    quoted = False
    escaped = False
    for index in range(opening, len(source)):
        char = source[index]
        if quoted:
            if escaped:
                escaped = False
            elif char == "\\":
                escaped = True
            elif char == '"':
                quoted = False
        elif char == '"':
            quoted = True
        elif char == "(":
            depth += 1
        elif char == ")":
            depth -= 1
            if depth == 0:
                return index
    raise ValueError("Unclosed tool parameter list")


def parse_capabilities(source: str, location: str) -> tuple[tuple[str, ...], bool, bool]:
    matches = CAPABILITY.findall(source)
    if len(matches) != 1:
        raise ValueError(f"{location}: expected exactly one ToolCapabilities attribute, found {len(matches)}")
    value = matches[0]
    declared = re.findall(r"ToolEffects\.(\w+)", value)
    if not declared or len(declared) != len(set(declared)) or any(effect not in EFFECTS for effect in declared):
        raise ValueError(f"{location}: invalid ToolEffects declaration: {value}")
    if "None" in declared and len(declared) != 1:
        raise ValueError(f"{location}: None cannot be combined with other effects")
    effects = tuple(effect for effect in EFFECTS if effect in declared)
    flags = {}
    for flag in ("RequiresHost", "RequiresDocument"):
        values = re.findall(rf"\b{flag}\s*=\s*(true|false)\b", value)
        if len(values) != 1:
            raise ValueError(f"{location}: expected one explicit {flag} value")
        flags[flag] = values[0] == "true"
    return effects, flags["RequiresHost"], flags["RequiresDocument"]


def extract_tools(root: Path) -> list[ToolInfo]:
    tools = []
    for path in sorted((root / "NavisHelper.McpServer" / "Tools").rglob("*.cs")):
        source = path.read_text(encoding="utf-8-sig")
        matches = list(TOOL.finditer(source))
        for index, marker in enumerate(matches):
            following = source[marker.end():matches[index + 1].start() if index + 1 < len(matches) else len(source)]
            method = METHOD.search(following)
            location = f"{path.relative_to(root)}:{source.count(chr(10), 0, marker.start()) + 1}"
            if method is None:
                raise ValueError(f"{location}: public tool method not found")
            attributes = following[:method.start()]
            effects, host, document = parse_capabilities(attributes, location)
            closing = parameter_end(following, method.end() - 1)
            parameters = following[method.end():closing]
            dry_run = bool(re.search(r"\bbool\s+apply\s*=\s*false\b", parameters))
            if dry_run and effects == ("None",):
                raise ValueError(f"{location}: apply=false tool declares no effect")
            if document and not host:
                raise ValueError(f"{location}: a document requires a host")
            tools.append(ToolInfo(marker.group(1) or snake(method.group(1)), effects, host, document, dry_run))
    names = [tool.name for tool in tools]
    if len(names) != len(set(names)):
        raise ValueError("Duplicate MCP tool names in capability table")
    return sorted(tools, key=lambda tool: tool.name)


def render(tools: list[ToolInfo], newline: str) -> str:
    lines = [
        BEGIN,
        "Effects: `None` reads only; `View` changes transient view state; `Document` changes savable document content;",
        "`Files` writes files; `Host` starts, opens in, or closes Navisworks; `LocalState` changes server-side state.",
        "Host and document indicate required runtime context. Dry-run means a `bool apply = false` parameter is available.",
        "",
        "| Tool | Effects | Host | Document | Dry-run |",
        "| --- | --- | --- | --- | --- |",
    ]
    for tool in tools:
        effects = ", ".join(tool.effects)
        host = "Yes" if tool.host else "No"
        document = "Yes" if tool.document else "No"
        dry_run = "Yes" if tool.dry_run else "No"
        lines.append(f"| `{tool.name}` | {effects} | {host} | {document} | {dry_run} |")
    lines.extend(["", END])
    return newline.join(lines)


def replace_section(document: str, generated: str, newline: str) -> str:
    if document.count(BEGIN) == 1 and document.count(END) == 1:
        start = document.index(BEGIN)
        end = document.index(END, start) + len(END)
        return document[:start] + generated + document[end:]
    if BEGIN in document or END in document:
        raise ValueError("Generated tool capability markers are incomplete or duplicated")
    anchor = newline + "## Common Rules"
    if anchor not in document:
        raise ValueError("Could not find Common Rules heading for capability table insertion")
    return document.replace(anchor, newline + "## Tool capabilities" + newline * 2 + generated + newline + anchor, 1)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true", help="verify the generated table")
    mode.add_argument("--update", action="store_true", help="write the generated table")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    document_path = root / "docs" / "MCP_TOOL_CONTRACTS.md"
    try:
        tools = extract_tools(root)
        original = document_path.read_bytes()
        has_bom = original.startswith(b"\xef\xbb\xbf")
        document = original.decode("utf-8-sig")
        newline = "\r\n" if "\r\n" in document else "\n"
        updated = replace_section(document, render(tools, newline), newline)
        if args.update:
            if updated != document:
                document_path.write_bytes((b"\xef\xbb\xbf" if has_bom else b"") + updated.encode("utf-8"))
            print(f"Updated tool capability table for {len(tools)} MCP tools.")
            return 0
        if updated != document:
            print("Tool capability table is missing or stale. Run: python scripts/check_mcp_tool_capabilities.py --update")
            return 1
        print(f"Tool capability table is current for {len(tools)} MCP tools.")
        return 0
    except (OSError, ValueError) as error:
        print(error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
