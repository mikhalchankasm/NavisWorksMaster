using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Application = Autodesk.Navisworks.Api.Application;
using Color = System.Drawing.Color;
using NavisHelper.Core;
using NavisHelper.Core.Localization;


namespace NavisHelper
{
    [Plugin("ColorsByName", "CBC", DisplayName = "Change colors by names")]
    [AddInPlugin(AddInLocation.None)]
    public class ColorsByName : AddInPlugin
    {
        private static readonly (string category, string name)[] NameProps = new[]
        {
            ("Item", "Name"),         // EN
            ("Элемент", "Имя"),       // RU
            ("Объект", "Имя"),        // RU, иногда
            ("Element", "Name"),      // EN, иногда
            ("", "Name"),             // Без категории EN
            ("", "Имя"),              // Без категории RU
        };
        private static readonly (string category, string name)[] InternalProps = new[]
        {
            ("LcOaNode", "Name"),     // Internal name (обычно для большинства моделей)
            ("LcOaNode", "LcOaSceneBaseUserName") // Иногда пользовательское имя
        };

        public override int Execute(params string[] parameters)
        {
            string colorFilePath = GetColorFilePath(parameters);
            try
            {
                if (string.IsNullOrEmpty(colorFilePath))
                    return 0;

                Document doc = Application.ActiveDocument;
                var lines = File.ReadAllLines(colorFilePath, Encoding.Default).ToList();
                var notFoundNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var logLines = new List<string>();

                // Лог времени
                var startTime = DateTime.Now;

                var progress = Autodesk.Navisworks.Api.Application.BeginProgress(
                    UiLocalizationService.Current.GetString("ColorsByNameProgressCaption"));
                try
                {
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (progress.IsCanceled)
                            break;

                        ProcessColorLineUniversal(lines[i], doc, notFoundNames);
                        if (lines.Count > 0)
                        {
                            double percent = (double)(i + 1) / lines.Count;
                            progress.Update(percent);
                        }
                    }
                    progress.Update(1.0);
                }
                finally
                {
                    Autodesk.Navisworks.Api.Application.EndProgress();
                }

                ApplySpecialTransparency(doc);
                //HideClashSite();

                // Логирование
                var endTime = DateTime.Now;
                logLines.Add($"Время поиска: {startTime:yyyy-MM-dd HH:mm:ss} — {endTime:yyyy-MM-dd HH:mm:ss}");
                foreach (var line in lines)
                {
                    var parts = line.Split(';');
                    if (parts.Length == 0)
                        continue;
                    var name = parts[0].Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        continue;
                    if (notFoundNames.Contains(name))
                        logLines.Add($"[NAME] {name} — НЕ найден в структуре модели");
                    else
                        logLines.Add($"[NAME] {name} — найден в структуре модели");
                }
                string nwdPath = doc.FileName;
                string logPath = string.IsNullOrEmpty(nwdPath)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "colorbyname_log.txt")
                    : Path.Combine(Path.GetDirectoryName(nwdPath), Path.GetFileNameWithoutExtension(nwdPath) + "_colorbyname_log.txt");
                File.WriteAllLines(logPath, logLines, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при обработке цветов: {ex.Message}\n{ex.StackTrace}", "ColorsByName", colorFilePath);
                return 0;
            }
            return 0;
        }

        /// <summary>
        /// Получает путь к файлу с цветами из параметров или диалога
        /// </summary>
        private string GetColorFilePath(string[] parameters)
        {
            if (parameters == null || parameters.Length == 0 || !File.Exists(parameters[0]))
            {
                using (var openFileDialog = new OpenFileDialog
                {
                    Filter = UiLocalizationService.Current.GetString("CommonFileFilterTextAll"),
                    RestoreDirectory = true
                })
                {
                    return openFileDialog.ShowDialog() == DialogResult.OK
                        ? openFileDialog.FileName
                        : null;
                }
            }
            else
            {
                return parameters[0];
            }
        }

        private void ProcessColorLineUniversal(string line, Document doc, ISet<string> notFoundNames)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            var parts = line.Split(';');
            if (parts.Length < 2) return;

            string itemName = parts[0].Trim();
            string rgbString = parts[1].Trim();
            float? transparency = null;
            if (parts.Length >= 3)
            {
                if (float.TryParse(parts[2].Trim().Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float t))
                    transparency = t / 100f;
            }

            int r = 0, g = 0, b = 0;
            try
            {
                var sysColor = ColorParser.ParseColor(rgbString);
                r = sysColor.R;
                g = sysColor.G;
                b = sysColor.B;
            }
            catch (Exception)
            {
                return; // некорректный цвет
            }

