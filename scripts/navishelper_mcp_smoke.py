#!/usr/bin/env python3
import argparse
import concurrent.futures
import json
import queue
import subprocess
import sys
import tempfile
import threading
import time
import zipfile
from pathlib import Path


REQUIRED_TOOLS = {
    "list_navisworks_hosts",
    "mcp_diagnostics",
    "mcp_recent_calls",
    "mcp_error_contract",
    "list_recent_navisworks_files",
    "start_navisworks",
    "open_latest_navisworks_file",
    "mcp_task_timer_start",
    "mcp_task_timer_finish",
    "mcp_health_check",
    "list_scenarios",
    "get_scenario",
    "save_scenario",
    "delete_scenario",
    "resolve_scenario",
    "active_model_context",
    "host_status",
    "selection_status",
    "selected_items_preview",
    "selected_items_ancestry",
    "selected_items_tree",
    "list_item_children",
    "item_properties_by_handle",
    "selection_property_report",
    "selection_export_properties",
    "model_color_scheme",
    "dump_subtree_names",
    "start_subtree_names_dump",
    "dump_subtree_names_status",
    "cancel_subtree_names_dump",
    "current_viewpoint_info",
    "list_saved_viewpoints",
    "list_selection_sets",
    "select_selection_set",
    "create_search_set",
    "selection_sets_manage",
    "selection_sets_reorder",
    "activate_saved_viewpoint",
    "list_root_items",
    "find_root_items_by_name",
    "find_items_by_bbox",
    "select_items",
    "show_all",
    "create_selection_set",
    "create_viewpoint",
    "zoom_to_selection",
    "fit_all",
    "clash_list_tests",
    "clash_list_results",
    "clash_bbox_pair_plan",
    "clash_pair_tests_create",
    "clash_create_matrix_from_selection",
    "clash_generate_report",
    "clash_save_viewpoints",
    "clash_isolate_result",
    "clash_reset_isolation",
    "capture_current_view",
    "clash_manage_tests",
    "saved_viewpoints_import",
}


class McpClient:
    def __init__(self, repo_root: Path):
        dll = repo_root / "NavisHelper.McpServer" / "bin" / "Release" / "net9.0" / "NavisHelper.McpServer.dll"
        if dll.exists():
            command = ["dotnet", str(dll)]
        else:
            command = [
                "dotnet",
                "run",
                "--project",
                str(repo_root / "NavisHelper.McpServer" / "NavisHelper.McpServer.csproj"),
                "-c",
                "Release",
                "--no-build",
            ]

        self._process = subprocess.Popen(
            command,
            cwd=str(repo_root),
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
            errors="replace",
            bufsize=1,
        )
        self._stdout = queue.Queue()
        self._stderr_lines = []
        self._next_id = 1

        threading.Thread(target=self._drain_stdout, daemon=True).start()
        threading.Thread(target=self._drain_stderr, daemon=True).start()

    def close(self):
        if self._process.poll() is None:
            self._process.terminate()
            try:
                self._process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                self._process.kill()

    def initialize(self):
        response = self.request(
            "initialize",
            {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": {"name": "navishelper-mcp-smoke", "version": "1.0"},
            },
        )
        self.notify("notifications/initialized", {})
        return response

    def list_tools(self):
        response = self.request("tools/list", {})
        return response["result"]["tools"]

    def call_tool(self, name, arguments=None):
        response = self.request("tools/call", {"name": name, "arguments": arguments or {}}, timeout=90)
        if "error" in response:
            raise RuntimeError(f"tools/call {name} failed: {response['error']}")

        result = response.get("result", {})
        content = result.get("content", [])
        if not content:
            return None

        text = content[0].get("text", "")
        if result.get("isError"):
            raise RuntimeError(f"tools/call {name} returned error content: {text}")
        if not text:
            return None

        try:
            return json.loads(text)
        except json.JSONDecodeError:
            return text

    def request(self, method, params, timeout=30):
        request_id = self._next_id
        self._next_id += 1
        self._send({"jsonrpc": "2.0", "id": request_id, "method": method, "params": params})

        deadline = time.time() + timeout
        seen = []
        while time.time() < deadline:
            if self._process.poll() is not None:
                raise RuntimeError(
                    "MCP server exited with code "
                    f"{self._process.returncode}. stderr={self._stderr_lines[-20:]}"
                )

            try:
                line = self._stdout.get(timeout=0.2)
            except queue.Empty:
                continue

            if not line.strip():
                continue

            seen.append(line)
            try:
                message = json.loads(line)
            except json.JSONDecodeError:
                continue

            if message.get("id") == request_id:
                return message

        raise TimeoutError(
            f"Timed out waiting for response id={request_id}. "
            f"seen={seen[-5:]} stderr={self._stderr_lines[-20:]}"
        )

    def notify(self, method, params):
        self._send({"jsonrpc": "2.0", "method": method, "params": params})

    def _send(self, payload):
        line = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
        self._process.stdin.write(line + "\n")
        self._process.stdin.flush()

    def _drain_stdout(self):
        for line in self._process.stdout:
            self._stdout.put(line.rstrip("\n"))

    def _drain_stderr(self):
        for line in self._process.stderr:
            self._stderr_lines.append(line.rstrip())


def repo_root_from_script() -> Path:
    return Path(__file__).resolve().parents[1]


def choose_names(list_response, explicit_names):
    if explicit_names:
        return explicit_names

    items = list_response.get("items", [])
    names = []
    for item in items:
        file_name = item.get("fileName") or item.get("file_name") or ""
        lowered = file_name.lower()
        if lowered.endswith(".rvm") or lowered.endswith(".dwg"):
            names.append(file_name)
        if len(names) >= 2:
            break

    if not names:
        raise RuntimeError("No .rvm/.dwg root items were found by list_root_items.")

    return names


def choose_many_names(list_response, limit):
    items = list_response.get("items", [])
    seen = set()

    def candidates_for(item):
        source_file = item.get("sourceFile") or item.get("source_file") or ""
        source_name = Path(source_file).name if source_file else ""
        return [
            item.get("fileName") or item.get("file_name") or "",
            item.get("displayName") or item.get("display_name") or "",
            source_name,
            item.get("path") or "",
        ]

    names = []
    for item in items:
        candidates = candidates_for(item)
        chosen = next(
            (
                candidate for candidate in candidates
                if candidate and (candidate.lower().endswith(".rvm") or candidate.lower().endswith(".dwg"))
            ),
            "",
        )
        if chosen and chosen.lower() not in seen:
            names.append(chosen)
            seen.add(chosen.lower())
        if len(names) >= limit:
            break

    if len(names) >= limit:
        return names

    for item in items:
        candidates = candidates_for(item)
        chosen = next((candidate for candidate in candidates if candidate and not candidate.lower().endswith(".nwd")), "")
        if chosen and chosen.lower() not in seen:
            names.append(chosen)
            seen.add(chosen.lower())
        if len(names) >= limit:
            break

    return names


