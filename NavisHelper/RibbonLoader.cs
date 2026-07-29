using System;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Autodesk.Navisworks.Api.Plugins;
using Autodesk.Windows;
using NavisHelper.Agent;
using NavisHelper.Core.Localization;

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
            var tab = GetOrCreateTab(ribbon);
            if (tab == null)
                return false;

            var panel = GetOrCreatePanel(tab);
            RibbonButton showPanelButton = GetOrCreateButton(
                panel.Source,
                RibbonIds.ShowPanelButton,
                () => new RibbonButton
                {
                    ShowText = true,
                    ShowImage = true,
                    Size = RibbonItemSize.Large,
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    KeyTip = "NH",
                    CommandHandler = new ShowPanelCommandHandler(),
                    Image = LoadEmbeddedImage("NavisHelper.Resources.ColorsByName_16.png"),
                    LargeImage = LoadEmbeddedImage("NavisHelper.Resources.ColorsByName_32.png")
                });

            tab.Title = "NavisHelper";
            panel.Source.Title = "NavisHelper";
            showPanelButton.Text = "NavisHelper";
            showPanelButton.ToolTip = "NavisHelper";

            return true;
        }

        private static RibbonTab GetOrCreateTab(RibbonControl ribbon)
        {
            foreach (var existingTab in ribbon.Tabs)
            {
                if (existingTab.Id == RibbonIds.Tab)
                    return existingTab;
            }

            var tab = new RibbonTab
            {
                Id = RibbonIds.Tab,
                KeyTip = "NH"
            };
            ribbon.Tabs.Add(tab);

            return tab;
        }

        private static RibbonPanel GetOrCreatePanel(RibbonTab tab)
        {
            foreach (var existingPanel in tab.Panels)
            {
                if (existingPanel.Id == RibbonIds.Panel ||
                    (existingPanel.Source != null &&
                     existingPanel.Source.Id == RibbonIds.PanelSource))
                {
                    if (existingPanel.Source == null)
                    {
                        existingPanel.Source = new RibbonPanelSource
                        {
                            Id = RibbonIds.PanelSource,
                            KeyTip = "N"
                        };
                    }

                    return existingPanel;
                }
            }

            var source = new RibbonPanelSource
            {
                Id = RibbonIds.PanelSource,
                KeyTip = "N"
            };
            var panel = new RibbonPanel
            {
                Id = RibbonIds.Panel,
                Source = source
            };
            tab.Panels.Add(panel);

            return panel;
        }

        private static RibbonButton GetOrCreateButton(
            RibbonPanelSource source,
            string id,
            Func<RibbonButton> factory)
        {
            foreach (RibbonItem item in source.Items)
            {
                var existing = item as RibbonButton;
                if (existing != null && existing.Id == id)
                    return existing;
            }

            RibbonButton button = factory();
            button.Id = id;
            source.Items.Add(button);
            return button;
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
        public event EventHandler CanExecuteChanged
        {
            add { }
            remove { }
        }

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
                    RibbonCommandMessages.ShowPluginMissing("NavisHelperDockPane.CBC");
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
                    RibbonCommandMessages.ShowDockPaneLoadFailed(
                        pluginRecord.LoadedPlugin?.GetType().Name ?? "null");
                    return;
                }

                dockPane.Visible = !dockPane.Visible;
            }
            catch (Exception ex)
            {
                RibbonCommandMessages.ShowError(ex.Message);
            }
        }
    }

}
