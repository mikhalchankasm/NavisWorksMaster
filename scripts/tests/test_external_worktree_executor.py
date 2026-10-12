from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import scripts.external_worktree_executor as subject
from scripts.agent_home_isolation import IsolatedHome
from scripts.executor_host_guard import HostSnapshot
from scripts.executor_primitives import ExecutorError, ProcessResult


class RunTests(unittest.TestCase):
    """The run path with the provider process replaced, so nothing is launched."""

    ACCESS = "sk-live-access-0123456789abcdef"
    REFRESH = "refresh-0123456789abcdefghij"
    API_KEY = "75696e8a159b4b24ba34a296c84417a1"

    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        # The real snapshot reads the operator's registry and local accounts;
        # a unit test must neither depend on them nor race a real change.
        quiet = HostSnapshot(environment={"PATH": "same"}, sandbox_accounts=frozenset(), acls={})
        self.snapshots = mock.patch.object(subject, "take_snapshot", return_value=quiet)
        self.snapshot = self.snapshots.start()
        self.addCleanup(self.snapshots.stop)
        # The real cleanup lists and stops processes on the operator's machine.
        self.msbuild = mock.patch.object(
            subject, "clean_orphan_msbuild_nodes", return_value={"found": 3, "terminated": 3}
        )
        self.cleaner = self.msbuild.start()
        self.addCleanup(self.msbuild.stop)
        self.brief = self.root / "brief.md"
        self.brief.write_text("## Outcome\nMove one file.\n", encoding="utf-8")
        self.worktree = self.root / "wt"
        self.worktree.mkdir()
        self.owner = self.root / "owner"
        self.owner.mkdir()
        home_path = self.root / "home"
        (home_path / "credentials").mkdir(parents=True)
        (home_path / "credentials" / "kimi-code.json").write_text(
            '{"access_token": "' + self.ACCESS + '", "refresh_token": "' + self.REFRESH + '", "token_type": "Bearer"}',
            encoding="utf-8",
        )
        self.home = IsolatedHome(
            provider="kimi",
            path=home_path,
            operator=self.root / "operator-home",
            env={"KIMI_CODE_HOME": str(home_path), "ZAI_API_KEY": self.API_KEY},
            inventory=(),
        )

    def contained_mkdtemp(self, created: list[Path] | None = None):
        """Keep a run that mocks the remover from leaving a directory in TEMP.

        Both tests below mock _remove_runtime on purpose, so the real directory
        would survive the test - and in a real run it would hold a credential.
        """
        holder = self.root / "temproots"
        holder.mkdir(exist_ok=True)
        # subject.tempfile is the tempfile module itself, so the patch is global
        # and the replacement must call the original rather than itself.
        original = tempfile.mkdtemp
        def make_runtime(**_kwargs: object) -> str:
            runtime = Path(original(dir=holder))
            if created is not None:
                created.append(runtime)
            return str(runtime)

        return mock.patch.object(subject.tempfile, "mkdtemp", side_effect=make_runtime)

    def execute(self, process: ProcessResult | BaseException, fingerprints: list[str]) -> dict[str, object]:
        with (
            mock.patch.object(subject, "resolve_worktree", return_value=self.worktree),
            mock.patch.object(subject, "main_checkout", return_value=self.owner),
            mock.patch.object(subject, "fingerprint", side_effect=fingerprints),
            mock.patch.object(subject, "_resolve_executable", return_value=Path("kimi")),
            mock.patch.object(subject, "prepare_isolated_home", return_value=self.home),
            mock.patch.object(
                subject, "_run_process",
                **({"side_effect": process} if isinstance(process, BaseException) else {"return_value": process}),
            ) as self.runner,
            mock.patch.object(subject, "persist_rotated_credentials", return_value=[]) as self.carrier,
        ):
            return subject.run("kimi", self.worktree, self.brief, runtime_root=self.root)

    def execute_advisor(
        self, process: ProcessResult, *, implicit_runtime: bool = False
    ) -> dict[str, object]:
        with (
            mock.patch.object(subject, "resolve_worktree", return_value=self.worktree),
            mock.patch.object(subject, "main_checkout", return_value=self.owner),
            mock.patch.object(subject, "worktree_head", return_value="baseline"),
            mock.patch.object(subject, "fingerprint", side_effect=["a", "a"]),
            mock.patch.object(subject, "_resolve_executable", return_value=Path("codex")),
            mock.patch.object(subject, "prepare_isolated_home", return_value=self.home),
            mock.patch.object(subject, "build_argv", return_value=(["codex"], None)),
            mock.patch.object(subject, "check_codex_isolation", return_value=None) as self.isolation_check,
            mock.patch.object(subject, "_run_process", return_value=process),
            mock.patch.object(subject, "persist_rotated_credentials", return_value=[]),
            mock.patch.object(subject, "restore_disposable_worktree", return_value={"worktreeRestored": True}),
        ):
            kwargs = {} if implicit_runtime else {"runtime_root": self.root}
            return subject.run("codex-plan", self.worktree, self.brief, **kwargs)

    FEATURES_CLEAN = chr(10).join(
        [f"{name}   stable   false" for name in (
            "apps", "browser_use", "browser_use_external", "browser_use_full_cdp_access", "computer_use", "plugins",
            "remote_plugin", "plugin_sharing", "in_app_browser", "image_generation", "skill_mcp_dependency_install",
            "tool_suggest", "browser_annotation_api")]
        + ["in_app_chat   stable   true"]
    )

    VERSION_OK = ProcessResult(0, "codex-cli 0.158.0", "", 0.1)

    def check(self, process: ProcessResult, version: ProcessResult | None = None) -> mock.MagicMock:
        with mock.patch.object(subject, "_run_process", side_effect=[version or self.VERSION_OK, process]) as runner:
            subject.check_codex_isolation(Path("codex.js"), {"CODEX_HOME": "h"}, self.worktree, self.root)
        return runner

    def test_an_unreviewed_codex_version_refuses_the_run(self) -> None:
        # 0.163.0 stands for any minor not yet reviewed; a pre-release or a foreign line of a reviewed minor is not a
        # plain patch release, so it needs the review too.
        for version in (ProcessResult(0, "codex-cli 0.163.0", "", 0.1), ProcessResult(1, "", "boom", 0.1),
                        ProcessResult(0, "codex-cli 0.159.3-alpha.1", "", 0.1),
                        ProcessResult(0, "codex-cli 0.15.9", "", 0.1), ProcessResult(0, "codex 0.159.3", "", 0.1),
                        ProcessResult(0, "codex-cli 1.159.0", "", 0.1)):
            with self.subTest(version=version.stdout), self.assertRaises(ExecutorError) as raised:
                self.check(ProcessResult(0, self.FEATURES_CLEAN, "", 1.0), version)
            self.assertEqual(raised.exception.category, "CONFIG_INVALID")
            self.assertIn("not a reviewed executor version", str(raised.exception))

    def test_every_reviewed_codex_version_passes_the_version_check(self) -> None:
        for text in ("codex-cli 0.158.0", "codex-cli 0.159.0", "codex-cli 0.159.2", "codex-cli 0.160.0", "codex-cli 0.161.0", "codex-cli 0.162.1"):
            with self.subTest(version=text):
                self.check(ProcessResult(0, self.FEATURES_CLEAN, "", 1.0), ProcessResult(0, text, "", 0.1))

    def test_a_patch_release_of_a_reviewed_minor_passes_the_version_check(self) -> None:
        # Owner, 2026-09-30: 0.159.0 is reviewed, so 0.159.7 runs once the feature check is clean.
        self.check(ProcessResult(0, self.FEATURES_CLEAN, "", 1.0), ProcessResult(0, "codex-cli 0.159.7", "", 0.1))

    def test_a_patch_older_than_the_reviewed_one_is_not_admitted(self) -> None:
        # #1076 review: 0.162.1 is reviewed, so 0.162.4 runs but the older 0.162.0 needs its own review.
        self.check(ProcessResult(0, self.FEATURES_CLEAN, "", 1.0), ProcessResult(0, "codex-cli 0.162.4", "", 0.1))
        with self.assertRaises(ExecutorError) as raised:
            self.check(ProcessResult(0, self.FEATURES_CLEAN, "", 1.0), ProcessResult(0, "codex-cli 0.162.0", "", 0.1))
        self.assertIn("not a reviewed executor version", str(raised.exception))

    def test_a_patch_release_still_needs_a_clean_feature_check(self) -> None:
        unsafe = self.FEATURES_CLEAN.replace("browser_use   stable   false", "browser_use   stable   true")
        with self.assertRaises(ExecutorError) as raised:
            self.check(ProcessResult(0, unsafe, "", 1.0), ProcessResult(0, "codex-cli 0.159.7", "", 0.1))
        self.assertEqual(raised.exception.category, "SANDBOX_UNSAFE")

    def test_the_codex_isolation_check_lists_features_with_the_runs_own_switches(self) -> None:
        runner = self.check(ProcessResult(0, self.FEATURES_CLEAN, "", 1.0))
        argv = runner.call_args_list[1].args[0]
        self.assertIn("--version", runner.call_args_list[0].args[0])
        self.assertIn("features", argv)
        self.assertIn("list", argv)
        self.assertIn('web_search="disabled"', argv)
        self.assertIn("features.apps=false", argv)
        self.assertEqual(runner.call_args_list[1].kwargs["env"], {"CODEX_HOME": "h"})

    def test_an_account_feature_left_on_refuses_the_run(self) -> None:
        with self.assertRaises(ExecutorError) as raised:
            self.check(ProcessResult(0, self.FEATURES_CLEAN.replace("apps   stable   false", "apps   stable   true", 1), "", 1.0))
        self.assertEqual(raised.exception.category, "SANDBOX_UNSAFE")
        self.assertIn("apps", str(raised.exception))

    def test_an_unverifiable_feature_listing_refuses_the_run(self) -> None:
        for process in (ProcessResult(1, self.FEATURES_CLEAN, "boom", 1.0), ProcessResult(0, "nothing", "", 1.0)):
            with self.subTest(process=process), self.assertRaises(ExecutorError) as raised:
                self.check(process)
            self.assertEqual(raised.exception.category, "CONFIG_INVALID")

    def test_the_advisor_run_proves_its_isolation_before_the_provider_starts(self) -> None:
        self.execute_advisor(ProcessResult(return_code=0, stdout="done", stderr="", duration_seconds=1.0))
        self.isolation_check.assert_called_once()

    def test_an_unknown_provider_is_refused_before_anything_is_built(self) -> None:
        provider = "not-an-executor-provider"
        self.assertNotIn(provider, subject.EXECUTOR_PROVIDERS)
        with self.assertRaises(ExecutorError) as raised:
            subject.run(provider, self.worktree, self.brief)
        self.assertEqual(raised.exception.category, "CONFIG_INVALID")

    def test_an_out_of_range_timeout_is_refused(self) -> None:
        with self.assertRaises(ExecutorError) as raised:
            subject.run("kimi", self.worktree, self.brief, timeout_seconds=99_999)
        self.assertEqual(raised.exception.category, "CONFIG_INVALID")

    def test_the_record_names_the_isolated_home_and_carries_no_credential(self) -> None:
        record = self.execute(
            ProcessResult(return_code=0, stdout="done nothing", stderr="", duration_seconds=1.0), ["a", "a"]
        )
        self.assertEqual(record["home"]["isolatedHome"], str(self.home.path))
        self.assertEqual(record["exitCode"], 0)
        self.assertEqual(record["stdoutTail"], "done nothing")
        self.assertNotIn(self.API_KEY, json.dumps(record))
        self.assertEqual(self.runner.call_args.kwargs["cwd"], self.worktree)
        self.assertEqual(self.runner.call_args.kwargs["spool_dir"], self.root)
        self.assertEqual(self.runner.call_args.kwargs["max_output_bytes"], 8_000_000)
        self.assertEqual(self.runner.call_args.kwargs["env"]["KIMI_CODE_HOME"], str(self.home.path))

    def test_the_record_counts_the_orphaned_msbuild_nodes_the_run_left(self) -> None:
        record = self.execute(ProcessResult(return_code=0, stdout="", stderr="", duration_seconds=1.0), ["a", "a"])
        self.assertEqual(record["msbuildOrphanNodes"], {"found": 3, "terminated": 3})
        started_ms, finished_ms, marker = self.cleaner.call_args.args
        self.assertLessEqual(started_ms, finished_ms)
        # The marker the child inherited is the run's own id, so only its nodes can match.
        self.assertEqual(marker, record["runId"])
        self.assertEqual(self.runner.call_args.kwargs["env"][subject.RUN_MARKER], record["runId"])

    def test_an_interrupted_run_still_cleans_its_msbuild_orphans(self) -> None:
        with self.assertRaises(KeyboardInterrupt):
            self.execute(KeyboardInterrupt(), ["a", "a"])
        self.cleaner.assert_called_once()

    def test_a_cleanup_that_raises_does_not_replace_the_record(self) -> None:
        self.cleaner.side_effect = RuntimeError("boom")
        record = self.execute(ProcessResult(return_code=0, stdout="", stderr="", duration_seconds=1.0), ["a", "a"])
        self.assertEqual(record["msbuildOrphanNodes"], {"found": None, "terminated": 0, "problem": "boom"})
        self.assertEqual(record["exitCode"], 0)

    def test_a_failed_msbuild_cleanup_is_recorded_without_failing_the_run(self) -> None:
        self.cleaner.return_value = {"found": None, "terminated": 0, "problem": "query failed"}
        record = self.execute(ProcessResult(return_code=0, stdout="", stderr="", duration_seconds=1.0), ["a", "a"])
        self.assertEqual(record["msbuildOrphanNodes"]["problem"], "query failed")
        self.assertNotIn("failure", record)

    def test_a_rotated_credential_is_carried_back_before_the_home_is_destroyed(self) -> None:
        """Otherwise the run consumes the operator's refresh token and keeps the replacement."""
        record = self.execute(ProcessResult(return_code=0, stdout="ok", stderr="", duration_seconds=1.0), ["a", "a"])
        self.carrier.assert_called_once_with("kimi", self.home.path, mock.ANY, operator=self.home.operator)
        self.assertEqual(record["credentialsCarriedBack"], [])

    def test_a_credential_the_provider_echoed_never_reaches_the_record(self) -> None:
        """_redact knows patterns; it does not know the tokens this launcher copied."""
        leak = 'config: {"accessToken":"' + self.ACCESS + '","refresh_token":"' + self.REFRESH + '"}'
        record = self.execute(ProcessResult(return_code=0, stdout=leak, stderr=leak, duration_seconds=1.0), ["a", "a"])
        blob = json.dumps(record)
        self.assertNotIn(self.ACCESS, blob)
        self.assertNotIn(self.REFRESH, blob)
        self.assertIn("[REDACTED]", record["stdoutTail"])
        self.assertIn("[REDACTED]", record["stderr"])

    def test_a_token_minted_during_the_run_is_redacted_too(self) -> None:
        rotated = "sk-live-rotated-fedcba9876543210"

        def rotate(*_args: object, **_kwargs: object) -> list[str]:
            (self.home.path / "credentials" / "kimi-code.json").write_text(
                '{"access_token": "' + rotated + '"}', encoding="utf-8"
            )
            return ["credentials/kimi-code.json"]

        with mock.patch.object(subject, "persist_rotated_credentials", side_effect=rotate):
            record = self.execute(
                ProcessResult(return_code=0, stdout=f"token={rotated}", stderr="", duration_seconds=1.0), ["a", "a"]
            )
        self.assertNotIn(rotated, json.dumps(record))

    def test_the_record_keeps_the_end_of_a_long_stream_not_its_opening(self) -> None:
        """A JSONL event stream opens with a preamble; what matters is how it ended."""
        stream = "x" * 6000 + " FINAL-MESSAGE"
        record = self.execute(ProcessResult(0, stream, "", 1.0), ["a", "a"])
        self.assertTrue(record["stdoutTail"].endswith("FINAL-MESSAGE"))
        self.assertEqual(len(record["stdoutTail"]), subject.MAX_STREAM_TAIL)

    def test_an_oversized_advisor_answer_is_preserved_but_an_executor_still_keeps_only_its_tail(self) -> None:
        stream = "A" + "x" * subject.MAX_STREAM_TAIL

        advisor = self.execute_advisor(ProcessResult(0, stream, "", 1.0))
        self.assertIn("stdoutPath", advisor)
        self.assertEqual(Path(str(advisor["stdoutPath"])).read_text(encoding="utf-8"), stream)
        self.assertEqual(advisor["stdoutBytes"], len(stream.encode("utf-8")))
        self.assertTrue(advisor["stdoutTruncated"])

        executor = self.execute(ProcessResult(0, stream, "", 1.0), ["a", "a"])
        self.assertEqual(executor["stdoutTail"], stream[-subject.MAX_STREAM_TAIL :])
        self.assertTrue(executor["stdoutTruncated"])
        self.assertNotIn("stdoutPath", executor)
        self.assertNotIn("stdoutBytes", executor)

    def test_an_oversized_advisor_answer_survives_successful_runtime_cleanup(self) -> None:
        stream = "A" + "x" * subject.MAX_STREAM_TAIL
        runtimes: list[Path] = []

        with self.contained_mkdtemp(runtimes):
            record = self.execute_advisor(ProcessResult(0, stream, "", 1.0), implicit_runtime=True)

        runtime = runtimes[0]
        stdout_path = Path(str(record["stdoutPath"]))
        self.assertTrue(stdout_path.exists(), f"missing sidecar: {stdout_path}")
        self.assertEqual(stdout_path.read_text(encoding="utf-8"), stream)
        self.assertEqual(record["runtimeDirectoryRemoved"], not runtime.exists())
        self.assertIs(record["runtimeDirectoryRemoved"], True)

    def test_an_oversized_advisor_answer_survives_failed_runtime_cleanup(self) -> None:
        stream = "A" + "x" * subject.MAX_STREAM_TAIL
        runtimes: list[Path] = []

        def fail_after_partial_cleanup(runtime: Path) -> tuple[bool, str]:
            for sidecar in runtime.glob("*.stdout.txt"):
                sidecar.unlink()
            return False, "locked"

        with (
            mock.patch.object(subject, "_remove_runtime", side_effect=fail_after_partial_cleanup),
            self.contained_mkdtemp(runtimes),
        ):
            record = self.execute_advisor(ProcessResult(0, stream, "", 1.0), implicit_runtime=True)

        runtime = runtimes[0]
        stdout_path = Path(str(record["stdoutPath"]))
        self.assertTrue(stdout_path.exists(), f"missing sidecar: {stdout_path}")
        self.assertEqual(stdout_path.read_text(encoding="utf-8"), stream)
        self.assertEqual(record["runtimeDirectoryRemoved"], not runtime.exists())
        self.assertIs(record["runtimeDirectoryRemoved"], False)

    def test_a_secret_straddling_the_tail_cut_is_still_removed(self) -> None:
        leak = "sk-live-access-0123456789abcdef"
        record = self.execute(ProcessResult(0, "a" * 6000 + " " + leak, "", 1.0), ["a", "a"])
        self.assertNotIn(leak, json.dumps(record))

    def test_a_token_whose_prefix_falls_outside_the_tail_is_still_caught(self) -> None:
        """A secret is recognised by its prefix; slicing first would orphan it."""
        stream = "a" * 6000 + "Bearer " + "q" * 3990
        record = self.execute(ProcessResult(0, stream, "", 1.0), ["a", "a"])
        self.assertNotIn("q" * 3990, record["stdoutTail"])
        self.assertIn("[REDACTED]", record["stdoutTail"])

    def test_ordinary_output_survives_redaction(self) -> None:
        record = self.execute(
            ProcessResult(return_code=0, stdout="Refactored twelve files, tests green.", stderr="", duration_seconds=1.0),
            ["a", "a"],
        )
        self.assertEqual(record["stdoutTail"], "Refactored twelve files, tests green.")

    def test_the_recorded_home_is_the_one_that_was_launched(self) -> None:
        """The trust file and the provider config land after prepare_isolated_home
        returns, so the snapshot it froze describes a home that never ran."""
        (self.home.path / "late-config.json").write_text('{"added": "by the launcher"}', encoding="utf-8")
        record = self.execute(ProcessResult(return_code=0, stdout="ok", stderr="", duration_seconds=1.0), ["a", "a"])
        listed = {entry["path"] for entry in record["home"]["files"]}
        self.assertIn("late-config.json", listed)
        self.assertIn("credentials/kimi-code.json", listed)
        self.assertEqual(record["home"]["isolatedHome"], str(self.home.path))

    def test_an_untouched_main_checkout_is_recorded_as_clean(self) -> None:
        record = self.execute(ProcessResult(return_code=0, stdout="ok", stderr="", duration_seconds=1.0), ["a", "a"])
        self.assertIs(record["mainCheckoutContaminated"], False)
        self.assertEqual(record["mainCheckout"], str(self.owner))

    def test_a_run_that_wrote_outside_its_worktree_is_flagged(self) -> None:
        """cwd is not a sandbox; the fingerprint is what makes the write visible."""
        record = self.execute(ProcessResult(return_code=0, stdout="ok", stderr="", duration_seconds=1.0), ["a", "b"])
        self.assertIs(record["mainCheckoutContaminated"], True)

    def test_a_contaminated_run_exits_non_zero_even_when_the_provider_succeeded(self) -> None:
        record = dict(exitCode=0, mainCheckoutContaminated=True, mainCheckout=str(self.owner))
        with mock.patch.object(subject, "run", return_value=record):
            code = subject.main(["--provider", "kimi", "--worktree", str(self.worktree), "--brief", str(self.brief)])
        self.assertEqual(code, 3)

    def test_a_run_that_changed_the_user_environment_fails_and_names_only_the_variable(self) -> None:
        """dotnet wrote the real HKCU PATH from a redirected home; values are never recorded."""
        self.snapshot.side_effect = [
            HostSnapshot(environment={"PATH": "digest-before", "TEMP": "t"}, sandbox_accounts=frozenset(), acls={}),
            HostSnapshot(environment={"PATH": "digest-after", "TEMP": "t"}, sandbox_accounts=frozenset(), acls={}),
        ]
        record = self.execute(ProcessResult(return_code=0, stdout="ok", stderr="", duration_seconds=1.0), ["a", "a"])
        self.assertEqual(record["host"]["operatorEnvironmentChanged"], ["PATH"])
        self.assertEqual(record["failure"], "OPERATOR_ENV_MUTATED")
        self.assertNotIn("digest-after", json.dumps(record))

    def test_a_path_change_is_recorded_by_entry_with_the_key_write_times(self) -> None:
        """#1052: a digest showed PATH changed, but not which entry or when."""
        self.snapshot.side_effect = [
            HostSnapshot(environment={"PATH": "d1"}, sandbox_accounts=frozenset(), acls={},
                         path=r"C:\Tools", environment_written="2026-10-08T11:48:00+00:00"),
            HostSnapshot(environment={"PATH": "d2"}, sandbox_accounts=frozenset(), acls={},
                         path=r"C:\Tools;C:\New", environment_written="2026-10-08T11:52:12+00:00"),
        ]
        record = self.execute(ProcessResult(return_code=0, stdout="ok", stderr="", duration_seconds=1.0), ["a", "a"])
        self.assertEqual(record["host"]["operatorPathChanges"], {"added": [r"C:\New"], "removed": []})
        self.assertEqual(record["host"]["environmentKeyWritten"],
                         {"before": "2026-10-08T11:48:00+00:00", "after": "2026-10-08T11:52:12+00:00"})
        self.assertEqual(record["failure"], "OPERATOR_ENV_MUTATED")

    def execute_codex(self, stdout: str) -> dict[str, object]:
        with (
            mock.patch.object(subject, "resolve_worktree", return_value=self.worktree),
            mock.patch.object(subject, "main_checkout", return_value=self.owner),
            mock.patch.object(subject, "fingerprint", side_effect=["a", "a"]),
            mock.patch.object(subject, "_resolve_executable", return_value=Path("codex")),
            mock.patch.object(subject, "prepare_isolated_home", return_value=self.home),
            mock.patch.object(subject, "build_argv", return_value=(["codex"], None)),
            mock.patch.object(subject, "check_codex_isolation", return_value=None),
            mock.patch.object(subject, "_run_process", return_value=ProcessResult(0, stdout, "", 1.0)),
            mock.patch.object(subject, "persist_rotated_credentials", return_value=[]),
        ):
            # A record path keeps the event journal (#1069) inside this test's directory.
            return subject.run("codex", self.worktree, self.brief, runtime_root=self.root,
                               record_path=self.root / "records" / "run.json")

    def test_a_codex_run_without_a_complete_event_stream_fails(self) -> None:
        """Exit 0 and no events looks like success in every other field."""
        record = self.execute_codex("done, tests green")
        self.assertEqual(record["host"]["telemetry"], {"problems": ["no_events", "unparsed_line:1"]})
        self.assertEqual(record["failure"], "EXECUTOR_TELEMETRY_INCOMPLETE")
        complete = "\n".join(json.dumps(event) for event in (
            {"type": "thread.started"}, {"type": "turn.started"}, {"type": "turn.completed", "usage": {}}))
        record = self.execute_codex(complete)
        self.assertEqual(record["host"]["telemetry"], {"problems": []})
        self.assertNotIn("hostViolations", record)

    def test_sandbox_accounts_that_appeared_during_the_run_fail_it(self) -> None:
        self.snapshot.side_effect = [
            HostSnapshot(environment={}, sandbox_accounts=frozenset(), acls={}),
            HostSnapshot(environment={}, sandbox_accounts=frozenset({"CodexSandboxOffline"}), acls={}),
        ]
        record = self.execute(
            ProcessResult(return_code=1, stdout="", stderr="boom", duration_seconds=1.0), ["a", "a"]
        )
        self.assertEqual(record["host"]["sandboxAccountsCreated"], ["CodexSandboxOffline"])
        self.assertEqual(record["failure"], "SANDBOX_ACCOUNTS_CREATED")
        self.assertEqual(record["processFailure"], "PROVIDER_FAILURE")

    def test_the_codex_sandbox_log_tail_survives_the_home_and_a_profile_grant_fails_the_run(self) -> None:
        """The incident's evidence was deleted with the disposable CODEX_HOME."""
        # Outside the runtime root, which the run is allowed to grant itself.
        elsewhere = tempfile.TemporaryDirectory()
        self.addCleanup(elsewhere.cleanup)
        profile = Path(elsewhere.name) / "profile"
        log_dir = self.home.path / ".sandbox"
        log_dir.mkdir()
        (log_dir / "sandbox.2026-09-26.log").write_text(
            f"granting write ACE to {self.worktree} for sandbox group and capability SID\n"
            f"granting read ACE to {profile / 'AppData'} for sandbox users\n",
            encoding="utf-8",
        )
        with mock.patch.dict(subject.os.environ, {"USERPROFILE": str(profile)}):
            record = self.execute_advisor(ProcessResult(return_code=0, stdout="ok", stderr="", duration_seconds=1.0))
        log = record["host"]["sandboxLog"]
        self.assertEqual([entry["name"] for entry in log["files"]], ["sandbox.2026-09-26.log"])
        self.assertIn("granting read ACE to", log["files"][0]["tail"])
        self.assertEqual(log["profileGrants"], [f"read {profile / 'AppData'}"])
        self.assertEqual(record["failure"], "SANDBOX_PROFILE_GRANT")

    def test_a_run_that_moved_the_host_exits_non_zero_even_when_the_provider_succeeded(self) -> None:
        record = dict(exitCode=0, mainCheckoutContaminated=False, hostViolations=["OPERATOR_ENV_MUTATED"])
        with mock.patch.object(subject, "run", return_value=record):
            code = subject.main(["--provider", "kimi", "--worktree", str(self.worktree), "--brief", str(self.brief)])
        self.assertEqual(code, 6)

    def test_a_clean_successful_run_exits_zero(self) -> None:
        record = dict(exitCode=0, mainCheckoutContaminated=False, mainCheckout=str(self.owner))
        with mock.patch.object(subject, "run", return_value=record):
            code = subject.main(["--provider", "kimi", "--worktree", str(self.worktree), "--brief", str(self.brief)])
        self.assertEqual(code, 0)

    def test_a_refusal_is_reported_as_json_on_stderr_with_its_category(self) -> None:
        with mock.patch.object(subject, "run", side_effect=ExecutorError("SANDBOX_UNSAFE", "nope")):
            code = subject.main(["--provider", "kimi", "--worktree", str(self.worktree), "--brief", str(self.brief)])
        self.assertEqual(code, 2)

    def test_output_overflow_precedes_stdout_substring_classification(self) -> None:
        record = self.execute(
            ProcessResult(
                return_code=125,
                stdout="moved source says unauthorized",
                stderr="",
                duration_seconds=1.0,
                output_too_large=True,
            ),
            ["a", "a"],
        )
        self.assertEqual(record["failure"], "OUTPUT_TOO_LARGE")
        self.assertTrue(record["outputTooLarge"])

    def test_a_stderr_auth_failure_is_classified(self) -> None:
        record = self.execute(
            ProcessResult(return_code=1, stdout="", stderr="unauthorized", duration_seconds=1.0), ["a", "a"]
        )
        self.assertEqual(record["failure"], "AUTH_MISSING")
        self.assertFalse(record["outputTooLarge"])

    def test_a_cleanup_failure_is_reported_without_losing_the_record(self) -> None:
        """A failed cleanup must not turn a run that happened into no evidence at all."""
        with (
            mock.patch.object(subject, "resolve_worktree", return_value=self.worktree),
            mock.patch.object(subject, "main_checkout", return_value=self.owner),
            mock.patch.object(subject, "fingerprint", side_effect=["a", "a"]),
            mock.patch.object(subject, "_resolve_executable", return_value=Path("kimi")),
            mock.patch.object(subject, "prepare_isolated_home", return_value=self.home),
            mock.patch.object(subject, "persist_rotated_credentials", return_value=[]),
            mock.patch.object(
                subject, "_run_process", return_value=ProcessResult(0, "ok", "", 1.0)
            ),
            mock.patch.object(subject, "_remove_runtime", return_value=(False, "locked")),
            self.contained_mkdtemp(),
        ):
            record = subject.run("kimi", self.worktree, self.brief)
        self.assertIs(record["runtimeDirectoryRemoved"], False)
        self.assertEqual(record["runtimeDirectoryProblem"], "locked")
        self.assertIn("runtimeDirectory", record)
        self.assertEqual(record["exitCode"], 0)

    def test_a_run_whose_credential_copy_survives_exits_non_zero(self) -> None:
        record = dict(exitCode=0, runtimeDirectoryRemoved=False, runtimeDirectory="C:/tmp/x")
        with mock.patch.object(subject, "run", return_value=record):
            code = subject.main(["--provider", "kimi", "--worktree", str(self.worktree), "--brief", str(self.brief)])
        self.assertEqual(code, 4)

    def test_cleanup_retries_before_giving_up(self) -> None:
        """OpenCode keeps its database open for a moment after the run returns."""
        root = self.root / "runtime"
        root.mkdir()
        with (
            mock.patch.object(subject, "_remove_runtime_dir", side_effect=[OSError("locked"), None]) as remover,
            mock.patch.object(subject.time, "sleep"),
        ):
            removed, problem = subject._remove_runtime(root)
        self.assertTrue(removed)
        self.assertEqual(problem, "")
        self.assertEqual(remover.call_count, 2)

    def test_cleanup_gives_up_after_the_attempt_budget(self) -> None:
        root = self.root / "runtime2"
        root.mkdir()
        with (
            mock.patch.object(subject, "_remove_runtime_dir", side_effect=OSError("still locked")) as remover,
            mock.patch.object(subject.time, "sleep"),
        ):
            removed, problem = subject._remove_runtime(root)
        self.assertFalse(removed)
        self.assertIn("still locked", problem)
        self.assertEqual(remover.call_count, subject.CLEANUP_ATTEMPTS)

    def test_the_runtime_directory_is_removed_even_when_the_run_raises(self) -> None:
        with (
            mock.patch.object(subject, "resolve_worktree", return_value=self.worktree),
            mock.patch.object(subject, "main_checkout", return_value=self.owner),
            mock.patch.object(subject, "_resolve_executable", return_value=Path("kimi")),
            mock.patch.object(subject, "prepare_isolated_home", side_effect=ExecutorError("AUTH_MISSING", "no")),
            mock.patch.object(subject, "_remove_runtime", return_value=(True, "")) as remover,
            self.contained_mkdtemp(),
        ):
            with self.assertRaises(ExecutorError):
                subject.run("kimi", self.worktree, self.brief)
        remover.assert_called_once()


if __name__ == "__main__":
    unittest.main()
