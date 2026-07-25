#!/usr/bin/env python3
import argparse
import json
import sys

from navishelper_mcp_smoke import McpClient, repo_root_from_script


def find_recent_error_code(client, expected_codes):
    try:
        recent = client.call_tool("mcp_recent_calls", {"lineCount": 50})
    except Exception:
        return None, None

    for line in reversed(recent.get("lines", [])):
        try:
            entry = json.loads(line)
        except json.JSONDecodeError:
            continue

        error_code = entry.get("error_code")
        if error_code in expected_codes:
            return error_code, entry

        error_message = entry.get("error_message") or ""
        for code in expected_codes:
            if code in error_message:
                return code, entry

    return None, None


def expect_tool_error(client, tool_name, arguments, expected_codes):
    try:
        client.call_tool(tool_name, arguments)
    except Exception as exc:
        message = str(exc)
        for code in expected_codes:
            if code in message:
                return {"ok": True, "tool": tool_name, "errorCode": code, "message": message}

        recent_code, recent_entry = find_recent_error_code(client, expected_codes)
        if recent_code:
            return {
                "ok": True,
                "tool": tool_name,
                "errorCode": recent_code,
                "message": message,
                "source": "mcp_recent_calls",
                "recentEntry": recent_entry,
            }

        raise RuntimeError(f"{tool_name} failed with unexpected error. Expected={expected_codes} Message={message}")

    raise RuntimeError(f"{tool_name} unexpectedly succeeded. Expected one of {expected_codes}.")


def get_hosts(client):
    response = client.call_tool("list_navisworks_hosts")
    return response.get("hosts", [])


def scenario_multiple_hosts(client):
    hosts = get_hosts(client)
    if len(hosts) < 2:
        raise RuntimeError(f"multiple-hosts scenario needs at least 2 hosts, got {len(hosts)}.")

    expected = expect_tool_error(client, "host_status", {}, ["multiple_hosts_detected"])
    targeted = []
    for host in hosts[:2]:
        instance_id = host.get("instanceId") or host.get("instance_id")
        status = client.call_tool("host_status", {"instanceId": instance_id})
        if not status.get("hasActiveDocument", status.get("has_active_document", False)):
            raise RuntimeError(f"targeted host_status returned no active document for {instance_id}: {status}")
        targeted.append({
            "instanceId": instance_id,
            "documentTitle": status.get("documentTitle", status.get("document_title")),
            "rootItemCount": status.get("rootItemCount", status.get("root_item_count")),
        })

    return {
        "scenario": "multiple-hosts",
        "hostCount": len(hosts),
        "expectedError": expected,
        "targeted": targeted,
    }


def scenario_no_document(client, instance_id):
    if not instance_id:
        raise RuntimeError("no-document scenario requires --instance-id.")

    status = client.call_tool("host_status", {"instanceId": instance_id})
    model_count = status.get("modelCount", status.get("model_count", 0))
    root_item_count = status.get("rootItemCount", status.get("root_item_count", 0))
    document_file_name = status.get("documentFileName", status.get("document_file_name", ""))
    if model_count != 0 or root_item_count != 0 or document_file_name:
        raise RuntimeError(f"Expected empty/no-model document, got: {status}")

    health = client.call_tool("mcp_health_check", {"instanceId": instance_id})
    if health.get("ok", True):
        raise RuntimeError(f"Expected degraded health with no active document, got: {health}")

    return {
        "scenario": "no-document",
        "instanceId": instance_id,
        "status": {
            "hasActiveDocument": status.get("hasActiveDocument", status.get("has_active_document")),
            "documentTitle": status.get("documentTitle", status.get("document_title")),
            "modelCount": model_count,
            "rootItemCount": root_item_count,
        },
        "healthVerdict": health.get("verdict"),
        "failedChecks": [
            check for check in health.get("checks", [])
            if not check.get("ok", False)
        ],
    }


def scenario_host_closed(client, instance_id):
    if not instance_id:
        raise RuntimeError("host-closed scenario requires --instance-id.")

    expected = expect_tool_error(
        client,
        "host_status",
        {"instanceId": instance_id},
        ["instance_not_found", "transport_connect_failed"],
    )
    hosts = get_hosts(client)
    return {
        "scenario": "host-closed",
        "instanceId": instance_id,
        "expectedError": expected,
        "remainingHostCount": len(hosts),
    }


def main():
    parser = argparse.ArgumentParser(description="Assert NavisHelper MCP failure-mode behavior.")
    parser.add_argument("--scenario", required=True, choices=["multiple-hosts", "no-document", "host-closed"])
    parser.add_argument("--instance-id", default="")
    args = parser.parse_args()

    client = McpClient(repo_root_from_script())
    try:
        client.initialize()
        if args.scenario == "multiple-hosts":
            output = scenario_multiple_hosts(client)
        elif args.scenario == "no-document":
            output = scenario_no_document(client, args.instance_id)
        elif args.scenario == "host-closed":
            output = scenario_host_closed(client, args.instance_id)
        else:
            raise RuntimeError(f"Unsupported scenario: {args.scenario}")

        output["ok"] = True
        print(json.dumps(output, ensure_ascii=False, indent=2))
        return 0
    finally:
        client.close()


if __name__ == "__main__":
    sys.exit(main())
