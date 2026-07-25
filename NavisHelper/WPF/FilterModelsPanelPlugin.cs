using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Core;

namespace NavisHelper.WPF
{
    //[Plugin("FilterModelsPanelPlugin", "COMPANY", DisplayName = "Filter Models Panel")]
    [DockPanePlugin(1000, 300, FixedSize = false)]
    public class FilterModelsPanelPlugin : DockPanePlugin
    {
        public override Control CreateControlPane()
        {
            try
            {
                // Создаем элемент управления WPF
                var wpfPanel = new FilterModelsPanel();
                
                // Создаем хост для WPF элемента
                var host = new ElementHost
                {
                    Dock = System.Windows.Forms.DockStyle.Fill,
                    Child = wpfPanel
                };
                
                return host;
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка при создании панели: {ex.Message}", "FilterModelsPanelPlugin");
                return null;
            }
        }

        public override void DestroyControlPane(Control pane)
        {
            if (pane is ElementHost host)
            {
                if (host.Child is FilterModelsPanel panel)
                {
                    // Освобождаем ресурсы, если необходимо
                    host.Child = null;
                }
                
                host.Dispose();
            }
        }
    }
} 