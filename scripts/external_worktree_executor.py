#!/usr/bin/env python3
"""Run one external agent in a git worktree and record what happened.

The other half of executor mode from AGENTS.md section 7. executor_worktree
decides what a run may be; this module runs it against a home built by
agent_home_isolation, and writes the provenance.

Entry point is `python -m scripts.external_worktree_executor`; running the file
by path does not put the repository root on sys.path.

On confinement, so the record is not read as promising more than it does: the
provider process is not sandboxed. It runs with the operator's own filesystem
rights and can write outside its worktree if it chooses to. What this module
gives is detection - the main checkout is fingerprinted before and after, and a
run that moved it is marked contaminated and exits non-zero. The same holds for
the host outside any checkout (executor_host_guard): the user environment in
the registry, local sandbox accounts, and the grants in Codex's sandbox log.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import tempfile
import time
import uuid
from pathlib import Path
from typing import Sequence

from dataclasses import replace

# Started as a script rather than as part of the package, this file puts
# scripts/ on sys.path instead of its parent, and the absolute imports below
# cannot resolve. AGENTS.md documents the `python -m` form, but the script form
# should not fail obscurely.
if __package__ in (None, ""):
    sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from scripts.agent_home_isolation import (
    CODEX_REVIEWED_VERSIONS,
    codex_version_is_reviewed,
    EXECUTOR_PROFILES,
    EXECUTOR_PROVIDERS,
    assert_isolated,
    inventory,
    load_executor_profiles,
    persist_rotated_credentials,
    prepare_isolated_home,
)
from scripts.executor_event_journal import write_event_journal
from scripts.executor_host_guard import (
    HostSnapshot,
    changed_acl_paths,
    changed_environment_names,
    created_sandbox_accounts,
    path_entry_changes,
    sandbox_log_evidence,
    take_snapshot,
    telemetry_problems,
    violations,
)
from scripts.executor_msbuild_nodes import RUN_MARKER, clean_orphan_msbuild_nodes
from scripts.executor_process_control import scan
from scripts.executor_worktree_acl import grant_worktree_access, restore_worktree_access, sandbox_sids
from scripts.executor_worktree import (
    _git,
    build_argv,
    codex_isolation_arguments,
    unsafe_codex_features,
    build_prompt,
    fingerprint,
    main_checkout,
    read_brief,
    resolve_worktree,
    restore_disposable_worktree,
    worktree_head,
)
from scripts.executor_primitives import (
    GLM_CREDENTIAL_ENV_NAMES,
    MAX_FILE_BYTES,
    MAX_TIMEOUT_SECONDS,
    MIN_TIMEOUT_SECONDS,
    ExecutorError,
    _classify_failure,
    _redact,
    _remove_runtime_dir,
    _resolve_executable,
    _launcher,
    _run_process,
)

RUN_SCHEMA = "avox-worktree-run/v1"
# A credential value is long and opaque; anything shorter than this is a field
# name, a type or a scope and redacting it would blank out ordinary output.
MIN_SECRET_LENGTH = 16


def _json_strings(value: object, found: set[str]) -> None:
    if isinstance(value, str):
        if len(value) >= MIN_SECRET_LENGTH:
            found.add(value)
    elif isinstance(value, dict):
        for item in value.values():
            _json_strings(item, found)
    elif isinstance(value, list):
        for item in value:
            _json_strings(item, found)


def _secret_values(home: Path, env: dict[str, str], only: Sequence[str] | None = None) -> set[str]:
    """Every credential value that physically exists in this run.

    _redact knows patterns. It does not know the tokens this launcher copied
    into the home, and its patterns do not match a bare quoted field such as
    "accessToken":"...". A provider diagnostic, or an agent reading its own
    home, would otherwise put a working token into the provenance record.
    """
    found: set[str] = set()
    if only is None:
        candidates = [
            Path(current, name)
            for current, _directories, files in os.walk(home, topdown=True, followlinks=False)
            for name in files
        ]
    else:
        candidates = [home / relative for relative in only]
    for candidate in candidates:
        try:
            raw = candidate.read_bytes()
            if len(raw) > MAX_FILE_BYTES:
                continue
            _json_strings(json.loads(raw.decode("utf-8")), found)
        except (OSError, UnicodeError, json.JSONDecodeError):
            continue
    for name in GLM_CREDENTIAL_ENV_NAMES:
        if len(env.get(name, "")) >= MIN_SECRET_LENGTH:
            found.add(env[name])
    return found


# How much of each stream the record keeps. _redact caps at 500 characters from
# the start, which for a JSONL event stream is the opening preamble and nothing
# else - useless for the case the field exists for, a run that did nothing.
MAX_STREAM_TAIL = 4000
# The whole event stream a run may print. Codex's JSON stream echoes every
# command's output, and gpt-6-sol died at the shared 256 KB cap twice on one
# task (#441). Only MAX_STREAM_TAIL of it reaches the record, so the stream
# stays bounded for memory and redaction, not for the orchestrator's context.
MAX_WORKTREE_STREAM_BYTES = 8_000_000


def _scrub(text: str, secrets: set[str]) -> str:
    """Redact the whole stream, then keep its end.

    Both passes run before the cut. Known values first, so one straddling it
    cannot survive as a fragment; then the pattern pass over the full text,
    because a token is recognised by a prefix - `Bearer `, `sk-`, `token=` -
    and slicing first would leave that prefix outside the tail while the secret
    itself stayed inside it.
    """
    return _scrub_whole(text, secrets)[-MAX_STREAM_TAIL:]


def _scrub_whole(text: str, secrets: set[str]) -> str:
    """Redact a complete stream without imposing the record's tail limit."""
    for secret in sorted(secrets, key=len, reverse=True):
        text = text.replace(secret, "[REDACTED]")
    return _redact(text, limit=max(len(text), 1))


