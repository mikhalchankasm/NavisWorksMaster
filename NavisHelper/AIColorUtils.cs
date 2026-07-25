using Autodesk.Navisworks.Api;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NavisHelper.Core;

namespace NavisHelper
{
    /// <summary>
    /// Утилиты для работы с автоматическим окрашиванием объектов через ИИ
    /// </summary>
    public static class AIColorUtils
    {
        /// <summary>
        /// Получает список имен объектов из выделения
        /// </summary>
        /// <param name="selection">Выделение</param>
        /// <returns>Список уникальных имен объектов</returns>
        public static List<string> GetObjectNamesFromSelection(ModelItemCollection selection)
        {
            if (selection == null || selection.Count == 0)
                return new List<string>();

            var names = new List<string>();
            
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
                            string displayName = GetModelItemDisplayName(modelItem);
                            if (!string.IsNullOrWhiteSpace(displayName))
                            {
                                names.Add(displayName);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Ошибка при обработке элемента: {ex.Message}", "AIColorUtils");
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при итерации по элементам: {ex.Message}", "AIColorUtils");
            }

            return names.Distinct().ToList();
        }

        /// <summary>
        /// Применяет цвета к объектам
        /// </summary>
        /// <param name="selection">Выделение</param>
        /// <param name="colors">Словарь имя объекта -> RGB цвет</param>
        /// <returns>Количество примененных цветов</returns>
        public static int ApplyColorsToObjects(ModelItemCollection selection, Dictionary<string, string> colors)
        {
            if (selection == null || colors == null || colors.Count == 0)
                return 0;

            int appliedCount = 0;
            
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
                            string displayName = GetModelItemDisplayName(modelItem);
                            if (string.IsNullOrWhiteSpace(displayName) || !colors.ContainsKey(displayName))
                                continue;

                            var colorString = colors[displayName];
                            if (ApplyColorToModelItem(modelItem, colorString))
                            {
                                appliedCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Ошибка при применении цвета к элементу: {ex.Message}", "AIColorUtils");
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при итерации по элементам для применения цветов: {ex.Message}", "AIColorUtils");
            }

            return appliedCount;
        }

        /// <summary>
        /// Применяет цвет к конкретному объекту
        /// </summary>
        /// <param name="modelItem">Объект модели</param>
        /// <param name="colorString">RGB цвет в формате "R,G,B"</param>
        /// <returns>true если цвет применен успешно</returns>
        public static bool ApplyColorToModelItem(ModelItem modelItem, string colorString)
        {
            if (modelItem == null || string.IsNullOrWhiteSpace(colorString))
                return false;

            try
            {
                // Парсинг RGB цвета
                var color = ColorParser.ParseColor(colorString);
                
                // Создание NavisWorks цвета
                var navisColor = Autodesk.Navisworks.Api.Color.FromByteRGB(color.R, color.G, color.B);
                
                // Применение цвета к объекту
                var items = new ModelItemCollection { modelItem };
                Application.ActiveDocument.Models.OverridePermanentColor(items, navisColor);
                
                Logger.Info($"Применен цвет {colorString} к объекту {GetModelItemDisplayName(modelItem)}", "AIColorUtils");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при применении цвета {colorString}: {ex.Message}", "AIColorUtils");
                return false;
            }
        }

        /// <summary>
        /// Получает DisplayName объекта модели безопасным способом
        /// </summary>
        /// <param name="modelItem">Объект модели</param>
        /// <returns>DisplayName или пустая строка</returns>
        private static string GetModelItemDisplayName(ModelItem modelItem)
        {
            try
            {
                return modelItem?.DisplayName ?? string.Empty;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при получении DisplayName: {ex.Message}", "AIColorUtils");
                return string.Empty;
            }
        }

        /// <summary>
        /// Проверяет, поддерживает ли объект изменение цвета
        /// </summary>
        /// <param name="modelItem">Объект модели</param>
        /// <returns>true если объект поддерживает изменение цвета</returns>
        public static bool SupportsColorOverride(ModelItem modelItem)
        {
            if (modelItem == null)
                return false;

            try
            {
                // Проверяем, что объект не является системным
                return !string.IsNullOrWhiteSpace(modelItem.DisplayName) && 
                       modelItem.DisplayName != "Root" &&
                       modelItem.DisplayName != "Models";
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Фильтрует объекты, которые поддерживают изменение цвета
        /// </summary>
        /// <param name="selection">Исходное выделение</param>
        /// <returns>Отфильтрованное выделение</returns>
        public static ModelItemCollection FilterColorableObjects(ModelItemCollection selection)
        {
            if (selection == null)
                return new ModelItemCollection();

            var filteredItems = new ModelItemCollection();
            
            try
            {
                foreach (var item in selection)
                {
                    if (item != null && SupportsColorOverride(item))
                    {
                        filteredItems.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при фильтрации объектов: {ex.Message}", "AIColorUtils");
            }

            return filteredItems;
        }

        /// <summary>
        /// Создает отчет о применении цветов
        /// </summary>
        /// <param name="appliedColors">Словарь примененных цветов</param>
        /// <returns>Текст отчета</returns>
        public static string CreateColorReport(Dictionary<string, string> appliedColors)
        {
            if (appliedColors == null || appliedColors.Count == 0)
                return "Цвета не были применены.";

            var report = new System.Text.StringBuilder();
            report.AppendLine($"Применено цветов: {appliedColors.Count}");
            report.AppendLine("Детали:");
            
            foreach (var kvp in appliedColors)
            {
                report.AppendLine($"  {kvp.Key}: {kvp.Value}");
            }

            return report.ToString();
        }
    }
}
