#!/usr/bin/env python3
"""Is the plugin Navisworks will load the plugin this checkout just built?

Run this before a live (L3) window. Nothing else in the repository compares the copy a
module *loads* against the copy that was *built*, and every instrument in the chain
reports success independently: the build succeeds, `install_local_bundle.ps1` succeeds,
and the host handshake reports a healthy plugin -- while Navisworks holds an older DLL.

Measured on 2026-09-20: the installed copy was 1588736 bytes while the branch build was
1590272, two C# changes apart. The only thing that caught it was a person reading
`pluginAssemblyLength` out of `host_status` and deciding to reinstall. Had nobody looked,
a whole measurement window would have produced numbers, and an error-message
verification, against a binary that was not the one under review.

There are three copies and therefore two links, and the guard names the one that broke:

    NavisHelper/bin/x64/Release<version>/            what the build produced
        -> the build copies it into
    NavisHelper.bundle/Contents/<version>/           the bundle in the checkout
        -> tools/install_local_bundle.ps1 copies it into
    %APPDATA%/Autodesk/ApplicationPlugins/...        what Navisworks loads

`PackageContents.xml` at the bundle root is compared too. It selects each Navisworks
series and its `ModuleName`, so an install can point away from the intended plugin while
every file under `Contents/` matches.

**Silence is not success.** Only nine files in the bundle are tracked by git -- the
`.dll.config` per version, the icon notes and the manifest -- because the DLLs are
gitignored. So on a clean checkout, or after the build outputs are cleaned, a naive
traversal compares two static config files, finds them equal, and reports no drift while
having verified nothing at all. A version therefore counts as verified only when the
plugin DLL was compared on *both* links, and a run that verified no version at all exits
non-zero saying so.

A differing hash does not always mean differing code: a rebuild of unchanged sources can
produce a new assembly MVID, which is exactly what `NavisHelper.Contracts.dll` does here.
That is reported as drift anyway, and deliberately. The question this guard answers is
not "did the code change" but "will the host load what I just built", and for that the
answer and the remedy are the same either way -- reinstall.

Not wired into CI. A GitHub runner has no installed bundle, so there this would either
always skip or always fail, and a check that cannot fail is not a check.

The MCP server is installed separately under `%LOCALAPPDATA%\\NavisHelper`, so this guard
also inventories every `McpServer` and `McpServer-<version>` install there. Each installed
`NavisHelper.McpServer.dll` is compared by hash with the checkout's
`NavisHelper.McpServer/bin/Release/net9.0` build. Versions remain useful inventory, but
are not evidence of equality: code can change while `AppVersion` does not. If the
checkout server is not built, no server can be verified and the run refuses to approve
the install just as it does when no plugin version was built.

The inventory still cannot say which server a client runs: that is selected in the
client's config, not by what is on disk. So the configs are read too. For each of the
five clients `NavisHelper.McpConfigurator` writes -- Claude Desktop, Codex, Cursor,
OpenCode and Kimi Code -- the guard names the server folder the config points at and
whether that folder holds the checkout's build:

    matches         the folder's DLL hashes to the checkout build
    differs         it exists and hashes to something else
    missing         the folder or its DLL is not there
    not configured  the config file exists but names no NavisHelper server
    no config       the client has no config file

Claude Code keeps its servers in its own settings and is not checked. A client that is
already running keeps the server process it started, so this describes the next start;
only `mcp_health_check` reports the version answering right now.

Measured on 2026-09-24: a fresh server had been installed into `McpServer-2.10.0.0` and
every client still ran an older one -- Claude Desktop's config pointed at
`McpServer-d28e8b7`, Codex's at `McpServer` (2.9). `McpConfigurator --detect` printed the
*proposed* path, not the configured one, so nothing in the repository showed it.

A client config also carries every other server that client uses, with their addresses
and their secrets. These files are therefore never parsed and never echoed: the only
thing read out of one is a path ending in `NavisHelper.McpServer.exe` or `.dll`, and the
only thing printed is that path's folder.

It is also the one `scripts/check_*.py` that is not a function of the repository. The
others answer "is the code consistent with itself" and hold on any machine; this one
answers "is this machine's install current", and a build alone makes it fail -- correctly,
because a build is exactly what leaves the install behind. So it does not belong in a
`for g in scripts/check_*.py` sweep before a commit. Run it before a live window.

Usage:
    python scripts/check_installed_bundle_drift.py [bundle_root]

`bundle_root` defaults to the per-user install. Point it at a machine-wide leftover --
`C:/ProgramData/Autodesk/ApplicationPlugins/NavisHelper.bundle` or
`C:/Program Files/NavisHelper` -- to check one that BUILD_BUNDLE_RULES.md says should not
exist in the first place.

Exit codes: 0 only when at least one version was verified end to end and nothing drifted.
1 on drift -- of the bundle, of an installed server, or of a client config -- and equally
on having verified nothing.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import sys
from datetime import datetime, timezone
from pathlib import Path
from xml.etree import ElementTree


ROOT = Path(__file__).resolve().parents[1]
REPO_BUNDLE = ROOT / "NavisHelper.bundle"
SERVER_ROOT = (Path(os.environ["LOCALAPPDATA"]) / "NavisHelper"
               if os.environ.get("LOCALAPPDATA") else Path())
VERSIONS = ("2024", "2025", "2026", "2027")
PLUGIN_DLL = "NavisHelper.dll"
REINSTALL = "powershell -ExecutionPolicy Bypass -File tools\\install_local_bundle.ps1"
INSTALL_SERVER = "powershell -ExecutionPolicy Bypass -File tools\\install_local_mcp_server.ps1"
BUILD_SERVER = "dotnet build NavisHelper.McpServer/NavisHelper.McpServer.csproj -c Release"
SERVER_DLL = "NavisHelper.McpServer.dll"
SERVER_EXE = "NavisHelper.McpServer.exe"
CONFIGURATOR_EXE = "NavisHelper.McpConfigurator.exe"

# The five clients McpConfigurator writes, with the config file each one reads. The
# configurator's adapter list is the authority for both; Claude Code is deliberately
# absent because its servers live in its own settings rather than in one of these files.
CLIENT_CONFIG_LAYOUT = (
    ("claude-desktop", "APPDATA", "Claude/claude_desktop_config.json"),
    ("codex", "USERPROFILE", ".codex/config.toml"),
    ("cursor", "USERPROFILE", ".cursor/mcp.json"),
    ("opencode", "APPDATA", "OpenCode/opencode.json"),
    ("kimi", "USERPROFILE", ".kimi-code/mcp.json"),
)

# Taken from the environment rather than resolved once, so the self-test can point the
# whole client section at a fixture tree.
CLIENT_ROOTS = {name: os.environ.get(name, "") for name in ("APPDATA", "USERPROFILE")}

# A Windows path, as JSON and TOML both quote one, up to the server it names. Matched
# instead of parsed: parsing would put every other server in the file -- and whatever
# credentials sit beside it -- within reach of this report.
SERVER_PATH_RE = re.compile(r"[A-Za-z]:[\\/][^\"'\s]*?NavisHelper\.McpServer\.(?:exe|dll)")


def default_bundle_root() -> Path:
    appdata = os.environ.get("APPDATA")
    if not appdata:
        # POSIX, or a stripped environment. Say so rather than inventing a path.
        return Path()
    return Path(appdata) / "Autodesk" / "ApplicationPlugins" / "NavisHelper.bundle"


def sha(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def describe(path: Path) -> str:
    stat = path.stat()
    written = datetime.fromtimestamp(stat.st_mtime, timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    return "%s  %8d  %s" % (sha(path)[:12], stat.st_size, written)


def expected_server_version() -> str | None:
    """Read the server version shared by the checkout's bundle manifest."""
    try:
        return ElementTree.parse(REPO_BUNDLE / "PackageContents.xml").getroot().attrib["AppVersion"]
    except (KeyError, OSError, ElementTree.ParseError):
        return None


