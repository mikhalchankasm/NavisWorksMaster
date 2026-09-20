#!/usr/bin/env python3
"""Guard the Codex executor boundary.

Each check below corresponds to a way the boundary was actually found to leak, on
this machine, by running a probe and reading what came back. They are cheap and
they fail closed, which is the only useful direction for a guard on an agent that
holds write access to a worktree.

This is a guard, not a substitute for the probes. A guard reads text; only
`scripts/codex_briefs/isolation_probe.md` and its siblings execute anything, and
the difference is the point: a check that compares and never runs can stay green
while the thing it protects stops working.
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import codex_executor as ce  # noqa: E402


def check_shim_is_rejected(failures):
    """A batch wrapper truncates an argument at its first newline.

    `codex.cmd` looks like a working entry point and mangles every multi-line
    brief into its first line, which is how this was mis-diagnosed for weeks as a
    "launch reliability quirk" elsewhere.
    """
    for shim in ("C:/npm/codex.cmd", "C:/npm/codex", "C:/npm/codex.ps1", "C:/npm/codex.bat"):
        try:
            resolved = ce.resolve_codex_entry([shim])
        except ce.ExecutorError:
            continue
        failures.append("resolve_codex_entry accepted a shim: %s -> %s" % (shim, resolved))


def check_environment_is_built_not_filtered(failures):
    """A new provider variable in the parent must not reach the executor."""
    parent = {
        "PATH": "/usr/bin",
        "ANTHROPIC_API_KEY": "secret",
        "OPENAI_API_KEY": "secret",
        "GH_TOKEN": "secret",
        "CLAUDE_CODE_SOMETHING": "secret",
        "NAVISHELPER_MCP_TOOLS": "all",
        "SOME_FUTURE_PROVIDER_TOKEN": "secret",
        "HOME": "C:/Users/Operator",
        "CODEX_HOME": "C:/Users/Operator/.codex",
    }
    env = ce.sanitize_environment(parent, Path("D:/run/home"), Path("D:/run/tmp"), Path("D:/run/home/.codex"))

    for leaked in ("ANTHROPIC_API_KEY", "OPENAI_API_KEY", "GH_TOKEN", "CLAUDE_CODE_SOMETHING",
                   "NAVISHELPER_MCP_TOOLS", "SOME_FUTURE_PROVIDER_TOKEN"):
        if leaked in env:
            failures.append("sanitize_environment passed through %s" % leaked)

    if env.get("HOME") != str(Path("D:/run/home")):
        failures.append("HOME was not redirected into the run home")
    if env.get("USERPROFILE") != str(Path("D:/run/home")):
        failures.append("USERPROFILE was not redirected; per-provider variables do not cover it")
    if env.get("CODEX_HOME") != str(Path("D:/run/home/.codex")):
        failures.append(
            "CODEX_HOME must point at the .codex folder, not the home: pointing it at the home "
            "made Codex look for auth.json one level above the copy and report 401"
        )
    if env.get("TEMP") == env.get("HOME"):
        failures.append("TEMP must not equal HOME; Codex refuses to create PATH aliases then")


def check_redaction_runs_before_the_tail(failures):
    """A token is recognised by its prefix.

    Cut the tail between `Bearer ` and the value and the tail keeps the secret and
    loses the only thing that would have matched it.
    """
    # Deliberately shaped so the pattern pass cannot match it: no sk-/eyJ/Bearer
    # prefix. Only the known-value pass can remove this, so disabling that pass
    # fails here instead of hiding behind the pattern pass. A refresh token in
    # auth.json looks like this, not like an API key.
    secret = "9f3c" + "d7b1a2e4" * 5
    text = ("preamble " * 2000) + "refresh_token=" + secret
    redacted = ce.redact(text, [secret])
    if secret in redacted:
        failures.append("redact left a known secret value in place")

    prefixed = "sk-" + "a" * 40
    if prefixed in ce.redact("token " + prefixed, []):
        failures.append("the pattern pass no longer catches an unknown token by its prefix")

    tailed = ce.tail(redacted, 200)
    if secret in tailed:
        failures.append("the secret survived into the tail; redaction must run before the cut")

    if ce.tail("short", 200) != "short":
        failures.append("tail altered a string shorter than the limit")

    long_tail = ce.tail("x" * 500 + "ENDMARKER", 100)
    if "ENDMARKER" not in long_tail:
        failures.append("tail returned the head instead of the tail")


def check_dirty_file_contents_are_fingerprinted(failures):
    """`git status --porcelain` alone cannot see an overwrite.

    Overwriting a file that was already modified leaves the status output
    identical, so the content of each dirty file has to be hashed too.
    """
    before = {"head": "abc", "porcelain": " M a.txt", "dirty": {"a.txt": "hash1"}}
    after = {"head": "abc", "porcelain": " M a.txt", "dirty": {"a.txt": "hash2"}}
    if not ce.fingerprint_changes(before, after):
        failures.append("fingerprint_changes missed an overwrite of an already-modified file")

    moved = {"head": "def", "porcelain": " M a.txt", "dirty": {"a.txt": "hash1"}}
    if not ce.fingerprint_changes(before, moved):
        failures.append("fingerprint_changes missed HEAD moving")

    if ce.fingerprint_changes(before, dict(before)):
        failures.append("fingerprint_changes reported a change where there was none")


def check_credentials_are_never_clobbered(failures):
    """Refuse to write back anything that does not parse.

    Codex consumes the refresh token when it refreshes. A home that is deleted
    after a refresh leaves the operator holding a retired token, and a careless
    write-back leaves them holding a corrupt file instead.
    """
    import json
    import tempfile

    with tempfile.TemporaryDirectory() as raw:
        tmp = Path(raw)
        real = tmp / "auth.json"
        real.write_text(json.dumps({"tokens": {"refresh": "old"}, "other_provider": "keep"}), encoding="utf-8")

        garbage = tmp / "run_garbage.json"
        garbage.write_text("not json at all", encoding="utf-8")
        status = ce.write_back_credentials(garbage, real, "{}")
        if not status.startswith("REFUSED"):
            failures.append("write_back_credentials accepted a credential that does not parse")
        if "other_provider" not in real.read_text(encoding="utf-8"):
            failures.append("a refused write-back still damaged the operator credential")

        refreshed = tmp / "run_good.json"
        refreshed.write_text(json.dumps({"tokens": {"refresh": "new"}}), encoding="utf-8")
        status = ce.write_back_credentials(refreshed, real, "{}")
        if not status.startswith("written back"):
            failures.append("write_back_credentials did not carry a refreshed credential back: " + status)

        merged = json.loads(real.read_text(encoding="utf-8"))
        if merged.get("tokens", {}).get("refresh") != "new":
            failures.append("the refreshed token was not written back")
        if merged.get("other_provider") != "keep":
            failures.append("write-back dropped another provider's route instead of merging")


def check_credentials_never_sit_where_the_executor_can_read_them(failures):
    """A credential the executor can read is a credential it can send.

    The run directory holds a copy of the operator's subscription token, and the
    hosted web__run tool reaches the public internet. While the run directory lived
    inside the repository, the executor's own working root contained the token.
    """
    if str(ce.RUNS_DIR).lower().startswith(str(ce.REPO_ROOT).lower()):
        failures.append(
            "RUNS_DIR is inside REPO_ROOT (%s): the credential copy would sit in the "
            "executor's readable working root, and web__run egress is open" % ce.RUNS_DIR
        )

    env = ce.sanitize_environment({"APPDATA": "C:/Users/Operator/AppData/Roaming",
                                   "LOCALAPPDATA": "C:/Users/Operator/AppData/Local",
                                   "PATH": "/usr/bin"},
                                  Path("D:/run/home"), Path("D:/run/tmp"),
                                  Path("D:/run/home/.codex"))
    for name in ("APPDATA", "LOCALAPPDATA"):
        value = env.get(name, "")
        if "Operator" in value or not value.startswith(str(Path("D:/run/home"))):
            failures.append(
                "%s still points at the operator profile (%s); anything under the real "
                "AppData is readable, and exfiltratable" % (name, value)
            )


def check_a_refused_write_back_keeps_the_copy(failures):
    """The only valid rotated token must not be deleted because a merge failed."""
    status = ce.remove_credential_copy(Path("D:/does-not-exist/auth.json"),
                                       "REFUSED: the operator credential does not parse")
    if not status.startswith("kept"):
        failures.append(
            "remove_credential_copy deleted the run copy after a refused write-back; "
            "that copy can hold the only valid rotated token"
        )

    for safe in ("unchanged", "written back (tokens); previous kept as auth.json.bak"):
        status = ce.remove_credential_copy(Path("D:/does-not-exist/auth.json"), safe)
        if status.startswith("kept"):
            failures.append("remove_credential_copy kept the copy after a successful write-back")


def check_overlapping_runs_cannot_restore_a_retired_token(failures):
    """Two runs copy the same old token; the later one must not undo the rotation."""
    import json
    import tempfile

    with tempfile.TemporaryDirectory() as raw:
        tmp = Path(raw)
        original = json.dumps({"tokens": {"refresh": "old"}, "other": "keep"})

        real = tmp / "auth.json"
        # Run A has already rotated the operator's token.
        real.write_text(json.dumps({"tokens": {"refresh": "rotated-by-A"}, "other": "keep"}),
                        encoding="utf-8")

        # Run B started from the same snapshot and changed nothing.
        run_b = tmp / "run_b.json"
        run_b.write_text(original, encoding="utf-8")

        status = ce.write_back_credentials(run_b, real, original)
        if status != "unchanged":
            failures.append("a run that changed nothing reported a write-back: " + status)

        after = json.loads(real.read_text(encoding="utf-8"))
        if after.get("tokens", {}).get("refresh") != "rotated-by-A":
            failures.append(
                "an overlapping run restored a retired token over a rotation; write-back "
                "must compare against the snapshot the run started from"
            )


def check_the_guarded_repository_is_the_one_the_executor_was_given(failures):
    """Fingerprinting the wrong checkout reports "unchanged" whatever happened."""
    inside = ce.git_root(Path(__file__).resolve().parent)
    if inside != ce.REPO_ROOT:
        failures.append("git_root did not resolve this repository from a subdirectory: %s" % inside)


def check_launch_line_keeps_its_isolation_flags(failures):
    """The flags are the boundary; losing one silently widens the executor."""
    argv = ce.build_argv(Path("C:/codex/codex.js"), Path("D:/repo"), Path("D:/run/last.txt"))

    for required in ("--ignore-user-config", "--ignore-rules", "--approve-for-me", "--json"):
        if required not in argv:
            failures.append("build_argv dropped %s" % required)

    if "--sandbox" in argv:
        failures.append("--sandbox contradicts --approve-for-me and the CLI refuses the pair")

    if argv[-1] != "-":
        failures.append("the brief must arrive on stdin, not as an argument")

    if not argv[0].endswith("node") or not argv[1].endswith("codex.js"):
        failures.append("the launch line must go through node and codex.js, never a shim")

    for feature in ("browser_use_full_cdp_access", "computer_use", "multi_agent", "remote_plugin"):
        if feature not in argv:
            failures.append(
                "feature %s is enabled by default in this CLI and must be disabled explicitly; "
                "--ignore-user-config does not cover it" % feature
            )


def check_brief_limit_matches_the_template(failures):
    template = Path(__file__).resolve().parent.parent / "docs" / "TASK_BRIEF.md"
    if ce.MAX_BRIEF_BYTES != 2048:
        failures.append("the brief limit drifted from the 2 KB docs/TASK_BRIEF.md sets")
    if template.is_file() and len(template.read_bytes()) > ce.MAX_BRIEF_BYTES:
        failures.append("docs/TASK_BRIEF.md itself exceeds the limit the launcher enforces")


def check_probe_briefs_exist(failures):
    """The boundary is verified by running something, not by reading this file."""
    briefs = Path(__file__).resolve().parent / "codex_briefs"
    probe = briefs / "isolation_probe.md"
    if not probe.is_file():
        failures.append("scripts/codex_briefs/isolation_probe.md is missing; the boundary is unverified")
        return
    text = probe.read_text(encoding="utf-8")
    for expected in ("MCP server", "MCP tool"):
        if expected not in text:
            failures.append("the isolation probe no longer asks about %ss" % expected)


def main() -> int:
    failures: list[str] = []

    check_shim_is_rejected(failures)
    check_environment_is_built_not_filtered(failures)
    check_redaction_runs_before_the_tail(failures)
    check_dirty_file_contents_are_fingerprinted(failures)
    check_credentials_are_never_clobbered(failures)
    check_credentials_never_sit_where_the_executor_can_read_them(failures)
    check_a_refused_write_back_keeps_the_copy(failures)
    check_overlapping_runs_cannot_restore_a_retired_token(failures)
    check_the_guarded_repository_is_the_one_the_executor_was_given(failures)
    check_launch_line_keeps_its_isolation_flags(failures)
    check_brief_limit_matches_the_template(failures)
    check_probe_briefs_exist(failures)

    if failures:
        print("Codex executor boundary check failed:")
        for failure in failures:
            print("  - %s" % failure)
        return 1

    print(
        "Codex executor boundary check passed: shim rejected, environment built from scratch, "
        "redaction before tail, dirty-content fingerprint, credential write-back guarded, "
        "%d default-on features disabled." % len(ce.DISABLED_FEATURES)
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
