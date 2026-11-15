using System;
using System.IO;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Agent.Services;
using Application = Autodesk.Navisworks.Api.Application;
using NavisHelper.Core;
using NavisHelper.Core.Localization;

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
                if (document == null || document.IsClear)
                {
                    MessageBox.Show(
                        UiLocalizationService.Current.GetString("Save2018_NoDocument_Message"),
                        UiLocalizationService.Current.GetString("Save2018_Dialog_Title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                var modelPath = document.FileName;
                var name = Path.GetFileNameWithoutExtension(modelPath);
                if (string.IsNullOrWhiteSpace(name))
                    name = string.IsNullOrWhiteSpace(document.Title) ? "model" : document.Title;
                using (var dialog = new SaveFileDialog
                {
                    Title = UiLocalizationService.Current.GetString("Save2018_Dialog_Title"),
                    Filter = UiLocalizationService.Current.GetString("Save2018_Dialog_Filter"),
                    DefaultExt = "nwd",
                    AddExtension = true,
                    OverwritePrompt = true,
                    InitialDirectory = string.IsNullOrWhiteSpace(modelPath)
                        ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                        : Path.GetDirectoryName(modelPath),
                    FileName = name + "_2018.nwd"
                })
                {
                    if (dialog.ShowDialog() != DialogResult.OK)
                        return 0;
                    if (!string.Equals(Path.GetExtension(dialog.FileName), ".nwd", StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrWhiteSpace(modelPath) && string.Equals(Path.GetFullPath(dialog.FileName),
                            Path.GetFullPath(modelPath), StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show(
                            string.Format(UiLocalizationService.Current.GetString("Save2018_SameFile_Message"), dialog.FileName),
                            UiLocalizationService.Current.GetString("Save2018_Dialog_Title"),
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return -1;
                    }

                    DocumentCommandService.SaveDocumentAsToPath(document, dialog.FileName, true, false, "2018");
                    MessageBox.Show(
                        string.Format(UiLocalizationService.Current.GetString("Save2018_Saved_Message"), dialog.FileName),
                        UiLocalizationService.Current.GetString("Save2018_Dialog_Title"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Критическая ошибка: {ex.Message}", "SaveAsNavis2018");
                MessageBox.Show(
                    string.Format(UiLocalizationService.Current.GetString("Save2018_Failed_Message"), ex.Message),
                    UiLocalizationService.Current.GetString("Save2018_Dialog_Title"),
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return -1;
            }
        }
    }
}
