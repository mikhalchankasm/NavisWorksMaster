using System;
using System.IO;

namespace NavisHelper.Core
{
    internal sealed class ProtectedModeSettingsStore
    {
        internal ProtectedModeSettingsStore(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("A settings file path is required.", nameof(filePath));
            FilePath = filePath;
        }

        internal string FilePath { get; }

        internal static ProtectedModeSettingsStore CreateDefault()
        {
            return new ProtectedModeSettingsStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NavisHelper", "protected_mode.ini"));
        }

        internal bool ReadEnabled()
        {
            try
            {
                return string.Equals(File.ReadAllText(FilePath).Trim(), "Enabled=true",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        internal bool TryWriteEnabled(bool enabled)
        {
            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(FilePath, (enabled ? "Enabled=true" : "Enabled=false") + Environment.NewLine);
                return true;
            }
            catch { return false; }
        }
    }
}
