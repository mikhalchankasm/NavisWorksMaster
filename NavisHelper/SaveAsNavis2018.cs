using System;
using System.IO;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using Application = Autodesk.Navisworks.Api.Application;
using NavisHelper.Core;

namespace NavisHelper
{
    [Plugin("SaveAsNavis2018", "MS", DisplayName = "Сохранить как Navisworks 2018")]
    [AddInPlugin(AddInLocation.None)]
    public class SaveAsNavis2018 : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                var document = Application.ActiveDocument;
                string modelPath = document.FileName;
                if (string.IsNullOrEmpty(modelPath))
                {
                    // Сохраняем во временную папку
                    string tempPath = Path.Combine(Path.GetTempPath(), $"temp_{Guid.NewGuid()}.nwd");
                    try
                    {
                        document.SaveFile(tempPath);
                        modelPath = tempPath;
                        Logger.Info($"Файл не был сохранён ранее. Сохранили во временную папку: {tempPath}", "SaveAsNavis2018", modelPath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error($"Ошибка при сохранении во временную папку: {ex.Message}", "SaveAsNavis2018");
                        return -1;
                    }
                }
                else
                {
                    // logPath не нужен, логгер сам определит путь
                }
                bool result = false;
                try
                {
                    result = document.TrySaveFile(modelPath, DocumentFileVersion.Navisworks2018);
                    if (!result)
                    {
                        Logger.Error($"Не удалось сохранить файл как Navisworks 2018: {modelPath}", "SaveAsNavis2018", modelPath);
                        return -1;
                    }
                    Logger.Info($"Файл успешно пересохранён как Navisworks 2018: {modelPath}", "SaveAsNavis2018", modelPath);
                    return 0;
                }
                catch (Exception ex)
                {
                    Logger.Error($"Ошибка при сохранении как Navisworks 2018: {ex.Message}", "SaveAsNavis2018", modelPath);
                    return -1;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Критическая ошибка: {ex.Message}", "SaveAsNavis2018");
                return -1;
            }
        }
    }
} 