using System;
using System.Linq;
using System.Windows;
using Autodesk.Navisworks.Api;
using NavisHelper.Interfaces;
using Application = Autodesk.Navisworks.Api.Application;

namespace NavisHelper.Dev
{
    /// <summary>
    /// Пример тестового скрипта. Показывает информацию о выделенных элементах.
    /// </summary>
    public class SelectionInfoScript : IDevScript
    {
        public string Name => "Инфо о выделении";

        public void Run()
        {
            var doc = Application.ActiveDocument;
            var selection = doc.CurrentSelection.SelectedItems;

            if (selection.Count == 0)
            {
                MessageBox.Show("Ничего не выделено.", Name);
                return;
            }

            var names = selection
                .Take(20)
                .Select(item => $"  {item.DisplayName ?? "(без имени)"}")
                .ToList();

            var msg = $"Выделено элементов: {selection.Count}\n\n" +
                      string.Join("\n", names);

            if (selection.Count > 20)
                msg += $"\n  ... и ещё {selection.Count - 20}";

            MessageBox.Show(msg, Name);
        }
    }

    /// <summary>
    /// Габариты выделенных объектов: длина (X), ширина (Y), высота (Z).
    /// </summary>
    public class BoundingBoxInfoScript : IDevScript
    {
        public string Name => "Габариты выделения";

        public void Run()
        {
            var doc = Application.ActiveDocument;
            var selection = doc.CurrentSelection.SelectedItems;

            if (selection.Count == 0)
            {
                MessageBox.Show("Ничего не выделено.", Name);
                return;
            }

            // Общий bounding box всех выделенных
            var bbox = selection.BoundingBox();
            if (bbox == null)
            {
                MessageBox.Show("Не удалось определить габариты.", Name);
                return;
            }

            double dx = bbox.Max.X - bbox.Min.X;
            double dy = bbox.Max.Y - bbox.Min.Y;
            double dz = bbox.Max.Z - bbox.Min.Z;

            string units = GetUnitsLabel(doc.Units);
            double toMm = ToMm(doc.Units);

            var msg = $"Выделено: {selection.Count} объектов\n\n" +
                      $"Габариты (в единицах модели — {units}):\n" +
                      $"  X (длина):  {dx:F4} {units}\n" +
                      $"  Y (ширина): {dy:F4} {units}\n" +
                      $"  Z (высота): {dz:F4} {units}\n\n" +
                      $"Габариты (мм):\n" +
                      $"  X: {dx * toMm:F1} мм\n" +
                      $"  Y: {dy * toMm:F1} мм\n" +
                      $"  Z: {dz * toMm:F1} мм\n\n" +
                      $"Min: ({bbox.Min.X:F4}, {bbox.Min.Y:F4}, {bbox.Min.Z:F4})\n" +
                      $"Max: ({bbox.Max.X:F4}, {bbox.Max.Y:F4}, {bbox.Max.Z:F4})";

            // По каждому объекту отдельно (до 10)
            if (selection.Count > 1 && selection.Count <= 10)
            {
                msg += "\n\n--- По объектам ---";
                foreach (var item in selection)
                {
                    var ib = item.BoundingBox();
                    if (ib == null) continue;
                    double ix = ib.Max.X - ib.Min.X;
                    double iy = ib.Max.Y - ib.Min.Y;
                    double iz = ib.Max.Z - ib.Min.Z;
                    string name = item.DisplayName ?? "(без имени)";
                    if (name.Length > 40) name = name.Substring(0, 37) + "...";
                    msg += $"\n{name}: {ix * toMm:F1} x {iy * toMm:F1} x {iz * toMm:F1} мм";
                }
            }

            MessageBox.Show(msg, Name);
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

    /// <summary>
    /// Пример: подсчёт элементов с геометрией.
    /// </summary>
    public class CountGeometryScript : IDevScript
    {
        public string Name => "Подсчёт геометрии";

        public void Run()
        {
            var doc = Application.ActiveDocument;
            var selection = doc.CurrentSelection.SelectedItems;

            if (selection.Count == 0)
            {
                MessageBox.Show("Выделите элементы.", Name);
                return;
            }

            int withGeom = 0;
            int withoutGeom = 0;

            foreach (var item in selection)
            {
                foreach (var desc in item.DescendantsAndSelf)
                {
                    if (desc.HasGeometry)
                        withGeom++;
                    else
                        withoutGeom++;
                }
            }

            MessageBox.Show(
                $"Всего потомков: {withGeom + withoutGeom}\n" +
                $"С геометрией: {withGeom}\n" +
                $"Без геометрии: {withoutGeom}",
                Name);
        }
    }
}
