using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using Autodesk.Navisworks.Api.Interop.ComApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Application = Autodesk.Navisworks.Api.Application;
using NavisHelper.Core;

namespace NavisHelper
{
    [Plugin("CsvAttributeLoader", "CSVL", DisplayName = "Загрузка атрибутов из CSV")]
    [AddInPlugin(AddInLocation.None)]
    public class CsvAttributeLoader : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            string csvFilePath = GetCsvFilePath(parameters);
            if (string.IsNullOrEmpty(csvFilePath))
                return 0;

            Document doc = Application.ActiveDocument;
            var progress = Application.BeginProgress("Загрузка атрибутов");

            try
            {
                var lines = File.ReadAllLines(csvFilePath, Encoding.UTF8);
                if (lines.Length < 2)
                {
                    Logger.Error("CSV файл пуст или содержит только заголовок", "CsvAttributeLoader", csvFilePath);
                    return 0;
                }

                var headers = lines[0].Split(';').Select(h => h.Trim()).ToArray();
                if (headers.Length < 2)
                {
                    Logger.Error("CSV файл должен содержать минимум два столбца", "CsvAttributeLoader", csvFilePath);
                    return 0;
                }

                // Строим индекс элементов один раз (оптимизация вместо множественных Search)
                var itemIndex = BuildItemIndex(doc);

                var notFoundNames = new List<string>();
                var state = Autodesk.Navisworks.Api.ComApi.ComApiBridge.State;
                var startTime = DateTime.Now;

                for (int i = 1; i < lines.Length; i++)
                {
                    if (progress.IsCanceled)
                        break;

                    var values = lines[i].Split(';').Select(v => v.Trim()).ToArray();
                    if (values.Length != headers.Length)
                        continue;

                    string itemName = values[0];

                    if (!itemIndex.TryGetValue(itemName, out var foundItem))
                    {
                        notFoundNames.Add(itemName);
                        continue;
                    }

                    var attributes = new Dictionary<string, string>();
                    for (int j = 1; j < headers.Length; j++)
                        attributes[headers[j]] = values[j];

                    ApplyAttributes(foundItem, attributes, state);

                    progress.Update((double)i / (lines.Length - 1));
                }

                WriteLog(doc, notFoundNames, startTime, lines.Length - 1);
                Logger.Info($"Загрузка завершена. Обработано: {lines.Length - 1}, не найдено: {notFoundNames.Count}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка: {ex.Message}", "CsvAttributeLoader", csvFilePath);
            }
            finally
            {
                Application.EndProgress();
            }

            return 0;
        }

        private string GetCsvFilePath(string[] parameters)
        {
            if (parameters.Length > 0 && File.Exists(parameters[0]))
                return parameters[0];

            using (var openFileDialog = new OpenFileDialog
            {
                Filter = "CSV файлы|*.csv|Текстовый файл|*.txt|Все файлы|*.*",
                Title = "Выберите CSV файл с атрибутами",
                RestoreDirectory = true
            })
            {
                return openFileDialog.ShowDialog() == DialogResult.OK ? openFileDialog.FileName : null;
            }
        }

        private Dictionary<string, ModelItem> BuildItemIndex(Document doc)
        {
            var index = new Dictionary<string, ModelItem>(StringComparer.OrdinalIgnoreCase);

            foreach (var rootItem in doc.Models.RootItems)
            foreach (var item in rootItem.DescendantsAndSelf)
            {
                // Internal name (LcOaNode.Name)
                var internalName = item.PropertyCategories
                    .FindPropertyByName("LcOaNode", "Name")?.Value?.ToDisplayString();

                if (!string.IsNullOrEmpty(internalName) && !index.ContainsKey(internalName))
                    index[internalName] = item;

                // LcOaSceneBaseUserName
                var userName = item.PropertyCategories
                    .FindPropertyByName("LcOaNode", "LcOaSceneBaseUserName")?.Value?.ToDisplayString();

                if (!string.IsNullOrEmpty(userName) && !index.ContainsKey(userName))
                    index[userName] = item;

                // Display name
                var displayName = item.DisplayName;
                if (!string.IsNullOrEmpty(displayName) && !index.ContainsKey(displayName))
                    index[displayName] = item;
            }

            return index;
        }

        private void ApplyAttributes(ModelItem item, Dictionary<string, string> attributes, InwOpState10 state)
        {
            var path = Autodesk.Navisworks.Api.ComApi.ComApiBridge.ToInwOaPath(item);
            var guiNode = (InwGUIPropertyNode2)state.GetGUIPropertyNode(path, true);
            var propVec = (InwOaPropertyVec)state.ObjectFactory(nwEObjectType.eObjectType_nwOaPropertyVec, null, null);

            foreach (var attr in attributes)
            {
                var prop = (InwOaProperty)state.ObjectFactory(nwEObjectType.eObjectType_nwOaProperty, null, null);
                prop.name = attr.Key;
                prop.UserName = attr.Key;
                prop.value = attr.Value;
                propVec.Properties().Add(prop);
            }

            guiNode.SetUserDefined(0, "CSV_Attributes", "CSV_Attributes", propVec);
        }

        private void WriteLog(Document doc, List<string> notFoundNames, DateTime startTime, int totalCount)
        {
            var logLines = new List<string>
            {
                $"Время загрузки атрибутов: {startTime:yyyy-MM-dd HH:mm:ss} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"Всего элементов: {totalCount}",
                $"Не найдено: {notFoundNames.Count}",
                ""
            };

            foreach (var name in notFoundNames)
                logLines.Add($"[NOT FOUND] {name}");

            string nwdPath = doc.FileName;
            string logPath = string.IsNullOrEmpty(nwdPath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "attributes_log.txt")
                : Path.Combine(Path.GetDirectoryName(nwdPath), Path.GetFileNameWithoutExtension(nwdPath) + "_attributes_log.txt");

            File.WriteAllLines(logPath, logLines, Encoding.UTF8);
        }
    }
}
