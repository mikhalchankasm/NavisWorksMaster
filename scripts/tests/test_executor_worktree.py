from __future__ import annotations

import json
import subprocess
import tempfile
import unittest
from pathlib import Path

import scripts.executor_worktree as subject
from scripts.executor_worktree import NUL
from scripts.executor_primitives import ExecutorError


def _git(*arguments: str, cwd: Path) -> None:
    subprocess.run(["git", *arguments], cwd=cwd, check=True, capture_output=True, timeout=60)


def _status(cwd: Path) -> str:
    return subprocess.run(
        ["git", "status", "--short"],
        cwd=cwd,
        check=True,
        capture_output=True,
        text=True,
        timeout=60,
    ).stdout


class WorktreeTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.main = self.root / "main"
        self.main.mkdir()
        _git("init", "-q", "-b", "master", ".", cwd=self.main)
        _git("config", "user.email", "t@example.invalid", cwd=self.main)
        _git("config", "user.name", "t", cwd=self.main)
        (self.main / "seed.txt").write_text("seed", encoding="utf-8")
        _git("add", "seed.txt", cwd=self.main)
        _git("commit", "-q", "-m", "seed", cwd=self.main)
        self.linked = self.root / "linked"
        _git("worktree", "add", "-q", "-b", "task/one", str(self.linked), cwd=self.main)

    def test_a_linked_worktree_on_its_own_branch_is_accepted(self) -> None:
        self.assertTrue(subject.resolve_worktree(self.linked).is_dir())

    def test_codex_plan_requires_an_advice_branch_but_codex_does_not(self) -> None:
        self.assertEqual(subject.resolve_worktree(self.linked, "codex"), self.linked.resolve())
        with self.assertRaises(ExecutorError) as raised:
            subject.resolve_worktree(self.linked, "codex-plan")
        self.assertEqual(raised.exception.category, "SANDBOX_UNSAFE")
        self.assertIn("advice/", str(raised.exception))

    def test_the_main_checkout_is_refused(self) -> None:
        with self.assertRaises(ExecutorError) as raised:
            subject.resolve_worktree(self.main)
        self.assertEqual(raised.exception.category, "SANDBOX_UNSAFE")
        self.assertIn("main checkout", str(raised.exception))

    def test_a_worktree_on_a_protected_branch_is_refused(self) -> None:
        second = self.root / "onmain"
        _git("worktree", "add", "-q", "--detach", str(second), cwd=self.main)
        _git("checkout", "-q", "-B", "main", cwd=second)
        with self.assertRaises(ExecutorError) as raised:
            subject.resolve_worktree(second)
        self.assertIn("own branch", str(raised.exception))

    def test_a_plain_directory_is_refused(self) -> None:
        plain = self.root / "plain"
        plain.mkdir()
        with self.assertRaises(ExecutorError) as raised:
            subject.resolve_worktree(plain)
        self.assertEqual(raised.exception.category, "SANDBOX_UNSAFE")

    def test_a_missing_directory_is_refused(self) -> None:
        with self.assertRaises(ExecutorError):
            subject.resolve_worktree(self.root / "absent")

    def test_the_main_checkout_is_found_from_the_linked_worktree(self) -> None:
        self.assertEqual(subject.main_checkout(self.linked), subject._canonical_path(self.main))

    def test_the_fingerprint_changes_when_the_main_checkout_is_written_to(self) -> None:
        """cwd is not a sandbox, so contamination is detected rather than prevented."""
        before = subject.fingerprint(self.main)
        self.assertEqual(before, subject.fingerprint(self.main))
        (self.main / "stray.txt").write_text("written from outside the worktree", encoding="utf-8")
        self.assertNotEqual(before, subject.fingerprint(self.main))

    def test_the_fingerprint_changes_when_the_main_checkout_moves(self) -> None:
        before = subject.fingerprint(self.main)
        (self.main / "seed.txt").write_text("edited", encoding="utf-8")
        _git("commit", "-q", "-am", "second", cwd=self.main)
        self.assertNotEqual(before, subject.fingerprint(self.main))

    def test_overwriting_an_already_modified_tracked_file_is_visible(self) -> None:
        """Porcelain says "modified" either way; only the content tells them apart."""
        (self.main / "seed.txt").write_text("the owner was working on this", encoding="utf-8")
        before = subject.fingerprint(self.main)
        (self.main / "seed.txt").write_text("an executor overwrote it", encoding="utf-8")
        self.assertNotEqual(before, subject.fingerprint(self.main))

    def test_overwriting_an_existing_untracked_file_is_visible(self) -> None:
        stray = self.main / "notes.txt"
        stray.write_text("the owner's notes", encoding="utf-8")
        before = subject.fingerprint(self.main)
        stray.write_text("clobbered", encoding="utf-8")
        self.assertNotEqual(before, subject.fingerprint(self.main))

    def test_deleting_an_untracked_file_is_visible(self) -> None:
        stray = self.main / "notes.txt"
        stray.write_text("the owner's notes", encoding="utf-8")
        before = subject.fingerprint(self.main)
        stray.unlink()
        self.assertNotEqual(before, subject.fingerprint(self.main))

    def test_a_dirty_checkout_still_fingerprints_stably(self) -> None:
        (self.main / "seed.txt").write_text("modified", encoding="utf-8")
        (self.main / "extra.txt").write_text("untracked", encoding="utf-8")
        self.assertEqual(subject.fingerprint(self.main), subject.fingerprint(self.main))

    def test_a_rename_record_yields_the_destination_and_drops_the_source(self) -> None:
        """With -z the source path is its own record, not an arrow inside one."""
        self.assertEqual(subject._dirty_paths("R  renamed.txt" + NUL + "seed.txt" + NUL), ["renamed.txt"])
        _git("mv", "seed.txt", "renamed.txt", cwd=self.main)
        self.assertEqual(subject.fingerprint(self.main), subject.fingerprint(self.main))

    def test_a_path_git_would_quote_is_still_matched(self) -> None:
        """The default porcelain format C-quotes such a path; -z emits it raw."""
        awkward = self.main / "замечания к чертежу.txt"
        awkward.write_text("the owner's notes", encoding="utf-8")
        before = subject.fingerprint(self.main)
        awkward.write_text("clobbered", encoding="utf-8")
        self.assertNotEqual(before, subject.fingerprint(self.main))

    def test_retargeting_a_symlink_is_visible(self) -> None:
        """Following it would miss a retarget between two files with equal content."""
        (self.main / "one.txt").write_text("same", encoding="utf-8")
        (self.main / "two.txt").write_text("same", encoding="utf-8")
        link = self.main / "pointer"
        try:
            link.symlink_to(self.main / "one.txt")
        except (OSError, NotImplementedError):
            self.skipTest("symlinks are not available to this account")
        before = subject.fingerprint(self.main)
        link.unlink()
        link.symlink_to(self.main / "two.txt")
        self.assertNotEqual(before, subject.fingerprint(self.main))

    def test_two_dirty_files_cannot_be_rewritten_into_the_same_digest(self) -> None:
        """Unframed concatenation lets a='' b='bX' serialize like a='b' b='X'."""
        first, second = self.main / "a.txt", self.main / "b.txt"
        first.write_text("", encoding="utf-8")
        second.write_text("bX", encoding="utf-8")
        before = subject.fingerprint(self.main)
        first.write_text("b", encoding="utf-8")
        second.write_text("X", encoding="utf-8")
        self.assertNotEqual(before, subject.fingerprint(self.main))

    def test_work_inside_the_linked_worktree_leaves_the_main_checkout_alone(self) -> None:
        before = subject.fingerprint(self.main)
        (self.linked / "new.txt").write_text("legitimate work", encoding="utf-8")
        self.assertEqual(before, subject.fingerprint(self.main))

    def test_codex_plan_restores_tracked_and_untracked_files_and_records_both(self) -> None:
        baseline = subject.worktree_head(self.linked)
        (self.linked / "seed.txt").write_text("advisor edit", encoding="utf-8")
        (self.linked / "notes.txt").write_text("advisor note", encoding="utf-8")

        record = subject.restore_disposable_worktree("codex-plan", self.linked, baseline)

        self.assertEqual(_status(self.linked), "")
        self.assertEqual((self.linked / "seed.txt").read_text(encoding="utf-8"), "seed")
        self.assertFalse((self.linked / "notes.txt").exists())
        self.assertIs(record["worktreeRestored"], True)
        self.assertEqual(record["discardedPaths"], ["notes.txt", "seed.txt"])
        self.assertNotIn("worktreeRestoreProblem", record)

    def test_executor_provider_worktree_is_untouched(self) -> None:
        baseline = subject.worktree_head(self.linked)
        (self.linked / "seed.txt").write_text("executor edit", encoding="utf-8")
        (self.linked / "notes.txt").write_text("executor note", encoding="utf-8")

        record = subject.restore_disposable_worktree("codex", self.linked, baseline)

        self.assertEqual((self.linked / "seed.txt").read_text(encoding="utf-8"), "executor edit")
        self.assertEqual((self.linked / "notes.txt").read_text(encoding="utf-8"), "executor note")
        self.assertEqual(record, {})


class BriefTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.path = Path(self.temp.name) / "brief.md"

    def test_the_limit_is_the_documented_two_kilobytes(self) -> None:
        self.assertEqual(subject.MAX_BRIEF_BYTES, 2048)

    def test_a_brief_at_the_limit_is_accepted(self) -> None:
        self.path.write_bytes(b"x" * subject.MAX_BRIEF_BYTES)
        self.assertEqual(len(subject.read_brief(self.path)), subject.MAX_BRIEF_BYTES)

    def test_an_oversized_brief_is_refused_and_says_to_split_the_task(self) -> None:
        self.path.write_bytes(b"x" * (subject.MAX_BRIEF_BYTES + 1))
        with self.assertRaises(ExecutorError) as raised:
            subject.read_brief(self.path)
        self.assertEqual(raised.exception.category, "CONFIG_INVALID")
        self.assertIn("Split the task", str(raised.exception))

    def test_an_empty_brief_is_refused(self) -> None:
        self.path.write_text("", encoding="utf-8")
        with self.assertRaises(ExecutorError):
            subject.read_brief(self.path)

    def test_a_missing_brief_is_refused(self) -> None:
        with self.assertRaises(ExecutorError):
            subject.read_brief(self.path)

    def test_a_brief_that_is_not_utf8_is_refused(self) -> None:
        self.path.write_bytes(b"\xff\xfe\x00bad")
        with self.assertRaises(ExecutorError):
            subject.read_brief(self.path)

    def test_the_advisor_prompt_describes_advice_without_an_acceptance_verdict(self) -> None:
        prompt = subject.build_prompt("codex-plan", "Should this design change?\n")
        self.assertTrue(prompt.startswith("You are advising the NavisHelper owner"))
        self.assertNotIn("executor", prompt)
        self.assertNotIn("acceptance criterion", prompt)

    def test_every_non_advisor_prompt_keeps_the_executor_preamble(self) -> None:
        for provider in ("claude", "kimi", "glm", "glm-flash", "codex"):
            with self.subTest(provider=provider):
                self.assertEqual(subject.build_prompt(provider, "BRIEF"), subject.PREAMBLE + "BRIEF")


