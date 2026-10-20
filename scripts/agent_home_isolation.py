#!/usr/bin/env python3
"""Isolated provider homes for executor-mode runs.

Executor mode (AGENTS.md section 7) lets an external agent CLI run with write
access in a git worktree. The rule is that such a run gets a home holding only
the credential and the fixed model route: no MCP declarations, hooks, skills,
plugins or alternate providers from the operator's own CLI profile. This module
builds that home and refuses to return one that does not meet the rule.

The reviewer-mode wrapper in external_executor.py keeps its own, narrower
isolation. The path guards are imported from executor_primitives.py rather
than reimplemented so the two modes cannot drift apart on what counts as an
unsafe path.

Executor mode deliberately copies the provider's real credential, refresh token
included. The reviewer sandbox strips the refresh token, which caps a Kimi run
at the 900-second access-token lifetime; that is correct for a single advisory
call and unworkable for a task that builds and tests.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import shutil
import subprocess
from dataclasses import dataclass
from functools import partial
from pathlib import Path
from xml.sax.saxutils import escape as xml_escape

from scripts.executor_worktree_acl import grant_runtime_access

from scripts.executor_primitives import (
    CODEX_PROVIDERS,
    EXPECTED_PROFILES,
    GLM_CREDENTIAL_ENV_NAMES,
    GLM_PROVIDERS,
    KIMI_ISOLATED_CONFIG_TEMPLATE,
    KIMI_SUPPORTED_EFFORTS,
    MAX_FILE_BYTES,
    PROVIDERS,
    ChildPath,
    ExecutorError,
    _canonical_path,
    _durable_replace,
    _repo_root,
    _reject_reparse_path,
    _sanitize_environment,
)

HOME_SCHEMA = "avox-isolated-home/v1"

# An MCP declaration written inside a configuration file rather than in a
# file of its own, which a name-based check cannot see.
MCP_IN_CONFIG = re.compile(r"^\s*\[+\s*mcp[_.]?servers?\b", re.IGNORECASE | re.MULTILINE)

# Names that carry ambient capability into a CLI session. None may exist in a
# home this module returns, whatever wrote it. The check runs on the finished
# home, so it also catches a name this module itself introduced by mistake, not
# only one an operator planted.
AMBIENT_CAPABILITY_NAMES = frozenset(
    {
        ".mcp.json",
        ".opencode",
        "agents",
        "commands",
        "extensions",
        "hooks",
        "mcp",
        "mcp.json",
        "mcp_servers.json",
        "opencode.json",
        "plugin",
        "plugins",
        "settings.local.json",
        "skill",
        "skills",
        "subagents",
    }
)

# Executor mode needs the tools the reviewer sandbox forbids, so the permission
# mode differs from the reviewer template's "manual". Everything that pulls in
# ambient capability - skill merging, extra skill dirs, telemetry - stays off.
KIMI_EXECUTOR_CONFIG_TEMPLATE = KIMI_ISOLATED_CONFIG_TEMPLATE.replace(
    'default_permission_mode = "manual"', 'default_permission_mode = "auto"'
)

# OpenCode keeps every provider the operator ever signed into in one auth.json.
# Copying it whole would hand the executor credentials for routes it has no
# business using, so only the Z.AI entries are carried across. The auth entry is
# keyed "zai" while the model route reads "zai-coding-plan/glm-5.3"; both spellings
# are accepted because the two names are not the same thing and installs differ.
OPENCODE_CREDENTIAL_KEYS = ("zai", "zai-coding-plan")

# Qwen keeps the static ModelStudio plan key beside every customization in one
# settings file. Rebuild that file from these reviewed pieces instead of
# copying the operator's extensions, permissions, memories, or other routes.
QWEN_PLAN_KEY = "BAILIAN_TOKEN_PLAN_API_KEY"
QWEN_SETTINGS_KEYS = frozenset({"env", "model", "modelProviders"})
QWEN_FOREIGN_CONTENT_NAMES = frozenset(
    {"extensions", "skills", "memories", "sessions", "projects", "file-history"}
)

# The only git settings an executor needs. A .gitconfig copied whole can carry
# include.path, core.hooksPath and credential helpers, which would walk straight
# back through the HOME boundary and run the operator's hooks inside the run.
GIT_IDENTITY_KEYS = ("user.name", "user.email")

# Codex and Qwen are executor-only: the reviewer wrapper has no parser or
# bounded-prompt path for them, so their profiles live beside EXPECTED_PROFILES
# rather than inside it. Each is still pinned in three places - here, in
# config/external-executors.json, and in the tests - so a single-file edit is
# rejected the same way the others are.
EXECUTOR_ONLY_PROFILES = {
    "qwen": {
        "model": "qwen3.8-max",
        "effort": "model-mandated",
        "authMode": "ModelStudio Token Plan key",
        "authType": "openai",
        "endpoint": "https://token-plan.ap-southeast-1.maas.aliyuncs.com/compatible-mode/v1",
    },
    # serviceTier "priority" is Codex Fast mode (owner request 2026-09-28; models_cache: gpt-6.1-sol "Fast", 2x speed).
    "codex": {"model": "gpt-6.1-sol", "effort": "high", "authMode": "ChatGPT OAuth", "serviceTier": "priority"},
    "codex-plan": {
        "model": "gpt-6.1-sol",
        "effort": "high",
        "authMode": "ChatGPT OAuth",
        "mode": "advisor",
    },
}
EXECUTOR_PROFILES = {**EXPECTED_PROFILES, **EXECUTOR_ONLY_PROFILES}
EXECUTOR_PROVIDERS = tuple(EXECUTOR_PROFILES)

# Only the model route and the sandbox mode. Codex reads MCP servers from
# [mcp_servers.*] in this same file, and the operator's copy declares six of
# them. --approve-for-me runs the workspace-write sandbox, and without a
# [windows] section the run took Codex's default mode rather than the
# operator's: the elevated mode grants its sandbox accounts access to the whole
# profile, which is how a profile was lost on 2026-09-24. codex 0.156.0 accepts
# only "elevated" and "unelevated".
# Codex ships account-level features that no config.toml declares. `apps` loads the signed-in ChatGPT account's
# connectors as the codex_apps MCP server (Outlook send_email, Notion pages, sites deploy, OpenAI platform keys,
# parental-control settings), and the browser, computer-use and plugin features add tools of their own. An isolated
# CODEX_HOME cannot remove what arrives with the login, so they are switched off twice: in this config.toml and on the
# command line (build_argv). In codex 0.158 `tools.web_search=false` no longer removes the web tool; the top-level
# `web_search = "disabled"` does. Found and probed from the HetengSpec session on 2026-09-29: without these the
# executor listed codex_apps, creative_production_mcp and a web tool; with them, no MCP server and no web tool.
CODEX_DISABLED_FEATURES = (
    "apps",
    "browser_use",
    "browser_use_external",
    "browser_use_full_cdp_access",
    "computer_use",
    "plugins",
    "remote_plugin",
    "plugin_sharing",
    "in_app_browser",
    "image_generation",
    "skill_mcp_dependency_install",
    "tool_suggest",
    "browser_annotation_api",
    "api_key_cyber_access_programs",
    "cli_daybreak",
)

# Every feature codex 0.158.0 still reports enabled in the isolated home with the switches above, reviewed on
# 2026-09-29: none loads account connectors, a browser, computer use, plugins or the web. The pre-run check refuses
# any enabled feature outside this set, so a new CLI release stops the executor until its new features are reviewed
# here (docs/EXECUTOR_SETUP.md, "Upgrading the CLI").
# The CLI versions whose tool surface was reviewed with these switches (2026-09-29: no MCP server, no web tool,
# probe from the HetengSpec session; 0.159.0 on 2026-09-29: the same enabled set, every disabled feature listed and off,
# probe again). `codex features list` cannot show whether a renamed or dropped web switch still
# works, so any other version refuses the executor until it is reviewed and added here in the same PR as its feature
# review (docs/EXECUTOR_SETUP.md, "Upgrading the CLI").
# 0.161.0 (2026-10-08): newly enabled api_key_model_discovery and in_app_voice reviewed harmless;
# browser_annotation_api, a browser surface, is switched off above.
# 0.162.1 (2026-10-10): newly enabled instant_interrupt (turn interruption) and ultrafast_mode (faster serving
# for the same model) reviewed harmless: no account, browser, connector, plugin or web surface.
# 0.163.0 (2026-10-17): newly enabled code_mode_interrupt reviewed harmless (interrupts code_mode, which stays off);
# api_key_cyber_access_programs (account-level API key programme) and cli_daybreak (undocumented) are switched off
# above because neither could be shown harmless. unified_exec_zsh_fork is now reported as removed.
CODEX_REVIEWED_VERSIONS = frozenset({"codex-cli 0.158.0", "codex-cli 0.159.0", "codex-cli 0.159.2", "codex-cli 0.160.0", "codex-cli 0.161.0", "codex-cli 0.162.1", "codex-cli 0.163.0"})

_CODEX_RELEASE = re.compile(r"codex-cli (\d+\.\d+)\.(\d+)")


def codex_version_is_reviewed(version_line: str) -> bool:
    """A reviewed CLI version, or a plain patch release of a reviewed minor (0.159.0 reviewed -> 0.159.7 accepted).

    Owner, 2026-09-30: a patch release no longer stops the executor. check_codex_isolation still proves the enabled
    set and every disabled switch in the isolated home before each run; what it cannot see is a new web tool with no
    feature switch at all, and the owner accepted that risk for patch releases. A new minor, a pre-release or an
    unparsable line still needs the review in docs/EXECUTOR_SETUP.md ("Upgrading the CLI").
    """
    text = (version_line or "").strip()
    if text in CODEX_REVIEWED_VERSIONS:
        return True
    release = _CODEX_RELEASE.fullmatch(text)
    if not release:
        return False
    # A patch counts only from the lowest reviewed patch of its minor upwards: 0.162.1 reviewed admits 0.162.4,
    # never the older 0.162.0 (#1076 review).
    reviewed_patches = [int(match.group(2)) for match in map(_CODEX_RELEASE.fullmatch, CODEX_REVIEWED_VERSIONS)
                        if match and match.group(1) == release.group(1)]
    return bool(reviewed_patches) and int(release.group(2)) >= min(reviewed_patches)

CODEX_REVIEWED_ENABLED_FEATURES = frozenset({
    "api_key_model_discovery", "auth_elicitation", "code_mode_host", "code_mode_interrupt", "collaboration_modes", "compaction_image_budget", "content_item_kinds",
    "daemon_auto_start", "enable_request_compression", "fast_mode", "goals", "guardian_approval",
    "guardian_reuse_parent_compaction", "hooks", "in_app_chat", "in_app_dictation", "in_app_local_automation",
    "in_app_updates", "in_app_voice", "instant_interrupt", "item_ids", "mentions_v2", "multi_agent", "realtime_conversation", "resize_all_images",
    "secret_auth_storage", "shell_snapshot", "shell_tool", "skill_search", "sleep_tool", "sqlite", "steer",
    "system_proxy_fallback", "terminal_resize_reflow", "tool_call_mcp_elicitation", "tool_search_always_defer_mcp_tools",
    "tui_app_server", "ultrafast_mode", "unbounded_connection_retries", "unified_exec", "unified_exec_tty", "unified_exec_zsh_fork",
    "view_image", "workspace_dependencies", "worktrees", "write_stdin_approval",
})

CODEX_EXECUTOR_CONFIG_TEMPLATE = (
    'model = "{model}"\n'
    'model_reasoning_effort = "{effort}"\n'
    'web_search = "disabled"\n'
    "\n"
    "[windows]\n"
    'sandbox = "unelevated"\n'
    "\n"
    "[features]\n"
    + "".join(f"{feature} = false\n" for feature in CODEX_DISABLED_FEATURES)
)

CLAUDE_EXECUTOR_SETTINGS = {
    "enableAllProjectMcpServers": False,
    "enabledPlugins": {},
    "hooks": {},
}


# Which files inside a built home may hold a rotated credential, where they came
# from, and which top-level keys are ours to write back. A provider refreshes by
# consuming its refresh token and issuing a new one; the refresh happens inside
# the disposable home, so discarding that home discards the replacement and
# leaves the operator holding a token the provider has already retired.
#
# Observed on Kimi while building this: the first executor run succeeded, the
# second failed with "The provided authorization grant is invalid", and the
# operator's credential file had not been written since long before either run.
ROTATING_CREDENTIALS: dict[str, tuple[tuple[str, str, tuple[str, ...] | None], ...]] = {
    "claude": ((".credentials.json", ".credentials.json", None),),
    "kimi": (("credentials/kimi-code.json", "credentials/kimi-code.json", None),),
    "glm": (("data/opencode/auth.json", "auth.json", OPENCODE_CREDENTIAL_KEYS),),
    "glm-flash": (("data/opencode/auth.json", "auth.json", OPENCODE_CREDENTIAL_KEYS),),
    # The ModelStudio plan key is static. Nothing from the disposable home is
    # ever carried back to the operator's settings file.
    "qwen": (),
    "codex": (("auth.json", "auth.json", None),),
    "codex-plan": (("auth.json", "auth.json", None),),
}


@dataclass(frozen=True)
class IsolatedHome:
    """A provider home built for one executor run."""

    provider: str
    path: Path
    operator: Path
    env: dict[str, str]
    inventory: tuple[tuple[str, str], ...]
    child_path: ChildPath | None = None

    def record(self) -> dict[str, object]:
        """The provenance fragment naming this home and everything in it."""
        record: dict[str, object] = {
            "schemaVersion": HOME_SCHEMA,
            "provider": self.provider,
            "isolatedHome": str(self.path),
            "environmentOverrides": sorted(self.env),
            "files": [{"path": name, "sha256": digest} for name, digest in self.inventory],
        }
        if self.child_path is not None:
            # How the child PATH was derived, in counts; the value itself is an
            # environment value, and the record never carries those.
            record["childPath"] = self.child_path.record()
        return record


def user_root(env: dict[str, str]) -> Path:
    """The operator's home directory as the launched CLI would resolve it."""
    base = env.get("USERPROFILE") if os.name == "nt" else env.get("HOME")
    base = base or env.get("HOME") or env.get("USERPROFILE")
    if not base:
        raise ExecutorError("AUTH_MISSING", "The operator home directory cannot be resolved")
    return Path(base).expanduser().absolute()


