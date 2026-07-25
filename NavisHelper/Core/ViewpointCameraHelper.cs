using System;
using Autodesk.Navisworks.Api;

namespace NavisHelper.Core
{
    public static class ViewpointCameraHelper
    {
        public static void ApplyIso1ViewToBox(Document document, BoundingBox3D box, Point3D focalPoint)
        {
            ApplyIsoViewToBox(document, box, focalPoint, false);
        }

        public static void ApplyIsoOppositeViewToBox(Document document, BoundingBox3D box, Point3D focalPoint)
        {
            ApplyIsoViewToBox(document, box, focalPoint, true);
        }

        private static void ApplyIsoViewToBox(Document document, BoundingBox3D box, Point3D focalPoint, bool opposite)
        {
            if (document == null || document.CurrentViewpoint == null || box == null)
                return;

            var center = focalPoint ?? box.Center;
            var sizeX = Math.Abs(box.Max.X - box.Min.X);
            var sizeY = Math.Abs(box.Max.Y - box.Min.Y);
            var sizeZ = Math.Abs(box.Max.Z - box.Min.Z);
            var size = Math.Max(Math.Max(sizeX, sizeY), sizeZ);
            if (size < 1e-6)
                size = 1.0;

            var distance = size * 3.0;
            var viewpoint = document.CurrentViewpoint.CreateCopy();
            viewpoint.Projection = ViewpointProjection.Orthographic;
            viewpoint.Position = new Point3D(
                center.X + (opposite ? -distance : distance),
                center.Y + (opposite ? distance : -distance),
                center.Z + distance);

            ResetViewpointOffsets(viewpoint);
            PointAtAndAlign(viewpoint, center);
            viewpoint.ZoomBox(box);
            ResetViewpointOffsets(viewpoint);
            PointAtAndAlign(viewpoint, center);
            SetFocalDistance(viewpoint, center);

            document.CurrentViewpoint.CopyFrom(viewpoint);
        }

        private static void PointAtAndAlign(Viewpoint viewpoint, Point3D point)
        {
            TryViewpointAction(() => viewpoint.PointAt(point), "point viewpoint at clash target");
            TryViewpointAction(() => viewpoint.AlignUp(new Vector3D(0, 0, 1)), "align viewpoint up vector");
        }

        private static void SetFocalDistance(Viewpoint viewpoint, Point3D point)
        {
            try
            {
                var p = viewpoint.Position;
                var dx = p.X - point.X;
                var dy = p.Y - point.Y;
                var dz = p.Z - point.Z;
                viewpoint.FocalDistance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to set viewpoint focal distance: " + ex.Message, "ViewpointCamera");
            }
        }

        private static void ResetViewpointOffsets(Viewpoint viewpoint)
        {
            TryViewpointAction(() => viewpoint.RightOffsetAtFocalDistance = 0, "reset right offset at focal distance");
            TryViewpointAction(() => viewpoint.UpOffsetAtFocalDistance = 0, "reset up offset at focal distance");
            TryViewpointAction(() => viewpoint.RightOffsetFactor = 0, "reset right offset factor");
            TryViewpointAction(() => viewpoint.UpOffsetFactor = 0, "reset up offset factor");
        }

        private static void TryViewpointAction(Action action, string context)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to " + context + ": " + ex.Message, "ViewpointCamera");
            }
        }
    }
}
