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
1 on drift, and equally on having verified nothing.
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
        return 0

    print(f"checkout bundle : {REPO_BUNDLE}")
    print(f"installed bundle: {bundle_root}")

    if not bundle_root.is_dir():
        print()
        print("No bundle is installed there, which is not drift -- it is nothing to compare.")
        print(f"Install one before a live window: {REINSTALL}")
        return 0

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
    return 0



def selftest() -> int:
    """Drive the branches this machine's state cannot reach, on fabricated trees.

    Link 2 (bundle -> install) is provable against a real machine: cause drift, see it
    named, reinstall, see it gone. Link 1 (build -> bundle) is not, without editing the
    checkout, and the branch that matters most -- refusing to approve a checkout whose
    DLLs were never built -- only shows up when nothing else is wrong.

    So they are driven here against temp directories, by pointing this module's own path
    constants at them. Nothing real is read or written, which is why this half can run in
    CI while the machine check cannot.
    """
    import io as _io
    import tempfile

    global ROOT, REPO_BUNDLE
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

    def run(root: Path, bundle: Path, installed: Path):
        global ROOT, REPO_BUNDLE
        ROOT, REPO_BUNDLE = root, bundle
        captured = _io.StringIO()
        stdout, sys.stdout = sys.stdout, captured
        try:
            code = main([str(installed)])
        finally:
            sys.stdout = stdout
        return code, captured.getvalue()

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
    real_root, real_bundle = ROOT, REPO_BUNDLE
    try:
        for label, kwargs, expected_code, expected, absent in cases:
            with tempfile.TemporaryDirectory(prefix="drift-selftest-") as tmp:
                root = Path(tmp)
                bundle, installed = build_tree(root, **kwargs)
                code, output = run(root, bundle, installed)
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
        ROOT, REPO_BUNDLE = real_root, real_bundle

    if failures:
        print("%d self-test assertion(s) failed" % failures)
        return 1
    print("self-test passed: every branch fires, and the agreeing case still exits 0")
    return 0


if __name__ == "__main__":
    if "--selftest" in sys.argv[1:]:
        raise SystemExit(selftest())
    raise SystemExit(main(sys.argv[1:]))