# Retries cover a provider that still holds a handle when the run returns. They
# do not cover a read-only file, which is a permission problem rather than a
# timing one - _remove_runtime_dir clears that before it deletes.
CLEANUP_ATTEMPTS = 4
CLEANUP_PAUSE_SECONDS = 1.5


def _remove_runtime(root: Path) -> tuple[bool, str]:
    """Best effort, with retries: OpenCode holds its database open for a moment.

    The directory holds a copy of a real credential, so failure is reported
    loudly - but it is reported, not raised. Raising here would discard the
    record of a run that already happened, which is how a failed cleanup turns
    into no evidence at all.
    """
    problem = ""
    for attempt in range(CLEANUP_ATTEMPTS):
        if not root.exists():
            return True, ""
        try:
            _remove_runtime_dir(root.parent, root)
            return True, ""
        except (ExecutorError, OSError) as exc:
            problem = str(exc)
            if attempt < CLEANUP_ATTEMPTS - 1:
                time.sleep(CLEANUP_PAUSE_SECONDS)
    return False, problem


def _host_changes(
    provider: str,
    home: Path,
    worktree: Path,
    runtime: Path,
    before: HostSnapshot,
    after: HostSnapshot,
    secrets: set[str],
) -> dict[str, object]:
    """What moved outside the worktree: user environment, local accounts, sandbox grants.

    Codex keeps its sandbox log under CODEX_HOME, which is the disposable home;
    the tail is copied here because the home is deleted when the run ends.
    """
    host: dict[str, object] = {
        "operatorEnvironmentChanged": changed_environment_names(before, after),
        "sandboxAccountsCreated": created_sandbox_accounts(before, after),
        "operatorAclChanged": changed_acl_paths(before, after),
        # Which PATH entries moved and when the key was written: what #1052 could not attribute.
        "operatorPathChanges": (
            path_entry_changes(before.path, after.path) if before.path is not None and after.path is not None else None
        ),
        "environmentKeyWritten": {"before": before.environment_written, "after": after.environment_written},
    }
    profile = os.environ.get("USERPROFILE") or os.environ.get("HOME")
    if provider in {"codex", "codex-plan"} and profile:
        evidence = sandbox_log_evidence(home / ".sandbox", Path(profile), [worktree, runtime])
        for entry in evidence["files"]:
            if "tail" in entry:
                entry["tail"] = _scrub(str(entry["tail"]), secrets)
        host["sandboxLog"] = evidence
    return host


