#!/usr/bin/env python3
"""
NavisHelper MCP smoke test.

Checks both layers:
- direct Navisworks host bridge over the local named pipe;
- public MCP stdio server over newline-delimited JSON-RPC.

The script can optionally launch Navisworks Manage with the newest .nwd file in a folder.
"""

from __future__ import annotations

import argparse
import ctypes
import json
import os
import pathlib
import queue
import struct
import subprocess
import sys
import tempfile
import threading
import time
import uuid


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--nwd-dir", default="", help="Folder with .nwd files. Used with --launch.")
    parser.add_argument("--version", default="2027", help="Navisworks version to target.")
    parser.add_argument("--launch", action="store_true", help="Launch Navisworks with the newest .nwd from --nwd-dir.")
    parser.add_argument("--roamer", default="", help="Explicit Roamer.exe path.")
    parser.add_argument("--mcp-server", default="", help="Explicit NavisHelper.McpServer.exe path.")
    parser.add_argument("--timeout", type=int, default=180, help="Startup/discovery timeout in seconds.")
    parser.add_argument("--skip-export-write", action="store_true", help="Do not write the temporary CSV export.")
    return parser.parse_args()


def repo_root() -> pathlib.Path:
    return pathlib.Path(__file__).resolve().parents[1]


def default_roamer(version: str) -> pathlib.Path:
    return pathlib.Path(r"C:\Program Files\Autodesk") / f"Navisworks Manage {version}" / "Roamer.exe"


def default_mcp_server() -> pathlib.Path:
    return repo_root() / "NavisHelper.McpServer" / "bin" / "Release" / "net9.0" / "NavisHelper.McpServer.exe"


def latest_nwd(nwd_dir: str) -> pathlib.Path:
    if not nwd_dir:
        raise RuntimeError("--nwd-dir is required with --launch.")
    folder = pathlib.Path(nwd_dir)
    files = list(folder.glob("*.nwd"))
    if not files:
        raise RuntimeError(f"No .nwd files found in {folder}")
    return max(files, key=lambda item: item.stat().st_mtime)


def launch_navisworks(args: argparse.Namespace) -> None:
    roamer = pathlib.Path(args.roamer) if args.roamer else default_roamer(args.version)
    if not roamer.exists():
        raise RuntimeError(f"Roamer.exe not found: {roamer}")
    model = latest_nwd(args.nwd_dir)
    print(f"Launching Navisworks {args.version}: {model}")
    subprocess.Popen([str(roamer), str(model)])


def instances_dir() -> pathlib.Path:
    return pathlib.Path(os.environ["LOCALAPPDATA"]) / "NavisHelper" / "Mcp" / "instances"


def is_process_running(pid_value: object) -> bool:
    try:
        pid = int(pid_value)
    except (TypeError, ValueError):
        return False
    if pid <= 0:
        return False
    if os.name == "nt":
        kernel32 = ctypes.windll.kernel32
        handle = kernel32.OpenProcess(0x1000, False, pid)
        if not handle:
            return False
        try:
            exit_code = ctypes.c_ulong()
            if not kernel32.GetExitCodeProcess(handle, ctypes.byref(exit_code)):
                return True
            return exit_code.value == 259
        finally:
            kernel32.CloseHandle(handle)
    try:
        os.kill(pid, 0)
        return True
    except OSError:
        return False


def wait_instance(version: str, timeout: int, min_mtime: float | None = None) -> dict:
    deadline = time.time() + timeout
    while time.time() < deadline:
        records = []
        folder = instances_dir()
        if folder.exists():
            for path in folder.glob("*.json"):
                path_mtime = path.stat().st_mtime
                if min_mtime is not None and path_mtime < min_mtime:
                    continue
                try:
                    data = json.loads(path.read_text(encoding="utf-8-sig"))
                except Exception:
                    continue
                if str(data.get("navisworks_version", "")) == str(version):
                    if not is_process_running(data.get("pid")):
                        continue
                    data["_path"] = str(path)
                    data["_mtime"] = path_mtime
                    records.append(data)
        if records:
            return max(records, key=lambda item: item.get("_mtime", 0))
        time.sleep(2)
    raise TimeoutError(f"No NavisHelper MCP host discovery record for Navisworks {version}.")


