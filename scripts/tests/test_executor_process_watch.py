"""Curator acceptance for #371: a stop named only in command text needs a process that actually ended.

Three executor runs (#360, #377, #387) ended with exit 6 `PROCESS_STOP_OUTSIDE_RUN` for Codex's own searches,
`rg -n '...|kill|Stop-Process' scripts/check_*.py`: no PID, nothing stopped. Masking quoted text was tried twice
and reviewed into four bypasses, so the text verdict stays as it is. What changes is the evidence: the launcher
samples every process every 250 ms, and a command outside the run with no PID to check fails the run only when
an older process outside the run ended while it ran. A named PID outside the run fails it whatever happened.
"""

from __future__ import annotations

import os
import unittest

from scripts.executor_host_guard import PROCESS_STOP_OUTSIDE_RUN, violations
from scripts.executor_process_control import scan
from scripts.executor_process_watch import (
    MIN_AGE_SECONDS, ProcessWatch, SeenProcess, WatchResult, _process_ages, _toolhelp_snapshot, arrival,
    outside_exits,
)
from scripts.tests.test_executor_process_control import FOREIGN_PID, POWERSHELL, command_item, stream

SEARCH_360 = "rg -n 'subprocess|Popen|dotnet|unlink|write_text|kill|Stop-Process' scripts/check_*.py"
SEARCH_377 = "rg -n 'subprocess|Popen|dotnet|kill|terminate|write_text|write_bytes' scripts"
SEARCH_387 = "rg -n 'kill|Terminate|Stop-Process|taskkill' scripts -g 'check_*.py'"
# From the #336 record: Codex also wraps searches in cmd.exe, and the text it runs is all in the command.
SEARCH_CMD = ('"C:\\\\Windows\\\\System32\\\\cmd.exe" /c \'rg -n -e subprocess -e Popen -e kill -e dotnet '
              '-e write_text scripts -g check_*.py\'')
START = 100.0  # the watch has run this long when the stream starts, so processes it saw first are old


def timed(stdout: str) -> tuple[tuple[float, int], ...]:
    """One sample per second; line k of the stream is on disk from sample START + k + 1."""
    timeline, written = [(START, 0)], 0
    for number, row in enumerate(stdout.split("\n")[:-1]):
        written += len(row.encode("utf-8")) + 1
        timeline.append((START + number + 1, written))
    return tuple(timeline)


def line_of(stdout: str, item_id: str, kind: str) -> int:
    return next(number for number, row in enumerate(stdout.split("\n"))
                if f'"type":"{kind}"' in row and f'"id":"{item_id}"' in row)


def ended(pid: int, exe: str, *, at: float, born: float | None = None, in_run: bool = False) -> SeenProcess:
    return SeenProcess(pid, exe, born, at - 0.25, at, in_run)


def entry_for(stdout: str, processes: tuple[SeenProcess, ...] = (), *, complete: bool = True,
              tree: frozenset[int] = frozenset()) -> dict[str, object]:
    watch = WatchResult(processes, timed(stdout), complete)
    [entry] = scan(stdout, tree, (), watch)
    return entry


def fails(entry: dict[str, object]) -> bool:
    return PROCESS_STOP_OUTSIDE_RUN in violations({"processControl": [entry]}, windows=False)