def common_prefix(values):
    if not values:
        return ""

    prefix = values[0]
    for value in values[1:]:
        while prefix and not value.lower().startswith(prefix.lower()):
            prefix = prefix[:-1]
        if not prefix:
            break

    return prefix.strip()


def extract_handles(find_response):
    handles = []
    for result in find_response.get("results", []):
        for match in result.get("matches", []):
            handle = match.get("matchHandle") or match.get("match_handle")
            if handle:
                handles.append(handle)
    return handles


def get_value(payload, camel_name, snake_name):
    return payload.get(camel_name, payload.get(snake_name))


def choose_target(hosts_response, instance_id, navisworks_version):
    if instance_id or navisworks_version:
        return {
            "instanceId": instance_id or "",
            "navisworksVersion": navisworks_version or "",
        }

    hosts = hosts_response.get("hosts", [])
    if len(hosts) != 1:
        details = [
            {
                "instanceId": get_value(host, "instanceId", "instance_id"),
                "pid": host.get("pid"),
                "navisworksVersion": get_value(host, "navisworksVersion", "navisworks_version"),
                "documentTitle": get_value(host, "documentTitle", "document_title"),
            }
            for host in hosts
        ]
        raise RuntimeError(
            "Expected exactly one Navisworks host for smoke test. "
            f"Use --instance-id or --navisworks-version. Hosts={details}"
        )

    return {
        "instanceId": get_value(hosts[0], "instanceId", "instance_id") or "",
        "navisworksVersion": "",
    }


def add_target(arguments, target):
    result = dict(arguments or {})
    if target.get("instanceId"):
        result["instanceId"] = target["instanceId"]
    if target.get("navisworksVersion"):
        result["navisworksVersion"] = target["navisworksVersion"]
    return result


def run_subtree_dump_smoke(client, target, root_name):
    output_path = Path(tempfile.gettempdir()) / f"navishelper-subtree-dump-smoke-{int(time.time())}.csv"
    partial_path = Path(str(output_path) + ".partial")
    for path in (output_path, partial_path):
        try:
            path.unlink()
        except FileNotFoundError:
            pass

    started = client.call_tool(
        "start_subtree_names_dump",
        add_target(
            {
                "rootName": root_name,
                "outputPath": str(output_path),
                "format": "csv",
                "includePath": False,
                "includeSourceFile": False,
                "includeHidden": True,
                "overwrite": True,
            },
            target,
        ),
    )
    job_id = get_value(started, "jobId", "job_id")
    if not job_id:
        raise RuntimeError(f"start_subtree_names_dump returned no jobId: {started}")
    returned_instance_id = get_value(started, "instanceId", "instance_id") or ""
    if target.get("instanceId") and returned_instance_id != target["instanceId"]:
        raise RuntimeError(f"start_subtree_names_dump returned unexpected instanceId: {started}")

    status = started
    for _ in range(20):
        status = client.call_tool(
            "dump_subtree_names_status",
            add_target({"jobId": job_id, "maxItemsPerPoll": 200, "maxElapsedMs": 250}, target),
        )
        state = get_value(status, "state", "state")
        if state in ("done", "failed", "cancelled"):
            break

    state = get_value(status, "state", "state")
    if state == "failed":
        raise RuntimeError(f"dump_subtree_names_status failed: {status}")

    if state == "done":
        item_count = get_value(status, "itemCount", "item_count") or 0
        file_size = get_value(status, "fileSizeBytes", "file_size_bytes") or 0
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
            "instanceId": returned_instance_id,
            "itemCount": item_count,
            "fileSizeBytes": file_size,
            "lineCount": line_count,
        }

    processed_count = get_value(status, "processedItemCount", "processed_item_count") or 0
    if processed_count < 1:
        raise RuntimeError(f"Chunked subtree dump made no progress before cancellation: {status}")

    cancelled = client.call_tool("cancel_subtree_names_dump", add_target({"jobId": job_id}, target))
    if get_value(cancelled, "state", "state") != "cancelled":
        raise RuntimeError(f"cancel_subtree_names_dump did not report cancelled: {cancelled}")
    if partial_path.exists():
        raise RuntimeError(f"cancel_subtree_names_dump left partial file behind: {partial_path}")
    try:
        output_path.unlink()
    except FileNotFoundError:
        pass
    return {
        "state": "cancelled_after_progress",
        "instanceId": returned_instance_id,
        "processedItemCount": processed_count,
    }


