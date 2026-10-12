"""Package a Windows MCP server directory as a Claude Desktop extension."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import shutil
import tempfile
import xml.etree.ElementTree as ET
import zipfile


ROOT = Path(__file__).resolve().parents[1]
EXE = "NavisHelper.McpServer.exe"


def bundle_version(value: str) -> str:
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+", value):
        raise ValueError("Version must have four numeric parts: a.b.c.d")
    major, minor, patch, revision = map(int, value.split("."))
    return f"{major}.{minor}.{patch}" + (f"+{revision}" if revision else "")


def project_version() -> str:
    project = ET.parse(ROOT / "NavisHelper.McpServer/NavisHelper.McpServer.csproj")
    value = project.findtext("./PropertyGroup/Version")
    if value is None:
        raise ValueError("The MCP server project has no Version")
    return value.strip()


def manifest(version: str) -> dict:
    return {
        "manifest_version": "0.3",
        "name": "navishelper",
        "display_name": "NavisHelper for Navisworks",
        "version": version,
        "description": "Connect Claude Desktop to Navisworks through NavisHelper.",
        "author": {"name": "NavisHelper contributors"},
        "icon": "icon.png",
        "compatibility": {"platforms": ["win32"]},
        "server": {
            "type": "binary",
            "entry_point": "server/" + EXE,
            "mcp_config": {
                "command": "${__dirname}/server/" + EXE,
                "args": [],
                "env": {
                    "NAVISHELPER_MCP_READ_ONLY": "${user_config.read_only}",
                    "NAVISHELPER_MCP_TOOLS": "${user_config.tool_profile}",
                },
            },
        },
        "user_config": {
            "read_only": {
                "type": "boolean",
                "title": "Read-only mode",
                "description": "Restrict the MCP server to read-only operations.",
                "default": False,
                "required": False,
            },
            "tool_profile": {
                "type": "string",
                "title": "Tool profile",
                "description": "Tools to expose: all, core, or server tool set names.",
                "default": "all",
                "required": False,
            },
        },
    }


def entry_info(name: str) -> zipfile.ZipInfo:
    info = zipfile.ZipInfo(name, date_time=(1980, 1, 1, 0, 0, 0))
    info.create_system = 3
    info.external_attr = 0o100644 << 16
    info.compress_type = zipfile.ZIP_DEFLATED
    return info


def build(server_dir: Path, output: Path, version: str) -> None:
    version = bundle_version(version)
    server_dir = server_dir.resolve()
    output = output.resolve()
    if not (server_dir / EXE).is_file():
        raise ValueError(f"Missing server executable: {server_dir / EXE}")
    if output.is_relative_to(server_dir):
        raise ValueError("Output must be outside the server directory")

    entries = {
        "icon.png": ROOT / "NavisHelper/Icons/ColorsByName_32.png",
        **{
            "server/" + path.relative_to(server_dir).as_posix(): path
            for path in server_dir.rglob("*") if path.is_file()
        },
    }
    manifest_bytes = (json.dumps(manifest(version), indent=2, sort_keys=True)
                      + "\n").encode("utf-8")
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(dir=output.parent, prefix=output.name + ".",
                                         suffix=".tmp", delete=False) as handle:
            temporary = Path(handle.name)
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED,
                             compresslevel=9) as archive:
            for name in sorted(["manifest.json", *entries]):
                info = entry_info(name)
                if name == "manifest.json":
                    archive.writestr(info, manifest_bytes, compresslevel=9)
                else:
                    # Streaming avoids holding server binaries in memory.
                    with entries[name].open("rb") as source:
                        info.file_size = entries[name].stat().st_size
                        with archive.open(info, "w") as destination:
                            shutil.copyfileobj(source, destination)
        os.replace(temporary, output)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--server-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", help="Four-part version; defaults to the server project")
    args = parser.parse_args()
    try:
        build(args.server_dir, args.output,
              args.version if args.version is not None else project_version())
    except (OSError, ValueError, ET.ParseError, zipfile.LargeZipFile) as error:
        parser.exit(1, f"Error: {error}\n")
    print(f"Created {args.output}")


if __name__ == "__main__":
    main()
