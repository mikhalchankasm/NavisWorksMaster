using System;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Core.Localization;

namespace NavisHelper
{
    [Plugin("ShowNavisHelperPanel", "CBC", DisplayName = "NavisHelper Panel")]
    [AddInPlugin(AddInLocation.None)]
    public class ShowNavisHelperPanel : AddInPlugin
    {
        private static Form _panelForm;
        private static UiLocalizationBindingRegistry _localizationBindings;

        public override int Execute(params string[] parameters)
        {
            if (_panelForm != null && !_panelForm.IsDisposed)
            {
                _panelForm.BringToFront();
                return 0;
            }

            _panelForm = new Form
            {
                Text = "NavisHelper",
                Width = 300,
                Height = 750,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.SizableToolWindow,
                TopMost = true,
                ShowInTaskbar = false,
                Font = new Font("Segoe UI", 9f)
            };
            _localizationBindings = new UiLocalizationBindingRegistry();
            _panelForm.FormClosed += OnPanelFormClosed;
            _panelForm.Disposed += OnPanelFormDisposed;
            SubscribeToLanguageChanges();

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(10)
            };

            // === Инструменты ===
            panel.Controls.Add(CreateGroupLabel("PanelGroupTools"));
            panel.Controls.Add(CreateButton("PanelButtonColorsByName", "ColorsByName.CBC"));
            panel.Controls.Add(CreateButton("PanelButtonOverridePdmsColors", "HideItems.CBC"));
            panel.Controls.Add(CreateButton("PanelButtonMarkupSelection", "MarkupViewpoint.CBC"));
            panel.Controls.Add(CreateButton("PanelButtonHeightMarks", "ShortestDistanceMarker.CBC"));
            panel.Controls.Add(CreateButton("PanelButtonTopSection", "TopViewSection.CBC"));
            panel.Controls.Add(CreateButton("PanelButtonBoundingRect", "TopViewBoundingRect.CBC"));
            panel.Controls.Add(CreateButton("PanelButtonBoundingHatch", "TopViewBoundingHatch.CBC"));
            panel.Controls.Add(CreateGroupLabel("PanelGroupSelectionMarkers"));
            panel.Controls.Add(CreateButton("PanelButtonCenterPoints", "SelectionCenterDotMarker.CBC"));
            panel.Controls.Add(CreateButton("PanelButtonCenterMarker", "SelectionHatchMarker.CBC"));
            panel.Controls.Add(CreateButton("PanelButtonBoundsMarker", "SelectionHatchBoundsMarker.CBC"));
            panel.Controls.Add(CreateButton("PanelButtonCopyNames", "CopySelectedNames.CBC"));

            panel.Controls.Add(CreateSeparator());

            // === Перенос цветов ===
            panel.Controls.Add(CreateGroupLabel("PanelGroupColorTransfer"));
            panel.Controls.Add(CreateButton("PanelButtonExportColors", "ExportColors.CBC"));
            panel.Controls.Add(CreateButton("PanelButtonImportColors", "ImportColors.CBC"));

            panel.Controls.Add(CreateSeparator());

            // === AI Цвета ===
            panel.Controls.Add(CreateGroupLabel("PanelGroupAiColors"));
            panel.Controls.Add(CreateButton("PanelButtonAiColoring", "AIColorObjects.CBC"));
            panel.Controls.Add(CreateButton("PanelButtonColorScheme", "AIColorSchemeSelector.CBC"));

            panel.Controls.Add(CreateSeparator());

            // === Фильтрация ===
            panel.Controls.Add(CreateGroupLabel("PanelGroupFiltering"));
            panel.Controls.Add(CreateButton("PanelButtonFilterList", "FilterModels.COMPANY"));

            panel.Controls.Add(CreateSeparator());

            // === Импорт / Экспорт ===
            panel.Controls.Add(CreateGroupLabel("PanelGroupImportExport"));
            panel.Controls.Add(CreateButton("PanelButtonLoadCsv", "CsvAttributeLoader.CSVL"));
            panel.Controls.Add(CreateButton("PanelButtonImportPsLists", "ImportPslists.CBC"));
            panel.Controls.Add(CreateButton("PanelButtonSaveHierarchy", "SaveHierarhy.COMPANY"));
            panel.Controls.Add(CreateButton("PanelButtonSaveViewpoints", "SaveViewpiontList.COMPANY"));
            panel.Controls.Add(CreateButton("PanelButtonSaveNwc2018", "SaveAsNavis2018.MS"));

