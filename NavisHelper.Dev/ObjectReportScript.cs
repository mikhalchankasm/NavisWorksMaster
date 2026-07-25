using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using NavisHelper.Interfaces;
using Application = Autodesk.Navisworks.Api.Application;
using ComApi = Autodesk.Navisworks.Api.Interop.ComApi;
using MessageBox = System.Windows.MessageBox;

namespace NavisHelper.Dev
{
    /// <summary>
    /// Отчёт по объекту: габариты + MD файл + скриншоты с разных ракурсов.
    /// </summary>
    public class ObjectReportScript : IDevScript
    {
        public string Name => "Отчёт по объекту";

        private static readonly string BaseDir = Path.Combine(Path.GetTempPath(), "NavisHelper", "ObjectReport");
        private const int ScreenWidth = 3840;
        private const int ScreenHeight = 2160;

        // Стандартные виды для Navisworks (Rotation3D: W, X, Y, Z)
        private static readonly (string Label, string File, double W, double X, double Y, double Z)[] Views = new[]
        {
            ("Спереди", "front",   0.7071,  0.7071,  0.0,     0.0),
            ("Справа",  "right",   0.5,     0.5,     0.5,     0.5),
        };

        public void Run()
        {
            var doc = Application.ActiveDocument;
            var selection = doc.CurrentSelection.SelectedItems;

            if (selection.Count == 0)
            {
                MessageBox.Show("Ничего не выделено.", Name);
                return;
            }

            var mainItem = selection.First();
            string objectName = mainItem.DisplayName ?? mainItem.ClassDisplayName ?? "Unnamed";

            var bbox = selection.BoundingBox();
            if (bbox == null)
            {
                MessageBox.Show("Не удалось определить габариты.", Name);
                return;
            }

            double dx = bbox.Max.X - bbox.Min.X;
            double dy = bbox.Max.Y - bbox.Min.Y;
            double dz = bbox.Max.Z - bbox.Min.Z;
            double toMm = ToMm(doc.Units);
            string units = GetUnitsLabel(doc.Units);

            string safeName = SanitizeFileName(objectName);
            if (safeName.Length > 100) safeName = safeName.Substring(0, 100);
            string folderPath = Path.Combine(BaseDir, safeName);

            try
            {
                if (!Directory.Exists(BaseDir))
                    Directory.CreateDirectory(BaseDir);
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось создать папку:\n{folderPath}\n\n{ex.Message}", Name);
                return;
            }

            // MD файл
            string mdPath = Path.Combine(folderPath, $"{safeName}.md");
            var md = new StringBuilder();
            md.AppendLine($"# {objectName}");
            md.AppendLine();
            md.AppendLine($"**Дата:** {DateTime.Now:yyyy-MM-dd HH:mm}");
            md.AppendLine();
            md.AppendLine("## Габариты");
            md.AppendLine();
            md.AppendLine($"| Ось | {units} | мм |");
            md.AppendLine("|------|---------|------|");
            md.AppendLine($"| X (длина) | {dx:F4} | {dx * toMm:F1} |");
            md.AppendLine($"| Y (ширина) | {dy:F4} | {dy * toMm:F1} |");
            md.AppendLine($"| Z (высота) | {dz:F4} | {dz * toMm:F1} |");
            md.AppendLine();
            md.AppendLine("## Координаты");
            md.AppendLine();
            md.AppendLine($"- **Min:** ({bbox.Min.X:F4}, {bbox.Min.Y:F4}, {bbox.Min.Z:F4})");
            md.AppendLine($"- **Max:** ({bbox.Max.X:F4}, {bbox.Max.Y:F4}, {bbox.Max.Z:F4})");
            md.AppendLine($"- **Центр:** ({bbox.Center.X:F4}, {bbox.Center.Y:F4}, {bbox.Center.Z:F4})");
            md.AppendLine();

            if (selection.Count > 1)
            {
                md.AppendLine($"**Выделено объектов:** {selection.Count}");
                md.AppendLine();
                foreach (var item in selection.Take(20))
                {
                    var ib = item.BoundingBox();
                    if (ib == null) continue;
                    double ix = (ib.Max.X - ib.Min.X) * toMm;
                    double iy = (ib.Max.Y - ib.Min.Y) * toMm;
                    double iz = (ib.Max.Z - ib.Min.Z) * toMm;
                    md.AppendLine($"- {item.DisplayName ?? "(без имени)"}: {ix:F1} x {iy:F1} x {iz:F1} мм");
                }
                md.AppendLine();
            }

            md.AppendLine("## Скриншоты");
            md.AppendLine();

            // Снимаем выделение
            doc.CurrentSelection.Clear();

            var savedVp = doc.CurrentViewpoint.CreateCopy();
            int shotCount = 0;
            string status;

            try
            {
                // Скриншот 1: текущий вид (zoom на объект)
                ZoomToBox(doc, bbox);
                string name1 = $"view_current_{safeName}.png";
                if (TakeScreenshot(Path.Combine(folderPath, name1)))
                {
                    md.AppendLine($"![Текущий вид]({name1})");
                    md.AppendLine();
                    shotCount++;
                }

                // Скриншоты 2-7: стандартные виды
                foreach (var v in Views)
                {
                    SetStandardView(doc, bbox, v.W, v.X, v.Y, v.Z);
                    string fileName = $"view_{v.File}_{safeName}.png";
                    if (TakeScreenshot(Path.Combine(folderPath, fileName)))
                    {
                        md.AppendLine($"![{v.Label}]({fileName})");
                        md.AppendLine();
                        shotCount++;
                    }
                }

                status = $"Скриншотов: {shotCount} ({ScreenWidth}x{ScreenHeight})";
            }
            catch (Exception ex)
            {
                status = $"Скриншоты: ошибка — {ex.Message}";
            }
            finally
            {
                doc.CurrentViewpoint.CopyFrom(savedVp);
            }

            try
            {
                File.WriteAllText(mdPath, md.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось записать MD:\n{ex.Message}", Name);
                return;
            }

            MessageBox.Show(
                $"Отчёт сохранён:\n{folderPath}\n\n" +
                $"Объект: {objectName}\n" +
                $"Габариты: {dx * toMm:F1} x {dy * toMm:F1} x {dz * toMm:F1} мм\n" +
                $"{status}\n" +
                $"MD: {Path.GetFileName(mdPath)}",
                Name);
        }

        // ===== Виды =====

        /// <summary>Установить стандартный вид по кватерниону + zoom на bbox.</summary>
        private void SetStandardView(Document doc, BoundingBox3D bbox, double w, double x, double y, double z)
        {
            var vp = doc.CurrentViewpoint.CreateCopy();
            vp.Projection = ViewpointProjection.Orthographic;
            vp.Rotation = new Rotation3D(w, x, y, z);

            // Позиция — далеко от центра чтобы ZoomBox отработал
            double size = Math.Max(Math.Max(
                bbox.Max.X - bbox.Min.X,
                bbox.Max.Y - bbox.Min.Y),
                bbox.Max.Z - bbox.Min.Z);
            vp.Position = new Point3D(bbox.Center.X, bbox.Center.Y, bbox.Center.Z + size * 3);

            vp.ZoomBox(bbox);
            doc.CurrentViewpoint.CopyFrom(vp);
            doc.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
            System.Threading.Thread.Sleep(300);
        }

        /// <summary>Zoom на bbox в текущем виде.</summary>
        private void ZoomToBox(Document doc, BoundingBox3D bbox)
        {
            var vp = doc.CurrentViewpoint.CreateCopy();
            vp.ZoomBox(bbox);
            doc.CurrentViewpoint.CopyFrom(vp);
            doc.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
            System.Threading.Thread.Sleep(300);
        }

        // ===== Скриншот =====

        private bool TakeScreenshot(string filePath)
        {
            try
            {
                ComApi.InwOpState10 state = ComApiBridge.State;
                var view = state.CurrentView;

                var anon = state.ObjectFactory(ComApi.nwEObjectType.eObjectType_nwOpAnonView)
                    as ComApi.InwOpAnonView;
                anon.ViewPoint = (ComApi.InwNvViewPoint)((ComApi.InwOpAnonView)view).ViewPoint.Copy();

                object picObj = state.CreatePicture(anon, Type.Missing, ScreenWidth, ScreenHeight);
                if (picObj == null) return false;

                Image img = ConvertFromIPicture(picObj) as Image;
                if (img == null) return false;

                img.Save(filePath, ImageFormat.Png);
                img.Dispose();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object ConvertFromIPicture(object image)
        {
            return OleConverter.Instance.Convert(image);
        }

        private class OleConverter : AxHost
        {
            public static readonly OleConverter Instance = new OleConverter();
            private OleConverter() : base(Guid.Empty.ToString()) { }
            public object Convert(object image)
            {
                return GetPictureFromIPicture(image);
            }
        }

        // ===== Утилиты =====

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString().Trim();
        }

        private static string GetUnitsLabel(Units u)
        {
            switch (u)
            {
                case Units.Millimeters: return "мм";
                case Units.Centimeters: return "см";
                case Units.Meters: return "м";
                case Units.Kilometers: return "км";
                case Units.Inches: return "дюйм";
                case Units.Feet: return "фут";
                case Units.Yards: return "ярд";
                case Units.Miles: return "миля";
                default: return "?";
            }
        }

        private static double ToMm(Units u)
        {
            switch (u)
            {
                case Units.Millimeters: return 1.0;
                case Units.Centimeters: return 10.0;
                case Units.Meters: return 1000.0;
                case Units.Kilometers: return 1000000.0;
                case Units.Inches: return 25.4;
                case Units.Feet: return 304.8;
                case Units.Yards: return 914.4;
                case Units.Miles: return 1609344.0;
                default: return 1000.0;
            }
        }
    }
}
