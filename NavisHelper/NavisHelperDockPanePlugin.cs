using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Core;
using NavisHelper.WPF;

namespace NavisHelper
{
    [Plugin("NavisHelperDockPane", "CBC",
        DisplayName = "NavisHelper",
        ToolTip = "Панель инструментов NavisHelper")]
    [DockPanePlugin(400, 600, FixedSize = false, AutoScroll = true)]
    public class NavisHelperDockPanePlugin : DockPanePlugin
    {
        public override Control CreateControlPane()
        {
            Logger.Info("NavisHelperDockPanePlugin.CreateControlPane invoked.", "AgentHost");
            var wpfPanel = new NavisHelperPanel();
            var host = new ElementHost
            {
                Dock = DockStyle.Fill,
                Child = wpfPanel
            };
            host.CreateControl();
            Logger.Info("ElementHost created for NavisHelper dock pane.", "AgentHost");
            NavisHelper.Agent.AgentRuntime.Initialize(host);
            return host;
        }

        public override void DestroyControlPane(Control pane)
        {
            if (pane is ElementHost host)
            {
                host.Child = null;
                host.Dispose();
            }

            NavisHelper.Agent.AgentRuntime.RestoreBackgroundControl();
        }
    }
}