def pipe_path(pipe_name: str) -> str:
    return r"\\.\pipe\\" + pipe_name


def read_exact(stream, size: int) -> bytes:
    chunks = []
    remaining = size
    while remaining:
        data = stream.read(remaining)
        if not data:
            raise EOFError("Pipe closed while reading frame.")
        chunks.append(data)
        remaining -= len(data)
    return b"".join(chunks)


def host_call(record: dict, command: str, payload: dict | None = None, timeout_ms: int = 120000) -> dict:
    request = {
        "request_id": "smoke-" + uuid.uuid4().hex,
        "instance_id": record["instance_id"],
        "command": command,
        "timeout_ms": timeout_ms,
        "payload": payload or {},
    }
    body = json.dumps(request, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    with open(pipe_path(record["pipe_name"]), "r+b", buffering=0) as stream:
        stream.write(struct.pack("<i", len(body)))
        stream.write(body)
        stream.flush()
        length = struct.unpack("<i", read_exact(stream, 4))[0]
        response = read_exact(stream, length)
    return json.loads(response.decode("utf-8"))


def run_subtree_dump_smoke(record: dict, root_name: str) -> dict:
    output_path = pathlib.Path(tempfile.gettempdir()) / f"navishelper-subtree-dump-smoke-{int(time.time())}.csv"
    partial_path = pathlib.Path(str(output_path) + ".partial")
    for path in (output_path, partial_path):
        try:
            path.unlink()
        except FileNotFoundError:
            pass

    started = host_call(record, "start_subtree_names_dump", {
        "root_name": root_name,
        "output_path": str(output_path),
        "format": "csv",
        "include_path": False,
        "include_source_file": False,
        "include_hidden": True,
        "overwrite": True,
    })
    assert_ok("start_subtree_names_dump", started)
    job_id = started["payload"].get("job_id")
    if not job_id:
        raise RuntimeError(f"start_subtree_names_dump returned no job_id: {started}")
    returned_instance_id = started["payload"].get("instance_id") or ""
    if returned_instance_id != record["instance_id"]:
        raise RuntimeError(f"start_subtree_names_dump returned unexpected instance_id: {started}")

    status = started
    for _ in range(20):
        status = host_call(record, "dump_subtree_names_status", {
            "job_id": job_id,
            "max_items_per_poll": 200,
            "max_elapsed_ms": 250,
        })
        assert_ok("dump_subtree_names_status", status)
        state = status["payload"].get("state")
        if state in {"done", "failed", "cancelled"}:
            break

    payload = status["payload"]
    state = payload.get("state")
    if state == "failed":
        raise RuntimeError(f"dump_subtree_names_status failed: {status}")

    if state == "done":
        item_count = payload.get("item_count", 0)
        file_size = payload.get("file_size_bytes", 0)
        if item_count < 1 or file_size < 1:
            raise RuntimeError(f"Completed subtree dump returned invalid counts: {status}")
        if not output_path.exists():
            raise RuntimeError(f"Completed subtree dump did not create output file: {output_path}")
        with output_path.open("r", encoding="utf-8-sig") as handle:
            line_count = sum(1 for _ in handle)
        if line_count < 2:
            raise RuntimeError(f"Completed subtree dump wrote too few CSV rows: lines={line_count} status={status}")
        try:
            output_path.unlink()
        except FileNotFoundError:
            pass
        return {
            "state": state,
            "instance_id": returned_instance_id,
            "item_count": item_count,
            "file_size_bytes": file_size,
            "line_count": line_count,
        }

    processed_count = payload.get("processed_item_count", 0)
    if processed_count < 1:
        raise RuntimeError(f"Chunked subtree dump made no progress before cancellation: {status}")

    cancelled = host_call(record, "cancel_subtree_names_dump", {"job_id": job_id})
    assert_ok("cancel_subtree_names_dump", cancelled)
    if cancelled["payload"].get("state") != "cancelled":
        raise RuntimeError(f"cancel_subtree_names_dump did not report cancelled: {cancelled}")
    if partial_path.exists():
        raise RuntimeError(f"cancel_subtree_names_dump left partial file behind: {partial_path}")
    try:
        output_path.unlink()
    except FileNotFoundError:
        pass
    return {
        "state": "cancelled_after_progress",
        "instance_id": returned_instance_id,
        "processed_item_count": processed_count,
    }


class McpClient:
    def __init__(self, exe: pathlib.Path):
        self.proc = subprocess.Popen(
            [str(exe)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
            errors="replace",
            bufsize=1,
        )
        self.out = queue.Queue()
        self.err = []
        threading.Thread(target=self._read_out, daemon=True).start()
        threading.Thread(target=self._read_err, daemon=True).start()

    def _read_out(self) -> None:
        assert self.proc.stdout is not None
        for line in self.proc.stdout:
            line = line.strip()
            if line:
                self.out.put(line)

    def _read_err(self) -> None:
        assert self.proc.stderr is not None
        for line in self.proc.stderr:
            line = line.strip()
            if line:
                self.err.append(line)

    def send(self, message: dict) -> None:
        assert self.proc.stdin is not None
        self.proc.stdin.write(json.dumps(message, ensure_ascii=False, separators=(",", ":")) + "\n")
        self.proc.stdin.flush()

    def read(self, timeout: int = 60) -> dict:
        deadline = time.time() + timeout
        while time.time() < deadline:
            try:
                line = self.out.get(timeout=0.2)
            except queue.Empty:
                continue
            try:
                return json.loads(line)
            except json.JSONDecodeError:
                continue
        tail = "\n".join(self.err[-10:])
        raise TimeoutError(f"Timed out waiting for MCP response. stderr tail:\n{tail}")

    def close(self) -> None:
        self.proc.terminate()
        try:
            self.proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            self.proc.kill()


def mcp_call(client: McpClient, request_id: int, method: str, params: dict | None = None, timeout: int = 60) -> dict:
    message = {"jsonrpc": "2.0", "id": request_id, "method": method}
    if params is not None:
        message["params"] = params
    client.send(message)
    return client.read(timeout)


def assert_ok(name: str, response: dict) -> None:
    if not response.get("ok", False):
        raise RuntimeError(f"{name} failed: {response.get('error_code')} {response.get('error_message')}")


def wait_loaded_document(record: dict, timeout: int) -> dict:
    deadline = time.time() + timeout
    last_status = None
    last_error = None
    while time.time() < deadline:
        try:
            last_status = host_call(record, "host_status")
        except (FileNotFoundError, OSError, EOFError) as exc:
            last_error = exc
            time.sleep(2)
            continue
        assert_ok("host_status", last_status)
        payload = last_status.get("payload", {})
        if payload.get("has_active_document") and int(payload.get("root_item_count") or 0) > 0:
            return last_status
        time.sleep(3)
    payload = (last_status or {}).get("payload", {})
    raise TimeoutError(
        "Active document did not finish loading root items. "
        f"title={payload.get('document_title')} roots={payload.get('root_item_count')} last_error={last_error}"
    )


def main() -> int:
    args = parse_args()
    min_instance_mtime = None
    if args.launch:
        min_instance_mtime = time.time() - 1.0
        launch_navisworks(args)

    record = wait_instance(args.version, args.timeout, min_instance_mtime)
    print(f"Host: {record['instance_id']} pipe={record['pipe_name']}")

    status = wait_loaded_document(record, args.timeout)
    print(f"Document: {status['payload'].get('document_title')} roots={status['payload'].get('root_item_count')}")

    roots = host_call(record, "list_root_items", {"limit": 5, "include_aliases": True})
    assert_ok("list_root_items", roots)
    items = roots["payload"].get("items", [])
    if not items:
        raise RuntimeError("No root items returned.")
    target = items[1] if len(items) > 1 else items[0]
    target_name = target.get("display_name") or target.get("file_name")
    print(f"Selection target: {target_name}")

    found = host_call(record, "find_root_items_by_name", {"names": [target_name], "comparison": "equals", "preview_limit": 3})
    assert_ok("find_root_items_by_name", found)
    handle = found["payload"]["results"][0]["matches"][0]["match_handle"]
    dump_result = run_subtree_dump_smoke(record, target_name)
    print(f"Subtree dump smoke: {dump_result}")

    selected = host_call(record, "select_items", {"match_handles": [handle], "apply": True, "preview_limit": 3})
    assert_ok("select_items", selected)

    report = host_call(record, "selection_property_report", {
        "item_limit": 1,
        "property_limit_per_item": 10,
        "row_limit": 10,
        "include_internal_names": True,
    })
    assert_ok("selection_property_report", report)
    print(f"Property report rows: {report['payload'].get('row_count')}")
    distinct = host_call(record, "selection_distinct_property_values", {
        "item_limit": 1,
        "value_limit": 10,
        "category_filters": ["Элемент"],
        "property_filters": ["Тип"],
    })
    assert_ok("selection_distinct_property_values", distinct)
    print(f"Distinct values returned: {distinct['payload'].get('returned_value_count')}")
    color_preview = host_call(record, "selection_color_by_property", {
        "apply": False,
        "item_limit": 1,
        "group_limit": 10,
        "category_filters": ["Элемент"],
        "property_filters": ["Тип"],
    })
    assert_ok("selection_color_by_property dry-run", color_preview)
    print(f"Color preview groups: {color_preview['payload'].get('returned_group_count')}")

    export_path = pathlib.Path(tempfile.gettempdir()) / "navishelper-selection-properties-smoke.csv"
    xlsx_export_path = pathlib.Path(tempfile.gettempdir()) / "navishelper-selection-properties-smoke.xlsx"
    dry = host_call(record, "selection_export_properties", {
        "output_path": str(export_path),
        "apply": False,
        "overwrite": True,
        "item_limit": 1,
        "property_limit_per_item": 10,
        "row_limit": 10,
        "include_internal_names": True,
    })
    assert_ok("selection_export_properties dry-run", dry)
    if not args.skip_export_write:
        written = host_call(record, "selection_export_properties", {
            "output_path": str(export_path),
            "apply": True,
            "overwrite": True,
            "item_limit": 1,
            "property_limit_per_item": 10,
            "row_limit": 10,
            "include_internal_names": True,
        })
        assert_ok("selection_export_properties write", written)
        print(f"CSV export: {export_path} bytes={written['payload'].get('file_size_bytes')}")
        written_xlsx = host_call(record, "selection_export_properties", {
            "output_path": str(xlsx_export_path),
            "format": "xlsx",
            "apply": True,
            "overwrite": True,
            "item_limit": 1,
            "property_limit_per_item": 10,
            "row_limit": 10,
            "include_internal_names": True,
        })
        assert_ok("selection_export_properties xlsx write", written_xlsx)
        print(f"XLSX export: {xlsx_export_path} bytes={written_xlsx['payload'].get('file_size_bytes')}")

    tests = host_call(record, "clash_list_tests", {"limit": 3, "include_status_counts": True})
    assert_ok("clash_list_tests", tests)
    print(f"Clash tests returned: {tests['payload'].get('returned_test_count')} total={tests['payload'].get('total_test_count')}")

    mcp_server = pathlib.Path(args.mcp_server) if args.mcp_server else default_mcp_server()
    if not mcp_server.exists():
        raise RuntimeError(f"MCP server not found: {mcp_server}")
    client = McpClient(mcp_server)
    try:
        init = mcp_call(client, 1, "initialize", {
            "protocolVersion": "2024-11-05",
            "capabilities": {},
            "clientInfo": {"name": "navishelper-smoke", "version": "1.0"},
        })
        if "result" not in init:
            raise RuntimeError(f"MCP initialize failed: {init}")
        client.send({"jsonrpc": "2.0", "method": "notifications/initialized", "params": {}})
        tools = mcp_call(client, 2, "tools/list", {})
        names = {tool.get("name") for tool in tools.get("result", {}).get("tools", [])}
        required = {
            "selection_property_report",
            "selection_export_properties",
            "selection_distinct_property_values",
            "selection_color_by_property",
            "dump_subtree_names",
            "start_subtree_names_dump",
            "dump_subtree_names_status",
            "cancel_subtree_names_dump",
            "clash_list_tests",
            "clash_list_results",
            "clash_manage_tests",
        }
        missing = sorted(required - names)
        if missing:
            raise RuntimeError(f"MCP tools missing: {missing}")
        print(f"MCP tools OK: {len(names)} tools, required present.")
    finally:
        client.close()

    print("SMOKE OK")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"SMOKE FAILED: {exc}", file=sys.stderr)
        raise