def run_selection_sets_lifecycle_smoke(client, target, root_name):
    """Exercise temporary Search Set mutations in a disposable Navisworks process."""
    prefix = f"__NH MCP lifecycle {int(time.time())}"
    target_folder = prefix + "/Target"
    set_11_path = prefix + "/Set 11"
    set_2_path = prefix + "/Set 2"
    moved_set_path = target_folder + "/Set 11"
    renamed_set_path = target_folder + "/Set 10"
    condition = {"category": "Item", "property": "Name", "operator": "equals", "value": root_name}

    def call_manage(operation, **arguments):
        payload = {"operation": operation}
        payload.update(arguments)
        return client.call_tool("selection_sets_manage", add_target(payload, target))

    def list_items():
        response = client.call_tool(
            "list_selection_sets",
            add_target({"pathPrefix": prefix, "limit": 100, "includeItemIds": True}, target),
        )
        return response.get("items", [])

    def item_at(path):
        for item in list_items():
            if get_value(item, "path", "path") == path:
                item_id = get_value(item, "itemId", "item_id")
                if item_id:
                    return item
        raise RuntimeError(f"Selection Sets lifecycle item was not found: {path}")

    folder_preview = call_manage("create_folder", pathOrName=target_folder)
    if folder_preview.get("apply", True):
        raise RuntimeError(f"create_folder did not default to dry-run: {folder_preview}")
    folder_apply = call_manage("create_folder", pathOrName=target_folder, apply=True)
    if not folder_apply.get("changed", False):
        raise RuntimeError(f"create_folder apply=true did not report change: {folder_apply}")

    for name in ("Set 11", "Set 2"):
        preview = client.call_tool(
            "create_search_set",
            add_target({"name": name, "folderPath": prefix, "conditions": [condition]}, target),
        )
        if preview.get("apply", True) or get_value(preview, "matchedItemCount", "matched_item_count") < 1:
            raise RuntimeError(f"create_search_set dry-run did not resolve the root condition: {preview}")
        applied = client.call_tool(
            "create_search_set",
            add_target({"name": name, "folderPath": prefix, "conditions": [condition], "apply": True}, target),
        )
        if not applied.get("created", False):
            raise RuntimeError(f"create_search_set apply=true did not create '{name}': {applied}")

    reorder_preview = client.call_tool(
        "selection_sets_reorder", add_target({"folderPath": prefix, "recursive": True}, target)
    )
    if reorder_preview.get("apply", True) or get_value(reorder_preview, "reorderedFolderCount", "reordered_folder_count") < 1:
        raise RuntimeError(f"selection_sets_reorder did not produce a natural-order dry-run plan: {reorder_preview}")
    reorder_apply = client.call_tool(
        "selection_sets_reorder", add_target({"folderPath": prefix, "recursive": True, "apply": True}, target)
    )
    if not reorder_apply.get("changed", False):
        raise RuntimeError(f"selection_sets_reorder apply=true did not report change: {reorder_apply}")

    set_11 = item_at(set_11_path)
    move_preview = call_manage("move", itemId=get_value(set_11, "itemId", "item_id"), targetFolderPath=target_folder)
    if move_preview.get("apply", True):
        raise RuntimeError(f"selection_sets_manage move did not default to dry-run: {move_preview}")
    move_apply = call_manage("move", itemId=get_value(set_11, "itemId", "item_id"), targetFolderPath=target_folder, apply=True)
    if not move_apply.get("changed", False):
        raise RuntimeError(f"selection_sets_manage move apply=true did not report change: {move_apply}")

    moved_set = item_at(moved_set_path)
    rename_preview = call_manage("rename", itemId=get_value(moved_set, "itemId", "item_id"), newName="Set 10")
    if rename_preview.get("apply", True):
        raise RuntimeError(f"selection_sets_manage rename did not default to dry-run: {rename_preview}")
    rename_apply = call_manage("rename", itemId=get_value(moved_set, "itemId", "item_id"), newName="Set 10", apply=True)
    if not rename_apply.get("changed", False):
        raise RuntimeError(f"selection_sets_manage rename apply=true did not report change: {rename_apply}")
    item_at(renamed_set_path)

    set_2 = item_at(set_2_path)
    delete_set_preview = call_manage("delete_set", itemId=get_value(set_2, "itemId", "item_id"))
    if delete_set_preview.get("apply", True):
        raise RuntimeError(f"selection_sets_manage delete_set did not default to dry-run: {delete_set_preview}")
    delete_set_apply = call_manage("delete_set", itemId=get_value(set_2, "itemId", "item_id"), apply=True)
    if not delete_set_apply.get("changed", False):
        raise RuntimeError(f"selection_sets_manage delete_set apply=true did not report change: {delete_set_apply}")

    root_folder = item_at(prefix)
    delete_folder_preview = call_manage("delete_folder", itemId=get_value(root_folder, "itemId", "item_id"), allowDeleteNonEmptyFolder=True)
    if delete_folder_preview.get("apply", True):
        raise RuntimeError(f"selection_sets_manage delete_folder did not default to dry-run: {delete_folder_preview}")
    delete_folder_apply = call_manage(
        "delete_folder", itemId=get_value(root_folder, "itemId", "item_id"), allowDeleteNonEmptyFolder=True, apply=True
    )
    if not delete_folder_apply.get("changed", False):
        raise RuntimeError(f"selection_sets_manage delete_folder apply=true did not report change: {delete_folder_apply}")
    if list_items():
        raise RuntimeError(f"Selection Sets lifecycle cleanup left temporary items: {list_items()}")

    return {"prefix": prefix, "createdSearchSetCount": 2, "reorderedFolderCount": get_value(reorder_apply, "reorderedFolderCount", "reordered_folder_count")}


def run_selection_properties_smoke(client, target, handles):
    """Validate bounded property reporting and temporary CSV/XLSX exports."""
    if not handles:
        raise RuntimeError("Selection properties smoke requires at least one match handle.")

    selected = client.call_tool("select_items", add_target({"matchHandles": handles[:1]}, target))
    if get_value(selected, "selectedItemCount", "selected_item_count") < 1:
        raise RuntimeError(f"select_items did not create a selection for property smoke: {selected}")

    bounded_report = client.call_tool(
        "selection_property_report",
        add_target({"itemLimit": 1, "propertyLimitPerItem": 1, "rowLimit": 1}, target),
    )
    if get_value(bounded_report, "selectedItemCount", "selected_item_count") < 1 or get_value(bounded_report, "rowCount", "row_count") < 1:
        raise RuntimeError(f"selection_property_report returned no bounded rows: {bounded_report}")

    base_path = Path(tempfile.gettempdir()) / f"navishelper-selection-properties-smoke-{int(time.time())}"
    csv_path = base_path.with_suffix(".csv")
    xlsx_path = base_path.with_suffix(".xlsx")
    try:
        for output_path, output_format in ((csv_path, "csv"), (xlsx_path, "xlsx")):
            try:
                output_path.unlink()
            except FileNotFoundError:
                pass

            dry_run = client.call_tool(
                "selection_export_properties",
                add_target(
                    {"outputPath": str(output_path), "format": output_format, "itemLimit": 1, "propertyLimitPerItem": 10, "rowLimit": 100},
                    target,
                ),
            )
            if dry_run.get("applied", True) or output_path.exists() or get_value(dry_run, "rowCount", "row_count") < 1:
                raise RuntimeError(f"selection_export_properties {output_format} dry-run was invalid: {dry_run}")

            applied = client.call_tool(
                "selection_export_properties",
                add_target(
                    {
                        "outputPath": str(output_path),
                        "format": output_format,
                        "itemLimit": 1,
                        "propertyLimitPerItem": 10,
                        "rowLimit": 100,
                        "apply": True,
                        "overwrite": True,
                    },
                    target,
                ),
            )
            if not applied.get("applied", False) or not output_path.exists() or output_path.stat().st_size < 1:
                raise RuntimeError(f"selection_export_properties {output_format} apply=true did not write an artifact: {applied}")

            if output_format == "csv":
                with output_path.open("r", encoding="utf-8-sig") as handle:
                    lines = handle.readlines()
                if len(lines) < 2 or not lines[0].startswith("Path;DisplayName;"):
                    raise RuntimeError(f"selection_export_properties csv has an invalid shape: {lines[:2]}")
            else:
                with zipfile.ZipFile(output_path) as archive:
                    if "xl/worksheets/sheet1.xml" not in archive.namelist():
                        raise RuntimeError("selection_export_properties xlsx is missing its worksheet.")
    finally:
        for output_path in (csv_path, xlsx_path):
            try:
                output_path.unlink()
            except FileNotFoundError:
                pass

    return {
        "selectedItemCount": get_value(selected, "selectedItemCount", "selected_item_count"),
        "boundedRowCount": get_value(bounded_report, "rowCount", "row_count"),
        "formats": ["csv", "xlsx"],
    }


