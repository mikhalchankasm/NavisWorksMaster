using System;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Agent.Contracts;

namespace NavisHelper
{
    /// <summary>
    /// Draws the visible overlay world markers held by <see cref="WorldMarkerOverlayStore"/>.
    /// All drawing is 2D, in <see cref="OverlayRender"/>: the world segments and head anchors
    /// come from WorldMarkerOverlayGeometry figures that the store prebuilds once per store
    /// version; each frame only projects them and emits pixel lines, pixel head glyphs and
    /// Text2D labels drawn beside the projected head. When the head projection is clipped
    /// away the head glyph and the label are skipped; segments are still drawn wherever both
    /// of their ends project. Every native handle built while drawing a frame — each
    /// Point2D passed to a pixel primitive — is disposed before the frame ends; a
    /// ProjectionResult is plain managed data with no native handle to release.
    /// <see cref="RenderPlugin.Render"/> is never overridden, because
    /// its 3D primitives never appear in the view. An exception inside a render callback is
    /// recorded in the store, never thrown.
    /// </summary>
    [Plugin("NavisHelper.WorldMarkers", "CBC")]
    public sealed class WorldMarkerOverlayRenderer : RenderPlugin
    {
        private const double SegmentLineWidth = 2.0;
        private const double GlyphLineWidth = 2.0;
        private const double LabelOffsetPx = 4.0;

        private TextFontInfo _font;

        public override void OverlayRender(View view, Graphics graphics)
        {
            try
            {
                var frame = WorldMarkerOverlayStore.BeginOverlayRender();
                if (frame.IsEmpty || frame.Document == null || view == null || graphics == null ||
                    !ReferenceEquals(view.Document, frame.Document))
                    return;

                graphics.DepthTest(false);
                graphics.Blend(true);

                var font = Font;
                var drawn = 0;
                foreach (var marker in frame.Markers)
                {
                    try
                    {
                        DrawMarker(view, graphics, font, marker);
                        drawn++;
                    }
                    catch (Exception ex)
                    {
                        WorldMarkerOverlayStore.RecordError(ex);
                    }
                }

                WorldMarkerOverlayStore.RecordDraw(drawn);
            }
            catch (Exception ex)
            {
                WorldMarkerOverlayStore.RecordError(ex);
            }
        }

        public override BoundingBox3D MakeRenderBoundingBox(View view)
        {
            try
            {
                return WorldMarkerOverlayStore.RenderBoundsRequested();
            }
            catch (Exception ex)
            {
                WorldMarkerOverlayStore.RecordError(ex);
                return new BoundingBox3D();
            }
        }

        private TextFontInfo Font
        {
            get
            {
                if (_font == null)
                    _font = new TextFontInfo("Arial", 24, 600, false, false);
                return _font;
            }
        }

        private static void DrawMarker(View view, Graphics graphics, TextFontInfo font, WorldMarkerOverlayMarkerGlyph marker)
        {
            DrawSegments(view, graphics, marker);

            var head = view.ProjectPoint(marker.HeadAnchor, true, true);
            if (head == null)
                return;

            using (var center = new Point2D(head.X, head.Y))
            {
                DrawHeadGlyph(graphics, marker, center);
            }

            if (string.IsNullOrEmpty(marker.Label))
                return;

            if (!graphics.CanRenderText2D(font, marker.Label))
                return;

            graphics.Color(marker.Color, marker.Alpha);
            using (var labelOrigin = new Point2D(
                head.X + marker.SizePx + LabelOffsetPx,
                head.Y - marker.SizePx - LabelOffsetPx))
            {
                graphics.Text2D(font, marker.Label, labelOrigin, 0, 0);
            }
        }

        private static void DrawSegments(View view, Graphics graphics, WorldMarkerOverlayMarkerGlyph marker)
        {
            if (marker.Segments.Count == 0)
                return;

            graphics.LineWidth(SegmentLineWidth);
            graphics.Color(marker.Color, marker.Alpha);
            foreach (var segment in marker.Segments)
            {
                var start = view.ProjectPoint(segment.Start, true, true);
                if (start == null)
                    continue;

                var end = view.ProjectPoint(segment.End, true, true);
                if (end == null)
                    continue;

                using (var from = new Point2D(start.X, start.Y))
                using (var to = new Point2D(end.X, end.Y))
                {
                    graphics.Line(from, to);
                }
            }
        }

        private static void DrawHeadGlyph(Graphics graphics, WorldMarkerOverlayMarkerGlyph marker, Point2D center)
        {
            var radius = marker.SizePx;
            graphics.Color(marker.Color, marker.Alpha);
            if (string.Equals(marker.Style, WorldMarkerStyles.Target, StringComparison.Ordinal))
            {
                graphics.LineWidth(GlyphLineWidth);
                graphics.Circle(center, radius, false);
                graphics.Circle(center, radius * 0.5, false);
                using (var west = new Point2D(center.X - radius, center.Y))
                using (var east = new Point2D(center.X + radius, center.Y))
                using (var north = new Point2D(center.X, center.Y - radius))
                using (var south = new Point2D(center.X, center.Y + radius))
                {
                    graphics.Line(west, east);
                    graphics.Line(north, south);
                }
                return;
            }

            if (string.Equals(marker.Style, WorldMarkerStyles.Cross, StringComparison.Ordinal))
            {
                graphics.LineWidth(GlyphLineWidth);
                using (var northWest = new Point2D(center.X - radius, center.Y - radius))
                using (var southEast = new Point2D(center.X + radius, center.Y + radius))
                using (var southWest = new Point2D(center.X - radius, center.Y + radius))
                using (var northEast = new Point2D(center.X + radius, center.Y - radius))
                {
                    graphics.Line(northWest, southEast);
                    graphics.Line(southWest, northEast);
                }
                return;
            }

            if (string.Equals(marker.Style, WorldMarkerStyles.Circle, StringComparison.Ordinal))
            {
                graphics.LineWidth(GlyphLineWidth);
                graphics.Circle(center, radius, false);
                return;
            }

            if (string.Equals(marker.Style, WorldMarkerStyles.Pin, StringComparison.Ordinal))
            {
                graphics.Circle(center, radius * 0.5, true);
                graphics.LineWidth(GlyphLineWidth);
                using (var top = new Point2D(center.X, center.Y + radius))
                {
                    graphics.Line(center, top);
                }
                return;
            }

            graphics.Circle(center, radius * 0.5, true);
        }
    }
}