            panel.Controls.Add(CreateSeparator());

            // === Точки обзора ===
            panel.Controls.Add(CreateGroupLabel("PanelGroupViewpoints"));
            panel.Controls.Add(CreateButton("PanelButtonSortViewpoints", "SortViewpoints.COMPANY"));

            panel.Controls.Add(CreateSeparator());

            // === Коллизии ===
            panel.Controls.Add(CreateGroupLabel("PanelGroupClashes"));
            panel.Controls.Add(CreateButton("PanelButtonRunClashes", "RunSaveClashReport.MS"));

            panel.Controls.Add(CreateSeparator());

            // === Справка ===
            panel.Controls.Add(CreateGroupLabel("PanelGroupHelp"));
            panel.Controls.Add(CreateButton("PanelButtonAbout", "AboutNavisHelper.CBC"));

            _panelForm.Controls.Add(panel);
            _panelForm.Show();
            NavisHelper.Agent.AgentRuntime.Initialize(_panelForm);

            return 0;
        }

        private Label CreateGroupLabel(string resourceKey)
        {
            var label = new Label
            {
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0x33, 0x33, 0x33),
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 4)
            };
            BindControlText(label, resourceKey);
            return label;
        }

        private static string Text(string resourceKey)
        {
            return UiLocalizationService.Current.GetString(resourceKey);
        }

        private static void SubscribeToLanguageChanges()
        {
            UiLocalizationService.Current.LanguageChanged += OnLanguageChanged;
        }

        private static void OnLanguageChanged(object sender, EventArgs e)
        {
            Form form = _panelForm;
            UiLocalizationBindingRegistry bindings = _localizationBindings;
            if (form == null || form.IsDisposed || bindings == null)
                return;

            Action update = bindings.Refresh;

            if (form.InvokeRequired)
                form.BeginInvoke(update);
            else
                update();
        }

        private static void OnPanelFormClosed(object sender, FormClosedEventArgs e)
        {
            ReleasePanelLocalization(sender as Form);
        }

        private static void OnPanelFormDisposed(object sender, EventArgs e)
        {
            ReleasePanelLocalization(sender as Form);
        }

        private static void ReleasePanelLocalization(Form form)
        {
            if (form == null || !ReferenceEquals(_panelForm, form))
                return;

            UiLocalizationService.Current.LanguageChanged -= OnLanguageChanged;
            _localizationBindings?.Dispose();
            _localizationBindings = null;
            _panelForm = null;
        }

        private static void BindControlText(Control control, string resourceKey)
        {
            UiLocalizationBindingRegistry bindings = _localizationBindings;
            if (bindings == null)
                throw new InvalidOperationException("Panel localization registry is unavailable.");

            bindings.Register(
                control,
                "Text",
                () => control.Text = Text(resourceKey));
        }

        private Button CreateButton(string resourceKey, string pluginId)
        {
            var btn = new Button
            {
                Tag = pluginId,
                Width = 250,
                Height = 32,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Margin = new Padding(0, 2, 0, 2),
                Cursor = Cursors.Hand,
                BackColor = Color.White
            };
            BindControlText(btn, resourceKey);
            btn.FlatAppearance.BorderColor = Color.FromArgb(0xCC, 0xCC, 0xCC);
            btn.Click += OnButtonClick;
            return btn;
        }

        private Label CreateSeparator()
        {
            return new Label
            {
                Height = 2,
                Width = 250,
                BorderStyle = BorderStyle.Fixed3D,
                Margin = new Padding(0, 6, 0, 6)
            };
        }

        private void OnButtonClick(object sender, EventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var pluginId = btn.Tag as string;
            if (string.IsNullOrEmpty(pluginId)) return;

            try
            {
                // Try the compatibility path before the Navisworks plugin registry.
                if (PluginCommandExecutor.TryExecuteDirect(pluginId))
                    return;

                // Иначе через реестр плагинов Navisworks
                Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin(pluginId);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    UiLocalizationService.Current.Format("CommonErrorMessageFormat", ex.Message),
                    "NavisHelper",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