def run_subtree_dump_failure_smoke(repo_root, client, target, root_name):
    output_path = Path(tempfile.gettempdir()) / f"navishelper-subtree-dump-conflict-{int(time.time())}.csv"
    partial_path = Path(str(output_path) + ".partial")
    jsonl_path = Path(tempfile.gettempdir()) / f"navishelper-subtree-dump-jsonl-{int(time.time())}.jsonl"
    jsonl_partial_path = Path(str(jsonl_path) + ".partial")
    for path in (output_path, partial_path, jsonl_path, jsonl_partial_path):
        try:
            path.unlink()
        except FileNotFoundError:
            pass

    conflict_args = add_target(
        {
            "rootName": root_name,
            "outputPath": str(output_path),
            "format": "csv",
            "includePath": False,
            "includeSourceFile": False,
            "includeHidden": True,
            "overwrite": True,
        },
        target,
    )

    def start_conflicting_dump():
        parallel_client = McpClient(repo_root)
        try:
            parallel_client.initialize()
            return ("started", parallel_client.call_tool("start_subtree_names_dump", conflict_args))
        except Exception as exc:
            return ("error", str(exc))
        finally:
            parallel_client.close()

    try:
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
            parallel_results = list(executor.map(lambda _: start_conflicting_dump(), range(2)))

        started_jobs = [value for kind, value in parallel_results if kind == "started"]
        errors = [value for kind, value in parallel_results if kind == "error"]
        if len(started_jobs) != 1 or len(errors) != 1:
            raise RuntimeError(f"parallel same-output dump starts did not produce one job and one rejection: {parallel_results}")
        if "already writing to this output path" not in errors[0].lower():
            raise RuntimeError(f"parallel same-output dump start returned an unexpected rejection: {parallel_results}")

        conflict_job_id = get_value(started_jobs[0], "jobId", "job_id")
        if not conflict_job_id:
            raise RuntimeError(f"parallel same-output dump start returned no jobId: {started_jobs[0]}")
        cancelled = client.call_tool("cancel_subtree_names_dump", add_target({"jobId": conflict_job_id}, target))
        if get_value(cancelled, "state", "state") != "cancelled":
            raise RuntimeError(f"parallel same-output dump job did not cancel: {cancelled}")
        if partial_path.exists():
            raise RuntimeError(f"parallel same-output dump cancellation left partial output: {partial_path}")

        before_status = client.call_tool("host_status", add_target({}, target))
        before_memory_mb = get_value(before_status, "workingSetMb", "working_set_mb")
        jsonl_started_at = time.monotonic()
        jsonl_started = client.call_tool(
            "start_subtree_names_dump",
            add_target(
                {
                    "rootName": root_name,
                    "outputPath": str(jsonl_path),
                    "format": "jsonl",
                    "includePath": False,
                    "includeSourceFile": False,
                    "includeHidden": True,
                    "overwrite": True,
                },
                target,
            ),
        )
        jsonl_job_id = get_value(jsonl_started, "jobId", "job_id")
        if not jsonl_job_id:
            raise RuntimeError(f"JSONL subtree dump returned no jobId: {jsonl_started}")

        jsonl_status = jsonl_started
        for _ in range(60):
            jsonl_status = client.call_tool(
                "dump_subtree_names_status",
                add_target({"jobId": jsonl_job_id, "maxItemsPerPoll": 500, "maxElapsedMs": 250}, target),
            )
            if get_value(jsonl_status, "state", "state") in ("done", "failed", "cancelled"):
                break

        if get_value(jsonl_status, "state", "state") != "done":
            raise RuntimeError(f"JSONL subtree dump did not complete: {jsonl_status}")
        jsonl_item_count = get_value(jsonl_status, "itemCount", "item_count") or 0
        if jsonl_item_count < 1 or not jsonl_path.exists():
            raise RuntimeError(f"JSONL subtree dump returned invalid output: {jsonl_status}")
        with jsonl_path.open("r", encoding="utf-8-sig") as handle:
            jsonl_rows = [json.loads(line) for line in handle if line.strip()]
        if len(jsonl_rows) != jsonl_item_count:
            raise RuntimeError(f"JSONL subtree dump row count mismatch: rows={len(jsonl_rows)} status={jsonl_status}")
        if any("path" in row or "sourceFile" in row for row in jsonl_rows):
            raise RuntimeError(f"JSONL subtree dump included disabled path/source fields: {jsonl_rows[:2]}")

        after_status = client.call_tool("host_status", add_target({}, target))
        after_memory_mb = get_value(after_status, "workingSetMb", "working_set_mb")
        health = client.call_tool("mcp_health_check", add_target({}, target))
        if not health.get("ok", health.get("Ok", False)):
            raise RuntimeError(f"post-JSONL subtree dump health check failed: {health}")
        return {
            "parallelConflictRejected": True,
            "jsonlItemCount": jsonl_item_count,
            "jsonlFileSizeBytes": get_value(jsonl_status, "fileSizeBytes", "file_size_bytes") or 0,
            "jsonlElapsedMs": round((time.monotonic() - jsonl_started_at) * 1000),
            "workingSetBeforeMb": before_memory_mb,
            "workingSetAfterMb": after_memory_mb,
            "workingSetDeltaMb": None if before_memory_mb is None or after_memory_mb is None else round(after_memory_mb - before_memory_mb, 1),
        }
    finally:
        for path in (output_path, partial_path, jsonl_path, jsonl_partial_path):
            try:
                path.unlink()
            except FileNotFoundError:
                pass


