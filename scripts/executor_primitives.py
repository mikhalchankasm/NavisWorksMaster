#!/usr/bin/env python3
"""Primitives shared by the reviewer wrapper and the executor-mode modules.

The bounded process runner and the path, environment and provider-profile
guards used to live in external_executor.py, and the executor-mode modules
imported them from there, which made the reviewer wrapper the accidental
owner of code it merely shares. They move here unchanged: same bodies, same
constant values, one home. external_executor.py imports them back, so its
module surface is what it always was.

Nothing here knows about sandboxes, proposals or reviewer mode; this is
process, path, environment and launcher plumbing only.
"""

from __future__ import annotations

import ctypes
import os
import re
import shutil
import signal
import stat
import subprocess
import sys
import tempfile
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Sequence


MAX_FILE_BYTES = 32_000
MAX_RESPONSE_BYTES = 256_000
MIN_TIMEOUT_SECONDS = 10
MAX_TIMEOUT_SECONDS = 1800
# cmd.exe treats a PATH longer than 8191 characters as absent: every lookup in
# it fails, including the `node` an npm shim runs. A child PATH past this
# limit is reduced in stages; one at or under it passes through unchanged.
MAX_CHILD_PATH_CHARS = 8000
PROVIDERS = ("claude", "kimi", "glm", "glm-flash")
GLM_PROVIDERS = frozenset({"glm", "glm-flash"})
KIMI_SUPPORTED_EFFORTS = frozenset({"low", "high", "max"})
EXPECTED_PROFILES = {
    "claude": {"model": "claude-sonnet-5", "effort": "low", "authMode": "Claude Pro OAuth"},
    "kimi": {"model": "kimi-code/k3", "effort": "high", "authMode": "Kimi Code managed OAuth"},
    "glm": {
        "model": "zai-coding-plan/glm-5.3",
        "effort": "max",
        "authMode": "Z.AI Coding Plan credential",
        "endpoint": "https://api.z.ai/api/coding/paas/v4",
    },
    "glm-flash": {
        "model": "zai-coding-plan/glm-5.3-flash",
        "effort": "max",
        "authMode": "Z.AI Coding Plan credential",
        "endpoint": "https://api.z.ai/api/coding/paas/v4",
    },
}
BASE_ENV_NAMES = frozenset(
    {
        "ALLUSERSPROFILE",
        "APPDATA",
        "COMSPEC",
        "HOME",
        "HOMEDRIVE",
        "HOMEPATH",
        "LANG",
        "LANGUAGE",
        "LC_ALL",
        "LC_CTYPE",
        "LOCALAPPDATA",
        "LOGNAME",
        "NO_COLOR",
        "OS",
        "PATH",
        "PATHEXT",
        "PROGRAMDATA",
        "PROGRAMFILES",
        "PROGRAMFILES(X86)",
        "PROGRAMW6432",
        "SYSTEMDRIVE",
        "SYSTEMROOT",
        "TEMP",
        "TERM",
        "TMP",
        "TMPDIR",
        "USER",
        "USERDOMAIN",
        "USERNAME",
        "USERPROFILE",
        "WINDIR",
    }
)
GLM_CREDENTIAL_ENV_NAMES = frozenset({"ZAI_API_KEY", "ZHIPU_API_KEY"})
# Fixed values, never forwarded from the operator. Redirecting USERPROFILE moves
# dotnet's files but not the registry: on first run in a fresh home dotnet
# appended <home>\.dotnet\tools to the real HKCU\Environment\Path, and on the
# 2026-09-24 incident machine that left 340 dead entries and a user PATH over
# the 32 767-character limit.
DOTNET_FIXED_ENV = {
    "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH": "0",
    "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1",
    "DOTNET_NOLOGO": "1",
    "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
    # Executors in parallel worktrees must not attach to each other's MSBuild worker nodes: a
    # shared node crashed with 0xe0434352 and left an error dialog on the owner's screen
    # (2026-09-27 and 2026-09-28).
    "MSBUILDDISABLENODEREUSE": "1",
    # A worker node that never gets its parent's connection waits this long before it exits, 15 min
    # by default. During one Codex run on 2026-09-28, 131 such nodes (~4 GB) piled up with their
    # parents gone; a node started by hand exited after 5.2 s with 5000. The node count itself cannot
    # be capped from the environment - see executor_msbuild_nodes.
    "MSBUILDNODECONNECTIONTIMEOUT": "60000",
}
KIMI_ISOLATED_CONFIG_TEMPLATE = """default_model = "kimi-code/k3"
default_permission_mode = "manual"
default_plan_mode = false
merge_all_available_skills = false
extra_skill_dirs = []
telemetry = false

[providers."managed:kimi-code"]
type = "kimi"
api_key = ""
base_url = "https://api.kimi.com/coding/v1"

[providers."managed:kimi-code".oauth]
storage = "file"
key = "oauth/kimi-code"

[models."kimi-code/k3"]
provider = "managed:kimi-code"
model = "k3"
max_context_size = 1048576
capabilities = [ "thinking", "always_thinking", "image_in", "video_in", "tool_use" ]
display_name = "K3"
support_efforts = [ "low", "high", "max" ]
default_effort = "{effort}"

[thinking]
enabled = true
effort = "{effort}"

[background]
max_running_tasks = 1
keep_alive_on_exit = false
"""


