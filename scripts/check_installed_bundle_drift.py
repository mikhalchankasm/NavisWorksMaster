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

    NavisHelper/bin/x64/Release<version>/NavisHelper.dll     what the build produced
        -> the build copies it into
    NavisHelper.bundle/Contents/<version>/                   the bundle in the checkout
        -> tools/install_local_bundle.ps1 copies it into
    %APPDATA%/Autodesk/ApplicationPlugins/NavisHelper.bundle what Navisworks loads

A differing hash does not always mean differing code: a rebuild of unchanged sources can
produce a new assembly MVID, which is exactly what `NavisHelper.Contracts.dll` does here.
That is reported as drift anyway, and deliberately. The question this guard answers is
not "did the code change" but "will the host load what I just built", and for that the
answer and the remedy are the same either way -- reinstall.

Not wired into CI. A GitHub runner has no installed bundle, so there this would either
always skip or always fail, and a check that cannot fail is not a check.

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

Exit codes: 0 when every link matches, or when nothing is installed; 1 on drift.
"""

from __future__ import annotations

import hashlib
import os
import sys
from datetime import datetime, timezone
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
REPO_BUNDLE = ROOT / "NavisHelper.bundle"
VERSIONS = ("2024", "2025", "2026", "2027")
PLUGIN_DLL = "NavisHelper.dll"
REINSTALL = "powershell -ExecutionPolicy Bypass -File tools\\install_local_bundle.ps1"


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


def built_plugin_path(version: str) -> Path | None:
    """The build output for one Navisworks version.

    `Release` and `Release2026` both target 2026 -- BUILD_BUNDLE_RULES.md says so -- so
    2026 is looked for under either name, newest first, rather than assumed.
    """
    candidates = [ROOT / "NavisHelper" / "bin" / "x64" / ("Release" + version) / PLUGIN_DLL]
    if version == "2026":
        candidates.append(ROOT / "NavisHelper" / "bin" / "x64" / "Release" / PLUGIN_DLL)
    existing = [path for path in candidates if path.is_file()]
    if not existing:
        return None
    return max(existing, key=lambda path: path.stat().st_mtime)


def check_version(version: str, bundle_root: Path, failures: list[str], notes: list[str],
                  drift: dict, matched: set) -> None:
    repo_dir = REPO_BUNDLE / "Contents" / version
    installed_dir = bundle_root / "Contents" / version
    built = built_plugin_path(version)

    if not repo_dir.is_dir():
        notes.append(f"{version}: no bundle folder in this checkout; nothing to compare.")
        return

    # Link 1: did the build reach the bundle in this checkout?
    repo_plugin = repo_dir / PLUGIN_DLL
    if built and repo_plugin.is_file():
        if sha(built) != sha(repo_plugin):
            failures.append(
                f"{version}: the checkout's bundle is older than the build output.\n"
                f"      built     {describe(built)}\n"
                f"      in bundle {describe(repo_plugin)}\n"
                f"      Rebuild this configuration; the build is what refreshes the bundle."
            )
    elif not built:
        notes.append(f"{version}: not built in this checkout, so the build link is unchecked.")

    # Link 2: did the install reach the machine?
    if not installed_dir.is_dir():
        if built:
            failures.append(
                f"{version}: built here but absent from the installed bundle.\n"
                f"      built {describe(built)}\n"
                f"      Navisworks {version} would load nothing from this checkout.\n"
                f"      {REINSTALL}"
            )
        else:
            notes.append(f"{version}: neither built nor installed.")
        return

    for repo_file in sorted(repo_dir.rglob("*")):
        if not repo_file.is_file():
            continue
        relative = repo_file.relative_to(repo_dir)
        installed_file = installed_dir / relative
        if not installed_file.is_file():
            failures.append(
                f"{version}: {relative} is in the checkout's bundle and not installed.\n"
                f"      {REINSTALL}"
            )
            continue
        # Grouped rather than repeated: the same file usually differs identically in all
        # four version folders, and twelve copies of one fact is not a report. The first
        # version of this guard printed exactly that.
        if sha(repo_file) != sha(installed_file):
            key = (str(relative), describe(repo_file), describe(installed_file))
            drift.setdefault(key, []).append(version)
        else:
            matched.add(str(relative))


def main(argv: list[str]) -> int:
    bundle_root = Path(argv[0]) if argv else default_bundle_root()
    if not bundle_root or str(bundle_root) == ".":
        print("APPDATA is not set, so the per-user bundle root cannot be located.")
        print("Pass the bundle root as an argument on a machine where it is installed.")
        return 0

    print(f"checkout bundle : {REPO_BUNDLE}")
    print(f"installed bundle: {bundle_root}")

    if not bundle_root.is_dir():
        print()
        print("No bundle is installed there, which is not drift -- it is nothing to compare.")
        print(f"Install one before a live window: {REINSTALL}")
        return 0

    failures: list[str] = []
    notes: list[str] = []
    drift: dict = {}
    matched: set = set()
    for version in VERSIONS:
        check_version(version, bundle_root, failures, notes, drift, matched)

    print()
    for note in notes:
        print(f"note: {note}")

    if drift or failures:
        plugin_drifted = any(relative == PLUGIN_DLL for relative, _, _ in drift)
        print()
        print("DRIFT: what Navisworks loads is not what this checkout built.")
        print("Live evidence gathered now would describe a different binary, and the build,")
        print("the installer and the host handshake would each still report success.")
        print()
        for (relative, in_bundle, installed), versions in sorted(drift.items()):
            print(f"  {relative}   differs in {', '.join(versions)}")
            print(f"      in bundle {in_bundle}")
            print(f"      installed {installed}")
        for failure in failures:
            print(f"  {failure}")
        if drift and not plugin_drifted and PLUGIN_DLL in matched:
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

    print()
    print("No drift: every installed file matches this checkout's bundle,")
    print("and the bundle matches the build output for each configuration built here.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