def installed_server_versions(folder: Path) -> list[str]:
    """Return package versions recorded in one server's .deps.json."""
    deps = folder / "NavisHelper.McpServer.deps.json"
    try:
        with deps.open("r", encoding="utf-8") as handle:
            targets = json.load(handle)["targets"]
    except (KeyError, OSError, TypeError, UnicodeError, json.JSONDecodeError):
        return []

    prefix = "NavisHelper.McpServer/"
    versions: set[str] = set()
    if isinstance(targets, dict):
        dictionaries = [targets]
        dictionaries.extend(value for value in targets.values() if isinstance(value, dict))
        for entries in dictionaries:
            for key in entries:
                if isinstance(key, str) and key.startswith(prefix):
                    versions.add(key[len(prefix):])
    return sorted(versions)


def checkout_server_dll() -> Path:
    """The server build this checkout produced; its hash is the reference for both
    the installed servers and the server each client is configured to run."""
    return ROOT / "NavisHelper.McpServer" / "bin" / "Release" / "net9.0" / SERVER_DLL


def check_server_installs() -> tuple[bool, Path | None]:
    """Inventory local MCP servers and verify at least one by DLL hash.

    Returns whether the inventory drifted, and the installed folder that matches the
    checkout build -- the one a client should be pointed at, when there is one.
    """
    expected = expected_server_version()
    checkout_dll = checkout_server_dll()
    folders: list[Path] = []
    if SERVER_ROOT and str(SERVER_ROOT) != "." and SERVER_ROOT.is_dir():
        folders = sorted(
            (path for path in SERVER_ROOT.iterdir()
             if path.is_dir() and (path.name == "McpServer" or path.name.startswith("McpServer-"))),
            key=lambda path: path.name.lower())

    installs = [(folder, installed_server_versions(folder)) for folder in folders]
    if installs:
        print("installed MCP servers:")
        for folder, versions in installs:
            shown = ", ".join(versions) if versions else "version unreadable"
            print(f"  {folder.name}: {shown}")
    else:
        print(f"installed MCP servers: none under {SERVER_ROOT}")

    if not expected:
        print("note: checkout MCP server version is unreadable from PackageContents.xml;")
        print("      installed versions are inventory only; hash verification continues.")

    if not checkout_dll.is_file():
        print()
        print("MCP SERVER NOTHING VERIFIED, which is not the same as no drift.")
        print(f"Checkout server DLL is absent: {checkout_dll}")
        print("Build the server, install it, point the MCP client at that install, and")
        print("restart the client:")
        print(f"  {BUILD_SERVER}")
        print(f"  {INSTALL_SERVER}")
        return True, None

    checkout_hash = sha(checkout_dll)
    matching = []
    stale = []
    for folder, versions in installs:
        installed_dll = folder / SERVER_DLL
        if installed_dll.is_file() and sha(installed_dll) == checkout_hash:
            matching.append((folder, versions))
        else:
            stale.append((folder, versions, installed_dll.is_file()))

    if not matching:
        print()
        print("MCP SERVER DRIFT: no installed server matches the checkout build by SHA-256.")
        for folder, versions, has_dll in stale:
            shown = ", ".join(versions) if versions else "version unreadable"
            if expected and expected in versions:
                detail = "version matches, code differs" if has_dll else "version matches, DLL missing"
            else:
                detail = "code differs" if has_dll else "DLL missing"
            print(f"  {folder.name}: {shown} ({detail})")
        print(f"  {INSTALL_SERVER}")
        print("  Then point the MCP client at that install and restart the client.")
        return True, None

    if stale:
        descriptions = []
        for folder, versions, has_dll in stale:
            shown = ", ".join(versions) if versions else "version unreadable"
            detail = "code differs" if has_dll else "DLL missing"
            descriptions.append(f"{folder.name} ({shown}; {detail})")
        print("note: stale MCP server installs beside the hash-matched install: "
              f"{', '.join(descriptions)}")
    names = ", ".join(folder.name for folder, _ in matching)
    print(f"MCP server verified by SHA-256: {names}.")
    return False, matching[0][0]


