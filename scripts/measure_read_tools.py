#!/usr/bin/env python3
"""Re-run the read-only latency table of docs/MCP_TOOL_BASELINE.md on a live Navisworks.

"The table" (39 rows) was measured once, on 2026-09-20, by one-off harnesses that
were never committed. This script makes it re-runnable:

* ``--plan`` prints the planned calls as JSON -- one entry per table row, in the
  table's order, each ``{"group", "tool", "case", "arguments"}``. It needs no
  Navisworks and no MCP server. Argument values that exist only at run time are
  placeholders written as strings starting with ``$`` (for example ``$ROOT_HANDLE``
  or ``$ZONE_MIN``); every tool with an ``apply`` parameter passes ``apply: false``.
* Without ``--plan`` the script starts Navisworks on ``--model`` with
  ``start_navisworks`` (``--navisworks-version``), records the host's plugin
  identity (``pluginAssemblyLength`` / ``pluginAssemblyLastWriteUtc``), resolves
  the run-time placeholders, calls every planned row twice, writes one JSON line
  per call to ``--out`` and prints a Markdown table with the doc's columns:
  group, tool (with case), status, first (host ms), warm (host ms), warm (wall ms).

Placeholder resolution, in order:

* ``$ROOT_NAME``   -- display name of the first ``list_root_items`` item;
* ``$ROOT_HANDLE`` -- the match handle for that root from ``find_root_items_by_name``;
* ``$PATH_2`` / ``$PATH_7`` -- model-tree paths two and seven levels deep, found by
  descending with ``list_item_children`` from that root (fewer levels if the tree
  is shallower; the run says so and uses the deepest path it reached);
* ``$CHILD_NAME``  -- display name of the item at ``$PATH_2`` (the select_by_search row
  selects that item under the root);
* ``$ZONE_MIN`` / ``$ZONE_MAX`` -- a 200-unit cube centred on the root's bounding box,
  read from ``selection_status`` with the root selected and ``includeBoundingBox``.

Selection order follows the table: the zone resolution selects the root and the
selection is cleared again before the measured rows, so the
``selection_status (empty selection)`` row really sees an empty selection; the
measured ``select_items`` row then establishes the table's "one root item
selected" invariant for the selection rows that follow.

The run refuses to start while a ``Roamer.exe`` process is already running, and
closes Navisworks at the end with ``close_navisworks`` ``mode=discard`` (the
document is never saved). Standard library only, plus ``McpClient`` from
``scripts/navavishelper_mcp_smoke.py`` (this checkout's built MCP server over stdio).
"""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from navishelper_mcp_smoke import McpClient  # noqa: E402

DEFAULT_MODEL = r"D:\Downloads\6501.5.nwd"
DEFAULT_NAVISWORKS_VERSION = "2027"
DEFAULT_OUT = "read_tools_latency.jsonl"

# The zone row of the table: "200-unit cube" around the root's bounding-box centre.
ZONE_HALF_EXTENT = 100.0
# The deepest path the table drives (its "7-level path" row); the root itself is level 1.
PATH_7_STEPS = 6
# A name that cannot exist as an item display name; select_by_search with
# replaceSelection=true and zero matches clears the selection (CopyFrom of an
# empty collection). There is no dedicated clear-selection tool.
NO_SUCH_ITEM_NAME = "NAVISHELPER-T34-NO-SUCH-ITEM-NAME-c1a97f2e"
# One tool call may legitimately take tens of seconds (a launch, a capped spatial
# scan); the per-request timeout is well above the server's own 10-second budget.
CALL_TIMEOUT_SECONDS = 300