class SearchesThatStoppedNothing(unittest.TestCase):
    def test_the_three_recorded_searches_pass_when_nothing_outside_the_run_ended(self) -> None:
        for command in (SEARCH_360, SEARCH_377, SEARCH_387, POWERSHELL + f"'{SEARCH_360}'", SEARCH_CMD):
            with self.subTest(command=command):
                entry = entry_for(stream(command_item("item_7", command, "scripts/check_x.py:1:kill")))
                self.assertEqual(entry["pids"], [])
                self.assertIs(entry["inRunTree"], False)
                self.assertEqual(entry["outsideExits"], [])
                self.assertFalse(fails(entry))

    def test_a_young_outside_process_ending_is_not_evidence(self) -> None:
        stdout = stream(command_item("item_7", SEARCH_360))
        at = START + line_of(stdout, "item_7", "item.completed")
        entry = entry_for(stdout, (ended(500, "git.exe", at=at, born=at - MIN_AGE_SECONDS + 1),))
        self.assertEqual(entry["outsideExits"], [])
        self.assertFalse(fails(entry))

    def test_an_exit_before_the_command_started_is_not_blamed_on_it(self) -> None:
        stdout = stream(command_item("item_7", SEARCH_360))
        entry = entry_for(stdout, (ended(500, "notepad.exe", at=START + 0.5),))
        self.assertFalse(fails(entry))


