using System;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;

namespace NavisHelper
{
    [Plugin("ShowNavisHelperPanel", "CBC", DisplayName = "NavisHelper Panel")]
    [AddInPlugin(AddInLocation.None)]
    public class ShowNavisHelperPanel : AddInPlugin
    {
        private static Form _panelForm;

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

            var panel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                Padding = new Padding(10)
            };

            // === Инструменты ===
            panel.Controls.Add(CreateGroupLabel("Инструменты"));
            panel.Controls.Add(CreateButton("Colors By Name", "ColorsByName.CBC"));
            panel.Controls.Add(CreateButton("Override PDMS Colors", "HideItems.CBC"));
            panel.Controls.Add(CreateButton("Пометить выделенное", "MarkupViewpoint.CBC"));
            panel.Controls.Add(CreateButton("Метки высот Z", "ShortestDistanceMarker.CBC"));
            panel.Controls.Add(CreateButton("Вид сверху + сечение", "TopViewSection.CBC"));
            panel.Controls.Add(CreateButton("Габаритный прям.", "TopViewBoundingRect.CBC"));
            panel.Controls.Add(CreateButton("Заштриховать габарит", "TopViewBoundingHatch.CBC"));
            panel.Controls.Add(CreateGroupLabel("Маркеры выделения"));
            panel.Controls.Add(CreateButton("Точки центров", "SelectionCenterDotMarker.CBC"));
            panel.Controls.Add(CreateButton("Маркер центров", "SelectionHatchMarker.CBC"));
            panel.Controls.Add(CreateButton("Маркер габаритов", "SelectionHatchBoundsMarker.CBC"));
            panel.Controls.Add(CreateButton("Копировать имена", "CopySelectedNames.CBC"));

            panel.Controls.Add(CreateSeparator());

            // === Перенос цветов ===
            panel.Controls.Add(CreateGroupLabel("Перенос цветов"));
            panel.Controls.Add(CreateButton("Выгрузить цвета", "ExportColors.CBC"));
            panel.Controls.Add(CreateButton("Загрузить цвета", "ImportColors.CBC"));

            panel.Controls.Add(CreateSeparator());

            // === AI Цвета ===
            panel.Controls.Add(CreateGroupLabel("AI Цвета"));
            panel.Controls.Add(CreateButton("AI Окраска", "AIColorObjects.CBC"));
            panel.Controls.Add(CreateButton("Цветовая схема", "AIColorSchemeSelector.CBC"));

            panel.Controls.Add(CreateSeparator());

            // === Фильтрация ===
            panel.Controls.Add(CreateGroupLabel("Фильтрация"));
            panel.Controls.Add(CreateButton("Фильтр по списку из файла", "FilterModels.COMPANY"));

            panel.Controls.Add(CreateSeparator());

            // === Импорт / Экспорт ===
            panel.Controls.Add(CreateGroupLabel("Импорт / Экспорт"));
            panel.Controls.Add(CreateButton("Загрузка атрибутов из CSV", "CsvAttributeLoader.CSVL"));
            panel.Controls.Add(CreateButton("Импорт PS-листов", "ImportPslists.CBC"));
            panel.Controls.Add(CreateButton("Сохранить иерархию", "SaveHierarhy.COMPANY"));
            panel.Controls.Add(CreateButton("Сохранить точки обзора", "SaveViewpiontList.COMPANY"));
            panel.Controls.Add(CreateButton("Сохранить как NWC 2018", "SaveAsNavis2018.MS"));

            panel.Controls.Add(CreateSeparator());

            // === Точки обзора ===
            panel.Controls.Add(CreateGroupLabel("Точки обзора"));
            panel.Controls.Add(CreateButton("Сортировка точек обзора", "SortViewpoints.COMPANY"));

            panel.Controls.Add(CreateSeparator());

            // === Коллизии ===
            panel.Controls.Add(CreateGroupLabel("Коллизии"));
            panel.Controls.Add(CreateButton("Проверка всех коллизий", "RunSaveClashReport.MS"));

            panel.Controls.Add(CreateSeparator());

            // === Справка ===
            panel.Controls.Add(CreateGroupLabel("Справка"));
            panel.Controls.Add(CreateButton("О программе", "AboutNavisHelper.CBC"));

            _panelForm.Controls.Add(panel);
            _panelForm.Show();
            NavisHelper.Agent.AgentRuntime.Initialize(_panelForm);

            return 0;
        }

        private Label CreateGroupLabel(string text)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(0x33, 0x33, 0x33),
                AutoSize = true,
                Margin = new Padding(0, 6, 0, 4)
            };
        }

        private Button CreateButton(string text, string pluginId)
        {
            var btn = new Button
            {
                Text = text,
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
                    "Ошибка: " + ex.Message,
                    "NavisHelper",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
