"""The whole Codex event stream of one run, kept next to its record (#1069).

The record keeps a 4000-character tail with line breaks collapsed; the event schema of a real run cannot be
checked from that. The journal keeps every captured line, in order, one per line.

Known secrets and token shapes are removed inside the decoded JSON - string values and keys - and the event is
serialised again, so a valid event stays valid whatever quotes or backslashes the secret holds. A line that is not a JSON object is
kept as text with the secret (raw and JSON-escaped) removed and is named in `problems`: a damaged stream never
becomes a clean journal. A run that timed out or hit the output limit is named the same way.
"""

from __future__ import annotations

import hashlib
import json
from pathlib import Path

from scripts.executor_primitives import redact_patterns

REDACTED = "[REDACTED]"


def _scrub(text: str, secrets: list[str]) -> str:
    # Known values of this run first, then the token shapes the record's tail also removes (Bearer, sk-, token=).
    for secret in secrets:
        text = text.replace(secret, REDACTED)
    return redact_patterns(text)


def _scrub_value(value: object, secrets: list[str]) -> object:
    if isinstance(value, str):
        return _scrub(value, secrets)
    if isinstance(value, list):
        return [_scrub_value(item, secrets) for item in value]
    if isinstance(value, dict):
        return {_scrub(str(key), secrets): _scrub_value(item, secrets) for key, item in value.items()}
    return value


def _lines(stdout: str) -> list[str]:
    # JSONL boundaries are "\n" only: str.splitlines would also cut at U+2028 inside a JSON string.
    rows = [row[:-1] if row.endswith("\r") else row for row in stdout.split("\n")]
    return rows[:-1] if rows and rows[-1] == "" else rows


def write_event_journal(
    stdout: str, secrets: set[str], path: Path, *, timed_out: bool, output_too_large: bool
) -> dict[str, object]:
    """Write the journal and return the record's `eventJournal` field."""
    ordered = sorted((secret for secret in secrets if secret), key=len, reverse=True)
    with_escaped = ordered + [json.dumps(secret)[1:-1] for secret in ordered]
    rows: list[str] = []
    problems: list[str] = []
    for number, text in enumerate(_lines(stdout), 1):
        try:
            event = json.loads(text)
        except ValueError:
            event = None
        if isinstance(event, dict):
            rows.append(json.dumps(_scrub_value(event, ordered), ensure_ascii=False, separators=(",", ":")))
        else:
            rows.append(_scrub(text, with_escaped))
            problems.append(f"unparsed_line:{number}")
    if output_too_large:
        problems.append("output_too_large")
    if timed_out:
        problems.append("timed_out")
    data = "".join(row + "\n" for row in rows).encode("utf-8")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return {"path": str(path), "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest(),
            "lines": len(rows), "problems": problems}