class StopsThatActed(unittest.TestCase):
    def test_an_older_outside_process_ending_during_the_command_fails_the_run(self) -> None:
        command = POWERSHELL + "'Get-Process Roamer | Stop-Process -Force'"
        stdout = stream(command_item("item_9", command))
        at = START + line_of(stdout, "item_9", "item.completed")
        for born in (None, START - MIN_AGE_SECONDS):
            with self.subTest(born=born):
                entry = entry_for(stdout, (ended(4242, "Roamer.exe", at=at, born=born),))
                self.assertEqual(entry["outsideExits"], [{"pid": 4242, "exe": "Roamer.exe"}])
                self.assertTrue(fails(entry))

    def test_a_search_still_fails_when_an_older_process_ended_meanwhile(self) -> None:
        """Coincidence counts against the command: the evidence never clears a window with an exit in it."""
        stdout = stream(command_item("item_7", SEARCH_360))
        at = START + line_of(stdout, "item_7", "item.completed")
        self.assertTrue(fails(entry_for(stdout, (ended(77, "chrome.exe", at=at),))))

    def test_a_stop_left_running_in_the_background_is_caught_after_the_command_returned(self) -> None:
        # The text names no background process (that would keep the text verdict); the watch sees the child.
        command = POWERSHELL + "'Stop-Process -Name Roamer'"
        stdout = stream(command_item("item_9", command))
        started = START + line_of(stdout, "item_9", "item.started") + 0.5
        late = START + 90
        child = SeenProcess(900, "pwsh.exe", started, late - 0.25, late, True)
        entry = entry_for(stdout, (child, ended(4242, "Roamer.exe", at=late - 0.1)))
        self.assertEqual(entry["outsideExits"], [{"pid": 4242, "exe": "Roamer.exe"}])
        self.assertEqual(entry["window"][1], late)  # the record shows the grown window the exit was judged in
        self.assertTrue(fails(entry))
        # A child still running when the run ended keeps the window open to the end.
        still_running = SeenProcess(900, "pwsh.exe", started, START + 200, None, True)
        watch = WatchResult((still_running, ended(4242, "Roamer.exe", at=START + 150)), (), True)
        self.assertEqual(len(outside_exits(watch, started - 1, started + 1)), 1)

    def test_a_named_pid_outside_the_run_fails_even_when_nothing_ended(self) -> None:
        entry = entry_for(stream(command_item("item_3", POWERSHELL + f"'Stop-Process -Id {FOREIGN_PID}'")))
        self.assertEqual(entry["outsideExits"], [])
        self.assertTrue(fails(entry))

    def test_a_remote_stop_keeps_the_text_verdict(self) -> None:
        for command in (POWERSHELL + "'Stop-Process -Name Roamer -ComputerName rig'",
                        "taskkill /S rig /IM Roamer.exe /F",
                        # Codex review: run through another channel, the stop is not next to a remote option.
                        'ssh rig "taskkill /IM Roamer.exe /F"',
                        POWERSHELL + "'icm -ComputerName rig { Stop-Process -Name Roamer }'",
                        POWERSHELL + "'Invoke-Command -Session $s { spps -Name Roamer }'",
                        "psexec \\\\rig taskkill /IM Roamer.exe /F",
                        "wsl kill 1", "docker exec builder kill 1"):
            with self.subTest(command=command):
                entry = entry_for(stream(command_item("item_4", command)))
                self.assertNotIn("outsideExits", entry)
                self.assertTrue(fails(entry))

    def test_a_command_that_decides_at_run_time_what_it_runs_keeps_the_text_verdict(self) -> None:
        """Codex review: `& $env:REMOTE_CLIENT rig 'taskkill ...'` reaches another machine without naming it."""
        for command in (POWERSHELL + "'& $env:REMOTE_CLIENT rig \"taskkill /IM Roamer.exe /F\"'",
                        POWERSHELL + "'iex \"Stop-Process -Name Roamer\"'",
                        POWERSHELL + "'Start-Process pwsh -ArgumentList \"Stop-Process -Name Roamer\"'",
                        POWERSHELL + "'& (\"s\" + \"sh\") rig taskkill /IM Roamer.exe'",
                        "pwsh -NoProfile -ec UwB0AG8AcAAtAFAAcgBvAGMAZQBzAHMA # Stop-Process",
                        "python -c \"import os; os.system('taskkill /IM Roamer.exe')\"",
                        "cmd /c %REMOTE_CLIENT% rig taskkill /IM Roamer.exe /F",
                        # Third review: a script can forward its argument anywhere.
                        "pwsh -File .\\forward.ps1 'Stop-Process -Name Roamer'",
                        "forward.cmd taskkill /IM Roamer.exe", "./forward 'kill 1'"):
            with self.subTest(command=command):
                entry = entry_for(stream(command_item("item_4", command)))
                self.assertNotIn("outsideExits", entry)
                self.assertTrue(fails(entry))

    def test_a_completion_without_a_start_keeps_the_text_verdict(self) -> None:
        """Codex review: with no start event the window would begin at the completion and miss an early stop."""
        [_, done] = command_item("item_8", POWERSHELL + "'Stop-Process -Name Roamer; Start-Sleep 60'")
        entry = entry_for(stream([done]))
        self.assertNotIn("outsideExits", entry)
        self.assertTrue(fails(entry))

    def test_without_complete_evidence_the_text_verdict_stands(self) -> None:
        stdout = stream(command_item("item_7", SEARCH_360))
        self.assertNotIn("outsideExits", entry_for(stdout, complete=False))
        self.assertTrue(fails(entry_for(stdout, complete=False)))
        [entry] = scan(stdout, frozenset(), (), None)
        self.assertNotIn("outsideExits", entry)
        self.assertTrue(fails(entry))
        # Decoding replaced bytes, so the byte offsets that place a command in time cannot be trusted.
        # The raw character, as errors="replace" leaves it; json.dumps would have escaped it.
        replaced = stream(command_item("item_7", SEARCH_360, "cafe")).replace("cafe", "caf�")
        self.assertNotIn("outsideExits", entry_for(replaced))

    def test_an_unfinished_command_s_window_runs_to_the_end(self) -> None:
        stdout = stream(command_item("item_7", SEARCH_360, completed=False))
        entry = entry_for(stdout, (ended(77, "Roamer.exe", at=START + 500),))
        self.assertIsNone(entry["window"][1])
        self.assertTrue(fails(entry))


