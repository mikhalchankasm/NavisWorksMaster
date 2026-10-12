"""Curator acceptance for #259: a process stop outside the executor's own run fails the run.

In the #249 run Codex ran `Stop-Process -Id 43204` against a dotnet process it had not shown to be its own.
Neither the launcher nor the record flagged it; only a manual reading of the event journal found it. These
fixture journals use the shape Codex really writes: the command is a PowerShell invocation wrapping the stop.

A stop is allowed only for a PID the launcher saw inside the executor's own process tree (its Job Object on
Windows) during the run; `ProcessResult.process_tree_pids` carries that set. Everything else - a PID outside
it, a stop by name or by pipeline with no PID to check - is a `PROCESS_STOP_OUTSIDE_RUN` host violation.
"""

from __future__ import annotations

import json
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import scripts.external_worktree_executor as launcher
from scripts.agent_home_isolation import IsolatedHome
from scripts.executor_host_guard import HostSnapshot, violations
from scripts.executor_primitives import ProcessResult

POWERSHELL = '"C:\\\\windows\\\\System32\\\\WindowsPowerShell\\\\v1.0\\\\powershell.exe" -Command '
OWN_PID = 4321
FOREIGN_PID = 43204


def command_item(item_id: str, command: str, output: str = "", *, completed: bool = True) -> list[dict]:
    started = {"type": "item.started", "item": {"id": item_id, "type": "command_execution", "command": command,
                                                 "aggregated_output": "", "exit_code": None,
                                                 "status": "in_progress"}}
    if not completed:
        return [started]
    done = {"type": "item.completed", "item": {"id": item_id, "type": "command_execution", "command": command,
                                                "aggregated_output": output, "exit_code": 0,
                                                "status": "completed"}}
    return [started, done]


def stream(*items: list[dict]) -> str:
    events = [{"type": "thread.started", "thread_id": "t"}, {"type": "turn.started"}]
    for item in items:
        events.extend(item)
    events.append({"type": "turn.completed", "usage": {"input_tokens": 1}})
    # Compact, as Codex writes it: a redaction that eats to the next space then runs past a closing quote.
    return "\n".join(json.dumps(event, separators=(",", ":")) for event in events) + "\n"


