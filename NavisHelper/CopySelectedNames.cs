using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using System;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;
using Application = Autodesk.Navisworks.Api.Application;
using NavisHelper.Core;
// using Microsoft.Toolkit.Uwp.Notifications; // Временно отключено из-за проблем с совместимостью

namespace NavisHelper
{
    [Plugin("CopySelectedNames", "CBC", DisplayName = "Copy Selected Names")]
    [AddInPlugin(AddInLocation.None)]
    public class CopySelectedNames : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            // Максимально безопасная версия с дополнительными проверками
            try
            {
                // Проверка доступности Application
                try
                {
                    var current = Application.MainDocument;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Application недоступен: {ex.Message}", "CopySelectedNames");
                    return 0;
                }

                // Проверка активного документа
                Document doc = null;
                try
                {
                    doc = Application.ActiveDocument;
                }
                catch (COMException comEx)
                {
                    Logger.Error($"COM ошибка при получении активного документа: {comEx.Message}", "CopySelectedNames");
                    return 0;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Ошибка при получении активного документа: {ex.Message}", "CopySelectedNames");
                    return 0;
                }

                if (doc == null)
                {
                    Logger.Error("Нет активного документа", "CopySelectedNames");
                    return 0;
                }

                // Проверка текущего выделения
                Selection currentSelection = null;
                try
                {
                    currentSelection = doc.CurrentSelection;
                }
                catch (COMException comEx)
                {
                    Logger.Error($"COM ошибка при получении текущего выделения: {comEx.Message}", "CopySelectedNames");
                    return 0;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Ошибка при получении текущего выделения: {ex.Message}", "CopySelectedNames");
                    return 0;
                }

                if (currentSelection == null)
                {
                    Logger.Error("Не удалось получить текущее выделение", "CopySelectedNames");
                    return 0;
                }

                // Получение выбранных элементов
                ModelItemCollection selection = null;
                try
                {
                    selection = currentSelection.GetSelectedItems();
                }
                catch (COMException comEx)
                {
                    Logger.Error($"COM ошибка при получении выбранных элементов: {comEx.Message}", "CopySelectedNames");
                    return 0;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Ошибка при получении выбранных элементов: {ex.Message}", "CopySelectedNames");
                    return 0;
                }

                if (selection == null)
                {
                    Logger.Error("Не удалось получить выбранные элементы", "CopySelectedNames");
                    return 0;
                }

                // Проверка количества элементов
                int selectionCount = 0;
                try
                {
                    selectionCount = selection.Count;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Ошибка при получении количества элементов: {ex.Message}", "CopySelectedNames");
                    return 0;
                }

                if (selectionCount == 0)
                {
                    Logger.Info("Нет выделенных элементов", "CopySelectedNames");
                    return 0;
                }

                // Безопасное извлечение имен
                var names = new System.Collections.Generic.List<string>();
                
                try
                {
                    foreach (var item in selection)
                    {
                        if (item == null) continue;
                        
                        try
                        {
                            var modelItem = item as ModelItem;
                            if (modelItem != null)
                            {
                                string displayName = null;
                                try
                                {
                                    displayName = modelItem.DisplayName;
                                }
                                catch (Exception ex)
                                {
                                    Logger.Error($"Ошибка при получении DisplayName: {ex.Message}", "CopySelectedNames");
                                    continue;
                                }

                                if (!string.IsNullOrWhiteSpace(displayName))
                                {
                                    names.Add(displayName);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error($"Ошибка при обработке элемента: {ex.Message}", "CopySelectedNames");
                            continue;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Ошибка при итерации по элементам: {ex.Message}", "CopySelectedNames");
                    return 0;
                }

                // Удаление дубликатов
                var uniqueNames = names.Distinct().ToList();

                if (uniqueNames.Count == 0)
                {
                    Logger.Info("У выделенных элементов нет имён", "CopySelectedNames");
                    return 0;
                }

                // Создание текста для буфера обмена
                string clipboardText = null;
                try
                {
                    var sb = new StringBuilder();
                    foreach (var name in uniqueNames)
                    {
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            sb.AppendLine(name);
                        }
                    }
                    clipboardText = sb.ToString();
                }
                catch (OutOfMemoryException memEx)
                {
                    Logger.Error($"Ошибка памяти при создании текста: {memEx.Message}", "CopySelectedNames");
                    return 0;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Ошибка при создании текста: {ex.Message}", "CopySelectedNames");
                    return 0;
                }

                if (string.IsNullOrWhiteSpace(clipboardText))
                {
                    Logger.Error("Не удалось создать текст для буфера обмена", "CopySelectedNames");
                    return 0;
                }

                // Безопасная установка в буфер обмена
                bool clipboardSet = false;
                int maxRetries = 5;
                
                for (int i = 0; i < maxRetries && !clipboardSet; i++)
                {
                    try
                    {
                        // Очистка буфера обмена перед установкой
                        if (i == 0)
                        {
                            try
                            {
                                Clipboard.Clear();
                            }
                            catch (Exception) { /* Игнорируем ошибки очистки */ }
                        }

                        Clipboard.SetText(clipboardText);
                        clipboardSet = true;
                    }
                    catch (ExternalException)
                    {
                        // Буфер обмена занят, ждем и пробуем снова
                        if (i < maxRetries - 1)
                        {
                            Thread.Sleep(200);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Ошибка при установке в буфер обмена (попытка {i + 1}): {ex.Message}", "CopySelectedNames");
                        if (i < maxRetries - 1)
                        {
                            Thread.Sleep(200);
                        }
                    }
                }

                if (clipboardSet)
                {
                    Logger.Info($"Скопировано имён: {uniqueNames.Count}", "CopySelectedNames");
                }
                else
                {
                    Logger.Error("Не удалось скопировать в буфер обмена после нескольких попыток", "CopySelectedNames");
                    return 0;
                }
            }
            catch (COMException comEx)
            {
                Logger.Error($"COM ошибка: {comEx.Message}", "CopySelectedNames");
                return 0;
            }
            catch (OutOfMemoryException memEx)
            {
                Logger.Error($"Ошибка памяти: {memEx.Message}", "CopySelectedNames");
                return 0;
            }
            catch (ThreadAbortException)
            {
                // Игнорируем прерывание потока
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"Неожиданная ошибка: {ex.Message}", "CopySelectedNames");
                return 0;
            }

            return 0;
        }

        /// <summary>
        /// Безопасное отображение уведомлений с обработкой ошибок
        /// </summary>
        private void ShowNotification(string title, string message)
        {
            try
            {
                // Простое логирование вместо уведомлений
                Logger.Info($"{title}: {message}", "CopySelectedNames");
            }
            catch (Exception ex)
            {
                // Если логирование не работает, игнорируем
                System.Diagnostics.Debug.WriteLine($"Ошибка при логировании: {ex.Message}");
            }
        }
    }
} 