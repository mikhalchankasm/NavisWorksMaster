"""Write access to an executor's worktree for the operator's own SID, for one Codex run (#1275).

The unelevated Codex sandbox runs each command under a restricted token, and Windows grants such a
token access only when both its ordinary SIDs and its restricting SIDs pass the DACL. Codex adds an
ACE for its capability SIDs on every writable root, which covers the restricting half. The ordinary
half still needs an ACE for a SID the token keeps enabled. Inside the sandbox BUILTIN\\Administrators
is deny-only, so a worktree that inherits only Administrators F, SYSTEM F and Users RX (T3D01,
D:\\GITHUB) refuses every write, while one that inherits Authenticated Users M works (#1275, w1/w2).

The grant is one separate explicit ACE for the SID in the launcher's own token, written with
SetNamedSecurityInfoW. `icacls /grant` and .NET AddAccessRule both fold a new ACE into an existing
one for the same SID and flags, and a partial right the operator already had could then never be
told apart again. Restoring removes exactly that ACE and compares every ACE before and after: the
only ones allowed to appear are the capability ACEs Codex wrote for this run's own SIDs.
"""

from __future__ import annotations

import os
import re
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Protocol

from scripts.executor_primitives import ExecutorError

MODIFY_RIGHTS = 0x1301BF
GRANT_FLAGS = "OICI"
# What Codex 0.162.1 writes on a writable root for a capability SID, seen on every probe worktree.
SANDBOX_ACE_FLAGS = "CIOI"
_ACE = re.compile(r"\(([^()]*)\)")
_SID = re.compile(r"S-1-\d+(?:-\d+)+", re.IGNORECASE)
_RIGHTS_ALIASES = {"FA": 0x1F01FF, "GA": 0x10000000}
# Account SIDs that SDDL writes as an alias: the built-in Administrator (RID 500) and Guest (RID 501).
_ACCOUNT_ALIASES = ("LA", "LG")


class AclBackend(Protocol):
    def operator_sid(self) -> str: ...
    def read(self, path: Path) -> str: ...
    def write(self, path: Path, sddl: str, *, protected: bool) -> None: ...


@dataclass(frozen=True)
class Dacl:
    control: str
    aces: tuple[str, ...]

    @property
    def protected(self) -> bool:
        return "P" in self.control.replace("AI", "").replace("AR", "")

    def explicit(self) -> list[str]:
        return [ace for ace in self.aces if "ID" not in _flag_pairs(ace)]


def parse_dacl(sddl: str) -> Dacl:
    """The DACL part of an SDDL string read with DACL_SECURITY_INFORMATION only."""
    if not sddl.startswith("D:"):
        raise ValueError("not a DACL: " + sddl[:40])
    body = sddl[2:]
    first = body.find("(")
    control = body if first < 0 else body[:first]
    return Dacl(control, tuple(_ACE.findall(body[len(control):])))


def _fields(ace: str) -> list[str]:
    return ace.split(";")


def _flag_pairs(ace: str) -> set[str]:
    flags = _fields(ace)[1] if len(_fields(ace)) > 1 else ""
    return {flags[index:index + 2].upper() for index in range(0, len(flags), 2)}


def ace_key(ace: str) -> tuple[str, ...]:
    """An ACE as Windows means it: flag order and letter case do not matter."""
    fields = _fields(ace)
    if len(fields) < 6:
        return (ace,)
    kind, _flags, rights, guid, inherited_guid, sid, *rest = fields
    return (kind.upper(), "".join(sorted(_flag_pairs(ace))), rights.lower(), guid.lower(), inherited_guid.lower(),
            sid.upper(), *rest)


def expand_sid_alias(sddl: str, alias: str, sid: str) -> str:
    """The SDDL with `alias` in an ACE's SID field written as `sid`.

    ConvertSecurityDescriptorToStringSecurityDescriptorW writes the built-in Administrator as LA, while
    the token gives its full SID; a launcher running as that account (hosted CI) would otherwise never
    find its own ACE again.
    """
    def one(match: re.Match[str]) -> str:
        fields = _fields(match.group(1))
        if len(fields) >= 6 and fields[5].upper() == alias.upper():
            fields[5] = sid
        return "(" + ";".join(fields) + ")"

    return _ACE.sub(one, sddl)


def grant_ace(sid: str) -> str:
    return f"A;{GRANT_FLAGS};0x{MODIFY_RIGHTS:x};;;{sid}"