# One entry per row of "The table" in docs/MCP_TOOL_BASELINE.md, in the table's
# order. "case" is the row's text after the tool name, empty when there is none.
# Placeholders are strings starting with "$" and are resolved at run time.
PLAN = [
    {"group": "diagnostics", "tool": "host_status", "case": "", "arguments": {}},
    {"group": "diagnostics", "tool": "mcp_health_check", "case": "", "arguments": {"rootItemLimit": 10}},
    {"group": "diagnostics", "tool": "mcp_diagnostics", "case": "", "arguments": {}},
    {"group": "diagnostics", "tool": "mcp_error_contract", "case": "", "arguments": {}},
    {"group": "diagnostics", "tool": "mcp_recent_calls", "case": "", "arguments": {}},
    {"group": "diagnostics", "tool": "list_navisworks_hosts", "case": "", "arguments": {}},
    {"group": "diagnostics", "tool": "list_recent_navisworks_files", "case": "", "arguments": {}},
    {"group": "query", "tool": "active_model_context", "case": "", "arguments": {"rootItemLimit": 100, "includeSavedItemsSummary": True}},
    {"group": "query", "tool": "list_root_items", "case": "", "arguments": {"limit": 1000, "includeAliases": False}},
    {"group": "query", "tool": "find_root_items_by_name", "case": "", "arguments": {"names": ["$ROOT_NAME"], "comparison": "equals", "previewLimit": 10}},
    {"group": "query", "tool": "list_item_children", "case": "(2-level path)", "arguments": {"parentPath": "$PATH_2", "limit": 200}},
    {"group": "query", "tool": "list_item_children", "case": "(7-level path)", "arguments": {"parentPath": "$PATH_7", "limit": 200}},
    {"group": "query", "tool": "find_items", "case": "whole_model, all, countOnly", "arguments": {"query": "$ROOT_NAME", "comparison": "equals", "matchDepth": "all", "countOnly": True}},
    {"group": "query", "tool": "find_items", "case": "scoped, all, countOnly", "arguments": {"query": "$ROOT_NAME", "comparison": "equals", "scope": "under_handle", "scopeHandle": "$ROOT_HANDLE", "matchDepth": "all", "countOnly": True}},
    {"group": "query", "tool": "find_items", "case": "scoped, first", "arguments": {"query": "$ROOT_NAME", "comparison": "equals", "scope": "under_handle", "scopeHandle": "$ROOT_HANDLE", "matchDepth": "first", "previewLimit": 10}},
    {"group": "query", "tool": "find_items_by_bbox", "case": "200³ zone, 100k scan cap", "arguments": {"min": "$ZONE_MIN", "max": "$ZONE_MAX", "maxScannedItems": 100000}},
    {"group": "selection", "tool": "selection_status", "case": "(empty selection)", "arguments": {"includeBoundingBox": True}},
    {"group": "selection", "tool": "select_items", "case": "", "arguments": {"matchHandles": ["$ROOT_HANDLE"]}},
    {"group": "selection", "tool": "selection_status", "case": "(1 item, with bbox)", "arguments": {"includeBoundingBox": True}},
    {"group": "selection", "tool": "selected_items_preview", "case": "", "arguments": {"limit": 20, "includeBoundingBoxes": False}},
    {"group": "selection", "tool": "selected_items_tree", "case": "", "arguments": {"maxItems": 10000, "format": "tree", "includeBoundingBoxes": False}},
    {"group": "selection", "tool": "selected_items_ancestry", "case": "", "arguments": {"limit": 20, "includeBoundingBoxes": False}},
    {"group": "selection", "tool": "selection_copy_names", "case": "", "arguments": {"limit": 10000, "includePaths": False, "includeSourceFiles": False}},
    {"group": "selection", "tool": "select_by_search", "case": "descendants_of", "arguments": {"conditions": [{"category": "Item", "property": "Name", "operator": "equals", "value": "$CHILD_NAME"}], "scope": "descendants_of", "parentConditions": [{"category": "Item", "property": "Name", "operator": "equals", "value": "$ROOT_NAME"}], "replaceSelection": True}},
    {"group": "reports", "tool": "selection_distinct_property_values", "case": "", "arguments": {"itemLimit": 100, "valueLimit": 1000}},
    {"group": "reports", "tool": "selection_property_report", "case": "", "arguments": {"itemLimit": 100, "propertyLimitPerItem": 1000, "rowLimit": 10000}},
    {"group": "properties", "tool": "item_properties_by_handle", "case": "", "arguments": {"matchHandles": ["$ROOT_HANDLE"], "itemLimit": 5, "propertyLimit": 50}},
    {"group": "view", "tool": "current_viewpoint_info", "case": "", "arguments": {}},
    {"group": "view", "tool": "list_saved_viewpoints", "case": "", "arguments": {"limit": 200}},
    {"group": "view", "tool": "zoom_to_selection", "case": "", "arguments": {}},
    {"group": "view", "tool": "focus_on_selection", "case": "", "arguments": {}},
    {"group": "view", "tool": "fit_all", "case": "", "arguments": {}},
    {"group": "sections", "tool": "get_current_section_box", "case": "", "arguments": {}},
    {"group": "sets", "tool": "list_selection_sets", "case": "", "arguments": {"limit": 200}},
    {"group": "scenarios", "tool": "list_scenarios", "case": "", "arguments": {}},
    {"group": "scenarios", "tool": "scenario_capabilities", "case": "", "arguments": {}},
    {"group": "clash", "tool": "clash_bbox_pair_plan", "case": "sourceMode=selection", "arguments": {"sourceMode": "selection", "apply": False}},
    {"group": "visibility", "tool": "hide_selected", "case": "apply=false", "arguments": {"apply": False}},
    {"group": "visibility", "tool": "show_all", "case": "", "arguments": {"apply": False}},
]


