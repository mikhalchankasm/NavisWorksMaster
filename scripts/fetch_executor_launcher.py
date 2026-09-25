#!/usr/bin/env python3
"""Fetch the external-executor launcher from an Avox clone at one pinned commit.

NavisHelper does not vendor Avox (https://github.com/mikhalchankasm/Avox.git).
An executor run must be reproducible, so this script copies the files in the
table below from one pinned Avox commit and refuses to write anything unless
every file's SHA-256 matches. Bump the pin by changing the commit and every
hash together in one reviewed PR.

The Avox clone is read through `git show <pin>:<path>` only: committed bytes,
never the working tree, and never a fetch. A missing pin is an error, not a
reason to go to the network. The default destination `artifacts/executor-launcher`
is git-ignored, so a fetched launcher is never committed by accident.

A destination that already exists is refetched only when it holds nothing but
pinned files plus `__pycache__` directories left by an earlier run. Any
other file there would run alongside the launcher without being covered by the
verified hashes, so it is a refusal: the script names the extra files, writes
nothing, and deletes nothing.

Usage:
    python scripts/fetch_executor_launcher.py [--avox <path>] [--dest <path>]
"""

from __future__ import annotations

import argparse
import hashlib
import os
import subprocess
import sys
from pathlib import Path

# The Avox commit this repository pins. On Avox `master`.
AVOX_PIN = "c267be59b36bdbe7be0ac276610dab8b0db73827"

# path in the Avox repository -> required SHA-256 at AVOX_PIN
PINNED_FILES = {
    "config/external-executors.json":
        "37090e3dffb167e801593a5a17e1dbf2c5959cb5b45e5750d79a8455176decb3",
    "scripts/agent_home_isolation.py":
        "68051b0bd0bb2d49e3b1b7e7bfa47cda4fc785b565c2a85b63c1643744a9514b",
    "scripts/executor_host_guard.py":
        "96692834616d69c1ad28886aac2e9336e07ad0107d53d71bbc7b227bb42a438b",
    "scripts/executor_primitives.py":
        "82c631dc8fbe0717b5a598afd95aab7f97c24fd9717b44527a2d28a96069b4b3",
    "scripts/executor_worktree.py":
        "19cbb115d83ed9eef550e113ab8034efc0bade18b23152803352084f2b2bcaf6",
    "scripts/external_worktree_executor.py":
        "a8d1fbed96e471ea34cb642130afcf67f66b8cefe107b93a470b8ff0e444eb8d",
}

DEFAULT_DEST_RELATIVE = "artifacts/executor-launcher"


def repo_root() -> Path:
    return Path(__file__).resolve().parents[1]


def pin_present(avox: Path) -> bool:
    """Does the clone's object database hold the pinned commit? No fetch, no worktree read."""
    probed = subprocess.run(
        ["git", "-C", str(avox), "cat-file", "-e", f"{AVOX_PIN}^{{commit}}"],
        capture_output=True)
    return probed.returncode == 0


def read_pinned(avox: Path, relative: str) -> bytes:
    """The committed bytes of `relative` at AVOX_PIN."""
    shown = subprocess.run(
        ["git", "-C", str(avox), "show", f"{AVOX_PIN}:{relative}"],
        capture_output=True)
    if shown.returncode != 0:
        detail = shown.stderr.decode("utf-8", errors="replace").strip()
        raise SystemExit(
            f"git show {AVOX_PIN}:{relative} failed in {avox}: {detail}")
    return shown.stdout


def unpinned_destination_files(dest: Path) -> list[str]:
    """Files under `dest` that are neither pinned paths nor inside a `__pycache__`."""
    if not dest.is_dir():
        return []
    extras: list[str] = []
    for path in dest.rglob("*"):
        if not path.is_file():
            continue
        relative = path.relative_to(dest)
        if relative.as_posix() in PINNED_FILES:
            continue
        if "__pycache__" in relative.parts:
            continue
        extras.append(relative.as_posix())
    return extras


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--avox",
        default=os.environ.get("AVOX_REPO"),
        metavar="PATH",
        help="an Avox clone to read the pin from (default: the AVOX_REPO "
             "environment variable; one of the two is required)")
    parser.add_argument(
        "--dest",
        default=None,
        metavar="PATH",
        help=f"where to write the launcher (default: {DEFAULT_DEST_RELATIVE} "
             "under the repository root, which is git-ignored)")
    args = parser.parse_args(argv)

    if not args.avox:
        parser.error("--avox is required when AVOX_REPO is not set")

    avox = Path(args.avox)
    if not pin_present(avox):
        print(f"pin {AVOX_PIN} is not present in {avox}.", file=sys.stderr)
        print(f"Run:  git -C {avox} fetch origin", file=sys.stderr)
        return 1

    fetched: dict[str, bytes] = {}
    mismatches: list[str] = []
    for relative, expected in PINNED_FILES.items():
        data = read_pinned(avox, relative)
        digest = hashlib.sha256(data).hexdigest()
        if digest != expected:
            mismatches.append(f"{relative}: sha256 {digest}, expected {expected}")
        else:
            fetched[relative] = data
    if mismatches:
        print(f"the content at pin {AVOX_PIN} does not match the pinned hashes;", file=sys.stderr)
        for mismatch in mismatches:
            print(f"  - {mismatch}", file=sys.stderr)
        print("nothing was written.", file=sys.stderr)
        return 1

    dest = repo_root() / DEFAULT_DEST_RELATIVE if args.dest is None else Path(args.dest)
    extras = unpinned_destination_files(dest)
    if extras:
        print(f"{dest} already holds files the pinned launcher does not define:", file=sys.stderr)
        for extra in sorted(extras):
            print(f"  - {extra}", file=sys.stderr)
        print("nothing was written and nothing was deleted; remove those files", file=sys.stderr)
        print("(or point --dest at a fresh directory) and rerun.", file=sys.stderr)
        return 1

    for relative, data in fetched.items():
        target = dest / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)

    print(f"Fetched {len(fetched)} files from Avox pin {AVOX_PIN} into {dest}.")
    print("Run the executor launcher with:")
    print(f"  cd {dest}")
    print("  python -m scripts.external_worktree_executor"
          " --provider {glm|qwen|codex} --worktree <path>"
          " --brief <file> --timeout 1800 --record <json>")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
