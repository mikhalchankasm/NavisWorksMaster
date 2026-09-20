#!/usr/bin/env python3
"""Run Codex as an executor under a home built for the run and nothing else.

Why this exists rather than `codex-plugin-cc` or an MCP wrapper: both run Codex
with the operator's own configuration. On this machine that configuration
declares ten `[mcp_servers.*]` sections, `sandbox_mode = "danger-full-access"`,
`approval_policy = "never"` and `features.search = true`. An executor is an agent
with write access to a worktree; it gets a home built for the run.

Three things are load-bearing and each cost a real incident somewhere:

1. **Never launch through the npm shim.** `which codex` returns a wrapper and
   `codex.cmd` is a batch file, and a batch file truncates an argument at its
   first newline. A multi-line brief arrives as its first line, the agent does
   something incoherent, and exits zero. Always `node <.../codex.js>`.
2. **Redirect `HOME` and `USERPROFILE`, not just `CODEX_HOME`.** Per-provider
   variables move a CLI's own config; they do not move what it reads from the
   home directory by convention. `.gitconfig` is rebuilt from `user.name` and
   `user.email` rather than copied, because a real one can carry `include.path`,
   `core.hooksPath` and a credential helper, all of which walk back through the
   boundary.
3. **Credentials rotate.** `auth.json` here holds a subscription login, not an
   API key. Codex consumes the refresh token when it refreshes and writes a new
   one; if that happens inside a directory that is then deleted, the operator is
   left holding a retired token. A changed credential is written back before the
   copy is removed, and only if it still parses.

`codex-cli 0.155.1` supplies `--ignore-user-config`, which does not load
`$CODEX_HOME/config.toml` while still taking auth from `CODEX_HOME`. That is a
structural guarantee rather than a filename check, so this launcher does not
write a replacement config at all: there is nothing to get wrong. `--ignore-rules`
does the same for execpolicy files.

Verify the boundary from the inside rather than trusting this docstring:

    python scripts/codex_executor.py --brief scripts/codex_briefs/isolation_probe.md

The right answer is "no MCP servers and no MCP tools". Anything else names a hole.

What the probes in `scripts/codex_briefs/` actually established on this machine,
codex-cli 0.155.1, by execution rather than self-report:

- No MCP servers and no MCP tools reach the executor.
- `HOME`, `USERPROFILE` and `CODEX_HOME` land inside the run directory, and the
  executor reads back the run home when asked.
- It can write inside the working root, and `python` and `git` are on its PATH,
  so it can run an acceptance command. An earlier
  `shell_environment_policy.inherit=none` left the shell unable to find `cmd`,
  which would also have left it unable to run the criterion; isolation comes from
  the redirected home and the sandbox, not from starving PATH.
- **One hole is open and could not be closed.** The hosted `web__run` tool
  reaches the public internet. It survived every lever available: `--search` not
  passed, `tools.web_search=false`, `tools.web__run=false`, `tools.web=false`. An
  executor here can fetch a URL, which is also a way to put repository content
  into one. Shell egress is a separate matter and was not reachable in the probe.
  Treat this as a live limitation of the boundary, not a residual risk that has
  been argued away: do not hand this executor a brief whose material you would
  not put on a public URL.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import stat
import subprocess
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
RUNS_DIR = REPO_ROOT / ".codex-runs"

# Codex reads its own config from CODEX_HOME and much else from the home
# directory by convention, so both move. TEMP moves too: a provider that drops a
# snapshot repository into temp leaves read-only git objects behind.
HOME_VARIABLES = ("HOME", "USERPROFILE")

# CODEX_HOME is the directory Codex reads auth.json from directly, so it is the
# `.codex` folder inside the run home, not the home itself. Pointing it at the
# home made the CLI look for auth.json one level above the copy and fail with
# "401 Missing bearer or basic authentication in header", which reads like a bad
# credential rather than a missing file.
CODEX_HOME_VARIABLE = "CODEX_HOME"

# TEMP must not be the home directory: Codex refuses to create its PATH aliases
# when it detects that its home sits under a temporary directory, and warns
# instead of failing, which is easy to miss.
TEMP_VARIABLES = ("TEMP", "TMP")

# Anything that could hand the executor a credential, a model endpoint or an MCP
# server is dropped. Matching is by prefix on the variable name, never by value.
DROPPED_ENVIRONMENT_PREFIXES = (
    "ANTHROPIC_",
    "CLAUDE",
    "OPENAI_",
    "CODEX_",
    "MCP_",
    "NAVISHELPER_",
    "GH_",
    "GITHUB_",
    "GIT_ASKPASS",
    "SSH_",
    "AWS_",
    "AZURE_",
    "GOOGLE_",
    "XDG_",
    "KIMI_",
    "OPENCODE_",
)

# Kept because the launcher cannot run without them.
KEPT_ENVIRONMENT = (
    "PATH",
    "PATHEXT",
    "SYSTEMROOT",
    "SYSTEMDRIVE",
    "WINDIR",
    "COMSPEC",
    "NUMBER_OF_PROCESSORS",
    "PROCESSOR_ARCHITECTURE",
    "OS",
    "LOCALAPPDATA",
    "APPDATA",
    "PROGRAMFILES",
    "PROGRAMFILES(X86)",
    "PROGRAMDATA",
    "COMMONPROGRAMFILES",
    "LANG",
    "LC_ALL",
)

TAIL_LIMIT = 4000

# Same ceiling docs/TASK_BRIEF.md sets for the brief template.
MAX_BRIEF_BYTES = 2048

# Features this CLI version ships enabled by default, which `--ignore-user-config`
# does NOT turn off because they are not coming from the operator's config at all.
# `codex features list` on 0.155.1 reports these as stable and true out of the box:
# an executor asked to edit one C# file would otherwise arrive holding browser
# control with full CDP access, desktop control, remote plugin loading and nested
# agents. EXECUTOR_SETUP names two of these (web_search, image_gen); the list has
# grown.
#
# `--disable` errors on a name this version does not know, which is the right
# direction to fail: a CLI update that renames one of these breaks the launch
# loudly instead of quietly widening the executor.
DISABLED_FEATURES = (
    "browser_use",
    "browser_use_external",
    "browser_use_full_cdp_access",
    "in_app_browser",
    "computer_use",
    "in_app_local_automation",
    "image_generation",
    "multi_agent",
    "plugins",
    "remote_plugin",
    "plugin_sharing",
    "skill_mcp_dependency_install",
    "skill_search",
    "apps",
    "tool_call_mcp_elicitation",
    "hooks",
)


class ExecutorError(RuntimeError):
    pass


# --------------------------------------------------------------------------- #
# Resolving the real entry point
# --------------------------------------------------------------------------- #

SHIM_SUFFIXES = (".cmd", ".bat", ".ps1", "")


def resolve_codex_entry(candidates):
    """Return the path to codex.js, never a shim.

    `candidates` is an iterable of paths to try, most specific first. A `.cmd` or
    extensionless wrapper is rejected outright: a batch wrapper truncates an
    argument at its first newline, which silently mangles a multi-line brief.
    """
    for candidate in candidates:
        path = Path(candidate)
        if path.name == "codex.js" and path.is_file():
            return path
    raise ExecutorError(
        "codex.js not found. Looked for the Node entry point, not the npm shim: "
        "a batch wrapper truncates a multi-line brief at its first newline. "
        "Tried: " + ", ".join(str(c) for c in candidates)
    )


def default_codex_candidates(environ=None):
    environ = environ or os.environ
    appdata = environ.get("APPDATA", "")
    roots = [
        Path(appdata) / "npm" / "node_modules" / "@openai" / "codex" / "bin" / "codex.js",
        Path(environ.get("PROGRAMFILES", "")) / "nodejs" / "node_modules" / "@openai" / "codex" / "bin" / "codex.js",
        REPO_ROOT / "node_modules" / "@openai" / "codex" / "bin" / "codex.js",
    ]
    return [r for r in roots if str(r) != "codex.js"]


# --------------------------------------------------------------------------- #
# The environment the run sees
# --------------------------------------------------------------------------- #


def sanitize_environment(parent, run_home: Path, run_temp=None, codex_home=None):
    """Build the run's environment from scratch instead of filtering the parent.

    Starting from nothing and adding back what is needed fails closed: a new
    provider variable in the parent does not silently reach the executor.
    """
    env = {}
    for name in KEPT_ENVIRONMENT:
        if name in parent:
            env[name] = parent[name]

    for name in HOME_VARIABLES:
        env[name] = str(run_home)
    for name in TEMP_VARIABLES:
        env[name] = str(run_temp or (run_home.parent / "tmp"))
    env[CODEX_HOME_VARIABLE] = str(codex_home or (run_home / ".codex"))

    # Belt and braces: if a kept variable ever overlaps a dropped prefix, the
    # drop wins.
    for name in list(env):
        if name in HOME_VARIABLES or name in TEMP_VARIABLES or name == CODEX_HOME_VARIABLE:
            continue
        if any(name.upper().startswith(prefix) for prefix in DROPPED_ENVIRONMENT_PREFIXES):
            del env[name]

    return env


def dropped_environment_names(parent):
    """Names (never values) of parent variables the run will not see."""
    return sorted(
        name
        for name in parent
        if name not in KEPT_ENVIRONMENT
        and name not in HOME_VARIABLES
        and name not in TEMP_VARIABLES
        and name != CODEX_HOME_VARIABLE
    )


# --------------------------------------------------------------------------- #
# Secrets
# --------------------------------------------------------------------------- #


def redact(text, secrets):
    """Replace known secret values, then obvious token shapes.

    Order matters and the reason is not obvious: the pattern pass must run before
    the tail is cut, because a token is recognised by its prefix. Cut between
    `Bearer ` and the value and the tail keeps the secret and loses the only
    thing that would have matched it.
    """
    if not text:
        return text

    for secret in sorted((s for s in secrets if s and len(s) >= 8), key=len, reverse=True):
        text = text.replace(secret, "[redacted]")

    out = []
    for token in text.split(" "):
        stripped = token.strip("\"',;)")
        if len(stripped) >= 20 and stripped.startswith(("sk-", "eyJ", "gho_", "ghp_", "Bearer")):
            out.append("[redacted]")
        else:
            out.append(token)
    return " ".join(out)


def collect_secret_values(auth_path: Path):
    """Secret values to redact, read from the credential file itself."""
    if not auth_path.is_file():
        return []
    try:
        data = json.loads(auth_path.read_text(encoding="utf-8"))
    except (ValueError, OSError):
        return []

    found = []

    def walk(node):
        if isinstance(node, dict):
            for value in node.values():
                walk(value)
        elif isinstance(node, list):
            for value in node:
                walk(value)
        elif isinstance(node, str) and len(node) >= 12:
            found.append(node)

    walk(data)
    return found


def tail(text, limit=TAIL_LIMIT):
    """The tail, not the head.

    500 characters of a JSONL event stream is the opening preamble. The field
    exists to tell a run that did nothing from one that worked, and the head
    cannot do that.
    """
    if not text or len(text) <= limit:
        return text or ""
    return "...[truncated]...\n" + text[-limit:]


# --------------------------------------------------------------------------- #
# The checkout guard
# --------------------------------------------------------------------------- #


def _git(args, cwd):
    result = subprocess.run(
        ["git"] + args,
        cwd=str(cwd),
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
    )
    return result.stdout.strip()


def checkout_fingerprint(repo: Path):
    """HEAD plus the content of everything already dirty.

    Hashing only `git status --porcelain` is not enough: overwriting a file that
    was already modified leaves the status output identical, so the change is
    invisible. The content of each dirty file is hashed too.
    """
    head = _git(["rev-parse", "HEAD"], repo)
    porcelain = _git(["status", "--porcelain"], repo)

    dirty = {}
    for line in porcelain.splitlines():
        path = line[3:].strip().strip('"')
        if not path or " -> " in path:
            continue
        full = repo / path
        if full.is_file():
            try:
                dirty[path] = hashlib.sha256(full.read_bytes()).hexdigest()
            except OSError:
                dirty[path] = "unreadable"

    return {"head": head, "porcelain": porcelain, "dirty": dirty}


def fingerprint_changes(before, after):
    changes = []
    if before.get("head") != after.get("head"):
        changes.append("HEAD moved: %s -> %s" % (before.get("head"), after.get("head")))
    if before.get("porcelain") != after.get("porcelain"):
        changes.append("working tree status changed")
    for path, digest in after.get("dirty", {}).items():
        if before.get("dirty", {}).get(path, digest) != digest:
            changes.append("already-modified file changed: " + path)
    return changes


# --------------------------------------------------------------------------- #
# Credentials
# --------------------------------------------------------------------------- #


def write_back_credentials(run_auth: Path, real_auth: Path):
    """Carry a refreshed credential back before the copy is discarded.

    Refuses anything that does not parse, and merges only the keys handed over,
    because the real file can hold several providers.
    """
    if not run_auth.is_file():
        return "no credential in the run home"
    if not real_auth.is_file():
        return "no operator credential to merge into; left the run copy in place"

    run_raw = run_auth.read_text(encoding="utf-8")
    real_raw = real_auth.read_text(encoding="utf-8")
    if run_raw == real_raw:
        return "unchanged"

    try:
        run_data = json.loads(run_raw)
    except ValueError:
        return "REFUSED: the run credential does not parse; operator store untouched"
    if not isinstance(run_data, dict) or not run_data:
        return "REFUSED: the run credential is not a non-empty object; operator store untouched"

    try:
        real_data = json.loads(real_raw)
    except ValueError:
        return "REFUSED: the operator credential does not parse; refusing to overwrite it"

    merged = dict(real_data)
    for key in run_data:
        merged[key] = run_data[key]

    backup = real_auth.with_name(real_auth.name + ".before-codex-run.bak")
    if not backup.exists():
        backup.write_text(real_raw, encoding="utf-8")

    real_auth.write_text(json.dumps(merged, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return "written back (%s); previous kept as %s" % (", ".join(sorted(run_data)), backup.name)


def remove_credential_copy(run_auth: Path):
    """Remove only the credential, keeping the run's logs and provenance.

    EXECUTOR_SETUP prescribes deleting the whole run directory. Keeping it and
    deleting just the credential is a deliberate deviation: the provenance record
    is the only evidence of what an executor did, and a refreshed token that
    failed to write back is still recoverable from disk rather than lost.
    """
    try:
        if run_auth.is_file():
            run_auth.chmod(run_auth.stat().st_mode | stat.S_IWRITE)
            run_auth.unlink()
            return "removed"
        return "absent"
    except OSError as exc:
        # Never raise from cleanup: that turns a run which already happened into
        # no record at all.
        return "could not remove: %s" % exc


def force_writable(path: Path):
    """Add the write bit, never assign a mode.

    `chmod(S_IWRITE | S_IREAD)` looks fine on Windows and strips the execute bit
    off every directory on POSIX, after which nothing below can be reached.
    """
    try:
        path.chmod(path.stat().st_mode | stat.S_IWRITE)
    except OSError:
        pass


# --------------------------------------------------------------------------- #
# The run
# --------------------------------------------------------------------------- #


@dataclass
class RunPlan:
    codex_js: Path
    brief: str
    cwd: Path
    run_dir: Path
    home: Path
    codex_home: Path
    timeout_seconds: int
    argv: list = field(default_factory=list)
    env: dict = field(default_factory=dict)


def build_argv(codex_js: Path, cwd: Path, last_message: Path, node="node"):
    """The launch line, with every isolation flag this CLI version offers.

    `--ignore-user-config` is the important one: it does not load
    `$CODEX_HOME/config.toml` while still taking auth from `CODEX_HOME`. That is
    why this launcher writes no replacement config -- the operator's ten
    `[mcp_servers.*]` sections, `danger-full-access` and `features.search` are
    not loaded at all, structurally, rather than being overridden one by one.

    `--approve-for-me` already selects the workspace-write sandbox, so `--sandbox`
    is not passed alongside it; the CLI refuses the combination as contradictory.
    """
    argv = [
        node,
        str(codex_js),
        "exec",
        "--approve-for-me",
        "--ignore-user-config",
        "--ignore-rules",
        "--json",
        "--skip-git-repo-check",
        "-C",
        str(cwd),
        "-o",
        str(last_message),
    ]
    for feature in DISABLED_FEATURES:
        argv += ["--disable", feature]
    argv += [
        "-c",
        "tools.web_search=false",
        "-c",
        "tools.image_gen=false",
        # web__run is a hosted tool that survives the absence of --search and
        # `tools.web_search=false`. An executor probe called it twice and fetched
        # a live page, so it is removed by name.
        "-c",
        "tools.web__run=false",
        "-c",
        "tools.web=false",
        # The shell the model runs gets a core environment, not none: with
        # inherit=none it could not find `cmd`, which also means it could not run
        # the acceptance command the brief hands it. Isolation comes from the
        # redirected home and the sandbox, not from starving PATH.
        "-c",
        "shell_environment_policy.inherit=core",
        # The brief arrives on stdin rather than as an argument. That is also why
        # the npm shim would have been fatal: a batch wrapper truncates an
        # argument at its first newline, and a brief is many lines.
        "-",
    ]
    return argv


def prepare_run(brief_text, cwd: Path, timeout_seconds: int, label: str, environ=None):
    environ = environ or os.environ
    stamp = time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
    run_dir = RUNS_DIR / ("%s-%s" % (stamp, label))
    home = run_dir / "home"
    codex_home = home / ".codex"
    codex_home.mkdir(parents=True, exist_ok=True)
    run_temp = run_dir / "tmp"
    run_temp.mkdir(parents=True, exist_ok=True)

    # Rebuild .gitconfig from identity alone.
    name = _git(["config", "user.name"], REPO_ROOT)
    email = _git(["config", "user.email"], REPO_ROOT)
    (home / ".gitconfig").write_text(
        "[user]\n\tname = %s\n\temail = %s\n" % (name, email),
        encoding="utf-8",
    )

    codex_js = resolve_codex_entry(default_codex_candidates(environ))
    last_message = run_dir / "last_message.txt"

    plan = RunPlan(
        codex_js=codex_js,
        brief=brief_text,
        cwd=cwd,
        run_dir=run_dir,
        home=home,
        codex_home=codex_home,
        timeout_seconds=timeout_seconds,
    )
    plan.argv = build_argv(codex_js, cwd, last_message)
    plan.env = sanitize_environment(environ, home, run_temp, codex_home)
    return plan


def digest_directory(root: Path):
    out = {}
    if not root.is_dir():
        return out
    for path in sorted(root.rglob("*")):
        if path.is_file():
            try:
                out[str(path.relative_to(root)).replace("\\", "/")] = hashlib.sha256(
                    path.read_bytes()
                ).hexdigest()[:16]
            except OSError:
                out[str(path.relative_to(root)).replace("\\", "/")] = "unreadable"
    return out


def run(plan: RunPlan, real_auth: Path):
    plan.run_dir.mkdir(parents=True, exist_ok=True)

    run_auth = plan.codex_home / "auth.json"
    if real_auth.is_file():
        shutil.copyfile(str(real_auth), str(run_auth))

    secrets = collect_secret_values(run_auth)
    before = checkout_fingerprint(REPO_ROOT)

    started = time.time()
    timed_out = False
    try:
        completed = subprocess.run(
            plan.argv,
            input=plan.brief,
            env=plan.env,
            cwd=str(plan.cwd),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=plan.timeout_seconds,
        )
        exit_code = completed.returncode
        stdout, stderr = completed.stdout, completed.stderr
    except subprocess.TimeoutExpired as expired:
        timed_out = True
        exit_code = None
        stdout = expired.stdout or ""
        stderr = expired.stderr or ""
        if isinstance(stdout, bytes):
            stdout = stdout.decode("utf-8", "replace")
        if isinstance(stderr, bytes):
            stderr = stderr.decode("utf-8", "replace")

    duration = round(time.time() - started, 1)
    after = checkout_fingerprint(REPO_ROOT)

    (plan.run_dir / "stdout.jsonl").write_text(redact(stdout, secrets), encoding="utf-8")
    (plan.run_dir / "stderr.txt").write_text(redact(stderr, secrets), encoding="utf-8")

    credential_status = write_back_credentials(run_auth, real_auth)
    credential_removal = remove_credential_copy(run_auth)

    record = {
        "label": plan.run_dir.name,
        "codex_entry": str(plan.codex_js),
        "argv": [a for a in plan.argv],
        "cwd": str(plan.cwd),
        "home": str(plan.home),
        "home_digest": digest_directory(plan.home),
        "environment_variable_names": sorted(plan.env),
        "dropped_environment_variable_names": dropped_environment_names(os.environ),
        "exit_code": exit_code,
        "timed_out": timed_out,
        "duration_seconds": duration,
        "checkout_before": before,
        "checkout_after": after,
        "checkout_changes": fingerprint_changes(before, after),
        "credential_write_back": credential_status,
        "credential_copy": credential_removal,
        "stdout_tail": tail(redact(stdout, secrets)),
        "stderr_tail": tail(redact(stderr, secrets)),
    }
    (plan.run_dir / "provenance.json").write_text(
        json.dumps(record, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
    )
    return record


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--brief", required=True, help="path to the task brief")
    parser.add_argument("--cwd", default=str(REPO_ROOT), help="working root for the executor")
    parser.add_argument("--timeout", type=int, default=1800, help="seconds before the run is killed")
    parser.add_argument("--label", default="run", help="short label for the run directory")
    parser.add_argument("--dry-run", action="store_true", help="print the plan and run nothing")
    args = parser.parse_args(argv)

    # --brief takes a path, never the text. A brief is multi-line by definition,
    # so the one place it must never be is a command-line argument: the npm shim
    # would truncate it at the first newline. It is read from the file and handed
    # over on stdin, and size-checked against the same 2 KB docs/TASK_BRIEF.md
    # allows -- a brief that outgrows the template is a brief that has stopped
    # naming one checkable fact.
    brief_path = Path(args.brief)
    if not brief_path.is_file():
        raise ExecutorError("brief not found: %s" % brief_path)
    brief_text = brief_path.read_text(encoding="utf-8")
    if len(brief_text.encode("utf-8")) > MAX_BRIEF_BYTES:
        raise ExecutorError(
            "brief is %d bytes, over the %d byte limit docs/TASK_BRIEF.md sets. "
            "Cut it rather than raising the limit: the length is the symptom."
            % (len(brief_text.encode("utf-8")), MAX_BRIEF_BYTES)
        )

    plan = prepare_run(brief_text, Path(args.cwd).resolve(), args.timeout, args.label)

    if args.dry_run:
        print("codex entry : %s" % plan.codex_js)
        print("argv        : %s" % " ".join(plan.argv))
        print("cwd         : %s" % plan.cwd)
        print("home        : %s" % plan.home)
        print("env passed  : %s" % ", ".join(sorted(plan.env)))
        print("env dropped : %d variables" % len(dropped_environment_names(os.environ)))
        print("brief bytes : %d, lines: %d" % (len(brief_text), len(brief_text.splitlines())))
        return 0

    real_auth = Path(os.environ.get("USERPROFILE", str(Path.home()))) / ".codex" / "auth.json"
    record = run(plan, real_auth)

    print("run          : %s" % plan.run_dir)
    print("exit code    : %s%s" % (record["exit_code"], " (timed out)" if record["timed_out"] else ""))
    print("duration     : %ss" % record["duration_seconds"])
    print("credential   : %s / copy %s" % (record["credential_write_back"], record["credential_copy"]))
    if record["checkout_changes"]:
        print("CHECKOUT MOVED:")
        for change in record["checkout_changes"]:
            print("  - %s" % change)
    else:
        print("checkout     : unchanged outside the executor's own edits")
    return 0 if record["exit_code"] == 0 else 1


if __name__ == "__main__":
    try:
        sys.exit(main())
    except ExecutorError as exc:
        print("error: %s" % exc, file=sys.stderr)
        sys.exit(2)