def pick(payload, *names):
    """First non-None value among the given keys, tolerating casing drift."""
    if not isinstance(payload, dict):
        return None
    for name in names:
        if payload.get(name) is not None:
            return payload[name]
    return None


def utc_now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds")


def roamer_running():
    """True/False on Windows, None when the check cannot run."""
    if sys.platform != "win32":
        return None
    try:
        result = subprocess.run(
            ["tasklist", "/FI", "IMAGENAME eq Roamer.exe", "/FO", "CSV", "/NH"],
            capture_output=True, text=True, errors="replace",
        )
    except OSError:
        return None
    if result.returncode != 0:
        return None
    return "roamer.exe" in result.stdout.lower()


def timed_call(client, tool, arguments, timeout=CALL_TIMEOUT_SECONDS):
    """One tools/call with the timing envelope and the client wall clock recorded.

    Uses the raw request rather than McpClient.call_tool so a failing call still
    yields its navishelper_timing (call_tool raises before the caller can read it).
    """
    started_at = utc_now_iso()
    started = time.perf_counter()
    try:
        response = client.request(
            "tools/call", {"name": tool, "arguments": arguments}, timeout=timeout)
    except Exception as exc:  # transport failure, timeout, or server exit
        wall_ms = round((time.perf_counter() - started) * 1000, 1)
        return {
            "status": "client_error", "elapsedMs": None, "wallMs": wall_ms,
            "toolOk": None, "errorCode": None, "message": f"{type(exc).__name__}: {exc}",
            "startedAtUtc": started_at,
        }
    wall_ms = round((time.perf_counter() - started) * 1000, 1)

    result = response.get("result") or {}
    timing = None
    meta = result.get("_meta") or result.get("meta") or {}
    if isinstance(meta, dict):
        timing = meta.get("navishelper_timing")
    payload = None
    content = result.get("content") or []
    if content and isinstance(content[0], dict):
        try:
            payload = json.loads(content[0].get("text", ""))
        except (ValueError, TypeError):
            payload = None
    if timing is None and isinstance(payload, dict):
        timing = payload.get("navishelper_timing")
    timing = timing if isinstance(timing, dict) else {}

    message = None
    if result.get("isError"):
        message = (content[0].get("text", "") if content and isinstance(content[0], dict) else "")[:500]
    return {
        "status": timing.get("status") or ("error" if result.get("isError") else "unknown"),
        "elapsedMs": timing.get("elapsed_ms"),
        "wallMs": wall_ms,
        "toolOk": timing.get("tool_ok"),
        "errorCode": timing.get("tool_error_code"),
        "message": message,
        "startedAtUtc": started_at,
    }


