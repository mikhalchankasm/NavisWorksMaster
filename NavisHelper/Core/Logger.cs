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

        /// <summary>
        /// Environment variable that moves the default log file.
        ///
        /// This file is compiled into NavisHelper.McpServer.Tests, so a test run
        /// writes to the same `%TEMP%\navishelper_log.txt` that a live Navisworks
        /// host is writing to. Two sessions reading that log to diagnose a live
        /// problem found each other's xUnit stack traces interleaved with rig
        /// traffic, and had to work out whose worktree each trace came from before
        /// they could use the file. Diagnosis is what this log is for, so polluting
        /// it defeats its purpose.
        /// </summary>
        public const string LogFileOverrideVariable = "NAVISHELPER_LOG_FILE";

        /// <summary>
        /// The default log path, given the override's value. Separated from the
        /// environment read so it can be tested without setting a process-wide
        /// variable that parallel tests would race on.
        /// </summary>
        public static string ResolveDefaultLogFilePath(string overrideValue)
        {
            if (!string.IsNullOrWhiteSpace(overrideValue))
                return overrideValue.Trim();

            return Path.Combine(Path.GetTempPath(), "navishelper_log.txt");
        }

        public static string GetLogFilePath(string modelPath = null)
        {
            if (string.IsNullOrEmpty(modelPath))
            {
                string overrideValue = null;
                try
                {
                    overrideValue = Environment.GetEnvironmentVariable(LogFileOverrideVariable);
                }
                catch
                {
                    // Reading the environment must never break a log write.
                }

                return ResolveDefaultLogFilePath(overrideValue);
            }

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
