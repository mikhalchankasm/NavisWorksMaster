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
                    Logger.Error("Документ не загружен. Пожалуйста, откройте документ Navisworks.", "SaveViewpiontListPlugin");
                    Logger.Info("Документ не загружен. Пожалуйста, откройте документ Navisworks.", "SaveViewpiontListPlugin");
                    return -1;
                }

                if (string.IsNullOrEmpty(doc.FileName))
                {
                    Logger.Error("Документ не сохранен. Пожалуйста, сохраните документ перед выполнением операции.", "SaveViewpiontListPlugin");
                    Logger.Info("Документ не сохранен. Пожалуйста, сохраните документ перед выполнением операции.", "SaveViewpiontListPlugin");
                    return -1;
                }

                // Получаем точки обзора
                var viewpoints = GetViewpoints(doc);

                if (viewpoints.Count == 0)
                {
                    Logger.Info("В документе не найдены точки обзора.", "SaveViewpiontListPlugin");
                    Logger.Info("В документе не найдены точки обзора.", "SaveViewpiontListPlugin");
                    return 0;
                }

                // Сохраняем в файл
                var txtFileName = SaveViewpointsToFile(doc.FileName, viewpoints);

                ShowSuccess($"Список точек обзора успешно сохранен в файл:\n{txtFileName}");
                return 1;
            }
            catch (Exception ex)
            {
                Logger.Error($"Произошла непредвиденная ошибка: {ex.Message}", "SaveViewpiontListPlugin");
                Logger.Info($"Ошибка: {ex.Message}", "SaveViewpiontListPlugin");
                return -1;
            }
        }

        private List<string> GetViewpoints(Document doc)
        {
            var list = new List<string>();
            var document = doc.ActiveView.Document;

            if (document?.SavedViewpoints?.Value == null)
                return list;

            foreach (var savedItem in document.SavedViewpoints.Value)
            {
                if (savedItem is FolderItem folderItem)
                {
                    foreach (var svp in folderItem.Children)
                    {
                        if (!string.IsNullOrEmpty(svp.DisplayName))
                        {
                            list.Add(svp.DisplayName);
                        }
                    }
                }
            }

            return list;
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
            Logger.Info(message, "SaveViewpiontListPlugin");
        }

        private void ShowWarning(string message)
        {
            Logger.Info(message, "SaveViewpiontListPlugin");
            Logger.Info(message, "SaveViewpiontListPlugin");
        }

        private void ShowSuccess(string message)
        {
            Logger.Info(message, "SaveViewpiontListPlugin");
            Logger.Info(message, "SaveViewpiontListPlugin");
        }
    }
}
