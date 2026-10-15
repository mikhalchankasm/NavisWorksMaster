#!/usr/bin/env python3
"""Run one MSBuild-heavy command at a time on this machine, across agent sessions.

Two agent sessions building at once crashed MSBuild with a dialog on the owner's
screen (#312). Each session already ran one build at a time; nothing coordinated
the sessions. Every MSBuild-heavy command goes through this wrapper instead:

    python scripts/build_lock.py -- <command> [args...]

That covers the executor launcher (its run may build), an L2 MSBuild matrix and
tools/build_installer.ps1. The lock is an exclusive OS byte-range lock on
%LOCALAPPDATA%\\NavisHelper\\build.lock, so it spans every session of one Windows
account -- all agent sessions on this machine run under the owner's. Windows drops
it when the holding process exits for any reason, so a crashed or killed session
cannot leave a stale lock; the file staying behind means nothing. The command runs
in a kill-on-close Job Object, so killing the wrapper ends the build with it rather
than freeing the lock beside a build still running.

A waiting run says who holds the lock (from build.lock.holder), polls, and after
--timeout seconds exits 75 without running the command. Otherwise the command's
own exit code is returned unchanged.
"""

from __future__ import annotations

import argparse
import msvcrt
import os
import subprocess
import sys
import time
from datetime import datetime
from pathlib import Path

TIMEOUT_EXIT = 75


def default_lock_file() -> Path:
    return Path(os.environ.get("LOCALAPPDATA") or Path.home()) / "NavisHelper" / "build.lock"


def try_lock(handle) -> bool:
    handle.seek(0)
    try:
        msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
        return True
    except OSError:
        return False


def holder_text(holder_file: Path) -> str:
    try:
        return holder_file.read_text(encoding="utf-8").strip() or "an unknown process"
    except OSError:
        return "an unknown process"


def acquire(lock_file: Path, timeout: float, poll: float):
    """The open, locked handle, or None when timeout passed first."""
    lock_file.parent.mkdir(parents=True, exist_ok=True)
    # open() handles are not inheritable (PEP 446), so the child never holds the lock.
    handle = open(lock_file, "a+b")
    holder_file = Path(str(lock_file) + ".holder")
    deadline = time.monotonic() + timeout
    announced = False
    while not try_lock(handle):
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            handle.close()
            print(f"build_lock: timed out after {timeout:g} s; {holder_text(holder_file)} still holds {lock_file}",
                  file=sys.stderr)
            return None
        if not announced:
            print(f"build_lock: waiting for the build lock held by {holder_text(holder_file)}", file=sys.stderr)
            announced = True
        # Never sleep past the deadline: a long poll would otherwise take a lock freed
        # after the timeout and run a command the caller had already given up on.
        time.sleep(min(poll, remaining))
    return handle


def run_in_job(command: list[str]) -> int:
    """Run command in a kill-on-close Job Object, the launcher's own pair of helpers.

    The OS drops the file lock the moment this process dies; without the Job, a
    killed wrapper would leave its MSBuild running while the next build starts. The
    child starts suspended and is resumed only once it is in the Job, so nothing it
    spawns escapes. Closing the Job also ends whatever the command left behind.
    """
    from executor_primitives import _WindowsJob, _resume_windows_process

    process = subprocess.Popen(command, creationflags=0x00000004)  # CREATE_SUSPENDED; subprocess has no name for it
    job = _WindowsJob()
    try:
        try:
            job.assign(process)
            _resume_windows_process(process)
        except BaseException:
            process.kill()
            process.wait()
            raise
        return process.wait()
    finally:
        job.close()


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0],
                                     usage="build_lock.py [--timeout S] [--lock-file PATH] -- command [args...]")
    parser.add_argument("--timeout", type=float, default=3600, help="seconds to wait for the lock (default 3600)")
    parser.add_argument("--lock-file", type=Path, default=None, help=argparse.SUPPRESS)
    parser.add_argument("--poll", type=float, default=5, help=argparse.SUPPRESS)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args(argv)
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if not command:
        parser.error("a command is required after --")

    lock_file = args.lock_file or default_lock_file()
    handle = acquire(lock_file, args.timeout, args.poll)
    if handle is None:
        return TIMEOUT_EXIT
    try:
        holder = f"pid {os.getpid()} since {datetime.now():%Y-%m-%d %H:%M:%S}: {' '.join(command)[:200]}"
        Path(str(lock_file) + ".holder").write_text(holder + "\n", encoding="utf-8")
        return run_in_job(command)
    finally:
        handle.close()


if __name__ == "__main__":
    sys.exit(main())
