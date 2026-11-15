using System;
using System.Collections.Generic;

namespace NavisHelper.Core
{
    internal static class PanelTabSelection
    {
        private static readonly string[] LegacyOrder =
        {
            "Panel_Tab_Model", "Panel_Tab_Colors", "Panel_Tab_Views",
            "Panel_Tab_Clashes", "Panel_Tab_Settings"
        };

        internal static string Resolve(string savedKey, int? legacyIndex, IReadOnlyList<string> currentKeys)
        {
            foreach (var key in currentKeys)
                if (string.Equals(key, savedKey, StringComparison.Ordinal)) return key;

            if (legacyIndex.HasValue && legacyIndex.Value >= 0 && legacyIndex.Value < LegacyOrder.Length)
            {
                var legacyKey = LegacyOrder[legacyIndex.Value];
                foreach (var key in currentKeys)
                    if (string.Equals(key, legacyKey, StringComparison.Ordinal)) return key;
            }

            return currentKeys[0];
        }
    }
}