class ExecutorError(RuntimeError):
    def __init__(
        self,
        category: str,
        message: str,
        *,
        exit_code: int = 2,
        artifact_path: str | None = None,
    ) -> None:
        super().__init__(message)
        self.category = category
        self.exit_code = exit_code
        self.artifact_path = artifact_path


@dataclass(frozen=True)
class ProcessResult:
    return_code: int
    stdout: str
    stderr: str
    duration_seconds: float
    timed_out: bool = False
    output_too_large: bool = False


def _repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


@dataclass(frozen=True)
class ChildPath:
    """The PATH a child process receives, and how it was derived."""

    value: str
    policy: str
    before_chars: int
    after_chars: int

    def record(self) -> dict[str, object]:
        """Policy and character counts only; the record never carries a PATH value."""
        return {"policy": self.policy, "beforeChars": self.before_chars, "afterChars": self.after_chars}


def _unquote_path_entry(entry: str) -> str:
    stripped = entry.strip()
    if len(stripped) >= 2 and stripped[0] == stripped[-1] == '"':
        return stripped[1:-1]
    return stripped


def _path_entry_key(entry: str) -> str:
    return os.path.normcase(os.path.normpath(_unquote_path_entry(entry)))


def _shim_path_keys(shim: Path | None) -> set[str]:
    """Directories a CLI shim and its child processes still have to find.

    The shim itself is launched by absolute path. What breaks when cmd.exe
    sees a PATH it cannot use is everything looked up by name inside it:
    node for an npm shim, then git, dotnet, PowerShell and python for what
    a run may shell out to, plus the Windows system directories and the
    directory the shim lives in.
    """
    keys: set[str] = set()

    def keep(directory: str | Path) -> None:
        keys.add(_path_entry_key(str(directory)))

    if shim is not None:
        keep(shim.parent)
    for tool in ("node", "git", "dotnet", "pwsh", "powershell"):
        found = shutil.which(tool)
        if found:
            keep(os.path.dirname(found))
    system_root = os.environ.get("SystemRoot")
    if system_root:
        system = Path(system_root)
        for directory in (
            system,
            system / "System32",
            system / "System32" / "Wbem",
            system / "System32" / "WindowsPowerShell" / "v1.0",
        ):
            keep(directory)
    python_directory = Path(sys.executable).resolve().parent
    for directory in (python_directory, python_directory / "Scripts", python_directory.parent / "Scripts"):
        keep(directory)
    return keys


def _reduce_child_path(value: str, *, shim: Path | None = None) -> ChildPath:
    """Reduce a Windows PATH cmd.exe cannot use, in stages, keeping order.

    Anything at or under the limit passes through untouched, duplicates and
    dead directories included: reduction starts only once cmd.exe would
    treat the PATH as absent. Then directories that do not exist go, then
    duplicates, and only if that is still not enough does the PATH narrow
    to what a CLI shim needs.
    """
    if os.name != "nt" or len(value) <= MAX_CHILD_PATH_CHARS:
        return ChildPath(value, "passthrough", len(value), len(value))
    existing = [
        entry
        for entry in value.split(";")
        if _unquote_path_entry(entry) and Path(_unquote_path_entry(entry)).is_dir()
    ]
    unique: list[str] = []
    seen: set[str] = set()
    for entry in existing:
        key = _path_entry_key(entry)
        if key not in seen:
            seen.add(key)
            unique.append(entry)
    pruned = ";".join(unique)
    if len(pruned) <= MAX_CHILD_PATH_CHARS:
        return ChildPath(pruned, "pruned", len(value), len(pruned))
    shim_keys = _shim_path_keys(shim)
    minimal = ";".join(entry for entry in unique if _path_entry_key(entry) in shim_keys)
    return ChildPath(minimal, "minimal", len(value), len(minimal))


