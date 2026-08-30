using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NavisHelper.Agent.Contracts
{
    public static class WorldMarkerInputPolicy
    {
        public const int MaxBatchSize = 100;
        public const int MaxNameLength = 128;
        public const int MaxLabelLength = 255;
        public const int MaxEncodedLabelLength = 255;
        public const double DefaultSize = 1.0;
        public const double MinSize = 1e-6;
        public const double MaxSize = 1e9;
        public const double MaxAbsoluteCoordinate = 1e12;

        private static readonly HashSet<string> SupportedStyles = new HashSet<string>(StringComparer.Ordinal)
        {
            WorldMarkerStyles.Target,
            WorldMarkerStyles.Cross,
            WorldMarkerStyles.Circle,
            WorldMarkerStyles.Pin,
            WorldMarkerStyles.Pole,
            WorldMarkerStyles.Box,
        };

        public static WorldMarkerBatchPlan NormalizeBatch(WorldMarkerCreateRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (request.Markers == null || request.Markers.Count == 0)
                throw new ArgumentException("markers must contain at least one marker.", nameof(request));
            if (request.Markers.Count > MaxBatchSize)
                throw new ArgumentException("markers must not contain more than " + MaxBatchSize.ToString(CultureInfo.InvariantCulture) + " items.", nameof(request));

            var documentUnits = WorldMarkerDxfBuilder.NormalizeDocumentUnits(request.DocumentUnits);
            var result = new WorldMarkerBatchPlan
            {
                DocumentUnits = documentUnits,
                ReplaceExisting = request.ReplaceExisting == true,
                Apply = request.Apply == true,
            };
            var markerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < request.Markers.Count; index++)
            {
                var normalized = NormalizeMarker(request.Markers[index], index);
                if (!markerIds.Add(normalized.MarkerId))
                {
                    throw new ArgumentException(
                        "markers contains duplicate names after normalization: " + normalized.Name + ".",
                        nameof(request));
                }
                result.Markers.Add(normalized);
            }

            return result;
        }

        public static WorldMarkerPlanItem NormalizeMarker(WorldMarkerSpec marker, int index = 0)
        {
            if (marker == null)
                throw new ArgumentException("markers[" + index.ToString(CultureInfo.InvariantCulture) + "] is required.");

            var name = NormalizeText(marker.Name, "name", MaxNameLength, false);
            var label = NormalizeText(marker.Label, "label", MaxLabelLength, true);
            var style = string.IsNullOrWhiteSpace(marker.Style)
                ? WorldMarkerStyles.Target
                : marker.Style.Trim().ToLowerInvariant();
            if (!SupportedStyles.Contains(style))
                throw new ArgumentException("style must be target, cross, circle, pin, pole, or box.");

            EnsureFinite(marker.X, "x");
            EnsureFinite(marker.Y, "y");
            var z = marker.Z.GetValueOrDefault(0.0);
            EnsureFinite(z, "z");
            var size = marker.Size.GetValueOrDefault(DefaultSize);
            EnsureFinite(size, "size");
            if (size < MinSize || size > MaxSize)
            {
                throw new ArgumentException(
                    "size must be between " + MinSize.ToString("R", CultureInfo.InvariantCulture) + " and " +
                    MaxSize.ToString("R", CultureInfo.InvariantCulture) + " document units.");
            }

            var color = marker.Color == null
                ? new WorldMarkerColor { R = 255, G = 0, B = 0 }
                : new WorldMarkerColor { R = marker.Color.R, G = marker.Color.G, B = marker.Color.B };
            ValidateColor(color);

            var poleRequested = style == WorldMarkerStyles.Pole ||
                (marker.Pole != null &&
                 (marker.Pole.Enabled == true ||
                  (!marker.Pole.Enabled.HasValue && (marker.Pole.BaseZ.HasValue || marker.Pole.TopZ.HasValue))));
            var poleBaseZ = marker.Pole == null ? 0.0 : marker.Pole.BaseZ.GetValueOrDefault(0.0);
            var poleTopZ = marker.Pole == null ? z : marker.Pole.TopZ.GetValueOrDefault(z);
            EnsureFinite(poleBaseZ, "pole.baseZ");
            EnsureFinite(poleTopZ, "pole.topZ");
            if (poleRequested && Math.Abs(poleTopZ - poleBaseZ) < MinSize)
            {
                var hasExplicitEndpoint = marker.Pole != null && (marker.Pole.BaseZ.HasValue || marker.Pole.TopZ.HasValue);
                if (hasExplicitEndpoint)
                {
                    throw new ArgumentException(
                        "Explicit pole baseZ and topZ must differ by at least " +
                        MinSize.ToString("R", CultureInfo.InvariantCulture) + " document units.");
                }
                poleTopZ = poleBaseZ + size;
            }

            var result = new WorldMarkerPlanItem
            {
                MarkerId = CreateMarkerId(name),
                Name = name,
                X = marker.X,
                Y = marker.Y,
                Z = z,
                Style = style,
                Size = size,
                Color = color,
                Label = label,
                PoleEnabled = poleRequested,
                PoleBaseZ = poleBaseZ,
                PoleTopZ = poleTopZ,
            };
            ValidateNumericBounds(result);
            WorldMarkerDxfBuilder.EncodeText(result.Label);
            return result;
        }

        public static void ValidateNumericBounds(WorldMarkerPlanItem marker)
        {
            if (marker == null)
                throw new ArgumentNullException(nameof(marker));
            ValidateBoundedCoordinate(marker.X, "x");
            ValidateBoundedCoordinate(marker.Y, "y");
            ValidateBoundedCoordinate(marker.Z, "z");
            EnsureFinite(marker.Size, "size");
            if (marker.Size < MinSize || marker.Size > MaxSize)
            {
                throw new ArgumentException(
                    "size must be between " + MinSize.ToString("R", CultureInfo.InvariantCulture) + " and " +
                    MaxSize.ToString("R", CultureInfo.InvariantCulture) + " document units.");
            }
            ValidateBoundedCoordinate(marker.PoleBaseZ, "pole.baseZ");
            ValidateBoundedCoordinate(marker.PoleTopZ, "pole.topZ");

            // This conservative envelope covers every v1-derived LINE/CIRCLE/TEXT coordinate,
            // including box half-extents and the +0.6*size text insertion offset.
            ValidateBoundedCoordinate(marker.X - marker.Size, "derived x min");
            ValidateBoundedCoordinate(marker.X + marker.Size, "derived x max");
            ValidateBoundedCoordinate(marker.Y - marker.Size, "derived y min");
            ValidateBoundedCoordinate(marker.Y + marker.Size, "derived y max");
            ValidateBoundedCoordinate(marker.Z - marker.Size, "derived z min");
            ValidateBoundedCoordinate(marker.Z + marker.Size, "derived z max");
        }

        public static string CreateMarkerId(string normalizedName)
        {
            if (string.IsNullOrWhiteSpace(normalizedName))
                throw new ArgumentException("A normalized marker name is required.", nameof(normalizedName));

            var canonical = normalizedName.Trim().Normalize(NormalizationForm.FormC).ToUpperInvariant();
            byte[] digest;
            using (var sha256 = SHA256.Create())
                digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical));

            var result = new StringBuilder("wm-", 19);
            for (var i = 0; i < 8; i++)
                result.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
            return result.ToString();
        }

        private static string NormalizeText(string value, string fieldName, int maxLength, bool allowEmpty)
        {
            var normalized = (value ?? string.Empty).Trim().Normalize(NormalizationForm.FormC);
            if (!allowEmpty && normalized.Length == 0)
                throw new ArgumentException(fieldName + " is required.");
            if (normalized.Length > maxLength)
                throw new ArgumentException(fieldName + " must not exceed " + maxLength.ToString(CultureInfo.InvariantCulture) + " characters.");
            if (normalized.Any(character => char.IsControl(character) || character == '\r' || character == '\n'))
                throw new ArgumentException(fieldName + " must not contain control characters or line breaks.");
            if (normalized.Any(char.IsSurrogate))
                throw new ArgumentException(fieldName + " supports Unicode BMP characters only in DXF v1.");
            return normalized;
        }

        private static void ValidateColor(WorldMarkerColor color)
        {
            if (color.R < 0 || color.R > 255 || color.G < 0 || color.G > 255 || color.B < 0 || color.B > 255)
                throw new ArgumentException("color channels must be integers from 0 to 255.");
        }

        private static void EnsureFinite(double value, string fieldName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException(fieldName + " must be a finite number.");
        }

        private static void ValidateBoundedCoordinate(double value, string fieldName)
        {
            EnsureFinite(value, fieldName);
            if (Math.Abs(value) > MaxAbsoluteCoordinate)
            {
                throw new ArgumentException(
                    fieldName + " must have an absolute value no greater than " +
                    MaxAbsoluteCoordinate.ToString("R", CultureInfo.InvariantCulture) + " document units.");
            }
        }
    }
}
