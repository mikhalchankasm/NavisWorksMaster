using Autodesk.Navisworks.Api;
using System.Windows.Forms;
using System;
using System.IO;
using NavisHelper.Core;

namespace NavisHelper
{
    public static class NavisSaveUtils
    {
        public static bool SaveAsNavis2018(Document document)
        {
            string modelPath = document.FileName;
            string logPath;
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
                    return false;
                }
            }
            else
            {
                logPath = Path.Combine(Path.GetDirectoryName(modelPath), Path.GetFileNameWithoutExtension(modelPath) + "_save2018_log.txt");
            }
            bool result = false;
            try
            {
                result = document.TrySaveFile(modelPath, DocumentFileVersion.Navisworks2018);
                if (!result)
                {
                    Logger.Error($"Не удалось сохранить файл как Navisworks 2018: {modelPath}", "SaveAsNavis2018", modelPath);
                    return false;
                }
                Logger.Info($"Файл успешно пересохранён как Navisworks 2018: {modelPath}", "SaveAsNavis2018", modelPath);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при сохранении как Navisworks 2018: {ex.Message}", "SaveAsNavis2018", modelPath);
                return false;
            }
        }
    }
} 