def resolve_arguments(value, values):
    """Replace "$PLACEHOLDER" strings anywhere in the argument structure."""
    if isinstance(value, str):
        if value.startswith("$"):
            key = value[1:]
            if key not in values:
                raise RuntimeError(f"Unknown placeholder ${key} in the plan.")
            return values[key]
        return value
    if isinstance(value, list):
        return [resolve_arguments(item, values) for item in value]
    if isinstance(value, dict):
        return {key: resolve_arguments(item, values) for key, item in value.items()}
    return value


def descend_paths(client, root_path, notes):
    """Paths below the root, one list_item_children descent per level."""
    paths = {}
    names = {}
    current = root_path
    for step in range(1, PATH_7_STEPS + 1):
        children = client.call_tool(
            "list_item_children", {"parentPath": current, "limit": 200})
        items = pick(children, "children", "Children") or []
        if not items:
            break
        first = items[0]
        current = pick(first, "path", "Path")
        if not current:
            raise RuntimeError(f"list_item_children returned a child without a path: {first}")
        paths[step + 1] = current  # the root itself is level 1
        names[step + 1] = pick(first, "displayName", "DisplayName", "display_name")
    deepest = max(paths) if paths else 1
    if 7 not in paths:
        notes.append(
            f"the model tree under the first root is only {deepest} level(s) deep; "
            f"$PATH_7 fell back to the depth-{deepest} path")
    return paths, names


def resolve_placeholders(client, notes):
    """Resolve every run-time placeholder the plan refers to."""
    values = {}

    roots = client.call_tool("list_root_items", {"limit": 1000, "includeAliases": False})
    items = pick(roots, "items", "Items") or []
    if not items:
        raise RuntimeError("list_root_items returned no items; nothing to measure against.")
    root = items[0]
    root_name = pick(root, "displayName", "DisplayName", "display_name") \
        or pick(root, "fileName", "FileName", "file_name") \
        or pick(root, "path", "Path")
    if not root_name:
        raise RuntimeError(f"the first root item carries no usable name: {root}")
    values["ROOT_NAME"] = root_name

    found = client.call_tool(
        "find_root_items_by_name",
        {"names": [root_name], "comparison": "equals", "previewLimit": 10})
    results = pick(found, "results", "Results") or []
    matches = results[0].get("matches", []) if results and isinstance(results[0], dict) else []
    root_handle = matches[0].get("matchHandle") if matches and isinstance(matches[0], dict) else None
    if not root_handle:
        raise RuntimeError(
            f"find_root_items_by_name returned no match handle for {root_name!r}: {found}")
    root_handle_items = matches[0].get("itemCount", matches[0].get("item_count"))
    if root_handle_items != 1:
        notes.append(
            f"the handle for {root_name!r} holds {root_handle_items} items; "
            "the '1 item' selection rows will measure that many")
    values["ROOT_HANDLE"] = root_handle

    paths, names = descend_paths(client, pick(root, "path", "Path") or root_name, notes)
    if 2 not in paths:
        raise RuntimeError(
            "the first root item has no children, so no 2-level path exists; "
            "the table cannot be re-run on this model as written")
    values["PATH_2"] = paths[2]
    values["PATH_7"] = paths.get(7, paths[max(paths)])
    if 2 not in names or not names[2]:
        raise RuntimeError(f"the item at {paths[2]} has no display name for $CHILD_NAME.")
    values["CHILD_NAME"] = names[2]

    # The zone: 200-unit cube centred on the root's bounding box. Reading it
    # needs the root selected; the selection is cleared again right after.
    selected = client.call_tool("select_items", {"matchHandles": [root_handle]})
    selected_count = pick(selected, "selectedItemCount", "selected_item_count")
    if not selected_count:
        raise RuntimeError(f"select_items did not select the root: {selected}")
    status = client.call_tool("selection_status", {"includeBoundingBox": True})
    box = pick(status, "boundingBox", "bounding_box", "BoundingBox") or {}
    center = pick(box, "center", "Center") or {}
    cx, cy, cz = (pick(center, "x", "X"), pick(center, "y", "Y"), pick(center, "z", "Z"))
    if cx is None or cy is None or cz is None:
        raise RuntimeError(f"selection_status returned no bounding-box centre: {status}")
    values["ZONE_MIN"] = {"x": cx - ZONE_HALF_EXTENT, "y": cy - ZONE_HALF_EXTENT, "z": cz - ZONE_HALF_EXTENT}
    values["ZONE_MAX"] = {"x": cx + ZONE_HALF_EXTENT, "y": cy + ZONE_HALF_EXTENT, "z": cz + ZONE_HALF_EXTENT}

    # No dedicated clear-selection tool exists; a whole-model search that matches
    # nothing replaces the selection with an empty collection.
    client.call_tool("select_by_search", {
        "conditions": [{"category": "Item", "property": "Name", "operator": "equals", "value": NO_SUCH_ITEM_NAME}],
        "scope": "whole_model",
        "replaceSelection": True,
    })
    cleared = client.call_tool("selection_status", {"includeBoundingBox": False})
    cleared_count = pick(cleared, "selectedItemCount", "selected_item_count")
    if cleared_count:
        raise RuntimeError(
            f"clearing the selection left {cleared_count} item(s) selected; "
            "the (empty selection) row would not be honest")

    return values


