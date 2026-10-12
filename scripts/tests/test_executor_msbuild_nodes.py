from __future__ import annotations

import json
import os
import subprocess
import sys
import unittest
from unittest import mock

import scripts.executor_msbuild_nodes as subject
from scripts import executor_primitives as primitives
from scripts.executor_msbuild_nodes import ProcessInfo

WORKER = r'"C:\Program Files\dotnet\dotnet.exe" "C:\Program Files\dotnet\sdk\9.0.318\MSBuild.dll" /noautoresponse /nologo /nodemode:1 /nodeReuse:false /low:false'
BUILD = r'"C:\Program Files\dotnet\dotnet.exe" build tests/Avox.E3D.McpServer.Tests'
STARTED, FINISHED = 1_000_000, 2_000_000


def process(pid: int, parent: int, created: int, command: str = WORKER, name: str = "dotnet.exe") -> ProcessInfo:
    return ProcessInfo(pid=pid, parent_pid=parent, name=name, command_line=command, created_ms=created)


RUN = "run-1"


def select(*processes: ProcessInfo, markers: dict[int, str | None] | None = None) -> list[int]:
    marker_of = (lambda pid: markers.get(pid)) if markers is not None else (lambda pid: RUN)
    return [node.pid for node in subject.orphan_msbuild_nodes(processes, STARTED, FINISHED, RUN, marker_of)]


class OrphanSelectionTests(unittest.TestCase):
    """Observed 2026-09-28: 131 of these (~4 GB) outlived their parents during one Codex run."""

    def test_a_worker_whose_parent_is_gone_is_selected(self) -> None:
        self.assertEqual(select(process(10, 99, 1_500_000)), [10])

    def test_a_worker_whose_parent_still_runs_is_kept(self) -> None:
        self.assertEqual(select(process(1, 0, 1_100_000, command=BUILD), process(10, 1, 1_500_000)), [])

    def test_a_parent_pid_taken_by_a_later_process_counts_as_gone(self) -> None:
        # Windows reuses PIDs: the process now holding the parent's PID started after the node.
        self.assertEqual(select(process(1, 0, 1_600_000, command="notepad.exe", name="notepad.exe"),
                                process(10, 1, 1_500_000)), [10])

    def test_a_worker_from_outside_the_run_window_is_kept(self) -> None:
        self.assertEqual(select(process(10, 99, STARTED - 1), process(11, 99, FINISHED + 1)), [])
        self.assertEqual(select(process(10, 99, STARTED), process(11, 99, FINISHED)), [10, 11])

    def test_an_orphan_from_another_run_or_with_an_unreadable_environment_is_kept(self) -> None:
        # Codex review on #577: creation time and a missing parent do not say whose orphan it is.
        orphans = [process(10, 99, 1_500_000), process(11, 99, 1_500_000), process(12, 99, 1_500_000)]
        self.assertEqual(select(*orphans, markers={10: RUN, 11: "run-2", 12: None}), [10])

    def test_without_a_marker_nothing_is_selected_and_the_environment_is_read_only_for_candidates(self) -> None:
        asked: list[int] = []
        live = [process(1, 0, 1_100_000, command=BUILD), process(10, 1, 1_500_000), process(11, 99, 1_500_000)]
        subject.orphan_msbuild_nodes(live, STARTED, FINISHED, RUN, lambda pid: asked.append(pid) or RUN)
        self.assertEqual(asked, [11])
        self.assertEqual(subject.orphan_msbuild_nodes(live, STARTED, FINISHED, "", lambda pid: ""), [])

    def test_a_node_with_reuse_on_is_kept(self) -> None:
        self.assertEqual(select(process(10, 99, 1_500_000, command=WORKER.replace("false", "true"))), [])

    def test_other_dotnet_and_msbuild_processes_are_kept(self) -> None:
        build = r'"C:\Program Files\dotnet\dotnet.exe" "C:\Program Files\dotnet\sdk\9.0.318\MSBuild.dll" -maxcpucount -restore'
        server = r'"C:\Users\x\AppData\Local\Avox\E3D\Mcp\server\Avox.E3D.McpServer.exe" --http'
        self.assertEqual(select(
            process(10, 99, 1_500_000, command=build),
            process(11, 99, 1_500_000, command=server, name="Avox.E3D.McpServer.exe"),
            process(12, 99, 1_500_000, name="MSBuild.exe"),
            process(13, 99, 1_500_000, command=""),
        ), [])


class SnapshotTests(unittest.TestCase):
    def test_a_snapshot_parses_a_list_a_single_row_and_a_missing_command_line(self) -> None:
        rows = subject.parse_snapshot(json.dumps([{"p": 4, "q": 0, "n": "System", "c": None, "t": 0},
                                                  {"p": 10, "q": 4, "n": "dotnet.exe", "c": WORKER, "t": 5}]))
        self.assertEqual([(row.pid, row.parent_pid, row.command_line, row.created_ms) for row in rows],
                         [(4, 0, "", 0), (10, 4, WORKER, 5)])
        self.assertEqual(len(subject.parse_snapshot('{"p": 1, "q": 0, "n": "x", "c": "", "t": 1}')), 1)
        self.assertEqual(subject.parse_snapshot(""), [])

    def test_terminate_stops_only_what_the_verified_stop_accepts(self) -> None:
        nodes = [process(10, 99, 1_500_000), process(11, 99, 1_500_000)]
        asked: list[tuple[int, str]] = []
        stopped = subject.terminate(nodes, RUN, stop=lambda node, marker: asked.append((node.pid, marker)) or node.pid == 11)
        self.assertEqual(stopped, [11])
        self.assertEqual(asked, [(10, RUN), (11, RUN)])


