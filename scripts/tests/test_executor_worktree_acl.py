from __future__ import annotations

import os
import shutil
import tempfile
import unittest
from pathlib import Path

import scripts.executor_worktree_acl as subject
from scripts.executor_primitives import ExecutorError

OPERATOR = "S-1-5-21-961352941-67178570-2462278036-1381"
SANDBOX = "S-1-5-21-3869428235-2351914406-3079185235-1110378894"
STRANGER = "S-1-5-21-1519988131-586330619-1303720927-2602970109"
INHERITED = ("A;OICIID;FA;;;BA", "A;OICIID;FA;;;SY", "A;OICIID;0x1200a9;;;BU")


class FakeAcl:
    """SetNamedSecurityInfoW as the module relies on it: explicit ACEs are written, inherited ones derived."""

    def __init__(self, explicit: tuple[str, ...] = (), inherited: tuple[str, ...] = INHERITED, control: str = "AI"):
        self.explicit, self.inherited, self.control = list(explicit), list(inherited), control
        self.writes = 0

    def operator_sid(self) -> str:
        return OPERATOR

    def read(self, path: Path) -> str:
        aces = self.explicit + ([] if "P" in self.control.replace("AI", "") else self.inherited)
        return "D:" + self.control + "".join(f"({ace})" for ace in aces)

    def write(self, path: Path, sddl: str, *, protected: bool) -> None:
        self.writes += 1
        self.explicit = [ace for ace in subject.parse_dacl(sddl).aces if "ID" not in ace.split(";")[1]]
        self.control = "PAI" if protected else "AI"


class GrantTests(unittest.TestCase):
    def setUp(self) -> None:
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.worktree = self.root / "Avox-wt"
        self.worktree.mkdir()
        self.main = self.root / "Avox"
        self.main.mkdir()

    def grant(self, backend: FakeAcl) -> dict[str, object] | None:
        return subject.grant_worktree_access(self.worktree, self.main, self.worktree, backend)

    def test_the_grant_is_a_separate_ace_and_restore_returns_the_exact_dacl(self) -> None:
        """A partial right of the operator with the same flags must survive: icacls /grant would merge into it."""
        partial = f"A;OICI;0x1200a9;;;{OPERATOR}"
        backend = FakeAcl(explicit=(partial,))
        before = backend.read(self.worktree)
        state = self.grant(backend)
        self.assertEqual(state["added"], f"A;OICI;0x1301bf;;;{OPERATOR}")
        self.assertEqual(backend.explicit, [partial, state["added"]])
        result = subject.restore_worktree_access(state, set(), backend)
        self.assertTrue(result["restored"])
        self.assertEqual((result["missing"], result["unexpected"]), ([], []))
        self.assertEqual(backend.read(self.worktree), before)
        self.assertEqual(result["before"], before)

    def test_an_existing_modify_right_is_left_alone(self) -> None:
        backend = FakeAcl(inherited=(*INHERITED, f"A;OICIID;0x1301bf;;;{OPERATOR}"))
        state = self.grant(backend)
        self.assertIsNone(state["added"])
        self.assertEqual(backend.writes, 0)
        self.assertTrue(subject.restore_worktree_access(state, set(), backend)["restored"])
        self.assertEqual(backend.writes, 0)

    def test_a_partial_inherited_right_is_not_mistaken_for_the_grant(self) -> None:
        backend = FakeAcl(inherited=(*INHERITED, f"A;OICIID;0x1200a9;;;{OPERATOR}"))
        self.assertIsNotNone(self.grant(backend)["added"])

    def test_this_runs_sandbox_aces_are_the_only_ones_allowed_to_appear(self) -> None:
        backend = FakeAcl()
        state = self.grant(backend)
        backend.explicit.append(f"A;OICI;0x1301bf;;;{SANDBOX}")
        result = subject.restore_worktree_access(state, {SANDBOX}, backend)
        self.assertTrue(result["restored"])

    def test_an_ace_for_an_unknown_sid_fails_the_restore_and_is_named(self) -> None:
        backend = FakeAcl()
        state = self.grant(backend)
        stranger = f"A;OICI;0x1301bf;;;{STRANGER}"
        backend.explicit.append(stranger)
        result = subject.restore_worktree_access(state, {SANDBOX}, backend)
        self.assertFalse(result["restored"])
        self.assertEqual(result["unexpected"], [stranger])

    def test_a_sandbox_sid_with_other_rights_is_not_excused(self) -> None:
        backend = FakeAcl()
        state = self.grant(backend)
        wider = f"A;OICI;FA;;;{SANDBOX}"
        backend.explicit.append(wider)
        self.assertEqual(subject.restore_worktree_access(state, {SANDBOX}, backend)["unexpected"], [wider])

    def test_a_lost_right_fails_the_restore_and_is_named(self) -> None:
        partial = f"A;OICI;0x1200a9;;;{OPERATOR}"
        backend = FakeAcl(explicit=(partial,))
        state = self.grant(backend)
        backend.explicit.remove(partial)
        result = subject.restore_worktree_access(state, set(), backend)
        self.assertFalse(result["restored"])
        self.assertEqual(result["missing"], [partial])

    def test_a_removed_grant_is_a_problem_not_a_silent_success(self) -> None:
        backend = FakeAcl()
        state = self.grant(backend)
        backend.explicit.clear()
        result = subject.restore_worktree_access(state, set(), backend)
        self.assertFalse(result["restored"])
        self.assertIn("no longer on the worktree", result["problem"])

    def test_a_protection_change_is_unexpected(self) -> None:
        backend = FakeAcl()
        state = self.grant(backend)
        backend.control = "PAI"
        self.assertFalse(subject.restore_worktree_access(state, set(), backend)["restored"])

    def test_a_failing_read_during_restore_is_reported(self) -> None:
        backend = FakeAcl()
        state = self.grant(backend)
        backend.read = lambda path: (_ for _ in ()).throw(PermissionError("denied"))
        result = subject.restore_worktree_access(state, set(), backend)
        self.assertFalse(result["restored"])
        self.assertIn("PermissionError", result["problem"])

    def test_only_the_worktree_root_is_ever_granted(self) -> None:
        backend = FakeAcl()
        with self.assertRaises(ExecutorError):
            subject.grant_worktree_access(self.main, self.main, self.main, backend)
        with self.assertRaises(ExecutorError):
            subject.grant_worktree_access(self.root, self.main, self.root, backend)
        inner = self.worktree / "src"
        inner.mkdir()
        with self.assertRaises(ExecutorError):
            subject.grant_worktree_access(inner, self.main, self.worktree, backend)
        self.assertEqual(backend.writes, 0)

    def test_the_toplevel_query_runs_only_where_a_grant_is_made(self) -> None:
        """A run whose worktree git cannot read must not fail on a platform that grants nothing."""
        calls: list[str] = []

        def toplevel() -> Path:
            calls.append("git")
            return self.worktree

        subject.grant_worktree_access(self.worktree, self.main, toplevel, FakeAcl())
        self.assertEqual(calls, ["git"])
        if os.name != "nt":
            self.assertIsNone(subject.grant_worktree_access(self.worktree, self.main, toplevel))
            self.assertEqual(calls, ["git"])

    def test_no_grant_off_windows(self) -> None:
        if os.name == "nt":
            self.skipTest("the default backend is the real one on Windows")
        self.assertIsNone(subject.grant_worktree_access(self.worktree, self.main, self.worktree))

    def test_sandbox_sids_come_from_this_runs_cap_sid_file(self) -> None:
        home = self.root / "codex-home"
        home.mkdir()
        self.assertEqual(subject.sandbox_sids(home), set())
        (home / "cap_sid").write_text(
            '{"workspace":"' + SANDBOX + '","workspace_by_cwd":{"d:/x":"S-1-5-21-1-2-3-4"}}', encoding="utf-8")
        self.assertEqual(subject.sandbox_sids(home), {SANDBOX, "S-1-5-21-1-2-3-4"})