def row_status(calls):
    """The doc's status column, without the refusal-reads-as-success trap."""
    statuses = []
    for call in calls:
        if call["status"] == "ok" and call["toolOk"] is False and call["errorCode"]:
            statuses.append(f"refused:{call['errorCode']}")
        else:
            statuses.append(str(call["status"]))
    distinct = list(dict.fromkeys(statuses))
    return "+".join(distinct)


def print_markdown_table(rows):
    header = ("group", "tool", "status", "first (host ms)", "warm (host ms)", "warm (wall ms)")
    print()
    print("| " + " | ".join(header) + " |")
    print("| " + " | ".join("---" for _ in header) + " |")
    for entry, calls in rows:
        label = f"`{entry['tool']}`" + (f" {entry['case']}" if entry["case"] else "")
        first = calls[0]["elapsedMs"]
        warm = calls[1]["elapsedMs"]
        warm_wall = calls[1]["wallMs"]
        print(f"| {entry['group']} | {label} | {row_status(calls)} | {first} | {warm} | {warm_wall} |")


def run_measurements(args):
    running = roamer_running()
    if running is None:
        print("note: could not check for a running Roamer.exe; continuing", file=sys.stderr)
    elif running:
        print(
            "refusing to start: a Roamer.exe process is already running. "
            "Close it (or let it be measured, not launched) and re-run.",
            file=sys.stderr,
        )
        return 2

    repo_root = Path(__file__).resolve().parents[1]
    notes = []
    client = McpClient(repo_root)
    try:
        client.initialize()

        started = client.call_tool("start_navisworks", {
            "navisworksVersion": args.navisworks_version,
            "filePath": args.model,
            "waitForHost": True,
        })
        outcome = pick(started, "outcome")
        if outcome != "host_ready":
            raise RuntimeError(
                f"start_navisworks did not reach host_ready (outcome={outcome!r}, "
                f"failureReason={pick(started, 'failureReason')!r})")

        host_status = client.call_tool("host_status", {})
        plugin = {
            "pluginAssemblyLength": pick(host_status, "pluginAssemblyLength", "plugin_assembly_length"),
            "pluginAssemblyLastWriteUtc": pick(host_status, "pluginAssemblyLastWriteUtc", "plugin_assembly_last_write_utc"),
        }

        values = resolve_placeholders(client, notes)

        with open(args.out, "w", encoding="utf-8", newline="\n") as out:
            header = {
                "kind": "run",
                "startedAtUtc": utc_now_iso(),
                "model": args.model,
                "navisworksVersion": args.navisworks_version,
                "startOutcome": outcome,
                "startupElapsedMs": pick(started, "startupElapsedMs", "startup_elapsed_ms"),
                "elapsedMs": pick(started, "elapsedMs"),
                **plugin,
                "placeholders": values,
                "notes": notes,
                "columns": ["kind", "index", "call", "group", "tool", "case",
                            "arguments", "status", "elapsedMs", "wallMs", "toolOk",
                            "errorCode", "message", "startedAtUtc"],
            }
            out.write(json.dumps(header, ensure_ascii=False) + "\n")

            rows = []
            for index, entry in enumerate(PLAN, start=1):
                arguments = resolve_arguments(entry["arguments"], values)
                calls = []
                for call_number in (1, 2):
                    record = timed_call(client, entry["tool"], arguments)
                    record.update({
                        "kind": "call", "index": index, "call": call_number,
                        "group": entry["group"], "tool": entry["tool"],
                        "case": entry["case"], "arguments": arguments,
                    })
                    out.write(json.dumps(record, ensure_ascii=False) + "\n")
                    out.flush()
                    calls.append(record)
                rows.append((entry, calls))

            close_record = timed_call(client, "close_navisworks",
                                      {"mode": "discard", "apply": True, "confirmClose": True})
            close_record.update({"kind": "close", "tool": "close_navisworks"})
            out.write(json.dumps(close_record, ensure_ascii=False) + "\n")

        print(f"wrote {2 * len(PLAN) + 2} lines to {args.out}")
        print(f"model: {args.model}")
        print(f"start outcome: {outcome} "
              f"(startup {pick(started, 'startupElapsedMs', 'startup_elapsed_ms')} ms)")
        print(f"plugin: length {plugin['pluginAssemblyLength']}, "
              f"lastWriteUtc {plugin['pluginAssemblyLastWriteUtc']}")
        for note in notes:
            print(f"note: {note}")
        print_markdown_table(rows)
        return 0
    finally:
        client.close()


