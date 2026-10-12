"""Curator acceptance: the whole Codex event stream is kept as a journal next to the run record (#1052).

The record keeps a 4000-character tail with line breaks collapsed, so the event schema of a real run cannot be
checked from it. The journal keeps every captured line, in order, with JSON boundaries. Known secrets are removed
inside string values and the line is serialised again, so a valid event stays valid; a line that was not valid
JSON is kept as text and named as a problem - a damaged stream never becomes a clean journal.
"""

from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import scripts.external_worktree_executor as launcher
from scripts.agent_home_isolation import IsolatedHome
from scripts.executor_host_guard import HostSnapshot
from scripts.executor_primitives import ProcessResult

SECRET = 'tok"en\\with-quote-0123456789'  # a quote and a backslash: escaped differently inside JSON
EVENTS = [
    {"type": "thread.started", "thread_id": "t"},
    {"type": "turn.started"},
    {"type": "item.completed", "item": {"id": "item_1", "type": "agent_message",
                                        "text": "first line\nsecond\tline \"quoted\" \\ end\n" + "x" * 4200}},
    {"type": "item.completed", "item": {"id": "item_2", "type": "command_execution", "command": "echo",
                                        "aggregated_output": "key=" + SECRET + " end", "exit_code": 0,
                                        "status": "completed"}},
    {"type": "turn.completed", "usage": {"input_tokens": 1}},
]
STREAM = "\n".join(json.dumps(event) for event in EVENTS) + "\n"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class JournalContents(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / "run.json.events.jsonl"

    def write(self, stdout: str, **limits: bool) -> dict[str, object]:
        from scripts.executor_event_journal import write_event_journal
        return write_event_journal(stdout, {SECRET}, self.path, timed_out=limits.get("timed_out", False),
                                   output_too_large=limits.get("output_too_large", False))

    def test_a_long_stream_is_kept_whole_valid_and_without_the_secret(self) -> None:
        self.assertGreater(len(STREAM), 4000)
        result = self.write(STREAM)
        self.assertEqual(result, {"path": str(self.path), "bytes": self.path.stat().st_size,
                                  "sha256": sha256(self.path), "lines": 5, "problems": []})
        text = self.path.read_text(encoding="utf-8")
        self.assertNotIn(SECRET, text)
        self.assertNotIn(json.dumps(SECRET)[1:-1], text)
        expected = json.loads(json.dumps(EVENTS))
        expected[3]["item"]["aggregated_output"] = "key=[REDACTED] end"
        self.assertEqual([json.loads(row) for row in text.splitlines()], expected)

    def test_damaged_lines_are_kept_and_named_never_a_clean_journal(self) -> None:
        rows = [json.dumps(EVENTS[0]), json.dumps(EVENTS[1]), '{"type":"item.comp' + SECRET,
                json.dumps(EVENTS[4]), json.dumps(EVENTS[4])[:-5]]
        result = self.write("\n".join(rows))
        self.assertEqual((result["lines"], result["problems"]), (5, ["unparsed_line:3", "unparsed_line:5"]))
        kept = self.path.read_text(encoding="utf-8").splitlines()
        self.assertEqual(kept[2], '{"type":"item.comp[REDACTED]')
        self.assertEqual(kept[4], json.dumps(EVENTS[4])[:-5])
        self.assertNotIn(SECRET, "\n".join(kept))

    def test_a_token_a_command_printed_is_removed_like_in_the_record_tail(self) -> None:
        """Codex review P1 on #1070: tokens the launcher never collected still have known shapes."""
        printed = "use Bearer abc.def-123 now; key sk-ABCDEFGHIJ12 end; token=xyz9 end"
        event = {"type": "item.completed", "item": {"id": "item_3", "type": "command_execution",
                                                    "aggregated_output": printed}}
        result = self.write(json.dumps(event) + "\n" + "garbage Bearer qwe.rty-1 sk-ABCDEFGHIJ12\n")
        self.assertEqual(result["problems"], ["unparsed_line:2"])
        kept = self.path.read_text(encoding="utf-8").splitlines()
        self.assertEqual(json.loads(kept[0])["item"]["aggregated_output"],
                         "use Bearer [REDACTED] now; key [REDACTED] end; token=[REDACTED] end")
        self.assertEqual(kept[1], "garbage Bearer [REDACTED] [REDACTED]")

    def test_a_cut_or_late_stream_is_written_and_marked_incomplete(self) -> None:
        result = self.write(STREAM, timed_out=True, output_too_large=True)
        self.assertEqual((result["lines"], result["problems"]), (5, ["output_too_large", "timed_out"]))


class JournalInTheRunRecord(unittest.TestCase):
    """run() writes the journal for every outcome, outside the runtime it deletes, and names it in the record.

    The token sits in a fake auth.json and matches none of _redact's patterns: only the launcher's own secret
    collector can know it. Each outcome gets its own directories, so one case cannot pass on another's file.
    """

    ACCESS = 'plain"tok\\en-7f3a9c2e5b1d40aa'

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
        for folder in (self.worktree, self.owner, home / "credentials"):
            folder.mkdir(parents=True)
        (home / "credentials" / "auth.json").write_text(json.dumps({"access_token": self.ACCESS}), encoding="utf-8")
        self.home = IsolatedHome(provider="codex", path=home, operator=self.root / "op", env={}, inventory=())
        events = json.loads(json.dumps(EVENTS))
        events[3]["item"]["aggregated_output"] = "key=" + self.ACCESS + " end"
        self.stream = "\n".join(json.dumps(event) for event in events) + "\n"
        self.expected = json.loads(json.dumps(events))
        self.expected[3]["item"]["aggregated_output"] = "key=[REDACTED] end"

    def run_codex(self, case: str, process: ProcessResult) -> tuple[dict[str, object], Path, list[Path]]:
        folder = self.root / case
        (folder / "records").mkdir(parents=True)
        (folder / "temp").mkdir()
        record_path = folder / "records" / "run.json"
        runtimes: list[Path] = []
        original = tempfile.mkdtemp

        def make_runtime(**_kwargs: object) -> str:
            runtimes.append(Path(original(dir=folder / "temp")))
            return str(runtimes[-1])

        with (
            mock.patch.object(launcher.tempfile, "mkdtemp", side_effect=make_runtime),
            mock.patch.object(launcher, "resolve_worktree", return_value=self.worktree),
            mock.patch.object(launcher, "main_checkout", return_value=self.owner),
            mock.patch.object(launcher, "fingerprint", side_effect=["a", "a"]),
            mock.patch.object(launcher, "_resolve_executable", return_value=Path("codex")),
            mock.patch.object(launcher, "prepare_isolated_home", return_value=self.home),
            mock.patch.object(launcher, "build_argv", return_value=(["codex"], None)),
            mock.patch.object(launcher, "check_codex_isolation", return_value=None),
            mock.patch.object(launcher, "_run_process", return_value=process),
            mock.patch.object(launcher, "persist_rotated_credentials", return_value=[]),
        ):
            record = launcher.run("codex", self.worktree, self.brief, record_path=record_path)
        return record, Path(str(record_path) + ".events.jsonl"), runtimes

    def test_every_outcome_keeps_its_own_journal_without_the_collected_secret(self) -> None:
        self.assertIn(json.dumps(self.ACCESS)[1:-1], self.stream)  # the token really is in the input
        for case, process, problems in (
            ("exit-0", ProcessResult(0, self.stream, "", 1.0), []),
            ("exit-1", ProcessResult(1, self.stream, "boom", 1.0), []),
            ("timeout", ProcessResult(124, self.stream, "", 1.0, timed_out=True), ["timed_out"]),
        ):
            with self.subTest(case=case):
                self.assertFalse((self.root / case).exists())
                record, journal, runtimes = self.run_codex(case, process)
                self.assertTrue(journal.is_file(), "no journal next to the record")
                self.assertEqual(record.get("eventJournal"),
                                 {"path": str(journal), "bytes": journal.stat().st_size, "sha256": sha256(journal),
                                  "lines": 5, "problems": problems})
                text = journal.read_text(encoding="utf-8")
                self.assertEqual([json.loads(row) for row in text.splitlines()], self.expected)
                self.assertNotIn(self.ACCESS, text)
                self.assertNotIn(json.dumps(self.ACCESS)[1:-1], text)
                self.assertEqual(len(runtimes), 1)
                self.assertTrue(record["runtimeDirectoryRemoved"])
                self.assertFalse(runtimes[0].exists())
                self.assertFalse(journal.resolve().is_relative_to(runtimes[0].resolve()))

if __name__ == "__main__":
    unittest.main()
