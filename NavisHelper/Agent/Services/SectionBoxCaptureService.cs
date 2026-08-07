using System;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed class SectionBoxCaptureService
    {
        public GetCurrentSectionBoxResponse Capture(Document document, GetCurrentSectionBoxRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (document.ActiveView == null)
                throw new AgentCommandException(ErrorCodes.NoActiveView, "The active document has no active view.");

            ParsedSectionBox parsed;
            try
            {
                parsed = SectionBoxClippingParser.Parse(document.ActiveView.GetClippingPlanes());
            }
            catch (SectionBoxParseException ex)
            {
                throw new AgentCommandException(ex.ErrorCode, ex.Message, true);
            }

            Rotation3D rotation = null;
            try
            {
                rotation = Rotation3D.CreateFromEulerAngles(
                    parsed.EulerRadians.X,
                    parsed.EulerRadians.Y,
                    parsed.EulerRadians.Z);
                var geometry = new SectionBoxGeometry
                {
                    FormatVersion = SectionBoxGeometryRules.CurrentFormatVersion,
                    CoordinateSpace = SectionBoxGeometryRules.DocumentGlobal,
                    DocumentUnits = NormalizeUnits(document.Units),
                    Center = new BoxVector3
                    {
                        X = (parsed.Minimum.X + parsed.Maximum.X) * 0.5,
                        Y = (parsed.Minimum.Y + parsed.Maximum.Y) * 0.5,
                        Z = (parsed.Minimum.Z + parsed.Maximum.Z) * 0.5,
                    },
                    HalfExtents = new BoxVector3
                    {
                        X = (parsed.Maximum.X - parsed.Minimum.X) * 0.5,
                        Y = (parsed.Maximum.Y - parsed.Minimum.Y) * 0.5,
                        Z = (parsed.Maximum.Z - parsed.Minimum.Z) * 0.5,
                    },
                    Axes = SectionBoxGeometryRules.AxesFromQuaternion(rotation.A, rotation.B, rotation.C, rotation.D),
                };
                SectionBoxGeometryRules.Validate(geometry);
                return new GetCurrentSectionBoxResponse
                {
                    Enabled = true,
                    Mode = "oriented_box",
                    Box = geometry,
                };
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(
                    ErrorCodes.SectionBoxPayloadUnsupported,
                    "The active Section Box rotation or geometry is invalid: " + ex.Message,
                    true);
            }
            finally
            {
                if (rotation != null)
                    rotation.Dispose();
            }
        }

        internal static string NormalizeUnits(Units units)
        {
            switch (units)
            {
                case Units.Meters: return "meters";
                case Units.Centimeters: return "centimeters";
                case Units.Millimeters: return "millimeters";
                case Units.Feet: return "feet";
                case Units.Inches: return "inches";
                case Units.Yards: return "yards";
                case Units.Kilometers: return "kilometers";
                case Units.Miles: return "miles";
                case Units.Micrometers: return "micrometers";
                case Units.Mils: return "mils";
                case Units.Microinches: return "microinches";
                default:
                    throw new AgentCommandException(
                        ErrorCodes.SectionBoxPayloadUnsupported,
                        "The active document uses unsupported units.",
                        true);
            }
        }
    }
}
