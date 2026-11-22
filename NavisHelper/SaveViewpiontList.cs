using Autodesk.Navisworks.Api.Plugins;
using Autodesk.Navisworks.Api;
using System.Collections.Generic;
using System;
using System.IO;
using System.Windows.Forms; // Для отображения сообщений пользователю
using NavisHelper.Core;
// using Microsoft.Toolkit.Uwp.Notifications; // Временно отключено из-за проблем с совместимостью

namespace NavisHelper
{
    [Plugin("SaveViewpiontList", "COMPANY", DisplayName = "Сохранить точки обзора")]
    [AddInPlugin(AddInLocation.None)]
    public class SaveViewpiontListPlugin : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                // Получаем активный документ
                Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;

                if (doc == null)
                {
                    ShowError("Документ не загружен. Пожалуйста, откройте документ Navisworks.");
                    return -1;
                }

                if (string.IsNullOrEmpty(doc.FileName))
                {
                    ShowError("Документ не сохранен. Пожалуйста, сохраните документ перед выполнением операции.");
                    return -1;
                }

                // Получаем точки обзора
                var viewpoints = GetViewpoints(doc);

                if (viewpoints.Count == 0)
                {
                    ShowWarning("В документе не найдены точки обзора.");
                    return 0;
                }

                // Сохраняем в файл
                var txtFileName = SaveViewpointsToFile(doc.FileName, viewpoints);

                ShowSuccess($"Список точек обзора успешно сохранен в файл:\n{txtFileName}");
                return 1;
            }
            catch (Exception ex)
            {
                ShowError($"Произошла непредвиденная ошибка: {ex.Message}");
                return -1;
            }
        }

        private List<string> GetViewpoints(Document doc)
        {
            var list = new List<string>();
            var document = doc?.ActiveView?.Document ?? doc;

            if (document?.SavedViewpoints?.Value == null)
                return list;

            CollectViewpointNames(document.SavedViewpoints.Value, list);
            return list;
        }

        private static void CollectViewpointNames(SavedItemCollection items, List<string> list)
        {
            if (items == null)
                return;

            foreach (var savedItem in items)
            {
                if (savedItem is GroupItem groupItem)
                {
                    CollectViewpointNames(groupItem.Children, list);
                }
                else if (savedItem != null && !string.IsNullOrEmpty(savedItem.DisplayName))
                {
                    list.Add(savedItem.DisplayName);
                }
            }
        }

        private string SaveViewpointsToFile(string nwdFilePath, List<string> viewpoints)
        {
            string txtFileName = Path.ChangeExtension(nwdFilePath, ".txt");
            
            try
            {
                // Проверяем, существует ли файл
                if (File.Exists(txtFileName))
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    txtFileName = Path.Combine(
                        Path.GetDirectoryName(txtFileName),
                        $"{Path.GetFileNameWithoutExtension(txtFileName)}_{timestamp}.txt"
                    );
                }

                File.WriteAllLines(txtFileName, viewpoints);
                return txtFileName;
            }
            catch (UnauthorizedAccessException)
            {
                throw new Exception("Нет прав доступа для создания файла. Попробуйте запустить программу от имени администратора.");
            }
            catch (IOException ex)
            {
                throw new Exception($"Ошибка при записи в файл: {ex.Message}");
            }
        }

        private void ShowError(string message)
        {
            Logger.Error(message, "SaveViewpiontListPlugin");
        }

        private void ShowWarning(string message)
        {
            Logger.Warn(message, "SaveViewpiontListPlugin");
        }

        private void ShowSuccess(string message)
        {
            Logger.Info(message, "SaveViewpiontListPlugin");
        }
    }
}