def client_config_paths() -> list[tuple[str, Path | None]]:
    """Each client's config file, in the order the configurator lists its adapters.

    `None` where the environment does not carry the root the file lives under, which is
    a machine this guard cannot read rather than a client with no config.
    """
    paths = []
    for client, root_name, relative in CLIENT_CONFIG_LAYOUT:
        root = CLIENT_ROOTS.get(root_name, "")
        paths.append((client, Path(root) / relative if root else None))
    return paths


def configured_server_folders(config: Path) -> list[Path]:
    """The folders of the NavisHelper servers one client config names.

    Matched, not parsed, and only the folder survives the call: the same file holds
    every other server that client runs, and printing anything else from it would put
    their addresses and secrets into a report that gets pasted into issues.
    """
    try:
        text = config.read_text(encoding="utf-8", errors="replace")
    except (OSError, UnicodeError):
        return []
    # Both formats quote a Windows path with doubled backslashes.
    return list(dict.fromkeys(
        Path(found.replace("\\\\", "\\")).parent for found in SERVER_PATH_RE.findall(text)))


def server_verdict(folder: Path, checkout_hash: str | None) -> str:
    """Is the server in that folder the one this checkout built?"""
    installed_dll = folder / SERVER_DLL
    if not installed_dll.is_file():
        return "missing"
    return "matches" if checkout_hash is not None and sha(installed_dll) == checkout_hash \
        else "differs"


def configure_command(clients: list[str], server_folder: Path | None) -> str:
    """The configurator run that repoints those clients, naming the server to point at."""
    root = SERVER_ROOT if str(SERVER_ROOT) != "." else Path("%LOCALAPPDATA%") / "NavisHelper"
    command = (f'"{root / "McpConfigurator" / CONFIGURATOR_EXE}" --configure '
               f'--clients {",".join(clients)}')
    if server_folder is not None:
        command += f' --mcp-server "{server_folder / SERVER_EXE}"'
    return command


