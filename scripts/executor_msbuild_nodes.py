"""Orphaned MSBuild worker nodes left behind by an executor run (#576).

`dotnet build` passes `-maxcpucount` without a number, and MSBuild then counts
the logical processors through the Windows API. Measured on 2026-09-28 (32
logical CPUs, SDK 9.0.318, a 12-project solution): DOTNET_PROCESSOR_COUNT=4,
MSBUILDNODELIMITOFFSET of -28 and +4, and a CPU affinity of 0xF each still gave
11 worker nodes, so the child environment cannot cap the node count. What it
can do is shorten how long a node that never got its parent's connection waits
(MSBUILDNODECONNECTIONTIMEOUT in executor_primitives.DOTNET_FIXED_ENV). After
the child exits, the launcher also terminates the orphans the run started.

Only these are touched: dotnet.exe running MSBuild.dll as a worker with node
reuse off, created inside the run's window, whose parent is gone, and whose own
environment carries this run's AVOX_EXECUTOR_RUN value. Creation time and a
missing parent show only that a node is an orphan, not whose; the launcher puts
the marker into the child environment and MSBuild workers inherit it (seen on
all 11 nodes of a probe build). A node whose environment cannot be read is left
alone. A node with a live parent belongs to a build still running somewhere, and
a node with reuse on is someone's deliberate persistent node. Nothing is
selected by image name alone.

A node is stopped through one open handle: its creation time and marker are
checked through that handle and TerminateProcess uses the same handle. While
the handle is open Windows cannot give the PID to another process, so a node
that exited after the snapshot can never turn into a kill of whatever took its
PID - E3D with unsaved work included.
"""

from __future__ import annotations

import json
import os
import re
import subprocess
from dataclasses import dataclass
from typing import Callable, Sequence

RUN_MARKER = "AVOX_EXECUTOR_RUN"
WORKER_ARGUMENTS = re.compile(r"msbuild\.dll\b.*/nodemode:\d+", re.IGNORECASE)
REUSE_OFF = re.compile(r"/nodereuse:false\b", re.IGNORECASE)
QUERY_TIMEOUT_SECONDS = 60
MAX_ENVIRONMENT_BYTES = 1 << 20
PROCESS_TERMINATE = 0x0001
PROCESS_VM_READ = 0x0010
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
# FILETIME counts 100 ns ticks from 1601-01-01; Unix milliseconds start at 1970.
FILETIME_UNIX_EPOCH = 116_444_736_000_000_000

# Creation time in Unix milliseconds, so a PID can be matched to the exact
# process it named when it was selected.
_CREATED = "$(if ($_.CreationDate) { ([DateTimeOffset]$_.CreationDate).ToUnixTimeMilliseconds() } else { 0 })"
SNAPSHOT_SCRIPT = (
    "[Console]::OutputEncoding = [Text.Encoding]::UTF8; "
    "@(Get-CimInstance Win32_Process | ForEach-Object { [pscustomobject]@{ "
    "p = [int]$_.ProcessId; q = [int]$_.ParentProcessId; n = [string]$_.Name; "
    "c = [string]$_.CommandLine; t = [long]" + _CREATED + " } }) | ConvertTo-Json -Compress"
)


@dataclass(frozen=True)
class ProcessInfo:
    pid: int
    parent_pid: int
    name: str
    command_line: str
    created_ms: int


def is_msbuild_worker(process: ProcessInfo) -> bool:
    command = process.command_line or ""
    return (
        process.name.lower() == "dotnet.exe"
        and WORKER_ARGUMENTS.search(command) is not None
        and REUSE_OFF.search(command) is not None
    )


def orphan_msbuild_nodes(
    processes: Sequence[ProcessInfo],
    started_ms: int,
    finished_ms: int,
    run_marker: str,
    marker_of: Callable[[int], str | None],
) -> list[ProcessInfo]:
    """This run's workers with reuse off, created in [started_ms, finished_ms], whose parent is gone.

    The parent counts as gone when no process holds its PID, or when the one
    holding it was created after the node: Windows reuses PIDs. marker_of reads
    a node's RUN_MARKER and is asked only about otherwise matching nodes.
    """
    by_pid = {process.pid: process for process in processes}
    orphans = []
    for process in processes:
        if not is_msbuild_worker(process) or not started_ms <= process.created_ms <= finished_ms:
            continue
        parent = by_pid.get(process.parent_pid)
        if parent is not None and parent.created_ms <= process.created_ms:
            continue
        if not run_marker or marker_of(process.pid) != run_marker:
            continue
        orphans.append(process)
    return sorted(orphans, key=lambda process: process.pid)


def parse_snapshot(text: str) -> list[ProcessInfo]:
    rows = json.loads(text) if text.strip() else []
    if isinstance(rows, dict):
        rows = [rows]
    return [
        ProcessInfo(
            pid=int(row["p"]),
            parent_pid=int(row["q"]),
            name=str(row.get("n") or ""),
            command_line=str(row.get("c") or ""),
            created_ms=int(row.get("t") or 0),
        )
        for row in rows
    ]


def snapshot_processes() -> list[ProcessInfo]:
    result = subprocess.run(
        ["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", SNAPSHOT_SCRIPT],
        capture_output=True,
        timeout=QUERY_TIMEOUT_SECONDS,
        check=False,
    )
    if result.returncode != 0:
        raise OSError(result.stderr.decode("utf-8", "replace").strip()[:300] or f"exit {result.returncode}")
    return parse_snapshot(result.stdout.decode("utf-8", "replace"))


