using System;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed class SectionBoxService
    {
        public SectionBoxResponse Execute(Document document, SectionBoxRequest request)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var error = SectionBoxWriteRules.Describe(request);
            if (error != null) throw new AgentCommandException(ErrorCodes.SchemaViolation, error);
            var operation = request.Operation.ToLowerInvariant();
            var view = document.ActiveView;
            if (view == null)
                throw new AgentCommandException(ErrorCodes.NoActiveView, "The active document has no active view.");
            SectionBoxGeometry planned = null;
            string plannedJson;
            if (operation == "clear")
            {
                var currentJson = view.GetClippingPlanes();
                if (!SectionPlaneClippingJson.Read(currentJson).Readable)
                    throw new AgentCommandException(ErrorCodes.CommandFailed,
                        "The active view's clipping could not be read; nothing was changed.");
                plannedJson = SectionPlaneClippingJson.Disable(currentJson);
            }
            else
            {
                try
                {
                    var units = SectionBoxCaptureService.NormalizeUnits(document.Units);
                    planned = request.Box ?? SectionBoxWriteRules.FromMinMax(request.Min, request.Max, units);
                    SectionBoxGeometryRules.Validate(planned);
                    if (!string.Equals(planned.DocumentUnits, units, StringComparison.Ordinal))
                        throw new ArgumentException("box.documentUnits must match the document units.");
                    var q = SectionBoxWriteRules.QuaternionFromAxes(planned.Axes);
                    using (var rotation = new Rotation3D(q[0], q[1], q[2], q[3]))
                    {
                        var euler = rotation.ToEulerAngles();
                        plannedJson = SectionBoxWriteRules.BuildClippingJson(planned, euler.X, euler.Y, euler.Z);
                    }
                }
                catch (ArgumentException ex)
                {
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
                }
            }
            var result = new SectionBoxResponse
            {
                Operation = operation, Apply = request.Apply == true, Enabled = operation == "set", Box = planned,
                Message = "Dry-run: section box was not changed.",
            };
            if (!result.Apply) return result;

            if (!view.TrySetClippingPlanes(plannedJson))
                view.SetClippingPlanes(plannedJson);
            result.Applied = true;
            view.RequestDelayedRedraw(ViewRedrawRequests.All);
            var readbackJson = view.GetClippingPlanes();
            var state = SectionPlaneClippingJson.Read(readbackJson);
            result.Enabled = state.Enabled;
            result.ReadbackMatches = false;
            if (operation == "clear")
                result.ReadbackMatches = state.Readable && !state.Enabled;
            else
            {
                try
                {
                    result.Readback = ReadBox(readbackJson, planned.DocumentUnits);
                    result.ReadbackMatches = Matches(planned, result.Readback);
                }
                catch (SectionBoxParseException ex) { result.Warnings.Add(ex.Message); }
                catch (ArgumentException ex) { result.Warnings.Add(ex.Message); }
            }
            result.Message = result.ReadbackMatches == true ? "Section box applied and read back." :
                "Section box was written, but readback differs from the requested state.";
            if (result.ReadbackMatches != true) result.Warnings.Add(result.Message);
            return result;
        }

        private static SectionBoxGeometry ReadBox(string json, string units)
        {
            var parsed = SectionBoxClippingParser.Parse(json);
            using (var rotation = Rotation3D.CreateFromEulerAngles(parsed.EulerRadians.X, parsed.EulerRadians.Y, parsed.EulerRadians.Z))
            {
                var geometry = SectionBoxWriteRules.FromMinMax(parsed.Minimum, parsed.Maximum, units);
                geometry.Axes = SectionBoxGeometryRules.AxesFromQuaternion(rotation.A, rotation.B, rotation.C, rotation.D);
                SectionBoxGeometryRules.Validate(geometry);
                return geometry;
            }
        }

        private static bool Matches(SectionBoxGeometry expected, SectionBoxGeometry actual)
        {
            var size = Math.Max(expected.HalfExtents.X, Math.Max(expected.HalfExtents.Y, expected.HalfExtents.Z));
            var tolerance = size * 1e-6;
            if (!Near(expected.Center, actual.Center, tolerance) || !Near(expected.HalfExtents, actual.HalfExtents, tolerance))
                return false;
            for (var i = 0; i < 3; i++)
                if (!Near(expected.Axes[i], actual.Axes[i], 1e-6)) return false;
            return true;
        }

        private static bool Near(BoxVector3 a, BoxVector3 b, double tolerance) =>
            Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance && Math.Abs(a.Z - b.Z) <= tolerance;
    }
}
