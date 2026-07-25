#!/usr/bin/env python3
import argparse
from datetime import datetime, timezone
import json
import sys
import time
from pathlib import Path

from navishelper_mcp_smoke import (
    McpClient,
    add_target,
    choose_names,
    choose_target,
    extract_handles,
    get_value,
    repo_root_from_script,
)


def elapsed_ms(started_at):
    return int((time.time() - started_at) * 1000)


def call_timed(client, name, arguments, timings):
    started_at = time.time()
    response = client.call_tool(name, arguments)
    timings.append({"tool": name, "elapsed_ms": elapsed_ms(started_at)})
    return response


def get_working_set(health):
    return health.get("workingSetMb", health.get("working_set_mb"))


def write_report(report_json_file, output):
    if not report_json_file:
        return

    report_path = Path(report_json_file)
    report_path.parent.mkdir(parents=True, exist_ok=True)
    output["reportJsonFile"] = str(report_path)
    report_path.write_text(json.dumps(output, ensure_ascii=False, indent=2), encoding="utf-8")


def main():
    parser = argparse.ArgumentParser(description="Mixed MCP stdio stress test for NavisHelper tools.")
    parser.add_argument("--count", type=int, default=50, help="Number of mixed stress iterations.")
    parser.add_argument("--delay-ms", type=int, default=0, help="Delay between iterations.")
    parser.add_argument("--names", nargs="*", help="Optional exact root item names.")
    parser.add_argument("--include-selection", action="store_true", help="Also run selection-changing select_items plus selection dry-runs.")
    parser.add_argument("--memory-checkpoint-interval", type=int, default=25, help="Run mcp_health_check and record memory every N iterations. Use 0 to disable.")
    parser.add_argument("--max-memory-delta-mb", type=float, default=0.0, help="Fail when working set delta exceeds this value. Use 0 to disable.")
    parser.add_argument("--report-json-file", default="", help="Optional path to write the stress report JSON.")
    parser.add_argument("--instance-id", default="", help="Explicit Navisworks host instance_id from list_navisworks_hosts.")
    parser.add_argument("--navisworks-version", default="", help="Target Navisworks version, for example 2027.")
    args = parser.parse_args()

    if args.count < 1:
        raise RuntimeError("--count must be at least 1.")

    repo_root = repo_root_from_script()
    client = McpClient(repo_root)
    timings = []
    started_at = time.time()
    started_at_utc = datetime.now(timezone.utc).isoformat()

    try:
        client.initialize()
        diagnostics = client.call_tool("mcp_diagnostics")
        hosts = {"hosts": diagnostics.get("hosts", [])}
        target = choose_target(hosts, args.instance_id, args.navisworks_version)

        health_before = call_timed(client, "mcp_health_check", add_target({"rootItemLimit": 5}, target), timings)
        if not health_before.get("ok", False):
            raise RuntimeError(f"mcp_health_check before stress returned degraded status: {health_before}")
        working_set_before = get_working_set(health_before)

        root_items = call_timed(client, "list_root_items", add_target({"limit": 1000}, target), timings)
        names = choose_names(root_items, args.names)

        ok_count = 0
        memory_checkpoints = []
        for iteration in range(1, args.count + 1):
            status = call_timed(client, "host_status", add_target({}, target), timings)
            if not status.get("hasActiveDocument", status.get("has_active_document", False)):
                raise RuntimeError(f"host_status reported no active document at iteration {iteration}: {status}")

            context = call_timed(
                client,
                "active_model_context",
                add_target({"rootItemLimit": 10, "includeSavedItemsSummary": False}, target),
                timings,
            )
            context_root_items = context.get("rootItems", context.get("root_items", {}))
            if context_root_items.get("rootItemCount", context_root_items.get("root_item_count", 0)) < 1:
                raise RuntimeError(f"active_model_context returned no root count at iteration {iteration}: {context}")

            find_response = call_timed(
                client,
                "find_root_items_by_name",
                add_target({"names": names, "comparison": "equals", "previewLimit": 1}, target),
                timings,
            )
            summary = find_response.get("summary", {})
            matched = summary.get("matchedQueries", summary.get("matched_queries", 0))
            if matched != len(names):
                raise RuntimeError(f"find_root_items_by_name matched {matched}/{len(names)} at iteration {iteration}.")

            handles = extract_handles(find_response)
            if not handles:
                raise RuntimeError(f"find_root_items_by_name returned no handles at iteration {iteration}.")

            properties = call_timed(
                client,
                "item_properties_by_handle",
                add_target(
                    {
                        "matchHandles": handles[:1],
                        "itemLimit": 1,
                        "propertyLimit": 10,
                        "categoryFilters": ["Item"],
                    },
                    target,
                ),
                timings,
            )
            property_results = properties.get("results", [])
            if not property_results:
                raise RuntimeError(f"item_properties_by_handle returned no results at iteration {iteration}: {properties}")

            saved_viewpoints = call_timed(client, "list_saved_viewpoints", add_target({"limit": 5}, target), timings)
            if saved_viewpoints.get("returnedItemCount", saved_viewpoints.get("returned_item_count", 0)) < 0:
                raise RuntimeError(f"list_saved_viewpoints returned invalid count at iteration {iteration}: {saved_viewpoints}")

            selection_sets = call_timed(client, "list_selection_sets", add_target({"limit": 5}, target), timings)
            if selection_sets.get("returnedItemCount", selection_sets.get("returned_item_count", 0)) < 0:
                raise RuntimeError(f"list_selection_sets returned invalid count at iteration {iteration}: {selection_sets}")

            viewpoint = call_timed(client, "current_viewpoint_info", add_target({}, target), timings)
            if not viewpoint.get("hasCurrentViewpoint", viewpoint.get("has_current_viewpoint", False)):
                raise RuntimeError(f"current_viewpoint_info returned no current viewpoint at iteration {iteration}: {viewpoint}")

            if args.include_selection:
                selected = call_timed(client, "select_items", add_target({"matchHandles": handles}, target), timings)
                selected_count = selected.get("selectedItemCount", selected.get("selected_item_count", 0))
                if selected_count < len(handles):
                    raise RuntimeError(f"select_items selected fewer items at iteration {iteration}: {selected}")

                selection_status = call_timed(
                    client,
                    "selection_status",
                    add_target({"includeBoundingBox": False}, target),
                    timings,
                )
                if selection_status.get("selectedItemCount", selection_status.get("selected_item_count", 0)) < len(handles):
                    raise RuntimeError(f"selection_status returned fewer items at iteration {iteration}: {selection_status}")

                hide_preview = call_timed(client, "hide_unselected", add_target({"previewLimit": 2}, target), timings)
                if hide_preview.get("apply", hide_preview.get("Apply", True)):
                    raise RuntimeError(f"hide_unselected did not default to dry-run at iteration {iteration}: {hide_preview}")

            ok_count += 1
            if args.memory_checkpoint_interval > 0 and iteration % args.memory_checkpoint_interval == 0:
                checkpoint_health = call_timed(client, "mcp_health_check", add_target({"rootItemLimit": 5}, target), timings)
                checkpoint_working_set = get_working_set(checkpoint_health)
                checkpoint_delta = None
                if working_set_before is not None and checkpoint_working_set is not None:
                    checkpoint_delta = round(float(checkpoint_working_set) - float(working_set_before), 1)

                checkpoint = {
                    "iteration": iteration,
                    "elapsedMs": elapsed_ms(started_at),
                    "ok": checkpoint_health.get("ok", False),
                    "workingSetMb": checkpoint_working_set,
                    "workingSetDeltaMb": checkpoint_delta,
                    "healthVerdict": checkpoint_health.get("verdict"),
                }
                memory_checkpoints.append(checkpoint)
                if not checkpoint["ok"]:
                    raise RuntimeError(f"mcp_health_check checkpoint degraded at iteration {iteration}: {checkpoint_health}")
                if args.max_memory_delta_mb > 0 and checkpoint_delta is not None and checkpoint_delta > args.max_memory_delta_mb:
                    raise RuntimeError(
                        f"Memory delta exceeded threshold at iteration {iteration}: "
                        f"delta={checkpoint_delta}MB threshold={args.max_memory_delta_mb}MB"
                    )

            if args.delay_ms > 0:
                time.sleep(args.delay_ms / 1000.0)

        health_after = call_timed(client, "mcp_health_check", add_target({"rootItemLimit": 5}, target), timings)
        if not health_after.get("ok", False):
            raise RuntimeError(f"mcp_health_check after stress returned degraded status: {health_after}")
        working_set_after = get_working_set(health_after)
        working_set_delta = None
        if working_set_before is not None and working_set_after is not None:
            working_set_delta = round(float(working_set_after) - float(working_set_before), 1)
        if args.max_memory_delta_mb > 0 and working_set_delta is not None and working_set_delta > args.max_memory_delta_mb:
            raise RuntimeError(
                f"Final memory delta exceeded threshold: delta={working_set_delta}MB "
                f"threshold={args.max_memory_delta_mb}MB"
            )

        total_elapsed = elapsed_ms(started_at)
        by_tool = {}
        for timing in timings:
            stats = by_tool.setdefault(timing["tool"], {"count": 0, "total_ms": 0, "max_ms": 0})
            stats["count"] += 1
            stats["total_ms"] += timing["elapsed_ms"]
            stats["max_ms"] = max(stats["max_ms"], timing["elapsed_ms"])

        for stats in by_tool.values():
            stats["avg_ms"] = round(stats["total_ms"] / stats["count"], 1)

        output = {
            "ok": True,
            "startedAtUtc": started_at_utc,
            "endedAtUtc": datetime.now(timezone.utc).isoformat(),
            "requestedCount": args.count,
            "completedCount": ok_count,
            "includeSelection": args.include_selection,
            "target": target,
            "names": names,
            "totalElapsedMs": total_elapsed,
            "totalToolCalls": len(timings),
            "workingSetBeforeMb": working_set_before,
            "workingSetAfterMb": working_set_after,
            "workingSetDeltaMb": working_set_delta,
            "memoryCheckpointInterval": args.memory_checkpoint_interval,
            "maxMemoryDeltaMb": args.max_memory_delta_mb,
            "memoryCheckpoints": memory_checkpoints,
            "healthBefore": health_before,
            "healthAfter": health_after,
            "byTool": by_tool,
            "logFilePath": get_value(diagnostics, "logFilePath", "log_file_path"),
        }
        write_report(args.report_json_file, output)

        print(json.dumps(output, ensure_ascii=False, indent=2))
        return 0
    finally:
        client.close()


if __name__ == "__main__":
    sys.exit(main())