def _sanitize_environment(
    provider: str, source: dict[str, str] | None = None, *, shim: Path | None = None
) -> tuple[dict[str, str], ChildPath | None]:
    source_env = source if source is not None else os.environ
    allowed = BASE_ENV_NAMES | (GLM_CREDENTIAL_ENV_NAMES if provider in GLM_PROVIDERS else frozenset())
    if os.name == "nt":
        normalized_source = {name.upper(): value for name, value in source_env.items()}
        env = {name: normalized_source[name] for name in allowed if name in normalized_source}
    else:
        env = {name: source_env[name] for name in allowed if name in source_env}
    env.update(DOTNET_FIXED_ENV)
    child_path: ChildPath | None = None
    if os.name == "nt" and "PATH" in env:
        child_path = _reduce_child_path(env["PATH"], shim=shim)
        env["PATH"] = child_path.value
    if provider == "kimi":
        env.update(
            {
                "KIMI_CODE_BACKGROUND_KEEP_ALIVE_ON_EXIT": "0",
                "KIMI_CODE_EXPERIMENTAL_FLAG": "1",
                "KIMI_CODE_NO_AUTO_UPDATE": "1",
                "KIMI_DISABLE_CRON": "1",
                "KIMI_DISABLE_TELEMETRY": "1",
            }
        )
    return env, child_path


def _sanitized_environment(
    provider: str, source: dict[str, str] | None = None, *, shim: Path | None = None
) -> dict[str, str]:
    return _sanitize_environment(provider, source, shim=shim)[0]


def _redact(text: str, limit: int = 500) -> str:
    """Scrub known secret shapes and cap the length.

    The cap is a parameter because a provenance record needs more than a log
    line: 500 characters of a JSONL event stream is its opening preamble and
    nothing else, which is useless for the case the field exists for.
    """
    return redact_patterns(text.replace("\r", " ").replace("\n", " ")).strip()[:limit]


def redact_patterns(value: str) -> str:
    """Secret-named environment values and token shapes, with line breaks and length left as they are.

    The event journal (#1069) applies this to every string it keeps, so a token a command printed is removed
    there as it is from the record's tail.
    """
    for name, secret in os.environ.items():
        if len(secret) >= 6 and any(
            fragment in name.upper() for fragment in ("API_KEY", "TOKEN", "SECRET", "PASSWORD", "CREDENTIAL", "COOKIE")
        ):
            value = value.replace(secret, "[REDACTED]")
    value = re.sub(r"(?i)bearer\s+[A-Za-z0-9._~+/=-]+", "Bearer [REDACTED]", value)
    value = re.sub(r"(?i)\b(?:sk|key)-[A-Za-z0-9_-]{8,}\b", "[REDACTED]", value)
    value = re.sub(r"(?i)(token|api[_-]?key|authorization)\s*[:=]\s*\S+", r"\1=[REDACTED]", value)
    return re.sub(r"\b[^\s@]+@[^\s@]+\.[^\s@]+\b", "[REDACTED_EMAIL]", value)


def _is_reparse_point(path: Path) -> bool:
    if path.is_symlink():
        return True
    is_junction = getattr(path, "is_junction", None)
    if callable(is_junction) and is_junction():
        return True
    try:
        attributes = getattr(path.lstat(), "st_file_attributes", 0)
    except OSError:
        return False
    return bool(attributes & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0))


def _canonical_path(path: Path) -> Path:
    return path.resolve(strict=False)


