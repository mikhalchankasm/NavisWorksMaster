using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop;
using Autodesk.Navisworks.Api.Plugins;
using Application = Autodesk.Navisworks.Api.Application;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;
using NavisHelper.Core.Localization;

namespace NavisHelper
{
    [Plugin("TopViewBoundingRect", "CBC", DisplayName = "Габаритный прямоугольник")]
    [AddInPlugin(AddInLocation.None)]
    public class TopViewBoundingRect : AddInPlugin
    {
        private const double LineR = 1.0;
        private const double LineG = 0.0;
        private const double LineB = 0.0;
        private const int LineThickness = 3;
        private const double PaddingFactor = 0.10;
        private double _hatchSpacing = 0.024;

        protected virtual string MarkupStyle => MarkupRedlineJsonBuilder.RectangleStyle;
        protected virtual string CommandTitle =>
            UiLocalizationService.Current.GetString("BoundingRectTitle");
        protected virtual string ViewpointNamePrefix =>
            UiLocalizationService.Current.GetString("BoundingRectViewpointPrefix");
        protected virtual string LogCommandTitle => "Габаритный прямоугольник";

        public override int Execute(params string[] parameters)
        {
            try
            {
                Document doc = Application.ActiveDocument;
                if (doc == null)
                {
                    Logger.Error("Нет активного документа", "TopViewBoundingRect");
                    return 0;
                }

                var selection = doc.CurrentSelection.SelectedItems;
                if (selection.Count == 0)
                {
                    MessageBox.Show(
                        UiLocalizationService.Current.GetString("CommonNoSelection"),
                        CommandTitle,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                Autodesk.Navisworks.Api.View activeView = doc.ActiveView;
                if (activeView == null)
                {
                    Logger.Error("Нет активного окна вида", "TopViewBoundingRect");
                    return 0;
                }
                Viewpoint currentViewpoint = doc.CurrentViewpoint.CreateCopy();
                _hatchSpacing = currentViewpoint.Projection == ViewpointProjection.Perspective
                    ? GetCameraSpacing(activeView, 10)
                    : 0.024;

                // Объединённый BoundingBox всех выделенных элементов
                BoundingBox3D combinedBBox = selection.BoundingBox();
                if (combinedBBox == null)
                {
                    MessageBox.Show(
                        UiLocalizationService.Current.GetString("BoundingRegionUnavailable"),
                        CommandTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 0;
                }

                Point3D bMin = combinedBBox.Min;
                Point3D bMax = combinedBBox.Max;

                // Проецируем 8 углов combined bbox в redline-координаты
                double redMinX = double.MaxValue, redMaxX = double.MinValue;
                double redMinY = double.MaxValue, redMaxY = double.MinValue;
                int validCorners = 0;

                double[] xs = { bMin.X, bMax.X };
                double[] ys = { bMin.Y, bMax.Y };
                double[] zs = { bMin.Z, bMax.Z };

                foreach (double cx in xs)
                foreach (double cy in ys)
                foreach (double cz in zs)
                {
                    double[] red = WorldToRedline(cx, cy, cz, activeView);
                    if (red == null) continue;
                    if (!IsFinite(red[0]) || !IsFinite(red[1])) continue;

                    if (red[0] < redMinX) redMinX = red[0];
                    if (red[0] > redMaxX) redMaxX = red[0];
                    if (red[1] < redMinY) redMinY = red[1];
                    if (red[1] > redMaxY) redMaxY = red[1];
                    validCorners++;
                }

                if (validCorners < 2)
                {
                    MessageBox.Show(
                        UiLocalizationService.Current.GetString("BoundingOutsideCamera"),
                        CommandTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                // Добавляем отступ
                double padX = (redMaxX - redMinX) * PaddingFactor;
                double padY = (redMaxY - redMinY) * PaddingFactor;
                double left = redMinX - padX;
                double right = redMaxX + padX;
                double top = redMinY - padY;
                double bottom = redMaxY + padY;

                // Запрашиваем имя точки обзора
                string defaultName = $"{ViewpointNamePrefix} {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                string viewpointName = PromptViewpointName(defaultName);
                if (viewpointName == null)
                    return 0;

                // The hatch is intentionally composed of the same persisted
                // RedlineLine primitives as the MCP markup style.
                string redlineJson = BuildRedlineJson(left, top, right, bottom);
                try
                {
                    RedlineJsonSanitizer.SetSupportedRedlines(activeView, redlineJson, null);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Не удалось установить redlines: {ex.Message}", "TopViewBoundingRect");
                    return 0;
                }

                SavedViewpoint savedVp = new SavedViewpoint(doc.CurrentViewpoint.CreateCopy());
                savedVp.DisplayName = viewpointName;

                int insertIndex = doc.SavedViewpoints.Value.Count();
                doc.SavedViewpoints.InsertCopy(insertIndex, savedVp);

                SavedViewpoint addedViewpoint = null;
                foreach (SavedItem item in doc.SavedViewpoints.Value)
                {
                    if (item.DisplayName == viewpointName && item is SavedViewpoint svp)
                        addedViewpoint = svp;
                }

                if (addedViewpoint != null)
                {
                    try
                    {
                        doc.SavedViewpoints.ReplaceFromCurrentView(addedViewpoint);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"ReplaceFromCurrentView error: {ex.Message}", "TopViewBoundingRect");
                    }

                    // ReplaceFromCurrentView may rebuild the SavedViewpoints tree.
                    // Resolve the item again before optional activation.
                    try
                    {
                        SavedViewpoint refreshedViewpoint = doc.SavedViewpoints.Value
                            .OfType<SavedViewpoint>()
                            .LastOrDefault(item => item.DisplayName == viewpointName);
                        if (refreshedViewpoint != null)
                            doc.SavedViewpoints.CurrentSavedViewpoint = refreshedViewpoint;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(
                            $"Точка обзора сохранена, но не активирована: {ex.Message}",
                            "TopViewBoundingRect");
                    }
                }

                Logger.Info($"{LogCommandTitle}: \"{viewpointName}\" ({selection.Count} элементов)", "TopViewBoundingRect");
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка: {ex.Message}\n{ex.StackTrace}", "TopViewBoundingRect");
                return 0;
            }
        }

        private double[] WorldToRedline(
            double wx, double wy, double wz,
            Autodesk.Navisworks.Api.View activeView)
        {
            ProjectionResult projected = activeView.ProjectPoint(new Point3D(wx, wy, wz), false, false);
            if (projected == null)
            {
                return null;
            }

            Point2D cameraSpace = LcOpRedline.ScreenToCameraSpace(activeView.Viewer, projected.X, projected.Y);
            if (!IsFinite(cameraSpace.X) || !IsFinite(cameraSpace.Y))
            {
                return null;
            }

            return new[] { cameraSpace.X, cameraSpace.Y };
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        protected virtual string BuildRedlineJson(double left, double top, double right, double bottom)
        {
            if (double.IsNaN(left) || double.IsNaN(top) || double.IsNaN(right) || double.IsNaN(bottom))
            {
                throw new InvalidOperationException("Некорректные координаты redline (NaN).");
            }
            return MarkupRedlineJsonBuilder.Build(
                MarkupStyle,
                new[] { new MarkupRedlineRect { Left = left, Top = top, Right = right, Bottom = bottom } },
                LineR,
                LineG,
                LineB,
                LineThickness,
                0.08,
                0.08,
                new MarkupRedlineBuildOptions
                {
                    HatchAngleDeg = 45,
                    HatchSpacing = _hatchSpacing,
                    HatchThickness = LineThickness,
                });
        }

        private static double GetCameraSpacing(Autodesk.Navisworks.Api.View view, int pixels)
        {
            try
            {
                int centerX = Math.Max(view.Width / 2, 1);
                int centerY = Math.Max(view.Height / 2, 1);
                Point2D center = LcOpRedline.ScreenToCameraSpace(view.Viewer, centerX, centerY);
                Point2D right = LcOpRedline.ScreenToCameraSpace(
                    view.Viewer,
                    Math.Min(centerX + 1, Math.Max(view.Width - 1, 1)),
                    centerY);
                Point2D down = LcOpRedline.ScreenToCameraSpace(
                    view.Viewer,
                    centerX,
                    Math.Min(centerY + 1, Math.Max(view.Height - 1, 1)));
                double unit = (Math.Abs(right.X - center.X) + Math.Abs(down.Y - center.Y)) / 2.0;
                if (IsFinite(unit) && unit > 1e-9)
                    return unit * Math.Max(pixels, 1);
            }
            catch
            {
            }

            return 0.024;
        }

        private string PromptViewpointName(string defaultName)
        {
            using (var form = new Form())
            {
                form.Text = UiLocalizationService.Current.GetString("CommonViewpointNameTitle");
                form.Size = new Size(420, 130);
                form.StartPosition = FormStartPosition.CenterScreen;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                string initialText = defaultName;
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string clip = Clipboard.GetText().Trim();
                        if (!string.IsNullOrEmpty(clip) && !clip.Contains("\n") && !clip.Contains("\r"))
                            initialText = clip;
                    }
                }
                catch { }

                var label = new Label
                {
                    Text = UiLocalizationService.Current.GetString("CommonViewpointNameLabel"),
                    Left = 10,
                    Top = 10,
                    Width = 380
                };
                var textBox = new TextBox { Left = 10, Top = 35, Width = 385, Text = initialText };
                textBox.SelectAll();
                var okBtn = new Button
                {
                    Text = UiLocalizationService.Current.GetString("CommonOk"),
                    Left = 230,
                    Top = 70,
                    Width = 75,
                    DialogResult = DialogResult.OK
                };
                var cancelBtn = new Button
                {
                    Text = UiLocalizationService.Current.GetString("CommonCancel"),
                    Left = 315,
                    Top = 70,
                    Width = 75,
                    DialogResult = DialogResult.Cancel
                };

                form.Controls.AddRange(new Control[] { label, textBox, okBtn, cancelBtn });
                form.AcceptButton = okBtn;
                form.CancelButton = cancelBtn;

                if (form.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text))
                    return textBox.Text.Trim();

                return null;
            }
        }
    }
}
