using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using Autodesk.Navisworks.Api.Interop.ComApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Application = Autodesk.Navisworks.Api.Application;
using NavisHelper.Core;
using NavisHelper.Core.Import;
using NavisHelper.Core.Localization;

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
            var ui = UiLocalizationService.Current;
            if (doc == null || doc.IsClear)
            {
                ShowInputError(ui.GetString("CsvAttributeDocumentRequired"), csvFilePath);
                return 0;
            }

            CsvAttributeTable table;
            try
            {
                table = CsvAttributeReader.Read(csvFilePath);
            }
            catch (CsvAttributeReadException ex)
            {
                ShowInputError(ui.Format(ex.ResourceKey, ex.Arguments), csvFilePath);
                return 0;
            }
            catch (Exception ex)
            {
                ShowInputError(ui.Format("CsvAttributeInputFailed", ex.Message), csvFilePath);
                return 0;
            }

            var progress = Application.BeginProgress(
                ui.GetString("CsvAttributeProgressCaption"));

            try
            {
                var headers = table.Headers;

                // Строим индекс элементов один раз (оптимизация вместо множественных Search)
                var itemIndex = BuildItemIndex(doc);

                var notFoundNames = new List<string>();
                var state = Autodesk.Navisworks.Api.ComApi.ComApiBridge.State;
                var startTime = DateTime.Now;
                int processedCount = 0;

                for (int i = 0; i < table.Rows.Count; i++)
                {
                    if (progress.IsCanceled)
                        break;

                    var values = table.Rows[i];
                    string itemName = values[0];

                    if (!itemIndex.TryGetValue(itemName, out var foundItem))
                    {
                        notFoundNames.Add(itemName);
                    }
                    else
                    {
                        var attributes = new Dictionary<string, string>();
                        for (int j = 1; j < headers.Length; j++)
                            attributes[headers[j]] = values[j];
                        ApplyAttributes(foundItem, attributes, state);
                    }
                    processedCount++;
                    progress.Update((double)processedCount / table.Rows.Count);
                }

                WriteLog(doc, notFoundNames, startTime, processedCount);
                Logger.Info(ui.Format("CsvAttributeImportSummary", processedCount, table.Rows.Count, notFoundNames.Count));
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

        private static void ShowInputError(string message, string path)
        {
            Logger.Error(message, "CsvAttributeLoader", path);
            MessageBox.Show(message, UiLocalizationService.Current.GetString("CsvAttributeProgressCaption"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private string GetCsvFilePath(string[] parameters)
        {
            if (parameters.Length > 0 && File.Exists(parameters[0]))
                return parameters[0];

            using (var openFileDialog = new OpenFileDialog
            {
                Filter = UiLocalizationService.Current.GetString("CommonFileFilterCsvTextAll"),
                Title = UiLocalizationService.Current.GetString("CsvAttributeDialogTitle"),
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