def run_clash_report_dry_run_smoke(client, target):
    tests = client.call_tool("clash_list_tests", add_target({"limit": 50}, target))
    total_test_count = get_value(tests, "totalTestCount", "total_test_count") or 0
    test_rows = get_value(tests, "tests", "Tests") or []
    if total_test_count < 1 or not test_rows:
        return {"state": "skipped", "reason": "no_clash_tests"}

    first_test = test_rows[0]
    test_name = get_value(first_test, "name", "Name") or ""
    if not test_name:
        raise RuntimeError(f"clash_list_tests returned a test without a name: {tests}")

    report = client.call_tool(
        "clash_generate_report",
        add_target(
            {
                "apply": False,
                "testName": test_name,
                "limit": 10,
                "captureScreenshots": False,
                "createViewpoints": False,
            },
            target,
        ),
    )
    applied = get_value(report, "applied", "Applied")
    if applied is not False:
        raise RuntimeError(f"clash_generate_report dry-run did not report applied=false: {report}")
    matched_test_count = get_value(report, "matchedTestCount", "matched_test_count") or 0
    returned_result_count = get_value(report, "returnedResultCount", "returned_result_count")
    output_directory = get_value(report, "outputDirectory", "output_directory")
    if matched_test_count < 1 or returned_result_count is None or not output_directory:
        raise RuntimeError(f"clash_generate_report dry-run returned an incomplete scope: {report}")
    return {
        "state": "passed",
        "testName": test_name,
        "matchedTestCount": matched_test_count,
        "returnedResultCount": returned_result_count,
        "outputDirectory": output_directory,
    }


