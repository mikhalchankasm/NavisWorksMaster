#!/usr/bin/env python3
"""Geometry acceptance against an explicitly selected disposable host.

Changes that host's selection, never saves its model. Writes only --output-dir.
Start a fresh SDK/sample model and verify its loaded plugin before running.
"""
import argparse
import csv
import json
import math
from pathlib import Path
from navishelper_mcp_smoke import McpClient


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--instance-id", required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    out = args.output_dir.resolve()
    out.mkdir(parents=True, exist_ok=True)
    client = McpClient(root)
    evidence = {}

    def call(name, **kwargs):
        result = client.call_tool(name, {"instanceId": args.instance_id, **kwargs})
        return result

    try:
        client.initialize()
        tools = {t["name"]: t for t in client.list_tools()}
        assert "export_selection_geometry" in tools
        assert "includeChain" in tools["selected_items_tree"]["inputSchema"]["properties"]
        evidence["host"] = call("host_status")
        records = client.call_tool("list_navisworks_hosts")["hosts"]
        record = next(h for h in records if h["instanceId"] == args.instance_id)
        assert record["documentTitleSource"] == "live_host_status", record
        roots = call("list_root_items")["items"]
        root_item = roots[0]
        found = call("find_root_items_by_name", names=[root_item["displayName"]], comparison="equals")
        handle = found["results"][0]["matches"][0]["matchHandle"]
        first = call("list_item_children", parentMatchHandle=handle, limit=1)
        second = call("list_item_children", parentMatchHandle=handle, limit=1, offset=1)
        assert first["children"][0]["matchHandle"] != second["children"][0]["matchHandle"]
        assert first["nextOffset"] == 1
        assert second["children"][0]["index"] == 2
        by_path = call("list_item_children", parentPath=first["children"][0]["path"])
        assert by_path["parentPath"] == first["children"][0]["path"]
        name = first["children"][0]["displayName"]
        all_matches = call("find_items", query=name, comparison="equals", matchDepth="all")
        scoped = call("find_items", query=name, comparison="equals", scope="under_handle", scopeHandle=handle, matchDepth="first")
        assert all_matches["matchedItemCount"] > 0 and scoped["matchedItemCount"] > 0
        evidence["search"] = {"all": all_matches, "scopedFirst": scoped}
        call("select_items", matchHandles=[handle])
        before = call("selection_status")
        flat = call("selected_items_tree", format="flat", includeChain=False, includeBoundingBoxes=True)
        assert all(not i.get("chain") for i in flat["items"])
        evidence["selection"] = before
        path = out / "mesh.jsonl"
        common = dict(scope="match_handle", matchHandles=[handle], itemLimit=10000, maxTriangles=1000000)
        dry = call("export_selection_geometry", outputPath=str(path), format="jsonl", **common)
        assert not dry["applied"] and not path.exists()
        assert dry["triangleCount"] > 0, dry
        applied = call("export_selection_geometry", outputPath=str(path), format="jsonl", apply=True, **common)
        assert applied["triangleCount"] == dry["triangleCount"] and applied["applied"]
        evidence["export"] = applied
        triangles = []
        fragments = []
        with path.open(encoding="utf-8") as stream:
            for line in stream:
                record = json.loads(line)
                if record["type"] == "triangle": triangles.append(record)
                if record["type"] == "fragment": fragments.append(record["fragment"])
        assert len(triangles) == applied["triangleCount"]
        assert len(fragments) == applied["fragmentCount"]
        vertices = [v for t in triangles for v in t["vertices"]]
        evidence["meshBounds"] = {"min": [min(v[i] for v in vertices) for i in range(3)],
                                  "max": [max(v[i] for v in vertices) for i in range(3)]}
        for bound in ("min", "max"):
            for i, axis in enumerate(("x", "y", "z")):
                assert math.isclose(evidence["meshBounds"][bound][i], before["boundingBox"][bound][axis], rel_tol=1e-6, abs_tol=1e-6)
        # Explicit duplicate/overlapping root and child scopes must count once.
        overlap = call("export_selection_geometry", outputPath=str(out / "overlap.obj"),
                       matchHandles=[handle, first["children"][0]["matchHandle"]], scope="match_handle")
        assert overlap["triangleCount"] == dry["triangleCount"]
        for fmt in ("obj", "ply", "stl"):
            result = call("export_selection_geometry", outputPath=str(out / ("mesh." + fmt)), format=fmt, apply=True, **common)
            assert result["triangleCount"] == dry["triangleCount"]
        local = call("export_selection_geometry", outputPath=str(out / "local.jsonl"), format="jsonl", coordinateSpace="item_local", apply=True, **common)
        assert local["triangleCount"] == dry["triangleCount"]
        local_records = [json.loads(line) for line in (out / "local.jsonl").read_text(encoding="utf-8").splitlines()]
        frames = {r["fragment"]["fragmentId"]: r["fragment"]["outputToWorld"] for r in local_records if r["type"] == "fragment"}
        local_triangles = [r for r in local_records if r["type"] == "triangle"]
        for world_triangle, local_triangle in zip(triangles, local_triangles):
            matrix = frames[local_triangle["fragmentId"]]
            for world_vertex, local_vertex in zip(world_triangle["vertices"], local_triangle["vertices"]):
                for axis in range(3):
                    restored = sum(matrix[j * 4 + axis] * local_vertex[j] for j in range(3)) + matrix[12 + axis]
                    assert math.isclose(restored, world_vertex[axis], rel_tol=1e-10, abs_tol=1e-10)
        sentinel = out / "sentinel.obj"
        sentinel.write_text("preserve-me", encoding="utf-8")
        try:
            call("export_selection_geometry", outputPath=str(sentinel), scope="match_handle", matchHandles=[handle], apply=True, overwrite=True, maxTriangles=1)
            raise AssertionError("Triangle limit did not fail")
        except RuntimeError as error:
            assert "maxTriangles" in str(error), error
        assert sentinel.read_text(encoding="utf-8") == "preserve-me"
        assert not list(out.glob("*.tmp*"))
        fake = handle.replace(args.instance_id, "different-instance")
        try:
            call("list_item_children", parentMatchHandle=fake)
            raise AssertionError("Foreign host handle was accepted")
        except RuntimeError as error:
            assert "different-instance" in str(error) and "stale_match_reference" in str(error)
        partial = call("select_items", matchHandles=[handle, fake])
        assert partial["partial"] and partial["results"][1]["status"] == "stale"
        assert "different-instance" in partial["results"][1]["message"]
        report_path = out / "properties.csv"
        props = call("selection_export_properties", outputPath=str(report_path), scope="match_handle", matchHandles=[handle, first["children"][0]["matchHandle"]], cleanValues=True, apply=True, includeEmptyValues=True)
        with report_path.open(encoding="utf-8-sig", newline="") as stream:
            rows = list(csv.DictReader(stream, delimiter=";"))
        assert rows and "ItemIndex" in rows[0]
        assert props["selectedItemCount"] >= 2
        after = call("selection_status")
        assert before["selectedItemCount"] == after["selectedItemCount"]
        assert before.get("boundingBox") == after.get("boundingBox")
        evidence["passed"] = True
    finally:
        (out / "evidence.json").write_text(json.dumps(evidence, indent=2, ensure_ascii=False), encoding="utf-8")
        client.close()
    print(json.dumps(evidence, indent=2, ensure_ascii=False))


if __name__ == "__main__":
    main()