def run(
    provider: str,
    worktree: Path,
    brief: Path,
    *,
    timeout_seconds: int = 1800,
    runtime_root: Path | None = None,
    record_path: Path | None = None,
) -> dict[str, object]:
    """One executor run: isolated home, provider CLI in the worktree, provenance out."""
    if provider not in EXECUTOR_PROVIDERS:
        raise ExecutorError("CONFIG_INVALID", "Provider must be one of " + ", ".join(EXECUTOR_PROVIDERS))
    if not MIN_TIMEOUT_SECONDS <= timeout_seconds <= MAX_TIMEOUT_SECONDS:
        raise ExecutorError("CONFIG_INVALID", f"Timeout must be {MIN_TIMEOUT_SECONDS}..{MAX_TIMEOUT_SECONDS} seconds")
    load_executor_profiles()
    target = resolve_worktree(worktree, provider)
    prompt = build_prompt(provider, read_brief(brief))
    executable = _resolve_executable(provider)
    owner_checkout = main_checkout(target)
    advisor_baseline = worktree_head(target) if provider == "codex-plan" else None

    # The runtime directory holds a copy of a real credential, so it lives
    # outside the worktree and is removed in the finally below whatever happens.
    root = Path(runtime_root) if runtime_root else Path(tempfile.mkdtemp(prefix="avox-executor-"))
    started = time.monotonic()
    run_id = uuid.uuid4().hex
    record: dict[str, object] | None = None
    provider_started = False
    window_started_ms: int | None = None
    window_finished_ms: int | None = None
    home_path: Path | None = None
    worktree_grant: dict[str, object] | None = None
    acl_result: dict[str, object] | None = None
    unrestored: dict[str, object] | None = None
    try:
        # The resolved CLI is passed as the shim: if the operator PATH has to
        # be reduced for cmd.exe, the directory the shim lives in survives it.
        home = prepare_isolated_home(provider, root, shim=executable)
        home_path = home.path
        env = dict(home.env)
        if provider == "claude":
            # Claude Code ignores workspace settings in an untrusted directory.
            # The trust flag goes into the isolated home, never the operator's,
            # and the home is rechecked for ambient capability afterwards.
            (home.path / ".claude.json").write_text(
                json.dumps({"projects": {str(target): {"hasTrustDialogAccepted": True}}}), encoding="utf-8"
            )
            assert_isolated(provider, home.path, env, operator=home.operator)
        argv, stdin_text = build_argv(provider, executable, prompt, env)
        if provider in ("codex", "codex-plan"):
            check_codex_isolation(executable, env, target, root)
        if provider == "codex":
            # The sandbox token's ordinary SIDs need an ACE of their own to write the worktree (#1275).
            worktree_grant = grant_worktree_access(target, owner_checkout, lambda: Path(
                _git("-C", str(target), "rev-parse", "--path-format=absolute", "--show-toplevel")))
        # MSBuild workers inherit it, which is how the cleanup below tells this run's nodes from others (#576).
        env[RUN_MARKER] = run_id
        # Snapshotted here: after the trust file and the provider configuration
        # that build_argv adds, and before the run. Taking it afterwards instead
        # would record everything the provider downloaded into its own home -
        # OpenCode alone writes a node_modules tree - and bury the settings the
        # record exists to attest.
        launched = replace(home, env=env, inventory=inventory(home.path))
        secrets = _secret_values(home.path, env)
        owner_before = fingerprint(owner_checkout)
        host_before = take_snapshot()
        provider_started = True
        window_started_ms = int(time.time() * 1000)
        process = _run_process(
            argv,
            cwd=target,
            timeout_seconds=timeout_seconds,
            env=env,
            stdin_text=stdin_text,
            spool_dir=root,
            max_output_bytes=MAX_WORKTREE_STREAM_BYTES,
        )
        window_finished_ms = int(time.time() * 1000)
        if worktree_grant is not None:
            # Success, provider failure and timeout all return here; an exception is handled in finally.
            acl_result = restore_worktree_access(worktree_grant, sandbox_sids(home.path))
        contaminated = fingerprint(owner_checkout) != owner_before
        host_after = take_snapshot()
        # Before the home is destroyed: a refresh inside it consumed the
        # operator's refresh token and issued a replacement that lives only
        # here. Discarding it logs the operator out of their own CLI.
        carried = persist_rotated_credentials(provider, home.path, env, operator=home.operator)
        # A refresh during the run issued tokens that did not exist before it,
        # so the known-value set is taken again - over the files the run started
        # with, not over whatever the provider has since written.
        secrets |= _secret_values(home.path, env, only=[name for name, _ in launched.inventory])
        redacted_stdout = _scrub_whole(process.stdout, secrets)
        host = _host_changes(provider, home.path, target, root, host_before, host_after, secrets)
        if acl_result is not None:
            host["worktreeAclGrant"] = acl_result
        if provider == "codex":
            host["processControl"] = scan(process.stdout, process.process_tree_pids, secrets)
            # Read from the whole stream before the record's tail is cut; only problem codes are kept.
            host["telemetry"] = {"problems": telemetry_problems(
                process.stdout, timed_out=process.timed_out, output_too_large=process.output_too_large
            )}
        stdout_truncated = len(redacted_stdout) > MAX_STREAM_TAIL
        record = {
            "schemaVersion": RUN_SCHEMA,
            "runId": run_id,
            "provider": provider,
            "model": EXECUTOR_PROFILES[provider]["model"],
            "effort": EXECUTOR_PROFILES[provider]["effort"],
            "worktree": str(target),
            "mainCheckout": str(owner_checkout),
            "mainCheckoutContaminated": contaminated,
            "exitCode": process.return_code,
            "timedOut": process.timed_out,
            "outputTooLarge": process.output_too_large,
            "durationSeconds": round(time.monotonic() - started, 3),
            "home": launched.record(),
            "credentialsCarriedBack": carried,
            # Both streams, redacted and truncated. They are here because a run
            # that silently does nothing looks identical to a successful one in
            # every other field - GLM produced exactly that once during #266:
            # 37 bytes, no work, a zero exit code.
            "stdoutTail": redacted_stdout[-MAX_STREAM_TAIL:],
            "stdoutTruncated": stdout_truncated,
            "stderr": _scrub(process.stderr, secrets),
            "host": host,
        }
        if provider == "codex":
            # The whole event stream, for checking a real run's schema (#1069); outside the runtime it outlives.
            journal = (
                Path(str(record_path) + ".events.jsonl") if record_path else root.parent / f"{run_id}.events.jsonl"
            )
            try:
                record["eventJournal"] = write_event_journal(
                    process.stdout, secrets, journal,
                    timed_out=process.timed_out, output_too_large=process.output_too_large,
                )
            except OSError as exc:
                record["eventJournal"] = {"path": str(journal), "problems": ["write_failed:" + type(exc).__name__]}
        if EXECUTOR_PROFILES[provider].get("mode") == "advisor" and stdout_truncated:
            stdout_path = (
                Path(str(record_path) + ".stdout.txt")
                if record_path
                else root.parent / f"{record['runId']}.stdout.txt"
            )
            stdout_bytes = redacted_stdout.encode("utf-8")
            record["stdoutPath"] = str(stdout_path)
            record["stdoutBytes"] = len(stdout_bytes)
            stdout_path.write_bytes(stdout_bytes)
        if process.return_code != 0:
            record["failure"] = _classify_failure(
                process.stderr,
                timed_out=process.timed_out,
                output_too_large=process.output_too_large,
            )
        found = violations(host)
        if found:
            # A run that moved the operator's machine failed whatever it
            # produced; the provider's own outcome is kept beside it.
            if "failure" in record:
                record["processFailure"] = record["failure"]
            record["failure"] = found[0]
            record["hostViolations"] = found
        return record
    finally:
        if window_started_ms is not None:
            # MSBuild workers the run started and left without a parent (#576). Here rather than
            # after the child returns: an interrupted run leaves the same orphans, and they outlive
            # the Job that _run_process closes.
            try:
                orphans = clean_orphan_msbuild_nodes(
                    window_started_ms, window_finished_ms or int(time.time() * 1000), run_id
                )
            except Exception as exc:  # never mask the run's own outcome
                orphans = {"found": None, "terminated": 0, "problem": str(exc)}
            if "problem" in orphans:
                orphans["problem"] = _redact(str(orphans["problem"]))
            if record is not None:
                record["msbuildOrphanNodes"] = orphans
        if provider_started and advisor_baseline is not None:
            restore = restore_disposable_worktree(provider, target, advisor_baseline)
            if "worktreeRestoreProblem" in restore:
                restore["worktreeRestoreProblem"] = _redact(str(restore["worktreeRestoreProblem"]))
            if record is not None:
                record.update(restore)
        if worktree_grant is not None and acl_result is None:
            # An interrupted run. Still before the home goes: its cap_sid names this run's sandbox SIDs.
            acl_result = restore_worktree_access(worktree_grant, sandbox_sids(home_path) if home_path else set())
            if not acl_result.get("restored"):
                unrestored = acl_result
        if runtime_root is None:
            removed, problem = _remove_runtime(root)
            if record is not None:
                record["runtimeDirectoryRemoved"] = removed
                if not removed:
                    record["runtimeDirectory"] = str(root)
                    record["runtimeDirectoryProblem"] = _redact(problem)
        if unrestored is not None:
            # There is no record to carry it, so the refusal itself names what is left on the worktree.
            raise ExecutorError("WORKTREE_ACL_NOT_RESTORED", json.dumps(unrestored), exit_code=6)


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Run an external agent in a git worktree against an isolated home.")
    parser.add_argument("--provider", required=True, choices=EXECUTOR_PROVIDERS)
    parser.add_argument("--worktree", required=True, type=Path)
    parser.add_argument("--brief", required=True, type=Path)
    parser.add_argument("--timeout", type=int, default=1800)
    parser.add_argument("--record", type=Path, help="Where to write the provenance record; stdout when omitted.")
    return parser


