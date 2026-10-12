"""Record process-stop attempts in Codex's command event stream.

Only literal PID targets can establish ownership. Names, variables and
pipelines remain unproven even when another stop in the command is owned.
This is command-text detection, not execution or inspection of script files.
"""

from __future__ import annotations

import json
import re
from typing import Collection

from scripts.executor_primitives import redact_patterns


_STOP = re.compile(
    r"(?<![\w.-])(?P<command>Stop-Process|spps|taskkill|tskill|kill|pkill|killall)"
    r"(?:\.exe)?(?![\w-])"
    r"|(?P<method>\.Kill\s*\()"
    r"|(?P<api>\bTerminateProcess\b)"
    r"|(?P<wmic>\bwmic(?:\.exe)?\b(?:[^;|\r\n]*?\bterminate\b"
    r"|(?=[^;|\r\n]*\bprocess\b)[^;|\r\n]*?\bdelete\b))"
    r"|(?P<cim>\bInvoke-CimMethod\b[^;|\r\n]*?\bTerminate\b)",
    re.IGNORECASE,
)
_SEPARATOR = re.compile(r"[;|\r\n]|&&")
_NUMBERS = r"\d+(?:\s*,\s*\d+)*"
_END_NUMBER = r"(?!\s*,)(?=$|[\s)'\";/&|])"
_ID = re.compile(r"(?<!\w)-Id(?:\s*:\s*|\s+)(?P<pids>" + _NUMBERS + r")" + _END_NUMBER, re.I)
_TASK_PID = re.compile(r"/PID\s+(?P<pids>\d+)" + _END_NUMBER, re.I)
_PROCESS_ID = re.compile(r"\bProcessId\s*=\s*(?P<pids>\d+)" + _END_NUMBER, re.I)
_GET_BY_ID = re.compile(r"\bGetProcessById\s*\(\s*(?P<pids>\d+)\s*\)", re.I)
_POSITIONAL = re.compile(r"^\s+(?P<pids>" + _NUMBERS + r")" + _END_NUMBER)
_KILL_ARGS = re.compile(
    r"^\s+(?:(?:-\d+|-[A-Za-z]+|--signal(?:=|\s+)\w+)\s+)?"
    r"(?P<pids>\d+(?:\s+\d+)*)" + _END_NUMBER,
)


def _targets(command: str) -> tuple[list[int], bool]:
    stops = list(_STOP.finditer(command))
    separators = list(_SEPARATOR.finditer(command))
    pids: list[int] = []
    proven = bool(stops)
    for index, stop in enumerate(stops):
        left = max((part.end() for part in separators if part.end() <= stop.start()), default=0)
        right = min((part.start() for part in separators if part.start() >= stop.end()), default=len(command))
        if index and stops[index - 1].end() > left:
            left = stops[index - 1].end()
        if index + 1 < len(stops):
            right = min(right, stops[index + 1].start())
        before = command[left:stop.start()]
        after = command[stop.end():right]
        matches: list[re.Match[str]] = []
        form = (stop.group("command") or "").lower()
        if form in ("stop-process", "spps"):
            matches = list(_ID.finditer(after))
            positional = _POSITIONAL.match(after)
            if positional:
                matches.insert(0, positional)
        elif form == "taskkill":
            matches = list(_TASK_PID.finditer(after))
        elif form == "tskill":
            positional = _POSITIONAL.match(after)
            matches = [positional] if positional else []
        elif form == "kill":
            positional = _KILL_ARGS.match(after)
            matches = [positional] if positional else []
        elif stop.group("method"):
            # A literal receiver, not an unrelated lookup assigned to a variable.
            receiver = re.search(r"GetProcessById\s*\(\s*(\d+)\s*\)\s*$", before, re.I)
            if receiver:
                matches = list(_GET_BY_ID.finditer(receiver.group()))
        elif stop.group("wmic"):
            text = stop.group()
            if re.search(r"\bterminate\b", text, re.I) or re.search(r"\bprocess\b", text, re.I):
                matches = list(_PROCESS_ID.finditer(text))
        elif stop.group("cim"):
            matches = list(_PROCESS_ID.finditer(before + stop.group()))

        targets = [int(number) for match in matches for number in re.findall(r"\d+", match.group("pids"))]
        # An additional dynamic/name target or an incoming pipeline cannot be proven.
        target_text = before + stop.group() + after if stop.group("wmic") or stop.group("cim") else after
        dynamic = re.search(r"\$(?:\w+|\{)|(?<!\w)-Name\b|/IM\b|\bName\s*=", target_text, re.I)
        piped = left > 0 and command[left - 1] == "|"
        # A WMI/CIM predicate can select more than the PID it names ("ProcessId=1 OR ProcessId>0"), and a
        # remote or filtered stop is not checked against this machine's Job: neither can prove ownership.
        selection = bool(stop.group("wmic") or stop.group("cim"))
        remote = re.search(r"(?<![\w-])(?:/S(?=[\s:])|/SERVER:|/node:|/FI\b|-ComputerName\b|-CimSession\b)",
                           before + stop.group() + after, re.I)
        proven = proven and bool(targets) and not dynamic and not piped and not selection and not remote
        pids.extend(pid for pid in targets if pid not in pids)
    return pids, bool(proven)


def _scrub(text: str, secrets: list[str]) -> str:
    for secret in secrets:
        text = text.replace(secret, "[REDACTED]")
    return redact_patterns(text)


def scan(
    stdout: str, run_tree_pids: frozenset[int], secrets: Collection[str] = ()
) -> list[dict[str, object]]:
    """One entry per item ID in first-seen order; completed replaces started.

    The raw stream is parsed and only the kept strings are redacted. Redacting first would let a token shape
    such as `token=\\S+` eat an event's closing quotes, drop the event as unparsable and hide its stop.
    """
    items: dict[str, dict[str, object]] = {}
    completed: set[str] = set()
    # JSONL boundaries are "\n" only: str.splitlines would also cut at U+2028 inside a JSON string.
    for row in stdout.split("\n"):
        try:
            event = json.loads(row[:-1] if row.endswith("\r") else row)
        except ValueError:
            continue  # telemetry_problems names an unparsed line, which fails the run on its own
        if not isinstance(event, dict) or event.get("type") not in ("item.started", "item.completed"):
            continue
        item = event.get("item")
        if not isinstance(item, dict) or item.get("type") != "command_execution":
            continue
        item_id = item.get("id")
        if not isinstance(item_id, str):
            continue
        if event["type"] == "item.completed":
            items[item_id] = item
            completed.add(item_id)
        elif item_id not in completed:
            items[item_id] = item

    known = sorted((secret for secret in secrets if secret), key=len, reverse=True)
    entries: list[dict[str, object]] = []
    for item_id, item in items.items():
        command = item.get("command")
        if not isinstance(command, str) or not _STOP.search(command):
            continue
        pids, proven = _targets(command)
        output = item.get("aggregated_output", "") if item_id in completed else ""
        entries.append({
            "itemId": item_id,
            "command": _scrub(command, known),
            "output": _scrub(output, known)[-2000:] if isinstance(output, str) else "",
            "pids": pids,
            "inRunTree": proven and all(pid in run_tree_pids for pid in pids),
        })
    return entries