def _reject_reparse_path(root: Path, target: Path, *, category: str, label: str) -> None:
    absolute_target = target.absolute()
    canonical_root = _canonical_path(root.absolute())
    canonical_target = _canonical_path(absolute_target)
    try:
        canonical_target.relative_to(canonical_root)
    except ValueError as exc:
        raise ExecutorError(category, f"{label} is outside the sandbox") from exc

    # Walk the caller-supplied path back to the canonical root so Windows 8.3
    # aliases are accepted without resolving away a symlink or junction first.
    current = absolute_target
    visited: set[str] = set()
    while True:
        key = os.path.normcase(os.path.normpath(str(current)))
        if key in visited:
            raise ExecutorError(category, f"{label} has an unsafe path cycle")
        visited.add(key)
        if _is_reparse_point(current):
            raise ExecutorError(category, f"{label} crosses a symlink or reparse point")
        if _canonical_path(current) == canonical_root:
            return
        parent = current.parent
        if parent == current:
            raise ExecutorError(category, f"{label} is outside the sandbox")
        current = parent


def _remove_runtime_dir(root: Path, runtime_dir: Path) -> None:
    _reject_reparse_path(root, runtime_dir, category="SESSION_CLEANUP_FAILED", label="Runtime directory")
    for current, directories, files in os.walk(runtime_dir, topdown=True, followlinks=False):
        current_path = Path(current)
        for name in [*directories, *files]:
            child = current_path / name
            if _is_reparse_point(child):
                raise ExecutorError("SESSION_CLEANUP_FAILED", "Runtime directory contains a reparse point")
            # Git writes its object files read-only, and a provider that keeps a
            # snapshot repository inside its data directory leaves a tree full of
            # them. On Windows rmtree cannot delete a read-only file, so the
            # directory - with its copy of a real credential - would survive.
            #
            # Bits are added, never assigned. Assigning S_IWRITE | S_IREAD looks
            # equivalent on Windows, where chmod only moves the read-only flag,
            # and on POSIX it strips the execute bit off every directory, after
            # which the tree cannot be traversed or deleted at all.
            try:
                mode = child.lstat().st_mode
                os.chmod(child, mode | stat.S_IWUSR | (stat.S_IXUSR if child.is_dir() else 0))
            except OSError:
                pass
    try:
        shutil.rmtree(runtime_dir)
    except OSError as exc:
        raise ExecutorError("SESSION_CLEANUP_FAILED", f"Runtime directory could not be removed: {exc}") from exc


# Where each npm-installed CLI keeps its real entry point, relative to the
# directory holding the shim.
NPM_ENTRY_POINTS = {
    "opencode": ("node_modules", "opencode-ai", "bin", "opencode.exe"),
    "codex": ("node_modules", "@openai", "codex", "bin", "codex.js"),
}


def _cli_name(provider: str) -> str:
    if provider in GLM_PROVIDERS:
        return "opencode"
    if provider in {"codex", "codex-plan"}:
        return "codex"
    return provider


def _prefer_native_binary(provider: str, resolved: Path) -> Path:
    """Step past an npm shim to the real entry point.

    On Windows `shutil.which` finds the .cmd shim. A batch wrapper truncates an
    argument at its first newline, so a multi-line prompt reaches the CLI as its
    first line only - the rest is silently dropped. That is what a GLM run doing
    nothing and reporting success looks like from outside, and Codex is
    installed the same way.
    """
    entry = NPM_ENTRY_POINTS.get(_cli_name(provider))
    if entry is None or resolved.suffix.lower() in {".exe", ".js"}:
        return resolved
    native = resolved.parent.joinpath(*entry)
    return native.resolve() if native.is_file() else resolved


def _resolve_executable(provider: str, override: str | None = None) -> Path:
    if override:
        candidate = Path(override).expanduser()
        if candidate.is_file():
            return _validate_executable_name(provider, _prefer_native_binary(provider, candidate.resolve()))
        raise ExecutorError("CLI_MISSING", f"Executable does not exist: {candidate}")
    env_name = f"AVOX_EXTERNAL_{provider.replace('-', '_').upper()}_CLI"
    if os.environ.get(env_name):
        return _resolve_executable(provider, os.environ[env_name])
    command = _cli_name(provider)
    found = shutil.which(command)
    if found:
        return _validate_executable_name(provider, _prefer_native_binary(provider, Path(found).resolve()))
    home = Path.home()
    fallbacks = {
        "claude": home / ".local" / "bin" / "claude.exe",
        "kimi": home / ".kimi-code" / "bin" / "kimi.exe",
        "opencode": home / "AppData" / "Roaming" / "npm" / "opencode.ps1",
        "codex": home / "AppData" / "Roaming" / "npm" / "codex.cmd",
        "qwen": home / "AppData" / "Roaming" / "npm" / "qwen.cmd",
    }
    candidate = fallbacks[command]
    if candidate.is_file():
        return _validate_executable_name(provider, _prefer_native_binary(provider, candidate.resolve()))
    raise ExecutorError("CLI_MISSING", f"{command} CLI was not found")


