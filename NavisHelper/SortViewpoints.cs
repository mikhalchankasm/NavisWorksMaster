using System;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Core;

namespace NavisHelper
{
    [Plugin("SortViewpoints", "COMPANY", DisplayName = "Сортировка точек обзора")]
    [AddInPlugin(AddInLocation.None)]
    public class SortViewpointsPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (doc == null)
                {
                    throw new InvalidOperationException("Документ не загружен.");
                }

                SortDocumentViewpoints(doc.ActiveView.Document);
                Logger.Info("Точки обзора успешно отсортированы.");
                return 1;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при сортировке точек обзора: {ex.Message}", "SortViewpoints");
                return 0;
            }
        }

        private void SortDocumentViewpoints(Document document)
        {
            if (document?.SavedViewpoints == null)
            {
                throw new ArgumentNullException(nameof(document), "Документ или коллекция точек обзора отсутствует.");
            }

            var viewpoints = document.SavedViewpoints.Value.ToArray();
            
            foreach (var folderItem in viewpoints.OfType<FolderItem>())
            {
                try
                {
                    ProcessFolder(document, folderItem);
                }
                catch (Exception ex)
                {
                    Logger.Error($"Ошибка при обработке папки {folderItem.DisplayName}: {ex.Message}", "SortViewpoints");
                }
            }
        }

        private void ProcessFolder(Document document, FolderItem sourceFolder)
        {
            var newFolderName = $"{sourceFolder.DisplayName}_";
            var newFolder = new FolderItem { DisplayName = newFolderName };

            document.SavedViewpoints.InsertCopy(document.SavedViewpoints.Value.Count, newFolder);

            var createdFolder = document.SavedViewpoints.Value
                .OfType<FolderItem>()
                .FirstOrDefault(x => x.DisplayName == newFolderName);

            if (createdFolder == null)
            {
                throw new InvalidOperationException($"Не удалось создать папку {newFolderName}");
            }

            var sortedItems = sourceFolder.Children
                .OrderBy(s => ExtractNumberTuple(s.DisplayName))
                .ToList();

            foreach (var item in sortedItems)
            {
                document.SavedViewpoints.InsertCopy(createdFolder, createdFolder.Children.Count, item.CreateUniqueCopy());
            }
        }

        private static (int mainNumber, int suffix) ExtractNumberTuple(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return (0, 0);
            }

            try
            {
                string mainNumberStr;
                int mainNumber;
                int suffix = 0;

                // Обработка номера до разделителя (точка, дефис или подчеркивание)
                var firstPart = name.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                mainNumberStr = string.Concat(firstPart.Where(char.IsDigit));
                
                if (!int.TryParse(mainNumberStr, out mainNumber))
                {
                    return (0, 0);
                }

                // Обработка суффикса
                if (name.Contains("-") || name.Contains("_"))
                {
                    var parts = name.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 1)
                    {
                        var suffixStr = string.Concat(parts[1].Where(char.IsDigit));
                        int.TryParse(suffixStr, out suffix);
                    }
                }

                return (mainNumber, suffix);
            }
            catch
            {
                return (0, 0);
            }
        }
    }
}
