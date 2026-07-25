using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using System;
using System.Windows.Forms;
using NavisHelper.Core;

namespace NavisHelper
{
    /// <summary>
    /// Плагин для выбора цветовой схемы ИИ-окрашивания
    /// </summary>
    [Plugin("AIColorSchemeSelector", "CBC", DisplayName = "AI Color Scheme")]
    [AddInPlugin(AddInLocation.None)]
    public class AIColorSchemeSelector : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            Logger.Info("🎨 Открытие диалога выбора цветовой схемы...", "AIColorSchemeSelector");
            
            try
            {
                // Получаем текущую схему
                var currentScheme = AIConfig.Instance.GetColorSchemeType();
                
                // Создаем диалог выбора
                using (var dialog = new ColorSchemeDialog(currentScheme))
                {
                    if (dialog.ShowDialog() == DialogResult.OK)
                    {
                        AIConfig.Instance.SetColorScheme((int)dialog.SelectedScheme);
                        Logger.Info($"✅ Выбрана схема: {ColorSchemes.GetSchemeNameRu(dialog.SelectedScheme)}", "AIColorSchemeSelector");
                        
                        MessageBox.Show(
                            $"Цветовая схема изменена на:\n{(int)dialog.SelectedScheme}. {ColorSchemes.GetSchemeNameRu(dialog.SelectedScheme)}",
                            "Цветовая схема",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        
                        return 1;
                    }
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"❌ Ошибка: {ex.Message}", "AIColorSchemeSelector");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 0;
            }
        }
    }

    /// <summary>
    /// Диалог выбора цветовой схемы
    /// </summary>
    public class ColorSchemeDialog : Form
    {
        public ColorSchemeType SelectedScheme { get; private set; }
        
        private ListBox _listBox;
        private Panel _previewPanel;
        private Label _descriptionLabel;
        
        public ColorSchemeDialog(ColorSchemeType currentScheme)
        {
            SelectedScheme = currentScheme;
            InitializeComponents();
            _listBox.SelectedIndex = (int)currentScheme - 1;
        }
        
        private void InitializeComponents()
        {
            // Настройки формы
            this.Text = "Выбор цветовой схемы";
            this.Size = new System.Drawing.Size(500, 450);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            
            // Заголовок
            var titleLabel = new Label
            {
                Text = "Выберите цветовую схему для автоматического окрашивания:",
                Location = new System.Drawing.Point(15, 15),
                Size = new System.Drawing.Size(460, 25),
                Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold)
            };
            this.Controls.Add(titleLabel);
            
            // Список схем
            _listBox = new ListBox
            {
                Location = new System.Drawing.Point(15, 45),
                Size = new System.Drawing.Size(250, 200),
                Font = new System.Drawing.Font("Segoe UI", 10)
            };
            
            // Заполняем список
            foreach (ColorSchemeType scheme in Enum.GetValues(typeof(ColorSchemeType)))
            {
                _listBox.Items.Add($"{(int)scheme}. {ColorSchemes.GetSchemeNameRu(scheme)}");
            }
            
            _listBox.SelectedIndexChanged += ListBox_SelectedIndexChanged;
            this.Controls.Add(_listBox);
            
            // Панель превью цветов
            var previewLabel = new Label
            {
                Text = "Превью цветов:",
                Location = new System.Drawing.Point(280, 45),
                Size = new System.Drawing.Size(120, 20),
                Font = new System.Drawing.Font("Segoe UI", 9)
            };
            this.Controls.Add(previewLabel);
            
            _previewPanel = new Panel
            {
                Location = new System.Drawing.Point(280, 65),
                Size = new System.Drawing.Size(190, 180),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = System.Drawing.Color.White
            };
            this.Controls.Add(_previewPanel);
            
            // Описание
            _descriptionLabel = new Label
            {
                Location = new System.Drawing.Point(15, 255),
                Size = new System.Drawing.Size(455, 80),
                Font = new System.Drawing.Font("Segoe UI", 9),
                ForeColor = System.Drawing.Color.DarkGray
            };
            this.Controls.Add(_descriptionLabel);
            
            // Кнопки
            var okButton = new Button
            {
                Text = "OK",
                Location = new System.Drawing.Point(280, 370),
                Size = new System.Drawing.Size(90, 30),
                DialogResult = DialogResult.OK
            };
            okButton.Click += (s, e) => 
            {
                if (_listBox.SelectedIndex >= 0)
                    SelectedScheme = (ColorSchemeType)(_listBox.SelectedIndex + 1);
            };
            this.Controls.Add(okButton);
            this.AcceptButton = okButton;
            
            var cancelButton = new Button
            {
                Text = "Отмена",
                Location = new System.Drawing.Point(380, 370),
                Size = new System.Drawing.Size(90, 30),
                DialogResult = DialogResult.Cancel
            };
            this.Controls.Add(cancelButton);
            this.CancelButton = cancelButton;
        }
        
        private void ListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_listBox.SelectedIndex < 0) return;
            
            var scheme = (ColorSchemeType)(_listBox.SelectedIndex + 1);
            UpdatePreview(scheme);
            UpdateDescription(scheme);
        }
        
        private void UpdatePreview(ColorSchemeType scheme)
        {
            _previewPanel.Controls.Clear();
            
            var palette = ColorSchemes.GetPalette(scheme);
            if (palette == null)
            {
                // Для Random показываем случайные цвета
                var random = new Random(42); // Фиксированный seed для консистентности превью
                palette = new string[10];
                for (int i = 0; i < 10; i++)
                {
                    int r = random.Next(50, 256);
                    int g = random.Next(50, 256);
                    int b = random.Next(50, 256);
                    palette[i] = $"{r},{g},{b}";
                }
            }
            
            int cols = 5;
            int rows = 2;
            int boxWidth = 35;
            int boxHeight = 80;
            int margin = 3;
            
            for (int i = 0; i < Math.Min(palette.Length, cols * rows); i++)
            {
                int col = i % cols;
                int row = i / cols;
                
                var colorParts = palette[i].Split(',');
                if (colorParts.Length != 3) continue;
                
                var panel = new Panel
                {
                    Location = new System.Drawing.Point(col * (boxWidth + margin) + 5, row * (boxHeight + margin) + 5),
                    Size = new System.Drawing.Size(boxWidth, boxHeight),
                    BackColor = System.Drawing.Color.FromArgb(
                        int.Parse(colorParts[0]),
                        int.Parse(colorParts[1]),
                        int.Parse(colorParts[2]))
                };
                _previewPanel.Controls.Add(panel);
            }
        }
        
        private void UpdateDescription(ColorSchemeType scheme)
        {
            string description;
            switch (scheme)
            {
                case ColorSchemeType.Grayscale:
                    description = "Классические оттенки серого.\nИдеально для технических чертежей и нейтральной визуализации.";
                    break;
                case ColorSchemeType.GrayPastel:
                    description = "Серые тона с мягкими пастельными акцентами.\nБаланс между техничностью и визуальной привлекательностью.";
                    break;
                case ColorSchemeType.Warm:
                    description = "Теплые тона: оранжевый, персиковый, медовый.\nПодходит для уютной визуализации интерьеров.";
                    break;
                case ColorSchemeType.Cool:
                    description = "Холодные тона: голубой, стальной, лавандовый.\nСоздает ощущение прохлады и современности.";
                    break;
                case ColorSchemeType.Earth:
                    description = "Земляные тона: коричневый, бежевый, песочный.\nНатуральная палитра для ландшафтных проектов.";
                    break;
                case ColorSchemeType.Ocean:
                    description = "Морская палитра: бирюзовый, аквамарин, морская пена.\nОсвежающие цвета для водных объектов.";
                    break;
                case ColorSchemeType.Forest:
                    description = "Лесная тема: зеленый, оливковый, хаки.\nПриродная палитра для экстерьеров и парков.";
                    break;
                case ColorSchemeType.Architectural:
                    description = "Архитектурные нейтральные тона: камень, бетон, мрамор.\nПрофессиональная палитра для BIM-моделей.";
                    break;
                case ColorSchemeType.HighContrast:
                    description = "Яркие контрастные цвета для максимальной различимости.\nХорошо для презентаций и выделения категорий.";
                    break;
                case ColorSchemeType.Random:
                    description = "Случайная генерация цветов.\nМаксимальное разнообразие, но без общей темы.";
                    break;
                default:
                    description = "";
                    break;
            }
            
            _descriptionLabel.Text = description;
        }
    }
}