class ProcessControlInTheRecord(unittest.TestCase):
    """run() and main() with the provider replaced: only the journal and the observed tree differ per case."""

    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        quiet = HostSnapshot(environment={"PATH": "same"}, sandbox_accounts=frozenset(), acls={})
        for name, value in (("take_snapshot", quiet), ("clean_orphan_msbuild_nodes", {"found": 0, "terminated": 0})):
            patcher = mock.patch.object(launcher, name, return_value=value)
            patcher.start()
            self.addCleanup(patcher.stop)
        self.brief = self.root / "brief.md"
        self.brief.write_text("## Outcome\nProbe.\n", encoding="utf-8")
        self.worktree, self.owner, home = self.root / "wt", self.root / "owner", self.root / "home"
        for folder in (self.worktree, self.owner, home):
            folder.mkdir(parents=True)
        self.home = IsolatedHome(provider="codex", path=home, operator=self.root / "op", env={}, inventory=())

    def run_codex(self, stdout: str, tree: frozenset[int] = frozenset()) -> tuple[dict[str, object], int]:
        process = ProcessResult(0, stdout, "", 1.0, process_tree_pids=tree)
        record_path = self.root / "records" / f"run-{len(list(self.root.glob('records/*.json')))}.json"
        record_path.parent.mkdir(exist_ok=True)
        with (
            mock.patch.object(launcher, "resolve_worktree", return_value=self.worktree),
            mock.patch.object(launcher, "main_checkout", return_value=self.owner),
            mock.patch.object(launcher, "fingerprint", side_effect=["a", "a"]),
            mock.patch.object(launcher, "_resolve_executable", return_value=Path("codex")),
            mock.patch.object(launcher, "prepare_isolated_home", return_value=self.home),
            mock.patch.object(launcher, "build_argv", return_value=(["codex"], None)),
            mock.patch.object(launcher, "check_codex_isolation", return_value=None),
            mock.patch.object(launcher, "_run_process", return_value=process),
            mock.patch.object(launcher, "persist_rotated_credentials", return_value=[]),
            mock.patch.object(launcher, "_remove_runtime", return_value=(True, "")),
        ):
            code = launcher.main(["--provider", "codex", "--worktree", str(self.worktree), "--brief",
                                  str(self.brief), "--record", str(record_path)])
        return json.loads(record_path.read_text(encoding="utf-8")), code

    def assert_violation(self, command: str, output: str, pids: list[int]) -> None:
        record, code = self.run_codex(stream(command_item("item_30", command, output)))
        self.assertEqual(record["host"]["processControl"], [
            {"itemId": "item_30", "command": command, "output": output, "pids": pids, "inRunTree": False}])
        self.assertIn("PROCESS_STOP_OUTSIDE_RUN", record["hostViolations"])
        self.assertEqual(code, 6)

    def test_stop_process_by_id_is_a_violation(self) -> None:
        """The #249 command, as Codex wrote it."""
        self.assert_violation(POWERSHELL + f"'Stop-Process -Id {FOREIGN_PID}; dotnet test -nodeReuse:false'",
                              "Stop-Process : Cannot find a process with the process identifier 43204.",
                              [FOREIGN_PID])

    def test_taskkill_by_pid_is_a_violation(self) -> None:
        self.assert_violation(POWERSHELL + f"'taskkill /PID {FOREIGN_PID} /F'",
                              f"SUCCESS: The process with PID {FOREIGN_PID} has been terminated.", [FOREIGN_PID])

    def test_a_pipeline_stop_has_no_pid_to_prove_and_is_a_violation(self) -> None:
        self.assert_violation(POWERSHELL + "'Get-Process dotnet | Stop-Process -Force'", "", [])

    def test_the_other_termination_forms_are_violations(self) -> None:
        for command, pids in (
            (f"kill -9 {FOREIGN_PID}", [FOREIGN_PID]),
            (POWERSHELL + f"'[System.Diagnostics.Process]::GetProcessById({FOREIGN_PID}).Kill()'", [FOREIGN_PID]),
            (f"wmic process where processid={FOREIGN_PID} call terminate", [FOREIGN_PID]),
            (POWERSHELL + "'Get-CimInstance Win32_Process -Filter \"Name=''dotnet.exe''\" | "
                          "Invoke-CimMethod -MethodName Terminate'", []),
        ):
            with self.subTest(command=command):
                self.assert_violation(command, "", pids)

    def test_listing_processes_alone_is_not_process_control(self) -> None:
        record, code = self.run_codex(stream(
            command_item("item_27", POWERSHELL + "'Get-CimInstance Win32_Process -Filter \"Name=''dotnet.exe''\"'",
                         "Get-CimInstance : Access denied"),
            command_item("item_29", POWERSHELL + "'Get-Process dotnet | Select-Object Id,StartTime,CPU'",
                         "40908 11:00:42\n43204 11:12:27 0.19\n56268 11:00:43"),
        ))
        self.assertEqual(record["host"]["processControl"], [])
        self.assertNotIn("hostViolations", record)
        self.assertEqual(code, 0)

    def test_a_stop_inside_the_run_s_own_process_tree_is_recorded_but_allowed(self) -> None:
        tree = frozenset({OWN_PID, 9999})
        for command in (POWERSHELL + f"'Stop-Process -Id {OWN_PID}'", f"taskkill /PID {OWN_PID} /T /F"):
            with self.subTest(command=command):
                record, code = self.run_codex(stream(command_item("item_5", command, "ok")), tree)
                self.assertEqual(record["host"]["processControl"], [
                    {"itemId": "item_5", "command": command, "output": "ok", "pids": [OWN_PID], "inRunTree": True}])
                self.assertNotIn("hostViolations", record)
                self.assertEqual(code, 0)

    def test_an_own_pid_does_not_prove_a_selection_or_a_remote_stop(self) -> None:
        """Codex review P1s: a WMI predicate can select more than the PID it names, and a remote PID is not ours."""
        for command in (
            f'wmic process where "ProcessId={OWN_PID} OR ProcessId>0" call terminate',
            f"wmic process where processid={OWN_PID} call terminate",
            POWERSHELL + f"'Get-CimInstance Win32_Process -Filter \"ProcessId={OWN_PID}\" | "
                         "Invoke-CimMethod -MethodName Terminate'",
            f"taskkill /S otherhost /PID {OWN_PID} /F",
            f"taskkill /PID {OWN_PID} /FI \"STATUS eq RUNNING\" /F",
            f"wmic /node:otherhost process where processid={OWN_PID} call terminate",
            f"tskill {OWN_PID} /SERVER:otherhost",
        ):
            with self.subTest(command=command):
                record, code = self.run_codex(stream(command_item("item_4", command)), frozenset({OWN_PID}))
                self.assertFalse(record["host"]["processControl"][0]["inRunTree"])
                self.assertEqual(code, 6)

    def test_one_foreign_pid_beside_an_own_one_is_a_violation(self) -> None:
        command = POWERSHELL + f"'Stop-Process -Id {OWN_PID},{FOREIGN_PID}'"
        record, code = self.run_codex(stream(command_item("item_6", command)), frozenset({OWN_PID}))
        self.assertEqual(record["host"]["processControl"][0]["pids"], [OWN_PID, FOREIGN_PID])
        self.assertFalse(record["host"]["processControl"][0]["inRunTree"])
        self.assertEqual(code, 6)

    def test_a_stop_in_a_run_cut_before_the_item_completed_still_counts(self) -> None:
        command = POWERSHELL + f"'Stop-Process -Id {FOREIGN_PID}'"
        record, code = self.run_codex(stream(command_item("item_7", command, completed=False)))
        self.assertEqual(record["host"]["processControl"], [
            {"itemId": "item_7", "command": command, "output": "", "pids": [FOREIGN_PID], "inRunTree": False}])
        self.assertIn("PROCESS_STOP_OUTSIDE_RUN", record["hostViolations"])
        self.assertEqual(code, 6)

    def test_the_249_journal_excerpt_flags_only_the_stop(self) -> None:
        """Items 27, 29 and 30 of the #249 journal, shortened; item_30 exited 0 although the stop failed."""
        item_27 = ('"C:\\\\windows\\\\System32\\\\WindowsPowerShell\\\\v1.0\\\\powershell.exe" -Command "Get-CimInstance '
                   'Win32_Process -Filter \\"Name=\'dotnet.exe\'\\" | Select-Object ProcessId,ParentProcessId,CommandLine; '
                   'git diff --check; git status --short"')
        item_29 = (POWERSHELL + "'Get-Process dotnet -ErrorAction SilentlyContinue | Select-Object Id,CPU,StartTime; "
                   "Get-ChildItem artifacts/249-test-temp -Force | Select-Object -First 8 Name,LastWriteTime'")
        item_30 = (POWERSHELL + "'Stop-Process -Id 43204; $env:DOTNET_CLI_HOME = Join-Path (Get-Location) "
                   "'\"'artifacts\\\\249-dotnet-home'; \"'dotnet test NavisHelper.McpServer.Tests/"
                   "NavisHelper.McpServer.Tests.csproj -c Release --filter \"FullyQualifiedName~ServerVersionStoreTests\" "
                   "--no-restore -nodeReuse:false -m:1 --verbosity normal'")
        output = "+ CategoryInfo : ObjectNotFound: (43204:Int32) [Stop-Process], ProcessCommandException"
        journal = stream(command_item("item_27", item_27, "PermissionDenied: (:) [Get-CimInstance], CimException"),
                         command_item("item_29", item_29, "40908 11:00:42\n43204 11:12:27\n56268 11:00:43"),
                         command_item("item_30", item_30, output))
        stop = {"itemId": "item_30", "command": item_30, "output": output, "pids": [43204]}
        record, code = self.run_codex(journal, frozenset({40100}))
        self.assertEqual(record["host"]["processControl"], [dict(stop, inRunTree=False)])
        self.assertEqual((record["hostViolations"], code), (["PROCESS_STOP_OUTSIDE_RUN"], 6))
        # Had the launcher seen 43204 in the run's own Job - the issue's unproven guess - the stop passes.
        record, code = self.run_codex(journal, frozenset({40100, 43204}))
        self.assertEqual(record["host"]["processControl"], [dict(stop, inRunTree=True)])
        self.assertEqual(code, 0)

    def test_a_token_shape_after_the_stop_neither_hides_it_nor_reaches_the_record(self) -> None:
        """Review of the executor's scanner: `token=\\S+` redaction eats the event's closing quotes.

        Scanning the redacted stream would drop the whole event as unparsable JSON - a stop missed, a clean
        run. The events are parsed first and only the kept strings are redacted.
        """
        command = POWERSHELL + f"'Stop-Process -Id {FOREIGN_PID}; curl.exe https://x.test -H token=abc123secret'"
        record, code = self.run_codex(stream(command_item("item_8", command, "authorization: abc123secret")))
        entries = record["host"]["processControl"]
        self.assertEqual([(entry["itemId"], entry["pids"], entry["inRunTree"]) for entry in entries],
                         [("item_8", [FOREIGN_PID], False)])
        self.assertNotIn("abc123secret", json.dumps(entries))
        self.assertEqual(code, 6)

    def test_a_credential_the_launcher_collected_is_removed_from_the_entry(self) -> None:
        from scripts.executor_process_control import scan

        secret = 'plain"tok\\en-7f3a9c2e5b1d40aa'  # a quote and a backslash, escaped inside the JSON line
        command = POWERSHELL + f"'Stop-Process -Id {FOREIGN_PID}; echo {secret}'"
        entries = scan(stream(command_item("item_9", command, "printed " + secret)), frozenset(), {secret})
        self.assertEqual(entries[0]["command"], command.replace(secret, "[REDACTED]"))
        self.assertEqual(entries[0]["output"], "printed [REDACTED]")

    def test_the_violation_is_named_by_the_host_guard(self) -> None:
        stop = {"itemId": "item_1", "command": "taskkill /PID 1 /F", "output": "", "pids": [1], "inRunTree": False}
        self.assertEqual(violations({"processControl": [stop]}, windows=False), ["PROCESS_STOP_OUTSIDE_RUN"])
        self.assertEqual(violations({"processControl": [dict(stop, inRunTree=True)]}, windows=False), [])