def _validate_executable_name(provider: str, path: Path) -> Path:
    expected = _cli_name(provider)
    if path.stem.lower() != expected:
        raise ExecutorError("CLI_INVALID", f"{provider} executable must be named {expected}")
    return path


def _launcher(path: Path, arguments: Sequence[str]) -> list[str]:
    if path.suffix.lower() == ".js":
        node = shutil.which("node")
        if not node:
            raise ExecutorError("CLI_MISSING", "node was not found to run the CLI entry point")
        return [node, str(path), *arguments]
    if path.suffix.lower() == ".ps1":
        shell = shutil.which("pwsh") or shutil.which("powershell")
        if not shell:
            raise ExecutorError("CLI_MISSING", "PowerShell launcher was not found")
        return [shell, "-NoProfile", "-File", str(path), *arguments]
    return [str(path), *arguments]


class _WindowsJob:
    """Kill-on-close Windows Job Object used for one external CLI run."""

    def __init__(self) -> None:
        from ctypes import wintypes

        class BasicLimitInformation(ctypes.Structure):
            _fields_ = [
                ("PerProcessUserTimeLimit", ctypes.c_longlong),
                ("PerJobUserTimeLimit", ctypes.c_longlong),
                ("LimitFlags", wintypes.DWORD),
                ("MinimumWorkingSetSize", ctypes.c_size_t),
                ("MaximumWorkingSetSize", ctypes.c_size_t),
                ("ActiveProcessLimit", wintypes.DWORD),
                ("Affinity", ctypes.c_size_t),
                ("PriorityClass", wintypes.DWORD),
                ("SchedulingClass", wintypes.DWORD),
            ]

        class IoCounters(ctypes.Structure):
            _fields_ = [
                ("ReadOperationCount", ctypes.c_ulonglong),
                ("WriteOperationCount", ctypes.c_ulonglong),
                ("OtherOperationCount", ctypes.c_ulonglong),
                ("ReadTransferCount", ctypes.c_ulonglong),
                ("WriteTransferCount", ctypes.c_ulonglong),
                ("OtherTransferCount", ctypes.c_ulonglong),
            ]

        class ExtendedLimitInformation(ctypes.Structure):
            _fields_ = [
                ("BasicLimitInformation", BasicLimitInformation),
                ("IoInfo", IoCounters),
                ("ProcessMemoryLimit", ctypes.c_size_t),
                ("JobMemoryLimit", ctypes.c_size_t),
                ("PeakProcessMemoryUsed", ctypes.c_size_t),
                ("PeakJobMemoryUsed", ctypes.c_size_t),
            ]

        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
        kernel32.CreateJobObjectW.restype = wintypes.HANDLE
        kernel32.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
        kernel32.SetInformationJobObject.restype = wintypes.BOOL
        kernel32.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
        kernel32.AssignProcessToJobObject.restype = wintypes.BOOL
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.CloseHandle.restype = wintypes.BOOL

        handle = kernel32.CreateJobObjectW(None, None)
        if not handle:
            raise ctypes.WinError(ctypes.get_last_error())
        limits = ExtendedLimitInformation()
        limits.BasicLimitInformation.LimitFlags = 0x00002000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if not kernel32.SetInformationJobObject(handle, 9, ctypes.byref(limits), ctypes.sizeof(limits)):
            error = ctypes.get_last_error()
            kernel32.CloseHandle(handle)
            raise ctypes.WinError(error)
        self._kernel32 = kernel32
        self._handle: int | None = handle
        self.assigned = False

    def assign(self, process: subprocess.Popen[bytes]) -> None:
        from ctypes import wintypes

        if self._handle is None or not self._kernel32.AssignProcessToJobObject(
            self._handle, wintypes.HANDLE(int(process._handle))  # type: ignore[attr-defined]
        ):
            raise ctypes.WinError(ctypes.get_last_error())
        self.assigned = True

    def close(self) -> None:
        if self._handle is not None:
            self._kernel32.CloseHandle(self._handle)
            self._handle = None


