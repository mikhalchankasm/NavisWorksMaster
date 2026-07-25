#!/usr/bin/env python3
"""No-Navisworks stdio smoke for the persistent scenario-library MCP tools."""

from __future__ import annotations

import os
import tempfile
from pathlib import Path

from navishelper_mcp_smoke import McpClient


def assert_true(value: object, message: str) -> None:
    if not value:
        raise RuntimeError(message)


def template_draft() -> dict:
    return {
        "schemaVersion": 1,
        "executionMode": "template",
        "name": "Scenario stdio smoke",
        "description": "No-Navisworks MCP lifecycle smoke.",
        "context": {"navisworksVersions": ["2027"]},
        "parameters": [
            {
                "name": "outputPath",
                "type": "filePath",
                "title": "Output",
                "required": True,
            }
        ],
        "steps": [
            {
                "stepId": "exportSelection",
                "tool": "selection_export_properties",
                "scenarioContractVersion": 1,
                "arguments": {
                    "outputPath": {"$parameter": "outputPath"},
                    "format": "csv",
                },
            }
        ],
    }


def exact_draft() -> dict:
    draft = template_draft()
    draft["executionMode"] = "exactReplay"
    draft["name"] = "Exact scenario stdio smoke"
    draft["exactReplay"] = {
        "fixedParameters": {"outputPath": "D:/Reports/scenario-smoke.csv"},
        "contextPolicy": "strict",
        "writePolicy": "repeatReviewedWrites",
        "safetyEnvelope": {
            # save_scenario replaces this valid placeholder with the canonical plan hash.
            "previewFingerprint": "sha256:" + ("0" * 64),
            "stepLimits": {
                "exportSelection": {
                    "maxMatchedItems": 100,
                    "maxModelWrites": 0,
                    "maxFileWrites": 1,
                    "approvedScaleGates": [],
                }
            },
        },
    }
    return draft


def main() -> int:
    repo_root = Path(__file__).resolve().parents[1]
    previous_root = os.environ.get("NAVISHELPER_SCENARIO_DIR")

    with tempfile.TemporaryDirectory(prefix="NavisHelper-ScenarioStdioSmoke-") as scenario_root:
        os.environ["NAVISHELPER_SCENARIO_DIR"] = scenario_root
        client = McpClient(repo_root)
        try:
            client.initialize()
            tools = client.list_tools()
            tool_names = {tool["name"] for tool in tools}
            assert_true(
                len(tools) == len(tool_names),
                "MCP tool registration contains duplicate public names.",
            )
            required = {
                "list_scenarios",
                "get_scenario",
                "save_scenario",
                "delete_scenario",
                "resolve_scenario",
                "scenario_capabilities",
            }
            assert_true(required <= tool_names, f"Missing scenario tools: {sorted(required - tool_names)}")

            save_scenario = next(tool for tool in tools if tool["name"] == "save_scenario")
            default_schema = (
                save_scenario["inputSchema"]["properties"]["scenario"]["properties"]
                ["parameters"]["items"]["properties"]["default"]
            )
            assert_true(
                default_schema == {},
                f"save_scenario default value schema is not Moonshot-compatible: {default_schema}",
            )

            capabilities = client.call_tool("scenario_capabilities")
            allowed_tools = {
                item.get("tool", "")
                for item in capabilities.get("allowedTools", [])
                if isinstance(item, dict)
            }
            allowed_tools.discard("")
            assert_true(allowed_tools, f"Scenario allowlist is empty: {capabilities}")
            assert_true(
                allowed_tools <= tool_names,
                "Scenario allowlist contains unregistered MCP tools: "
                f"{sorted(allowed_tools - tool_names)}",
            )

            template = client.call_tool(
                "save_scenario",
                {"scenario": template_draft(), "apply": True, "confirmSave": True},
            )
            assert_true(template.get("ok") and template.get("applied"), f"Template save failed: {template}")

            resolved_template = client.call_tool(
                "resolve_scenario",
                {
                    "scenarioId": template["scenarioId"],
                    "parameterValues": {"outputPath": "D:/Reports/scenario-smoke.csv"},
                    "navisworksVersion": "2027",
                },
            )
            assert_true(resolved_template.get("ok"), f"Template resolve failed: {resolved_template}")
            assert_true(
                not resolved_template["steps"][0]["applyArgumentOverrides"],
                "Template preview unexpectedly emitted apply authorization.",
            )

            exact = client.call_tool(
                "save_scenario",
                {
                    "scenario": exact_draft(),
                    "apply": True,
                    "confirmSave": True,
                    "confirmExactReplay": True,
                },
            )
            assert_true(exact.get("ok") and exact.get("applied"), f"Exact save failed: {exact}")

            exact_preview = client.call_tool(
                "resolve_scenario",
                {"scenarioId": exact["scenarioId"], "executionIntent": "preview"},
            )
            assert_true(exact_preview.get("ok"), f"Exact preview failed: {exact_preview}")
            assert_true(
                not exact_preview["steps"][0]["applyArgumentOverrides"],
                "Exact preview unexpectedly emitted apply authorization.",
            )

            exact_replay = client.call_tool(
                "resolve_scenario",
                {
                    "scenarioId": exact["scenarioId"],
                    "executionIntent": "exact_replay",
                    "navisworksVersion": "2027",
                },
            )
            assert_true(exact_replay.get("ok"), f"Exact replay resolve failed: {exact_replay}")
            assert_true(
                exact_replay["steps"][0]["applyArgumentOverrides"].get("apply") is True,
                "Direct exact replay did not emit the expected apply override.",
            )

            listed = client.call_tool("list_scenarios")
            assert_true(listed.get("count") == 2, f"Unexpected scenario list: {listed}")

            fetched = client.call_tool("get_scenario", {"scenarioId": exact["scenarioId"]})
            fingerprint = fetched["scenario"]["exactReplay"]["safetyEnvelope"]["previewFingerprint"]
            assert_true(fingerprint != "sha256:" + ("0" * 64), "Server did not bind canonical plan fingerprint.")

            for saved in (template, exact):
                deleted = client.call_tool(
                    "delete_scenario",
                    {
                        "scenarioId": saved["scenarioId"],
                        "expectedSha256": saved["sha256"],
                        "apply": True,
                        "confirmDelete": True,
                    },
                )
                assert_true(deleted.get("applied"), f"Scenario delete failed: {deleted}")

            print("Scenario-library stdio smoke passed: template + exactReplay lifecycle.")
            return 0
        finally:
            client.close()
            if previous_root is None:
                os.environ.pop("NAVISHELPER_SCENARIO_DIR", None)
            else:
                os.environ["NAVISHELPER_SCENARIO_DIR"] = previous_root


if __name__ == "__main__":
    raise SystemExit(main())