def _rights(ace: str) -> int | None:
    text = _fields(ace)[2] if len(_fields(ace)) > 2 else ""
    if text.lower().startswith("0x"):
        try:
            return int(text, 16)
        except ValueError:
            return None
    return _RIGHTS_ALIASES.get(text.upper())


def already_granted(dacl: Dacl, sid: str) -> bool:
    """An allow ACE, explicit or inherited, that already gives this SID Modify on the folder and below."""
    for ace in dacl.aces:
        fields = _fields(ace)
        if len(fields) < 6 or fields[0].upper() != "A" or fields[5].upper() != sid.upper():
            continue
        pairs = _flag_pairs(ace)
        rights = _rights(ace)
        if {"OI", "CI"} <= pairs and "IO" not in pairs and rights is not None and rights & MODIFY_RIGHTS == MODIFY_RIGHTS:
            return True
    return False


def _build(explicit: list[str], protected: bool) -> str:
    # Explicit ACEs only: SetNamedSecurityInfoW derives the inherited ones from the parent itself.
    return "D:" + ("P" if protected else "") + "".join(f"({ace})" for ace in explicit)


def _is_sandbox_ace(ace: str, run_sids: set[str]) -> bool:
    fields = _fields(ace)
    return (len(fields) >= 6 and fields[0].upper() == "A" and "".join(sorted(_flag_pairs(ace))) == SANDBOX_ACE_FLAGS
            and _rights(ace) == MODIFY_RIGHTS and fields[5].upper() in run_sids)


def dacl_changes(before: Dacl, after: Dacl, run_sids: set[str]) -> tuple[list[str], list[str]]:
    """ACEs lost and ACEs gained, ignoring only this run's own sandbox capability ACEs."""
    sids = {sid.upper() for sid in run_sids}
    old, new = Counter(ace_key(ace) for ace in before.aces), Counter(ace_key(ace) for ace in after.aces)
    by_key = {ace_key(ace): ace for ace in (*before.aces, *after.aces)}
    missing = [by_key[key] for key, count in (old - new).items() for _ in range(count)]
    gained = [by_key[key] for key, count in (new - old).items() for _ in range(count)]
    unexpected = [ace for ace in gained if not _is_sandbox_ace(ace, sids)]
    # Protection, not the whole control string: SetNamedSecurityInfoW always sets AI (auto-inherited), a marker.
    if before.protected != after.protected:
        unexpected.append("control:" + before.control + "->" + after.control)
    return missing, unexpected


def sandbox_sids(codex_home: Path) -> set[str]:
    """Every capability SID Codex minted for this run, from its own cap_sid file."""
    try:
        text = (codex_home / "cap_sid").read_text(encoding="utf-8")
    except OSError:
        return set()
    return {match.upper() for match in _SID.findall(text)}


def _canonical(path: Path) -> str:
    return os.path.normcase(os.path.realpath(path))


def check_target(worktree: Path, main_checkout: Path, toplevel: Path) -> None:
    """Only the executor's own worktree root: never the main checkout, a parent of it, or a subfolder."""
    target, main = _canonical(worktree), _canonical(main_checkout)
    if target == main or main.startswith(target.rstrip("\\/") + os.sep):
        raise ExecutorError("SANDBOX_UNSAFE", "The worktree ACL grant would cover the main checkout")
    if _canonical(toplevel) != target:
        raise ExecutorError("SANDBOX_UNSAFE", "The worktree ACL grant target is not the worktree root")


def grant_worktree_access(
    worktree: Path, main_checkout: Path, toplevel: Path | Callable[[], Path], backend: AclBackend | None = None
) -> dict[str, object] | None:
    """Add the operator's Modify ACE before the run; None off Windows, where there is no such sandbox.

    `toplevel` may be a callable: the git query behind it then runs only where a grant is made.
    """
    if backend is None:
        if os.name != "nt":
            return None
        backend = WindowsAcl()
    check_target(worktree, main_checkout, toplevel() if callable(toplevel) else toplevel)
    try:
        sid = backend.operator_sid()
        before = backend.read(worktree)
        dacl = parse_dacl(before)
    except (OSError, ValueError) as exc:
        raise ExecutorError("SANDBOX_UNSAFE", f"The worktree ACL could not be read: {exc}") from exc
    state: dict[str, object] = {"path": str(worktree), "sid": sid, "before": before}
    if already_granted(dacl, sid):
        state["added"] = None
        return state
    ace = grant_ace(sid)
    try:
        backend.write(worktree, _build([*dacl.explicit(), ace], dacl.protected), protected=dacl.protected)
    except OSError as exc:
        raise ExecutorError(
            "SANDBOX_UNSAFE", f"The worktree ACL grant failed on {worktree}: {exc}; DACL before it: {before}"
        ) from exc
    state["added"] = ace
    return state


