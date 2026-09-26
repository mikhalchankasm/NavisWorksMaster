using System;
using System.Collections.Generic;
using System.Globalization;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>A point in world space, in document units.</summary>
    public readonly struct WorldMarkerPoint
    {
        public WorldMarkerPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }
        public double Y { get; }
        public double Z { get; }
    }

    /// <summary>A straight world-space segment, projected to the screen every frame.</summary>
    public readonly struct WorldMarkerSegment
    {
        public WorldMarkerSegment(WorldMarkerPoint start, WorldMarkerPoint end)
        {
            Start = start;
            End = end;
        }

        public WorldMarkerPoint Start { get; }
        public WorldMarkerPoint End { get; }
    }

    /// <summary>What one overlay marker contributes to a frame.</summary>
    public sealed class WorldMarkerOverlayFigure
    {
        internal WorldMarkerOverlayFigure(
            IReadOnlyList<WorldMarkerSegment> segments,
            WorldMarkerPoint headAnchor,
            WorldMarkerPoint labelAnchor)
        {
            Segments = segments;
            HeadAnchor = headAnchor;
            LabelAnchor = labelAnchor;
        }

        /// <summary>The world-space segments to project; empty when the marker has neither a world size nor a pole.</summary>
        public IReadOnlyList<WorldMarkerSegment> Segments { get; }

        /// <summary>The world point the pixel head is centred on: the top of a pin stem, otherwise the marker anchor.</summary>
        public WorldMarkerPoint HeadAnchor { get; }

        /// <summary>The world point the label is drawn beside, offset in pixels: the highest point of the marker.</summary>
        public WorldMarkerPoint LabelAnchor { get; }
    }

    /// <summary>
    /// The pure world geometry of overlay world markers. Everything a marker draws with a size in document
    /// units is a segment here; the pixel head and the label text belong to the renderer.
    /// </summary>
    public static class WorldMarkerOverlayGeometry
    {
        /// <summary>The fixed number of segments a target or circle ring is built from.</summary>
        public const int RingSegmentCount = 32;

        /// <summary>Builds the figure of a validated marker, taking the marker's own size as its world size.</summary>
        public static WorldMarkerOverlayFigure Build(WorldMarkerPlanItem marker)
        {
            if (marker == null)
                throw new ArgumentNullException(nameof(marker));
            return Build(marker, marker.Size);
        }

        /// <summary>
        /// Builds the figure of a validated marker. <paramref name="worldSize"/> is the figure size in document
        /// units; when it is null no world figure is drawn and only the pole remains, if the pole is enabled.
        /// </summary>
        public static WorldMarkerOverlayFigure Build(WorldMarkerPlanItem marker, double? worldSize)
        {
            if (marker == null)
                throw new ArgumentNullException(nameof(marker));
            if (!IsSupportedStyle(marker.Style))
                throw new ArgumentException("Unsupported normalized world marker style: " + marker.Style + ".", nameof(marker));

            var size = worldSize;
            if (size.HasValue)
                EnsureWorldSize(size.Value);

            var anchor = new WorldMarkerPoint(marker.X, marker.Y, marker.Z);
            var head = anchor;
            var topZ = marker.Z;
            var segments = new List<WorldMarkerSegment>();

            if (size.HasValue)
            {
                var half = size.Value / 2.0;
                switch (marker.Style)
                {
                    case WorldMarkerStyles.Box:
                        AppendBox(segments, marker.X, marker.Y, marker.Z, half);
                        topZ = marker.Z + half;
                        break;
                    case WorldMarkerStyles.Cross:
                        AppendCross(segments, marker.X, marker.Y, marker.Z, half);
                        topZ = marker.Z + half;
                        break;
                    case WorldMarkerStyles.Target:
                        AppendRing(segments, marker.X, marker.Y, marker.Z, half);
                        AppendDiameters(segments, marker.X, marker.Y, marker.Z, half);
                        break;
                    case WorldMarkerStyles.Circle:
                        AppendRing(segments, marker.X, marker.Y, marker.Z, half);
                        break;
                    case WorldMarkerStyles.Pin:
                        head = new WorldMarkerPoint(marker.X, marker.Y, marker.Z + size.Value);
                        segments.Add(new WorldMarkerSegment(anchor, head));
                        topZ = head.Z;
                        break;
                    case WorldMarkerStyles.Pole:
                        break;
                }
            }

            if (marker.PoleEnabled)
            {
                segments.Add(Segment(marker.X, marker.Y, marker.PoleBaseZ, marker.X, marker.Y, marker.PoleTopZ));
                topZ = Math.Max(topZ, Math.Max(marker.PoleBaseZ, marker.PoleTopZ));
            }

            return new WorldMarkerOverlayFigure(
                segments.AsReadOnly(),
                head,
                new WorldMarkerPoint(marker.X, marker.Y, Math.Max(topZ, head.Z)));
        }

        private static bool IsSupportedStyle(string style)
        {
            switch (style)
            {
                case WorldMarkerStyles.Target:
                case WorldMarkerStyles.Cross:
                case WorldMarkerStyles.Circle:
                case WorldMarkerStyles.Pin:
                case WorldMarkerStyles.Pole:
                case WorldMarkerStyles.Box:
                    return true;
                default:
                    return false;
            }
        }

        private static void EnsureWorldSize(double size)
        {
            if (double.IsNaN(size) || double.IsInfinity(size) ||
                size < WorldMarkerInputPolicy.MinSize || size > WorldMarkerInputPolicy.MaxSize)
            {
                throw new ArgumentException(
                    "size must be between " + WorldMarkerInputPolicy.MinSize.ToString("R", CultureInfo.InvariantCulture) +
                    " and " + WorldMarkerInputPolicy.MaxSize.ToString("R", CultureInfo.InvariantCulture) +
                    " document units.", nameof(size));
            }
        }

        private static void AppendBox(List<WorldMarkerSegment> segments, double centerX, double centerY, double centerZ, double half)
        {
            var x0 = centerX - half;
            var x1 = centerX + half;
            var y0 = centerY - half;
            var y1 = centerY + half;
            var z0 = centerZ - half;
            var z1 = centerZ + half;

            AppendRectangle(segments, x0, y0, x1, y1, z0);
            AppendRectangle(segments, x0, y0, x1, y1, z1);
            segments.Add(Segment(x0, y0, z0, x0, y0, z1));
            segments.Add(Segment(x1, y0, z0, x1, y0, z1));
            segments.Add(Segment(x1, y1, z0, x1, y1, z1));
            segments.Add(Segment(x0, y1, z0, x0, y1, z1));
        }

        private static void AppendRectangle(List<WorldMarkerSegment> segments, double x0, double y0, double x1, double y1, double z)
        {
            segments.Add(Segment(x0, y0, z, x1, y0, z));
            segments.Add(Segment(x1, y0, z, x1, y1, z));
            segments.Add(Segment(x1, y1, z, x0, y1, z));
            segments.Add(Segment(x0, y1, z, x0, y0, z));
        }

        private static void AppendCross(List<WorldMarkerSegment> segments, double centerX, double centerY, double centerZ, double half)
        {
            segments.Add(Segment(centerX - half, centerY, centerZ, centerX + half, centerY, centerZ));
            segments.Add(Segment(centerX, centerY - half, centerZ, centerX, centerY + half, centerZ));
            segments.Add(Segment(centerX, centerY, centerZ - half, centerX, centerY, centerZ + half));
        }

        private static void AppendRing(List<WorldMarkerSegment> segments, double centerX, double centerY, double centerZ, double radius)
        {
            var ring = new WorldMarkerPoint[RingSegmentCount];
            for (var index = 0; index < RingSegmentCount; index++)
            {
                var angle = 2.0 * Math.PI * index / RingSegmentCount;
                ring[index] = new WorldMarkerPoint(
                    centerX + radius * Math.Cos(angle),
                    centerY + radius * Math.Sin(angle),
                    centerZ);
            }

            for (var index = 0; index < RingSegmentCount; index++)
                segments.Add(new WorldMarkerSegment(ring[index], ring[(index + 1) % RingSegmentCount]));
        }

        private static void AppendDiameters(List<WorldMarkerSegment> segments, double centerX, double centerY, double centerZ, double radius)
        {
            segments.Add(Segment(centerX - radius, centerY, centerZ, centerX + radius, centerY, centerZ));
            segments.Add(Segment(centerX, centerY - radius, centerZ, centerX, centerY + radius, centerZ));
        }

        private static WorldMarkerSegment Segment(double startX, double startY, double startZ, double endX, double endY, double endZ)
        {
            return new WorldMarkerSegment(
                new WorldMarkerPoint(startX, startY, startZ),
                new WorldMarkerPoint(endX, endY, endZ));
        }
    }
}