class ArgvTests(unittest.TestCase):
    def test_claude_gets_no_mcp_no_skills_and_takes_the_prompt_on_stdin(self) -> None:
        env: dict[str, str] = {}
        argv, stdin_text = subject.build_argv("claude", Path("claude.exe"), "PROMPT", env)
        self.assertIn("--strict-mcp-config", argv)
        self.assertIn("--disable-slash-commands", argv)
        self.assertNotIn("--mcp-config", argv)
        self.assertEqual(stdin_text, "PROMPT")
        self.assertNotIn("PROMPT", argv)

    def test_opencode_is_given_a_config_that_declares_no_mcp(self) -> None:
        env: dict[str, str] = {}
        argv, stdin_text = subject.build_argv("glm", Path("opencode"), "PROMPT", env)
        self.assertIn("--pure", argv)
        self.assertIsNone(stdin_text)
        config = json.loads(env["OPENCODE_CONFIG_CONTENT"])
        self.assertEqual(config["mcp"], {})
        self.assertEqual(config["subagent_depth"], 0)

    def test_codex_runs_sandboxed_and_takes_no_mcp(self) -> None:
        """The only one of the four with a real filesystem sandbox of its own."""
        env: dict[str, str] = {}
        argv, stdin_text = subject.build_argv("codex", Path("codex.js"), "PROMPT", env)
        self.assertIn("exec", argv)
        # #1052: an explicit workspace-write sandbox; --approve-for-me would hand
        # approvals to Codex's auto-reviewer after every -c override.
        self.assertEqual(argv[argv.index("--sandbox") + 1], "workspace-write")
        self.assertNotIn("--approve-for-me", argv)
        self.assertIn("tools.web_search=false", argv)
        self.assertIn("tools.image_gen=false", argv)
        self.assertIn("PROMPT", argv)
        self.assertIsNone(stdin_text)
        self.assertNotIn("--dangerously-bypass-approvals-and-sandbox", argv)

    def test_both_codex_routes_switch_off_account_connectors_browser_and_web(self) -> None:
        """HetengSpec session, 2026-09-29: codex 0.158 gave the executor the ChatGPT account's connectors.

        codex_apps (Outlook send_email, Notion, sites deploy, OpenAI platform keys) arrives with the login, not
        from config.toml, and tools.web_search=false no longer removed the web tool.
        """
        for provider in ("codex", "codex-plan"):
            with self.subTest(provider=provider):
                argv, _ = subject.build_argv(provider, Path("codex.js"), "PROMPT", {})
                pairs = {argv[i + 1] for i, argument in enumerate(argv[:-1]) if argument == "-c"}
                self.assertIn('web_search="disabled"', pairs)
                for feature in ("apps", "browser_use", "browser_use_external", "browser_use_full_cdp_access",
                                "computer_use", "plugins", "remote_plugin", "plugin_sharing", "in_app_browser",
                                "image_generation", "skill_mcp_dependency_install", "tool_suggest"):
                    self.assertIn(f"features.{feature}=false", pairs)

    def test_codex_advisor_uses_the_measured_workspace_write_route(self) -> None:
        codex_argv, _ = subject.build_argv("codex", Path("codex.js"), "PROMPT", {})
        isolation = [
            "-c", "tools.web_search=false",
            "-c", "tools.image_gen=false",
            "-c", 'web_search="disabled"',
        ]
        for feature in (
            "apps", "browser_use", "browser_use_external", "browser_use_full_cdp_access", "computer_use", "plugins",
            "remote_plugin", "plugin_sharing", "in_app_browser", "image_generation", "skill_mcp_dependency_install",
            "tool_suggest", "browser_annotation_api",
        ):
            isolation += ["-c", f"features.{feature}=false"]
        expected = [
            "exec", "--model", "gpt-6.1-sol",
            "-c", 'model_reasoning_effort="high"',
            "-c", 'service_tier="priority"',
            *isolation,
            "-c", 'approval_policy="never"',
            "--sandbox", "workspace-write", "--skip-git-repo-check", "--json", "PROMPT",
        ]
        self.assertEqual(codex_argv[-len(expected):], expected)
        argv, stdin_text = subject.build_argv("codex-plan", Path("codex.js"), "PROMPT", {})
        self.assertIn("exec", argv)
        self.assertIn('approval_policy="never"', argv)
        self.assertIn("--approve-for-me", argv)
        self.assertNotIn("--sandbox", argv)
        self.assertFalse(any("sandbox_permissions" in argument for argument in argv))
        self.assertIn("tools.web_search=false", argv)
        self.assertIn("tools.image_gen=false", argv)
        self.assertIn("PROMPT", argv)
        self.assertIsNone(stdin_text)

    def test_the_features_listing_is_judged_fail_closed(self) -> None:
        """codex ignores an unknown feature name silently, so the listing itself is judged, not the switches."""
        disabled = ("apps", "browser_use", "browser_use_external", "browser_use_full_cdp_access", "computer_use",
                    "plugins", "remote_plugin", "plugin_sharing", "in_app_browser", "image_generation",
                    "skill_mcp_dependency_install", "tool_suggest", "browser_annotation_api")
        rows = ["WARNING: proceeding, even though we could not create PATH aliases"]
        rows += [f"{name:<40} stable             false" for name in disabled]
        rows += [f"{name:<40} stable             true" for name in ("in_app_chat", "in_app_local_automation", "hooks")]
        rows += ["codex_apps_mcp_2026_07_28                under development  false"]

        def listing(lines):
            return chr(10).join(lines)

        self.assertEqual(subject.unsafe_codex_features(listing(rows)), [])
        apps_on = [row.replace("false", "true") if row.startswith("apps ") else row for row in rows]
        self.assertEqual(subject.unsafe_codex_features(listing(apps_on)), ["apps"])
        renamed = [row for row in rows if not row.startswith("apps ")] + ["integrations                             stable             true"]
        self.assertEqual(subject.unsafe_codex_features(listing(renamed)), ["apps (not listed)", "integrations (not reviewed)"])
        risky = rows + ["connectors_v2                            stable             true"]
        self.assertEqual(subject.unsafe_codex_features(listing(risky)), ["connectors_v2"])
        new_mcp = [row.replace("under development  false", "under development  true") for row in rows]
        self.assertEqual(subject.unsafe_codex_features(listing(new_mcp)), ["codex_apps_mcp_2026_07_28"])
        dashed = rows + ["remote-plugin-v2                         stable             true"]
        self.assertEqual(subject.unsafe_codex_features(listing(dashed)), ["remote-plugin-v2 (not reviewed)"])
        unknown = rows + ["brand_new_thing                          stable             true"]
        self.assertEqual(subject.unsafe_codex_features(listing(unknown)), ["brand_new_thing (not reviewed)"])
        self.assertIsNone(subject.unsafe_codex_features(""))
        self.assertIsNone(subject.unsafe_codex_features("done nothing"))

    def test_every_provider_is_pinned_to_the_reviewed_route(self) -> None:
        for provider in ("claude", "kimi", "glm", "codex", "codex-plan"):
            with self.subTest(provider=provider):
                argv, _ = subject.build_argv(provider, Path("cli"), "PROMPT", {})
                self.assertIn(str(subject.EXECUTOR_PROFILES[provider]["model"]), argv)

    def test_the_preamble_tells_the_agent_to_stop_rather_than_work_around(self) -> None:
        self.assertIn("write only inside this worktree", subject.PREAMBLE)
        self.assertIn("Do not weaken or delete a test", subject.PREAMBLE)
        self.assertIn("Do not merge", subject.PREAMBLE)


if __name__ == "__main__":
    unittest.main()