class _Handle:
    """An open process handle; Windows keeps the PID bound to this process until it closes."""

    def __init__(self, pid: int, access: int) -> None:
        import ctypes
        import ctypes.wintypes as wintypes

        self._ctypes = ctypes
        self._kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        self._kernel32.OpenProcess.restype = wintypes.HANDLE
        self.value = self._kernel32.OpenProcess(access, False, pid) if ctypes.sizeof(ctypes.c_void_p) == 8 else None
        # Passed as a pointer-sized value; a bare int would go through as a 32-bit C int.
        self._arg = ctypes.c_void_p(self.value)

    def __enter__(self) -> "_Handle":
        return self

    def __exit__(self, *_: object) -> None:
        if self.value:
            self._kernel32.CloseHandle(self._arg)

    def created_ms(self) -> int | None:
        ctypes = self._ctypes
        times = [ctypes.c_ulonglong() for _ in range(4)]
        if not self._kernel32.GetProcessTimes(self._arg, *(ctypes.byref(time) for time in times)):
            return None
        return (times[0].value - FILETIME_UNIX_EPOCH) // 10_000

    def environment_variable(self, name: str) -> str | None:
        """PEB -> RTL_USER_PROCESS_PARAMETERS, x64 offsets: ProcessParameters 0x20, Environment 0x80, size 0x3F0."""
        ctypes = self._ctypes

        class BasicInformation(ctypes.Structure):
            _fields_ = [("ExitStatus", ctypes.c_void_p), ("PebBaseAddress", ctypes.c_void_p),
                        ("AffinityMask", ctypes.c_void_p), ("BasePriority", ctypes.c_void_p),
                        ("UniqueProcessId", ctypes.c_void_p), ("InheritedFromUniqueProcessId", ctypes.c_void_p)]

        wow64 = ctypes.c_int()
        if not self._kernel32.IsWow64Process(self._arg, ctypes.byref(wow64)) or wow64.value:
            return None
        basic = BasicInformation()
        ntdll = ctypes.WinDLL("ntdll")
        if ntdll.NtQueryInformationProcess(self._arg, 0, ctypes.byref(basic), ctypes.sizeof(basic), None) != 0:
            return None

        def read(address: int, size: int) -> bytes | None:
            buffer = ctypes.create_string_buffer(size)
            got = ctypes.c_size_t()
            if not self._kernel32.ReadProcessMemory(self._arg, ctypes.c_void_p(address), buffer, size, ctypes.byref(got)):
                return None
            return buffer.raw if got.value == size else None

        pointer = read((basic.PebBaseAddress or 0) + 0x20, 8)
        parameters = int.from_bytes(pointer, "little") if pointer else 0
        environment = read(parameters + 0x80, 8) if parameters else None
        size = read(parameters + 0x3F0, 8) if parameters else None
        length = int.from_bytes(size, "little") if size else 0
        if not environment or not 0 < length <= MAX_ENVIRONMENT_BYTES:
            return None
        block = read(int.from_bytes(environment, "little"), length)
        if block is None:
            return None
        prefix = name.upper() + "="
        for entry in block.decode("utf-16-le", "replace").split("\0"):
            if entry.upper().startswith(prefix):
                return entry[len(prefix):]
        return None

    def terminate(self) -> bool:
        return bool(self._kernel32.TerminateProcess(self._arg, 1))


def read_environment_variable(pid: int, name: str) -> str | None:
    """One variable from another 64-bit process's environment; None when it cannot be read."""
    if os.name != "nt":
        return None
    with _Handle(pid, PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ) as handle:
        return handle.environment_variable(name) if handle.value else None


def process_created_ms(pid: int) -> int | None:
    if os.name != "nt":
        return None
    with _Handle(pid, PROCESS_QUERY_LIMITED_INFORMATION) as handle:
        return handle.created_ms() if handle.value else None


def stop_verified(node: ProcessInfo, run_marker: str) -> bool:
    """Terminate the node only if the process behind one open handle is still it and still this run's."""
    if os.name != "nt" or not run_marker:
        return False
    with _Handle(node.pid, PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ) as handle:
        if not handle.value:
            return False
        created = handle.created_ms()
        # CIM rounds the same FILETIME to microseconds; a reused PID belongs to a process created
        # after this one exited, never within a millisecond of it.
        if created is None or abs(created - node.created_ms) > 1:
            return False
        if handle.environment_variable(RUN_MARKER) != run_marker:
            return False
        return handle.terminate()


def terminate(
    nodes: Sequence[ProcessInfo], run_marker: str, stop: Callable[[ProcessInfo, str], bool] = stop_verified
) -> list[int]:
    return [node.pid for node in nodes if stop(node, run_marker)]


def clean_orphan_msbuild_nodes(started_ms: int, finished_ms: int, run_marker: str) -> dict[str, object]:
    """Terminate the run's orphaned workers; the result goes into the run record."""
    if os.name != "nt":
        return {"found": 0, "terminated": 0, "skipped": "not_windows"}
    try:
        orphans = orphan_msbuild_nodes(
            snapshot_processes(), started_ms, finished_ms, run_marker,
            lambda pid: read_environment_variable(pid, RUN_MARKER),
        )
        if not orphans:
            return {"found": 0, "terminated": 0}
        return {"found": len(orphans), "terminated": len(terminate(orphans, run_marker))}
    except (OSError, subprocess.SubprocessError, ValueError, KeyError, TypeError) as exc:
        return {"found": None, "terminated": 0, "problem": str(exc)[:300]}
