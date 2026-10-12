"""Curator acceptance for the launcher slice of #1052: prevention before the run, observability after it.

BEFORE the run (prevention): the `codex` route keeps Codex's approval policy at `never` with an explicit
workspace-write sandbox. In codex 0.161 `--approve-for-me` appends `approvals_reviewer="auto_review"` and
`approval_policy="on-request"` after every `-c` override (utils/cli/src/shared_options.rs#L70-L83, called by
exec/src/lib.rs#L285-L286), so the route must carry no flag that adds a later override. These argv checks do
NOT show that the real sandbox holds; that is the curator's control run.
AFTER the run (detection, cannot undo anything): the record says whether the event stream is complete.
A complete stream proves completion only, not that nothing escaped the sandbox.

Where the expected structure comes from:
- seen in the real records of runs 1043-1051 (4000-character tails): item events carry `item.id` (`item_N`) and
  `item.type`; `agent_message` items arrive only as `item.completed`; `command_execution` as started/completed;
  `turn.completed` carries `usage`; the two timed-out runs end without `turn.completed`;
- NOT seen in any record (tails never reach the start): `thread.started` first and `turn.started` before items.
  They come from the event list of the installed 0.161 binary; the control run confirms or corrects them.
The lines below are synthetic in those shapes and do not prove the real CLI format.
"""

from __future__ import annotations

import json
import unittest
from pathlib import Path

import scripts.executor_host_guard as guard
import scripts.executor_worktree as worktree


def line(kind: str, **fields: object) -> str:
    return json.dumps({"type": kind, **fields})


COMMAND = {"id": "item_1", "type": "command_execution", "command": "dotnet test", "aggregated_output": "",
           "exit_code": None, "status": "in_progress"}
COMPLETE = [
    line("thread.started", thread_id="t"),
    line("turn.started"),
    line("item.started", item=COMMAND),
    line("item.completed", item={**COMMAND, "exit_code": 0, "status": "completed"}),
    line("item.completed", item={"id": "item_2", "type": "agent_message", "text": "done"}),  # no item.started
    line("turn.completed", usage={"input_tokens": 1, "output_tokens": 1}),
]


# Flags that set the approval policy or reviewer after the -c overrides, or drop the sandbox.
LATER_OVERRIDES = ("--approve-for-me", "--not-so-yolo", "--full-auto", "--dangerously-bypass-approvals-and-sandbox",
                   "--yolo", "-a", "--ask-for-approval")


def route_problems(argv: list[str]) -> list[str]:
    """Why an argv could leave Codex with anything but approval_policy=never in a workspace-write sandbox."""
    problems = ["later_override:" + flag for flag in LATER_OVERRIDES if flag in argv]
    pairs = [argv[i + 1] for i, argument in enumerate(argv[:-1]) if argument == "-c"]
    policy = [pair for pair in pairs if pair.split("=", 1)[0].strip() in
              ("approval_policy", "approvals_reviewer", "sandbox_mode")]
    if policy != ['approval_policy="never"']:
        problems.append("policy_overrides:" + ",".join(policy))
    sandboxes = [argv[i + 1] for i, argument in enumerate(argv[:-1]) if argument == "--sandbox"]
    if sandboxes != ["workspace-write"]:
        problems.append("sandbox:" + ",".join(sandboxes))
    return problems


class PreventionBeforeTheRun(unittest.TestCase):
    def test_the_executor_route_leaves_approvals_at_never(self) -> None:
        argv, _ = worktree.build_argv("codex", Path("codex.js"), "PROMPT", {})
        self.assertEqual(route_problems(argv), [])

    def test_the_route_first_shipped_in_1066_is_rejected(self) -> None:
        """never beside --approve-for-me: the flag's later overrides void never (Codex review P1)."""
        argv, _ = worktree.build_argv("codex", Path("codex.js"), "PROMPT", {})
        at = argv.index("--sandbox")
        shipped = argv[:at] + ["--approve-for-me"] + argv[at + 2:]
        self.assertEqual(route_problems(shipped), ["later_override:--approve-for-me", "sandbox:"])
        late = argv[:-1] + ["-c", 'approval_policy="on-request"', argv[-1]]
        self.assertEqual(route_problems(late),
                         ['policy_overrides:approval_policy="never",approval_policy="on-request"'])