def _resume_windows_process(process: subprocess.Popen[bytes]) -> None:
    """Resume every initial thread after the suspended process joins its Job."""
    from ctypes import wintypes

    class ThreadEntry32(ctypes.Structure):
        _fields_ = [
            ("dwSize", wintypes.DWORD),
            ("cntUsage", wintypes.DWORD),
            ("th32ThreadID", wintypes.DWORD),
            ("th32OwnerProcessID", wintypes.DWORD),
            ("tpBasePri", wintypes.LONG),
            ("tpDeltaPri", wintypes.LONG),
            ("dwFlags", wintypes.DWORD),
        ]

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
    kernel32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    kernel32.Thread32First.argtypes = [wintypes.HANDLE, ctypes.POINTER(ThreadEntry32)]
    kernel32.Thread32First.restype = wintypes.BOOL
    kernel32.Thread32Next.argtypes = [wintypes.HANDLE, ctypes.POINTER(ThreadEntry32)]
    kernel32.Thread32Next.restype = wintypes.BOOL
    kernel32.OpenThread.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel32.OpenThread.restype = wintypes.HANDLE
    kernel32.ResumeThread.argtypes = [wintypes.HANDLE]
    kernel32.ResumeThread.restype = wintypes.DWORD
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL

    snapshot = kernel32.CreateToolhelp32Snapshot(0x00000004, 0)  # TH32CS_SNAPTHREAD
    if snapshot == ctypes.c_void_p(-1).value:
        raise ctypes.WinError(ctypes.get_last_error())
    resumed = 0
    try:
        entry = ThreadEntry32()
        entry.dwSize = ctypes.sizeof(entry)
        found = bool(kernel32.Thread32First(snapshot, ctypes.byref(entry)))
        while found:
            if entry.th32OwnerProcessID == process.pid:
                thread = kernel32.OpenThread(0x0002, False, entry.th32ThreadID)  # THREAD_SUSPEND_RESUME
                if not thread:
                    raise ctypes.WinError(ctypes.get_last_error())
                try:
                    if kernel32.ResumeThread(thread) == 0xFFFFFFFF:
                        raise ctypes.WinError(ctypes.get_last_error())
                    resumed += 1
                finally:
                    kernel32.CloseHandle(thread)
            found = bool(kernel32.Thread32Next(snapshot, ctypes.byref(entry)))
    finally:
        kernel32.CloseHandle(snapshot)
    if resumed == 0:
        raise OSError("Cannot locate the suspended external CLI thread")


def _stop_process_tree(process: subprocess.Popen[bytes], windows_job: _WindowsJob | None = None) -> None:
    if os.name == "nt":
        if windows_job is not None:
            assigned = windows_job.assigned
            windows_job.close()
        else:
            assigned = False
        if not assigned and process.poll() is None:
            subprocess.run(
                ["taskkill.exe", "/PID", str(process.pid), "/T", "/F"],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                check=False,
                creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
            )
    else:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.kill()


