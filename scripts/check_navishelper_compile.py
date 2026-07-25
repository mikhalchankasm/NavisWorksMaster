#!/usr/bin/env python3
"""Validate the non-SDK NavisHelper source inventory and compiled plugin surface.

The main NavisHelper project uses explicit Compile items. This check compares
those items with tracked C# files and requires every intentional exception to
be listed in scripts/navishelper_compile_allowlist.txt. It also scans only the
files that are actually compiled, after removing C# comments, for duplicate
Navisworks Plugin IDs, unreachable AddInPlugin classes, and growth beyond the
reviewed partial-class limits.
"""

from __future__ import annotations

import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[1]
PROJECT_PATH = REPO_ROOT / "NavisHelper" / "NavisHelper.csproj"
ALLOWLIST_PATH = REPO_ROOT / "scripts" / "navishelper_compile_allowlist.txt"
HOST_COMMAND_NAMES_PATH = REPO_ROOT / "NavisHelper.Contracts" / "Statuses.cs"
HOST_COMMAND_BASELINE_PATH = REPO_ROOT / "scripts" / "host_command_names_baseline.txt"
PARTIAL_LIMITS_PATH = REPO_ROOT / "scripts" / "navishelper_partial_limits.txt"
PLUGIN_ATTRIBUTE_RE = re.compile(r'\[\s*Plugin\s*\(\s*"([^"]+)"', re.IGNORECASE)
ADDIN_PLUGIN_CLASS_RE = re.compile(
    r"\b(?:(?:public|internal|protected|private|sealed|abstract|partial)\s+)*"
    r"class\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*AddInPlugin\b",
    re.MULTILINE,
)
PARTIAL_CLASS_RE = re.compile(
    r"\bpartial\s+class\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\b"
)
HOST_COMMAND_CLASS_RE = re.compile(
    r"\bpublic\s+static\s+class\s+HostCommandNames\s*\{(?P<body>.*?)\n\s*\}",
    re.DOTALL,
)
HOST_COMMAND_VALUE_RE = re.compile(r'\bpublic\s+const\s+string\s+\w+\s*=\s*"([^"]+)"\s*;')


def repo_path_key(path: str) -> str:
    """Return a case-insensitive comparison key using repository separators."""

    return path.replace("\\", "/").strip("/").casefold()


def normalize_compile_include(include: str) -> str | None:
    """Convert a project-relative Compile Include into a repository path."""

    normalized = include.replace("\\", "/").strip()
    if not normalized or normalized.startswith("/"):
        return None

    if normalized.casefold().startswith("navishelper/"):
        normalized = normalized[len("NavisHelper/") :]

    parts = [part for part in normalized.split("/") if part not in ("", ".")]
    if not parts or ".." in parts:
        return None

    return "NavisHelper/" + "/".join(parts)


def load_allowlist(errors: list[str]) -> dict[str, str]:
    """Read explicit source exceptions and return key -> display path."""

    entries: dict[str, str] = {}
    try:
        lines = ALLOWLIST_PATH.read_text(encoding="utf-8").splitlines()
    except OSError as exc:
        errors.append(f"Cannot read allowlist {ALLOWLIST_PATH}: {exc}")
        return entries

    for line_number, raw_line in enumerate(lines, start=1):
        value = raw_line.strip()
        if not value or value.startswith("#"):
            continue

        normalized = value.replace("\\", "/")
        if not normalized.casefold().startswith("navishelper/"):
            errors.append(
                f"{ALLOWLIST_PATH}:{line_number}: path must start with NavisHelper/"
            )
            continue

        key = repo_path_key(normalized)
        if key in entries:
            errors.append(
                f"{ALLOWLIST_PATH}:{line_number}: duplicate allowlist entry {value}"
            )
            continue

        entries[key] = normalized

    return entries