class ObservationAfterTheRun(unittest.TestCase):
    def problems(self, lines: list[str], *, timed_out: bool = False, too_large: bool = False) -> list[str]:
        return guard.telemetry_problems("\n".join(lines), timed_out=timed_out, output_too_large=too_large)

    def test_a_complete_stream_has_no_problems(self) -> None:
        self.assertEqual(self.problems(COMPLETE), [])

    def test_missing_cut_or_failed_streams_are_named(self) -> None:
        self.assertEqual(self.problems([]), ["no_events"])
        self.assertEqual(self.problems(COMPLETE[:5] + ['{"type":"turn.comp']), ["no_final_turn", "unparsed_line:6"])
        self.assertEqual(self.problems(COMPLETE[:3] + [line("turn.failed", error={})]),
                         ["no_final_turn", "turn_failed", "unfinished_item:item_1"])
        self.assertEqual(self.problems(COMPLETE[:5] + [line("approval.requested"), COMPLETE[5]]),
                         ["unknown_type:approval.requested"])
        self.assertEqual(self.problems(COMPLETE, timed_out=True, too_large=True), ["output_too_large", "timed_out"])

    def test_a_stream_without_its_start_is_not_complete(self) -> None:
        """Codex review: a lone final event read as a whole run."""
        self.assertEqual(self.problems(['{"type":"turn.completed"}']),
                         ["malformed_turn:1", "no_thread_start", "no_turn_start"])
        self.assertEqual(self.problems(COMPLETE[5:]), ["no_thread_start", "no_turn_start"])
        self.assertEqual(self.problems(COMPLETE[:1] + COMPLETE[2:]), ["no_turn_start"])

    def test_config_warnings_before_the_turn_are_allowed(self) -> None:
        """codex 0.162.1 (2026-10-10): two completed `error` items between thread.started and turn.started."""
        text = ("Codex is ignoring 1 unrecognized configuration setting. Check for typos or deprecated settings.\n"
                "  session-flags: `tools.image_gen` is ignored.")
        warning = line("item.completed", item={"id": "item_0", "type": "error", "message": text})
        self.assertEqual(self.problems(COMPLETE[:1] + [warning, warning] + COMPLETE[1:]), [])
        # Review of #1076: an ignored isolation switch must not pass as a harmless warning.
        for other in (text.replace("tools.image_gen", "web_search"),
                      text.replace("1 unrecognized configuration setting", "2 unrecognized configuration settings")
                      + "\n  session-flags: `sandbox_mode` is ignored."):
            unknown = line("item.completed", item={"id": "item_0", "type": "error", "message": other})
            self.assertEqual(self.problems(COMPLETE[:1] + [unknown] + COMPLETE[1:]), ["no_turn_start"])
        self.assertEqual(self.problems(COMPLETE[:1] + [line("item.started", item={"id": "item_0", "type": "error"})]
                                       + COMPLETE[1:]), ["no_turn_start", "unfinished_item:item_0"])
        message = line("item.completed", item={"id": "item_0", "type": "agent_message", "text": "early"})
        self.assertEqual(self.problems(COMPLETE[:1] + [message] + COMPLETE[1:]), ["no_turn_start"])

    def test_items_without_identity_are_not_complete(self) -> None:
        """Codex review: two empty items closed each other through the id 'None'."""
        self.assertEqual(self.problems([line("thread.started"), line("turn.started"), line("item.started", item={}),
                                        line("item.completed", item={}), '{"type":"turn.completed"}']),
                         ["malformed_item:3", "malformed_item:4", "malformed_turn:5"])

    def test_a_line_separator_inside_a_json_string_does_not_cut_the_event(self) -> None:
        """U+2028 is valid unescaped inside JSON text; only "\\n" ends a JSONL line (#1070 review)."""
        message = '{"type":"item.completed","item":{"id":"item_2","type":"agent_message","text":"a\u2028b\u2029c"}}'
        self.assertEqual(self.problems(COMPLETE[:4] + [message] + COMPLETE[5:]), [])
        self.assertEqual(guard.telemetry_problems("\r\n".join(COMPLETE) + "\r\n", timed_out=False,
                                                  output_too_large=False), [])

    def test_an_incomplete_stream_fails_the_run_and_a_clean_one_does_not(self) -> None:
        base = {"operatorEnvironmentChanged": [], "sandboxAccountsCreated": [], "operatorAclChanged": []}
        self.assertEqual(guard.violations({**base, "telemetry": {"problems": ["no_events"]}}, windows=True),
                         ["EXECUTOR_TELEMETRY_INCOMPLETE"])
        self.assertEqual(guard.violations({**base, "telemetry": {"problems": []}}, windows=True), [])

    def test_a_path_change_names_the_entries(self) -> None:
        before = r"C:\Tools;C:\Users\op\.dotnet\tools;C:\Python313\\"
        after = r"c:\tools\;C:\Python313;C:\Users\op\AppData\Local\Programs\npm-node24"
        self.assertEqual(guard.path_entry_changes(before, after),
                         {"added": [r"C:\Users\op\AppData\Local\Programs\npm-node24"],
                          "removed": [r"C:\Users\op\.dotnet\tools"]})
        self.assertEqual(guard.path_entry_changes(before, before), {"added": [], "removed": []})


if __name__ == "__main__":
    unittest.main()
