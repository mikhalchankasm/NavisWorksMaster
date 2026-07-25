using System;
using System.Globalization;

namespace NavisHelper.Agent.Contracts
{
    public static class ElevationViewpointNamingHelper
    {
        public static string BuildDayFolderName(DateTime timestamp)
        {
            return "Высотный день " +
                   timestamp.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        public static string BuildDefaultViewpointName(DateTime timestamp)
        {
            return "Высоты Z " +
                   timestamp.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        }
    }
}
