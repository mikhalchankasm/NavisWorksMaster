using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using Application = Autodesk.Navisworks.Api.Application;

namespace NavisHelper
{
    /// <summary>
    /// Runtime-only elevation labels rendered by the Navisworks Graphics API.
    /// The overlay does not alter the camera and is intentionally not persisted.
    /// </summary>
    [Plugin("NavisHelper.ElevationGraphicsMarker", "CBC")]
    public class ElevationGraphicsMarkerTool : ToolPlugin
    {
        private static readonly object Sync = new object();
        private static List<ElevationGraphicsMarkerVisual> _markers =
            new List<ElevationGraphicsMarkerVisual>();
        private static Tool? _previousTool;
        private static bool _deactivationPending;
        private static System.Windows.Threading.Dispatcher _uiDispatcher;
        private static Document _markerDocument;
        private static List<ModelItem> _markerDocumentRoots = new List<ModelItem>();

        public static bool IsActive
        {
            get { return IsThisToolActive(); }
        }
        public static string LastError { get; private set; }
        public static int MarkerCount
        {
            get
            {
                lock (Sync)
                    return _markers.Count;
            }
        }

        public static bool ShowFromCurrentSelection()
        {
            LastError = null;
            try
            {
                var document = Application.MainDocument;
                var selection = document?.CurrentSelection?.SelectedItems;
                if (document == null || selection == null || selection.Count == 0)
                {
                    LastError = "Выберите объекты для Graphics-меток.";
                    return false;
                }
                if (selection.Count > ElevationMarkerPlanHelper.DefaultMaximumItemCount)
                {
                    LastError = "Выбрано больше " +
                        ElevationMarkerPlanHelper.DefaultMaximumItemCount +
                        " объектов.";
                    return false;
                }

                var markers = new List<ElevationGraphicsMarkerVisual>();
                foreach (ModelItem item in selection)
                {
                    if (item == null)
                        continue;

                    var bounds = item.BoundingBox();
                    var value = ElevationMarkerService.ReadValue(document, item, 0);
                    if (bounds == null || value == null)
                        continue;

                    markers.Add(new ElevationGraphicsMarkerVisual
                    {
                        Point = new Point3D(
                            (bounds.Min.X + bounds.Max.X) / 2.0,
                            (bounds.Min.Y + bounds.Max.Y) / 2.0,
                            bounds.Max.Z),
                        Label = value.Label,
                    });
                }

                if (markers.Count == 0)
                {
                    LastError = "Не удалось получить габариты выделенных объектов.";
                    return false;
                }

                lock (Sync)
                {
                    _markers = markers;
                    _markerDocument = document;
                    _markerDocumentRoots = CaptureDocumentRoots(document);
                    _deactivationPending = false;
                    _uiDispatcher = System.Windows.Threading.Dispatcher.FromThread(
                        System.Threading.Thread.CurrentThread) ?? _uiDispatcher;
                }

                EnsureToolActive();
                if (!string.IsNullOrWhiteSpace(LastError))
                {
                    ClearMarkerState();
                    return false;
                }
                RequestRedraw();
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }

        public static void Hide()
        {
            var wasActive = IsThisToolActive();
            Tool? previousTool;
            lock (Sync)
            {
                ClearMarkerStateUnsafe();
                _deactivationPending = false;
                previousTool = _previousTool;
                _previousTool = null;
            }
            if (!wasActive)
            {
                RequestRedraw();
                return;
            }

            try
            {
                if (previousTool.HasValue)
                    Application.MainDocument.Tool.Value = previousTool.Value;
                else
                    Application.MainDocument.Tool.Value = Tool.Select;
                RequestRedraw();
            }
            catch
            {
                ScheduleInvalidMarkerDeactivation();
            }
        }

        public override bool MouseDown(
            View view,
            KeyModifiers modifiers,
            ushort button,
            int x,
            int y,
            double timeOffset)
        {
            return false;
        }

        public override bool MouseUp(
            View view,
            KeyModifiers modifiers,
            ushort button,
            int x,
            int y,
            double timeOffset)
        {
            return false;
        }

        public override void OverlayRender(View view, Graphics graphics)
        {
            ElevationGraphicsMarkerVisual[] markers;
            lock (Sync)
                markers = _markers.ToArray();

            if (!IsMarkerDocumentCurrent())
            {
                if (IsThisToolActive())
                    ScheduleInvalidMarkerDeactivation();
                return;
            }
            if (markers.Length == 0)
                return;

            using (var font = new TextFontInfo("Arial", 28, 600, false, false))
            {
                foreach (var marker in markers)
                {
                    try
                    {
                        var projection = view.ProjectPoint(marker.Point, true, true);
                        if (projection == null)
                            continue;

                        var top = new Point2D(projection.X, projection.Y);
                        var labelOrigin = new Point2D(top.X + 18, top.Y - 18);

                        graphics.LineWidth(3);
                        graphics.Color(Color.FromByteRGB(0, 255, 255), 1.0);
                        graphics.Line(new Point2D(top.X - 5, top.Y), new Point2D(top.X + 5, top.Y));
                        graphics.Line(new Point2D(top.X, top.Y - 5), new Point2D(top.X, top.Y + 5));
                        graphics.Line(top, new Point2D(labelOrigin.X - 3, labelOrigin.Y + 3));

                        if (graphics.CanRenderText2D(font, marker.Label))
                        {
                            graphics.Color(Color.FromByteRGB(0, 255, 255), 1.0);
                            graphics.Text2D(font, marker.Label, labelOrigin, 0, 0);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void EnsureToolActive()
        {
            try
            {
                var pluginRecord = Application.Plugins.FindPlugin(
                    "NavisHelper.ElevationGraphicsMarker.CBC");
                if (pluginRecord == null)
                {
                    LastError = "Elevation Graphics plugin not found.";
                    return;
                }

                var tool = pluginRecord.LoadedPlugin as ToolPlugin ??
                           pluginRecord.LoadPlugin() as ToolPlugin;
                if (tool == null)
                {
                    LastError = "Elevation Graphics plugin could not be loaded.";
                    return;
                }

                if (!IsThisToolActive())
                {
                    var currentTool = Application.MainDocument.Tool.Value;
                    lock (Sync)
                    {
                        _previousTool = currentTool == Tool.CustomToolPlugin
                            ? Tool.Select
                            : currentTool;
                    }
                }
                Application.MainDocument.Tool.SetCustomToolPlugin(tool);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        private static bool IsMarkerDocumentCurrent()
        {
            lock (Sync)
            {
                var document = Application.MainDocument;
                if (document == null ||
                    !ReferenceEquals(document, _markerDocument) ||
                    _markerDocumentRoots.Count == 0)
                    return false;

                try
                {
                    var currentRoots = CaptureDocumentRoots(document);
                    return currentRoots.Count == _markerDocumentRoots.Count &&
                           _markerDocumentRoots.All(
                               saved => currentRoots.Any(
                                   current => ReferenceEquals(saved, current) || Equals(saved, current)));
                }
                catch
                {
                    return false;
                }
            }
        }

        private static bool IsThisToolActive()
        {
            try
            {
                var document = Application.MainDocument;
                return document != null &&
                       document.Tool.Value == Tool.CustomToolPlugin &&
                       string.Equals(
                           document.Tool.CustomToolPluginId,
                           "NavisHelper.ElevationGraphicsMarker.CBC",
                           StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void ScheduleInvalidMarkerDeactivation()
        {
            System.Windows.Threading.Dispatcher dispatcher;
            lock (Sync)
            {
                ClearMarkerStateUnsafe();
                if (_deactivationPending)
                    return;
                _deactivationPending = true;
                dispatcher = _uiDispatcher;
            }

            if (dispatcher == null || dispatcher.HasShutdownStarted)
            {
                CancelInvalidMarkerDeactivation();
                return;
            }

            try
            {
                var operation = dispatcher.BeginInvoke(
                    new Action(CompleteInvalidMarkerDeactivation));
                operation.Aborted += (sender, args) =>
                    CancelInvalidMarkerDeactivation();
                if (operation.Status ==
                    System.Windows.Threading.DispatcherOperationStatus.Aborted)
                    CancelInvalidMarkerDeactivation();
            }
            catch
            {
                CancelInvalidMarkerDeactivation();
            }
        }

        private static void CancelInvalidMarkerDeactivation()
        {
            lock (Sync)
                _deactivationPending = false;
            RequestRedraw();
        }

        private static void CompleteInvalidMarkerDeactivation()
        {
            lock (Sync)
            {
                if (!_deactivationPending)
                    return;
                _deactivationPending = false;
                _previousTool = null;
            }
            try
            {
                if (IsThisToolActive())
                    Application.MainDocument.Tool.Value = Tool.Select;
            }
            catch
            {
            }
            RequestRedraw();
        }

        private static void ClearMarkerState()
        {
            lock (Sync)
                ClearMarkerStateUnsafe();
        }

        private static void ClearMarkerStateUnsafe()
        {
            _markers.Clear();
            _markerDocument = null;
            _markerDocumentRoots.Clear();
        }

        private static List<ModelItem> CaptureDocumentRoots(Document document)
        {
            var roots = new List<ModelItem>();
            if (document == null)
                return roots;

            foreach (Model model in document.Models)
            {
                if (model?.RootItem != null)
                    roots.Add(model.RootItem);
            }
            return roots;
        }

        private static void RequestRedraw()
        {
            try
            {
                Application.MainDocument?.ActiveView?.RequestDelayedRedraw(
                    ViewRedrawRequests.All);
            }
            catch
            {
            }
        }

        private sealed class ElevationGraphicsMarkerVisual
        {
            public Point3D Point { get; set; }
            public string Label { get; set; }
        }
    }
}