def load_executor_profiles(path: Path | None = None) -> dict[str, object]:
    """Check the executor-only profiles against the reviewed file.

    The same anti-substitution guard the reviewer wrapper has: the route is
    pinned here, in config/external-executors.json and in the tests, so editing
    one of them is rejected by the others rather than silently taking effect.
    """
    source = path or (_repo_root() / "config" / "external-executors.json")
    try:
        data = json.loads(source.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ExecutorError("CONFIG_INVALID", f"Cannot load executor config: {exc}") from exc
    declared = data.get("executorProviders") if isinstance(data, dict) else None
    if declared != EXECUTOR_ONLY_PROFILES:
        raise ExecutorError("CONFIG_INVALID", "Executor-only profiles do not match the reviewed fixed routes")
    return declared


def operator_home(provider: str, env: dict[str, str]) -> Path:
    """The provider's normal home - the one executor mode must not run against."""
    root = user_root(env)
    if provider == "claude":
        return root / ".claude"
    if provider == "kimi":
        return root / ".kimi-code"
    if provider == "qwen":
        return root / ".qwen"
    if provider in ("codex", "codex-plan"):
        return root / ".codex"
    return root / ".local" / "share" / "opencode"


def _copy_credential(source: Path, target: Path, *, label: str) -> None:
    if not source.exists():
        raise ExecutorError("AUTH_MISSING", f"{label} is unavailable; sign in with the provider CLI first")
    _reject_reparse_path(source.parent, source, category="AUTH_MISSING", label=label)
    try:
        if not source.is_file() or source.stat().st_size > MAX_FILE_BYTES:
            raise ExecutorError("AUTH_MISSING", f"{label} is invalid or exceeds the bounded limit")
        target.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
        shutil.copyfile(source, target)
        if os.name != "nt":
            target.chmod(0o600)
    except OSError as exc:
        raise ExecutorError("AUTH_MISSING", f"{label} could not be isolated") from exc


def _copy_selected_credential(
    source: Path, target: Path, keys: tuple[str, ...], *, label: str, required: bool = True
) -> None:
    """Copy only the named entries of a multi-provider credential file."""
    if not source.exists():
        if not required:
            return
        raise ExecutorError("AUTH_MISSING", f"{label} is unavailable; sign in with the provider CLI first")
    _reject_reparse_path(source.parent, source, category="AUTH_MISSING", label=label)
    try:
        raw = source.read_bytes()
        if len(raw) > MAX_FILE_BYTES:
            raise ExecutorError("AUTH_MISSING", f"{label} exceeds the bounded limit")
        payload = json.loads(raw.decode("utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ExecutorError("AUTH_MISSING", f"{label} is invalid") from exc
    if not isinstance(payload, dict):
        raise ExecutorError("AUTH_MISSING", f"{label} must be a JSON object")
    selected = {key: payload[key] for key in keys if key in payload}
    if not selected:
        if not required:
            return
        raise ExecutorError("AUTH_MISSING", f"{label} holds none of: " + ", ".join(keys))
    _write(target, json.dumps(selected, separators=(",", ":")), label=label)


def _write(path: Path, text: str, *, label: str) -> None:
    try:
        path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8", newline="\n")
        if os.name != "nt":
            path.chmod(0o600)
    except OSError as exc:
        raise ExecutorError("CONFIG_INVALID", f"{label} could not be written") from exc


def _kimi_effort() -> str:
    effort = str(EXPECTED_PROFILES["kimi"]["effort"])
    if effort not in KIMI_SUPPORTED_EFFORTS:
        raise ExecutorError(
            "CONFIG_INVALID", "Kimi effort must be one of " + ", ".join(sorted(KIMI_SUPPORTED_EFFORTS))
        )
    return effort


def _build_claude(home: Path, source: Path, env: dict[str, str]) -> dict[str, str]:
    _copy_credential(source / ".credentials.json", home / ".credentials.json", label="Claude OAuth credential")
    _write(home / "settings.json", json.dumps(CLAUDE_EXECUTOR_SETTINGS, indent=2), label="Claude executor settings")
    return {"CLAUDE_CONFIG_DIR": str(home)}


def _build_kimi(home: Path, source: Path, env: dict[str, str]) -> dict[str, str]:
    _write(
        home / "config.toml",
        KIMI_EXECUTOR_CONFIG_TEMPLATE.format(effort=_kimi_effort()),
        label="Kimi executor configuration",
    )
    _copy_credential(
        source / "credentials" / "kimi-code.json",
        home / "credentials" / "kimi-code.json",
        label="Kimi managed OAuth credential",
    )
    device_id = source / "device_id"
    if device_id.exists():
        _copy_credential(device_id, home / "device_id", label="Kimi device identity")
    return {"KIMI_CODE_HOME": str(home)}


def _build_opencode(home: Path, source: Path, env: dict[str, str]) -> dict[str, str]:
    # The Z.AI route can be authenticated two ways: an entry in auth.json, or an
    # API key in the environment, which _sanitized_environment already carries.
    # Demanding the file when the environment holds the key would refuse a setup
    # that works; accepting neither would launch a run that cannot authenticate.
    _copy_selected_credential(
        source / "auth.json",
        home / "data" / "opencode" / "auth.json",
        OPENCODE_CREDENTIAL_KEYS,
        label="OpenCode credential",
        required=not any(env.get(name) for name in GLM_CREDENTIAL_ENV_NAMES),
    )
    for relative in ("config", "cache", "state"):
        (home / relative).mkdir(mode=0o700, parents=True, exist_ok=True)
    return {
        "XDG_CONFIG_HOME": str(home / "config"),
        "XDG_DATA_HOME": str(home / "data"),
        "XDG_CACHE_HOME": str(home / "cache"),
        "XDG_STATE_HOME": str(home / "state"),
    }


def _build_qwen(home: Path, source: Path, env: dict[str, str]) -> dict[str, str]:
    """Rebuild Qwen's settings with one model route and the static plan key."""
    settings = source / "settings.json"
    if not settings.exists():
        raise ExecutorError("AUTH_MISSING", "Qwen settings are unavailable; sign in with the provider CLI first")
    _reject_reparse_path(source, settings, category="AUTH_MISSING", label="Qwen settings")
    try:
        raw = settings.read_bytes()
        if len(raw) > MAX_FILE_BYTES:
            raise ExecutorError("AUTH_MISSING", "Qwen settings exceed the bounded limit")
        payload = json.loads(raw.decode("utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ExecutorError("AUTH_MISSING", "Qwen settings are unavailable or invalid") from exc
    if not isinstance(payload, dict):
        raise ExecutorError("AUTH_MISSING", "Qwen settings must be a JSON object")

    profile = EXECUTOR_ONLY_PROFILES["qwen"]
    security = payload.get("security")
    auth = security.get("auth") if isinstance(security, dict) else None
    selected_type = auth.get("selectedType") if isinstance(auth, dict) else None
    if selected_type != profile["authType"]:
        found = repr(selected_type) if isinstance(selected_type, str) else "missing"
        raise ExecutorError(
            "AUTH_MISSING", f"Qwen auth type must be {profile['authType']!r}; found {found}"
        )

    source_env = payload.get("env")
    plan_key = source_env.get(QWEN_PLAN_KEY) if isinstance(source_env, dict) else None
    if not isinstance(plan_key, str) or not plan_key:
        raise ExecutorError("AUTH_MISSING", "Qwen settings do not hold the ModelStudio Token Plan key")

    model_name, endpoint = str(profile["model"]), str(profile["endpoint"])
    model = payload.get("model")
    if not isinstance(model, dict) or model.get("name") != model_name or model.get("baseUrl") != endpoint:
        raise ExecutorError("CONFIG_INVALID", "Qwen's selected model does not match the reviewed fixed route")
    providers = payload.get("modelProviders")
    routes = providers.get("openai") if isinstance(providers, dict) else None
    if not isinstance(routes, list):
        raise ExecutorError("CONFIG_INVALID", "Qwen's reviewed OpenAI-compatible model route is unavailable")
    matching = [
        route
        for route in routes
        if isinstance(route, dict)
        and route.get("id") == model_name
        and route.get("baseUrl") == endpoint
        and route.get("envKey") == QWEN_PLAN_KEY
    ]
    if len(matching) != 1:
        raise ExecutorError("CONFIG_INVALID", "Qwen's reviewed model route is missing or ambiguous")
    route = matching[0]
    selected_route = {
        key: route[key]
        for key in ("id", "name", "capabilities", "baseUrl", "envKey", "generationConfig")
        if key in route
    }
    isolated = {
        "env": {QWEN_PLAN_KEY: plan_key},
        "modelProviders": {"openai": [selected_route]},
        "model": {"name": model_name, "baseUrl": endpoint},
    }
    target = home / "userprofile" / ".qwen" / "settings.json"
    expected_root = _canonical_path(Path(env["USERPROFILE"]))
    if _canonical_path(target.parent.parent) != expected_root:
        raise ExecutorError("SANDBOX_UNSAFE", "Qwen settings are not under the redirected executor home")
    _write(target, json.dumps(isolated, indent=2), label="Qwen executor settings")
    return {
        "OPENAI_API_KEY": plan_key,
        "QWEN_CODE_SUPPRESS_YOLO_WARNING": "1",
    }


def _assert_codex_uses_the_declared_auth_mode(source: Path) -> None:
    """Refuse an API key where the profile declares a subscription login.

    Both live at the same auth.json path. A file-type and size check cannot tell
    them apart, so an operator who had signed in with an API key would have had
    the executor quietly spend it while the pinned profile said ChatGPT OAuth -
    a different account, a different bill, and not the route that was reviewed.
    """
    try:
        payload = json.loads(source.read_bytes().decode("utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise ExecutorError("AUTH_MISSING", "Codex credential is invalid") from exc
    if not isinstance(payload, dict):
        raise ExecutorError("AUTH_MISSING", "Codex credential must be a JSON object")
    if payload.get("OPENAI_API_KEY"):
        raise ExecutorError(
            "AUTH_MISSING",
            "Codex is signed in with an API key; the reviewed profile declares ChatGPT OAuth",
        )
    tokens = payload.get("tokens")
    if str(payload.get("auth_mode", "")).lower() != "chatgpt" or not isinstance(tokens, dict) or not tokens:
        raise ExecutorError("AUTH_MISSING", "Codex is not signed in with ChatGPT OAuth; run codex login")


def _build_codex(provider: str, home: Path, source: Path, env: dict[str, str]) -> dict[str, str]:
    profile = EXECUTOR_ONLY_PROFILES[provider]
    _write(
        home / "config.toml",
        CODEX_EXECUTOR_CONFIG_TEMPLATE.format(model=profile["model"], effort=profile["effort"]),
        label="Codex executor configuration",
    )
    credential = source / "auth.json"
    if not credential.exists():
        raise ExecutorError("AUTH_MISSING", "Codex credential is unavailable; run codex login first")
    _assert_codex_uses_the_declared_auth_mode(credential)
    _copy_credential(credential, home / "auth.json", label="Codex OAuth credential")
    return {"CODEX_HOME": str(home)}


_BUILDERS = {
    "claude": _build_claude,
    "kimi": _build_kimi,
    "glm": _build_opencode,
    "glm-flash": _build_opencode,
    "qwen": _build_qwen,
    "codex": partial(_build_codex, "codex"),
    "codex-plan": partial(_build_codex, "codex-plan"),
}


def _git_identity(root: Path) -> str:
    """The operator's commit identity, rebuilt rather than copied.

    Asking git for the two values it needs, instead of copying .gitconfig,
    means include directives, hooks paths and credential helpers stay outside
    the run. A value is taken as its first line only, so a crafted setting
    cannot append sections of its own.
    """
    env = {name: value for name, value in os.environ.items() if not name.upper().startswith("GIT_")}
    env.update({"HOME": str(root), "USERPROFILE": str(root)})
    fields: list[str] = []
    for key in GIT_IDENTITY_KEYS:
        try:
            result = subprocess.run(
                ["git", "config", "--global", "--get", key],
                env=env,
                capture_output=True,
                text=True,
                timeout=30,
                check=False,
            )
        except (OSError, subprocess.SubprocessError):
            continue
        value = result.stdout.splitlines()[0].strip() if result.returncode == 0 and result.stdout.strip() else ""
        if value:
            fields.append("\t{} = {}".format(key.split(".", 1)[1], value))
    if not fields:
        return ""
    return "[user]\n" + "\n".join(fields) + "\n"


NUGET_CONFIG_TEMPLATE = (
    '<?xml version="1.0" encoding="utf-8"?>\n<configuration>\n'
    "  <packageSources>\n    <clear />\n  </packageSources>\n{fallback}</configuration>\n"
)


def _nuget_config(operator_root: Path) -> str:
    """No package source, and the operator's package folder as a read-only fallback (#1274).

    The sandbox has no network, so restore must find every package locally. The file is generated, never copied:
    the operator's own NuGet.Config may carry sources with credentials.
    """
    folder = Path(os.environ.get("NUGET_PACKAGES") or operator_root / ".nuget" / "packages")
    if not folder.is_dir():
        return NUGET_CONFIG_TEMPLATE.format(fallback="")
    value = xml_escape(str(folder), {'"': "&quot;"})
    return NUGET_CONFIG_TEMPLATE.format(
        fallback=f'  <fallbackPackageFolders>\n    <add key="operator" value="{value}" />\n  </fallbackPackageFolders>\n'
    )


def _redirect_home(home: Path, root: Path, work: Path | None = None) -> dict[str, str]:
    """Point the run's HOME at a fresh directory, so nothing is found by convention.

    The per-provider variables move a CLI's own config. They do not move what a
    CLI reads from the user's home by convention. OpenCode was observed listing
    thirty-odd of the operator's personal skills - Notion, spreadsheets, a daily
    brief - from an otherwise fully isolated home, because it resolves those
    against os.homedir() rather than its own data directory. Redirecting HOME
    reduced that to the one skill the repository itself ships, which is the
    executor's legitimate working material.

    git config is copied in, or the executor cannot make a commit.

    For Codex (#1274) the profile moves out of CODEX_HOME into `work`, a sibling that is TEMP itself. The
    sandbox makes TEMP a writable root and its ACE is inherited by everything below; a folder outside a
    writable root - DOTNET_CLI_HOME, the NuGet caches, APPDATA with NuGet.Config - stays denied however it
    is granted. `work` also carries an explicit Modify ACE for the operator, which the token's ordinary
    SIDs need (see executor_worktree_acl). Its subfolders are created without mode 0o700: on Windows that
    mode writes a protected DACL and would cut them off from both ACEs. CODEX_HOME, with the credential,
    gets neither and stays unreadable from the sandbox.
    """
    if work is None:
        redirected = home / "userprofile"
        redirected.mkdir(mode=0o700, parents=True, exist_ok=True)
        mode = 0o700
    else:
        work.mkdir(mode=0o700)
        grant_runtime_access(work)
        redirected = work / "userprofile"
        redirected.mkdir()
        mode = 0o777
    identity = _git_identity(root)
    if identity:
        _write(redirected / ".gitconfig", identity, label="Executor git identity")
    appdata = redirected / "AppData" / "Roaming"
    local_appdata = redirected / "AppData" / "Local"
    dotnet_home = redirected / ".dotnet"
    nuget_home = redirected / ".nuget"
    # Codex 0.156.0 (resolved_permissions.rs, windows_temp_env_roots) makes
    # TEMP and TMP writable roots and grants them an inheritable write ACE for
    # a capability SID minted per CODEX_HOME. With the operator's TEMP that
    # left a new orphaned ACE on the real AppData\Local\Temp every run.
    temp = local_appdata / "Temp" if work is None else work
    cache_paths = {
        "APPDATA": appdata,
        "LOCALAPPDATA": local_appdata,
        "TEMP": temp,
        "TMP": temp,
        "DOTNET_CLI_HOME": dotnet_home,
        "NUGET_PACKAGES": nuget_home / "packages",
        "NUGET_HTTP_CACHE_PATH": nuget_home / "http-cache",
        "NUGET_PLUGINS_CACHE_PATH": nuget_home / "plugins-cache",
        "NUGET_SCRATCH": nuget_home / "scratch",
    }
    for path in cache_paths.values():
        path.mkdir(mode=mode, parents=True, exist_ok=True)
    if work is not None:
        # Created here first: _write would make a missing parent with mode 0o700, cut off from the sandbox's ACE.
        (appdata / "NuGet").mkdir(exist_ok=True)
        _write(appdata / "NuGet" / "NuGet.Config", _nuget_config(root), label="Executor NuGet configuration")
    overrides = {
        "HOME": str(redirected),
        "USERPROFILE": str(redirected),
        **{name: str(path) for name, path in cache_paths.items()},
    }
    if os.name == "nt":
        drive, _, tail = str(redirected).partition(":")
        overrides["HOMEDRIVE"] = f"{drive}:"
        overrides["HOMEPATH"] = tail
    return overrides


def assert_isolated(provider: str, home: Path, env: dict[str, str], operator: Path | None = None) -> None:
    """Refuse a home that is the operator's own, or that carries ambient capability.

    `operator` is passed explicitly once the launch environment has had HOME
    redirected, because resolving it from that environment would point back
    inside the isolated home and the overlap check would fire on itself.
    """
    if provider == "qwen":
        # Check content before overlap so the negative control against the real
        # ~/.qwen names what would cross the boundary, not merely where it sits.
        for current, directories, files in os.walk(home, topdown=True, followlinks=False):
            for name in [*directories, *files]:
                if name.lower() in QWEN_FOREIGN_CONTENT_NAMES:
                    relative = Path(current, name).relative_to(home)
                    raise ExecutorError(
                        "SANDBOX_UNSAFE", f"The qwen executor home carries foreign content: {relative}"
                    )
        settings = home / "userprofile" / ".qwen" / "settings.json"
        try:
            payload = json.loads(settings.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as exc:
            raise ExecutorError("SANDBOX_UNSAFE", "The qwen executor settings are unavailable or invalid") from exc
        profile = EXECUTOR_ONLY_PROFILES["qwen"]
        routes = payload.get("modelProviders", {}).get("openai") if isinstance(payload, dict) else None
        valid_route = (
            isinstance(routes, list)
            and len(routes) == 1
            and isinstance(routes[0], dict)
            and routes[0].get("id") == profile["model"]
            and routes[0].get("baseUrl") == profile["endpoint"]
            and routes[0].get("envKey") == QWEN_PLAN_KEY
        )
        valid_key = (
            isinstance(payload, dict)
            and isinstance(payload.get("env"), dict)
            and set(payload["env"]) == {QWEN_PLAN_KEY}
            and isinstance(payload["env"][QWEN_PLAN_KEY], str)
            and bool(payload["env"][QWEN_PLAN_KEY])
        )
        if (
            not isinstance(payload, dict)
            or set(payload) != QWEN_SETTINGS_KEYS
            or payload.get("model")
            != {"name": profile["model"], "baseUrl": profile["endpoint"]}
            or set(payload.get("modelProviders", {})) != {"openai"}
            or not valid_route
            or not valid_key
        ):
            raise ExecutorError("SANDBOX_UNSAFE", "The qwen executor settings contain an unreviewed route or content")

    normal = _canonical_path(operator if operator is not None else operator_home(provider, env))
    candidate = _canonical_path(home)
    if candidate == normal or normal in candidate.parents or candidate in normal.parents:
        raise ExecutorError("SANDBOX_UNSAFE", f"The {provider} executor home overlaps the operator's own home")
    for current, directories, files in os.walk(home, topdown=True, followlinks=False):
        for name in [*directories, *files]:
            if name.lower() in AMBIENT_CAPABILITY_NAMES:
                relative = Path(current, name).relative_to(home)
                raise ExecutorError(
                    "SANDBOX_UNSAFE", f"The {provider} executor home carries ambient capability: {relative}"
                )
            # A name check is not enough for a CLI that declares its MCP servers
            # inside its own configuration file rather than in a file of their
            # own, which is how Codex does it.
            if name.lower().endswith(".toml"):
                candidate = Path(current, name)
                if MCP_IN_CONFIG.search(candidate.read_text(encoding="utf-8", errors="replace")):
                    raise ExecutorError(
                        "SANDBOX_UNSAFE",
                        f"The {provider} executor home declares MCP servers in {candidate.relative_to(home)}",
                    )


def inventory(home: Path) -> tuple[tuple[str, str], ...]:
    """Every file in the home with its digest, for the provenance record."""
    entries: list[tuple[str, str]] = []
    for current, directories, files in os.walk(home, topdown=True, followlinks=False):
        directories.sort()
        for name in sorted(files):
            path = Path(current, name)
            entries.append((path.relative_to(home).as_posix(), hashlib.sha256(path.read_bytes()).hexdigest()))
    return tuple(entries)


def prepare_isolated_home(
    provider: str, runtime_dir: Path, env: dict[str, str] | None = None, *, shim: Path | None = None
) -> IsolatedHome:
    """Build a fresh home for one executor run and prove it meets the isolation rule."""
    if provider not in EXECUTOR_PROVIDERS:
        raise ExecutorError("CONFIG_INVALID", "Provider must be one of " + ", ".join(EXECUTOR_PROVIDERS))
    # A caller-supplied environment goes through the same filter as the default
    # one. Copying it verbatim would let an alternate-provider key or an ambient
    # config path survive into the launch environment and defeat the isolation.
    # The shim is the resolved CLI executable; on Windows an over-long operator
    # PATH is reduced around it, because cmd.exe cannot use one at all.
    environment, child_path = _sanitize_environment(provider, source=env, shim=shim)
    home = runtime_dir / f"{provider}-home"
    _reject_reparse_path(runtime_dir, home, category="SANDBOX_UNSAFE", label="Executor home")
    if home.exists():
        raise ExecutorError("SANDBOX_UNSAFE", "The executor home already exists; each run gets a fresh one")
    try:
        home.mkdir(mode=0o700, parents=True)
    except OSError as exc:
        raise ExecutorError("SESSION_CLEANUP_FAILED", "The executor home could not be created") from exc

    source = operator_home(provider, environment)
    if not source.is_dir():
        raise ExecutorError("AUTH_MISSING", f"The {provider} operator home is unavailable; sign in first")
    if provider == "qwen":
        # Qwen has no config-dir variable: it discovers ~/.qwen only after the
        # process home has been redirected, so build its settings at that path.
        environment.update(_redirect_home(home, user_root(environment)))
        environment.update(_BUILDERS[provider](home, source, environment))
    else:
        environment.update(_BUILDERS[provider](home, source, environment))
    if provider in GLM_PROVIDERS:
        environment["OPENCODE_DISABLE_AUTOUPDATE"] = "1"
    work = runtime_dir / f"{provider}-work" if provider in CODEX_PROVIDERS else None
    if provider != "qwen":
        environment.update(_redirect_home(home, user_root(environment), work))
    # The check runs on the finished home: the redirect writes .gitconfig and
    # the cache directories into it, and that is the home the provider
    # receives. HOME resolves inside the home by then, so the operator's home
    # is passed explicitly rather than derived from the environment.
    assert_isolated(provider, home, environment, operator=source)
    if work is not None:
        assert_isolated(provider, work, environment, operator=source)
    return IsolatedHome(
        provider=provider,
        path=home,
        operator=source,
        env=environment,
        inventory=inventory(home),
        child_path=child_path,
    )


def persist_rotated_credentials(
    provider: str, home: Path, env: dict[str, str], operator: Path | None = None
) -> list[str]:
    """Carry a rotated credential back, so a run does not log the operator out.

    Returns the operator-relative paths that were updated. Anything that does
    not parse as a JSON object is left alone: a half-written or corrupt file in
    a disposable home must never overwrite the operator's working credential.
    For a shared file such as OpenCode's auth.json only the routes the executor
    was given are written back; every other provider's entry is preserved.
    """
    source_home = operator if operator is not None else operator_home(provider, env)
    carried: list[str] = []
    for home_relative, source_relative, keys in ROTATING_CREDENTIALS.get(provider, ()):
        isolated, source = home / home_relative, source_home / source_relative
        if not isolated.is_file() or not source.is_file():
            continue
        try:
            fresh = isolated.read_bytes()
            if fresh == source.read_bytes() or len(fresh) > MAX_FILE_BYTES:
                continue
            payload = json.loads(fresh.decode("utf-8"))
            if not isinstance(payload, dict) or not payload:
                continue
            if keys is not None:
                merged = json.loads(source.read_bytes().decode("utf-8"))
                if not isinstance(merged, dict):
                    continue
                merged.update({key: payload[key] for key in keys if key in payload})
                payload = merged
        except (OSError, UnicodeError, json.JSONDecodeError):
            continue
        staged = source.with_name(source.name + ".avox-rotated")
        try:
            staged.write_text(json.dumps(payload, separators=(",", ":")), encoding="utf-8", newline="\n")
            if os.name != "nt":
                staged.chmod(0o600)
            _durable_replace(staged, source)
        except (OSError, ExecutorError):
            staged.unlink(missing_ok=True)
            continue
        carried.append(source_relative)
    return carried
