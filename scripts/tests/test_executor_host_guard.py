from __future__ import annotations

import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import scripts.executor_host_guard as subject
from scripts.executor_host_guard import HostSnapshot

PROFILE = Path(r"C:\Users\operator") if os.name == "nt" else Path("/home/operator")
WORKTREE = Path(r"D:\GitHub\Avox-i429k") if os.name == "nt" else Path("/work/Avox-i429k")
RUNTIME = PROFILE / "AppData" / "Local" / "Temp" / "avox-executor-1"


def grant(access: str, path: Path | str, principal: str = "sandbox users") -> str:
    return f"2026-09-26 12:00:00 granting {access} ACE to {path} for {principal}"


class ProfileGrantTests(unittest.TestCase):
    """The lines that preceded the lost profile, and the ones a run may make."""

    def flagged(self, *lines: str) -> list[str]:
        return subject.profile_grants("\n".join(lines), PROFILE, [WORKTREE, RUNTIME])

    def test_the_incident_grants_are_flagged(self) -> None:
        found = self.flagged(
            grant("read", PROFILE / "AppData"),
            grant("read", PROFILE / "AppData" / "Roaming" / "Microsoft" / "Windows" / "Start Menu"),
            grant("read", PROFILE / ".claude.json"),
            grant("write", PROFILE / "AppData" / "Local" / "Temp", "sandbox group and capability SID"),
        )
        self.assertEqual(
            found,
            [
                f"read {PROFILE / 'AppData'}",
                f"read {PROFILE / 'AppData' / 'Roaming' / 'Microsoft' / 'Windows' / 'Start Menu'}",
                f"read {PROFILE / '.claude.json'}",
                f"write {PROFILE / 'AppData' / 'Local' / 'Temp'}",
            ],
        )

    def test_the_profile_itself_and_its_parents_are_flagged(self) -> None:
        self.assertEqual(self.flagged(grant("read", PROFILE)), [f"read {PROFILE}"])
        self.assertEqual(self.flagged(grant("read", PROFILE.parent)), [f"read {PROFILE.parent}"])

    def test_the_worktree_and_the_run_directory_are_the_run_s_own(self) -> None:
        self.assertEqual(
            self.flagged(
                grant("write", WORKTREE, "sandbox group and capability SID"),
                grant("write", RUNTIME / "codex-home", "sandbox group and capability SID"),
                grant("read", WORKTREE / "src"),
            ),
            [],
        )

    def test_a_sibling_whose_name_extends_an_allowed_root_is_not_allowed(self) -> None:
        sibling = RUNTIME.parent / (RUNTIME.name + "-other")
        self.assertEqual(self.flagged(grant("write", sibling)), [f"write {sibling}"])

    def test_a_repeated_grant_is_reported_once_and_other_lines_are_ignored(self) -> None:
        line = grant("read", PROFILE / "AppData")
        self.assertEqual(self.flagged(line, "applied deny ACE to protect D:\\GitHub\\Avox\\.git", line), [f"read {PROFILE / 'AppData'}"])


class SandboxLogEvidenceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.logs = Path(self.temp.name) / ".sandbox"

    def test_an_absent_log_directory_is_recorded_as_empty(self) -> None:
        evidence = subject.sandbox_log_evidence(self.logs, PROFILE, [WORKTREE])
        self.assertEqual(evidence["files"], [])
        self.assertEqual(evidence["profileGrants"], [])

    def test_only_the_tail_of_a_long_log_is_kept(self) -> None:
        self.logs.mkdir()
        lines = [f"line {index}" for index in range(subject.SANDBOX_LOG_TAIL_LINES + 50)]
        lines.append(grant("read", PROFILE / "AppData"))
        (self.logs / "sandbox.2026-09-26.log").write_text("\n".join(lines), encoding="utf-8")
        evidence = subject.sandbox_log_evidence(self.logs, PROFILE, [WORKTREE])
        tail = evidence["files"][0]["tail"]
        self.assertEqual(len(tail.splitlines()), subject.SANDBOX_LOG_TAIL_LINES)
        self.assertNotIn("line 0\n", tail)
        self.assertEqual(evidence["profileGrants"], [f"read {PROFILE / 'AppData'}"])

    def test_a_grant_before_the_recorded_tail_is_still_found(self) -> None:
        self.logs.mkdir()
        lines = [grant("read", PROFILE / "AppData")]
        lines += [f"line {index}" for index in range(subject.SANDBOX_LOG_TAIL_LINES + 50)]
        (self.logs / "sandbox.2026-09-26.log").write_text("\n".join(lines), encoding="utf-8")
        evidence = subject.sandbox_log_evidence(self.logs, PROFILE, [WORKTREE])
        self.assertNotIn("granting", evidence["files"][0]["tail"])
        self.assertEqual(evidence["profileGrants"], [f"read {PROFILE / 'AppData'}"])