def _run_process(
    argv: Sequence[str],
    *,
    cwd: Path,
    timeout_seconds: int,
    env: dict[str, str],
    stdin_text: str | None = None,
    spool_dir: Path | None = None,
    max_output_bytes: int = MAX_RESPONSE_BYTES,
) -> ProcessResult:
    started = time.monotonic()
    launch_options: dict[str, Any]
    windows_job: _WindowsJob | None = None
    if os.name == "nt":
        launch_options = {
            "creationflags": getattr(subprocess, "CREATE_NO_WINDOW", 0)
            | getattr(subprocess, "CREATE_SUSPENDED", 0x00000004)
        }
    else:
        launch_options = {"start_new_session": True}
    spool_dir = cwd if spool_dir is None else spool_dir
    with (
        tempfile.TemporaryFile(dir=spool_dir) as stdout_file,
        tempfile.TemporaryFile(dir=spool_dir) as stderr_file,
    ):
        stdin_context = tempfile.TemporaryFile(dir=spool_dir) if stdin_text is not None else None
        process: subprocess.Popen[bytes] | None = None
        process_tree_stopped = False
        try:
            if os.name == "nt":
                try:
                    windows_job = _WindowsJob()
                except OSError as exc:
                    raise ExecutorError(
                        "CLI_FAILURE", f"Cannot create a bounded Windows process job: {_redact(str(exc))}"
                    ) from exc
            if stdin_context is not None:
                stdin_context.write(stdin_text.encode("utf-8"))
                stdin_context.seek(0)
            try:
                process = subprocess.Popen(
                    list(argv),
                    cwd=str(cwd),
                    env=env,
                    stdin=stdin_context if stdin_context is not None else subprocess.DEVNULL,
                    stdout=stdout_file,
                    stderr=stderr_file,
                    **launch_options,
                )
                if windows_job is not None:
                    windows_job.assign(process)
                    _resume_windows_process(process)
            except OSError as exc:
                if process is not None:
                    _stop_process_tree(process, windows_job)
                    process_tree_stopped = True
                    windows_job = None
                raise ExecutorError("CLI_FAILURE", f"Cannot start external CLI: {_redact(str(exc))}") from exc

            deadline = started + timeout_seconds
            timed_out = False
            output_too_large = False
            while process.poll() is None:
                stdout_size = os.fstat(stdout_file.fileno()).st_size
                stderr_size = os.fstat(stderr_file.fileno()).st_size
                if stdout_size > max_output_bytes or stderr_size > max_output_bytes:
                    output_too_large = True
                    _stop_process_tree(process, windows_job)
                    process_tree_stopped = True
                    windows_job = None
                    break
                if time.monotonic() >= deadline:
                    timed_out = True
                    _stop_process_tree(process, windows_job)
                    process_tree_stopped = True
                    windows_job = None
                    break
                time.sleep(0.01)
            if process.poll() is None:
                _stop_process_tree(process, windows_job)
                process_tree_stopped = True
                windows_job = None
            else:
                # The session leader may exit after leaving background children.
                # Closing the Windows Job or killing the POSIX process group
                # terminates those descendants before any runtime is removed.
                _stop_process_tree(process, windows_job)
                process_tree_stopped = True
                windows_job = None
            stdout_size = os.fstat(stdout_file.fileno()).st_size
            stderr_size = os.fstat(stderr_file.fileno()).st_size
            output_too_large = output_too_large or stdout_size > max_output_bytes or stderr_size > max_output_bytes
            stdout_file.seek(0)
            stderr_file.seek(0)
            stdout = stdout_file.read(max_output_bytes)
            stderr = stderr_file.read(max_output_bytes)
            return ProcessResult(
                124 if timed_out else (125 if output_too_large else (process.returncode or 0)),
                stdout.decode("utf-8", errors="replace"),
                stderr.decode("utf-8", errors="replace"),
                time.monotonic() - started,
                timed_out,
                output_too_large,
            )
        except BaseException:
            if process is not None and not process_tree_stopped:
                _stop_process_tree(process, windows_job)
                windows_job = None
            raise
        finally:
            if windows_job is not None:
                windows_job.close()
            if stdin_context is not None:
                stdin_context.close()


def _durable_replace(source: Path, destination: Path) -> None:
    if os.name == "nt":
        from ctypes import wintypes

        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.MoveFileExW.argtypes = [wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.DWORD]
        kernel32.MoveFileExW.restype = wintypes.BOOL
        flags = 0x00000001 | 0x00000008  # REPLACE_EXISTING | WRITE_THROUGH
        if not kernel32.MoveFileExW(str(source), str(destination), flags):
            raise ctypes.WinError(ctypes.get_last_error())
        return

    os.replace(source, destination)
    directory_flags = os.O_RDONLY | getattr(os, "O_DIRECTORY", 0)
    directory_fd = os.open(destination.parent, directory_flags)
    try:
        os.fsync(directory_fd)
    finally:
        os.close(directory_fd)


def _classify_failure(stderr: str, *, timed_out: bool = False, output_too_large: bool = False) -> str:
    if timed_out:
        return "TIMED_OUT"
    if output_too_large:
        return "OUTPUT_TOO_LARGE"
    text = stderr.lower()
    if any(token in text for token in ("not logged in", "unauthorized", "authentication", "invalid api key")):
        return "AUTH_MISSING"
    if any(token in text for token in ("quota", "rate limit", "usage limit", "insufficient", "credits")):
        return "QUOTA_EXCEEDED"
    if any(token in text for token in ("model not found", "unknown model", "invalid model")):
        return "MODEL_UNAVAILABLE"
    return "PROVIDER_FAILURE"
