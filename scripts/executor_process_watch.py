"""Which processes outside an executor run ended while it ran, and when each byte of its output arrived (#371).

Windows does not record who ended a process, and an unelevated launcher cannot subscribe to the kernel's audit
events. What it can see is that a process outside the run's Job was present at one sample and gone at the next,
250 ms later. `executor_process_control` uses that as evidence: a command whose text names a stop, but during
which no older process outside the run ended, stopped nothing outside the run.

The evidence is a coincidence in time. It can blame a command for an unrelated exit (measured 2026-10-30 on the
owner's machine with other agent sessions running: one 250 ms sample in 45 saw a process older than 10 s end),
which fails the run as before. It never clears a command during which an older outside process ended. A process
younger than `MIN_AGE_SECONDS` is not evidence: other sessions' `git` and `bash` end every few seconds.
"""

from __future__ import annotations

import ctypes
import os
import time
from dataclasses import dataclass, field
from typing import Callable, Collection, Iterable

# A process must have been seen this long before a command started to count as evidence against it.
MIN_AGE_SECONDS = 10.0
SAMPLE_INTERVAL_SECONDS = 0.25

Snapshot = Callable[[], "dict[tuple[int, int, str], str] | None"]


@dataclass(frozen=True)
class SeenProcess:
    pid: int
    exe: str
    # Negative for a process older than the watch; None when the first sample listed it and its age is unknown.
    first_seen: float | None
    last_seen: float
    ended_by: float | None  # the first sample without it; None while it was still running at the last one
    in_run: bool  # listed by the run's Job at any sample while it was seen


@dataclass(frozen=True)
class WatchResult:
    """Seconds since the watch started. `complete` is False when any sample failed: then nothing is evidence."""

    processes: tuple[SeenProcess, ...]
    stdout_timeline: tuple[tuple[float, int], ...]  # (time, stdout bytes written) at every sample
    complete: bool


@dataclass
class _Live:
    pid: int
    exe: str
    first_seen: float | None
    last_seen: float
    in_run: bool


@dataclass
class ProcessWatch:
    """Call `sample` once per launcher tick, after the Job's member list was refreshed."""

    snapshot: Snapshot = field(default_factory=lambda: _toolhelp_snapshot)
    clock: Callable[[], float] = time.monotonic
    # Seconds each PID has run, asked once for the processes of the first sample. Without it one that started a
    # moment before the watch would read as old, and its exit would blame a harmless command (#371 review).
    ages: Callable[[Iterable[int]], "dict[int, float]"] = field(default_factory=lambda: _process_ages)

    def __post_init__(self) -> None:
        self._start = self.clock()
        self._live: dict[tuple[int, int, str], _Live] = {}
        self._ended: list[SeenProcess] = []
        self._timeline: list[tuple[float, int]] = []
        self._complete = True
        self._sampled = False

    def sample(self, stdout_bytes: int, job_pids: Collection[int]) -> None:
        now = self.clock() - self._start
        processes = self.snapshot()
        if processes is None:
            self._complete = False
            return
        for key, live in list(self._live.items()):
            if key not in processes:
                self._ended.append(SeenProcess(live.pid, live.exe, live.first_seen, live.last_seen, now, live.in_run))
                del self._live[key]
        ages = {} if self._sampled else self.ages(key[0] for key in processes)
        for key, exe in processes.items():
            live = self._live.get(key)
            if live is None:
                # A first-sample process whose start time cannot be read stays None: old, so its exit is evidence.
                first = now if self._sampled else (now - ages[key[0]] if key[0] in ages else None)
                live = self._live[key] = _Live(key[0], exe, first, now, False)
            live.last_seen = now
            live.in_run = live.in_run or key[0] in job_pids
        self._timeline.append((now, stdout_bytes))
        self._sampled = True

    def result(self) -> WatchResult:
        running = (SeenProcess(p.pid, p.exe, p.first_seen, p.last_seen, None, p.in_run) for p in self._live.values())
        return WatchResult(tuple(self._ended) + tuple(running), tuple(self._timeline), self._complete and self._sampled)


def arrival(timeline: tuple[tuple[float, int], ...], offset: int) -> tuple[float, float | None]:
    """The last sample before `offset` bytes had been written, and the first sample at or after it (None: never)."""
    before = 0.0
    for when, size in timeline:
        if size >= offset:
            return before, when
        before = when
    return before, None


