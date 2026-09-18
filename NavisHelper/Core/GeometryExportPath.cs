using System.Text.RegularExpressions;

namespace NavisHelper.Core
{
    internal static class GeometryExportPath
    {
        public static bool IsFullyQualifiedWindowsPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                Regex.IsMatch(path, @"^(?:[A-Za-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+[\\/])");
        }
    }
}