@unittest.skipUnless(os.name == "nt", "the executor's process tree is a Windows Job Object")
class TheRunsOwnProcessTreeIsObserved(unittest.TestCase):
    """The real _run_process: a child and its grandchild are both seen inside the run's Job.

    Nothing is stopped here: the grandchild sleeps briefly and exits, and the child waits for it.
    """

    def test_child_and_grandchild_pids_are_in_the_observed_tree(self) -> None:
        from scripts.executor_primitives import _run_process

        script = (
            "import os, subprocess, sys\n"
            "grandchild = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(2)'])\n"
            "print(os.getpid(), grandchild.pid, flush=True)\n"
            "grandchild.wait()\n"
        )
        with tempfile.TemporaryDirectory() as folder:
            result = _run_process([sys.executable, "-c", script], cwd=Path(folder), timeout_seconds=60,
                                  env=dict(os.environ))
        self.assertEqual(result.return_code, 0, result.stderr)
        child, grandchild = (int(value) for value in result.stdout.split())
        self.assertIn(child, result.process_tree_pids)
        self.assertIn(grandchild, result.process_tree_pids)
        self.assertNotIn(os.getpid(), result.process_tree_pids)


class ThePreambleForbidsProcessStops(unittest.TestCase):
    def test_the_executor_is_told_never_to_stop_a_process_and_to_report_a_lock(self) -> None:
        from scripts.executor_worktree import PREAMBLE

        self.assertIn("Never stop, kill or signal any process", PREAMBLE)
        self.assertIn("report the lock and stop", PREAMBLE)


if __name__ == "__main__":
    unittest.main()
