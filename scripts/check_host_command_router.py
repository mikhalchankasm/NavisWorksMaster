#!/usr/bin/env python3
"""Guard the typed host router against command-surface drift."""

from pathlib import Path
import re


ROOT = Path(__file__).resolve().parents[1]
STATUSES = ROOT / "NavisHelper.Contracts" / "Statuses.cs"
ROUTERS = (
    ROOT / "NavisHelper" / "Agent" / "Host" / "AgentHostService.CommandRouter.cs",
    ROOT / "NavisHelper" / "Agent" / "Host" / "AgentHostService.ClashTransfer.cs",
)
DISPATCH = ROOT / "NavisHelper" / "Agent" / "Host" / "AgentHostService.Dispatch.cs"
REQUEST_POLICY = ROOT / "NavisHelper.Contracts" / "HostRequestPolicy.cs"
TRANSPORT = ROOT / "NavisHelper" / "Agent" / "Host" / "AgentHostService.Transport.cs"
OPERATION_HISTORY = ROOT / "NavisHelper" / "Agent" / "Host" / "AgentHostService.OperationHistory.cs"
EXPECTED_COMMAND_COUNT = 87
EXPECTED_SPECIAL = {"FindItems"}
EXPECTED_BYPASS = {
    "ClashReportStatus",
    "LastOperationStatus",
    "CancelClashReport",
    "CancelSubtreeNamesDump",
    "ClashRunStatus",
    "CancelClashRun",
}


def command_names_from_contract() -> set[str]:
    text = STATUSES.read_text(encoding="utf-8-sig")
    start = text.index("public static class HostCommandNames")
    end = text.index("\n    }", start)
    return set(re.findall(r"public const string (\w+)\s*=", text[start:end]))


def router_references() -> list[str]:
    references: list[str] = []
    for path in ROUTERS:
        text = path.read_text(encoding="utf-8-sig")
        references.extend(re.findall(r"router\.Register<[^>]+>\(\s*HostCommandNames\.(\w+)", text, re.DOTALL))
    return references


def special_dispatch_references() -> list[str]:
    text = DISPATCH.read_text(encoding="utf-8-sig")
    return re.findall(r"string\.Equals\(\s*command\s*,\s*HostCommandNames\.(\w+)\s*,", text)


def policy_references() -> list[str]:
    text = REQUEST_POLICY.read_text(encoding="utf-8-sig")
    return re.findall(r"string\.Equals\(\s*command\s*,\s*HostCommandNames\.(\w+)\s*,", text)


def enum_members(path: Path, enum_name: str) -> set[str]:
    text = path.read_text(encoding="utf-8-sig")
    match = re.search(rf"public\s+enum\s+{enum_name}\s*\{{(?P<body>.*?)\}}", text, re.DOTALL)
    if match is None:
        raise SystemExit(f"Enum {enum_name} was not found in {path}.")
    return set(
        re.findall(
            r"^\s*(\w+)\s*(?:=\s*[^,]+)?\s*(?:,|$)",
            match.group("body"),
            re.MULTILINE,
        )
    )


def dispatch_bypass_arms() -> set[str]:
    text = DISPATCH.read_text(encoding="utf-8-sig")
    return set(re.findall(r"case\s+HostRequestGateBypassKind\.(\w+)\s*:", text))


def require_call(path: Path, expression: str, expected_count: int) -> None:
    actual_count = path.read_text(encoding="utf-8-sig").count(expression)
    if actual_count != expected_count:
        raise SystemExit(
            f"Expected {expected_count} call(s) to {expression!r} in {path}, found {actual_count}."
        )


def main() -> int:
    expected = command_names_from_contract()
    router_refs = router_references()
    dispatch_refs = special_dispatch_references()
    policy_refs = policy_references()
    router_names = set(router_refs)
    dispatch_names = set(dispatch_refs)
    policy_names = set(policy_refs)
    covered = router_names | dispatch_names | policy_names

    if len(expected) != EXPECTED_COMMAND_COUNT:
        raise SystemExit(
            f"HostCommandNames count drift: expected {EXPECTED_COMMAND_COUNT}, found {len(expected)}."
        )
    if len(router_refs) != len(router_names):
        raise SystemExit("CommandRouter contains duplicate HostCommandNames references.")
    if len(dispatch_refs) != len(dispatch_names):
        raise SystemExit("Special dispatch contains duplicate HostCommandNames references.")
    if dispatch_names != EXPECTED_SPECIAL:
        raise SystemExit(
            "Special dispatch drift: "
            f"missing={sorted(EXPECTED_SPECIAL - dispatch_names)} "
            f"extra={sorted(dispatch_names - EXPECTED_SPECIAL)}"
        )
    if len(policy_refs) != len(policy_names):
        raise SystemExit("HostRequestPolicy contains duplicate HostCommandNames references.")
    if policy_names != EXPECTED_BYPASS:
        raise SystemExit(
            "Request-gate bypass policy drift: "
            f"missing={sorted(EXPECTED_BYPASS - policy_names)} "
            f"extra={sorted(policy_names - EXPECTED_BYPASS)}"
        )
    if router_names & policy_names:
        raise SystemExit(
            "Request-gate bypass commands must not be registered in the typed router: "
            f"{sorted(router_names & policy_names)}"
        )
    if router_names & dispatch_names:
        raise SystemExit(
            "Special commands must not be registered in the typed router: "
            f"{sorted(router_names & dispatch_names)}"
        )

    bypass_kinds = enum_members(REQUEST_POLICY, "HostRequestGateBypassKind") - {"None"}
    dispatch_arms = dispatch_bypass_arms()
    if dispatch_arms != bypass_kinds:
        raise SystemExit(
            "Bypass dispatch arm drift: "
            f"missing={sorted(bypass_kinds - dispatch_arms)} "
            f"extra={sorted(dispatch_arms - bypass_kinds)}"
        )

    require_call(TRANSPORT, "HostRequestPolicy.IsRequestGateBypassCommand(command)", 1)
    require_call(DISPATCH, "HostRequestPolicy.GetRequestGateBypassKind(command)", 1)
    require_call(OPERATION_HISTORY, "HostRequestPolicy.IsOperationStatusPollCommand(command)", 3)
    if covered != expected:
        raise SystemExit(
            "Host command coverage drift: "
            f"missing={sorted(expected - covered)} extra={sorted(covered - expected)}"
        )

    print(
        "Host command router check passed: "
        f"{len(expected)} command names, {len(router_names)} typed routes, "
        f"{len(dispatch_names)} special references, {len(policy_names)} policy bypass references."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
