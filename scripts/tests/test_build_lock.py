"""Acceptance test for scripts/build_lock.py (#312).

Two agent sessions building at once crashed MSBuild on the owner's screen. The
script serializes MSBuild-heavy commands across processes; these tests run it as
a CLI against a lock file in a temporary folder, so no real build is involved.
"""

from __future__ import annotations

import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "build_lock.py"
TIMEOUT_EXIT = 75


def process_alive(pid: int) -> bool:
    """Without os.kill: on Windows os.kill(pid, 0) would terminate the process."""
    import ctypes

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.OpenProcess.restype = ctypes.c_void_p
    handle = kernel32.OpenProcess(0x00100000, False, pid)  # SYNCHRONIZE
    if not handle:
        return False
    try:
        return kernel32.WaitForSingleObject(ctypes.c_void_p(handle), 0) == 0x00000102  # WAIT_TIMEOUT
    finally:
        kernel32.CloseHandle(ctypes.c_void_p(handle))


def stop(process: subprocess.Popen) -> None:
    """Kill and wait, so the lock file is closed before the temp folder is removed."""
    if process.poll() is None:
        process.kill()
    process.wait(timeout=20)


class BuildLockTests(unittest.TestCase):
    def setUp(self):
        folder = tempfile.TemporaryDirectory(prefix="build-lock-")
        self.addCleanup(folder.cleanup)
        self.lock = Path(folder.name) / "build.lock"
        self.holder_file = Path(str(self.lock) + ".holder")

    def args(self, *command: str, timeout: float = 30) -> list[str]:
        return [sys.executable, str(SCRIPT), "--lock-file", str(self.lock), "--timeout", str(timeout),
                "--poll", "0.2", "--", *command]

    def run_locked(self, *command: str, timeout: float = 30) -> subprocess.CompletedProcess:
        return subprocess.run(self.args(*command, timeout=timeout), capture_output=True,
                              encoding="utf-8", errors="replace", timeout=120)

    def start_holder(self, seconds: float) -> subprocess.Popen:
        holder = subprocess.Popen(self.args(sys.executable, "-c", f"import time; time.sleep({seconds})"),
                                  stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        self.addCleanup(stop, holder)
        deadline = time.monotonic() + 20
        while not self.holder_file.exists():
            self.assertLess(time.monotonic(), deadline, "the holder never took the lock")
            time.sleep(0.05)
        return holder

    def test_the_child_exit_code_passes_through(self):
        result = self.run_locked(sys.executable, "-c", "import sys; sys.exit(7)")
        self.assertEqual(7, result.returncode, result.stderr)

    def test_a_second_command_waits_for_the_first_and_then_runs(self):
        holder = self.start_holder(2)
        started = time.monotonic()
        result = self.run_locked(sys.executable, "-c", "print('ran')")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("ran", result.stdout.strip())
        self.assertIn("waiting for the build lock", result.stderr)
        self.assertIn(f"pid {holder.pid}", result.stderr)
        self.assertGreater(time.monotonic() - started, 0.5)
        self.assertEqual(0, holder.wait(timeout=20))

    def test_the_wait_gives_up_after_the_timeout_and_names_the_holder(self):
        holder = self.start_holder(60)
        result = self.run_locked(sys.executable, "-c", "print('ran')", timeout=1)
        self.assertEqual(TIMEOUT_EXIT, result.returncode, result.stderr)
        self.assertNotIn("ran", result.stdout)
        self.assertIn(f"pid {holder.pid}", result.stderr)
        self.assertIn("timed out", result.stderr)

    def test_a_killed_holder_releases_the_lock(self):
        holder = self.start_holder(60)
        holder.kill()
        holder.wait(timeout=20)
        result = self.run_locked(sys.executable, "-c", "print('ran')", timeout=5)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertNotIn("waiting", result.stderr)

    def test_killing_the_holder_also_ends_the_command_it_runs(self):
        # Otherwise the lock is free while the old build still runs, and a second
        # build starts beside it: the very thing the lock exists to prevent.
        pid_file = Path(str(self.lock) + ".child")
        holder = subprocess.Popen(self.args(sys.executable, "-c",
                                            f"import os, time; open(r'{pid_file}', 'w').write(str(os.getpid())); time.sleep(60)"),
                                  stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        self.addCleanup(stop, holder)
        deadline = time.monotonic() + 20
        while not (pid_file.exists() and pid_file.read_text()):
            self.assertLess(time.monotonic(), deadline, "the command never started")
            time.sleep(0.05)
        child = int(pid_file.read_text())
        holder.kill()
        holder.wait(timeout=20)
        deadline = time.monotonic() + 10
        while process_alive(child):
            self.assertLess(time.monotonic(), deadline, f"the command (pid {child}) outlived its killed holder")
            time.sleep(0.1)

    def test_a_long_poll_does_not_outlast_the_timeout(self):
        self.start_holder(2)
        result = subprocess.run([sys.executable, str(SCRIPT), "--lock-file", str(self.lock), "--timeout", "1",
                                 "--poll", "5", "--", sys.executable, "-c", "print('ran')"],
                                capture_output=True, encoding="utf-8", errors="replace", timeout=60)
        self.assertEqual(TIMEOUT_EXIT, result.returncode, result.stderr)
        self.assertNotIn("ran", result.stdout)

    def test_a_missing_command_is_a_usage_error(self):
        result = subprocess.run([sys.executable, str(SCRIPT), "--lock-file", str(self.lock)],
                                capture_output=True, encoding="utf-8", errors="replace", timeout=60)
        self.assertEqual(2, result.returncode)
        self.assertFalse(self.lock.exists())


if __name__ == "__main__":
    unittest.main()
