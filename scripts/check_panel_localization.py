#!/usr/bin/env python3
"""Reject hard-coded user-facing strings in compiled NavisHelper panel sources."""

from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "NavisHelper" / "NavisHelper.csproj"
ALLOWLIST = ROOT / "scripts" / "panel_localization_allowlist.txt"

RESOURCE_KEY = re.compile(
    r"^(?:Panel_[A-Za-z0-9_]+|Settings(?:_[A-Za-z0-9_]+|[A-Z][A-Za-z0-9]*)|Common[A-Z][A-Za-z0-9]*)$"
)
CYRILLIC = re.compile(r"[А-Яа-яЁё]")
ASCII_LETTER = re.compile(r"[A-Za-z]")
UI_CONTEXT = re.compile(
    r"""
    \b(?:Text|Content|Header|ToolTip|Title|Filter)\s*=
    |MessageBox\s*\.\s*Show
    |Interaction\s*\.\s*InputBox
    |BeginProgress\s*\(
    |SetGlobalStatus(?:Resource)?\s*\(
    |SetGlobalBusy\s*\(
    |new\s+MenuItem\b
    |UiTheme\s*\.
    |CreateGroupHeader\s*\(
    |MakeLocalizedButtonContent\s*\(
    |\b(?:ActionBtn|Btn|ClashResultMenuItem|ClashTestMenuItem|BuildClashStatusMenu)\s*\(
    |FolderPickerDialog\s*\.\s*Show
    |new\s+Microsoft\.Win32\.(?:OpenFileDialog|SaveFileDialog)
    """,
    re.VERBOSE,
)
DIRECT_MANAGER_STATUS = re.compile(
    r"\b_(?:sel|clash)Mgr\.Last[A-Za-z0-9_]*Status\b"
)
DIRECT_PREFORMATTED_STATUS = re.compile(r"\bSetGlobalStatus\s*\(")
LOCALIZED_INTERACTIVE_REASON = re.compile(
    r"\b(?:AgentRuntime\.)?BeginInteractiveOperation\s*\("
    r"[^;]{0,600}?\b(?:PanelUi|UiLocalizationService|OperationLabel)\s*\(",
    re.DOTALL,
)
LOCALIZED_BUSY_REASON = re.compile(
    r"\bRejectClashInteractiveBusy\s*\("
    r"[^;]{0,600}?\b(?:PanelUi|UiLocalizationService|OperationLabel)\s*\(",
    re.DOTALL,
)
LOCALIZED_PERSISTED_IDENTIFIER = re.compile(
    r"\b(?:MakeUniqueSavedViewpointName|FindOrCreateSavedViewpointFolder)\s*\("
    r"[^;]{0,600}?\b(?:PanelUi|UiLocalizationService)\s*\(",
    re.DOTALL,
)
PERSISTED_IDENTIFIER_RESOURCE_KEY = re.compile(
    r'"(?:Panel_Clash_Viewpoints_ResetName|Panel_Clash_GroupDefaultName|'
    r'Panel_Colors_SearchSet_DefaultName)"'
)
PREFORMATTED_STATUS_EXCEPTION = re.compile(
    r"\bFormatGlobalStatusResource\s*\("
)
STRING_BATCH_ERROR_SUMMARIES = re.compile(
    r"\berrorSummar(?:y|ies)\s*=\s*new\s+List<string>\s*\("
)
PREFORMATTED_BATCH_SUMMARY = re.compile(
    r"\bUiLocalizationService\.Current\.Format\s*\(\s*"
    r'"Panel_Clash_Viewpoints_FirstErrors_Format"',
    re.DOTALL,
)
PREFORMATTED_PALETTE_STATUS = re.compile(
    r"\bSetGlobalStatusResource\s*\("
    r"[^;]{0,600}?\bPaletteCommand(?:Title|Description)\s*\(",
    re.DOTALL,
)
PREFORMATTED_CLASH_SUFFIX = re.compile(
    r"\bPanelUi\s*\(\s*"
    r'"Panel_Clash_Viewpoint_NoCenterSuffix"\s*\)'
)
CONCATENATED_SAVED_NAMES = re.compile(
    r"\bsavedNames\s*(?:=|\+=)"
)


@dataclass(frozen=True)
class Literal:
    value: str
    line: int


