using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using Autodesk.Navisworks.Api.ComApi;
// using NavisHelper.WPF; // Временно отключено
// using System.Windows; // Временно отключено
using NavisHelper.Core;
using NavisHelper.Core.Localization;
// using Microsoft.Toolkit.Uwp.Notifications; // Временно отключено из-за проблем с совместимостью

namespace NavisHelper
{
    [Plugin("FilterModels", "COMPANY", DisplayName = "Фильтр по списку из файла")]
    [AddInPlugin(AddInLocation.None)]
    public class FilterModelsPlugin : AddInPlugin
    {
        // private FilterModelsPanel _window; // Временно отключено

        public override int Execute(params string[] parameters)
        {
            try
            {
                // Получаем активный документ
                var doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (doc == null)
                {
                    Logger.Error("Нет загруженного документа.", "FilterNavisModels");
                    return 0;
                }

                // Создаем и показываем окно
                // if (_window == null)
                // {
                //     _window = new FilterModelsPanel();
                //     _window.Initialize(doc);
                // }

                // Показываем окно
                // _window.Show();
                
                // Временно отключено - используем простое логирование
                Logger.Info("Фильтр моделей временно отключен", "FilterNavisModels");

                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка: {ex.Message}", "FilterNavisModels");
                return -1;
            }
        }

        private void CreateSelectionSet(Document doc, HashSet<ModelItem> visibleItems)
        {
            try
            {
                // Создаем имя для набора на основе текущей даты и времени
                string setName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                
                // Создаем новую выборку из найденных элементов
                var search = new Search();
                var searchCondition = SearchCondition.HasPropertyByDisplayName("LcOaNode", "LcOaSceneBaseType");
                search.SearchConditions.Add(searchCondition);
                
                // Создаем набор на основе поиска
                var selection = new SelectionSet(search);
                selection.DisplayName = setName;
                
                // Устанавливаем элементы в текущую выборку
                doc.CurrentSelection.Clear();
                doc.CurrentSelection.CopyFrom(visibleItems);
                
                // Сохраняем текущую выборку как набор
                doc.SelectionSets.Value.Add(selection);
                
                Logger.Info($"Создан набор: {setName}", "FilterNavisModels");
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при создании набора: {ex.Message}", "FilterNavisModels");
            }
        }

        private void ProcessModelItem(ModelItem item, List<string> allowedFiles, bool exactMatch, HashSet<ModelItem> itemsToKeepVisible, int currentDepth, int maxDepth)
        {
            // Проверяем глубину вложенности
            if (currentDepth > maxDepth)
            {
                return;
            }
            
            string modelName = item.DisplayName;
            bool isMatch;

            if (exactMatch)
            {
                // Точное совпадение
                isMatch = allowedFiles.Contains(modelName, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                // Частичное совпадение
                isMatch = allowedFiles.Any(allowedFile => 
                    modelName.IndexOf(allowedFile, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (isMatch)
            {
                // Если нашли совпадение, добавляем элемент и всех его родителей в список видимых
                var currentItem = item;
                while (currentItem != null)
                {
                    itemsToKeepVisible.Add(currentItem);
                    currentItem = currentItem.Parent;
                }
            }

            // Рекурсивно обрабатываем все дочерние элементы с увеличением глубины
            foreach (ModelItem childItem in item.Children)
            {
                ProcessModelItem(childItem, allowedFiles, exactMatch, itemsToKeepVisible, currentDepth + 1, maxDepth);
            }
        }

        private bool ShowSearchTypeDialog()
        {
            DialogResult result = System.Windows.Forms.MessageBox.Show(
                UiLocalizationService.Current.GetString("FilterSearchPrompt"),
                UiLocalizationService.Current.GetString("FilterSearchTitle"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            
            return result == DialogResult.Yes;
        }

        private void CollectItemsToHide(ModelItem item, HashSet<ModelItem> itemsToKeepVisible, List<ModelItem> itemsToHide, int currentDepth, int maxDepth)
        {
            // Проверяем глубину вложенности
            if (currentDepth > maxDepth)
            {
                return;
            }
            
            // Если текущий элемент в списке видимых, не скрываем его и его детей
            if (itemsToKeepVisible.Contains(item))
            {
                return;
            }
            
            // Если элемент не в списке видимых, добавляем его для скрытия
            itemsToHide.Add(item);

            // Проверяем детей только если текущий элемент не в списке видимых
            foreach (ModelItem childItem in item.Children)
            {
                CollectItemsToHide(childItem, itemsToKeepVisible, itemsToHide, currentDepth + 1, maxDepth);
            }
        }

        /// <summary>
        /// Открывает диалог выбора TXT-файла и возвращает путь
        /// </summary>
        private string OpenFileDialog()
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = UiLocalizationService.Current.GetString("CommonFileFilterTextOnly");
                openFileDialog.Title = UiLocalizationService.Current.GetString("FilterFileDialogTitle");

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    return openFileDialog.FileName;
                }
            }
            return null;
        }
    }
}
