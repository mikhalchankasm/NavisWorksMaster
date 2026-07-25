using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using Autodesk.Windows;
using NavisHelper.Agent;

namespace NavisHelper
{
    [Plugin("NavisHelper.RibbonLoader", "NavisHelper", DisplayName = "NavisHelper")]
    public class RibbonLoader : EventWatcherPlugin
    {
        private DispatcherTimer _timer;

        internal static void EnsureAgentRuntimeInitialized()
        {
            AgentRuntime.Initialize(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        }

        public override void OnLoaded()
        {
            EnsureAgentRuntimeInitialized();

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(2);
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        public override void OnUnloading()
        {
            AgentRuntime.Shutdown();

            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTimerTick;
                _timer = null;
            }
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            try
            {
                EnsureAgentRuntimeInitialized();

                var ribbon = ComponentManager.Ribbon;
                if (ribbon == null)
                    return;

                if (CreateRibbonButtons(ribbon))
                {
                    _timer.Stop();
                    _timer.Tick -= OnTimerTick;
                }
            }
            catch
            {
                // Ribbon not ready yet, will retry
            }
        }

        private bool CreateRibbonButtons(RibbonControl ribbon)
        {
            // === Вкладка NavisHelper ===
            var tab = GetOrCreateTab(ribbon, "NavisHelper", "NH");
            if (tab == null)
                return false;

            var panel = GetOrCreatePanel(tab, "NavisHelper", "N");

            var button = new RibbonButton();
            button.Id = "NavisHelper.Button.ShowPanel";
            button.Text = "NavisHelper";
            button.ShowText = true;
            button.ShowImage = true;
            button.Size = RibbonItemSize.Large;
            button.Orientation = System.Windows.Controls.Orientation.Vertical;
            button.KeyTip = "NH";
            button.CommandHandler = new ShowPanelCommandHandler();
            button.Image = LoadEmbeddedImage("NavisHelper.Resources.ColorsByName_16.png");
            button.LargeImage = LoadEmbeddedImage("NavisHelper.Resources.ColorsByName_32.png");
            panel.Source.Items.Add(button);

            // Parent & Child — встроен в dock panel (вкладка "Навигация")

            return true;
        }

        private static RibbonTab GetOrCreateTab(RibbonControl ribbon, string title, string keyTip)
        {
            string tabId = title + ".Tab";

            foreach (var existingTab in ribbon.Tabs)
            {
                if (existingTab.Id == tabId)
                    return existingTab;
            }

            var tab = new RibbonTab();
            tab.Id = tabId;
            tab.Title = title;
            tab.KeyTip = keyTip;
            ribbon.Tabs.Add(tab);

            return tab;
        }

        private static RibbonPanel GetOrCreatePanel(RibbonTab tab, string title, string keyTip)
        {
            string panelId = title + ".Panel";

            foreach (var existingPanel in tab.Panels)
            {
                if (existingPanel.Source != null && existingPanel.Source.Id == panelId + "_Source")
                    return existingPanel;
            }

            var source = new RibbonPanelSource();
            source.Id = panelId + "_Source";
            source.Title = title;
            source.KeyTip = keyTip;

            var panel = new RibbonPanel();
            panel.Source = source;
            tab.Panels.Add(panel);

            return panel;
        }

        private static ImageSource LoadEmbeddedImage(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return null;

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = stream;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Обработчик команды кнопки — открывает/закрывает DockPane.
    /// </summary>
    internal class ShowPanelCommandHandler : ICommand
    {
        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public void Execute(object parameter)
        {
            try
            {
                RibbonLoader.EnsureAgentRuntimeInitialized();

                var pluginRecord = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin("NavisHelperDockPane.CBC");
                if (pluginRecord == null)
                {
                    System.Windows.MessageBox.Show(
                        "Плагин NavisHelperDockPane.CBC не найден в реестре.",
                        "NavisHelper", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                // Если плагин ещё не загружен — загрузить
                if (pluginRecord.IsLoaded == false)
                {
                    pluginRecord.LoadPlugin();
                }

                var dockPane = pluginRecord.LoadedPlugin as DockPanePlugin;
                if (dockPane == null)
                {
                    System.Windows.MessageBox.Show(
                        "Не удалось загрузить DockPane. LoadedPlugin = " +
                        (pluginRecord.LoadedPlugin?.GetType().Name ?? "null"),
                        "NavisHelper", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                dockPane.Visible = !dockPane.Visible;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    "Ошибка: " + ex.Message,
                    "NavisHelper", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