def grant_runtime_access(path: Path, backend: AclBackend | None = None) -> str | None:
    """The same ACE on a run directory the launcher created and deletes with the run (#1274); nothing to restore."""
    if backend is None:
        if os.name != "nt":
            return None
        backend = WindowsAcl()
    try:
        sid = backend.operator_sid()
        dacl = parse_dacl(backend.read(path))
        if already_granted(dacl, sid):
            return None
        ace = grant_ace(sid)
        backend.write(path, _build([*dacl.explicit(), ace], dacl.protected), protected=dacl.protected)
    except (OSError, ValueError) as exc:
        raise ExecutorError("SANDBOX_UNSAFE", f"The run directory ACL grant failed on {path}: {exc}") from exc
    return ace


def restore_worktree_access(
    state: dict[str, object], run_sids: set[str], backend: AclBackend | None = None
) -> dict[str, object]:
    """Remove exactly the added ACE and prove the DACL is the one the run started with."""
    backend = backend or WindowsAcl()
    path = Path(str(state["path"]))
    result = {key: state[key] for key in ("path", "sid", "before", "added")}
    problem = ""
    try:
        text = backend.read(path)
        current = parse_dacl(text)
        added = state.get("added")
        if added:
            explicit = current.explicit()
            matches = [index for index, ace in enumerate(explicit) if ace_key(ace) == ace_key(str(added))]
            if not matches:
                problem = "the added ACE is no longer on the worktree"
            else:
                del explicit[matches[-1]]
                backend.write(path, _build(explicit, current.protected), protected=current.protected)
                text = backend.read(path)
                current = parse_dacl(text)
        missing, unexpected = dacl_changes(parse_dacl(str(state["before"])), current, run_sids)
        result["after"] = text
    except (OSError, ValueError) as exc:
        missing, unexpected, problem = [], [], f"{type(exc).__name__}: {exc}"
    result.update(missing=missing, unexpected=unexpected, restored=not (missing or unexpected or problem))
    if problem:
        result["problem"] = problem
    return result