class SnapshotComparisonTests(unittest.TestCase):
    def test_changed_added_and_removed_variables_are_named(self) -> None:
        before = HostSnapshot(environment={"PATH": "a", "GONE": "x", "SAME": "s"}, sandbox_accounts=frozenset())
        after = HostSnapshot(environment={"PATH": "b", "NEW": "y", "SAME": "s"}, sandbox_accounts=frozenset())
        self.assertEqual(subject.changed_environment_names(before, after), ["GONE", "NEW", "PATH"])

    def test_an_unreadable_snapshot_is_unknown_not_clean(self) -> None:
        known = HostSnapshot(environment={}, sandbox_accounts=frozenset())
        unknown = HostSnapshot(environment=None, sandbox_accounts=None)
        self.assertIsNone(subject.changed_environment_names(known, unknown))
        self.assertIsNone(subject.created_sandbox_accounts(unknown, known))

    def test_a_folder_whose_security_descriptor_moved_is_named(self) -> None:
        """The unelevated sandbox logs no grant; the descriptor is the only witness."""
        before = HostSnapshot(environment={}, sandbox_accounts=frozenset(), acls={"C:/p": "a", "C:/p/Temp": "t"})
        after = HostSnapshot(environment={}, sandbox_accounts=frozenset(), acls={"C:/p": "a", "C:/p/Temp": "t2"})
        self.assertEqual(subject.changed_acl_paths(before, after), ["C:/p/Temp"])
        self.assertIsNone(subject.changed_acl_paths(before, HostSnapshot(environment={}, sandbox_accounts=None)))

    def test_only_accounts_that_appeared_are_reported(self) -> None:
        before = HostSnapshot(environment={}, sandbox_accounts=frozenset({"CodexSandboxOnline"}))
        after = HostSnapshot(environment={}, sandbox_accounts=frozenset({"CodexSandboxOnline", "CodexSandboxOffline"}))
        self.assertEqual(subject.created_sandbox_accounts(before, after), ["CodexSandboxOffline"])

    def test_account_names_are_matched_in_localized_net_user_output(self) -> None:
        output = "Учетные записи пользователей для \\\\HOST\n---\nAdministrator  CodexSandboxOffline  mikha\nКоманда выполнена успешно.\n"
        completed = mock.Mock(returncode=0, stdout=output)
        with mock.patch.object(subject.os, "name", "nt"), mock.patch.object(subject.subprocess, "run", return_value=completed):
            self.assertEqual(subject._sandbox_accounts(), frozenset({"CodexSandboxOffline"}))

    def test_violations_are_ordered_most_specific_first(self) -> None:
        host = {
            "operatorEnvironmentChanged": ["PATH"],
            "sandboxAccountsCreated": ["CodexSandboxOffline"],
            "operatorAclChanged": ["C:/p/Temp"],
            "sandboxLog": {"profileGrants": ["read C:\\Users\\operator\\AppData"]},
        }
        self.assertEqual(
            subject.violations(host),
            ["SANDBOX_PROFILE_GRANT", "SANDBOX_ACCOUNTS_CREATED", "OPERATOR_ACL_MUTATED", "OPERATOR_ENV_MUTATED"],
        )
        clean = {"operatorEnvironmentChanged": [], "sandboxAccountsCreated": [], "operatorAclChanged": []}
        self.assertEqual(subject.violations(clean, windows=True), [])

    def test_an_unavailable_snapshot_fails_closed_on_windows_only(self) -> None:
        """An unknown is not a clean result where the probe should have worked."""
        host = {"operatorEnvironmentChanged": [], "sandboxAccountsCreated": None, "operatorAclChanged": []}
        self.assertEqual(subject.violations(host, windows=True), ["HOST_SNAPSHOT_UNAVAILABLE"])
        self.assertEqual(subject.unavailable_snapshots(host), ["sandboxAccountsCreated"])
        self.assertEqual(subject.violations(host, windows=False), [])

    @unittest.skipUnless(os.name == "nt", "the user environment lives in the Windows registry")
    def test_the_real_user_environment_is_readable_and_holds_path(self) -> None:
        digests = subject._user_environment_digests()
        self.assertIsNotNone(digests)
        self.assertIn("PATH", digests)
        self.assertTrue(all(len(value) == 64 for value in digests.values()))


    @unittest.skipUnless(os.name == "nt", "security descriptors are read with Get-Acl")
    def test_the_real_profile_folders_have_descriptors_and_a_missing_one_does_not_sink_the_rest(self) -> None:
        """A CI runner lacks some profile folders; that must not blank the snapshot."""
        with tempfile.TemporaryDirectory() as present:
            missing = str(Path(present) / "absent")
            paths = [*subject.watched_profile_directories(), present, missing]
            digests = subject._profile_acl_digests(paths)
        self.assertIsNotNone(digests)
        self.assertEqual(sorted(digests), sorted(set(paths)))
        self.assertNotEqual(digests[present], digests[missing])


if __name__ == "__main__":
    unittest.main()