def load_allowlist() -> dict[str, tuple[str, str]]:
    entries: dict[str, tuple[str, str]] = {}
    for number, raw in enumerate(ALLOWLIST.read_text(encoding="utf-8").splitlines(), 1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if line.startswith(r"\#"):
            line = line[1:]
        parts = line.split("|", 2)
        if len(parts) != 3 or not all(part.strip() for part in parts):
            raise ValueError(f"{ALLOWLIST}:{number}: expected literal|classification|reason")
        literal, classification, reason = (part.strip() for part in parts)
        entries[literal] = (classification, reason)
    return entries


def compiled_panel_sources() -> list[Path]:
    root = ET.parse(PROJECT).getroot()
    sources: list[Path] = []
    for element in root.iter():
        if element.tag.rsplit("}", 1)[-1] != "Compile":
            continue
        include = element.attrib.get("Include")
        if not include:
            continue
        relative = Path(include.replace("\\", "/"))
        name = relative.name
        if (
            name == "NavisHelperPanel.cs"
            or (name.startswith("NavisHelperPanel.") and name.endswith(".cs"))
            or name in {
                "NavisHelperSettingsTabBuilder.cs",
                "PanelLocalizationBindings.cs",
                "ShowNavisHelperPanel.cs",
                "UiTheme.cs",
            }
        ):
            sources.append(PROJECT.parent / relative)
    return sorted(set(sources))


def decode_regular_string(raw: str) -> str:
    replacements = {
        r"\\": "\\",
        r"\"": '"',
        r"\n": "\n",
        r"\r": "\r",
        r"\t": "\t",
        r"\0": "\0",
    }
    for source, target in replacements.items():
        raw = raw.replace(source, target)
    return raw


def csharp_literals(source: str) -> list[Literal]:
    literals: list[Literal] = []
    index = 0
    line = 1
    length = len(source)
    while index < length:
        char = source[index]
        if char == "\n":
            line += 1
            index += 1
            continue
        if source.startswith("//", index):
            end = source.find("\n", index + 2)
            index = length if end < 0 else end
            continue
        if source.startswith("/*", index):
            end = source.find("*/", index + 2)
            end = length - 2 if end < 0 else end
            line += source.count("\n", index, end + 2)
            index = end + 2
            continue

        prefix_length = 0
        verbatim = False
        if source.startswith('$@"', index) or source.startswith('@$"', index):
            prefix_length = 3
            verbatim = True
        elif source.startswith('@"', index):
            prefix_length = 2
            verbatim = True
        elif source.startswith('$"', index):
            prefix_length = 2
        elif char == '"':
            prefix_length = 1

        if prefix_length == 0:
            index += 1
            continue

        start_line = line
        cursor = index + prefix_length
        value: list[str] = []
        while cursor < length:
            current = source[cursor]
            if current == "\n":
                line += 1
            if verbatim:
                if current == '"' and cursor + 1 < length and source[cursor + 1] == '"':
                    value.append('"')
                    cursor += 2
                    continue
                if current == '"':
                    cursor += 1
                    break
                value.append(current)
                cursor += 1
                continue

            if current == "\\" and cursor + 1 < length:
                value.append(source[cursor : cursor + 2])
                cursor += 2
                continue
            if current == '"':
                cursor += 1
                break
            value.append(current)
            cursor += 1

        text = "".join(value)
        literals.append(Literal(text if verbatim else decode_regular_string(text), start_line))
        index = cursor
    return literals


def context_for(lines: list[str], line_number: int) -> str:
    index = line_number - 1
    start = index
    while start > 0 and index - start < 12:
        if ";" in lines[start - 1]:
            break
        start -= 1
    end = index + 1
    while end < len(lines) and end - index < 12:
        if ";" in lines[end - 1]:
            break
        end += 1
    return "\n".join(lines[start:end])


def line_number_for_offset(source: str, offset: int) -> int:
    return source.count("\n", 0, offset) + 1


def is_technical_literal(value: str) -> bool:
    plain_text = re.sub(r"\{[^{}]*\}", "", value)
    if not plain_text or not ASCII_LETTER.search(plain_text):
        return True
    if value in {
        "A",
        "B",
        "A:",
        "B:",
        "AI",
        "CAM",
        "GIF",
        "HTML",
        "NavisHelper",
        "px",
    }:
        return True
    if re.fullmatch(r"[A-Za-z0-9_.-]+\.(?:dll|zip|txt|md|gif|bcf|json|xml)", value, re.I):
        return True
    if re.fullmatch(r"[A-Za-z][A-Za-z0-9_.-]*\.(?:CBC|COMPANY)", value):
        return True
    if re.fullmatch(r"\\[uU][0-9A-Fa-f]{4,8}", value):
        return True
    if re.fullmatch(r"[\d\s.,:%+#/\\(){}*-]+", plain_text):
        return True
    return False


def main() -> int:
    try:
        allowlist = load_allowlist()
    except (OSError, ValueError) as error:
        print(f"panel localization audit configuration error: {error}", file=sys.stderr)
        return 2

    failures: list[str] = []
    sources = compiled_panel_sources()
    if not sources:
        print("panel localization audit configuration error: no compiled panel sources found", file=sys.stderr)
        return 2

    for path in sources:
        source = path.read_text(encoding="utf-8-sig")
        lines = source.splitlines()
        structural_patterns = (
            (
                DIRECT_MANAGER_STATUS,
                "manager diagnostic status is passed through panel code; use a structured UI outcome",
            ),
            (
                DIRECT_PREFORMATTED_STATUS,
                "preformatted global status is forbidden; use SetGlobalStatusResource",
            ),
            (
                LOCALIZED_INTERACTIVE_REASON,
                "localized text flows into BeginInteractiveOperation",
            ),
            (
                LOCALIZED_BUSY_REASON,
                "localized text flows into RejectClashInteractiveBusy",
            ),
            (
                LOCALIZED_PERSISTED_IDENTIFIER,
                "localized text flows into a persisted/model identifier",
            ),
            (
                PERSISTED_IDENTIFIER_RESOURCE_KEY,
                "persisted/model identifier must use an invariant baseline constant",
            ),
            (
                PREFORMATTED_STATUS_EXCEPTION,
                "localized status is stored in an exception; carry a resource descriptor instead",
            ),
            (
                STRING_BATCH_ERROR_SUMMARIES,
                "batch errors are stored as localized strings; keep structured error records",
            ),
            (
                PREFORMATTED_BATCH_SUMMARY,
                "batch error summary is localized before the final status is stored",
            ),
            (
                PREFORMATTED_PALETTE_STATUS,
                "palette title is localized before the final status is stored",
            ),
            (
                PREFORMATTED_CLASH_SUFFIX,
                "saved-viewpoint suffix is localized before the final status is stored",
            ),
            (
                CONCATENATED_SAVED_NAMES,
                "saved-viewpoint names and localized suffixes must remain structured",
            ),
        )
        for pattern, message in structural_patterns:
            for match in pattern.finditer(source):
                relative = path.relative_to(ROOT)
                failures.append(
                    f"{relative}:{line_number_for_offset(source, match.start())}: {message}"
                )

        for literal in csharp_literals(source):
            value = literal.value
            if RESOURCE_KEY.fullmatch(value) or value in allowlist:
                continue
            current_line = lines[literal.line - 1]
            if (
                "Logger." in current_line
                or "new System.Windows.Data.Binding(" in current_line
                or "new Binding(" in current_line
                or "BeginInteractiveOperation(" in current_line
                or "FileName =" in current_line
                or "DefaultExt =" in current_line
            ):
                continue
            if (
                re.fullmatch(r"[a-z][a-z0-9_]*", value)
                or (
                    re.fullmatch(r"[A-Za-z][A-Za-z0-9_.:-]*", value)
                    and re.search(r"[.:-]", value)
                )
            ) and not re.search(
                r"\b(?:Text|Content|Header|ToolTip|Title|Filter)\s*=",
                current_line,
            ):
                continue
            if value.startswith("NavisHelper {"):
                continue
            context = context_for(lines, literal.line)
            hard_coded_cyrillic = bool(CYRILLIC.search(value))
            hard_coded_ui = bool(UI_CONTEXT.search(context)) and not is_technical_literal(value)
            if not hard_coded_cyrillic and not hard_coded_ui:
                continue
            relative = path.relative_to(ROOT)
            category = "Cyrillic string" if hard_coded_cyrillic else "hard-coded UI string"
            failures.append(f"{relative}:{literal.line}: {category}: {value!r}")

    if failures:
        print("Panel localization audit failed:")
        for failure in failures:
            print(f"  {failure}")
        print(
            "Move user-facing text to semantic resources or add a narrow exact-literal "
            "entry to scripts/panel_localization_allowlist.txt."
        )
        return 1

    print(
        f"Panel localization audit passed: {len(sources)} compiled panel/helper source files; "
        f"{len(allowlist)} exact allowlist entries."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