class WindowsAcl:
    """The Win32 calls, kept apart so the logic above is tested without touching a real DACL."""

    SE_FILE_OBJECT = 1
    DACL_SECURITY_INFORMATION = 0x00000004
    PROTECTED_DACL_SECURITY_INFORMATION = 0x80000000
    UNPROTECTED_DACL_SECURITY_INFORMATION = 0x20000000
    TOKEN_QUERY = 0x0008
    TOKEN_USER = 1

    def __init__(self) -> None:
        import ctypes
        from ctypes import wintypes

        self.ctypes, self.wintypes = ctypes, wintypes
        self.advapi = ctypes.WinDLL("advapi32", use_last_error=True)
        self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        void, pvoid = ctypes.c_void_p, ctypes.POINTER(ctypes.c_void_p)
        self.kernel.GetCurrentProcess.restype = wintypes.HANDLE
        self.kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        self.kernel.LocalFree.argtypes = [void]
        self.advapi.OpenProcessToken.argtypes = [wintypes.HANDLE, wintypes.DWORD, ctypes.POINTER(wintypes.HANDLE)]
        self.advapi.GetTokenInformation.argtypes = [
            wintypes.HANDLE, ctypes.c_int, void, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD)]
        self.advapi.ConvertSidToStringSidW.argtypes = [void, ctypes.POINTER(wintypes.LPWSTR)]
        self.advapi.ConvertStringSidToSidW.argtypes = [wintypes.LPCWSTR, pvoid]
        self.advapi.GetNamedSecurityInfoW.argtypes = [
            wintypes.LPCWSTR, ctypes.c_int, wintypes.DWORD, pvoid, pvoid, pvoid, pvoid, pvoid]
        self.advapi.GetNamedSecurityInfoW.restype = wintypes.DWORD
        self.advapi.ConvertSecurityDescriptorToStringSecurityDescriptorW.argtypes = [
            void, wintypes.DWORD, wintypes.DWORD, ctypes.POINTER(wintypes.LPWSTR), ctypes.POINTER(wintypes.DWORD)]
        self.advapi.ConvertStringSecurityDescriptorToSecurityDescriptorW.argtypes = [
            wintypes.LPCWSTR, wintypes.DWORD, pvoid, ctypes.POINTER(wintypes.DWORD)]
        self.advapi.GetSecurityDescriptorDacl.argtypes = [
            void, ctypes.POINTER(wintypes.BOOL), pvoid, ctypes.POINTER(wintypes.BOOL)]
        self.advapi.SetNamedSecurityInfoW.argtypes = [
            wintypes.LPWSTR, ctypes.c_int, wintypes.DWORD, void, void, void, void]
        self.advapi.SetNamedSecurityInfoW.restype = wintypes.DWORD

    def _fail(self, code: int | None = None) -> OSError:
        return self.ctypes.WinError(code if code is not None else self.ctypes.get_last_error())

    def _free_string(self, text: object) -> None:
        self.kernel.LocalFree(self.ctypes.cast(text, self.ctypes.c_void_p))

    def _sid_text(self, sid: object) -> str:
        text = self.wintypes.LPWSTR()
        if not self.advapi.ConvertSidToStringSidW(sid, self.ctypes.byref(text)):
            raise self._fail()
        try:
            return str(text.value)
        finally:
            self._free_string(text)

    def alias_sid(self, alias: str) -> str | None:
        """The full SID an SDDL alias stands for on this machine, None when it stands for none."""
        sid = self.ctypes.c_void_p()
        if not self.advapi.ConvertStringSidToSidW(alias, self.ctypes.byref(sid)):
            return None
        try:
            return self._sid_text(sid)
        finally:
            self.kernel.LocalFree(sid)

    def operator_sid(self) -> str:
        ctypes, wintypes = self.ctypes, self.wintypes
        token = wintypes.HANDLE()
        if not self.advapi.OpenProcessToken(self.kernel.GetCurrentProcess(), self.TOKEN_QUERY, ctypes.byref(token)):
            raise self._fail()
        try:
            size = wintypes.DWORD(0)
            self.advapi.GetTokenInformation(token, self.TOKEN_USER, None, 0, ctypes.byref(size))
            buffer = ctypes.create_string_buffer(size.value)
            if not self.advapi.GetTokenInformation(token, self.TOKEN_USER, buffer, size, ctypes.byref(size)):
                raise self._fail()
            # TOKEN_USER starts with SID_AND_ATTRIBUTES, whose first field is the PSID.
            return self._sid_text(ctypes.c_void_p.from_buffer(buffer).value)
        finally:
            self.kernel.CloseHandle(token)

    def read(self, path: Path) -> str:
        ctypes, wintypes = self.ctypes, self.wintypes
        descriptor = ctypes.c_void_p()
        code = self.advapi.GetNamedSecurityInfoW(
            str(path), self.SE_FILE_OBJECT, self.DACL_SECURITY_INFORMATION, None, None, None, None,
            ctypes.byref(descriptor))
        if code:
            raise self._fail(code)
        try:
            text, length = wintypes.LPWSTR(), wintypes.DWORD()
            if not self.advapi.ConvertSecurityDescriptorToStringSecurityDescriptorW(
                    descriptor, 1, self.DACL_SECURITY_INFORMATION, ctypes.byref(text), ctypes.byref(length)):
                raise self._fail()
            try:
                sddl = str(text.value)
            finally:
                self._free_string(text)
        finally:
            self.kernel.LocalFree(descriptor)
        operator = self.operator_sid()
        for alias in _ACCOUNT_ALIASES:
            if self.alias_sid(alias) == operator:
                sddl = expand_sid_alias(sddl, alias, operator)
        return sddl

    def write(self, path: Path, sddl: str, *, protected: bool) -> None:
        ctypes, wintypes = self.ctypes, self.wintypes
        descriptor = ctypes.c_void_p()
        if not self.advapi.ConvertStringSecurityDescriptorToSecurityDescriptorW(
                sddl, 1, ctypes.byref(descriptor), None):
            raise self._fail()
        try:
            present, defaulted, dacl = wintypes.BOOL(), wintypes.BOOL(), ctypes.c_void_p()
            if not self.advapi.GetSecurityDescriptorDacl(
                    descriptor, ctypes.byref(present), ctypes.byref(dacl), ctypes.byref(defaulted)):
                raise self._fail()
            flags = self.DACL_SECURITY_INFORMATION | (
                self.PROTECTED_DACL_SECURITY_INFORMATION if protected else self.UNPROTECTED_DACL_SECURITY_INFORMATION)
            code = self.advapi.SetNamedSecurityInfoW(
                ctypes.create_unicode_buffer(str(path)), self.SE_FILE_OBJECT, flags, None, None, dacl, None)
            if code:
                raise self._fail(code)
        finally:
            self.kernel.LocalFree(descriptor)
