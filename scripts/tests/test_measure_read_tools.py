"""Acceptance test for the response-size mode of scripts/measure_read_tools.py (#427).

#427 asks for an L3 table of response sizes for every read-only tool, at default
arguments and at maximum limits, measured the same way as the latency table. These
tests pin the size plan (which tools, which arguments) and the size table's format;
none of them starts Navisworks or an MCP server.

The read-only set is read from the generated capability table in
docs/MCP_TOOL_CONTRACTS.md (43 of 128 tools on main 42ea9de). When a read-only tool is
added or removed, that table changes with it and this test fails until the size plan
follows.
"""

from __future__ import annotations

import contextlib
import io
import json
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))

import measure_read_tools as mrt  # noqa: E402

CONTRACTS = ROOT / "docs" / "MCP_TOOL_CONTRACTS.md"


def read_only_tools():
    """Tools whose Effects are `None` in the generated capability table.

    scripts/check_mcp_tool_capabilities.py keeps that table equal to the server's
    tool annotations, so a read-only tool the server gains shows up here too.
    """
    text = CONTRACTS.read_text(encoding="utf-8")
    block = text.split("<!-- BEGIN GENERATED TOOL CAPABILITIES -->", 1)[1]
    block = block.split("<!-- END GENERATED TOOL CAPABILITIES -->", 1)[0]
    tools = set()
    for line in block.splitlines():
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        if len(cells) == 5 and cells[0].startswith("`") and cells[1] == "None":
            tools.add(cells[0].strip("`"))
    return tools


READ_ONLY_TOOLS = read_only_tools()

# The "default" case passes only what the tool needs to have a subject; every
# tool not listed here is called with no arguments at all.
DEFAULT_ARGUMENTS = {
    "clash_run_status": {"operationId": "$NO_SUCH_OPERATION"},
    "find_duplicate_geometry": {"matchHandles": ["$ROOT_HANDLE"]},
    "find_items": {"query": "$ROOT_NAME", "comparison": "equals"},
    "find_items_by_bbox": {"min": "$ZONE_MIN", "max": "$ZONE_MAX"},
    "find_root_items_by_name": {"names": ["$ROOT_NAME"]},
    "get_item_texture_mapping": {"matchHandles": ["$ROOT_HANDLE"]},
    "get_scenario": {"scenarioId": "$SCENARIO_ID"},
    "item_appearance": {"matchHandles": ["$ROOT_HANDLE"]},
    "item_properties_by_handle": {"matchHandles": ["$ROOT_HANDLE"]},
    "list_item_children": {"parentPath": "$PATH_2"},
    "match_handle_items": {"matchHandle": "$ROOT_HANDLE"},
    "resolve_scenario": {"scenarioId": "$SCENARIO_ID"},
    "save_document_status": {"operationId": "$NO_SUCH_OPERATION"},
}

# The "max" case adds these knobs to the default arguments: an `include*` knob is
# set to true, every other knob to SIZE_MAX_LIMIT, which the host clamps to its own
# maximum. Tools not listed have no size knob; their "max" case equals "default".
MAX_KNOBS = {
    "active_model_context": ["rootItemLimit", "includeRootAliases", "includeSavedItemsSummary"],
    "clash_list_clusters": ["limit", "previewRowsPerCluster", "maxResults", "includeAllStatuses", "includeIgnored"],
    "clash_list_results": ["limit", "includeAllStatuses", "includeItemNames", "includeAssignedTo", "includeComments", "includeIgnored"],
    "clash_list_tests": ["limit", "includeStatusCounts"],
    "clash_root_matrix": ["limit", "includeAllStatuses"],
    "find_duplicate_geometry": ["maxGroups", "previewPerGroup"],
    "find_items": ["previewLimit"],
    "find_items_by_bbox": ["maxScannedItems", "maxResults", "previewLimit", "includeHidden", "includeContainers"],
    "find_root_items_by_name": ["previewLimit"],
    "get_item_texture_mapping": ["maxItems"],
    "item_appearance": ["maxItems"],
    "item_properties_by_handle": ["itemLimit", "propertyLimit", "includeInternalNames"],
    "list_item_children": ["limit", "includeHidden"],
    "list_recent_navisworks_files": ["limit"],
    "list_root_items": ["limit", "includeAliases"],
    "list_saved_viewpoints": ["limit", "includeItemIds"],
    "list_scenarios": ["limit"],
    "list_selection_sets": ["limit", "includeItemIds"],
    "match_handle_items": ["limit", "includePaths", "includeSourceFiles"],
    "mcp_health_check": ["rootItemLimit", "includeViewpointCheck"],
    "mcp_recent_calls": ["lineCount"],
    "selected_items_ancestry": ["limit", "includeBoundingBoxes"],
    "selected_items_preview": ["limit", "includeBoundingBoxes"],
    "selected_items_tree": ["maxItems", "includeBoundingBoxes"],
    "selection_copy_names": ["limit", "includePaths", "includeSourceFiles"],
    "selection_distinct_property_values": ["itemLimit", "valueLimit", "includeEmptyValues"],
    "selection_property_report": ["itemLimit", "propertyLimitPerItem", "rowLimit", "includeInternalNames", "includeEmptyValues"],
    "selection_status": ["includeBoundingBox"],
}

# Non-knob arguments a "max" case also sets: the flat format is the one that came
# back as 1-3 MB on the owner's model before #434.
MAX_EXTRAS = {
    "selected_items_tree": {"format": "flat"},
}