            // 1. Поиск по internal name
            foreach (var (cat, prop) in InternalProps)
            {
                var search = new Search();
                search.SearchConditions.Add(
                    SearchCondition.HasPropertyByName(cat, prop).EqualValue(VariantData.FromDisplayString(itemName))
                );
                search.Selection.SelectAll();
                var foundItems = search.FindAll(doc, false);
                if (foundItems.Count > 0)
                {
                    ApplyColor(doc, foundItems, r, g, b, transparency);
                    return;
                }
            }
            // 2. Поиск по DisplayName (все варианты)
            foreach (var (cat, prop) in NameProps)
            {
                var search = new Search();
                search.SearchConditions.Add(
                    SearchCondition.HasPropertyByDisplayName(cat, prop).EqualValue(VariantData.FromDisplayString(itemName))
                );
                search.Selection.SelectAll();
                var foundItems = search.FindAll(doc, false);
                if (foundItems.Count > 0)
                {
                    ApplyColor(doc, foundItems, r, g, b, transparency);
                    return;
                }
            }
            // 3. Поиск по DisplayName элемента (fallback)
            var foundByDisplayName = new ModelItemCollection();
            var rootItems = doc.Models.CreateCollectionFromRootItems();
            foreach (ModelItem root in rootItems)
                CollectByDisplayName(root, itemName, foundByDisplayName);
            if (foundByDisplayName.Count > 0)
            {
                ApplyColor(doc, foundByDisplayName, r, g, b, transparency);
                return;
            }
            // Не найдено
            notFoundNames.Add(itemName);
        }

        private void ApplyColor(Document doc, ModelItemCollection items, int r, int g, int b, float? transparency)
        {
            var sysColor = Color.FromArgb(r, g, b);
            var nwColor = Autodesk.Navisworks.Api.Color.FromByteRGB(sysColor.R, sysColor.G, sysColor.B);
            doc.Models.OverridePermanentColor(items, nwColor);
            if (transparency.HasValue)
                doc.Models.OverridePermanentTransparency(items, transparency.Value);
        }

        private void CollectByDisplayName(ModelItem item, string name, ModelItemCollection found)
        {
            if (!string.IsNullOrEmpty(item.DisplayName) && string.Equals(item.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                found.Add(item);
            foreach (ModelItem child in item.Children)
                CollectByDisplayName(child, name, found);
        }

        /// <summary>
        /// Применяет спец. прозрачности для определённых элементов
        /// </summary>
        private void ApplySpecialTransparency(Document doc)
        {
            // Прозрачность 0.5 для "Translucent(0.2"
            SetTransparencyByMaterial(doc, "Translucent(0.2", 0.5f);
            // Прозрачность 0.2 для "Translucent(0.08"
            SetTransparencyByMaterial(doc, "Translucent(0.08", 0.2f);
            // Прозрачность 0.5 для всех с "-INSU" в имени
            SetTransparencyByName(doc, "-INSU", 0.5f);
        }

        private void SetTransparencyByMaterial(Document doc, string materialSubstring, float transparency)
        {
            var search = new Search();
            search.SearchConditions.Add(SearchCondition.HasPropertyByDisplayName("Item", "Material")
                .DisplayStringContains(materialSubstring));
            search.Selection.SelectAll();
            var foundItems = search.FindAll(doc, true);
            if (foundItems.Count > 0)
                doc.Models.OverridePermanentTransparency(foundItems, transparency);
        }

        private void SetTransparencyByName(Document doc, string nameSubstring, float transparency)
        {
            var search = new Search();
            search.SearchConditions.Add(SearchCondition.HasPropertyByName("LcOaNode", "Name")
                .DisplayStringContains(nameSubstring));
            search.Selection.SelectAll();
            var foundItems = search.FindAll(doc, true);
            if (foundItems.Count > 0)
                doc.Models.OverridePermanentTransparency(foundItems, transparency);
        }

        /// <summary>
        /// скрытие clashbox
        /// </summary>
        private void HideClashSite()
        {
            var oS = new Search();
            oS.SearchConditions.Add(SearchCondition.HasPropertyByDisplayName("Item", "Name").EqualValue(VariantData.FromDisplayString("/CLASH-SITE")));
            oS.Selection.SelectAll();
            var findItems = oS.FindAll(Application.ActiveDocument, false);
            if (findItems.Count > 0)
            {
                Application.ActiveDocument.Models.SetHidden(findItems, true);
            }

            oS.Clear();
            oS.SearchConditions.Add(SearchCondition.HasPropertyByDisplayName("Item", "Name").EqualValue(VariantData.FromDisplayString("/40/103_1/HAZARDOUS_AREA")));
            oS.Selection.SelectAll();
            findItems = oS.FindAll(Application.ActiveDocument, false);
            if (findItems.Count > 0)
            {
                Application.ActiveDocument.Models.OverridePermanentTransparency(findItems, 0.9);
                Application.ActiveDocument.Models.SetHidden(findItems, true);
            }
        }
    }
}
