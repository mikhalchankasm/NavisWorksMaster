"""What an executor run changed on the operator's machine outside its worktree.

Isolating the home moves files. It does not move the registry, local accounts
or the rights a sandbox grants, and on 2026-09-24 those three were how a
Windows profile was lost: dotnet appended hundreds of entries to the real user
PATH, and the Codex sandbox granted its accounts read access to the whole
AppData. Its logs lived in the disposable home and were deleted with it.

Nothing here prevents a change. It is detection in the "fail loudly if it
moved" sense: a snapshot before the run, one after, and a named failure
category when they differ.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import subprocess
from dataclasses import dataclass
from pathlib import Path

OPERATOR_ENV_MUTATED = "OPERATOR_ENV_MUTATED"
OPERATOR_ACL_MUTATED = "OPERATOR_ACL_MUTATED"
HOST_SNAPSHOT_UNAVAILABLE = "HOST_SNAPSHOT_UNAVAILABLE"
SANDBOX_ACCOUNTS_CREATED = "SANDBOX_ACCOUNTS_CREATED"
SANDBOX_PROFILE_GRANT = "SANDBOX_PROFILE_GRANT"
EXECUTOR_TELEMETRY_INCOMPLETE = "EXECUTOR_TELEMETRY_INCOMPLETE"
PROCESS_STOP_OUTSIDE_RUN = "PROCESS_STOP_OUTSIDE_RUN"
WORKTREE_ACL_NOT_RESTORED = "WORKTREE_ACL_NOT_RESTORED"

# `codex exec --json` event types listed in the installed 0.161 binary (2026-10-08). Anything else is
# reported, not ignored: a renamed event in a new release must not read as a quiet run.
CODEX_EVENT_TYPES = frozenset({
    "thread.started", "turn.started", "turn.completed", "turn.failed",
    "item.started", "item.updated", "item.completed",
})

SANDBOX_LOG_TAIL_LINES = 200
SANDBOX_LOG_TAIL_BYTES = 32 * 1024
# The grant scan reads the whole log, not the recorded tail: a grant early in a
# long log is exactly as dangerous as one at the end.
SANDBOX_LOG_SCAN_BYTES = 64 * 1024 * 1024
ACCOUNT_QUERY_TIMEOUT_SECONDS = 30
ACL_QUERY_TIMEOUT_SECONDS = 60
SANDBOX_ACCOUNT = re.compile(r"\bCodexSandbox\w*", re.IGNORECASE)
GRANT_LINE = re.compile(r"granting (?P<access>read|write) ACE to (?P<path>.+?) for ")


@dataclass(frozen=True)
class HostSnapshot:
    """Digests, except the user PATH: its entries are what made #1052 unattributable.

    The PATH value and the key's last write time stay in the local run record.
    """

    environment: dict[str, str] | None
    sandbox_accounts: frozenset[str] | None
    acls: dict[str, str] | None = None
    path: str | None = None
    environment_written: str | None = None


def _user_environment_digests() -> dict[str, str] | None:
    """HKCU\\Environment as name -> digest of (type, value); None off Windows."""
    if os.name != "nt":
        return None
    import winreg

    digests: dict[str, str] = {}
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as key:
            index = 0
            while True:
                try:
                    name, value, kind = winreg.EnumValue(key, index)
                except OSError:
                    break
                digests[name.upper()] = hashlib.sha256(f"{kind}\0{value}".encode("utf-8", "surrogatepass")).hexdigest()
                index += 1
    except OSError:
        return None
    return digests


def _user_path_and_write_time() -> tuple[str | None, str | None]:
    """HKCU\\Environment PATH and the key's last write time (UTC, ISO); (None, None) off Windows."""
    if os.name != "nt":
        return None, None
    import winreg
    from datetime import datetime, timedelta, timezone

    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as key:
            written = winreg.QueryInfoKey(key)[2]  # 100 ns intervals since 1601-01-01
            try:
                path = str(winreg.QueryValueEx(key, "Path")[0])
            except OSError:
                path = ""
    except OSError:
        return None, None
    stamp = datetime(1601, 1, 1, tzinfo=timezone.utc) + timedelta(microseconds=written // 10)
    return path, stamp.isoformat()


def _sandbox_accounts() -> frozenset[str] | None:
    """Local accounts named CodexSandbox*; None when the query itself failed.

    `net user` prints names in columns under a localized header. The names
    themselves are ASCII, so matching them needs no knowledge of the language.
    """
    if os.name != "nt":
        return None
    try:
        result = subprocess.run(
            ["net", "user"],
            capture_output=True,
            text=True,
            errors="replace",
            timeout=ACCOUNT_QUERY_TIMEOUT_SECONDS,
            check=False,
        )
    except (OSError, subprocess.SubprocessError):
        return None
    if result.returncode != 0:
        return None
    return frozenset(match.group(0) for match in SANDBOX_ACCOUNT.finditer(result.stdout))


def watched_profile_directories() -> list[str]:
    """The operator's own profile folders, from the launcher's real environment.

    The unelevated Codex sandbox logs no grant line at all, so the log cannot
    show an ACE it added; the folder's security descriptor can.
    """
    profile = os.environ.get("USERPROFILE")
    if not profile:
        return []
    candidates = [
        Path(profile),
        Path(profile) / "AppData",
        Path(os.environ.get("LOCALAPPDATA") or Path(profile) / "AppData" / "Local"),
        Path(os.environ.get("APPDATA") or Path(profile) / "AppData" / "Roaming"),
        Path(os.environ.get("TEMP") or Path(profile) / "AppData" / "Local" / "Temp"),
    ]
    unique: list[str] = []
    for candidate in candidates:
        if str(candidate) not in unique:
            unique.append(str(candidate))
    return unique


def _profile_acl_digests(paths: list[str]) -> dict[str, str] | None:
    """path -> digest of its access-control SDDL; None off Windows or on failure."""
    if os.name != "nt" or not paths:
        return None
    # Paths go in on stdin: -Command appends trailing arguments to the script
    # text instead of binding them to $args.
    # A folder that does not exist, or cannot be read, gets a stable marker
    # instead of failing the whole query: a CI runner has no profile Temp.
    script = (
        "$r=[ordered]@{}; "
        "foreach($p in ([Console]::In.ReadToEnd() | ConvertFrom-Json)){ "
        "if(-not (Test-Path -LiteralPath $p)){ $r[$p]='absent'; continue }; "
        "try { $r[$p]=(Get-Acl -LiteralPath $p -ErrorAction Stop).Sddl } catch { $r[$p]='unreadable' } }; "
        "$r | ConvertTo-Json -Compress"
    )
    try:
        result = subprocess.run(
            ["powershell", "-NoProfile", "-NonInteractive", "-Command", script],
            input=json.dumps(paths),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=ACL_QUERY_TIMEOUT_SECONDS,
            check=False,
        )
        payload = json.loads(result.stdout) if result.returncode == 0 else None
    except (OSError, subprocess.SubprocessError, ValueError):
        return None
    if not isinstance(payload, dict):
        return None
    return {str(path): hashlib.sha256(str(sddl).encode("utf-8")).hexdigest() for path, sddl in payload.items()}


def take_snapshot() -> HostSnapshot:
    path, written = _user_path_and_write_time()
    return HostSnapshot(
        environment=_user_environment_digests(),
        sandbox_accounts=_sandbox_accounts(),
        acls=_profile_acl_digests(watched_profile_directories()),
        path=path,
        environment_written=written,
    )


def path_entry_changes(before: str, after: str) -> dict[str, list[str]]:
    """PATH entries added and removed, compared as Windows does: case-insensitive, trailing slash ignored."""
    def entries(value: str) -> list[str]:
        return [entry.strip() for entry in value.split(";") if entry.strip()]

    def key(entry: str) -> str:
        return entry.rstrip("\\/").casefold()

    old, new = entries(before), entries(after)
    old_keys, new_keys = {key(entry) for entry in old}, {key(entry) for entry in new}
    return {
        "added": [entry for entry in new if key(entry) not in old_keys],
        "removed": [entry for entry in old if key(entry) not in new_keys],
    }


def telemetry_problems(stdout: str, *, timed_out: bool, output_too_large: bool) -> list[str]:
    """Why a Codex `--json` event stream cannot be read as a complete run; empty when it can.

    Completion is all this proves. An action the run took is not undone by noticing it afterwards,
    and a complete stream does not by itself show that nothing escaped the sandbox.
    """
    problems: set[str] = set()
    open_items: set[str] = set()
    events = 0
    last = None
    turn_started = False
    # JSONL boundaries are "\n" only: str.splitlines would also cut at U+2028 inside a JSON string.
    rows = (row[:-1] if row.endswith("\r") else row for row in stdout.split("\n"))
    for number, text in enumerate((row for row in rows if row.strip()), 1):
        try:
            event = json.loads(text)
        except ValueError:
            event = None
        kind = event.get("type") if isinstance(event, dict) else None
        if not isinstance(kind, str):
            problems.add(f"unparsed_line:{number}")
            continue
        events += 1
        last = kind
        # The start is listed by the 0.161 binary but no recorded tail reaches it; the control run confirms it.
        if events == 1 and kind != "thread.started":
            problems.add("no_thread_start")
        if kind not in CODEX_EVENT_TYPES:
            problems.add("unknown_type:" + kind[:40])
            continue
        if kind == "turn.started":
            turn_started = True
            continue
        # Recorded on codex 0.162.1 (2026-10-10, #1052 probe): the warning that `tools.image_gen` is ignored arrives
        # as completed `error` items between thread.started and turn.started. Only that exact warning is allowed
        # early: an ignored isolation switch (web search, sandbox, approvals) must still stop the run (#1076 review).
        early_warning = (kind == "item.completed" and isinstance(event.get("item"), dict)
                         and event["item"].get("type") == "error"
                         and _KNOWN_IGNORED_SETTING.fullmatch(str(event["item"].get("message") or "")) is not None)
        if kind != "thread.started" and not turn_started and not early_warning:
            problems.add("no_turn_start")
        if kind == "turn.failed":
            problems.add("turn_failed")
        if kind == "turn.completed" and not isinstance(event.get("usage"), dict):
            problems.add(f"malformed_turn:{number}")
        if kind.startswith("item."):
            # Seen in real records: every item carries id `item_N` and a type. An agent message arrives only as
            # item.completed, so a completion without a start is normal; a start without a completion is not.
            item = event.get("item")
            item_id = item.get("id") if isinstance(item, dict) else None
            if not isinstance(item_id, str) or not item_id or not isinstance(item.get("type"), str):
                problems.add(f"malformed_item:{number}")
                continue
            if kind == "item.started":
                open_items.add(item_id)
            elif kind == "item.completed":
                open_items.discard(item_id)
    if events == 0:
        problems.add("no_events")
    elif last != "turn.completed":
        problems.add("no_final_turn")
    problems.update("unfinished_item:" + item for item in open_items)
    if timed_out:
        problems.add("timed_out")
    if output_too_large:
        problems.add("output_too_large")
    return sorted(problems)


_KNOWN_IGNORED_SETTING = re.compile(
    r"Codex is ignoring 1 unrecognized configuration setting\. Check for typos or deprecated settings\.\n"
    r"  session-flags: `tools\.image_gen` is ignored\.")


def changed_environment_names(before: HostSnapshot, after: HostSnapshot) -> list[str] | None:
    if before.environment is None or after.environment is None:
        return None
    names = set(before.environment) | set(after.environment)
    return sorted(name for name in names if before.environment.get(name) != after.environment.get(name))


def changed_acl_paths(before: HostSnapshot, after: HostSnapshot) -> list[str] | None:
    if before.acls is None or after.acls is None:
        return None
    return sorted(path for path in set(before.acls) | set(after.acls) if before.acls.get(path) != after.acls.get(path))


def created_sandbox_accounts(before: HostSnapshot, after: HostSnapshot) -> list[str] | None:
    if before.sandbox_accounts is None or after.sandbox_accounts is None:
        return None
    return sorted(after.sandbox_accounts - before.sandbox_accounts)


def _tail(path: Path) -> str:
    with path.open("rb") as handle:
        handle.seek(0, os.SEEK_END)
        size = handle.tell()
        handle.seek(max(0, size - SANDBOX_LOG_TAIL_BYTES))
        text = handle.read().decode("utf-8", errors="replace")
    return "\n".join(text.splitlines()[-SANDBOX_LOG_TAIL_LINES:])


def _scan_text(path: Path) -> str:
    with path.open("rb") as handle:
        return handle.read(SANDBOX_LOG_SCAN_BYTES).decode("utf-8", errors="replace")


def _normalized(path: str | Path) -> str:
    return os.path.normcase(os.path.normpath(str(path).strip().strip('"')))


def _is_within(path: str, root: str) -> bool:
    return path == root or path.startswith(root.rstrip("\\/") + os.sep)


def profile_grants(log_text: str, profile: Path, allowed_roots: list[Path]) -> list[str]:
    """Grant lines that reach into the operator's profile outside the allowed roots.

    The worktree and the run directory are the run's own. Anything else under
    the real USERPROFILE - AppData, a dotfile, the profile itself - is the
    pattern that preceded the incident.
    """
    profile_key = _normalized(profile)
    allowed = [_normalized(root) for root in allowed_roots]
    found: list[str] = []
    for line in log_text.splitlines():
        match = GRANT_LINE.search(line)
        if match is None:
            continue
        target = _normalized(match.group("path"))
        if not _is_within(target, profile_key) and not _is_within(profile_key, target):
            continue
        if any(_is_within(target, root) for root in allowed):
            continue
        entry = f"{match.group('access')} {match.group('path').strip()}"
        if entry not in found:
            found.append(entry)
    return found


def sandbox_log_evidence(log_dir: Path, profile: Path, allowed_roots: list[Path]) -> dict[str, object]:
    """Copy the tail of each sandbox log before the home that holds it is deleted."""
    files: list[dict[str, object]] = []
    grants: list[str] = []
    if log_dir.is_dir():
        for path in sorted(log_dir.glob("*.log")):
            try:
                tail = _tail(path)
                scanned = _scan_text(path)
            except OSError as exc:
                files.append({"name": path.name, "problem": type(exc).__name__})
                continue
            files.append({"name": path.name, "bytes": path.stat().st_size, "tail": tail})
            grants.extend(grant for grant in profile_grants(scanned, profile, allowed_roots) if grant not in grants)
    return {"directory": str(log_dir), "files": files, "profileGrants": grants}


SNAPSHOT_FIELDS = ("operatorEnvironmentChanged", "sandboxAccountsCreated", "operatorAclChanged")


def unavailable_snapshots(host: dict[str, object]) -> list[str]:
    return [name for name in SNAPSHOT_FIELDS if host.get(name) is None]


def violations(host: dict[str, object], *, windows: bool | None = None) -> list[str]:
    """Failure categories, most specific first; empty when nothing moved.

    On Windows a snapshot that could not be taken fails the run: an unknown
    is not a clean result. Elsewhere there is no registry to read, so None is
    the expected answer rather than a failed probe.
    """
    windows = os.name == "nt" if windows is None else windows
    found: list[str] = []
    # The launcher's own grant (#1275) is the one allowed change; anything left of it, or added beside it, is not.
    grant = host.get("worktreeAclGrant")
    if isinstance(grant, dict) and grant.get("restored") is not True:
        found.append(WORKTREE_ACL_NOT_RESTORED)
    log = host.get("sandboxLog")
    if isinstance(log, dict) and log.get("profileGrants"):
        found.append(SANDBOX_PROFILE_GRANT)
    if host.get("sandboxAccountsCreated"):
        found.append(SANDBOX_ACCOUNTS_CREATED)
    if host.get("operatorAclChanged"):
        found.append(OPERATOR_ACL_MUTATED)
    if host.get("operatorEnvironmentChanged"):
        found.append(OPERATOR_ENV_MUTATED)
    telemetry = host.get("telemetry")
    if isinstance(telemetry, dict) and telemetry.get("problems"):
        found.append(EXECUTOR_TELEMETRY_INCOMPLETE)
    if windows and unavailable_snapshots(host):
        found.append(HOST_SNAPSHOT_UNAVAILABLE)
    if any(entry.get("inRunTree") is False for entry in host.get("processControl", [])):
        found.append(PROCESS_STOP_OUTSIDE_RUN)
    return found