def check_client_configs(matching_install: Path | None) -> bool:
    """Report the server each MCP client is configured to run, and whether it is this build.

    `matching_install` is the inventory's answer -- the installed folder whose DLL hashes
    to the checkout build -- so the fix can name a server that is actually there.
    """
    checkout_dll = checkout_server_dll()
    checkout_hash = sha(checkout_dll) if checkout_dll.is_file() else None

    print()
    print("MCP clients, by the server their config names:")
    drifted: list[str] = []
    named = 0
    for client, config in client_config_paths():
        if config is None:
            print(f"  {client}: its config root is not in the environment, so it cannot")
            print("      be located; nothing about this client is verified.")
            continue
        if not config.is_file():
            print(f"  {client}: no config")
            continue
        folders = configured_server_folders(config)
        if not folders:
            print(f"  {client}: not configured")
            continue
        named += 1
        for folder in folders:
            verdict = server_verdict(folder, checkout_hash)
            print(f"  {client}: {verdict}  {folder}")
            if verdict in ("differs", "missing"):
                drifted.append(client)
    print("note: Claude Code keeps its servers in its own settings, not in a file this")
    print("      guard reads, so it is not checked.")
    if named and checkout_hash is None:
        print(f"note: {checkout_dll} is absent, so no client can be 'matches' and")
        print("      'differs' here means 'not shown to be this checkout's build'.")

    if not drifted:
        return False

    print()
    print("MCP CLIENT DRIFT: " + ", ".join(dict.fromkeys(drifted)))
    print("would not run this checkout's server. Nothing else reports this: the build,")
    print("the server installer and the client each succeed on their own, and a client")
    print("that is already running keeps the server process it started.")
    if matching_install is None:
        print(f"  {INSTALL_SERVER}")
        print("  No installed server matches this checkout's build, so install one first.")
    print(f"  {configure_command(list(dict.fromkeys(drifted)), matching_install)}")
    print("  Then restart those clients.")
    return True


def build_dir_for(version: str) -> Path | None:
    """The build output directory for one Navisworks version.

    `Release` and `Release2026` both target 2026 -- BUILD_BUNDLE_RULES.md says so -- so
    2026 is looked for under either name, newest first, rather than assumed.
    """
    candidates = [ROOT / "NavisHelper" / "bin" / "x64" / ("Release" + version)]
    if version == "2026":
        candidates.append(ROOT / "NavisHelper" / "bin" / "x64" / "Release")
    existing = [path for path in candidates if (path / PLUGIN_DLL).is_file()]
    if not existing:
        return None
    return max(existing, key=lambda path: (path / PLUGIN_DLL).stat().st_mtime)


class Report:
    def __init__(self) -> None:
        self.drift: dict = {}
        self.failures: list[str] = []
        self.notes: list[str] = []
        self.matched: set = set()
        self.verified: list[str] = []

    def differs(self, relative: str, version: str, left: Path, right: Path,
                left_label: str, right_label: str) -> None:
        # Grouped rather than repeated: the same file usually differs identically in all
        # four version folders, and twelve copies of one fact is not a report. The first
        # version of this guard printed exactly that.
        # Labelled by role, not by age: an earlier version printed 'newer'/'older',
        # which asserts an ordering this guard never checks.
        key = (relative, f'{left_label} {describe(left)}', f'{right_label} {describe(right)}')
        self.drift.setdefault(key, []).append(version)


def compare_tree(source: Path, target: Path, version: str, report: Report,
                 missing_message: str) -> None:
    for source_file in sorted(source.rglob("*")):
        if not source_file.is_file():
            continue
        relative = source_file.relative_to(source)
        target_file = target / relative
        if not target_file.is_file():
            report.failures.append(f"{version}: {relative} {missing_message}\n      {REINSTALL}")
            continue
        if sha(source_file) != sha(target_file):
            report.differs(str(relative), version, source_file, target_file,
                           'in bundle', 'installed')
        else:
            report.matched.add(str(relative))