CODEX_FEATURE_CHECK_TIMEOUT_SECONDS = 60


def check_codex_isolation(executable: Path, env: dict[str, str], cwd: Path, spool_dir: Path) -> None:
    """Proves, in the isolated home and with the run's own switches, that no account feature stays on.

    The switches are not trusted by name: codex ignores an unknown feature silently, so a rename in a new CLI
    release would leave the ChatGPT account's connectors, browser or plugins enabled (HetengSpec session,
    2026-09-29). A refusal happens before the provider runs and spends no quota.
    """
    version = _run_process(
        _launcher(executable, ["--version"]),
        cwd=cwd,
        timeout_seconds=CODEX_FEATURE_CHECK_TIMEOUT_SECONDS,
        env=dict(env),
        spool_dir=spool_dir,
    )
    version_text = (version.stdout or "").strip().splitlines()[-1:] if version.return_code == 0 else []
    if not version_text or not codex_version_is_reviewed(version_text[0]):
        raise ExecutorError(
            "CONFIG_INVALID",
            "Codex CLI " + (version_text[0].strip() if version_text else "version unknown")
            + " is not a reviewed executor version (" + ", ".join(sorted(CODEX_REVIEWED_VERSIONS))
            + ") - review it as in docs/EXECUTOR_SETUP.md (Upgrading the CLI)",
        )
    result = _run_process(
        _launcher(executable, ["features", "list", *codex_isolation_arguments()]),
        cwd=cwd,
        timeout_seconds=CODEX_FEATURE_CHECK_TIMEOUT_SECONDS,
        env=dict(env),
        spool_dir=spool_dir,
    )
    if result.return_code != 0 or result.timed_out:
        raise ExecutorError("CONFIG_INVALID", "codex features list failed, so the executor isolation cannot be verified")
    unsafe = unsafe_codex_features(result.stdout)
    if unsafe is None:
        raise ExecutorError("CONFIG_INVALID", "codex features list printed no features to verify the isolation against")
    if unsafe:
        raise ExecutorError("SANDBOX_UNSAFE", ("Codex features not proven off in the isolated home: " + ", ".join(unsafe)
                             + " - review them as in docs/EXECUTOR_SETUP.md (Upgrading the CLI)"))


