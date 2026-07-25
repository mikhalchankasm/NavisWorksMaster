using System;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Core;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisHelper
{
    /// <summary>
    /// Compatibility entry point retained for existing ribbon and bundle command IDs.
    /// The former shortest-distance prototype has been replaced by bounding-box Z markers.
    /// </summary>
    [Plugin("ShortestDistanceMarker", "CBC", DisplayName = "Метки высот Z")]
    [AddInPlugin(AddInLocation.None)]
    public sealed class ShortestDistanceMarker : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            try
            {
                if (TryShowHeightMarksPanel())
                    return 0;

                MessageBox.Show(
                    "Не удалось открыть вкладку «Высоты Z» в панели NavisHelper.",
                    "Метки высот Z",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return 1;
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось открыть вкладку высот Z: " + ex, "ElevationMarker");
                MessageBox.Show(
                    "Не удалось открыть вкладку «Высоты Z». Подробности записаны в журнал.",
                    "Метки высот Z",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }
        }

        private static bool TryShowHeightMarksPanel()
        {
            NavisHelper.WPF.NavisHelperPanel panel = NavisHelper.WPF.NavisHelperPanel.Current;
            PluginRecord record = NwApplication.Plugins.FindPlugin("NavisHelperDockPane.CBC");
            if (record == null)
                return false;

            if (!record.IsLoaded)
                record.LoadPlugin();

            var dockPane = record.LoadedPlugin as DockPanePlugin;
            if (dockPane == null)
                return false;

            dockPane.Visible = true;
            if (panel == null)
            {
                System.Windows.Forms.Application.DoEvents();
                panel = NavisHelper.WPF.NavisHelperPanel.Current;
            }

            return panel != null && panel.ShowHeightMarksTab();
        }
    }
}