class SidAliasTests(unittest.TestCase):
    def test_only_the_sid_field_of_the_alias_is_expanded(self) -> None:
        admin = "S-1-5-21-4239583824-265399229-1323058055-500"
        sddl = "D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1301bf;;;la)(A;OICIID;FA;;;LG)"
        self.assertEqual(subject.expand_sid_alias(sddl, "LA", admin),
                         f"D:PAI(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1301bf;;;{admin})(A;OICIID;FA;;;LG)")

    def test_a_restore_finds_the_grant_read_back_under_the_alias(self) -> None:
        """Hosted CI runs as the built-in Administrator: the token says its SID, the DACL says LA."""
        admin = "S-1-5-21-4239583824-265399229-1323058055-500"

        class AliasAcl(FakeAcl):
            def operator_sid(self) -> str:
                return admin

            def read(self, path: Path) -> str:
                return subject.expand_sid_alias(super().read(path).replace(admin, "LA"), "LA", admin)

        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        worktree = Path(temp.name) / "Avox-wt"
        worktree.mkdir()
        backend = AliasAcl()
        state = subject.grant_worktree_access(worktree, Path(temp.name) / "Avox", worktree, backend)
        self.assertEqual(state["added"], f"A;OICI;0x1301bf;;;{admin}")
        self.assertTrue(subject.restore_worktree_access(state, set(), backend)["restored"])


@unittest.skipUnless(os.name == "nt", "a real DACL")
class WindowsAclTests(unittest.TestCase):
    def test_a_real_grant_and_restore_leave_the_folder_as_it_was(self) -> None:
        root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, root, True)
        (root / "sub").mkdir()
        backend = subject.WindowsAcl()
        before = backend.read(root)
        state = subject.grant_worktree_access(root, root.parent / "elsewhere", root, backend)
        self.assertIn(state["added"], backend.read(root))
        self.assertIn(f"0x1301bf;;;{backend.operator_sid()}", backend.read(root / "sub"))
        result = subject.restore_worktree_access(state, set(), backend)
        self.assertTrue(result["restored"], result)
        after = subject.parse_dacl(backend.read(root))
        self.assertEqual(after.aces, subject.parse_dacl(before).aces)
        self.assertEqual(after.protected, subject.parse_dacl(before).protected)
        self.assertNotIn(f";;;{backend.operator_sid()}", backend.read(root / "sub"))

    def test_la_stands_for_this_machines_rid_500_account(self) -> None:
        self.assertRegex(subject.WindowsAcl().alias_sid("LA") or "", r"^S-1-5-21-\d+-\d+-\d+-500$")


if __name__ == "__main__":
    unittest.main()
