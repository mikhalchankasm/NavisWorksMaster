using Autodesk.Navisworks.Api.Plugins;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using NavisHelper.Core;

namespace NavisHelper
{
    /// <summary>
    /// Информация о версии приложения
    /// </summary>
    public static class AppVersion
    {
        /// <summary>
        /// Получает версию сборки
        /// </summary>
        public static Version Version
        {
            get
            {
                return Assembly.GetExecutingAssembly().GetName().Version;
            }
        }

        /// <summary>
        /// Получает строковое представление версии (Major.Minor.Build.Revision)
        /// </summary>
        public static string VersionString
        {
            get
            {
                var v = Version;
                return v.ToString();
            }
        }

        /// <summary>
        /// Полная версия с ревизией
        /// </summary>
        public static string FullVersionString
        {
            get
            {
                return Version.ToString();
            }
        }

        /// <summary>
        /// Название продукта
        /// </summary>
        public static string ProductName
        {
            get
            {
                var attr = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyProductAttribute>();
                return attr?.Product ?? "NavisHelper";
            }
        }

        /// <summary>
        /// Описание
        /// </summary>
        public static string Description
        {
            get
            {
                var attr = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyDescriptionAttribute>();
                return attr?.Description ?? "";
            }
        }

        /// <summary>
        /// Компания
        /// </summary>
        public static string Company
        {
            get
            {
                var attr = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyCompanyAttribute>();
                return attr?.Company ?? "";
            }
        }

        /// <summary>
        /// Copyright
        /// </summary>
        public static string Copyright
        {
            get
            {
                var attr = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyCopyrightAttribute>();
                return attr?.Copyright ?? "";
            }
        }
    }

    /// <summary>
    /// Плагин About
    /// </summary>
    [Plugin("AboutNavisHelper", "CBC", DisplayName = "About NavisHelper")]
    [AddInPlugin(AddInLocation.None)]
    public class AboutNavisHelper : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            Logger.Info("📋 Открытие диалога About...", "AboutNavisHelper");
            
            try
            {
                using (var dialog = new AboutForm())
                {
                    dialog.ShowDialog();
                }
                return 1;
            }
            catch (Exception ex)
            {
                Logger.Error($"❌ Ошибка: {ex.Message}", "AboutNavisHelper");
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 0;
            }
        }
    }

    /// <summary>
    /// Диалог About
    /// </summary>
    public class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            // Настройки формы
            this.Text = "О программе NavisHelper";
            this.Size = new Size(450, 350);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.BackColor = Color.White;

            // Заголовок
            var titleLabel = new Label
            {
                Text = AppVersion.ProductName,
                Location = new Point(20, 20),
                Size = new Size(400, 40),
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                ForeColor = Color.FromArgb(51, 51, 51)
            };
            this.Controls.Add(titleLabel);

            // Версия
            var versionLabel = new Label
            {
                Text = $"Версия {AppVersion.VersionString}",
                Location = new Point(20, 65),
                Size = new Size(400, 25),
                Font = new Font("Segoe UI", 12),
                ForeColor = Color.FromArgb(100, 100, 100)
            };
            this.Controls.Add(versionLabel);

            // Разделитель
            var separator = new Panel
            {
                Location = new Point(20, 100),
                Size = new Size(395, 1),
                BackColor = Color.FromArgb(220, 220, 220)
            };
            this.Controls.Add(separator);

            // Описание
            var descLabel = new Label
            {
                Text = AppVersion.Description,
                Location = new Point(20, 115),
                Size = new Size(395, 25),
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(80, 80, 80)
            };
            this.Controls.Add(descLabel);

            // Функции
            var featuresLabel = new Label
            {
                Text = "Возможности:\n" +
                       "• Автоматическое окрашивание объектов (10 цветовых схем)\n" +
                       "• Работа с Clash Detective\n" +
                       "• Импорт/экспорт данных\n" +
                       "• Управление viewpoints\n" +
                       "• И многое другое...",
                Location = new Point(20, 145),
                Size = new Size(395, 100),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(60, 60, 60)
            };
            this.Controls.Add(featuresLabel);

            // Copyright
            var copyrightLabel = new Label
            {
                Text = AppVersion.Copyright,
                Location = new Point(20, 250),
                Size = new Size(200, 20),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(120, 120, 120)
            };
            this.Controls.Add(copyrightLabel);

            // Компания
            var companyLabel = new Label
            {
                Text = AppVersion.Company,
                Location = new Point(20, 270),
                Size = new Size(200, 20),
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(120, 120, 120)
            };
            this.Controls.Add(companyLabel);

            // Кнопка OK
            var okButton = new Button
            {
                Text = "OK",
                Location = new Point(330, 270),
                Size = new Size(90, 30),
                DialogResult = DialogResult.OK,
                FlatStyle = FlatStyle.Flat
            };
            okButton.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
            this.Controls.Add(okButton);
            this.AcceptButton = okButton;
        }
    }
}

