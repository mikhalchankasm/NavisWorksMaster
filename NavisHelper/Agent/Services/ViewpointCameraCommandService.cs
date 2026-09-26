using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed class ViewpointCameraCommandService
    {
        private const string OrthographicPlacementWarning =
            "Navisworks holds the camera at effectivePosition instead of the requested position. " +
            "In orthographic projection Navisworks chooses the camera's place on its line of sight; " +
            "the view direction, up vector, and heightField are as requested.";

        private readonly DocumentCommandService _viewpointCommands;

        public ViewpointCameraCommandService(DocumentCommandService viewpointCommands)
        {
            _viewpointCommands = viewpointCommands ?? throw new ArgumentNullException(nameof(viewpointCommands));
        }

        public ViewpointSetCameraResponse SetCamera(Document document, ViewpointSetCameraRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (document.ActiveView == null || document.CurrentViewpoint == null)
                throw new AgentCommandException(ErrorCodes.NoActiveView, "There is no active view.");

            ViewpointCameraPlan plan;
            try
            {
                plan = ViewpointCameraPlanHelper.Build(request);
            }
            catch (ArgumentException exception)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, exception.Message);
            }

            var apply = request.Apply == true;
            var savePreview = PreviewSave(document, plan);
            var response = BuildResponse(plan, apply, savePreview);
            if (!apply)
                return response;
            if (savePreview != null && savePreview.NameConflict)
                throw new AgentCommandException(ErrorCodes.ViewpointNameConflict, "Viewpoint with the same name already exists in the target folder.");

            var original = document.CurrentViewpoint.CreateCopy();
            try
            {
                var candidate = original.CreateCopy();
                ApplyPlan(candidate, plan);
                document.CurrentViewpoint.CopyFrom(candidate);
                document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);

                var applied = document.CurrentViewpoint.CreateCopy();
                response.EffectivePosition = ToPointInfo(applied.Position);
                if (!ViewpointCameraPlanHelper.PointsNearlyEqual(response.EffectivePosition, plan.Position))
                    response.Warnings.Add(OrthographicPlacementWarning);

                response.Applied = true;
                if (plan.SaveRequested)
                {
                    var saved = _viewpointCommands.CreateViewpoint(document, new CreateViewpointRequest
                    {
                        Name = plan.SaveName,
                        FolderPath = plan.SaveFolderPath,
                        Apply = true,
                    });
                    response.Saved = saved.Created == true;
                    response.CreatedFolderCount = saved.CreatedFolderCount;
                    response.SaveFolderPath = saved.FolderPath;
                }

                return response;
            }
            catch (Exception applyException)
            {
                try
                {
                    document.CurrentViewpoint.CopyFrom(original);
                    document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
                }
                catch (Exception restoreException)
                {
                    throw new AgentCommandException(
                        ErrorCodes.CameraStateRestoreFailed,
                        "Camera apply failed and the original viewpoint could not be restored. Apply error: " +
                        applyException.Message + " Restore error: " + restoreException.Message);
                }

                throw;
            }
        }

        private CreateViewpointResponse PreviewSave(Document document, ViewpointCameraPlan plan)
        {
            if (!plan.SaveRequested)
                return null;
            return _viewpointCommands.CreateViewpoint(document, new CreateViewpointRequest
            {
                Name = plan.SaveName,
                FolderPath = plan.SaveFolderPath,
                Apply = false,
            });
        }

        private static ViewpointSetCameraResponse BuildResponse(
            ViewpointCameraPlan plan,
            bool apply,
            CreateViewpointResponse savePreview)
        {
            return new ViewpointSetCameraResponse
            {
                Apply = apply,
                Applied = false,
                Position = plan.Position,
                Target = plan.Target,
                Direction = plan.Direction,
                Up = plan.Up,
                FocalDistance = plan.FocalDistance,
                Projection = plan.Projection,
                HeightField = plan.HeightField,
                ZoomBox = plan.ZoomBox,
                SaveName = plan.SaveName,
                SaveFolderPath = savePreview == null ? plan.SaveFolderPath : savePreview.FolderPath,
                SaveRequested = plan.SaveRequested,
                SaveNameConflict = savePreview != null && savePreview.NameConflict,
                Saved = false,
                Warnings = new List<string>(),
            };
        }

        private static void ApplyPlan(Viewpoint viewpoint, ViewpointCameraPlan plan)
        {
            var position = ToPoint(plan.Position);
            var target = ToPoint(plan.Target);
            var up = ToVector(plan.Up);

            viewpoint.Position = position;
            viewpoint.Projection = plan.Projection == ViewpointCameraPlanHelper.Orthographic
                ? ViewpointProjection.Orthographic
                : ViewpointProjection.Perspective;
            PointAndAlign(viewpoint, target, up, plan.FocalDistance);

            if (plan.ZoomBox != null)
            {
                viewpoint.ZoomBox(new BoundingBox3D(ToPoint(plan.ZoomBox.Min), ToPoint(plan.ZoomBox.Max)));
                var zoomHeightField = viewpoint.HeightField;
                viewpoint.Position = position;
                PointAndAlign(viewpoint, target, up, plan.FocalDistance);
                viewpoint.HeightField = zoomHeightField;
            }

            if (plan.HeightField.HasValue)
                viewpoint.HeightField = plan.HeightField.Value;
        }

        private static void PointAndAlign(Viewpoint viewpoint, Point3D target, Vector3D up, double focalDistance)
        {
            viewpoint.FocalDistance = focalDistance;
            viewpoint.RightOffsetAtFocalDistance = 0;
            viewpoint.UpOffsetAtFocalDistance = 0;
            viewpoint.RightOffsetFactor = 0;
            viewpoint.UpOffsetFactor = 0;
            viewpoint.PointAt(target);
            viewpoint.AlignUp(up);
        }

        private static Point3Info ToPointInfo(Point3D point)
        {
            return new Point3Info { X = point.X, Y = point.Y, Z = point.Z };
        }

        private static Point3D ToPoint(Point3Info point)
        {
            return new Point3D(point.X, point.Y, point.Z);
        }

        private static Vector3D ToVector(Point3Info point)
        {
            return new Vector3D(point.X, point.Y, point.Z);
        }
    }
}
