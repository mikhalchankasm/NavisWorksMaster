#!/usr/bin/env python3
"""Fetch the external-executor launcher from an Avox clone at one pinned commit.

NavisHelper does not vendor Avox (https://github.com/mikhalchukasm/Avox.git).
An executor run must be reproducible, so this script copies five files from one
pinned Avox commit and refuses to write anything unless every file's SHA-256
matches the table below. Bump the pin by changing the commit and the five
hashes together in one reviewed PR.

The Avox clone is read through `git show <pin>:<path>` only: committed bytes,
never the working tree, and never a fetch. A missing pin is an error, not a
reason to go to the network. The default destination `artifacts/executor-launcher`
is git-ignored, so a fetched launcher is never committed by accident.

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
AVOX_PIN = "2be9a74707aba11a16e40c6d21ad15d20fab42a1"

# path in the Avox repository -> required SHA-256 at AVOX_PIN
PINNED_FILES = {
    "config/external-executors.json":
        "37090e3dffb167e801593a5a17e1dbf2c5959cb5b45e5750d79a8455176decb3",
    "scripts/agent_home_isolation.py":
        "da923cc00e1711235af9cb0bc2cc34bb69e3f5222eadef0d1ee27f3d921c02c7",
    "scripts/executor_primitives.py":
        "556894c60b637dac9a7950b534a69c3ebe31a4c5a9fe8ceffaba45965e490083",
    "scripts/executor_worktree.py":
        "19cbb115d83ed9eef550e113ab8034efc0bade18b23152803352084f2b2bcaf6",
    "scripts/external_worktree_executor.py":
        "fe78c9a2c19440bed28529ea4042c367dc2451f3a1a72a76f66034b229a9dda8",
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
