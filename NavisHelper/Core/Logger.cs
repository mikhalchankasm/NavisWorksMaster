using System;
using System.IO;
using System.Text;
using System.Threading;

namespace NavisHelper.Core
{
    public static class Logger
    {
        private const long MaxLogFileBytes = 5 * 1024 * 1024;
        private const int BackupFileCount = 3;
        private static readonly Mutex LogMutex = new Mutex(false, @"Local\NavisHelper.Core.Logger");

        public static void Info(string message, string context = null, string modelPath = null)
        {
            WriteLog("INFO", message, context, modelPath);
        }

        public static void Warn(string message, string context = null, string modelPath = null)
        {
            WriteLog("WARN", message, context, modelPath);
        }

        public static void Error(string message, string context = null, string modelPath = null)
        {
            WriteLog("ERROR", message, context, modelPath);
        }

        private static void WriteLog(string level, string message, string context, string modelPath)
        {
            bool hasMutex = false;

            try
            {
                try
                {
                    hasMutex = LogMutex.WaitOne(TimeSpan.FromSeconds(2));
                }
                catch (AbandonedMutexException)
                {
                    hasMutex = true;
                }

                if (!hasMutex)
                    return;

                string logFileName = GetLogFileName(modelPath);
                string directory = Path.GetDirectoryName(logFileName);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                RotateLogFileIfNeeded(logFileName);

                string prefix = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}]";
                if (!string.IsNullOrEmpty(context))
                    prefix += $" [{context}]";

                using (var stream = new FileStream(logFileName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                using (var writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    writer.WriteLine($"{prefix} {message}");
                }
            }
            catch
            {
                // Logging must never break Navisworks plugin commands or MCP requests.
            }
            finally
            {
                if (hasMutex)
                {
                    try
                    {
                        LogMutex.ReleaseMutex();
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static string GetLogFilePath(string modelPath = null)
        {
            if (string.IsNullOrEmpty(modelPath))
                return Path.Combine(Path.GetTempPath(), "navishelper_log.txt");

            string directory = Path.GetDirectoryName(modelPath);
            if (string.IsNullOrEmpty(directory))
                directory = Path.GetTempPath();

            string fileName = Path.GetFileNameWithoutExtension(modelPath);
            if (string.IsNullOrEmpty(fileName))
                fileName = "navishelper";

            return Path.Combine(directory, fileName + "_navishelper_log.txt");
        }

        private static string GetLogFileName(string modelPath)
        {
            return GetLogFilePath(modelPath);
        }

        private static void RotateLogFileIfNeeded(string logFileName)
        {
            try
            {
                if (string.IsNullOrEmpty(logFileName) || !File.Exists(logFileName))
                    return;

                var info = new FileInfo(logFileName);
                if (info.Length < MaxLogFileBytes)
                    return;

                var oldestBackupPath = GetBackupPath(logFileName, BackupFileCount);
                if (File.Exists(oldestBackupPath))
                    File.Delete(oldestBackupPath);

                for (var index = BackupFileCount - 1; index >= 1; index--)
                {
                    var sourcePath = GetBackupPath(logFileName, index);
                    if (!File.Exists(sourcePath))
                        continue;

                    File.Move(sourcePath, GetBackupPath(logFileName, index + 1));
                }

                File.Move(logFileName, GetBackupPath(logFileName, 1));
            }
            catch
            {
            }
        }

        private static string GetBackupPath(string logFileName, int index)
        {
            return logFileName + "." + index;
        }
    }
}