class TheWatchItself(unittest.TestCase):
    def test_processes_are_tracked_by_pid_parent_and_name(self) -> None:
        clock = iter([0.0, 0.0, 1.0, 2.0, 3.0])
        snapshots = iter([
            {(10, 1, "a.exe"): "a.exe", (20, 1, "b.exe"): "b.exe"},
            {(10, 1, "a.exe"): "a.exe", (20, 1, "b.exe"): "b.exe", (30, 20, "c.exe"): "c.exe"},
            {(20, 1, "b.exe"): "b.exe", (30, 20, "c.exe"): "c.exe", (10, 5, "x.exe"): "x.exe"},  # 10 reused
            {(20, 1, "b.exe"): "b.exe", (10, 5, "x.exe"): "x.exe"},
        ])
        watch = ProcessWatch(snapshot=lambda: next(snapshots), clock=lambda: next(clock), ages=lambda pids: {})
        watch.sample(0, ())
        watch.sample(10, {30})
        watch.sample(20, ())
        watch.sample(30, ())
        result = watch.result()
        by = {(p.pid, p.exe): p for p in result.processes}
        self.assertEqual((by[(10, "a.exe")].first_seen, by[(10, "a.exe")].ended_by), (None, 2.0))
        self.assertEqual((by[(10, "x.exe")].first_seen, by[(10, "x.exe")].ended_by), (2.0, None))
        self.assertEqual((by[(30, "c.exe")].in_run, by[(30, "c.exe")].ended_by), (True, 3.0))
        self.assertFalse(by[(10, "x.exe")].in_run)
        self.assertEqual(result.stdout_timeline, ((0.0, 0), (1.0, 10), (2.0, 20), (3.0, 30)))
        self.assertTrue(result.complete)

    def test_one_failed_sample_makes_the_whole_watch_incomplete(self) -> None:
        snapshots = iter([{(1, 0, "a.exe"): "a.exe"}, None, {(1, 0, "a.exe"): "a.exe"}])
        watch = ProcessWatch(snapshot=lambda: next(snapshots), clock=lambda: 0.0, ages=lambda pids: {})
        for _ in range(3):
            watch.sample(0, ())
        self.assertFalse(watch.result().complete)

    def test_a_process_running_at_the_first_sample_keeps_its_real_age(self) -> None:
        """Codex review: one started a moment before the watch is young, not old, when its start time is known."""
        clock = iter([0.0, 0.0, 6.0])
        snapshots = iter([{(10, 1, "git.exe"): "git.exe", (20, 1, "Roamer.exe"): "Roamer.exe",
                           (30, 1, "csrss.exe"): "csrss.exe"}, {}])
        watch = ProcessWatch(snapshot=lambda: next(snapshots), clock=lambda: next(clock),
                             ages=lambda pids: {10: 1.0, 20: 3600.0})
        watch.sample(0, ())
        watch.sample(0, ())
        by = {p.exe: p for p in watch.result().processes}
        self.assertEqual((by["git.exe"].first_seen, by["Roamer.exe"].first_seen, by["csrss.exe"].first_seen),
                         (-1.0, -3600.0, None))
        # A command from 5 s: git is 6 s old at its start, Roamer an hour, csrss of unknown age counts as old.
        self.assertEqual(sorted(p.exe for p in outside_exits(watch.result(), 5.0, 7.0)), ["Roamer.exe", "csrss.exe"])

    @unittest.skipUnless(os.name == "nt", "process times are read through kernel32")
    def test_this_process_has_an_age(self) -> None:
        age = _process_ages([os.getpid()]).get(os.getpid())
        self.assertIsNotNone(age)
        self.assertGreater(age, 0.0)

    def test_arrival_brackets_a_byte_offset_between_two_samples(self) -> None:
        timeline = ((0.0, 0), (1.0, 50), (2.0, 120))
        self.assertEqual(arrival(timeline, 50), (0.0, 1.0))
        self.assertEqual(arrival(timeline, 51), (1.0, 2.0))
        self.assertEqual(arrival(timeline, 500), (2.0, None))

    @unittest.skipUnless(os.name == "nt", "Toolhelp is Windows-only")
    def test_the_toolhelp_snapshot_lists_this_process(self) -> None:
        processes = _toolhelp_snapshot()
        self.assertIsNotNone(processes)
        self.assertIn(os.getpid(), {pid for pid, _, _ in processes})


if __name__ == "__main__":
    unittest.main()
