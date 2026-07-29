using System;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using NavisHelper.Core;
using NavisHelper.Core.Localization;
using Application = Autodesk.Navisworks.Api.Application;

namespace NavisHelper
{
    [Plugin("MarkupViewpoint", "CBC", DisplayName = "Пометить выделенное")]
    [AddInPlugin(AddInLocation.None)]
    public class MarkupViewpoint : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                Document document = Application.ActiveDocument;
                if (document == null)
                {
                    Logger.Error("Нет активного документа", "MarkupViewpoint");
                    return 0;
                }

                if (document.ActiveView == null || document.CurrentViewpoint == null)
                {
                    Logger.Error("Нет активного окна вида", "MarkupViewpoint");
                    return 0;
                }

                var selection = document.CurrentSelection.SelectedItems;
                if (selection == null || selection.Count == 0)
                {
                    MessageBox.Show(
                        UiLocalizationService.Current.GetString("CommonNoSelection"),
                        UiLocalizationService.Current.GetString("MarkupTitle"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                Viewpoint viewpoint = document.CurrentViewpoint.CreateCopy();

                string defaultName =
                    $"{UiLocalizationService.Current.GetString("MarkupDefaultNamePrefix")} {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                string viewpointName = PromptViewpointName(defaultName);
                if (viewpointName == null)
                    return 0;

                var response = new DocumentCommandService().MarkupSelection(
                    document,
                    new MarkupSelectionRequest
                    {
                        Name = viewpointName,
                        Source = "current_selection",
                        AutoTopView = false,
                        FitToSelection = false,
                        MinMarkSizeMm = GetCameraRelativeMinimumMarkSizeMm(viewpoint),
                        MarkSoloMinSizeMm = 0,
                        MarkMergeGapMm = 0,
                        MarkStyle = MarkupRedlineJsonBuilder.TargetStyle,
                        ClusterBy = "none",
                        Apply = true,
                    });

                int markCount = response.MarkCount.GetValueOrDefault();
                int skippedCount = response.SkippedItemCount.GetValueOrDefault();
                string message = $"Создана точка обзора \"{response.Name}\" с {markCount} эллипсами";
                if (skippedCount > 0)
                    message += $" ({skippedCount} элементов не удалось спроецировать)";

                Logger.Info(message, "MarkupViewpoint");
                if (response.Warnings != null)
                {
                    foreach (string warning in response.Warnings)
                        Logger.Info("Предупреждение: " + warning, "MarkupViewpoint");
                }
                return 0;
            }
            catch (AgentCommandException ex)
            {
                Logger.Error($"Ошибка разметки: {ex.Message}", "MarkupViewpoint");
                MessageBox.Show(
                    GetUserErrorMessage(ex),
                    UiLocalizationService.Current.GetString("MarkupTitle"),
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка: {ex.Message}\n{ex.StackTrace}", "MarkupViewpoint");
                MessageBox.Show(
                    UiLocalizationService.Current.GetString("MarkupCreateFailed"),
                    UiLocalizationService.Current.GetString("MarkupTitle"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 0;
            }
        }

        private static double GetCameraRelativeMinimumMarkSizeMm(Viewpoint viewpoint)
        {
            const double frameHeightFraction = 0.02;
            const double maximumSizeMm = 1000000;

            double documentUnitsPerMillimeter = SectionBoxHelper.MmToDocUnits(1);
            if (documentUnitsPerMillimeter <= 1e-12 ||
                double.IsNaN(documentUnitsPerMillimeter) ||
                double.IsInfinity(documentUnitsPerMillimeter))
            {
                return 500;
            }

            double sizeMm = viewpoint.HeightField * frameHeightFraction / documentUnitsPerMillimeter;
            if (double.IsNaN(sizeMm) || double.IsInfinity(sizeMm) || sizeMm <= 0)
                return 500;
            return Math.Min(sizeMm, maximumSizeMm);
        }

        private static string GetUserErrorMessage(AgentCommandException exception)
        {
            if (string.Equals(exception.ErrorCode, ErrorCodes.ViewpointNameConflict, StringComparison.Ordinal))
            {
                return UiLocalizationService.Current.GetString("MarkupViewpointNameConflict");
            }

            if (string.Equals(exception.ErrorCode, ErrorCodes.SavedViewpointNotFound, StringComparison.Ordinal))
                return UiLocalizationService.Current.GetString("MarkupSavedViewpointsUnavailable");

            if (string.Equals(exception.ErrorCode, ErrorCodes.NoSelection, StringComparison.Ordinal))
                return UiLocalizationService.Current.GetString("CommonNoSelection");

            return UiLocalizationService.Current.GetString("MarkupCreateFailed");
        }

        private static string PromptViewpointName(string defaultName)
        {
            using (var form = new Form())
            {
                form.Text = UiLocalizationService.Current.GetString("CommonViewpointNameTitle");
                form.Size = new Size(420, 130);
                form.StartPosition = FormStartPosition.CenterScreen;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                string initialText = defaultName;
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string clip = Clipboard.GetText().Trim();
                        if (!string.IsNullOrEmpty(clip) && !clip.Contains("\n") && !clip.Contains("\r"))
                            initialText = clip;
                    }
                }
                catch
                {
                }

                var label = new Label
                {
                    Text = UiLocalizationService.Current.GetString("CommonViewpointNameLabel"),
                    Left = 10,
                    Top = 10,
                    Width = 380
                };
                var textBox = new TextBox { Left = 10, Top = 35, Width = 385, Text = initialText };
                textBox.SelectAll();
                var okButton = new Button
                {
                    Text = UiLocalizationService.Current.GetString("CommonOk"),
                    Left = 230,
                    Top = 70,
                    Width = 75,
                    DialogResult = DialogResult.OK
                };
                var cancelButton = new Button
                {
                    Text = UiLocalizationService.Current.GetString("CommonCancel"),
                    Left = 315,
                    Top = 70,
                    Width = 75,
                    DialogResult = DialogResult.Cancel
                };

                form.Controls.AddRange(new Control[] { label, textBox, okButton, cancelButton });
                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                if (form.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(textBox.Text))
                    return textBox.Text.Trim();

                return null;
            }
        }
    }
}