def main(argv: Sequence[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    try:
        record = run(
            args.provider,
            args.worktree,
            args.brief,
            timeout_seconds=args.timeout,
            record_path=args.record,
        )
    except ExecutorError as exc:
        print(json.dumps({"schemaVersion": RUN_SCHEMA, "error": exc.category, "message": str(exc)}), file=sys.stderr)
        return exc.exit_code
    text = json.dumps(record, indent=2)
    if args.record:
        args.record.write_text(text, encoding="utf-8", newline="\n")
    else:
        print(text)
    if record.get("runtimeDirectoryRemoved") is False:
        print(
            f"The runtime directory {record.get('runtimeDirectory')} still exists and holds a copy of a real "
            "credential. Delete it.",
            file=sys.stderr,
        )
        return 4
    if record.get("hostViolations"):
        print(
            "The run changed the operator's machine outside its worktree ("
            + ", ".join(record["hostViolations"])
            + "). See the record's host section before running another executor.",
            file=sys.stderr,
        )
        return 6
    if record.get("worktreeRestored") is False:
        print(
            f"The advisor worktree {record.get('worktree')} could not be restored: "
            f"{record.get('worktreeRestoreProblem')}",
            file=sys.stderr,
        )
        return 5
    if record.get("mainCheckoutContaminated"):
        print(
            f"The run changed {record['mainCheckout']}, which is outside its worktree. Inspect it before trusting "
            "anything this run produced.",
            file=sys.stderr,
        )
        return 3
    return 0 if record.get("exitCode") == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
