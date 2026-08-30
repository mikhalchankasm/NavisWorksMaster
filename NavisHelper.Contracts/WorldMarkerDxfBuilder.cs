using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    public static class WorldMarkerDxfBuilder
    {
        private static readonly Dictionary<string, UnitDefinition> Units =
            new Dictionary<string, UnitDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                { "inches", new UnitDefinition("Inches", 1) },
                { "feet", new UnitDefinition("Feet", 2) },
                { "miles", new UnitDefinition("Miles", 3) },
                { "millimeters", new UnitDefinition("Millimeters", 4) },
                { "centimeters", new UnitDefinition("Centimeters", 5) },
                { "meters", new UnitDefinition("Meters", 6) },
                { "kilometers", new UnitDefinition("Kilometers", 7) },
                { "microinches", new UnitDefinition("Microinches", 8) },
                { "mils", new UnitDefinition("Mils", 9) },
                { "yards", new UnitDefinition("Yards", 10) },
                { "micrometers", new UnitDefinition("Micrometers", 13) },
                { "microns", new UnitDefinition("Micrometers", 13) },
            };

        public static string Build(WorldMarkerPlanItem marker, string documentUnits)
        {
            if (marker == null)
                throw new ArgumentNullException(nameof(marker));
            ValidatePlanItem(marker);
            var units = ResolveUnits(documentUnits);
            var builder = new StringBuilder(4096);

            Pair(builder, 0, "SECTION");
            Pair(builder, 2, "HEADER");
            Pair(builder, 9, "$ACADVER");
            Pair(builder, 1, "AC1027");
            Pair(builder, 9, "$INSUNITS");
            Pair(builder, 70, units.InsUnitsCode);
            Pair(builder, 0, "ENDSEC");
            Pair(builder, 0, "SECTION");
            Pair(builder, 2, "ENTITIES");
            Pair(builder, 999, "NavisHelper World Marker " + marker.MarkerId);

            AppendStyle(builder, marker);
            if (marker.PoleEnabled && marker.Style != WorldMarkerStyles.Pole)
                AppendLine(builder, marker, marker.X, marker.Y, marker.PoleBaseZ, marker.X, marker.Y, marker.PoleTopZ);
            if (!string.IsNullOrEmpty(marker.Label))
                AppendText(builder, marker);

            Pair(builder, 0, "ENDSEC");
            Pair(builder, 0, "EOF");
            return builder.ToString();
        }

        public static string NormalizeDocumentUnits(string value)
        {
            return ResolveUnits(value).CanonicalName;
        }

        public static int GetInsUnitsCode(string value)
        {
            return ResolveUnits(value).InsUnitsCode;
        }

        public static string EncodeText(string value)
        {
            if (value == null)
                return string.Empty;

            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (char.IsControl(character) || character == '\r' || character == '\n')
                    throw new ArgumentException("DXF text must not contain control characters or line breaks.", nameof(value));
                if (char.IsSurrogate(character))
                    throw new ArgumentException("DXF v1 text supports Unicode BMP characters only.", nameof(value));
                if (character > 0x7e || character < 0x20 || character == '\\' || character == '%')
                {
                    builder.Append("\\U+");
                    builder.Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append(character);
                }
            }
            var encoded = builder.ToString();
            if (encoded.Length > WorldMarkerInputPolicy.MaxEncodedLabelLength)
            {
                throw new ArgumentException(
                    "DXF group-code 1 text must encode to no more than " +
                    WorldMarkerInputPolicy.MaxEncodedLabelLength.ToString(CultureInfo.InvariantCulture) +
                    " ASCII characters.",
                    nameof(value));
            }
            return encoded;
        }

        private static void AppendStyle(StringBuilder builder, WorldMarkerPlanItem marker)
        {
            var half = marker.Size / 2.0;
            switch (marker.Style)
            {
                case WorldMarkerStyles.Target:
                    AppendCircle(builder, marker, marker.X, marker.Y, marker.Z, half);
                    AppendOrthogonalCross(builder, marker, half);
                    break;
                case WorldMarkerStyles.Cross:
                    AppendOrthogonalCross(builder, marker, half);
                    break;
                case WorldMarkerStyles.Circle:
                    AppendCircle(builder, marker, marker.X, marker.Y, marker.Z, half);
                    break;
                case WorldMarkerStyles.Pin:
                    AppendPin(builder, marker, half);
                    break;
                case WorldMarkerStyles.Pole:
                    AppendLine(builder, marker, marker.X, marker.Y, marker.PoleBaseZ, marker.X, marker.Y, marker.PoleTopZ);
                    AppendCircle(builder, marker, marker.X, marker.Y, marker.Z, half * 0.35);
                    AppendOrthogonalCross(builder, marker, half);
                    break;
                case WorldMarkerStyles.Box:
                    AppendBox(builder, marker, half);
                    break;
                default:
                    throw new ArgumentException("Unsupported normalized world marker style: " + marker.Style + ".", nameof(marker));
            }
        }

        private static void AppendOrthogonalCross(StringBuilder builder, WorldMarkerPlanItem marker, double half)
        {
            AppendLine(builder, marker, marker.X - half, marker.Y, marker.Z, marker.X + half, marker.Y, marker.Z);
            AppendLine(builder, marker, marker.X, marker.Y - half, marker.Z, marker.X, marker.Y + half, marker.Z);
        }

        private static void AppendPin(StringBuilder builder, WorldMarkerPlanItem marker, double half)
        {
            var headRadius = half * 0.4;
            var headY = marker.Y + half * 0.2;
            AppendCircle(builder, marker, marker.X, headY, marker.Z, headRadius);
            AppendLine(builder, marker, marker.X, headY - headRadius, marker.Z, marker.X, marker.Y - half, marker.Z);
            AppendLine(builder, marker, marker.X - headRadius * 0.35, marker.Y - half * 0.75, marker.Z, marker.X, marker.Y - half, marker.Z);
            AppendLine(builder, marker, marker.X + headRadius * 0.35, marker.Y - half * 0.75, marker.Z, marker.X, marker.Y - half, marker.Z);
        }

        private static void AppendBox(StringBuilder builder, WorldMarkerPlanItem marker, double half)
        {
            var x0 = marker.X - half;
            var x1 = marker.X + half;
            var y0 = marker.Y - half;
            var y1 = marker.Y + half;
            var z0 = marker.Z - half;
            var z1 = marker.Z + half;

            AppendRectangle(builder, marker, x0, y0, x1, y1, z0);
            AppendRectangle(builder, marker, x0, y0, x1, y1, z1);
            AppendLine(builder, marker, x0, y0, z0, x0, y0, z1);
            AppendLine(builder, marker, x1, y0, z0, x1, y0, z1);
            AppendLine(builder, marker, x1, y1, z0, x1, y1, z1);
            AppendLine(builder, marker, x0, y1, z0, x0, y1, z1);
        }

        private static void AppendRectangle(StringBuilder builder, WorldMarkerPlanItem marker, double x0, double y0, double x1, double y1, double z)
        {
            AppendLine(builder, marker, x0, y0, z, x1, y0, z);
            AppendLine(builder, marker, x1, y0, z, x1, y1, z);
            AppendLine(builder, marker, x1, y1, z, x0, y1, z);
            AppendLine(builder, marker, x0, y1, z, x0, y0, z);
        }

        private static void AppendLine(StringBuilder builder, WorldMarkerPlanItem marker, double x0, double y0, double z0, double x1, double y1, double z1)
        {
            EntityStart(builder, "LINE", marker.Color);
            Pair(builder, 10, x0);
            Pair(builder, 20, y0);
            Pair(builder, 30, z0);
            Pair(builder, 11, x1);
            Pair(builder, 21, y1);
            Pair(builder, 31, z1);
        }

        private static void AppendCircle(StringBuilder builder, WorldMarkerPlanItem marker, double x, double y, double z, double radius)
        {
            EntityStart(builder, "CIRCLE", marker.Color);
            Pair(builder, 10, x);
            Pair(builder, 20, y);
            Pair(builder, 30, z);
            Pair(builder, 40, radius);
            Pair(builder, 210, 0.0);
            Pair(builder, 220, 0.0);
            Pair(builder, 230, 1.0);
        }

        private static void AppendText(StringBuilder builder, WorldMarkerPlanItem marker)
        {
            EntityStart(builder, "TEXT", marker.Color);
            Pair(builder, 10, marker.X + marker.Size * 0.6);
            Pair(builder, 20, marker.Y + marker.Size * 0.6);
            Pair(builder, 30, marker.Z);
            Pair(builder, 40, marker.Size * 0.25);
            Pair(builder, 1, EncodeText(marker.Label));
            Pair(builder, 7, "STANDARD");
            Pair(builder, 50, 0.0);
            Pair(builder, 210, 0.0);
            Pair(builder, 220, 0.0);
            Pair(builder, 230, 1.0);
        }

        private static void EntityStart(StringBuilder builder, string type, WorldMarkerColor color)
        {
            Pair(builder, 0, type);
            Pair(builder, 8, "0");
            Pair(builder, 420, (color.R << 16) | (color.G << 8) | color.B);
        }

        private static void ValidatePlanItem(WorldMarkerPlanItem marker)
        {
            if (!WorldMarkerArtifactPathPolicy.IsMarkerId(marker.MarkerId))
                throw new ArgumentException("markerId is not a generated world-marker ID.", nameof(marker));
            if (marker.Style != WorldMarkerStyles.Target && marker.Style != WorldMarkerStyles.Cross &&
                marker.Style != WorldMarkerStyles.Circle && marker.Style != WorldMarkerStyles.Pin &&
                marker.Style != WorldMarkerStyles.Pole && marker.Style != WorldMarkerStyles.Box)
            {
                throw new ArgumentException("The normalized marker style is unsupported.", nameof(marker));
            }
            WorldMarkerInputPolicy.ValidateNumericBounds(marker);
            if (marker.Color == null || marker.Color.R < 0 || marker.Color.R > 255 ||
                marker.Color.G < 0 || marker.Color.G > 255 || marker.Color.B < 0 || marker.Color.B > 255)
            {
                throw new ArgumentException("color channels must be integers from 0 to 255.", nameof(marker));
            }
            if ((marker.PoleEnabled || marker.Style == WorldMarkerStyles.Pole) &&
                Math.Abs(marker.PoleTopZ - marker.PoleBaseZ) < WorldMarkerInputPolicy.MinSize)
            {
                throw new ArgumentException(
                    "pole baseZ and topZ must differ by at least " +
                    WorldMarkerInputPolicy.MinSize.ToString("R", CultureInfo.InvariantCulture) +
                    " document units when the pole is enabled.",
                    nameof(marker));
            }
            if ((marker.Label ?? string.Empty).Length > WorldMarkerInputPolicy.MaxLabelLength)
                throw new ArgumentException("label exceeds the DXF v1 length limit.", nameof(marker));
            EncodeText(marker.Label ?? string.Empty);
        }

        private static void Pair(StringBuilder builder, int code, string value)
        {
            builder.Append(code.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            builder.Append(value ?? string.Empty).Append("\r\n");
        }

        private static void Pair(StringBuilder builder, int code, int value)
        {
            Pair(builder, code, value.ToString(CultureInfo.InvariantCulture));
        }

        private static void Pair(StringBuilder builder, int code, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("DXF coordinates must be finite numbers.");
            Pair(builder, code, value.ToString("R", CultureInfo.InvariantCulture));
        }

        private static UnitDefinition ResolveUnits(string value)
        {
            var key = NormalizeUnitKey(value);
            UnitDefinition definition;
            if (!Units.TryGetValue(key, out definition))
            {
                throw new ArgumentException(
                    "documentUnits must be Inches, Feet, Miles, Millimeters, Centimeters, Meters, Kilometers, Microinches, Mils, Yards, or Micrometers.",
                    nameof(value));
            }
            return definition;
        }

        private static string NormalizeUnitKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("documentUnits is required.", nameof(value));
            return value.Trim().Replace("_", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();
        }

        private sealed class UnitDefinition
        {
            public UnitDefinition(string canonicalName, int insUnitsCode)
            {
                CanonicalName = canonicalName;
                InsUnitsCode = insUnitsCode;
            }

            public string CanonicalName { get; private set; }
            public int InsUnitsCode { get; private set; }
        }
    }
}