@unittest.skipUnless(os.name == "nt", "reads a real Windows process environment")
class ProcessHandleTests(unittest.TestCase):
    def test_only_a_child_with_this_runs_marker_and_its_own_creation_time_is_stopped(self) -> None:
        sleeper = [sys.executable, "-c", "import time; time.sleep(30)"]
        marked = subprocess.Popen(sleeper, env=dict(os.environ, **{subject.RUN_MARKER: "run-live"}))
        plain_env = {name: value for name, value in os.environ.items() if name != subject.RUN_MARKER}
        plain = subprocess.Popen(sleeper, env=plain_env)
        try:
            for _ in range(50):
                if subject.read_environment_variable(marked.pid, subject.RUN_MARKER) == "run-live":
                    break
                subprocess.run([sys.executable, "-c", "import time; time.sleep(0.1)"], check=False)
            self.assertEqual(subject.read_environment_variable(marked.pid, subject.RUN_MARKER), "run-live")
            self.assertIsNone(subject.read_environment_variable(plain.pid, subject.RUN_MARKER))

            # Codex review on #577: the stop goes through one handle that re-checks identity and marker.
            created = subject.process_created_ms(marked.pid)
            self.assertIsNotNone(created)
            node = process(marked.pid, 0, created)
            self.assertFalse(subject.stop_verified(process(marked.pid, 0, created + 5_000), "run-live"))
            self.assertFalse(subject.stop_verified(node, "run-other"))
            self.assertIsNone(marked.poll())
            self.assertFalse(subject.stop_verified(process(plain.pid, 0, subject.process_created_ms(plain.pid)), "run-live"))
            self.assertTrue(subject.stop_verified(node, "run-live"))
            self.assertIsNotNone(marked.wait(timeout=10))
            self.assertIsNone(plain.poll())
        finally:
            marked.kill()
            plain.kill()
            marked.wait()
            plain.wait()


class CleanupTests(unittest.TestCase):
    def test_only_the_selected_orphans_are_terminated(self) -> None:
        snapshot = [process(1, 0, 1_100_000, command=BUILD), process(10, 1, 1_500_000), process(11, 99, 1_500_000)]
        with (
            mock.patch.object(subject.os, "name", "nt"),
            mock.patch.object(subject, "snapshot_processes", return_value=snapshot),
            mock.patch.object(subject, "read_environment_variable", return_value=RUN) as reader,
            mock.patch.object(subject, "terminate", return_value=[11]) as stop,
        ):
            self.assertEqual(subject.clean_orphan_msbuild_nodes(STARTED, FINISHED, RUN), {"found": 1, "terminated": 1})
        reader.assert_called_once_with(11, subject.RUN_MARKER)
        self.assertEqual([node.pid for node in stop.call_args.args[0]], [11])

    def test_no_orphans_means_nothing_is_stopped(self) -> None:
        with (
            mock.patch.object(subject.os, "name", "nt"),
            mock.patch.object(subject, "snapshot_processes", return_value=[]),
            mock.patch.object(subject, "terminate") as stop,
        ):
            self.assertEqual(subject.clean_orphan_msbuild_nodes(STARTED, FINISHED, RUN), {"found": 0, "terminated": 0})
        stop.assert_not_called()

    def test_a_failed_query_is_reported_not_raised(self) -> None:
        with (
            mock.patch.object(subject.os, "name", "nt"),
            mock.patch.object(subject, "snapshot_processes", side_effect=OSError("access denied")),
        ):
            result = subject.clean_orphan_msbuild_nodes(STARTED, FINISHED, RUN)
        self.assertEqual(result, {"found": None, "terminated": 0, "problem": "access denied"})

    def test_off_windows_nothing_is_queried(self) -> None:
        with (
            mock.patch.object(subject.os, "name", "posix"),
            mock.patch.object(subject, "snapshot_processes") as query,
        ):
            self.assertEqual(subject.clean_orphan_msbuild_nodes(STARTED, FINISHED, RUN)["skipped"], "not_windows")
        query.assert_not_called()


class ChildEnvironmentTests(unittest.TestCase):
    def test_nodes_that_never_connect_give_up_after_a_minute_and_reuse_stays_off(self) -> None:
        self.assertEqual(primitives.DOTNET_FIXED_ENV["MSBUILDNODECONNECTIONTIMEOUT"], "60000")
        self.assertEqual(primitives.DOTNET_FIXED_ENV["MSBUILDDISABLENODEREUSE"], "1")


if __name__ == "__main__":
    unittest.main()