def source_csharp_files(errors: list[str]) -> dict[str, str]:
    """Return tracked and non-ignored untracked NavisHelper C# files."""

    try:
        result = subprocess.run(
            [
                "git",
                "ls-files",
                "-z",
                "--cached",
                "--others",
                "--exclude-standard",
                "--",
                "NavisHelper",
            ],
            cwd=REPO_ROOT,
            check=True,
            capture_output=True,
        )
    except (OSError, subprocess.CalledProcessError) as exc:
        errors.append(f"Cannot enumerate tracked files with git ls-files: {exc}")
        return {}

    entries: dict[str, str] = {}
    for raw_path in result.stdout.decode("utf-8").split("\0"):
        if not raw_path or not raw_path.casefold().endswith(".cs"):
            continue
        if not raw_path.casefold().startswith("navishelper/"):
            continue
        # git ls-files includes an unstaged deletion until the change is staged.
        # The source inventory describes the current worktree; real Compile
        # entries are checked separately and still fail if their file is gone.
        if not (REPO_ROOT / raw_path).is_file():
            continue
        entries[repo_path_key(raw_path)] = raw_path
    return entries


def compile_entries(errors: list[str]) -> dict[str, str]:
    """Parse real XML Compile elements; XML comments are ignored by design."""

    try:
        root = ET.parse(PROJECT_PATH).getroot()
    except (OSError, ET.ParseError) as exc:
        errors.append(f"Cannot parse {PROJECT_PATH}: {exc}")
        return {}

    entries: dict[str, str] = {}
    for element in root.iter():
        if element.tag.rsplit("}", 1)[-1] != "Compile":
            continue

        include = element.attrib.get("Include", "")
        normalized = normalize_compile_include(include)
        if normalized is None:
            errors.append(f"Invalid Compile Include in {PROJECT_PATH}: {include!r}")
            continue

        key = repo_path_key(normalized)
        if key in entries:
            errors.append(
                f"Duplicate real Compile Include in {PROJECT_PATH}: {entries[key]} and {include}"
            )
            continue

        entries[key] = normalized

    return entries


def strip_csharp_comments(source: str) -> str:
    """Remove // and /* */ comments while preserving strings and line breaks."""

    result: list[str] = []
    i = 0
    state = "normal"

    while i < len(source):
        current = source[i]
        following = source[i + 1] if i + 1 < len(source) else ""

        if state == "normal":
            if current == "/" and following == "/":
                result.extend((" ", " "))
                i += 2
                while i < len(source) and source[i] not in "\r\n":
                    result.append(" ")
                    i += 1
                continue
            if current == "/" and following == "*":
                result.extend((" ", " "))
                i += 2
                while i < len(source):
                    if source[i] == "*" and i + 1 < len(source) and source[i + 1] == "/":
                        result.extend((" ", " "))
                        i += 2
                        break
                    result.append("\n" if source[i] in "\r\n" else " ")
                    i += 1
                continue
            if current == '"':
                state = "string"
            elif current == "'":
                state = "char"
            elif current == "@" and following == '"':
                result.append(current)
                result.append(following)
                state = "verbatim_string"
                i += 2
                continue
            result.append(current)
            i += 1
            continue

        if state == "string":
            result.append(current)
            if current == "\\" and i + 1 < len(source):
                result.append(source[i + 1])
                i += 2
                continue
            if current == '"':
                state = "normal"
            i += 1
            continue

        if state == "char":
            result.append(current)
            if current == "\\" and i + 1 < len(source):
                result.append(source[i + 1])
                i += 2
                continue
            if current == "'":
                state = "normal"
            i += 1
            continue

        # Verbatim string: doubled quotes are escaped quotes.
        result.append(current)
        if current == '"':
            if following == '"':
                result.append(following)
                i += 2
                continue
            state = "normal"
        i += 1

    return "".join(result)


def plugin_ids(compiled: dict[str, str], errors: list[str]):
    """Find Plugin IDs in real Compile files and return ID -> locations."""

    occurrences: dict[str, list[tuple[str, int, str]]] = defaultdict(list)
    for path in sorted(compiled.values(), key=str.casefold):
        source_path = REPO_ROOT / path
        try:
            source = source_path.read_text(encoding="utf-8-sig")
        except OSError as exc:
            errors.append(f"Cannot read compiled source {path}: {exc}")
            continue

        source_without_comments = strip_csharp_comments(source)
        for match in PLUGIN_ATTRIBUTE_RE.finditer(source_without_comments):
            line_number = source_without_comments.count("\n", 0, match.start()) + 1
            plugin_id = match.group(1)
            occurrences[plugin_id.casefold()].append((path, line_number, plugin_id))

    return occurrences


