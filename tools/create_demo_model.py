"""Create the public demo fixture; requires ezdxf 1.4.4 (pip install ezdxf==1.4.4).

Only neutral, generated geometry is written. No Navisworks process is launched.
The two AABB intersections are design intent; native Clash Detective must confirm
the actual results after import. Coordinates and DXF insertion units are mm.
"""

import argparse
import json
from pathlib import Path

import ezdxf


def objects():
    result = []

    def add(system, low, high):
        index = sum(item[1] == system for item in result) + 1
        result.append((f"{system.upper()}-{index:02d}", system, low, high))

    for y in (-2500, 2500):
        for x in (-4500, -1500, 1500, 4500):
            add("Structure", (x - 200, y - 200, 0), (x + 200, y + 200, 5000))
    for left, right in ((-2300, -500), (500, 2300)):
        add("HVAC", (left, -2700, 3400), (right, -2300, 3800))
    for y in (-500, 500, 1500):
        for left, right in ((-3500, -100), (100, 3500)):
            add("HVAC", (left, y - 200, 3400), (right, y + 200, 3800))
    for y in (-1000, 1000):
        for x in (-3500, -1750, 0, 1750):
            add("Electrical", (x, y - 100, 4200), (x + 1500, y + 100, 4300))
    return result


def faces(low, high):
    x, y, z = low
    X, Y, Z = high
    points = [(x, y, z), (X, y, z), (X, Y, z), (x, Y, z),
              (x, y, Z), (X, y, Z), (X, Y, Z), (x, Y, Z)]
    # Outward-wound, closed six-face box; one polyface per named block.
    indices = [(3, 2, 1, 0), (4, 5, 6, 7), (0, 1, 5, 4),
               (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    return [[points[index] for index in face] for face in indices]


def planned_clashes(items):
    return [(a[0], b[0]) for a in items for b in items
            if a[1] == "HVAC" and b[1] == "Structure"
            and all(min(a[3][axis], b[3][axis]) > max(a[2][axis], b[2][axis])
                    for axis in range(3))]


def create(path):
    if path.suffix.lower() != ".dxf":
        raise ValueError("Output must end in .dxf")
    items = objects()
    expected = [("HVAC-01", "STRUCTURE-02"), ("HVAC-02", "STRUCTURE-03")]
    if len(items) != 24 or planned_clashes(items) != expected:
        raise ValueError("Demo geometry no longer matches the capture scenario")
    doc = ezdxf.new("R2013")
    doc.units = ezdxf.units.MM
    for system, color in (("HVAC", 4), ("Electrical", 2), ("Structure", 8)):
        doc.layers.new(system, dxfattribs={"color": color})
    for name, system, low, high in items:
        block = doc.blocks.new(name)
        mesh = block.add_polyface(dxfattribs={"layer": system})
        mesh.append_faces(faces(low, high))
        doc.modelspace().add_blockref(name, (0, 0, 0), dxfattribs={"layer": system})
    path.parent.mkdir(parents=True, exist_ok=True)
    # Exclusive creation avoids replacing a manually adjusted demo model.
    with path.open("x", encoding="utf-8") as stream:
        doc.write(stream)
    imported = ezdxf.readfile(path)
    inserts = list(imported.modelspace().query("INSERT"))
    if len(inserts) != 24 or {item.dxf.name for item in inserts} != {item[0] for item in items}:
        raise ValueError("Round-trip block names differ")
    round_trip = []
    for item in inserts:
        meshes = list(imported.blocks[item.dxf.name].query("POLYLINE"))
        if len(meshes) != 1 or not meshes[0].is_poly_face_mesh:
            raise ValueError("Expected one closed box mesh per block")
        if len(list(meshes[0].faces())) != 6:
            raise ValueError("Round-trip box face count differs")
        vertices = [vertex.dxf.location for vertex in meshes[0].vertices
                    if not vertex.is_face_record]
        if len(vertices) != 8:
            raise ValueError("Round-trip box vertex count differs")
        low = tuple(min(point[axis] for point in vertices) for axis in range(3))
        high = tuple(max(point[axis] for point in vertices) for axis in range(3))
        round_trip.append((item.dxf.name, item.dxf.layer, low, high))
    if round_trip != items or planned_clashes(round_trip) != expected:
        raise ValueError("Round-trip geometry differs from the capture scenario")
    if imported.units != ezdxf.units.MM or imported.audit().has_errors:
        raise ValueError("Invalid DXF or unit mismatch")
    print(json.dumps({"path": str(path.resolve()), "objects": len(inserts),
                      "units": "mm", "plannedClashes": expected}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, help="New .dxf path outside the repository")
    create(parser.parse_args().output)
