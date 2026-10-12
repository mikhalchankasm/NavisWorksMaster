from __future__ import annotations

import json
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import scripts.agent_home_isolation as subject
import scripts.executor_worktree as worktree
from scripts.executor_primitives import ExecutorError


class AgentHomeIsolationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.operator = self.root / "operator"
        self.runtime = self.root / "runtime"
        self.runtime.mkdir()
        self.env = {"USERPROFILE": str(self.operator), "HOME": str(self.operator)}
        self._plant_operator_home()

    def _plant_operator_home(self) -> None:
        """A realistic operator profile: credentials next to ambient capability."""
        claude = self.operator / ".claude"
        (claude / "plugins" / "repos").mkdir(parents=True)
        (claude / "skills").mkdir()
        (claude / ".credentials.json").write_text('{"claudeAiOauth": {"accessToken": "c"}}', encoding="utf-8")
        (claude / "settings.json").write_text('{"hooks": {"PreToolUse": []}}', encoding="utf-8")
        (claude / "settings.local.json").write_text("{}", encoding="utf-8")

        kimi = self.operator / ".kimi-code"
        (kimi / "credentials").mkdir(parents=True)
        (kimi / "credentials" / "kimi-code.json").write_text('{"access_token": "k"}', encoding="utf-8")
        (kimi / "device_id").write_text("device", encoding="utf-8")
        (kimi / "mcp.json").write_text('{"mcpServers": {"blender": {}, "navishelper": {}}}', encoding="utf-8")

        opencode = self.operator / ".local" / "share" / "opencode"
        opencode.mkdir(parents=True)
        (opencode / "auth.json").write_text(
            '{"zai-coding-plan": {"key": "z"}, "anthropic": {"key": "a"}, "openai": {"key": "o"}}',
            encoding="utf-8",
        )
        (opencode / "opencode.json").write_text('{"mcp": {"navishelper": {}}}', encoding="utf-8")

        qwen = self.operator / ".qwen"
        for name in ("extensions", "skills", "memories", "sessions", "projects", "file-history"):
            (qwen / name).mkdir(parents=True)
        (qwen / "settings.json").write_text(
            json.dumps(
                {
                    "ui": {"theme": "operator"},
                    "security": {"auth": {"selectedType": "openai"}},
                    "env": {subject.QWEN_PLAN_KEY: "plan-secret"},
                    "modelProviders": {
                        "openai": [
                            {
                                "id": "another-model",
                                "name": "Another route",
                                "baseUrl": "https://example.invalid/v1",
                                "envKey": subject.QWEN_PLAN_KEY,
                                "generationConfig": {},
                            },
                            {
                                "id": "qwen3.8-max",
                                "name": "[ModelStudio Token Plan for Global/Intl] qwen3.8-max",
                                "baseUrl": "https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1",
                                "envKey": subject.QWEN_PLAN_KEY,
                                "generationConfig": {
                                    "extra_body": {"enable_thinking": True},
                                    "thinkingMandatory": True,
                                },
                            },
                        ]
                    },
                    "model": {
                        "name": "qwen3.8-max",
                        "baseUrl": "https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1",
                    },
                    "permissions": {"allow": ["operator-only"]},
                }
            ),
            encoding="utf-8",
        )

        codex = self.operator / ".codex"
        codex.mkdir(parents=True)
        (codex / "auth.json").write_text(
            '{"auth_mode": "chatgpt", "OPENAI_API_KEY": null, "tokens": {"access_token": "c0"}}', encoding="utf-8"
        )
        (codex / "config.toml").write_text(
            'model = "gpt-6-sol"\n\n[mcp_servers.navishelper]\ncommand = "navishelper"\n'
            '\n[mcp_servers.blender]\ncommand = "blender"\n',
            encoding="utf-8",
        )

    def prepare(self, provider: str) -> subject.IsolatedHome:
        return subject.prepare_isolated_home(provider, self.runtime, dict(self.env))

    def test_each_provider_gets_its_own_home_and_environment_override(self) -> None:
        expected = {
            "claude": "CLAUDE_CONFIG_DIR",
            "kimi": "KIMI_CODE_HOME",
            "glm": "XDG_DATA_HOME",
            "qwen": "USERPROFILE",
            "codex": "CODEX_HOME",
            "codex-plan": "CODEX_HOME",
        }
        for provider, variable in expected.items():
            with self.subTest(provider=provider):
                home = self.prepare(provider)
                self.assertEqual(home.path, self.runtime / f"{provider}-home")
                self.assertTrue(home.env[variable].startswith(str(home.path)))
                self.assertTrue(home.path.is_dir())

    def test_ambient_capability_in_the_operator_home_does_not_reach_the_isolated_home(self) -> None:
        """The point of the module: mcp.json, plugins and skills stay behind."""
        for provider in ("claude", "kimi", "glm", "qwen", "codex", "codex-plan"):
            with self.subTest(provider=provider):
                home = self.prepare(provider)
                names = {Path(name).name.lower() for name, _ in home.inventory}
                self.assertEqual(names & subject.AMBIENT_CAPABILITY_NAMES, set())
                self.assertFalse((home.path / "mcp.json").exists())
                self.assertFalse((home.path / "plugins").exists())
                self.assertFalse((home.path / "skills").exists())

    def test_qwen_home_carries_one_route_and_the_plan_key_only(self) -> None:
        home = self.prepare("qwen")
        settings_path = home.path / "userprofile" / ".qwen" / "settings.json"
        settings = json.loads(settings_path.read_text(encoding="utf-8"))
        self.assertEqual(set(settings), subject.QWEN_SETTINGS_KEYS)
        self.assertEqual(settings["env"], {subject.QWEN_PLAN_KEY: "plan-secret"})
        self.assertEqual(len(settings["modelProviders"]["openai"]), 1)
        self.assertEqual(settings["modelProviders"]["openai"][0]["id"], "qwen3.8-max")
        self.assertEqual(settings["model"]["name"], "qwen3.8-max")
        self.assertNotIn("ui", settings)
        self.assertNotIn("permissions", settings)
        for name in subject.QWEN_FOREIGN_CONTENT_NAMES:
            self.assertFalse((home.path / "userprofile" / ".qwen" / name).exists())
        self.assertNotIn("plan-secret", json.dumps(home.record()))

    def test_qwen_profile_is_pinned_in_code_config_and_test(self) -> None:
        expected = {
            "model": "qwen3.8-max",
            "effort": "model-mandated",
            "authMode": "ModelStudio Token Plan key",
            "authType": "openai",
            "endpoint": "https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1",
        }
        self.assertEqual(subject.EXECUTOR_ONLY_PROFILES["qwen"], expected)
        config = json.loads(
            (Path(subject.__file__).resolve().parents[1] / "config" / "external-executors.json").read_text(encoding="utf-8")
        )
        self.assertEqual(config["executorProviders"]["qwen"], expected)

    def test_qwen_argv_disables_ambient_and_network_capability(self) -> None:
        home = self.prepare("qwen")
        argv, stdin_text = worktree.build_argv("qwen", Path("qwen"), "PROMPT", home.env)
        self.assertIsNone(stdin_text)
        self.assertEqual(home.env["OPENAI_API_KEY"], "plan-secret")
        self.assertEqual(home.env["QWEN_CODE_SUPPRESS_YOLO_WARNING"], "1")
        self.assertIn("--safe-mode", argv)
        self.assertIn("--bare", argv)
        self.assertEqual(
            argv[argv.index("--approval-mode") + 1],
            "yolo",
            "Qwen is non-interactive, so a prompt-gated mode denies shell tools instead of requesting approval",
        )
        self.assertEqual(argv[argv.index("--auth-type") + 1], "openai")
        self.assertEqual(
            argv[argv.index("--openai-base-url") + 1],
            "https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1",
        )
        self.assertEqual(argv[argv.index("--model") + 1], "qwen3.8-max")
        self.assertEqual(argv[argv.index("--output-format") + 1], "text")
        self.assertFalse("--openai-api-key" in argv)
        self.assertFalse(any(value == "plan-secret" for value in argv))
        self.assertEqual(argv[argv.index("--chat-recording") + 1], "false")
        self.assertEqual(argv[argv.index("--max-subagent-depth") + 1], "1")
        self.assertEqual(argv[argv.index("--max-wall-time") + 1], "1800s")
        self.assertEqual(argv[argv.index("--max-tool-calls") + 1], "1000")
        excluded = argv.index("--exclude-tools")
        self.assertEqual(argv[excluded + 1 : excluded + 3], ["web_fetch", "web_search"])
        self.assertEqual(argv[-2:], ["-p", "PROMPT"])

    def test_qwen_refuses_a_disagreeing_operator_auth_type(self) -> None:
        settings_path = self.operator / ".qwen" / "settings.json"
        settings = json.loads(settings_path.read_text(encoding="utf-8"))
        settings["security"]["auth"]["selectedType"] = "qwen-oauth"
        settings_path.write_text(json.dumps(settings), encoding="utf-8")

        with self.assertRaises(ExecutorError) as raised:
            self.prepare("qwen")

        self.assertEqual(raised.exception.category, "AUTH_MISSING")
        self.assertIn("qwen-oauth", str(raised.exception))

    def test_qwen_records_explicitly_that_its_static_key_does_not_rotate(self) -> None:
        home = self.prepare("qwen")
        self.assertEqual(subject.ROTATING_CREDENTIALS["qwen"], ())
        self.assertEqual(subject.persist_rotated_credentials("qwen", home.path, home.env, home.operator), [])

    def test_the_credential_is_carried_across_whole(self) -> None:
        """Executor mode keeps the refresh token; only the reviewer sandbox strips it."""
        claude = self.prepare("claude")
        self.assertEqual(
            (claude.path / ".credentials.json").read_text(encoding="utf-8"),
            (self.operator / ".claude" / ".credentials.json").read_text(encoding="utf-8"),
        )
        kimi = self.prepare("kimi")
        self.assertEqual(
            (kimi.path / "credentials" / "kimi-code.json").read_text(encoding="utf-8"),
            '{"access_token": "k"}',
        )
        self.assertEqual((kimi.path / "device_id").read_text(encoding="utf-8"), "device")

    def test_only_the_fixed_route_is_carried_out_of_a_shared_credential_file(self) -> None:
        """OpenCode keeps every provider in one auth.json; the executor gets one."""
        glm = self.prepare("glm")
        carried = json.loads((glm.path / "data" / "opencode" / "auth.json").read_text(encoding="utf-8"))
        self.assertEqual(carried, {"zai-coding-plan": {"key": "z"}})
        self.assertNotIn("anthropic", carried)
        self.assertNotIn("openai", carried)

    def test_a_credential_file_without_the_fixed_route_is_refused(self) -> None:
        auth = self.operator / ".local" / "share" / "opencode" / "auth.json"
        auth.write_text('{"anthropic": {"key": "a"}}', encoding="utf-8")
        with self.assertRaises(ExecutorError) as raised:
            self.prepare("glm")
        self.assertEqual(raised.exception.category, "AUTH_MISSING")

    def test_a_caller_supplied_environment_is_filtered_like_the_default_one(self) -> None:
        """Otherwise an alternate-provider key survives into the launch environment."""
        home = subject.prepare_isolated_home(
            "kimi", self.runtime, {**self.env, "OPENAI_API_KEY": "leak", "NODE_OPTIONS": "--require evil.js"}
        )
        self.assertNotIn("OPENAI_API_KEY", home.env)
        self.assertNotIn("NODE_OPTIONS", home.env)
        self.assertIn("KIMI_CODE_HOME", home.env)

    def test_the_codex_configuration_carries_the_route_and_no_mcp_servers(self) -> None:
        """Codex declares MCP inside its own config.toml, where a name check is blind.

        The operator's copy declares six of them; the isolated one declares none.
        """
        home = self.prepare("codex")
        config = (home.path / "config.toml").read_text(encoding="utf-8")
        self.assertIn('model = "gpt-6.1-sol"', config)
        self.assertIn('model_reasoning_effort = "high"', config)
        self.assertIn('[windows]\nsandbox = "unelevated"\n', config)
        self.assertNotIn("mcp_servers", config)
        self.assertNotIn("navishelper", config)
        self.assertIn("c0", (home.path / "auth.json").read_text(encoding="utf-8"))

    def test_the_codex_template_pins_the_unelevated_windows_sandbox(self) -> None:
        """Without it the run took Codex's default mode, not the operator's.

        The elevated mode grants its sandbox accounts the whole profile.
        """
        self.assertEqual(
            subject.CODEX_EXECUTOR_CONFIG_TEMPLATE,
            'model = "{model}"\nmodel_reasoning_effort = "{effort}"\nweb_search = "disabled"\n\n'
            '[windows]\nsandbox = "unelevated"\n\n'
            "[features]\napps = false\nbrowser_use = false\nbrowser_use_external = false\n"
            "browser_use_full_cdp_access = false\ncomputer_use = false\nplugins = false\nremote_plugin = false\n"
            "plugin_sharing = false\nin_app_browser = false\nimage_generation = false\n"
            "skill_mcp_dependency_install = false\ntool_suggest = false\nbrowser_annotation_api = false\n",
        )

    def test_the_codex_advisor_configuration_carries_its_own_route(self) -> None:
        home = self.prepare("codex-plan")
        config = (home.path / "config.toml").read_text(encoding="utf-8")
        # Owner 2026-10-01: both Codex routes use GPT-6.1 at high effort; the
        # advisor differs from the executor by its mode, not by its model.
        self.assertIn('model = "gpt-6.1-sol"', config)
        self.assertIn('model_reasoning_effort = "high"', config)
        self.assertIn('[windows]\nsandbox = "unelevated"\n', config)
        self.assertEqual(subject.EXECUTOR_PROFILES["codex-plan"]["mode"], "advisor")
        self.assertNotIn("mode", subject.EXECUTOR_PROFILES["codex"])
        self.assertNotIn("mcp_servers", config)
        self.assertIn("c0", (home.path / "auth.json").read_text(encoding="utf-8"))
        self.assertEqual(subject.ROTATING_CREDENTIALS["codex-plan"], subject.ROTATING_CREDENTIALS["codex"])

    def test_an_api_key_login_is_refused_where_the_profile_declares_oauth(self) -> None:
        """Both live at the same path; only the contents tell them apart."""
        auth = self.operator / ".codex" / "auth.json"
        auth.write_text('{"auth_mode": "apikey", "OPENAI_API_KEY": "sk-live-0123456789"}', encoding="utf-8")
        with self.assertRaises(ExecutorError) as raised:
            self.prepare("codex")
        self.assertEqual(raised.exception.category, "AUTH_MISSING")
        self.assertIn("API key", str(raised.exception))

    def test_a_codex_login_without_tokens_is_refused(self) -> None:
        auth = self.operator / ".codex" / "auth.json"
        auth.write_text('{"auth_mode": "chatgpt", "tokens": {}}', encoding="utf-8")
        with self.assertRaises(ExecutorError) as raised:
            self.prepare("codex")
        self.assertEqual(raised.exception.category, "AUTH_MISSING")

    def test_an_mcp_declaration_inside_a_config_file_is_refused(self) -> None:
        home = self.prepare("codex")
        (home.path / "config.toml").write_text(
            'model = "x"\n[mcp_servers.navishelper]\ncommand = "navishelper"\n', encoding="utf-8"
        )
        with self.assertRaises(ExecutorError) as raised:
            subject.assert_isolated("codex", home.path, home.env, operator=home.operator)
        self.assertEqual(raised.exception.category, "SANDBOX_UNSAFE")
        self.assertIn("declares MCP servers", str(raised.exception))

    def test_the_executor_only_profile_is_pinned_against_the_reviewed_file(self) -> None:
        expected = {
            "qwen": {
                "model": "qwen3.8-max",
                "effort": "model-mandated",
                "authMode": "ModelStudio Token Plan key",
                "authType": "openai",
                "endpoint": "https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1",
            },
            "codex": {"model": "gpt-6.1-sol", "effort": "high", "authMode": "ChatGPT OAuth", "serviceTier": "priority"},
            "codex-plan": {
                "model": "gpt-6.1-sol",
                "effort": "high",
                "authMode": "ChatGPT OAuth",
                "mode": "advisor",
            },
        }
        self.assertEqual(subject.EXECUTOR_ONLY_PROFILES, expected)
        self.assertEqual(subject.load_executor_profiles(), expected)
        tampered = self.root / "tampered.json"
        tampered.write_text(
            '{"schemaVersion": "avox-external-executors/v1", '
            '"executorProviders": {"codex": {"model": "gpt-4", "effort": "low", "authMode": "ChatGPT OAuth"}}}',
            encoding="utf-8",
        )
        with self.assertRaises(ExecutorError) as raised:
            subject.load_executor_profiles(tampered)
        self.assertEqual(raised.exception.category, "CONFIG_INVALID")

    def test_claude_settings_declare_no_mcp_no_plugins_no_hooks(self) -> None:
        home = self.prepare("claude")
        settings = json.loads((home.path / "settings.json").read_text(encoding="utf-8"))
        self.assertIs(settings["enableAllProjectMcpServers"], False)
        self.assertEqual(settings["enabledPlugins"], {})
        self.assertEqual(settings["hooks"], {})

    def test_kimi_config_carries_the_pinned_effort_and_allows_tools(self) -> None:
        home = self.prepare("kimi")
        config = (home.path / "config.toml").read_text(encoding="utf-8")
        self.assertIn('default_permission_mode = "auto"', config)
        self.assertIn("merge_all_available_skills = false", config)
        self.assertIn("extra_skill_dirs = []", config)
        self.assertIn(f'effort = "{subject.EXPECTED_PROFILES["kimi"]["effort"]}"', config)

    def test_a_home_that_overlaps_the_operator_home_is_refused(self) -> None:
        inside = self.operator / ".claude" / "runtime"
        inside.mkdir(parents=True)
        with self.assertRaises(ExecutorError) as raised:
            subject.assert_isolated("claude", inside, dict(self.env))
        self.assertEqual(raised.exception.category, "SANDBOX_UNSAFE")
        with self.assertRaises(ExecutorError):
            subject.assert_isolated("claude", self.operator, dict(self.env))

    def test_ambient_capability_inside_the_built_home_is_refused(self) -> None:
        """Catches a name this module introduced by mistake, not only a planted one."""
        home = self.prepare("kimi")
        (home.path / "mcp.json").write_text("{}", encoding="utf-8")
        with self.assertRaises(ExecutorError) as raised:
            subject.assert_isolated("kimi", home.path, home.env, operator=home.operator)
        self.assertEqual(raised.exception.category, "SANDBOX_UNSAFE")
        self.assertIn("mcp.json", str(raised.exception))

    def test_qwen_operator_home_negative_control_names_foreign_content(self) -> None:
        with self.assertRaises(ExecutorError) as raised:
            subject.assert_isolated(
                "qwen", self.operator / ".qwen", dict(self.env), operator=self.operator / ".qwen"
            )
        self.assertEqual(raised.exception.category, "SANDBOX_UNSAFE")
        self.assertIn("foreign content", str(raised.exception))
        self.assertTrue(any(name in str(raised.exception) for name in subject.QWEN_FOREIGN_CONTENT_NAMES))

    def test_a_nested_ambient_directory_is_refused(self) -> None:
        home = self.prepare("claude")
        (home.path / "projects" / "plugins").mkdir(parents=True)
        with self.assertRaises(ExecutorError):
            subject.assert_isolated("claude", home.path, home.env, operator=home.operator)

    def test_ambient_capability_written_by_the_redirect_is_refused(self) -> None:
        """The redirect writes .gitconfig and seven cache directories into the
        home after it is built; a check that ran before the redirect never saw
        those files, yet they are part of the home the provider receives."""
        original = subject._redirect_home

        def redirect_and_plant_skills(home: Path, root: Path) -> dict[str, str]:
            overrides = original(home, root)
            (home / "skills").mkdir(exist_ok=True)
            return overrides

        with mock.patch.object(subject, "_redirect_home", redirect_and_plant_skills):
            with self.assertRaises(ExecutorError) as raised:
                self.prepare("claude")
        self.assertEqual(raised.exception.category, "SANDBOX_UNSAFE")
        self.assertIn("skills", str(raised.exception))

    def test_a_missing_credential_fails_as_auth_missing(self) -> None:
        (self.operator / ".claude" / ".credentials.json").unlink()
        with self.assertRaises(ExecutorError) as raised:
            self.prepare("claude")
        self.assertEqual(raised.exception.category, "AUTH_MISSING")

    def test_a_missing_operator_home_fails_as_auth_missing(self) -> None:
        with self.assertRaises(ExecutorError) as raised:
            subject.prepare_isolated_home("kimi", self.runtime, {"USERPROFILE": str(self.root / "absent")})
        self.assertEqual(raised.exception.category, "AUTH_MISSING")

    def test_each_run_gets_a_fresh_home(self) -> None:
        self.prepare("kimi")
        with self.assertRaises(ExecutorError) as raised:
            self.prepare("kimi")
        self.assertEqual(raised.exception.category, "SANDBOX_UNSAFE")

    def test_an_unknown_provider_is_refused(self) -> None:
        with self.assertRaises(ExecutorError) as raised:
            subject.prepare_isolated_home("unknown", self.runtime, dict(self.env))
        self.assertEqual(raised.exception.category, "CONFIG_INVALID")

    def test_the_record_names_the_home_and_every_file_in_it(self) -> None:
        home = self.prepare("kimi")
        record = home.record()
        self.assertEqual(record["schemaVersion"], subject.HOME_SCHEMA)
        self.assertEqual(record["isolatedHome"], str(home.path))
        self.assertIn("KIMI_CODE_HOME", record["environmentOverrides"])
        listed = {entry["path"] for entry in record["files"]}
        self.assertEqual(listed, {"config.toml", "credentials/kimi-code.json", "device_id"})
        for entry in record["files"]:
            self.assertRegex(entry["sha256"], r"^[0-9a-f]{64}$")

    def test_the_record_states_which_child_path_policy_applied(self) -> None:
        """The run record says how the child PATH was derived, never the PATH itself."""
        home = subject.IsolatedHome(
            provider="glm",
            path=self.runtime / "glm-home",
            env={"PATH": "redacted-by-policy"},
            operator=self.operator / ".local" / "share" / "opencode",
            inventory=(),
            child_path=subject.ChildPath(
                value="redacted-by-policy", policy="minimal", before_chars=25500, after_chars=787
            ),
        )
        self.assertEqual(home.record()["childPath"], {"policy": "minimal", "beforeChars": 25500, "afterChars": 787})
        self.assertNotIn("redacted-by-policy", json.dumps(home.record()))

    def test_the_record_omits_child_path_when_none_was_derived(self) -> None:
        home = subject.IsolatedHome(
            provider="glm",
            path=self.runtime / "glm-home",
            env={},
            operator=self.operator / ".local" / "share" / "opencode",
            inventory=(),
        )
        self.assertNotIn("childPath", home.record())

    @unittest.skipUnless(os.name == "nt", "child PATH reduction exists because cmd.exe cannot use a long one")
    def test_an_overlong_operator_path_reaches_the_home_environment_reduced(self) -> None:
        shim_dir = self.root / "npm"
        shim_dir.mkdir()
        fillers = []
        for index in range(140):
            filler = self.root / f"tool-chain-entry-{index:03d}-with-a-long-directory-name"
            filler.mkdir()
            fillers.append(str(filler))
        original = ";".join([str(shim_dir), *fillers])
        home = subject.prepare_isolated_home(
            "kimi", self.runtime, {**self.env, "PATH": original}, shim=shim_dir / "qwen.cmd"
        )
        self.assertLessEqual(len(home.env["PATH"]), 8191)
        self.assertEqual(home.child_path.policy, "minimal")
        self.assertEqual(home.child_path.before_chars, len(original))
        self.assertIn(str(shim_dir), home.env["PATH"].split(";"))
        self.assertEqual(home.record()["childPath"]["policy"], "minimal")

    def test_a_rotated_credential_is_carried_back_to_the_operator_home(self) -> None:
        """A refresh consumes the old token; discarding the replacement logs the operator out."""
        home = self.prepare("kimi")
        source = self.operator / ".kimi-code" / "credentials" / "kimi-code.json"
        (home.path / "credentials" / "kimi-code.json").write_text(
            '{"access_token": "k2", "refresh_token": "r2"}', encoding="utf-8"
        )
        carried = subject.persist_rotated_credentials("kimi", home.path, home.env, operator=home.operator)
        self.assertEqual(carried, ["credentials/kimi-code.json"])
        self.assertEqual(json.loads(source.read_text(encoding="utf-8")), {"access_token": "k2", "refresh_token": "r2"})

    def test_an_unchanged_credential_is_not_written_back(self) -> None:
        home = self.prepare("kimi")
        source = self.operator / ".kimi-code" / "credentials" / "kimi-code.json"
        before = source.read_bytes()
        self.assertEqual(subject.persist_rotated_credentials("kimi", home.path, home.env, operator=home.operator), [])
        self.assertEqual(source.read_bytes(), before)

    def test_a_corrupt_credential_never_overwrites_the_working_one(self) -> None:
        home = self.prepare("kimi")
        source = self.operator / ".kimi-code" / "credentials" / "kimi-code.json"
        before = source.read_bytes()
        (home.path / "credentials" / "kimi-code.json").write_text("{truncated", encoding="utf-8")
        self.assertEqual(subject.persist_rotated_credentials("kimi", home.path, home.env, operator=home.operator), [])
        self.assertEqual(source.read_bytes(), before)

    def test_carrying_back_a_shared_file_preserves_the_other_providers(self) -> None:
        """OpenCode's auth.json holds every login; only our route may be rewritten."""
        home = self.prepare("glm")
        source = self.operator / ".local" / "share" / "opencode" / "auth.json"
        (home.path / "data" / "opencode" / "auth.json").write_text(
            '{"zai-coding-plan": {"key": "z2"}}', encoding="utf-8"
        )
        self.assertEqual(subject.persist_rotated_credentials("glm", home.path, home.env, operator=home.operator), ["auth.json"])
        merged = json.loads(source.read_text(encoding="utf-8"))
        self.assertEqual(merged["zai-coding-plan"], {"key": "z2"})
        self.assertEqual(merged["anthropic"], {"key": "a"})
        self.assertEqual(merged["openai"], {"key": "o"})

    def test_the_launch_home_is_redirected_away_from_the_operator_directory(self) -> None:
        """Per-provider variables move a CLI's own config; they do not move what it
        reads from the home directory by convention. OpenCode was observed listing
        thirty-odd of the operator's personal skills from an otherwise isolated home."""
        home = self.prepare("glm")
        self.assertEqual(home.env["HOME"], str(home.path / "userprofile"))
        self.assertEqual(home.env["USERPROFILE"], str(home.path / "userprofile"))
        self.assertNotEqual(home.env["HOME"], str(self.operator))
        self.assertTrue((home.path / "userprofile").is_dir())
        self.assertEqual(home.operator, self.operator / ".local" / "share" / "opencode")

    def test_dotnet_and_nuget_caches_are_redirected_into_created_home_directories(self) -> None:
        isolated = self.root / "isolated"
        overrides = subject._redirect_home(isolated, self.operator)
        expected = {
            "APPDATA": isolated / "userprofile" / "AppData" / "Roaming",
            "LOCALAPPDATA": isolated / "userprofile" / "AppData" / "Local",
            "TEMP": isolated / "userprofile" / "AppData" / "Local" / "Temp",
            "TMP": isolated / "userprofile" / "AppData" / "Local" / "Temp",
            "DOTNET_CLI_HOME": isolated / "userprofile" / ".dotnet",
            "NUGET_PACKAGES": isolated / "userprofile" / ".nuget" / "packages",
            "NUGET_HTTP_CACHE_PATH": isolated / "userprofile" / ".nuget" / "http-cache",
            "NUGET_PLUGINS_CACHE_PATH": isolated / "userprofile" / ".nuget" / "plugins-cache",
            "NUGET_SCRATCH": isolated / "userprofile" / ".nuget" / "scratch",
        }
        for variable, directory in expected.items():
            with self.subTest(variable=variable):
                self.assertEqual(overrides[variable], str(directory))
                self.assertTrue(directory.is_dir())

    def test_only_the_commit_identity_follows_the_redirected_home(self) -> None:
        """A .gitconfig copied whole would carry include.path, core.hooksPath and a
        credential helper back through the boundary the redirect just built."""
        (self.operator / ".gitconfig").write_text(
            "[user]\n\tname = Owner\n\temail = owner@example.invalid\n"
            "[include]\n\tpath = ~/secrets.gitconfig\n"
            "[core]\n\thooksPath = ~/evil-hooks\n"
            "[credential]\n\thelper = \"!sh -c 'echo leak'\"\n",
            encoding="utf-8",
        )
        home = self.prepare("kimi")
        written = (home.path / "userprofile" / ".gitconfig").read_text(encoding="utf-8")
        self.assertIn("name = Owner", written)
        self.assertIn("email = owner@example.invalid", written)
        for forbidden in ("include", "hooksPath", "helper", "secrets.gitconfig", "evil-hooks"):
            self.assertNotIn(forbidden, written)

    def test_whatever_git_config_is_written_carries_nothing_but_the_identity(self) -> None:
        home = self.prepare("kimi")
        written = home.path / "userprofile" / ".gitconfig"
        if written.exists():
            text = written.read_text(encoding="utf-8")
            self.assertTrue(text.startswith("[user]"))
            self.assertNotIn("include", text)
            self.assertNotIn("hooksPath", text)

    def test_the_record_names_environment_variables_but_never_their_values(self) -> None:
        """The record is written to a provenance artifact, so a value in it would leak."""
        home = subject.IsolatedHome(
            provider="glm",
            path=self.runtime / "glm-home",
            env={"ZAI_API_KEY": "secret-value", "XDG_DATA_HOME": "/x"},
            operator=self.operator / ".local" / "share" / "opencode",
            inventory=(("data/opencode/auth.json", "0" * 64),),
        )
        self.assertNotIn("secret-value", json.dumps(home.record()))
        self.assertEqual(home.record()["environmentOverrides"], ["XDG_DATA_HOME", "ZAI_API_KEY"])


if __name__ == "__main__":
    unittest.main()
