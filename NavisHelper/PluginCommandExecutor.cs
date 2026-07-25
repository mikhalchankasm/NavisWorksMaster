using System;
using System.Collections.Generic;

namespace NavisHelper
{
    internal static class PluginCommandExecutor
    {
        private static readonly Dictionary<string, Action> DirectCommands = new Dictionary<string, Action>(StringComparer.Ordinal)
        {
            { "ExportColors.CBC", () => new ExportColors().Execute() },
            { "ImportColors.CBC", () => new ImportColors().Execute() }
        };

        internal static bool TryExecuteDirect(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId))
                return false;

            Action command;
            if (!DirectCommands.TryGetValue(pluginId, out command))
                return false;

            command();
            return true;
        }
    }
}