def check_version(version: str, bundle_root: Path, report: Report) -> None:
    repo_dir = REPO_BUNDLE / "Contents" / version
    installed_dir = bundle_root / "Contents" / version
    build_dir = build_dir_for(version)

    if not repo_dir.is_dir():
        report.notes.append(f"{version}: no bundle folder in this checkout; nothing to compare.")
        return

    repo_plugin = repo_dir / PLUGIN_DLL
    built_link_ok = False

    # Link 1: did the build reach the bundle in this checkout? Every file the bundle
    # carries that the build also produces is compared, not the plugin DLL alone --
    # NavisHelper.Contracts.dll and ru/NavisHelper.resources.dll are copied by the same
    # target and a stale one of those invalidates evidence just as thoroughly.
    if build_dir is None:
        report.notes.append(
            f"{version}: not built in this checkout, so nothing about its binaries is verified.")
    elif not repo_plugin.is_file():
        report.failures.append(
            f"{version}: built, but {PLUGIN_DLL} is missing from the checkout's bundle.\n"
            f"      built at {build_dir}\n"
            f"      Rebuild this configuration; the build is what refreshes the bundle.")
    else:
        for repo_file in sorted(repo_dir.rglob("*")):
            if not repo_file.is_file():
                continue
            relative = repo_file.relative_to(repo_dir)
            built_file = build_dir / relative
            if not built_file.is_file():
                # Tracked content such as .dll.config and icons has no build counterpart.
                continue
            if sha(built_file) != sha(repo_file):
                report.differs(f"{relative} (build -> checkout bundle)", version,
                               built_file, repo_file, 'built    ', 'in bundle')
        built_link_ok = sha(build_dir / PLUGIN_DLL) == sha(repo_plugin)

    # Link 2: did the install reach the machine?
    if not installed_dir.is_dir():
        if build_dir is not None:
            report.failures.append(
                f"{version}: built here but absent from the installed bundle.\n"
                f"      Navisworks {version} would load nothing from this checkout.\n"
                f"      {REINSTALL}")
        else:
            report.notes.append(f"{version}: neither built nor installed.")
        return

    compare_tree(repo_dir, installed_dir, version, report,
                 "is in the checkout's bundle and not installed.")

    installed_plugin = installed_dir / PLUGIN_DLL
    installed_link_ok = (
        repo_plugin.is_file() and installed_plugin.is_file()
        and sha(repo_plugin) == sha(installed_plugin))
    if built_link_ok and installed_link_ok:
        report.verified.append(version)


def main(argv: list[str]) -> int:
    bundle_root = Path(argv[0]) if argv else default_bundle_root()
    if not bundle_root or str(bundle_root) == ".":
        print("APPDATA is not set, so the per-user bundle root cannot be located.")
        print("Pass the bundle root as an argument on a machine where it is installed.")
        server_drift, matching_install = check_server_installs()
        client_drift = check_client_configs(matching_install)
        return 1 if server_drift or client_drift else 0

    print(f"checkout bundle : {REPO_BUNDLE}")
    print(f"installed bundle: {bundle_root}")
    server_drift, matching_install = check_server_installs()
    client_drift = check_client_configs(matching_install)

    if not bundle_root.is_dir():
        print()
        print("No bundle is installed there, which is not drift -- it is nothing to compare.")
        print(f"Install one before a live window: {REINSTALL}")
        return 1 if server_drift or client_drift else 0

    report = Report()
    # The manifest at the bundle root, outside Contents/: it selects each Navisworks
    # series and its ModuleName, so an install can point away from the plugin while every
    # file under Contents/ matches.
    for root_file in sorted(REPO_BUNDLE.glob("*")):
        if not root_file.is_file():
            continue
        installed_file = bundle_root / root_file.name
        if not installed_file.is_file():
            report.failures.append(
                f"bundle root: {root_file.name} is not installed.\n      {REINSTALL}")
        elif sha(root_file) != sha(installed_file):
            report.differs(root_file.name, "bundle root", root_file, installed_file,
                           'in bundle', 'installed')

    for version in VERSIONS:
        check_version(version, bundle_root, report)

    print()
    for note in report.notes:
        print(f"note: {note}")

    if report.drift or report.failures:
        plugin_drifted = any(relative.startswith(PLUGIN_DLL) for relative, _, _ in report.drift)
        print()
        print("DRIFT: what Navisworks loads is not what this checkout built.")
        print("Live evidence gathered now would describe a different binary, and the build,")
        print("the installer and the host handshake would each still report success.")
        print()
        for (relative, left, right), versions in sorted(report.drift.items()):
            print(f"  {relative}   differs in {', '.join(versions)}")
            print(f"      {left}")
            print(f"      {right}")
        for failure in report.failures:
            print(f"  {failure}")
        if report.drift and not plugin_drifted and PLUGIN_DLL in report.matched:
            print()
            print(f"  {PLUGIN_DLL} itself matches, so this is a stale install rather than")
            print("  different plugin code -- a rebuild changes an assembly's hash even when")
            print("  nothing inside it changed. The remedy is the same either way.")
        print()
        print(f"  {REINSTALL}")
        print("  Navisworks must be closed before installing.")
        print()
        print("  This says nothing about the code. It is about this machine, and a build")
        print("  alone is enough to cause it -- so it does not block a commit, and it is")
        print("  not part of the pre-commit guard sweep. It blocks a live window.")
        return 1

    if not report.verified:
        print()
        print("NOTHING VERIFIED, which is not the same as no drift.")
        print(f"No version had {PLUGIN_DLL} compared on both links, so this run says nothing")
        print("about the binary Navisworks would load. Only nine files in the bundle are")
        print("tracked by git -- the per-version .dll.config, the icon notes and the")
        print("manifest -- so a checkout whose DLLs were never built compares those, finds")
        print("them equal, and would otherwise have reported success.")
        print()
        print("Build the configurations you intend to exercise, install, and run this again:")
        print("  MSBuild NavisHelper.sln -restore -p:Configuration=Release2027 -p:Platform=x64")
        print(f"  {REINSTALL}")
        return 1

    print()
    print(f"No drift. Verified end to end: {', '.join(report.verified)}.")
    print("For each, the built plugin matches the checkout's bundle and the installed copy,")
    print("and every other file in those folders matches the install.")
    return 1 if server_drift or client_drift else 0