def main(argv=None):
    # A Windows console pipe may default to a legacy code page; every printed
    # character here (³, Cyrillic names) must survive the trip.
    try:
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    except (AttributeError, ValueError, OSError):
        pass

    parser = argparse.ArgumentParser(
        description="Re-run the read-only latency table of docs/MCP_TOOL_BASELINE.md on a live Navisworks.",
        epilog="With --plan no Navisworks is started and no MCP server is contacted: "
               "the plan prints and the process exits. Without --plan a Navisworks is "
               "launched on --model, placeholders ($ROOT_HANDLE, $PATH_2, $PATH_7, "
               "$ZONE_MIN, ...) are resolved against that document, every planned row "
               "is called twice, the per-call records go to --out as JSONL, and a "
               "Markdown table with the doc's columns is printed. The document is "
               "closed with mode=discard and never saved.")
    parser.add_argument("--plan", action="store_true",
                        help="print the planned calls as JSON (one entry per table row, in the table's order) and exit")
    parser.add_argument("--model", default=DEFAULT_MODEL,
                        help=f"model file to open (default: {DEFAULT_MODEL})")
    parser.add_argument("--navisworks-version", default=DEFAULT_NAVISWORKS_VERSION,
                        help=f"Navisworks Manage version to start (default: {DEFAULT_NAVISWORKS_VERSION})")
    parser.add_argument("--out", default=DEFAULT_OUT,
                        help=f"JSONL output file, one line per call (default: {DEFAULT_OUT})")
    args = parser.parse_args(argv)

    if args.plan:
        print(json.dumps(PLAN, indent=2))
        return 0

    return run_measurements(args)


if __name__ == "__main__":
    raise SystemExit(main())