def main():
    parser = argparse.ArgumentParser(description="End-to-end smoke test for NavisHelper MCP tools.")
    parser.add_argument(
        "--names",
        nargs="*",
        help="Optional exact root item names. If omitted, two .rvm/.dwg names are chosen from list_root_items.",
    )
    parser.add_argument("--skip-view", action="store_true", help="Skip select_items, zoom_to_selection, and fit_all.")
    parser.add_argument("--apply-saved-viewpoint", action="store_true", help="Apply one existing saved viewpoint when --skip-view is not set.")
    parser.add_argument("--selection-sets-lifecycle", action="store_true", help="Run temporary Search Set create/reorder/move/rename/delete checks. Use only with a disposable Navisworks process.")
    parser.add_argument("--selection-properties-smoke", action="store_true", help="Run temporary CSV/XLSX property export checks. Use only with a disposable Navisworks process.")
    parser.add_argument("--subtree-dump-failure-smoke", action="store_true", help="Run parallel output-path conflict and JSONL no-path dump checks; use only with a disposable Navisworks session.")
    parser.add_argument("--clash-report-dry-run", action="store_true", help="Validate clash_generate_report dry-run against the first available Clash Detective test.")
    parser.add_argument("--instance-id", default="", help="Explicit Navisworks host instance_id from list_navisworks_hosts.")
    parser.add_argument("--navisworks-version", default="", help="Target Navisworks version, for example 2027.")
    args = parser.parse_args()

    repo_root = repo_root_from_script()
    client = McpClient(repo_root)
    try:
        client.initialize()
        tools = client.list_tools()
        tool_names = {tool["name"] for tool in tools}
        missing = sorted(REQUIRED_TOOLS - tool_names)
        if missing:
            raise RuntimeError(f"Missing MCP tools: {missing}")

        error_contract = client.call_tool("mcp_error_contract")
        errors = error_contract.get("errors", [])
        if not errors:
            raise RuntimeError(f"mcp_error_contract returned no errors: {error_contract}")

        diagnostics = client.call_tool("mcp_diagnostics")
        hosts = {"hosts": diagnostics.get("hosts", [])}
        target = choose_target(hosts, args.instance_id, args.navisworks_version)

        status = client.call_tool("host_status", add_target({}, target))
        if not status.get("hasActiveDocument"):
            raise RuntimeError("Navisworks host has no active document.")

        health = client.call_tool("mcp_health_check", add_target({"rootItemLimit": 5}, target))
        if not health.get("ok", False):
            raise RuntimeError(f"mcp_health_check returned degraded status: {health}")

        active_context = client.call_tool(
            "active_model_context",
            add_target({"rootItemLimit": 5, "includeSavedItemsSummary": True}, target),
        )
        context_status = active_context.get("hostStatus", active_context.get("host_status", {}))
        context_root_items = active_context.get("rootItems", active_context.get("root_items", {}))
        if not context_status.get("hasActiveDocument", context_status.get("has_active_document", False)):
            raise RuntimeError(f"active_model_context reported no active document: {active_context}")
        if context_root_items.get("rootItemCount", context_root_items.get("root_item_count", 0)) < 1:
            raise RuntimeError(f"active_model_context returned no root item count: {active_context}")
        if not active_context.get("searchGuidance", active_context.get("search_guidance", [])):
            raise RuntimeError(f"active_model_context returned no search guidance: {active_context}")

        root_items = client.call_tool("list_root_items", add_target({"limit": 1000, "includeAliases": False}, target))
        spatial_search = client.call_tool(
            "find_items_by_bbox",
            add_target(
                {
                    "min": {"x": -1.0e15, "y": -1.0e15, "z": -1.0e15},
                    "max": {"x": 1.0e15, "y": 1.0e15, "z": 1.0e15},
                    "matchMode": "contains",
                    "includeContainers": True,
                    "maxScannedItems": 100,
                    "maxResults": 10,
                    "previewLimit": 1,
                },
                target,
            ),
        )
        if spatial_search.get("returnedItemCount", spatial_search.get("returned_item_count", 0)) < 1:
            raise RuntimeError(f"find_items_by_bbox returned no broad-zone matches: {spatial_search}")
        if not spatial_search.get("matchHandle", spatial_search.get("match_handle", "")):
            raise RuntimeError(f"find_items_by_bbox returned matches without a match handle: {spatial_search}")
        names = choose_names(root_items, args.names)
        find_response = client.call_tool(
            "find_root_items_by_name",
            add_target({"names": names, "comparison": "equals", "previewLimit": 2}, target),
        )
        summary = find_response.get("summary", {})
        matched = summary.get("matchedQueries", summary.get("matched_queries", 0))
        if matched != len(names):
            raise RuntimeError(f"Expected {len(names)} matched queries, got {matched}. Names={names}")

        handles = extract_handles(find_response)
        if not handles:
            raise RuntimeError("find_root_items_by_name returned no match handles.")

        subtree_dump_smoke = run_subtree_dump_smoke(client, target, names[0])
        selection_sets_lifecycle_smoke = None
        if args.selection_sets_lifecycle:
            selection_sets_lifecycle_smoke = run_selection_sets_lifecycle_smoke(client, target, names[0])
        selection_properties_smoke = None
        if args.selection_properties_smoke:
            selection_properties_smoke = run_selection_properties_smoke(client, target, handles)
        subtree_dump_failure_smoke = None
        if args.subtree_dump_failure_smoke:
            subtree_dump_failure_smoke = run_subtree_dump_failure_smoke(repo_root, client, target, names[0])
        clash_report_dry_run = None
        if args.clash_report_dry_run:
            clash_report_dry_run = run_clash_report_dry_run_smoke(client, target)

        properties = client.call_tool(
            "item_properties_by_handle",
            add_target(
                {
                    "matchHandles": handles[:1],
                    "itemLimit": 1,
                    "propertyLimit": 20,
                    "includeInternalNames": False,
                    "categoryFilters": ["Item", "Элемент"],
                },
                target,
            ),
        )
        property_results = properties.get("results", [])
        if not property_results or property_results[0].get("returnedItemCount", property_results[0].get("returned_item_count", 0)) < 1:
            raise RuntimeError(f"item_properties_by_handle returned no item previews: {properties}")

        viewpoint = client.call_tool("current_viewpoint_info", add_target({}, target))
        if not viewpoint.get("hasCurrentViewpoint", viewpoint.get("has_current_viewpoint", False)):
            raise RuntimeError(f"current_viewpoint_info reported no current viewpoint: {viewpoint}")

        saved_viewpoints = client.call_tool("list_saved_viewpoints", add_target({"limit": 20}, target))
        if saved_viewpoints.get("returnedItemCount", saved_viewpoints.get("returned_item_count", 0)) < 0:
            raise RuntimeError(f"list_saved_viewpoints returned invalid count: {saved_viewpoints}")

        selection_sets = client.call_tool("list_selection_sets", add_target({"limit": 20}, target))
        if selection_sets.get("returnedItemCount", selection_sets.get("returned_item_count", 0)) < 0:
            raise RuntimeError(f"list_selection_sets returned invalid count: {selection_sets}")

        selection_set_preview_existing = None
        selection_set_items = [
            item for item in selection_sets.get("items", [])
            if get_value(item, "type", "type") == "SelectionSet"
        ]
        if selection_set_items:
            selection_set_preview_existing = client.call_tool(
                "select_selection_set",
                add_target({"pathOrName": get_value(selection_set_items[0], "path", "path")}, target),
            )
            if selection_set_preview_existing.get("apply", selection_set_preview_existing.get("Apply", True)):
                raise RuntimeError(f"select_selection_set without apply did not default to dry-run: {selection_set_preview_existing}")
            if selection_set_preview_existing.get("selected", selection_set_preview_existing.get("Selected")) is not None:
                raise RuntimeError(f"select_selection_set dry-run reported selection: {selection_set_preview_existing}")

        viewpoint_activation_preview = None
        saved_viewpoint_items = [
            item for item in saved_viewpoints.get("items", [])
            if get_value(item, "type", "type") == "SavedViewpoint"
        ]
        if saved_viewpoint_items:
            viewpoint_activation_preview = client.call_tool(
                "activate_saved_viewpoint",
                add_target({"pathOrName": get_value(saved_viewpoint_items[0], "path", "path")}, target),
            )
            if viewpoint_activation_preview.get("apply", viewpoint_activation_preview.get("Apply", True)):
                raise RuntimeError(f"activate_saved_viewpoint without apply did not default to dry-run: {viewpoint_activation_preview}")
            if viewpoint_activation_preview.get("activated", viewpoint_activation_preview.get("Activated")) is not None:
                raise RuntimeError(f"activate_saved_viewpoint dry-run reported activation: {viewpoint_activation_preview}")

        selected = None
        selection_set_apply_existing = None
        viewpoint_activation_apply = None
        selection_set_preview = None
        viewpoint_preview = None
        show_all_preview = None
        hide_unselected_preview = None
        selection_ancestry = None
        selection_tree = None
        large_selection_tree = None
        zoomed = None
        fitted = None
        if not args.skip_view:
            if args.apply_saved_viewpoint and saved_viewpoint_items:
                viewpoint_activation_apply = client.call_tool(
                    "activate_saved_viewpoint",
                    add_target({"pathOrName": get_value(saved_viewpoint_items[0], "path", "path"), "apply": True}, target),
                )
                if not viewpoint_activation_apply.get("activated", viewpoint_activation_apply.get("Activated", False)):
                    raise RuntimeError(f"activate_saved_viewpoint apply=true did not report activation: {viewpoint_activation_apply}")
                viewpoint_after_apply = client.call_tool("current_viewpoint_info", add_target({}, target))
                if not viewpoint_after_apply.get("hasCurrentViewpoint", viewpoint_after_apply.get("has_current_viewpoint", False)):
                    raise RuntimeError(f"current_viewpoint_info failed after saved viewpoint activation: {viewpoint_after_apply}")

            if selection_set_items:
                selection_set_apply_existing = client.call_tool(
                    "select_selection_set",
                    add_target({"pathOrName": get_value(selection_set_items[0], "path", "path"), "apply": True}, target),
                )
                if not selection_set_apply_existing.get("selected", selection_set_apply_existing.get("Selected", False)):
                    raise RuntimeError(f"select_selection_set apply=true did not report selection: {selection_set_apply_existing}")
                selection_status_after_set = client.call_tool("selection_status", add_target({"includeBoundingBox": False}, target))
                expected_set_count = selection_set_apply_existing.get(
                    "selectedItemCount",
                    selection_set_apply_existing.get("selected_item_count", 0),
                )
                actual_set_count = selection_status_after_set.get(
                    "selectedItemCount",
                    selection_status_after_set.get("selected_item_count", 0),
                )
                if actual_set_count != expected_set_count:
                    raise RuntimeError(
                        "selection_status did not match select_selection_set apply result: "
                        f"expected={expected_set_count} actual={actual_set_count}"
                    )

            selected = client.call_tool("select_items", add_target({"matchHandles": handles}, target))
            if selected.get("selectedItemCount", selected.get("selected_item_count", 0)) < len(handles):
                raise RuntimeError(f"select_items selected fewer items than expected: {selected}")

            selection_status = client.call_tool("selection_status", add_target({"includeBoundingBox": True}, target))
            if selection_status.get("selectedItemCount", selection_status.get("selected_item_count", 0)) < len(handles):
                raise RuntimeError(f"selection_status reported fewer selected items than expected: {selection_status}")

            selection_preview = client.call_tool(
                "selected_items_preview",
                add_target({"limit": 5, "includeBoundingBoxes": False}, target),
            )
            preview_items = selection_preview.get("items", [])
            if len(preview_items) < min(len(handles), 5):
                raise RuntimeError(f"selected_items_preview returned fewer items than expected: {selection_preview}")

            selection_ancestry = client.call_tool(
                "selected_items_ancestry",
                add_target({"limit": 5, "includeBoundingBoxes": False}, target),
            )
            ancestry_items = selection_ancestry.get("items", [])
            if len(ancestry_items) < min(len(handles), 5):
                raise RuntimeError(f"selected_items_ancestry returned fewer items than expected: {selection_ancestry}")
            for item in ancestry_items:
                chain = get_value(item, "chain", "chain") or []
                selected_node = get_value(item, "item", "item") or {}
                if not chain:
                    raise RuntimeError(f"selected_items_ancestry returned an empty chain: {selection_ancestry}")
                if not get_value(chain[-1], "isSelectedItem", "is_selected_item"):
                    raise RuntimeError(f"selected_items_ancestry chain does not end with the selected item: {selection_ancestry}")
                if get_value(chain[-1], "path", "path") != get_value(selected_node, "path", "path"):
                    raise RuntimeError(f"selected_items_ancestry selected item does not match the chain tail: {selection_ancestry}")

            selection_tree = client.call_tool(
                "selected_items_tree",
                add_target({"maxItems": 10000, "format": "tree", "includeBoundingBoxes": False}, target),
            )
            tree_returned_count = selection_tree.get("returnedItemCount", selection_tree.get("returned_item_count", 0))
            if tree_returned_count < len(handles):
                raise RuntimeError(f"selected_items_tree returned fewer selected items than expected: {selection_tree}")
            if not selection_tree.get("roots", []):
                raise RuntimeError(f"selected_items_tree returned no roots for non-empty selection: {selection_tree}")

            hide_unselected_preview = client.call_tool(
                "hide_unselected",
                add_target({"previewLimit": 3}, target),
            )
            if hide_unselected_preview.get("apply", hide_unselected_preview.get("Apply", True)):
                raise RuntimeError(f"hide_unselected without apply did not default to dry-run: {hide_unselected_preview}")
            would_hide = hide_unselected_preview.get("wouldHideItemCount", hide_unselected_preview.get("would_hide_item_count", 0))
            affected_preview = hide_unselected_preview.get("affectedItemsPreview", hide_unselected_preview.get("affected_items_preview", []))
            affected_root_summaries = hide_unselected_preview.get("affectedRootSummaries", hide_unselected_preview.get("affected_root_summaries", []))
            affected_root_count = hide_unselected_preview.get("affectedRootCount", hide_unselected_preview.get("affected_root_count", 0))
            if would_hide > 0 and not affected_preview:
                raise RuntimeError(f"hide_unselected dry-run returned no affected item preview: {hide_unselected_preview}")
            if would_hide > 0 and (affected_root_count < 1 or not affected_root_summaries):
                raise RuntimeError(f"hide_unselected dry-run returned no affected root summary: {hide_unselected_preview}")
            if len(affected_preview) > 3:
                raise RuntimeError(f"hide_unselected affected preview exceeded requested limit: {hide_unselected_preview}")
            if len(affected_root_summaries) > 20:
                raise RuntimeError(f"hide_unselected root summary exceeded the fixed safety limit: {hide_unselected_preview}")
            for summary in affected_root_summaries:
                if "affectedItemCount" not in summary and "affected_item_count" not in summary:
                    raise RuntimeError(f"hide_unselected root summary omitted affected item count: {hide_unselected_preview}")

            preview_name = "MCP smoke dry run " + str(int(time.time()))
            selection_set_preview = client.call_tool(
                "create_selection_set",
                add_target({"name": preview_name}, target),
            )
            if selection_set_preview.get("apply", selection_set_preview.get("Apply", True)):
                raise RuntimeError(f"create_selection_set without apply did not default to dry-run: {selection_set_preview}")
            if selection_set_preview.get("created", selection_set_preview.get("Created")) is not None:
                raise RuntimeError(f"create_selection_set dry-run reported creation: {selection_set_preview}")

            zoomed = client.call_tool("zoom_to_selection", add_target({}, target))
            if not zoomed.get("zoomApplied", zoomed.get("zoom_applied", False)):
                raise RuntimeError(f"zoom_to_selection did not apply: {zoomed}")

            fitted = client.call_tool("fit_all", add_target({}, target))
            if not fitted.get("fitApplied", fitted.get("fit_applied", False)):
                raise RuntimeError(f"fit_all did not apply: {fitted}")

            viewpoint_preview = client.call_tool(
                "create_viewpoint",
                add_target({"name": preview_name, "folderPath": "MCP Smoke Dry Run"}, target),
            )
            if viewpoint_preview.get("apply", viewpoint_preview.get("Apply", True)):
                raise RuntimeError(f"create_viewpoint without apply did not default to dry-run: {viewpoint_preview}")
            if viewpoint_preview.get("created", viewpoint_preview.get("Created")) is not None:
                raise RuntimeError(f"create_viewpoint dry-run reported creation: {viewpoint_preview}")

            show_all_preview = client.call_tool("show_all", add_target({}, target))
            if show_all_preview.get("apply", show_all_preview.get("Apply", True)):
                raise RuntimeError(f"show_all without apply did not default to dry-run: {show_all_preview}")
            if show_all_preview.get("revealedItemCount", show_all_preview.get("revealed_item_count")) is not None:
                raise RuntimeError(f"show_all dry-run reported applied reveal: {show_all_preview}")

            many_names = choose_many_names(root_items, 120)
            root_total_count = root_items.get("rootItemCount", root_items.get("root_item_count", 0))
            if root_total_count > 100 and len(many_names) <= 100:
                raise RuntimeError(
                    "list_root_items reported more than 100 root items, but smoke could not collect "
                    f"more than 100 selectable root names: collected={len(many_names)}"
                )
            if len(many_names) > 100:
                large_query = common_prefix(many_names)
                if len(large_query) < 2:
                    large_query = many_names[0][:3]
                if len(large_query) < 2:
                    raise RuntimeError(f"Could not derive a shared root-name query for >100 selected_items_tree smoke: {many_names[:5]}")

                many_find_response = client.call_tool(
                    "find_root_items_by_name",
                    add_target({"names": [large_query], "comparison": "contains", "previewLimit": 1}, target),
                )
                many_handles = extract_handles(many_find_response)
                total_large_matches = (many_find_response.get("summary", {}) or {}).get(
                    "totalItemsInMatches",
                    (many_find_response.get("summary", {}) or {}).get("total_items_in_matches", 0),
                )
                if len(many_handles) != 1 or total_large_matches <= 100:
                    raise RuntimeError(
                        "find_root_items_by_name did not return one broad handle with more than 100 items "
                        f"for selected_items_tree smoke: query={large_query} response={many_find_response}"
                    )

                selected_many = client.call_tool("select_items", add_target({"matchHandles": many_handles}, target))
                selected_many_count = selected_many.get("selectedItemCount", selected_many.get("selected_item_count", 0))
                if selected_many_count <= 100:
                    raise RuntimeError(f"select_items did not create a >100 selection for selected_items_tree smoke: {selected_many}")

                large_selection_tree = client.call_tool(
                    "selected_items_tree",
                    add_target({"maxItems": 10000, "format": "flat", "includeBoundingBoxes": False}, target),
                )
                returned_many_count = large_selection_tree.get(
                    "returnedItemCount",
                    large_selection_tree.get("returned_item_count", 0),
                )
                flat_items = large_selection_tree.get("items", [])
                if returned_many_count <= 100 or len(flat_items) <= 100:
                    raise RuntimeError(
                        "selected_items_tree did not return more than 100 flat items "
                        f"for a >100 selection: {large_selection_tree}"
                    )

                limited_selection_tree = client.call_tool(
                    "selected_items_tree",
                    add_target({"maxItems": 100, "format": "flat", "includeBoundingBoxes": False}, target),
                )
                if not limited_selection_tree.get("truncated", False):
                    raise RuntimeError(f"selected_items_tree maxItems=100 did not report truncation: {limited_selection_tree}")
                limited_returned_count = limited_selection_tree.get(
                    "returnedItemCount",
                    limited_selection_tree.get("returned_item_count", 0),
                )
                if limited_returned_count != 100:
                    raise RuntimeError(
                        "selected_items_tree maxItems=100 returned an unexpected item count: "
                        f"{limited_selection_tree}"
                    )

        recent_calls = client.call_tool("mcp_recent_calls", {"lineCount": 20})
        recent_count = recent_calls.get("returnedLineCount", recent_calls.get("returned_line_count", 0))
        if recent_count < 1:
            raise RuntimeError(f"mcp_recent_calls returned no log lines: {recent_calls}")

        output = {
            "ok": True,
            "document": status.get("documentTitle"),
            "rootItemCount": status.get("rootItemCount"),
            "hostCount": len(hosts.get("hosts", [])),
            "logFilePath": get_value(diagnostics, "logFilePath", "log_file_path"),
            "target": target,
            "toolCount": len(tool_names),
            "errorContractCount": len(errors),
            "recentCallCount": recent_count,
            "healthVerdict": health.get("verdict"),
            "activeModelContextRootPreviewCount": len(context_root_items.get("items", [])),
            "names": names,
            "matchedQueries": matched,
            "matchHandles": len(handles),
            "subtreeDumpSmoke": subtree_dump_smoke,
            "selectionSetsLifecycleSmoke": selection_sets_lifecycle_smoke,
            "selectionPropertiesSmoke": selection_properties_smoke,
            "subtreeDumpFailureSmoke": subtree_dump_failure_smoke,
            "clashReportDryRun": clash_report_dry_run,
            "propertyHandleResults": len(property_results),
            "savedViewpointCount": saved_viewpoints.get("returnedItemCount"),
            "selectionSetCount": selection_sets.get("returnedItemCount"),
            "selectSelectionSetDryRun": None if selection_set_preview_existing is None else not selection_set_preview_existing.get("apply", True),
            "selectSelectionSetApplyCount": None if selection_set_apply_existing is None else selection_set_apply_existing.get("selectedItemCount"),
            "activateSavedViewpointDryRun": None if viewpoint_activation_preview is None else not viewpoint_activation_preview.get("apply", True),
            "activateSavedViewpointApply": None if viewpoint_activation_apply is None else viewpoint_activation_apply.get("activated"),
            "selectedItemCount": None if selected is None else selected.get("selectedItemCount"),
            "selectionStatusCount": None if selected is None else selection_status.get("selectedItemCount"),
            "selectionPreviewCount": None if selected is None else len(selection_preview.get("items", [])),
            "selectionAncestryCount": None if selected is None else len(selection_ancestry.get("items", [])),
            "selectionTreeReturnedCount": None if selection_tree is None else selection_tree.get("returnedItemCount"),
            "largeSelectionTreeReturnedCount": None if large_selection_tree is None else large_selection_tree.get("returnedItemCount"),
            "selectionSetDryRun": None if selection_set_preview is None else not selection_set_preview.get("apply", True),
            "viewpointDryRun": None if viewpoint_preview is None else not viewpoint_preview.get("apply", True),
            "showAllDryRun": None if show_all_preview is None else not show_all_preview.get("apply", True),
            "hideUnselectedDryRunPreviewCount": None if hide_unselected_preview is None else len(hide_unselected_preview.get("affectedItemsPreview", [])),
            "hideUnselectedDryRunRootSummaryCount": None if hide_unselected_preview is None else len(hide_unselected_preview.get("affectedRootSummaries", [])),
            "zoomApplied": None if zoomed is None else zoomed.get("zoomApplied"),
            "fitApplied": None if fitted is None else fitted.get("fitApplied"),
        }
        print(json.dumps(output, ensure_ascii=False, indent=2))
        return 0
    finally:
        client.close()


if __name__ == "__main__":
    sys.exit(main())