def outside_exits(watch: WatchResult, start: float, end: float | None) -> list[SeenProcess]:
    """Processes outside the run, seen at least MIN_AGE_SECONDS before `start`, that ended in [start, end].

    `end` grows to the last exit of any run process started in the window and still running after it: a stop
    that a command leaves behind in a background process acts after the command's own output has arrived.
    """
    grown = True
    while grown and end is not None:  # a grandchild started while its parent ran grows the window again
        grown = False
        for child in watch.processes:
            if (child.in_run and child.first_seen is not None and start <= child.first_seen <= end
                    and (child.ended_by is None or child.ended_by > end)):
                end, grown = child.ended_by, True
                if end is None:
                    break
    return [p for p in watch.processes
            if not p.in_run and p.ended_by is not None and p.ended_by > start
            and (end is None or p.last_seen < end)
            and (p.first_seen is None or p.first_seen <= start - MIN_AGE_SECONDS)]


def _toolhelp_snapshot() -> dict[tuple[int, int, str], str] | None:
    """(pid, parent pid, image name) -> image name for every process; None off Windows or on failure.

    The parent and the name are part of the key so that a reused PID reads as a new process.
    """
    if os.name != "nt":
        return None
    from ctypes import wintypes

    class ProcessEntry(ctypes.Structure):
        _fields_ = [("dwSize", wintypes.DWORD), ("cntUsage", wintypes.DWORD), ("th32ProcessID", wintypes.DWORD),
                    ("th32DefaultHeapID", ctypes.c_size_t), ("th32ModuleID", wintypes.DWORD),
                    ("cntThreads", wintypes.DWORD), ("th32ParentProcessID", wintypes.DWORD),
                    ("pcPriClassBase", ctypes.c_long), ("dwFlags", wintypes.DWORD),
                    ("szExeFile", ctypes.c_wchar * 260)]

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
    kernel32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    kernel32.Process32FirstW.argtypes = [wintypes.HANDLE, ctypes.c_void_p]
    kernel32.Process32NextW.argtypes = [wintypes.HANDLE, ctypes.c_void_p]
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = kernel32.CreateToolhelp32Snapshot(0x00000002, 0)  # TH32CS_SNAPPROCESS
    if not handle or handle == wintypes.HANDLE(-1).value:
        return None
    try:
        entry = ProcessEntry()
        entry.dwSize = ctypes.sizeof(ProcessEntry)
        found: dict[tuple[int, int, str], str] = {}
        ok = kernel32.Process32FirstW(handle, ctypes.byref(entry))
        while ok:
            found[(entry.th32ProcessID, entry.th32ParentProcessID, entry.szExeFile)] = entry.szExeFile
            ok = kernel32.Process32NextW(handle, ctypes.byref(entry))
        # ERROR_NO_MORE_FILES ends a complete walk; anything else cut it short.
        return found if ctypes.get_last_error() == 18 else None
    finally:
        kernel32.CloseHandle(handle)


def _process_ages(pids: Iterable[int]) -> dict[int, float]:
    """PID -> seconds since it started, for the processes whose start time this user may read."""
    if os.name != "nt":
        return {}
    from ctypes import wintypes

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel32.OpenProcess.restype = wintypes.HANDLE
    kernel32.GetProcessTimes.argtypes = [wintypes.HANDLE] + [ctypes.POINTER(wintypes.FILETIME)] * 4
    kernel32.GetSystemTimeAsFileTime.argtypes = [ctypes.POINTER(wintypes.FILETIME)]
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]

    def ticks(value: wintypes.FILETIME) -> int:
        return (value.dwHighDateTime << 32) | value.dwLowDateTime  # 100 ns since 1601

    now = wintypes.FILETIME()
    kernel32.GetSystemTimeAsFileTime(ctypes.byref(now))
    ages: dict[int, float] = {}
    for pid in pids:
        handle = kernel32.OpenProcess(0x1000, False, pid)  # PROCESS_QUERY_LIMITED_INFORMATION
        if not handle:
            continue
        try:
            created, gone, kernel, user = (wintypes.FILETIME() for _ in range(4))
            if kernel32.GetProcessTimes(handle, ctypes.byref(created), ctypes.byref(gone),
                                        ctypes.byref(kernel), ctypes.byref(user)) and ticks(created):
                ages[pid] = max(0.0, (ticks(now) - ticks(created)) / 10_000_000)
        finally:
            kernel32.CloseHandle(handle)
    return ages


def describe(processes: Iterable[SeenProcess], limit: int = 20) -> list[dict[str, object]]:
    return [{"pid": p.pid, "exe": p.exe} for p in list(processes)[:limit]]