def load_compiled_sources(
    compiled: dict[str, str], errors: list[str]
) -> dict[str, str]:
    """Read and comment-strip every real Compile source once."""

    result: dict[str, str] = {}
    for path in sorted(compiled.values(), key=str.casefold):
        try:
            source = (REPO_ROOT / path).read_text(encoding="utf-8-sig")
        except OSError as exc:
            errors.append(f"Cannot read compiled source {path}: {exc}")
            continue
        result[path] = strip_csharp_comments(source)
    return result


def last_declaration_boundary(source: str) -> int:
    """Return the last normal-code `;` or `}`, ignoring C# string contents."""

    last_boundary = -1
    i = 0
    state = "normal"
    while i < len(source):
        current = source[i]
        following = source[i + 1] if i + 1 < len(source) else ""

        if state == "normal":
            if current == '"':
                state = "string"
            elif current == "'":
                state = "char"
            elif current == "@" and following == '"':
                state = "verbatim_string"
                i += 2
                continue
            elif current in ";}":
                last_boundary = i
            i += 1
            continue

        if state in ("string", "char"):
            if current == "\\" and i + 1 < len(source):
                i += 2
                continue
            if (state == "string" and current == '"') or (
                state == "char" and current == "'"
            ):
                state = "normal"
            i += 1
            continue

        if current == '"':
            if following == '"':
                i += 2
                continue
            state = "normal"
        i += 1

    return last_boundary


def mask_csharp_literals(source: str) -> str:
    """Replace C# string/char literal contents with spaces, preserving positions."""

    result = list(source)
    i = 0
    state = "normal"
    while i < len(source):
        current = source[i]
        following = source[i + 1] if i + 1 < len(source) else ""

        if state == "normal":
            if current == "@" and following == '"':
                result[i] = result[i + 1] = " "
                state = "verbatim_string"
                i += 2
                continue
            if current == '"':
                result[i] = " "
                state = "string"
            elif current == "'":
                result[i] = " "
                state = "char"
            i += 1
            continue

        if current not in "\r\n":
            result[i] = " "
        if state in ("string", "char"):
            if current == "\\" and i + 1 < len(source):
                if source[i + 1] not in "\r\n":
                    result[i + 1] = " "
                i += 2
                continue
            if (state == "string" and current == '"') or (
                state == "char" and current == "'"
            ):
                state = "normal"
            i += 1
            continue

        if current == '"':
            if following == '"':
                result[i + 1] = " "
                i += 2
                continue
            state = "normal"
        i += 1

    return "".join(result)


def external_type_reference_pattern(class_name: str) -> re.Pattern[str]:
    """Match deliberate C# type usages without accepting bare words or strings."""

    name = re.escape(class_name)
    return re.compile(
        rf"(?:"
        rf"\b(?:new|typeof|nameof|is|as)\s*(?:\(\s*)?{name}\b"
        rf"|:\s*{name}\b"
        rf"|\b{name}\s*\."
        rf"|\b{name}\s+[A-Za-z_][A-Za-z0-9_]*\b"
        rf"|[<(,]\s*{name}\s*[,)>]"
        rf")"
    )


def validate_addin_plugin_reachability(
    sources: dict[str, str], errors: list[str]
) -> int:
    """Require every compiled AddInPlugin to be registered or externally referenced."""

    class_count = 0
    for path, source in sources.items():
        for match in ADDIN_PLUGIN_CLASS_RE.finditer(source):
            class_count += 1
            class_name = match.group("name")
            prefix = source[: match.start()]
            previous_boundary = last_declaration_boundary(prefix)
            directly_preceding_declarations = prefix[previous_boundary + 1 :]
            if PLUGIN_ATTRIBUTE_RE.search(directly_preceding_declarations):
                continue

            reference_re = external_type_reference_pattern(class_name)
            external_references = [
                other_path
                for other_path, other_source in sources.items()
                if other_path != path
                and reference_re.search(mask_csharp_literals(other_source))
            ]
            if not external_references:
                line_number = source.count("\n", 0, match.start()) + 1
                errors.append(
                    f"Unreachable compiled AddInPlugin {class_name} at "
                    f"{path}:{line_number}: add [Plugin], reference the type from "
                    "another compiled source, or remove the dead code"
                )
    return class_count