def expected_max(tool):
    arguments = dict(DEFAULT_ARGUMENTS.get(tool, {}))
    for knob in MAX_KNOBS.get(tool, []):
        arguments[knob] = True if knob.startswith("include") else mrt.SIZE_MAX_LIMIT
    arguments.update(MAX_EXTRAS.get(tool, {}))
    return arguments


def run_main(argv):
    out = io.StringIO()
    with contextlib.redirect_stdout(out):
        code = mrt.main(argv)
    return code, out.getvalue()


class FakeClient:
    def __init__(self, response):
        self.response = response
        self.calls = []

    def request(self, method, params, timeout=None):
        self.calls.append((method, params))
        return self.response


def tool_response(timing, payload=None):
    payload = dict(payload or {"ok": True})
    return {"result": {
        "content": [{"type": "text", "text": json.dumps(payload)}],
        "_meta": {"navishelper_timing": timing},
    }}


class SizePlanTests(unittest.TestCase):
    def test_constants(self):
        self.assertEqual(mrt.SIZE_MAX_LIMIT, 1_000_000)
        self.assertEqual(mrt.RESPONSE_BUDGET_BYTES, 84_000)

    def test_capability_table_was_found(self):
        self.assertIn("host_status", READ_ONLY_TOOLS)
        self.assertNotIn("save_document", READ_ONLY_TOOLS)

    def test_size_plan_covers_exactly_the_read_only_tools(self):
        self.assertEqual({entry["tool"] for entry in mrt.SIZE_PLAN}, READ_ONLY_TOOLS)

    def test_each_tool_has_default_then_max(self):
        cases = {}
        for entry in mrt.SIZE_PLAN:
            cases.setdefault(entry["tool"], []).append(entry["case"])
        for tool in READ_ONLY_TOOLS:
            self.assertEqual(cases[tool], ["default", "max"], tool)
        self.assertEqual(len(mrt.SIZE_PLAN), 2 * len(READ_ONLY_TOOLS))

    def test_default_arguments(self):
        for entry in mrt.SIZE_PLAN:
            if entry["case"] == "default":
                self.assertEqual(entry["arguments"], DEFAULT_ARGUMENTS.get(entry["tool"], {}), entry["tool"])

    def test_max_arguments(self):
        for entry in mrt.SIZE_PLAN:
            if entry["case"] == "max":
                self.assertEqual(entry["arguments"], expected_max(entry["tool"]), entry["tool"])

    def test_no_size_entry_passes_apply(self):
        for entry in mrt.SIZE_PLAN:
            self.assertNotIn("apply", entry["arguments"], entry["tool"])


class CliTests(unittest.TestCase):
    def test_plan_sizes_prints_the_size_plan(self):
        code, out = run_main(["--plan", "--sizes"])
        self.assertEqual(code, 0)
        self.assertEqual(json.loads(out), mrt.SIZE_PLAN)

    def test_plan_without_sizes_is_still_the_latency_plan(self):
        code, out = run_main(["--plan"])
        self.assertEqual(code, 0)
        self.assertEqual(json.loads(out), mrt.PLAN)
        self.assertEqual(len(mrt.PLAN), 39)


class ResponseBytesTests(unittest.TestCase):
    def test_timed_call_records_response_bytes(self):
        client = FakeClient(tool_response({"status": "ok", "elapsed_ms": 7, "tool_ok": True, "response_bytes": 1234}))
        record = mrt.timed_call(client, "host_status", {})
        self.assertEqual(record["responseBytes"], 1234)
        self.assertEqual(record["elapsedMs"], 7)

    def test_timed_call_without_response_bytes_records_none(self):
        client = FakeClient(tool_response({"status": "ok", "elapsed_ms": 7, "tool_ok": True}))
        record = mrt.timed_call(client, "host_status", {})
        self.assertIsNone(record["responseBytes"])

    def test_size_table_format(self):
        rows = [
            ({"tool": "selected_items_tree", "case": "max", "arguments": {}},
             {"status": "ok", "toolOk": True, "errorCode": None, "elapsedMs": 12, "responseBytes": 90000}),
            ({"tool": "host_status", "case": "default", "arguments": {}},
             {"status": "ok", "toolOk": True, "errorCode": None, "elapsedMs": 3, "responseBytes": 840}),
            ({"tool": "get_scenario", "case": "default", "arguments": {}},
             {"status": "ok", "toolOk": False, "errorCode": "scenario_not_found", "elapsedMs": 1, "responseBytes": 300}),
            ({"tool": "clash_run_status", "case": "default", "arguments": {}},
             {"status": "client_error", "toolOk": None, "errorCode": None, "elapsedMs": None, "responseBytes": None}),
        ]
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            mrt.print_size_table(rows)
        lines = out.getvalue().splitlines()
        self.assertIn("| tool | case | status | response bytes | budget | host ms |", lines)
        self.assertIn("| `selected_items_tree` | max | ok | 90000 | over | 12 |", lines)
        self.assertIn("| `host_status` | default | ok | 840 | within | 3 |", lines)
        self.assertIn("| `get_scenario` | default | refused:scenario_not_found | 300 | within | 1 |", lines)
        self.assertIn("| `clash_run_status` | default | client_error | None | unknown | None |", lines)


if __name__ == "__main__":
    unittest.main()
