using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper
{
    /// <summary>
    /// The static overlay world-marker store shared by the host commands and
    /// <see cref="WorldMarkerOverlayRenderer"/>. It holds the current planner snapshot and the
    /// document it belongs to, swapped by reference on the UI thread; a render callback reads
    /// one frame reference per call. Per store version it prebuilds the native handles (Point3D
    /// segment ends and anchors, Colors, the union BoundingBox3D) by calling
    /// WorldMarkerOverlayGeometry once per visible marker, so no handle is built per frame.
    /// After a change it requests a delayed redraw of the active view: OverlayRender, or All
    /// when the visible bounds changed.
    /// </summary>
    public static class WorldMarkerOverlayStore
    {
        private static readonly object Sync = new object();

        private static WorldMarkerOverlayFrame _frame = WorldMarkerOverlayFrame.Empty;
        private static long _version;
        private static int _overlayRenderCount;
        private static int _renderBoundsCount;
        private static int _lastDrawMarkerCount = -1;
        private static DateTime? _lastDrawUtc;
        private static string _lastError;

        public static WorldMarkerOverlayFrame Current
        {
            get
            {
                lock (Sync)
                    return _frame;
            }
        }

        public static void Update(Document document, WorldMarkerOverlaySnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            WorldMarkerOverlayFrame previous;
            WorldMarkerOverlayFrame next;
            lock (Sync)
            {
                previous = _frame;
                next = BuildFrame(document, snapshot, _version + 1);
                _frame = next;
                _version = next.Version;
            }

            RequestRedraw(previous, next);
        }

        public static void Clear()
        {
            WorldMarkerOverlayFrame previous;
            WorldMarkerOverlayFrame next;
            lock (Sync)
            {
                previous = _frame;
                if (previous.IsEmpty && previous.Document == null)
                    return;

                next = new WorldMarkerOverlayFrame(
                    _version + 1,
                    null,
                    WorldMarkerOverlaySnapshot.Empty,
                    WorldMarkerOverlayBounds.Empty,
                    new BoundingBox3D(),
                    new WorldMarkerOverlayMarkerGlyph[0]);
                _frame = next;
                _version = next.Version;
            }

            RequestRedraw(previous, next);
        }

        /// <summary>
        /// Document-change hook: clears the stored markers when they belonged to a document
        /// other than the active one. The host wires this to document open and close.
        /// </summary>
        public static void OnDocumentChanged(Document activeDocument)
        {
            bool clear;
            lock (Sync)
            {
                var frame = _frame;
                clear = !frame.IsEmpty && !ReferenceEquals(frame.Document, activeDocument);
            }

            if (clear)
                Clear();
        }

        public static WorldMarkerOverlayFrame BeginOverlayRender()
        {
            lock (Sync)
            {
                _overlayRenderCount++;
                return _frame;
            }
        }

        public static BoundingBox3D RenderBoundsRequested()
        {
            lock (Sync)
            {
                _renderBoundsCount++;
                return _frame.Bounds;
            }
        }

        public static void RecordDraw(int markerCount)
        {
            lock (Sync)
            {
                _lastDrawMarkerCount = markerCount;
                _lastDrawUtc = DateTime.UtcNow;
            }
        }

        public static void RecordError(Exception exception)
        {
            lock (Sync)
            {
                _lastError = exception == null
                    ? null
                    : exception.GetType().Name + ": " + exception.Message;
            }
        }

        public static WorldMarkerOverlayDiagnostics GetDiagnostics()
        {
            lock (Sync)
            {
                return new WorldMarkerOverlayDiagnostics(
                    _version,
                    _frame.Snapshot.Count,
                    _frame.Markers.Count,
                    _overlayRenderCount,
                    _renderBoundsCount,
                    _lastDrawMarkerCount,
                    _lastDrawUtc,
                    _lastError);
            }
        }

        private static WorldMarkerOverlayFrame BuildFrame(Document document, WorldMarkerOverlaySnapshot snapshot, long version)
        {
            var glyphs = new List<WorldMarkerOverlayMarkerGlyph>(snapshot.Count);
            foreach (var marker in snapshot.Markers)
            {
                if (marker.Visible)
                    glyphs.Add(BuildGlyph(marker));
            }

            var worldBounds = WorldMarkerOverlayPlanner.VisibleBounds(snapshot);
            return new WorldMarkerOverlayFrame(
                version, document, snapshot, worldBounds, BuildNativeBounds(worldBounds), glyphs.AsReadOnly());
        }

        private static WorldMarkerOverlayMarkerGlyph BuildGlyph(WorldMarkerOverlayMarker marker)
        {
            var color = marker.Color;
            var figure = WorldMarkerOverlayGeometry.Build(new WorldMarkerPlanItem
            {
                MarkerId = marker.MarkerId,
                Name = marker.Name,
                X = marker.X,
                Y = marker.Y,
                Z = marker.Z,
                Style = marker.Style,
                Size = marker.WorldSize ?? 0.0,
                Color = color,
                Label = marker.Label,
                PoleEnabled = marker.PoleEnabled,
                PoleBaseZ = marker.PoleBaseZ,
                PoleTopZ = marker.PoleTopZ,
            }, marker.WorldSize);

            var segments = new WorldMarkerOverlaySegment3D[figure.Segments.Count];
            for (var index = 0; index < figure.Segments.Count; index++)
            {
                var segment = figure.Segments[index];
                segments[index] = new WorldMarkerOverlaySegment3D(
                    new Point3D(segment.Start.X, segment.Start.Y, segment.Start.Z),
                    new Point3D(segment.End.X, segment.End.Y, segment.End.Z));
            }

            return new WorldMarkerOverlayMarkerGlyph(
                marker.Style,
                marker.SizePx,
                marker.Alpha.HasValue ? marker.Alpha.Value / 255.0 : 1.0,
                Color.FromByteRGB((byte)color.R, (byte)color.G, (byte)color.B),
                new Point3D(figure.HeadAnchor.X, figure.HeadAnchor.Y, figure.HeadAnchor.Z),
                new Point3D(figure.LabelAnchor.X, figure.LabelAnchor.Y, figure.LabelAnchor.Z),
                marker.Label,
                segments);
        }

        private static BoundingBox3D BuildNativeBounds(WorldMarkerOverlayBounds bounds)
        {
            if (bounds.IsEmpty)
                return new BoundingBox3D();

            return new BoundingBox3D()
                .Extend(new Point3D(bounds.MinX, bounds.MinY, bounds.MinZ))
                .Extend(new Point3D(bounds.MaxX, bounds.MaxY, bounds.MaxZ));
        }

        private static void RequestRedraw(WorldMarkerOverlayFrame previous, WorldMarkerOverlayFrame next)
        {
            try
            {
                var view = next.Document != null ? next.Document.ActiveView : null;
                if (view == null)
                    view = previous.Document != null ? previous.Document.ActiveView : null;
                if (view == null)
                    return;

                view.RequestDelayedRedraw(
                    BoundsChanged(previous, next) ? ViewRedrawRequests.All : ViewRedrawRequests.OverlayRender);
            }
            catch (Exception ex)
            {
                RecordError(ex);
            }
        }

        private static bool BoundsChanged(WorldMarkerOverlayFrame previous, WorldMarkerOverlayFrame next)
        {
            var oldBounds = previous.WorldBounds;
            var newBounds = next.WorldBounds;
            if (oldBounds.IsEmpty && newBounds.IsEmpty)
                return false;
            if (oldBounds.IsEmpty || newBounds.IsEmpty)
                return true;

            return oldBounds.MinX != newBounds.MinX || oldBounds.MinY != newBounds.MinY ||
                oldBounds.MinZ != newBounds.MinZ || oldBounds.MaxX != newBounds.MaxX ||
                oldBounds.MaxY != newBounds.MaxY || oldBounds.MaxZ != newBounds.MaxZ;
        }
    }

    /// <summary>The immutable data of one store version: the snapshot, its document and the prebuilt frame.</summary>
    public sealed class WorldMarkerOverlayFrame
    {
        public static readonly WorldMarkerOverlayFrame Empty = new WorldMarkerOverlayFrame(
            0,
            null,
            WorldMarkerOverlaySnapshot.Empty,
            WorldMarkerOverlayBounds.Empty,
            new BoundingBox3D(),
            new WorldMarkerOverlayMarkerGlyph[0]);

        internal WorldMarkerOverlayFrame(
            long version,
            Document document,
            WorldMarkerOverlaySnapshot snapshot,
            WorldMarkerOverlayBounds worldBounds,
            BoundingBox3D bounds,
            IReadOnlyList<WorldMarkerOverlayMarkerGlyph> markers)
        {
            Version = version;
            Document = document;
            Snapshot = snapshot;
            WorldBounds = worldBounds;
            Bounds = bounds;
            Markers = markers;
        }

        public long Version { get; }

        public Document Document { get; }

        public WorldMarkerOverlaySnapshot Snapshot { get; }

        public WorldMarkerOverlayBounds WorldBounds { get; }

        /// <summary>The cached union bounds of the visible markers; an empty box when nothing is visible.</summary>
        public BoundingBox3D Bounds { get; }

        /// <summary>The prebuilt glyph records of the visible markers, in snapshot order.</summary>
        public IReadOnlyList<WorldMarkerOverlayMarkerGlyph> Markers { get; }

        public bool IsEmpty
        {
            get { return Markers.Count == 0; }
        }
    }

    /// <summary>Everything one visible marker draws, with the native handles built once per store version.</summary>
    public sealed class WorldMarkerOverlayMarkerGlyph
    {
        internal WorldMarkerOverlayMarkerGlyph(
            string style,
            int sizePx,
            double alpha,
            Color color,
            Point3D headAnchor,
            Point3D labelAnchor,
            string label,
            WorldMarkerOverlaySegment3D[] segments)
        {
            Style = style;
            SizePx = sizePx;
            Alpha = alpha;
            Color = color;
            HeadAnchor = headAnchor;
            LabelAnchor = labelAnchor;
            Label = label;
            Segments = segments;
        }

        public string Style { get; }

        public int SizePx { get; }

        /// <summary>The marker alpha as a 0-1 factor; 1 when the marker is opaque.</summary>
        public double Alpha { get; }

        public Color Color { get; }

        public Point3D HeadAnchor { get; }

        public Point3D LabelAnchor { get; }

        public string Label { get; }

        public IReadOnlyList<WorldMarkerOverlaySegment3D> Segments { get; }
    }

    /// <summary>One world-space segment of a marker figure, prebuilt as native points.</summary>
    public sealed class WorldMarkerOverlaySegment3D
    {
        internal WorldMarkerOverlaySegment3D(Point3D start, Point3D end)
        {
            Start = start;
            End = end;
        }

        public Point3D Start { get; }

        public Point3D End { get; }
    }

    /// <summary>A point-in-time copy of the store's counters and errors.</summary>
    public sealed class WorldMarkerOverlayDiagnostics
    {
        internal WorldMarkerOverlayDiagnostics(
            long version,
            int storedMarkerCount,
            int visibleMarkerCount,
            int overlayRenderCount,
            int renderBoundsCount,
            int lastDrawMarkerCount,
            DateTime? lastDrawUtc,
            string lastError)
        {
            Version = version;
            StoredMarkerCount = storedMarkerCount;
            VisibleMarkerCount = visibleMarkerCount;
            OverlayRenderCount = overlayRenderCount;
            RenderBoundsCount = renderBoundsCount;
            LastDrawMarkerCount = lastDrawMarkerCount;
            LastDrawUtc = lastDrawUtc;
            LastError = lastError;
        }

        public long Version { get; }

        public int StoredMarkerCount { get; }

        public int VisibleMarkerCount { get; }

        public int OverlayRenderCount { get; }

        public int RenderBoundsCount { get; }

        /// <summary>The marker count of the last draw pass; -1 before the first one.</summary>
        public int LastDrawMarkerCount { get; }

        public DateTime? LastDrawUtc { get; }

        public string LastError { get; }
    }
}
