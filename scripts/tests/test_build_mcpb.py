"""Curator-owned acceptance test for scripts/build_mcpb.py.

Claude Desktop drops servers written into claude_desktop_config.json on restart,
so NavisHelper ships to it as an MCP Bundle (.mcpb): a zip with manifest.json at
the root and the published MCP server under server/. These tests run the script
as a CLI against a fake server directory; no .NET build is needed.
"""

from __future__ import annotations

import json
import re
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts" / "build_mcpb.py"
EXE = "NavisHelper.McpServer.exe"


def run(*args: str) -> subprocess.CompletedProcess:
    return subprocess.run([sys.executable, str(SCRIPT), *args], capture_output=True,
                          text=True, encoding="utf-8", errors="replace", cwd=ROOT)


class BuildMcpbTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temp = tempfile.TemporaryDirectory()
        self.temp = Path(self._temp.name)
        self.server = self.temp / "server-out"
        (self.server / "runtimes" / "win" / "lib").mkdir(parents=True)
        (self.server / EXE).write_bytes(b"MZ fake exe")
        (self.server / "NavisHelper.McpServer.dll").write_bytes(b"fake dll")
        (self.server / "NavisHelper.McpServer.deps.json").write_text("{}", encoding="utf-8")
        (self.server / "runtimes" / "win" / "lib" / "native.dll").write_bytes(b"native")

    def tearDown(self) -> None:
        self._temp.cleanup()

    def build(self, version: str | None = "2.10.0.0", name: str = "out.mcpb") -> Path:
        output = self.temp / name
        args = ["--server-dir", str(self.server), "--output", str(output)]
        if version is not None:
            args += ["--version", version]
        result = run(*args)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return output

    def manifest(self, bundle: Path) -> dict:
        with zipfile.ZipFile(bundle) as archive:
            return json.loads(archive.read("manifest.json").decode("utf-8"))

    def test_bundle_layout(self) -> None:
        bundle = self.build()
        with zipfile.ZipFile(bundle) as archive:
            names = set(archive.namelist())
        self.assertIn("manifest.json", names)
        self.assertIn("icon.png", names)
        for relative in (EXE, "NavisHelper.McpServer.dll", "NavisHelper.McpServer.deps.json",
                         "runtimes/win/lib/native.dll"):
            self.assertIn("server/" + relative, names)
        self.assertTrue(all("\\" not in name for name in names), names)

    def test_manifest_contract(self) -> None:
        manifest = self.manifest(self.build())
        self.assertEqual(manifest["manifest_version"], "0.3")
        self.assertEqual(manifest["name"], "navishelper")
        self.assertEqual(manifest["display_name"], "NavisHelper for Navisworks")
        self.assertEqual(manifest["version"], "2.10.0")
        self.assertTrue(manifest["description"].strip())
        self.assertTrue(manifest["author"]["name"].strip())
        self.assertEqual(manifest["icon"], "icon.png")
        self.assertEqual(manifest["compatibility"]["platforms"], ["win32"])

        server = manifest["server"]
        self.assertEqual(server["type"], "binary")
        self.assertEqual(server["entry_point"], "server/" + EXE)
        config = server["mcp_config"]
        self.assertEqual(config["command"], "${__dirname}/server/" + EXE)
        self.assertEqual(config["args"], [])
        self.assertEqual(config["env"], {
            "NAVISHELPER_MCP_READ_ONLY": "${user_config.read_only}",
            "NAVISHELPER_MCP_TOOLS": "${user_config.tool_profile}",
        })

        user_config = manifest["user_config"]
        self.assertEqual(user_config["read_only"]["type"], "boolean")
        self.assertIs(user_config["read_only"]["default"], False)
        self.assertEqual(user_config["tool_profile"]["type"], "string")
        self.assertEqual(user_config["tool_profile"]["default"], "all")
        for entry in user_config.values():
            self.assertTrue(entry["title"].strip())
            self.assertTrue(entry["description"].strip())

    def test_fourth_version_part_becomes_build_metadata(self) -> None:
        self.assertEqual(self.manifest(self.build("2.10.0.3"))["version"], "2.10.0+3")

    def test_default_version_comes_from_the_server_project(self) -> None:
        project = (ROOT / "NavisHelper.McpServer" / "NavisHelper.McpServer.csproj").read_text(encoding="utf-8")
        major, minor, patch, revision = re.search(r"<Version>(\d+)\.(\d+)\.(\d+)\.(\d+)</Version>", project).groups()
        expected = f"{major}.{minor}.{patch}" + ("" if revision == "0" else "+" + revision)
        self.assertEqual(self.manifest(self.build(version=None))["version"], expected)

    def test_builds_are_byte_identical(self) -> None:
        first = self.build(name="a.mcpb").read_bytes()
        second = self.build(name="b.mcpb").read_bytes()
        self.assertEqual(first, second)

    def test_missing_server_executable_is_refused_without_output(self) -> None:
        (self.server / EXE).unlink()
        output = self.temp / "refused.mcpb"
        result = run("--server-dir", str(self.server), "--output", str(output), "--version", "2.10.0.0")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn(EXE, result.stdout + result.stderr)
        self.assertFalse(output.exists())

    def test_malformed_version_is_refused(self) -> None:
        output = self.temp / "bad.mcpb"
        result = run("--server-dir", str(self.server), "--output", str(output), "--version", "2.10")
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
