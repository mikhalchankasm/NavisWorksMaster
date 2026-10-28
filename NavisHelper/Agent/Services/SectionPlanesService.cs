using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed class SectionPlanesService
    {
        public SectionPlanesResponse Execute(Document document, SectionPlanesRequest request)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            request = request ?? new SectionPlanesRequest();
            var operation = (request.Operation ?? "get").ToLowerInvariant();
            if (operation != "get" && operation != "set" && operation != "clear")
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "operation must be get, set or clear");
            var view = document.ActiveView;
            if (view == null)
                throw new AgentCommandException(ErrorCodes.NoActiveView, "The active document has no active view.");
            var json = view.GetClippingPlanes();
            var current = SectionPlaneClippingJson.Read(json);
            if (operation == "get") return Response(operation, current);
            if (!current.Readable)
                throw new AgentCommandException(ErrorCodes.CommandFailed,
                    "The active view's clipping could not be read; nothing was changed.");

            var requested = new List<SectionPlaneState>();
            if (operation == "set")
            {
                BoxVector3 minimum = null, maximum = null;
                if ((request.Planes ?? new List<SectionPlaneSpec>()).Any(spec => spec?.FromSelection != null) &&
                    document.CurrentSelection.SelectedItems.Count > 0)
                {
                    var bounds = document.CurrentSelection.SelectedItems.BoundingBox();
                    if (bounds != null && !bounds.IsEmpty)
                    {
                        minimum = new BoxVector3 { X = bounds.Min.X, Y = bounds.Min.Y, Z = bounds.Min.Z };
                        maximum = new BoxVector3 { X = bounds.Max.X, Y = bounds.Max.Y, Z = bounds.Max.Z };
                    }
                }
                var indices = new HashSet<int>();
                foreach (var spec in request.Planes ?? new List<SectionPlaneSpec>())
                {
                    string error;
                    var resolved = SectionPlaneRules.Resolve(spec,
                        current.Planes.FirstOrDefault(plane => plane.Index == spec?.Index),
                        minimum, maximum, SectionBoxHelper.MmToDocUnits(1.0), out error);
                    if (resolved == null)
                        throw new AgentCommandException(ErrorCodes.SchemaViolation, error);
                    if (!indices.Add(resolved.Index))
                        throw new AgentCommandException(ErrorCodes.SchemaViolation, "Each plane index must appear only once.");
                    requested.Add(resolved);
                }
            }

            var plannedJson = operation == "clear" ? SectionPlaneClippingJson.Disable(json) :
                SectionPlaneClippingJson.Write(json, requested, request.Linked ?? current.Linked, enabled: true);
            var planned = SectionPlaneClippingJson.Read(plannedJson);
            if (request.Apply != true)
            {
                var preview = Response(operation, planned);
                preview.Message = "Dry-run: section planes were not changed.";
                return preview;
            }
            if (!view.TrySetClippingPlanes(plannedJson))
                view.SetClippingPlanes(plannedJson);
            view.RequestDelayedRedraw(ViewRedrawRequests.All);
            var readback = SectionPlaneClippingJson.Read(view.GetClippingPlanes());
            var result = Response(operation, readback);
            result.Applied = true;
            result.ReadbackMatches = readback.Readable && (operation == "clear" ? !readback.Enabled :
                readback.Enabled && readback.Linked == planned.Linked && requested.All(expected =>
                    readback.Planes.Any(actual => Matches(expected, actual))));
            result.Message = result.ReadbackMatches == true ? "Section planes applied and read back." :
                "Section planes were written, but readback differs from the requested state.";
            if (result.ReadbackMatches != true) result.Warnings.Add(result.Message);
            return result;
        }

        private static bool Matches(SectionPlaneState expected, SectionPlaneState actual) =>
            expected.Index == actual.Index && expected.Enabled == actual.Enabled &&
            Math.Abs(expected.Distance - actual.Distance) <= 1e-6 &&
            Math.Abs(expected.Normal.X - actual.Normal.X) <= 1e-6 &&
            Math.Abs(expected.Normal.Y - actual.Normal.Y) <= 1e-6 &&
            Math.Abs(expected.Normal.Z - actual.Normal.Z) <= 1e-6;

        private static SectionPlanesResponse Response(string operation, ParsedSectionPlanes state) => new SectionPlanesResponse
        {
            Operation = operation, Mode = state.Mode, Enabled = state.Enabled, Linked = state.Linked,
            Planes = state.Planes.Select(plane => new SectionPlaneInfo
            {
                Index = plane.Index, Enabled = plane.Enabled, Normal = plane.Normal,
                Distance = plane.Distance, Direction = SectionPlaneRules.DirectionName(plane.Normal),
            }).ToList(),
        };
    }
}