def validate_reachability_guard_fixtures(errors: list[str]) -> None:
    """Keep the lightweight source guard honest on its important edge cases."""

    cases = [
        (
            "registered attribute containing a semicolon",
            {"Plugin.cs": '[Plugin("live", ToolTip = "a;b")]\nclass Live : AddInPlugin {}'},
            False,
        ),
        (
            "external constructor reference",
            {
                "Plugin.cs": "class FactoryPlugin : AddInPlugin {}",
                "Factory.cs": "var plugin = new FactoryPlugin();",
            },
            False,
        ),
        (
            "type name inside a string literal",
            {
                "Plugin.cs": "class Colors : AddInPlugin {}",
                "Text.cs": 'var text = "new Colors()";',
            },
            True,
        ),
        (
            "unrelated variable with the same name",
            {
                "Plugin.cs": "class Colors : AddInPlugin {}",
                "State.cs": "int Colors = 1;",
            },
            True,
        ),
    ]

    for title, sources, should_fail in cases:
        fixture_errors: list[str] = []
        validate_addin_plugin_reachability(sources, fixture_errors)
        if bool(fixture_errors) != should_fail:
            errors.append(
                f"AddInPlugin reachability guard fixture failed ({title}); "
                f"expected error={should_fail}, actual errors={fixture_errors}"
            )


def read_partial_limits(errors: list[str]) -> dict[str, int]:
    """Read reviewed `TypeName=maximum partial files` limits."""

    result: dict[str, int] = {}
    try:
        lines = PARTIAL_LIMITS_PATH.read_text(encoding="utf-8").splitlines()
    except OSError as exc:
        errors.append(f"Cannot read partial limits {PARTIAL_LIMITS_PATH}: {exc}")
        return result

    for line_number, raw_line in enumerate(lines, start=1):
        value = raw_line.strip()
        if not value or value.startswith("#"):
            continue
        name, separator, limit_text = value.partition("=")
        if not separator or not name.strip():
            errors.append(
                f"{PARTIAL_LIMITS_PATH}:{line_number}: expected TypeName=maximum"
            )
            continue
        try:
            limit = int(limit_text.strip())
        except ValueError:
            errors.append(
                f"{PARTIAL_LIMITS_PATH}:{line_number}: invalid maximum {limit_text!r}"
            )
            continue
        if limit < 0:
            errors.append(
                f"{PARTIAL_LIMITS_PATH}:{line_number}: maximum must be non-negative"
            )
            continue
        result[name.strip()] = limit
    return result


def validate_partial_limits(sources: dict[str, str], errors: list[str]) -> None:
    """Block new partial files for reviewed compatibility god-classes."""

    limits = read_partial_limits(errors)
    files_by_type: dict[str, set[str]] = defaultdict(set)
    for path, source in sources.items():
        for match in PARTIAL_CLASS_RE.finditer(source):
            files_by_type[match.group("name")].add(path)

    for type_name, limit in limits.items():
        paths = sorted(files_by_type.get(type_name, set()), key=str.casefold)
        if len(paths) > limit:
            errors.append(
                f"{type_name} now spans {len(paths)} partial files; reviewed maximum "
                f"is {limit}. New feature families must use their own type.\n  "
                + "\n  ".join(paths)
            )


