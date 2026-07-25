using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using Application = Autodesk.Navisworks.Api.Application;
using Color = Autodesk.Navisworks.Api.Color;
using NavisHelper.Core;

namespace NavisHelper
{
    // =========================================================
    // Общий диалог выбора папки с возможностью вставки пути
    // =========================================================
    internal static class FolderPicker
    {
        public static string Show(string title, string initialPath = null)
        {
            using (var form = new Form
            {
                Text = title,
                Width = 550,
                Height = 150,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                TopMost = true,
                ShowInTaskbar = false
            })
            {
                var label = new Label
                {
                    Text = "Путь к папке (можно вставить Ctrl+V):",
                    Dock = DockStyle.Top,
                    Height = 22,
                    Padding = new Padding(8, 4, 0, 0),
                    Font = new Font("Segoe UI", 9f)
                };

                var textBox = new TextBox
                {
                    Name = "pathBox",
                    Text = initialPath ?? "",
                    Dock = DockStyle.Top,
                    Height = 24,
                    Font = new Font("Segoe UI", 9f)
                };
                textBox.Margin = new Padding(8, 0, 8, 0);

                var btnPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 40,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(8, 4, 8, 4)
                };

                var btnOk = new Button { Text = "OK", Width = 80, DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "Отмена", Width = 80, DialogResult = DialogResult.Cancel };
                var btnBrowse = new Button { Text = "Обзор...", Width = 80 };

                btnBrowse.Click += (s, e) =>
                {
                    var dlg = new FolderBrowserDialog
                    {
                        Description = title,
                        ShowNewFolderButton = true
                    };
                    if (!string.IsNullOrEmpty(textBox.Text) && Directory.Exists(textBox.Text))
                        dlg.SelectedPath = textBox.Text;
                    if (dlg.ShowDialog() == DialogResult.OK)
                        textBox.Text = dlg.SelectedPath;
                };

                btnPanel.Controls.Add(btnOk);
                btnPanel.Controls.Add(btnCancel);
                btnPanel.Controls.Add(btnBrowse);

                form.Controls.Add(textBox);
                form.Controls.Add(label);
                form.Controls.Add(btnPanel);
                form.AcceptButton = btnOk;
                form.CancelButton = btnCancel;

                if (form.ShowDialog() == DialogResult.OK)
                {
                    string path = textBox.Text.Trim().Trim('"');
                    if (Directory.Exists(path))
                        return path;

                    // Если папки нет — предложим создать
                    var create = MessageBox.Show(
                        "Папка не существует. Создать?\n" + path,
                        title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (create == DialogResult.Yes)
                    {
                        Directory.CreateDirectory(path);
                        return path;
                    }
                }
                return null;
            }
        }
    }

    // =====================================================
    // ВЫГРУЗИТЬ ЦВЕТА (Export)
    // =====================================================
    [Plugin("ExportColors", "CBC", DisplayName = "Выгрузить цвета")]
    [AddInPlugin(AddInLocation.None)]
    public class ExportColors : AddInPlugin
    {
        private const int TIMEOUT_FULL_SEC = 60;
        private const int TIMEOUT_LIMITED_SEC = 120;
        private const int MAX_DEPTH_FALLBACK = 3;

