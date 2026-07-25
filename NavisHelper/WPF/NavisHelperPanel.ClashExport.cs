using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace NavisHelper.WPF
{
    public partial class NavisHelperPanel
    {
        private void ExportSelectedClashesToBcf()
        {
            try
            {
                var row = _clashGrid?.SelectedItem;
                if (row == null)
                {
                    MessageBox.Show("Выберите коллизию для экспорта.", "Export BCF");
                    return;
                }

                dynamic dyn = row;
                var clashes = GetClashResultsFromRow(row);
                var clash = clashes.FirstOrDefault();
                if (clash == null)
                {
                    MessageBox.Show("Неверная запись коллизии.", "Export BCF");
                    return;
                }

                var name = (string)(dyn?.Name ?? "(clash)");
                var itemA = (string)(dyn?.ItemA ?? "?");
                var itemB = (string)(dyn?.ItemB ?? "?");
                var assignedTo = TryGetClashAssignedTo(clash);

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Экспорт в BCF",
                    Filter = "BCF (*.bcf)|*.bcf|ZIP (*.zip)|*.zip|Все файлы|*.*",
                    FileName = $"{SanitizeFileName(name)}.bcf",
                    DefaultExt = ".bcf"
                };
                if (dlg.ShowDialog() != true) return;

                var point = clash.Center;
                double cx = point == null ? 0 : point.X;
                double cy = point == null ? 0 : point.Y;
                double cz = point == null ? 0 : point.Z;

                var xml = $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                          $"<markup>\n" +
                          $"  <topic>\n" +
                          $"    <title>{EncodeXml(name)}</title>\n" +
                          $"    <description>Point: {cx:F6}, {cy:F6}, {cz:F6}; raw clashes: {clashes.Count}</description>\n" +
                          $"    <labels>\n" +
                          $"      <label>Clash</label>\n" +
                          $"    </labels>\n" +
                          $"    <creationDate>{DateTime.Now:yyyy-MM-ddTHH:mm:ss}</creationDate>\n" +
                          $"  </topic>\n" +
                          $"  <properties>\n" +
                          $"    <itemA>{EncodeXml(itemA)}</itemA>\n" +
                          $"    <itemB>{EncodeXml(itemB)}</itemB>\n" +
                          $"    <assignedTo>{EncodeXml(assignedTo)}</assignedTo>\n" +
                          $"  </properties>\n" +
                          $"</markup>";
                using (var fs = new FileStream(dlg.FileName, FileMode.Create, FileAccess.Write))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create, false))
                {
                    var markupEntry = archive.CreateEntry("markup/markup.bcf");
                    using (var writer = new StreamWriter(markupEntry.Open(), System.Text.Encoding.UTF8))
                    {
                        writer.Write(xml);
                    }

                    var projectEntry = archive.CreateEntry("markup/project.bcf");
                    using (var writer = new StreamWriter(projectEntry.Open(), System.Text.Encoding.UTF8))
                    {
                        writer.Write("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<project></project>");
                    }
                }

                SetGlobalStatus($"BCF экспортирован: {dlg.FileName}", Brushes.DarkGreen);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message, "Export BCF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