def read_host_command_values(errors: list[str]) -> list[str]:
    """Read HostCommandNames values from the source class in declaration order."""

    try:
        source = HOST_COMMAND_NAMES_PATH.read_text(encoding="utf-8-sig")
    except OSError as exc:
        errors.append(f"Cannot read {HOST_COMMAND_NAMES_PATH}: {exc}")
        return []

    match = HOST_COMMAND_CLASS_RE.search(strip_csharp_comments(source))
    if not match:
        errors.append(f"Cannot find HostCommandNames class in {HOST_COMMAND_NAMES_PATH}")
        return []
    return HOST_COMMAND_VALUE_RE.findall(match.group("body"))


def read_host_command_baseline(errors: list[str]) -> list[str]:
    """Read the reviewed HostCommandNames wire-value snapshot."""

    try:
        lines = HOST_COMMAND_BASELINE_PATH.read_text(encoding="utf-8").splitlines()
    except OSError as exc:
        errors.append(f"Cannot read baseline {HOST_COMMAND_BASELINE_PATH}: {exc}")
        return []
    return [line.strip() for line in lines if line.strip() and not line.lstrip().startswith("#")]


def main() -> int:
    errors: list[str] = []
    validate_reachability_guard_fixtures(errors)
    tracked = source_csharp_files(errors)
    compiled = compile_entries(errors)
    allowlist = load_allowlist(errors)

    tracked_keys = set(tracked)
    compiled_keys = set(compiled)
    allowlist_keys = set(allowlist)

    for path in compiled.values():
        if not (REPO_ROOT / path).is_file():
            errors.append(f"Compile Include points to a missing file: {path}")

    for key, path in allowlist.items():
        if key not in tracked_keys:
            errors.append(f"Allowlisted path is not a tracked C# file: {path}")
        if key in compiled_keys:
            errors.append(f"Allowlisted path is already compiled: {path}")

    missing_compile = sorted(tracked_keys - compiled_keys - allowlist_keys)
    if missing_compile:
        errors.append(
            "Tracked C# files missing from Compile and allowlist:\n  "
            + "\n  ".join(tracked[key] for key in missing_compile)
        )

    plugin_occurrences = plugin_ids(compiled, errors)
    compiled_sources = load_compiled_sources(compiled, errors)
    addin_plugin_class_count = validate_addin_plugin_reachability(
        compiled_sources, errors
    )
    validate_partial_limits(compiled_sources, errors)
    duplicate_ids = {
        key: locations for key, locations in plugin_occurrences.items() if len(locations) > 1
    }
    if duplicate_ids:
        duplicate_lines = ["Duplicate compiled Navisworks Plugin IDs:"]
        for locations in sorted(duplicate_ids.values(), key=lambda value: value[0][2].casefold()):
            duplicate_lines.append(f"  {locations[0][2]}:")
            duplicate_lines.extend(
                f"    {path}:{line_number}" for path, line_number, _ in locations
            )
        errors.append("\n".join(duplicate_lines))

    host_command_values = read_host_command_values(errors)
    host_command_baseline = read_host_command_baseline(errors)
    if len(host_command_values) != len(set(host_command_values)):
        errors.append("HostCommandNames contains duplicate wire values")
    if len(host_command_baseline) != len(set(host_command_baseline)):
        errors.append("HostCommandNames baseline contains duplicate wire values")
    if host_command_values != host_command_baseline:
        errors.append(
            "HostCommandNames wire values differ from the reviewed baseline "
            f"({len(host_command_values)} source values, {len(host_command_baseline)} baseline values). "
            "Update the baseline only with an explicit protocol-surface review."
        )

    print(
        "NavisHelper inventory: "
        f"{len(tracked)} tracked C# files, "
        f"{len(compiled)} real Compile entries, "
        f"{len(allowlist)} explicit exceptions, "
        f"{sum(len(locations) for locations in plugin_occurrences.values())} compiled Plugin attributes, "
        f"{addin_plugin_class_count} compiled AddInPlugin classes, "
        f"{len(host_command_values)} HostCommandNames values."
    )
    if allowlist:
        print("Allowlisted non-Compile files:")
        for path in sorted(allowlist.values(), key=str.casefold):
            print(f"  {path}")

    if errors:
        print("NavisHelper inventory check failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("NavisHelper inventory check passed; compiled Plugin IDs are unique.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