def selftest() -> int:
    """Drive the branches this machine's state cannot reach, on fabricated trees.

    Link 2 (bundle -> install) is provable against a real machine: cause drift, see it
    named, reinstall, see it gone. Link 1 (build -> bundle) is not, without editing the
    checkout, and the branch that matters most -- refusing to approve a checkout whose
    DLLs were never built -- only shows up when nothing else is wrong.

    So they are driven here against temp directories, by pointing this module's own path
    constants at them. Nothing real is read or written, which is why this half can run in
    CI while the machine check cannot.

    The client section is in the same position: the only way to see it name a stale
    server on a real machine is to reconfigure a real client, which this guard has no
    business doing. So its five verdicts are driven against fixture config files under
    redirected roots, including one that carries another server's secret beside the
    NavisHelper path, to prove that only the folder comes back out.
    """
    import io as _io
    import tempfile

    global ROOT, REPO_BUNDLE, SERVER_ROOT, CLIENT_ROOTS
    failures = 0

    def build_tree(root: Path, *, plugin_in_build=b"BUILT", plugin_in_bundle=b"BUILT",
                   plugin_installed=b"BUILT", satellite=b"RU", install_satellite=True):
        build = root / "NavisHelper" / "bin" / "x64" / "Release2027"
        bundle = root / "NavisHelper.bundle"
        installed = root / "installed"
        for path in (build / "ru", bundle / "Contents" / "2027" / "ru",
                     installed / "Contents" / "2027" / "ru"):
            path.mkdir(parents=True, exist_ok=True)
        if plugin_in_build is not None:
            (build / PLUGIN_DLL).write_bytes(plugin_in_build)
        (build / "ru" / "NavisHelper.resources.dll").write_bytes(satellite)
        if plugin_in_bundle is not None:
            (bundle / "Contents" / "2027" / PLUGIN_DLL).write_bytes(plugin_in_bundle)
        (bundle / "Contents" / "2027" / "ru" / "NavisHelper.resources.dll").write_bytes(satellite)
        (bundle / "PackageContents.xml").write_bytes(b"<manifest/>")
        (installed / "PackageContents.xml").write_bytes(b"<manifest/>")
        if plugin_installed is not None:
            (installed / "Contents" / "2027" / PLUGIN_DLL).write_bytes(plugin_installed)
        if install_satellite:
            (installed / "Contents" / "2027" / "ru" / "NavisHelper.resources.dll").write_bytes(satellite)
        return bundle, installed

    def client_roots(root: Path) -> dict[str, str]:
        """The two environment roots the client section reads, redirected into a fixture."""
        return {"APPDATA": str(root / "clients" / "AppData"),
                "USERPROFILE": str(root / "clients" / "UserProfile")}

    def run(root: Path, bundle: Path, installed: Path, server_root: Path | None = None,
            clients: dict[str, str] | None = None):
        global ROOT, REPO_BUNDLE, SERVER_ROOT, CLIENT_ROOTS
        ROOT, REPO_BUNDLE, SERVER_ROOT = root, bundle, server_root or root / "servers"
        CLIENT_ROOTS = client_roots(root)
        # Written through the layout table rather than by hand, so a case cannot pass
        # while the table points somewhere no client reads.
        for client, text in (clients or {}).items():
            config = dict(client_config_paths())[client]
            config.parent.mkdir(parents=True, exist_ok=True)
            config.write_text(text, encoding="utf-8")
        captured = _io.StringIO()
        stdout, sys.stdout = sys.stdout, captured
        try:
            code = main([str(installed)])
        finally:
            sys.stdout = stdout
        return code, captured.getvalue()

    def write_server_build(root: Path, contents: bytes = b"SERVER") -> None:
        server_build = root / "NavisHelper.McpServer" / "bin" / "Release" / "net9.0"
        server_build.mkdir(parents=True, exist_ok=True)
        (server_build / SERVER_DLL).write_bytes(contents)

    def write_server_install(server_root: Path, folder_name: str, version: str,
                             contents: bytes | None = b"SERVER") -> None:
        folder = server_root / folder_name
        folder.mkdir(parents=True, exist_ok=True)
        deps = {"targets": {".NETCoreApp,Version=v9.0": {
            "NavisHelper.McpServer/" + version: {}}}}
        (folder / "NavisHelper.McpServer.deps.json").write_text(
            json.dumps(deps), encoding="utf-8")
        if contents is not None:
            (folder / SERVER_DLL).write_bytes(contents)

    cases = (
        ("build differs from the checkout bundle",
         dict(plugin_in_bundle=b"STALE", plugin_installed=b"STALE"), 1,
         "build -> checkout bundle", None),
        ("built, but the bundle has no plugin",
         dict(plugin_in_bundle=None, plugin_installed=None), 1,
         "missing from the checkout's bundle", None),
        ("nothing built at all",
         dict(plugin_in_build=None, plugin_in_bundle=None, plugin_installed=None), 1,
         "NOTHING VERIFIED", None),
        ("everything agrees", dict(), 0, "Verified end to end: 2027", "DRIFT"),
        ("the install lacks a file the bundle carries",
         dict(install_satellite=False), 1, "not installed", None),
    )
    real_root, real_bundle, real_server_root = ROOT, REPO_BUNDLE, SERVER_ROOT
    real_client_roots = CLIENT_ROOTS
    try:
        for label, kwargs, expected_code, expected, absent in cases:
            with tempfile.TemporaryDirectory(prefix="drift-selftest-") as tmp:
                root = Path(tmp)
                bundle, installed = build_tree(root, **kwargs)
                server_root = root / "servers"
                write_server_build(root)
                write_server_install(server_root, "McpServer", "2.10.0.0")
                code, output = run(root, bundle, installed, server_root)
                for ok, detail in (
                    (code == expected_code, "exit %d, expected %d" % (code, expected_code)),
                    (expected in output, "output lacks %r" % expected),
                    (absent is None or absent not in output, "output contains %r" % absent),
                ):
                    if not ok:
                        failures += 1
                        print("  FAIL  %s -- %s" % (label, detail))
                        print(output)
                        break
                else:
                    print("  PASS  %s" % label)
    finally:
        ROOT, REPO_BUNDLE, SERVER_ROOT = real_root, real_bundle, real_server_root
        CLIENT_ROOTS = real_client_roots

    server_cases = (
        ("checkout server not built", None,
         (("McpServer", "2.10.0.0", b"SERVER"),), 1,
         ("MCP SERVER NOTHING VERIFIED", BUILD_SERVER, INSTALL_SERVER), None),
        ("MCP server same version with different bytes", b"CHECKOUT",
         (("McpServer", "2.10.0.0", b"INSTALLED"),), 1,
         ("McpServer: 2.10.0.0", "version matches, code differs", INSTALL_SERVER,
          "point the MCP client at that install and restart the client"), None),
        ("MCP server hash match", b"SAME",
         (("McpServer", "2.9.0.0", b"SAME"),), 0,
         ("McpServer: 2.9.0.0", "MCP server verified by SHA-256: McpServer."),
         "MCP SERVER DRIFT"),
        ("MCP server hash match with a stale install", b"SAME",
         (("McpServer-2.10.0.0", "2.10.0.0", b"SAME"),
          ("McpServer", "2.9.0.0", b"STALE")), 0,
         ("stale MCP server installs beside the hash-matched install: "
          "McpServer (2.9.0.0; code differs)",), "MCP SERVER DRIFT"),
    )
    try:
        for label, checkout_bytes, installs, expected_code, expected_parts, absent in server_cases:
            with tempfile.TemporaryDirectory(prefix="drift-server-selftest-") as tmp:
                root = Path(tmp)
                bundle, installed = build_tree(root)
                (bundle / "PackageContents.xml").write_text(
                    '<ApplicationPackage AppVersion="2.10.0.0"/>', encoding="utf-8")
                (installed / "PackageContents.xml").write_text(
                    '<ApplicationPackage AppVersion="2.10.0.0"/>', encoding="utf-8")
                server_root = root / "servers"
                server_root.mkdir()
                if checkout_bytes is not None:
                    write_server_build(root, checkout_bytes)
                for folder_name, version, contents in installs:
                    write_server_install(server_root, folder_name, version, contents)
                code, output = run(root, bundle, installed, server_root)
                for ok, detail in (
                    (code == expected_code, "exit %d, expected %d" % (code, expected_code)),
                    (all(part in output for part in expected_parts),
                     "output lacks one of %r" % (expected_parts,)),
                    (absent is None or absent not in output, "output contains %r" % absent),
                ):
                    if not ok:
                        failures += 1
                        print("  FAIL  %s -- %s" % (label, detail))
                        print(output)
                        break
                else:
                    print("  PASS  %s" % label)
    finally:
        ROOT, REPO_BUNDLE, SERVER_ROOT = real_root, real_bundle, real_server_root
        CLIENT_ROOTS = real_client_roots

    FIXTURE_MATCHING = "McpServer-2.10.0.0"
    FIXTURE_STALE = "McpServer-d28e8b7"
    FIXTURE_SECRET = "sk-selftest-0000000000"

    def json_config(folder: Path, with_other_server: bool = False) -> str:
        """A config in the JSON shape Claude Desktop, Cursor, OpenCode and Kimi keep.

        `json.dumps` doubles every backslash in the path, which is how those clients
        write one, so the fixture exercises the unescaping rather than assuming it.
        """
        servers = {"navishelper": {"command": str(folder / SERVER_EXE)}}
        if with_other_server:
            # Another tenant of the same file, carrying the kind of value that must never
            # reach this report: a client config holds every server that client runs.
            servers["other"] = {"command": "other-mcp.exe",
                                "headers": {"Authorization": "Bearer " + FIXTURE_SECRET}}
        return json.dumps({"mcpServers": servers})

    def toml_config(folder: Path) -> str:
        """The same entry as Codex keeps it: a TOML string with doubled backslashes."""
        return '[mcp_servers.navishelper]\ncommand = "%s"\n' % str(
            folder / SERVER_EXE).replace("\\", "\\\\")

    client_cases = (
        ("a TOML config with doubled backslashes, pointing at the matching install",
         b"CHECKOUT", {"codex": lambda servers: toml_config(servers / FIXTURE_MATCHING)}, 0,
         ("codex: matches", "kimi: no config"), "MCP CLIENT DRIFT"),
        ("a client pointed at a stale install", b"CHECKOUT",
         {"claude-desktop": lambda servers: json_config(servers / FIXTURE_STALE)}, 1,
         ("claude-desktop: differs", "MCP CLIENT DRIFT: claude-desktop",
          "--clients claude-desktop", '--mcp-server "{matching}"'), None),
        ("a client pointed at a folder that holds no server", b"CHECKOUT",
         {"cursor": lambda servers: json_config(servers / "McpServer-removed")}, 1,
         ("cursor: missing", "MCP CLIENT DRIFT: cursor"), None),
        ("a config that names no NavisHelper server", b"CHECKOUT",
         {"opencode": lambda servers: json.dumps(
             {"mcpServers": {"other": {"command": "other-mcp.exe"}}})}, 0,
         ("opencode: not configured",), "MCP CLIENT DRIFT"),
        ("no client config at all", b"CHECKOUT", {}, 0,
         ("claude-desktop: no config", "codex: no config", "cursor: no config",
          "opencode: no config", "kimi: no config", "Claude Code"), "MCP CLIENT DRIFT"),
        ("another server's secret beside the NavisHelper path", b"CHECKOUT",
         {"claude-desktop": lambda servers: json_config(servers / FIXTURE_MATCHING,
                                                       with_other_server=True)}, 0,
         ("claude-desktop: matches",), FIXTURE_SECRET),
        ("checkout server not built, so no client can match", None,
         {"claude-desktop": lambda servers: json_config(servers / FIXTURE_STALE)}, 1,
         ("claude-desktop: differs", "no client can be 'matches'",
          "MCP CLIENT DRIFT: claude-desktop", INSTALL_SERVER, "install one first"), None),
    )
    try:
        for label, checkout_bytes, configs, expected_code, expected_parts, absent in client_cases:
            with tempfile.TemporaryDirectory(prefix="drift-client-selftest-") as tmp:
                root = Path(tmp)
                bundle, installed = build_tree(root)
                server_root = root / "servers"
                server_root.mkdir()
                if checkout_bytes is not None:
                    write_server_build(root, checkout_bytes)
                write_server_install(server_root, FIXTURE_MATCHING, "2.10.0.0", checkout_bytes)
                write_server_install(server_root, FIXTURE_STALE, "2.9.0.0", b"OLDER")
                parts = tuple(
                    part.replace("{matching}", str(server_root / FIXTURE_MATCHING / SERVER_EXE))
                    for part in expected_parts)
                code, output = run(root, bundle, installed, server_root,
                                   {client: make(server_root) for client, make in configs.items()})
                for ok, detail in (
                    (code == expected_code, "exit %d, expected %d" % (code, expected_code)),
                    (all(part in output for part in parts),
                     "output lacks one of %r" % (parts,)),
                    (absent is None or absent not in output, "output contains %r" % absent),
                ):
                    if not ok:
                        failures += 1
                        print("  FAIL  %s -- %s" % (label, detail))
                        print(output)
                        break
                else:
                    print("  PASS  %s" % label)
    finally:
        ROOT, REPO_BUNDLE, SERVER_ROOT = real_root, real_bundle, real_server_root
        CLIENT_ROOTS = real_client_roots

    if failures:
        print("%d self-test assertion(s) failed" % failures)
        return 1
    print("self-test passed: every branch fires, and the agreeing case still exits 0")
    return 0


if __name__ == "__main__":
    if "--selftest" in sys.argv[1:]:
        raise SystemExit(selftest())
    raise SystemExit(main(sys.argv[1:]))
