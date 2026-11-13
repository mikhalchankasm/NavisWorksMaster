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
# A capability SID the run's cap_sid did not name, as the third one on NH-i440 (#440, 0.167.2).
UNLISTED = "S-1-5-21-3909064243-768639762-3194373299-659136188"
STRANGER = "S-1-5-21-1519988131-586330619-1303720927-2602970109"
INHERITED = ("A;OICIID;FA;;;BA", "A;OICIID;FA;;;SY", "A;OICIID;0x1200a9;;;BU")


def root_ace(sid: str) -> str:
    return f"A;OICI;0x1301bf;;;{sid}"


def git_deny(sid: str) -> str:
    return f"D;;0x130156;;;{sid}"


class FakeAcl:
    """SetNamedSecurityInfoW and SetFileSecurityW as the module relies on them, for a root and its `.git`.

    Explicit ACEs are written; inherited ones are derived, `.git`'s from the root's object-inheritable
    ACEs. A propagating write leaves the AI marker, as the real call does; an exact write does not.
    STRANGER and OPERATOR are accounts; every other SID maps to none.
    """

    def __init__(self, explicit: tuple[str, ...] = (), inherited: tuple[str, ...] = INHERITED, control: str = ""):
        self.explicit, self.inherited, self.control = list(explicit), list(inherited), control
        self.git_explicit: list[str] = []
        self.git_control = ""
        self.writes = 0

    def operator_sid(self) -> str:
        return OPERATOR

    def resolves(self, sid: str) -> bool:
        return sid.upper() in {OPERATOR.upper(), STRANGER.upper()}

    def _root_aces(self) -> list[str]:
        return self.explicit + ([] if "P" in self.control.replace("AI", "") else self.inherited)

    def read(self, path: Path) -> str:
        if path.name != ".git":
            return "D:" + self.control + "".join(f"({ace})" for ace in self._root_aces())
        derived = []
        for ace in self._root_aces():
            kind, flags, rights, *_rest, sid = ace.split(";")
            if "OI" in {flags[index:index + 2] for index in range(0, len(flags), 2)}:  # a file inherits it
                derived.append(f"{kind};ID;{rights};;;{sid}")
        aces = self.git_explicit + ([] if "P" in self.git_control.replace("AI", "") else derived)
        return "D:" + self.git_control + "".join(f"({ace})" for ace in aces)

    def _store(self, path: Path, explicit: list[str], control: str) -> None:
        if path.name == ".git":
            self.git_explicit, self.git_control = explicit, control
        else:
            self.explicit, self.control = explicit, control

    def write(self, path: Path, sddl: str, *, protected: bool) -> None:
        self.writes += 1
        self._store(path, subject.parse_dacl(sddl).explicit(), "PAI" if protected else "AI")

    def write_exact(self, path: Path, sddl: str) -> None:
        self.writes += 1
        dacl = subject.parse_dacl(sddl)
        self._store(path, dacl.explicit(), dacl.control)


