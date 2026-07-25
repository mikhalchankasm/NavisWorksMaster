using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using MessageBox = System.Windows.MessageBox;
using NavisHelper.Core;

namespace NavisHelper.WPF
{
    /// <summary>
    /// Логика взаимодействия для FilterModelsPanel.xaml
    /// </summary>
    public partial class FilterModelsPanel : Window
    {
        private Document _doc;
        private HashSet<ModelItem> _lastVisibleItems;

        public FilterModelsPanel()
        {
            InitializeComponent();
        }

        public void Initialize(Document doc)
        {
            _doc = doc;
            _lastVisibleItems = new HashSet<ModelItem>();
                            // txtStatus.Text = "Готов к работе";
                Logger.Info("Готов к работе", "FilterModelsPanel");
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Текстовые файлы (*.txt)|*.txt",
                Title = "Выберите текстовый файл"
            };

            if (dialog.ShowDialog() == true)
            {
                txtFilePath.Text = dialog.FileName;
            }
        }

        private void btnFilter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(txtFilePath.Text))
                {
                    MessageBox.Show("Выберите файл со списком.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!File.Exists(txtFilePath.Text))
                {
                    MessageBox.Show("Файл не найден.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var allowedFiles = File.ReadAllLines(txtFilePath.Text)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => line.Trim())
                    .ToList();

                if (!allowedFiles.Any())
                {
                    MessageBox.Show("Файл пуст или содержит только пустые строки.", "Ошибка", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Разделяем на rvm и остальные
                var rvmNames = allowedFiles.Where(x => x.EndsWith(".rvm", StringComparison.OrdinalIgnoreCase)).ToList();
                var otherNames = allowedFiles.Except(rvmNames).ToList();

                var itemsToKeepVisible = new HashSet<ModelItem>();
                var rootItems = _doc.Models.CreateCollectionFromRootItems();

                // Для лога
                var logLines = new List<string>();
                var foundRvm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var foundOther = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 1. Поиск rvm только на первом уровне
                foreach (ModelItem rootItem in rootItems)
                {
                    foreach (var rvm in rvmNames)
                    {
                        if (string.Equals(rootItem.DisplayName, rvm, StringComparison.OrdinalIgnoreCase))
                        {
                            itemsToKeepVisible.Add(rootItem);
                            foundRvm.Add(rvm);
                        }
                    }
                }

                // 2. Обычный рекурсивный поиск для остальных
                foreach (ModelItem rootItem in rootItems)
                {
                    FindAndLogModelItems(rootItem, otherNames, rbExact.IsChecked == true, itemsToKeepVisible, foundOther, 0, 10);
                }

                var itemsToHide = new List<ModelItem>();
                foreach (ModelItem rootItem in rootItems)
                {
                    CollectItemsToHide(rootItem, itemsToKeepVisible, itemsToHide, 0, 10);
                }

                if (itemsToHide.Any())
                {
                    _doc.Models.SetHidden(itemsToHide, true);
                }

                _lastVisibleItems = itemsToKeepVisible;
                // txtStatus.Text = $"Скрыто элементов: {itemsToHide.Count}";
                Logger.Info($"Скрыто элементов: {itemsToHide.Count}", "FilterModelsPanel");

                // Формируем лог
                foreach (var rvm in rvmNames)
                {
                    if (foundRvm.Contains(rvm))
                        logLines.Add($"[RVM] {rvm} — найден на первом уровне");
                    else
                        logLines.Add($"[RVM] {rvm} — НЕ найден на первом уровне");
                }
                foreach (var name in otherNames)
                {
                    if (foundOther.Contains(name))
                        logLines.Add($"[NAME] {name} — найден в структуре модели");
                    else
                        logLines.Add($"[NAME] {name} — НЕ найден в структуре модели");
                }

                // Путь к NWD и лог-файлу
                string nwdPath = _doc.FileName;
                string logPath = string.IsNullOrEmpty(nwdPath)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "filter_log.txt")
                    : Path.Combine(Path.GetDirectoryName(nwdPath), Path.GetFileNameWithoutExtension(nwdPath) + "_filter_log.txt");
                File.WriteAllLines(logPath, logLines);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnReset_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var rootItems = _doc.Models.CreateCollectionFromRootItems();
                foreach (ModelItem rootItem in rootItems)
                {
                    ResetVisibility(rootItem);
                }

                _lastVisibleItems.Clear();
                // txtStatus.Text = "Фильтр сброшен";
                Logger.Info("Фильтр сброшен", "FilterModelsPanel");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FindAndLogModelItems(ModelItem item, List<string> allowedFiles, bool exactMatch, HashSet<ModelItem> itemsToKeepVisible, HashSet<string> foundNames, int currentDepth, int maxDepth)
        {
            if (currentDepth > maxDepth)
                return;

            string modelName = item.DisplayName;
            bool isMatch = false;
            if (exactMatch)
            {
                isMatch = allowedFiles.Contains(modelName, StringComparer.OrdinalIgnoreCase);
                if (isMatch)
                    foundNames.Add(modelName);
            }
            else
            {
                foreach (var allowedFile in allowedFiles)
                {
                    if (modelName.IndexOf(allowedFile, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isMatch = true;
                        foundNames.Add(allowedFile);
                    }
                }
            }

            if (isMatch)
            {
                var currentItem = item;
                while (currentItem != null)
                {
                    itemsToKeepVisible.Add(currentItem);
                    currentItem = currentItem.Parent;
                }
            }

            foreach (ModelItem childItem in item.Children)
            {
                FindAndLogModelItems(childItem, allowedFiles, exactMatch, itemsToKeepVisible, foundNames, currentDepth + 1, maxDepth);
            }
        }

        private void CollectItemsToHide(ModelItem item, HashSet<ModelItem> itemsToKeepVisible, List<ModelItem> itemsToHide, int currentDepth, int maxDepth)
        {
            if (currentDepth > maxDepth)
            {
                return;
            }
            
            if (itemsToKeepVisible.Contains(item))
            {
                return;
            }
            
            itemsToHide.Add(item);

            foreach (ModelItem childItem in item.Children)
            {
                CollectItemsToHide(childItem, itemsToKeepVisible, itemsToHide, currentDepth + 1, maxDepth);
            }
        }

        private void ResetVisibility(ModelItem item)
        {
            var itemsToShow = new List<ModelItem> { item };
            foreach (ModelItem childItem in item.Children)
            {
                CollectItemsToShow(childItem, itemsToShow);
            }
            _doc.Models.SetHidden(itemsToShow, false);
        }

        private void CollectItemsToShow(ModelItem item, List<ModelItem> itemsToShow)
        {
            itemsToShow.Add(item);
            foreach (ModelItem childItem in item.Children)
            {
                CollectItemsToShow(childItem, itemsToShow);
            }
        }
    }
} 