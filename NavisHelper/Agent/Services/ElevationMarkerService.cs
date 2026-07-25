using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal static class ElevationMarkerService
    {
        public static ElevationMarkerResult Create(
            Document document,
            IList<ModelItem> items,
            double targetZMillimeters,
            string viewpointName,
            bool includeDimensionLine)
        {
            if (document == null || document.ActiveView == null || document.CurrentViewpoint == null)
                throw new InvalidOperationException("Нет активного документа или окна вида.");
            if (items == null || items.Count == 0)
                throw new InvalidOperationException("Список объектов для высотных меток пуст.");
            if (string.IsNullOrWhiteSpace(viewpointName))
                throw new InvalidOperationException("Укажите имя точки обзора.");

            var normalizedName = viewpointName.Replace('/', ' ').Replace('\\', ' ').Trim();
            var root = document.SavedViewpoints == null ? null : document.SavedViewpoints.RootItem;
            if (root == null)
                throw new InvalidOperationException("В документе недоступна коллекция сохранённых точек обзора.");
            var folderName = ElevationViewpointNamingHelper.BuildDayFolderName(DateTime.Now);
            var existingFolder = FindDayFolder(document, folderName);
            if (existingFolder != null && existingFolder.Children.OfType<SavedViewpoint>().Any(item =>
                    string.Equals(item.DisplayName, normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Точка обзора с таким именем уже существует.");
            }

            var contractBounds = new List<ElevationBounds>();
            foreach (var item in items)
            {
                if (item == null)
                    continue;
                var bounds = item.BoundingBox();
                if (bounds == null)
                    continue;

                contractBounds.Add(new ElevationBounds
                {
                    MinX = bounds.Min.X,
                    MinY = bounds.Min.Y,
                    MinZ = bounds.Min.Z,
                    MaxX = bounds.Max.X,
                    MaxY = bounds.Max.Y,
                    MaxZ = bounds.Max.Z,
                });
            }

            if (contractBounds.Count == 0)
                throw new InvalidOperationException("Не удалось получить габариты выбранных объектов.");

            var targetZ = MmToDocumentUnits(document, targetZMillimeters);
            var plan = ElevationMarkerPlanHelper.Build(contractBounds, targetZ);

            // Project and save against the user's current camera. Height markers must
            // never reframe, rotate, or otherwise replace the chosen presentation view.
            var pixelUnits = GetCameraUnitsPerPixel(document.ActiveView);
            var projected = new List<ElevationProjectedItem>();
            for (var index = 0; index < plan.Count; index++)
            {
                var itemPlan = plan[index];
                var top = Project(
                    document.ActiveView,
                    new Point3D(itemPlan.TopX, itemPlan.TopY, itemPlan.TopZ));
                var target = Project(
                    document.ActiveView,
                    new Point3D(itemPlan.TopX, itemPlan.TopY, itemPlan.TargetZ));
                if (top == null || target == null)
                {
                    throw new InvalidOperationException(
                        "Не удалось спроецировать объект №" +
                        (index + 1).ToString(CultureInfo.CurrentCulture) +
                        " в текущий ракурс.");
                }

                projected.Add(new ElevationProjectedItem
                {
                    TopX = top.X,
                    TopY = top.Y,
                    BaseX = target.X,
                    BaseY = target.Y,
                    Label = ElevationMarkerPlanHelper.FormatElevationLabel(
                        DocumentUnitsToMm(document, itemPlan.TopZ)),
                });
            }

            var mode = includeDimensionLine
                ? ElevationRedlineBuilder.DimensionMode
                : ElevationRedlineBuilder.LabelMode;
            var redlines = ElevationRedlineBuilder.BuildNativeText(
                mode,
                projected,
                pixelUnits.X,
                pixelUnits.Y,
                10);
            string nativeTextWarning;
            var nativeTextAccepted = TrySetNativeTextRedlines(
                document.ActiveView,
                redlines.Json,
                out nativeTextWarning);
            if (!nativeTextAccepted)
            {
                Logger.Error(
                    "Нативный RedlineText недоступен, используется векторный текст: " +
                    nativeTextWarning,
                    "ElevationMarker");
                redlines = ElevationRedlineBuilder.Build(
                    mode,
                    projected,
                    pixelUnits.X,
                    pixelUnits.Y);
                RedlineJsonSanitizer.SetSupportedRedlines(document.ActiveView, redlines.Json, null);
            }

            document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
            // Saved-item wrappers can become stale after view/redline mutations.
            // Resolve the folder again immediately before inserting the viewpoint.
            var targetFolder = FindOrCreateDayFolder(document, folderName);
            document.SavedViewpoints.AddCopy(
                targetFolder,
                new SavedViewpoint(document.CurrentViewpoint.CreateCopy())
                {
                    DisplayName = normalizedName,
                });

            var created = FindViewpoint(document, folderName, normalizedName);
            if (created == null)
                throw new InvalidOperationException("Созданная точка обзора не найдена.");

            // A SavedViewpoint constructed from CurrentViewpoint contains the camera
            // only. Replace it from the live view to persist the active redlines.
            document.SavedViewpoints.ReplaceFromCurrentView(created);

            // ReplaceFromCurrentView can rebuild the .NET saved-viewpoint tree and
            // invalidate the object instance used for replacement. Resolve it again
            // before activation. Activation is convenient but not part of persistence,
            // so do not report a successfully saved viewpoint as failed if Navisworks
            // refuses to activate it.
            var refreshed = FindViewpoint(document, folderName, normalizedName);
            try
            {
                if (refreshed != null)
                    document.SavedViewpoints.CurrentSavedViewpoint = refreshed;
                else
                    Logger.Error(
                        "Точка обзора сохранена, но после обновления дерева не найдена для активации.",
                        "ElevationMarker");
            }
            catch (Exception ex)
            {
                Logger.Error(
                    "Точка обзора сохранена, но не активирована: " + ex.Message,
                    "ElevationMarker");
            }

            Logger.Info(
                "Создана точка обзора \"" + normalizedName + "\"; объектов=" +
                projected.Count.ToString(CultureInfo.InvariantCulture) +
                "; примитивов=" + redlines.PrimitiveCount.ToString(CultureInfo.InvariantCulture) +
                "; native_text=" + (nativeTextAccepted ? "true" : "false") +
                "; камера сохранена без изменений.",
                "ElevationMarker");
            return new ElevationMarkerResult
            {
                ViewpointName = normalizedName,
                FolderName = folderName,
                ItemCount = projected.Count,
                PrimitiveCount = redlines.PrimitiveCount,
            };
        }

        public static ElevationObjectValue ReadValue(
            Document document,
            ModelItem item,
            double targetZMillimeters)
        {
            if (document == null || item == null)
                return null;
            var bounds = item.BoundingBox();
            if (bounds == null)
                return null;

            var topZMm = DocumentUnitsToMm(document, bounds.Max.Z);
            return new ElevationObjectValue
            {
                TopZMillimeters = topZMm,
                DifferenceMillimeters = topZMm - targetZMillimeters,
                Label = ElevationMarkerPlanHelper.FormatElevationLabel(topZMm),
            };
        }

        private static FolderItem FindOrCreateDayFolder(Document document, string folderName)
        {
            var existingFolder = FindDayFolder(document, folderName);
            if (existingFolder != null)
                return existingFolder;

            var root = document == null || document.SavedViewpoints == null
                ? null
                : document.SavedViewpoints.RootItem;
            if (root == null)
                throw new InvalidOperationException("В документе недоступна коллекция сохранённых точек обзора.");

            document.SavedViewpoints.InsertCopy(
                root,
                root.Children.Count,
                new FolderItem { DisplayName = folderName });
            var createdFolder = document.SavedViewpoints.RootItem.Children
                .OfType<FolderItem>()
                .FirstOrDefault(item =>
                    string.Equals(item.DisplayName, folderName, StringComparison.OrdinalIgnoreCase));
            if (createdFolder == null)
                throw new InvalidOperationException("Не удалось создать папку Saved Viewpoints \"" + folderName + "\".");
            return createdFolder;
        }

        private static FolderItem FindDayFolder(Document document, string folderName)
        {
            var root = document == null || document.SavedViewpoints == null
                ? null
                : document.SavedViewpoints.RootItem;
            if (root == null)
                throw new InvalidOperationException("В документе недоступна коллекция сохранённых точек обзора.");

            var existingItem = root.Children
                .FirstOrDefault(item =>
                    string.Equals(item.DisplayName, folderName, StringComparison.OrdinalIgnoreCase));
            var existingFolder = existingItem as FolderItem;
            if (existingFolder != null)
                return existingFolder;
            if (existingItem != null)
                throw new InvalidOperationException(
                    "Элемент Saved Viewpoints с именем \"" + folderName + "\" уже существует и не является папкой.");
            return null;
        }

        private static SavedViewpoint FindViewpoint(
            Document document,
            string folderName,
            string viewpointName)
        {
            var root = document == null || document.SavedViewpoints == null
                ? null
                : document.SavedViewpoints.RootItem;
            var folder = root == null
                ? null
                : root.Children
                    .OfType<FolderItem>()
                    .FirstOrDefault(item =>
                        string.Equals(item.DisplayName, folderName, StringComparison.OrdinalIgnoreCase));
            return folder == null
                ? null
                : folder.Children
                    .OfType<SavedViewpoint>()
                    .LastOrDefault(item =>
                        string.Equals(item.DisplayName, viewpointName, StringComparison.OrdinalIgnoreCase));
        }

        private static Point2D Project(Autodesk.Navisworks.Api.View view, Point3D point)
        {
            try
            {
                var projected = view.ProjectPoint(point, false, false);
                if (projected == null)
                    return null;
                var camera = LcOpRedline.ScreenToCameraSpace(
                    view.Viewer,
                    projected.X,
                    projected.Y);
                return IsFinite(camera.X) && IsFinite(camera.Y) ? camera : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool TrySetNativeTextRedlines(
            Autodesk.Navisworks.Api.View view,
            string json,
            out string warning)
        {
            warning = string.Empty;
            if (view == null)
            {
                warning = "Active view is unavailable.";
                return false;
            }

            try
            {
                view.SetRedlines(json);
                var roundTrip = view.GetRedlines();
                if (!string.IsNullOrWhiteSpace(roundTrip) &&
                    roundTrip.IndexOf("\"RedlineText\"", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                warning = "Navisworks did not return RedlineText from GetRedlines().";
                return false;
            }
            catch (Exception ex)
            {
                warning = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static Point2D GetCameraUnitsPerPixel(Autodesk.Navisworks.Api.View view)
        {
            var centerX = Math.Max(view.Width / 2, 1);
            var centerY = Math.Max(view.Height / 2, 1);
            var rightX = Math.Min(centerX + 1, Math.Max(view.Width - 1, 1));
            var downY = Math.Min(centerY + 1, Math.Max(view.Height - 1, 1));
            var center = LcOpRedline.ScreenToCameraSpace(view.Viewer, centerX, centerY);
            var right = LcOpRedline.ScreenToCameraSpace(view.Viewer, rightX, centerY);
            var down = LcOpRedline.ScreenToCameraSpace(view.Viewer, centerX, downY);
            var x = Math.Abs(right.X - center.X);
            var y = Math.Abs(down.Y - center.Y);
            if (!IsFinite(x) || x < 1e-9)
                x = 0.002;
            if (!IsFinite(y) || y < 1e-9)
                y = 0.002;
            return new Point2D(x, y);
        }

        private static double MmToDocumentUnits(Document document, double millimeters)
        {
            if (!IsFinite(millimeters))
                throw new InvalidOperationException("Уровень Z должен быть конечным числом.");
            return millimeters / DocumentUnitsToMm(document, 1.0);
        }

        private static double DocumentUnitsToMm(Document document, double value)
        {
            var units = document == null ? Units.Meters : document.Units;
            switch (units)
            {
                case Units.Centimeters: return value * 10.0;
                case Units.Meters: return value * 1000.0;
                case Units.Kilometers: return value * 1000000.0;
                case Units.Inches: return value * 25.4;
                case Units.Feet: return value * 304.8;
                case Units.Yards: return value * 914.4;
                case Units.Miles: return value * 1609344.0;
                case Units.Millimeters: return value;
                case Units.Micrometers: return value * 0.001;
                case Units.Mils: return value * 0.0254;
                case Units.Microinches: return value * 0.0000254;
                default:
                    throw new InvalidOperationException(
                        "Не поддерживаются единицы документа: " + units + ".");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    internal sealed class ElevationMarkerResult
    {
        public string ViewpointName { get; set; }
        public string FolderName { get; set; }
        public int ItemCount { get; set; }
        public int PrimitiveCount { get; set; }
    }

    internal sealed class ElevationObjectValue
    {
        public double TopZMillimeters { get; set; }
        public double DifferenceMillimeters { get; set; }
        public string Label { get; set; }
    }
}