class GrantTests(unittest.TestCase):
    def setUp(self) -> None:
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.worktree = self.root / "Avox-wt"
        self.worktree.mkdir()
        (self.worktree / ".git").write_text("gitdir: elsewhere", encoding="utf-8")
        self.git = self.worktree / ".git"
        self.main = self.root / "Avox"
        self.main.mkdir()

    def grant(self, backend: FakeAcl) -> dict[str, object] | None:
        return subject.grant_worktree_access(self.worktree, self.main, self.worktree, backend)

    def kinds(self, result: dict[str, object]) -> list[tuple[str, str]]:
        return [(Path(str(entry["path"])).name, str(entry["kind"])) for entry in result["reverted"]]

    def test_the_grant_is_a_separate_ace_and_restore_returns_the_exact_dacl(self) -> None:
        """A partial right of the operator with the same flags must survive: icacls /grant would merge into it."""
        partial = f"A;OICI;0x1200a9;;;{OPERATOR}"
        backend = FakeAcl(explicit=(partial,))
        before, git_before = backend.read(self.worktree), backend.read(self.git)
        state = self.grant(backend)
        self.assertEqual(state["added"], root_ace(OPERATOR))
        self.assertEqual(backend.explicit, [partial, state["added"]])
        self.assertEqual((state["gitPath"], state["gitBefore"]), (str(self.git), git_before))
        result = subject.restore_worktree_access(state, set(), backend)
        self.assertTrue(result["restored"], result)
        self.assertEqual((result["missing"], result["unexpected"]), ([], []))
        self.assertEqual(self.kinds(result), [("Avox-wt", "grant")])
        # Byte for byte, AI marker included, on the root and on `.git` that inherited the grant.
        self.assertEqual((backend.read(self.worktree), backend.read(self.git)), (before, git_before))
        self.assertEqual((result["after"], result["gitAfter"]), (before, git_before))

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

    def test_what_codex_0_167_2_left_on_nh_i440_is_reverted_and_the_run_is_clean(self) -> None:
        """Three root allows and three `.git` denies, one SID missing from cap_sid (#440): all taken off."""
        backend = FakeAcl()
        before, git_before = backend.read(self.worktree), backend.read(self.git)
        state = self.grant(backend)
        backend.explicit = [root_ace(SANDBOX), root_ace(UNLISTED), *backend.explicit]
        backend.git_explicit = [git_deny(SANDBOX), git_deny(UNLISTED)]
        result = subject.restore_worktree_access(state, {SANDBOX}, backend)
        self.assertTrue(result["restored"], result)
        self.assertEqual(self.kinds(result), [("Avox-wt", "sandbox"), ("Avox-wt", "sandbox"), ("Avox-wt", "grant"),
                                              (".git", "sandbox"), (".git", "sandbox")])
        self.assertEqual([entry.get("listed") for entry in result["reverted"]], [True, False, None, True, False])
        self.assertEqual((backend.read(self.worktree), backend.read(self.git)), (before, git_before))

    def test_sandbox_aces_are_reverted_when_the_operator_needed_no_grant(self) -> None:
        backend = FakeAcl(inherited=(*INHERITED, f"A;OICIID;0x1301bf;;;{OPERATOR}"))
        before = backend.read(self.worktree)
        state = self.grant(backend)
        backend.explicit.append(root_ace(SANDBOX))
        result = subject.restore_worktree_access(state, {SANDBOX}, backend)
        self.assertTrue(result["restored"], result)
        self.assertEqual(backend.read(self.worktree), before)

    def test_an_ace_for_an_account_is_unexpected_and_still_reverted(self) -> None:
        backend = FakeAcl()
        before = backend.read(self.worktree)
        state = self.grant(backend)
        stranger = root_ace(STRANGER)
        backend.explicit.append(stranger)
        result = subject.restore_worktree_access(state, {SANDBOX}, backend)
        self.assertFalse(result["restored"])
        self.assertTrue(result["daclRestored"])
        self.assertEqual(result["unexpected"], [stranger])
        self.assertEqual(backend.read(self.worktree), before)

    def test_a_listed_sid_does_not_excuse_an_ace_for_an_account(self) -> None:
        """cap_sid only labels the report: a SID that maps to an account is never a sandbox SID."""
        backend = FakeAcl()
        state = self.grant(backend)
        backend.explicit.append(root_ace(STRANGER))
        self.assertEqual(subject.restore_worktree_access(state, {STRANGER}, backend)["unexpected"], [root_ace(STRANGER)])

    def test_a_sandbox_sid_with_other_rights_or_place_is_not_excused(self) -> None:
        backend = FakeAcl()
        state = self.grant(backend)
        wider, allow_on_git = f"A;OICI;FA;;;{SANDBOX}", f"A;;0x1301bf;;;{SANDBOX}"
        backend.explicit.append(wider)
        backend.git_explicit.append(allow_on_git)
        result = subject.restore_worktree_access(state, {SANDBOX}, backend)
        self.assertEqual(result["unexpected"], [wider, allow_on_git])
        self.assertTrue(result["daclRestored"])

    def test_a_lost_right_fails_the_restore_and_is_put_back(self) -> None:
        partial = f"A;OICI;0x1200a9;;;{OPERATOR}"
        backend = FakeAcl(explicit=(partial,))
        before = backend.read(self.worktree)
        state = self.grant(backend)
        backend.explicit.remove(partial)
        result = subject.restore_worktree_access(state, set(), backend)
        self.assertFalse(result["restored"])
        self.assertEqual(result["missing"], [partial])
        self.assertEqual(backend.read(self.worktree), before)

    def test_a_removed_grant_is_a_problem_not_a_silent_success(self) -> None:
        backend = FakeAcl()
        state = self.grant(backend)
        backend.explicit.clear()
        result = subject.restore_worktree_access(state, set(), backend)
        self.assertFalse(result["restored"])
        self.assertIn("no longer on the worktree", result["problem"])

    def test_a_protection_change_is_unexpected_and_undone(self) -> None:
        backend = FakeAcl()
        before = backend.read(self.worktree)
        state = self.grant(backend)
        backend.control = "PAI"
        result = subject.restore_worktree_access(state, set(), backend)
        self.assertFalse(result["restored"])
        self.assertEqual(backend.read(self.worktree), before)

    def test_a_failing_read_during_restore_is_reported(self) -> None:
        backend = FakeAcl()
        state = self.grant(backend)
        backend.read = lambda path: (_ for _ in ()).throw(PermissionError("denied"))
        result = subject.restore_worktree_access(state, set(), backend)
        self.assertFalse(result["restored"])
        self.assertFalse(result["daclRestored"])
        self.assertIn("PermissionError", result["problem"])

    def test_a_worktree_without_git_has_only_the_root_snapshotted(self) -> None:
        self.git.unlink()
        backend = FakeAcl()
        state = self.grant(backend)
        self.assertNotIn("gitPath", state)
        result = subject.restore_worktree_access(state, set(), backend)
        self.assertTrue(result["restored"], result)
        self.assertNotIn("gitAfter", result)

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
        # The 0.167.2 layout: four keys, the last two keyed by path.
        (home / "cap_sid").write_text(
            '{"workspace":"' + SANDBOX + '","readonly":"S-1-5-21-5-6-7-8","workspace_by_cwd":{"d:/x":"S-1-5-21-1-2-3-4"},'
            '"writable_root_by_path":{"d:/x":"S-1-5-21-9-10-11-12"}}', encoding="utf-8")
        self.assertEqual(subject.sandbox_sids(home),
                         {SANDBOX, "S-1-5-21-5-6-7-8", "S-1-5-21-1-2-3-4", "S-1-5-21-9-10-11-12"})


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
        self.assertEqual(state["added"], root_ace(admin))
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
        self.assertEqual(backend.read(root), before)
        self.assertNotIn(f";;;{backend.operator_sid()}", backend.read(root / "sub"))

    def test_a_real_restore_takes_off_what_the_sandbox_left_on_the_root_and_git(self) -> None:
        """The #440 shapes, written for SIDs no account has, come off the root, `.git` and every file below."""
        root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, root, True)
        (root / "src").mkdir()
        (root / "src" / "a.cs").write_text("x", encoding="utf-8")
        (root / ".git").write_text("gitdir: elsewhere", encoding="utf-8")
        backend = subject.WindowsAcl()
        before, git_before = backend.read(root), backend.read(root / ".git")
        state = subject.grant_worktree_access(root, root.parent / "elsewhere", root, backend)
        dacl = subject.parse_dacl(backend.read(root))
        backend.write(root, subject._build([root_ace(SANDBOX), root_ace(UNLISTED), *dacl.explicit()], dacl.protected),
                      protected=dacl.protected)
        git = subject.parse_dacl(backend.read(root / ".git"))
        backend.write(root / ".git", subject._build([git_deny(SANDBOX), git_deny(UNLISTED), *git.explicit()],
                                                    git.protected), protected=git.protected)
        result = subject.restore_worktree_access(state, {SANDBOX}, backend)
        self.assertTrue(result["restored"], result)
        self.assertEqual(len(result["reverted"]), 5)
        self.assertEqual((backend.read(root), backend.read(root / ".git")), (before, git_before))
        self.assertNotIn(SANDBOX, backend.read(root / "src" / "a.cs"))

    def test_a_capability_sid_maps_to_no_account_and_the_operator_does(self) -> None:
        backend = subject.WindowsAcl()
        self.assertFalse(backend.resolves(UNLISTED))
        self.assertTrue(backend.resolves(backend.operator_sid()))

    def test_la_stands_for_this_machines_rid_500_account(self) -> None:
        self.assertRegex(subject.WindowsAcl().alias_sid("LA") or "", r"^S-1-5-21-\d+-\d+-\d+-500$")


if __name__ == "__main__":
    unittest.main()