        public override int Execute(params string[] parameters)
        {
            IDisposable interactiveBusy = null;
            try
            {
                interactiveBusy = NavisHelper.Agent.AgentRuntime.BeginInteractiveOperation("Export colors");
                var doc = Application.ActiveDocument;
                var selection = doc.CurrentSelection.SelectedItems;

                if (selection.Count == 0)
                {
                    MessageBox.Show("Выделите объекты в дереве модели.",
                        "Выгрузить цвета", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 0;
                }

                // Выбор папки через форму с текстовым полем
                string folder = FolderPicker.Show("Выберите папку для сохранения файлов цветов");
                if (folder == null) return 0;

                // Спросить про перезапись
                bool overwrite = true;
                var existingFiles = Directory.GetFiles(folder, "*.txt");
                if (existingFiles.Length > 0)
                {
                    var answer = MessageBox.Show(
                        string.Format("В папке уже есть {0} .txt файлов.\n\nПерезаписывать существующие файлы?",
                            existingFiles.Length),
                        "Выгрузить цвета",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    overwrite = (answer == DialogResult.Yes);
                }

                int totalFiles = 0;
                int totalColors = 0;
                int skippedExisting = 0;
                var errors = new List<string>();

                var progressForm = CreateProgressForm("Выгрузка цветов");
                progressForm.Show();
                System.Windows.Forms.Application.DoEvents();

                var progressBar = progressForm.Controls["progressBar"] as ProgressBar;
                var progressLabel = progressForm.Controls["progressLabel"] as Label;

                try
                {
                    int selIndex = 0;
                    foreach (ModelItem selectedItem in selection)
                    {
                        selIndex++;
                        string itemName = selectedItem.DisplayName;
                        if (string.IsNullOrWhiteSpace(itemName))
                        {
                            errors.Add(string.Format("#{0}: пустое имя, пропущен", selIndex));
                            continue;
                        }

                        // Проверяем, существует ли файл (если не перезаписываем)
                        string safeFileName = SanitizeFileName(itemName) + ".txt";
                        string filePath = Path.Combine(folder, safeFileName);
                        if (!overwrite && File.Exists(filePath))
                        {
                            skippedExisting++;
                            continue;
                        }

                        // Обновляем прогресс
                        progressLabel.Text = string.Format("[{0}/{1}] {2}",
                            selIndex, selection.Count, itemName);
                        progressBar.Value = (int)((double)selIndex / selection.Count * 100);
                        System.Windows.Forms.Application.DoEvents();

                        // === Попытка 1: полная выгрузка, таймаут TIMEOUT_FULL_SEC ===
                        var sw = Stopwatch.StartNew();
                        var descendants = new List<ModelItem>();
                        bool collectTimedOut = !CollectDescendantsWithTimeout(
                            selectedItem, descendants, sw, TIMEOUT_FULL_SEC);

                        var lines = new List<string>();

                        if (!collectTimedOut)
                        {
                            // Сбор прошёл, теперь выгружаем цвета
                            foreach (var item in descendants)
                            {
                                if (sw.Elapsed.TotalSeconds >= TIMEOUT_FULL_SEC)
                                {
                                    collectTimedOut = true;
                                    break;
                                }
                                ExportItemColor(item, lines);
                            }
                        }

                        if (collectTimedOut)
                        {
                            // === Попытка 2: только MAX_DEPTH_FALLBACK уровней ===
                            progressLabel.Text = string.Format("[{0}/{1}] Повтор ({2} ур.): {3}",
                                selIndex, selection.Count, MAX_DEPTH_FALLBACK, itemName);
                            System.Windows.Forms.Application.DoEvents();

                            lines.Clear();
                            descendants.Clear();
                            sw.Restart();

                            bool collect2TimedOut = !CollectDescendantsLimitedWithTimeout(
                                selectedItem, descendants, 0, MAX_DEPTH_FALLBACK,
                                sw, TIMEOUT_LIMITED_SEC);

                            if (collect2TimedOut)
                            {
                                errors.Add(string.Format("{0} — таймаут на сборе ({1} ур.), пропущен", itemName, MAX_DEPTH_FALLBACK));
                                continue;
                            }

                            bool export2TimedOut = false;
                            foreach (var item in descendants)
                            {
                                if (sw.Elapsed.TotalSeconds >= TIMEOUT_LIMITED_SEC)
                                {
                                    export2TimedOut = true;
                                    break;
                                }
                                ExportItemColor(item, lines);
                            }

                            if (export2TimedOut)
                            {
                                errors.Add(string.Format("{0} — таймаут на выгрузке ({1} ур. > {2} сек), пропущен",
                                    itemName, MAX_DEPTH_FALLBACK, TIMEOUT_LIMITED_SEC));
                                continue;
                            }

                            errors.Add(string.Format("{0} — выгружено {1} ур. (полная > {2} сек)",
                                itemName, MAX_DEPTH_FALLBACK, TIMEOUT_FULL_SEC));
                        }

                        File.WriteAllLines(filePath, lines, Encoding.UTF8);
                        totalFiles++;
                        totalColors += lines.Count;
                    }
                }
                finally
                {
                    progressForm.Close();
                    progressForm.Dispose();
                }

                // Лог
                var logLines = new List<string>();
                logLines.Add("=== Выгрузка цветов: лог ===");
                logLines.Add(string.Format("Папка: {0}", folder));
                logLines.Add(string.Format("Выделено объектов: {0}", selection.Count));
                logLines.Add(string.Format("Создано файлов: {0}", totalFiles));
                logLines.Add(string.Format("Пропущено (уже есть): {0}", skippedExisting));
                logLines.Add(string.Format("Всего строк цветов: {0}", totalColors));
                if (errors.Count > 0)
                {
                    logLines.Add("");
                    logLines.Add("--- Предупреждения ---");
                    foreach (var err in errors) logLines.Add(err);
                }
                string logPath = Path.Combine(folder, "_export_log.txt");
                File.WriteAllLines(logPath, logLines, Encoding.UTF8);

                var msg = string.Format(
                    "Готово!\nСоздано файлов: {0}\nПропущено (уже есть): {1}\nВсего строк цветов: {2}\nПапка: {3}",
                    totalFiles, skippedExisting, totalColors, folder);
                if (errors.Count > 0)
                    msg += string.Format("\n\nПредупреждения ({0}):\n{1}\n\nЛог: {2}",
                        errors.Count, string.Join("\n", errors.Take(10)), logPath);

                MessageBox.Show(msg, "Выгрузить цвета", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message + "\n" + ex.StackTrace,
                    "Выгрузить цвета", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (interactiveBusy != null)
                    interactiveBusy.Dispose();
            }
            return 0;
        }

        private void ExportItemColor(ModelItem item, List<string> lines)
        {
            string name = item.DisplayName;
            if (string.IsNullOrWhiteSpace(name)) return;

            try
            {
                Color color = null;
                double transparency = 0;

                if (item.HasGeometry)
                {
                    color = item.Geometry.PermanentColor;
                    transparency = item.Geometry.PermanentTransparency;
                }
                else
                {
                    var firstGeom = item.FindFirstGeometry();
                    if (firstGeom != null)
                    {
                        color = firstGeom.PermanentColor;
                        transparency = firstGeom.PermanentTransparency;
                    }
                }

                if (color == null) return;

                int r = (int)Math.Round(color.R * 255.0);
                int g = (int)Math.Round(color.G * 255.0);
                int b = (int)Math.Round(color.B * 255.0);
                int transp = (int)Math.Round(transparency * 100.0);

                lines.Add(string.Format("{0};{1},{2},{3};{4}", name, r, g, b, transp));
            }
            catch { }
        }

        /// <summary>
        /// Сбор всех потомков с проверкой таймаута. Возвращает false если таймаут.
        /// </summary>
        private bool CollectDescendantsWithTimeout(ModelItem item, List<ModelItem> result,
            Stopwatch sw, int timeoutSec)
        {
            if (sw.Elapsed.TotalSeconds >= timeoutSec) return false;
            result.Add(item);
            foreach (ModelItem child in item.Children)
            {
                if (!CollectDescendantsWithTimeout(child, result, sw, timeoutSec))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Сбор потомков до maxDepth уровней с проверкой таймаута.
        /// </summary>
        private bool CollectDescendantsLimitedWithTimeout(ModelItem item, List<ModelItem> result,
            int currentDepth, int maxDepth, Stopwatch sw, int timeoutSec)
        {
            if (sw.Elapsed.TotalSeconds >= timeoutSec) return false;
            result.Add(item);
            if (currentDepth >= maxDepth) return true;
            foreach (ModelItem child in item.Children)
            {
                if (!CollectDescendantsLimitedWithTimeout(child, result,
                    currentDepth + 1, maxDepth, sw, timeoutSec))
                    return false;
            }
            return true;
        }

        internal static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        internal static Form CreateProgressForm(string title)
        {
            var form = new Form
            {
                Text = title,
                Width = 500,
                Height = 120,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                ControlBox = false,
                TopMost = true,
                ShowInTaskbar = false
            };

            var label = new Label
            {
                Name = "progressLabel",
                Text = "Подготовка...",
                Dock = DockStyle.Top,
                Height = 30,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Padding = new Padding(10, 5, 10, 0),
                Font = new Font("Segoe UI", 9f)
            };

            var bar = new ProgressBar
            {
                Name = "progressBar",
                Dock = DockStyle.Bottom,
                Height = 30,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous
            };

            form.Controls.Add(label);
            form.Controls.Add(bar);
            return form;
        }
    }

    // =====================================================
    // ЗАГРУЗИТЬ ЦВЕТА (Import)
    // =====================================================
    [Plugin("ImportColors", "CBC", DisplayName = "Загрузить цвета")]
    [AddInPlugin(AddInLocation.None)]
    public class ImportColors : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            IDisposable interactiveBusy = null;
            try
            {
                interactiveBusy = NavisHelper.Agent.AgentRuntime.BeginInteractiveOperation("Import colors");
                var doc = Application.ActiveDocument;
                var selection = doc.CurrentSelection.SelectedItems;

                if (selection.Count == 0)
                {
                    MessageBox.Show("Выделите объекты в дереве модели, куда применить цвета.",
                        "Загрузить цвета", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 0;
                }

                // Выбор папки через форму с текстовым полем
                string folder = FolderPicker.Show("Выберите папку с файлами цветов");
                if (folder == null) return 0;

                // Индексируем все .txt файлы в папке
                var colorFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var filePath in Directory.GetFiles(folder, "*.txt"))
                {
                    string fileNameNoExt = Path.GetFileNameWithoutExtension(filePath);
                    colorFiles[fileNameNoExt] = filePath;
                }

                if (colorFiles.Count == 0)
                {
                    MessageBox.Show("В папке нет .txt файлов.", "Загрузить цвета",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 0;
                }

                int totalApplied = 0;
                int totalMatched = 0;
                int totalNotFound = 0;
                var matchedFiles = new List<string>();
                var unmatchedItems = new List<string>();
                var itemLogs = new List<string>();

                var progressForm = ExportColors.CreateProgressForm("Загрузка цветов");
                progressForm.Show();
                System.Windows.Forms.Application.DoEvents();

                var progressBar = progressForm.Controls["progressBar"] as ProgressBar;
                var progressLabel = progressForm.Controls["progressLabel"] as Label;

                try
                {
                    int selIndex = 0;
                    foreach (ModelItem selectedItem in selection)
                    {
                        selIndex++;
                        string itemName = selectedItem.DisplayName;
                        if (string.IsNullOrWhiteSpace(itemName)) continue;

                        string safeName = ExportColors.SanitizeFileName(itemName);

                        // Ищем файл по имени объекта
                        string colorFilePath = null;
                        if (colorFiles.ContainsKey(safeName))
                            colorFilePath = colorFiles[safeName];
                        else if (colorFiles.ContainsKey(itemName))
                            colorFilePath = colorFiles[itemName];

                        if (colorFilePath == null)
                        {
                            unmatchedItems.Add(itemName);
                            totalNotFound++;
                            continue;
                        }

                        matchedFiles.Add(itemName);

                        progressLabel.Text = string.Format("[{0}/{1}] {2}",
                            selIndex, selection.Count, itemName);
                        progressBar.Value = (int)((double)selIndex / selection.Count * 100);
                        System.Windows.Forms.Application.DoEvents();

                        var colorMap = ReadColorFile(colorFilePath);
                        if (colorMap.Count == 0)
                        {
                            itemLogs.Add(string.Format("--- {0} ---\nФайл: {1}\nЦветов в файле: 0 (пустой/ошибка парсинга)", itemName, colorFilePath));
                            continue;
                        }

                        // Собираем потомков ТОЛЬКО этого объекта, до 3 уровней
                        var descendants = new List<ModelItem>();
                        CollectDescendantsLimited(selectedItem, descendants, 0, 3);

                        // Собираем уникальные имена потомков для лога
                        var treeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var item in descendants)
                        {
                            if (!string.IsNullOrWhiteSpace(item.DisplayName))
                                treeNames.Add(item.DisplayName);
                        }

                        // Группируем по имени
                        var nameGroups = new Dictionary<string, ModelItemCollection>(StringComparer.OrdinalIgnoreCase);
                        foreach (var item in descendants)
                        {
                            string name = item.DisplayName;
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            if (!colorMap.ContainsKey(name)) continue;

                            if (!nameGroups.ContainsKey(name))
                                nameGroups[name] = new ModelItemCollection();
                            nameGroups[name].Add(item);
                        }

                        // Применяем цвета
                        int itemApplied = 0;
                        foreach (var kvp in nameGroups)
                        {
                            var entry = colorMap[kvp.Key];
                            var nwColor = Autodesk.Navisworks.Api.Color.FromByteRGB(
                                (byte)entry.R, (byte)entry.G, (byte)entry.B);

                            doc.Models.OverridePermanentColor(kvp.Value, nwColor);

                            if (entry.Transparency > 0)
                            {
                                doc.Models.OverridePermanentTransparency(kvp.Value, entry.Transparency / 100.0);
                            }

                            itemApplied += kvp.Value.Count;
                            totalApplied += kvp.Value.Count;
                        }

                        // Подробный лог для этого объекта
                        var itemLog = new StringBuilder();
                        itemLog.AppendLine(string.Format("--- {0} ---", itemName));
                        itemLog.AppendLine(string.Format("Файл: {0}", colorFilePath));
                        itemLog.AppendLine(string.Format("Имён в файле: {0}", colorMap.Count));
                        itemLog.AppendLine(string.Format("Потомков в дереве: {0} (уник. имён: {1})", descendants.Count, treeNames.Count));
                        itemLog.AppendLine(string.Format("Совпало имён: {0}, применено к объектам: {1}", nameGroups.Count, itemApplied));

                        // Детали по каждому имени из файла
                        var notFoundInTree = new List<string>();
                        var foundInTree = new List<string>();
                        foreach (var colorName in colorMap.Keys)
                        {
                            if (nameGroups.ContainsKey(colorName))
                                foundInTree.Add(string.Format("  [OK] {0} -> {1} шт.", colorName, nameGroups[colorName].Count));
                            else if (treeNames.Contains(colorName))
                                foundInTree.Add(string.Format("  [???] {0} — есть в дереве, но не попал в группу", colorName));
                            else
                                notFoundInTree.Add(string.Format("  [НЕТ] {0}", colorName));
                        }
                        if (foundInTree.Count > 0)
                        {
                            itemLog.AppendLine("Найдены:");
                            foreach (var l in foundInTree) itemLog.AppendLine(l);
                        }
                        if (notFoundInTree.Count > 0)
                        {
                            itemLog.AppendLine("Не найдены в дереве:");
                            foreach (var l in notFoundInTree) itemLog.AppendLine(l);
                        }

                        itemLogs.Add(itemLog.ToString());
                        totalMatched++;
                    }
                }
                finally
                {
                    progressForm.Close();
                    progressForm.Dispose();
                }

                // Лог
                var logLines = new List<string>();
                logLines.Add("=== Загрузка цветов: лог ===");
                logLines.Add(string.Format("Папка: {0}", folder));
                logLines.Add(string.Format("Файлов в папке: {0}", colorFiles.Count));
                logLines.Add(string.Format("Выделено объектов: {0}", selection.Count));
                logLines.Add(string.Format("Совпало файлов: {0}", totalMatched));
                logLines.Add(string.Format("Не найдено файлов: {0}", totalNotFound));
                logLines.Add(string.Format("Применено к объектам: {0}", totalApplied));
                logLines.Add("");
                if (matchedFiles.Count > 0)
                {
                    logLines.Add("--- Совпавшие ---");
                    foreach (var n in matchedFiles) logLines.Add("[OK] " + n);
                }
                if (unmatchedItems.Count > 0)
                {
                    logLines.Add("");
                    logLines.Add("--- Не найден файл ---");
                    foreach (var n in unmatchedItems) logLines.Add("[НЕТ] " + n);
                }

                // Подробные логи по каждому объекту
                if (itemLogs.Count > 0)
                {
                    logLines.Add("");
                    logLines.Add("==============================");
                    logLines.Add("ПОДРОБНОСТИ ПО КАЖДОМУ ОБЪЕКТУ");
                    logLines.Add("==============================");
                    foreach (var il in itemLogs)
                    {
                        logLines.Add("");
                        logLines.Add(il);
                    }
                }

                string logPath = Path.Combine(folder, "_import_log.txt");
                File.WriteAllLines(logPath, logLines, Encoding.UTF8);

                var msg = string.Format(
                    "Готово!\nСовпало файлов: {0} из {1}\nПрименено к объектам: {2}\nНе найдено файлов: {3}\n\nЛог: {4}",
                    totalMatched, selection.Count, totalApplied, totalNotFound, logPath);
                MessageBox.Show(msg, "Загрузить цвета", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка: " + ex.Message + "\n" + ex.StackTrace,
                    "Загрузить цвета", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (interactiveBusy != null)
                    interactiveBusy.Dispose();
            }
            return 0;
        }

        private Dictionary<string, ColorEntry> ReadColorFile(string filePath)
        {
            var colorMap = new Dictionary<string, ColorEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(filePath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(';');
                if (parts.Length < 2) continue;

                string name = parts[0].Trim();
                if (string.IsNullOrEmpty(name)) continue;

                try
                {
                    var color = ColorParser.ParseColor(parts[1].Trim());
                    int transparency = 0;
                    if (parts.Length >= 3)
                        int.TryParse(parts[2].Trim(), out transparency);

                    colorMap[name] = new ColorEntry
                    {
                        R = color.R,
                        G = color.G,
                        B = color.B,
                        Transparency = transparency
                    };
                }
                catch { }
            }
            return colorMap;
        }

        private struct ColorEntry
        {
            public int R, G, B, Transparency;
        }

        private void CollectDescendantsLimited(ModelItem item, List<ModelItem> result, int depth, int maxDepth)
        {
            result.Add(item);
            if (depth >= maxDepth) return;
            foreach (ModelItem child in item.Children)
                CollectDescendantsLimited(child, result, depth + 1, maxDepth);
        }

        private bool CollectDescendantsWithTimeout(ModelItem item, List<ModelItem> result,
            Stopwatch sw, int timeoutSec)
        {
            if (sw.Elapsed.TotalSeconds >= timeoutSec) return false;
            result.Add(item);
            foreach (ModelItem child in item.Children)
            {
                if (!CollectDescendantsWithTimeout(child, result, sw, timeoutSec))
                    return false;
            }
            return true;
        }
    }
}
