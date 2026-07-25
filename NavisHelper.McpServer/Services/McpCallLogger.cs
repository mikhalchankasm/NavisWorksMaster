using System.Text;
using System.Text.Json;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed class McpCallLogger
{
    private const long MaxLogFileBytes = 5 * 1024 * 1024;
    private const int BackupFileCount = 3;
    private const int RetentionDays = 14;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly Mutex CrossProcessMutex = new Mutex(false, "NavisHelperMcpCallLogger");
    private readonly object _syncRoot = new object();
    private readonly string _logDirectory;

    public McpCallLogger()
    {
        var directory = Environment.GetEnvironmentVariable("NAVISHELPER_MCP_LOG_DIR");
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NavisHelper",
                "Mcp",
                "logs");
        }

        Directory.CreateDirectory(directory);
        _logDirectory = directory;
    }

    public string LogFilePath
    {
        get { return GetCurrentLogFilePath(); }
    }

    public McpRecentCallsResponse GetRecentCalls(int lineCount)
    {
        if (lineCount < 1)
            lineCount = 1;
        if (lineCount > 200)
            lineCount = 200;

        var logFilePath = GetCurrentLogFilePath();
        var lines = ReadTailLines(logFilePath, lineCount);
        return new McpRecentCallsResponse
        {
            LogFilePath = logFilePath,
            RequestedLineCount = lineCount,
            ReturnedLineCount = lines.Count,
            Lines = lines,
        };
    }

    public void LogHostCall(
        string command,
        string requestId,
        InstanceDiscoveryRecord record,
        long elapsedMilliseconds,
        string status,
        string errorCode,
        string errorMessage,
        object responseSummary)
    {
        var elapsedHuman = ElapsedTimeFormatter.Format(elapsedMilliseconds);
        var reportElapsedToUser = elapsedMilliseconds >= ElapsedTimeFormatter.ReportThresholdMs;
        Log(new
        {
            event_name = "host_call",
            timestamp_utc = DateTime.UtcNow,
            command = command,
            request_id = requestId,
            status = status,
            elapsed_ms = elapsedMilliseconds,
            elapsed_human = elapsedHuman,
            report_elapsed_to_user = reportElapsedToUser,
            user_elapsed_message = reportElapsedToUser ? ElapsedTimeFormatter.BuildUserMessage(elapsedMilliseconds) : null,
            instance_id = record == null ? null : record.InstanceId,
            pipe_name = record == null ? null : record.PipeName,
            pid = record == null ? (int?)null : record.Pid,
            navisworks_version = record == null ? null : record.NavisworksVersion,
            document_title = record == null ? null : record.DocumentTitle,
            plugin_assembly_path = record == null ? null : record.PluginAssemblyPath,
            plugin_assembly_last_write_utc = record == null ? null : record.PluginAssemblyLastWriteUtc,
            plugin_assembly_length = record == null ? null : record.PluginAssemblyLength,
            error_code = errorCode,
            error_message = errorMessage,
            response_summary = responseSummary,
        });
    }

    public void Log(object entry)
    {
        try
        {
            var line = JsonSerializer.Serialize(entry, JsonOptions);
            lock (_syncRoot)
            {
                var logFilePath = GetCurrentLogFilePath();
                var hasMutex = false;
                try
                {
                    try
                    {
                        hasMutex = CrossProcessMutex.WaitOne(TimeSpan.FromSeconds(2));
                    }
                    catch (AbandonedMutexException)
                    {
                        hasMutex = true;
                    }

                    if (!hasMutex)
                        return;

                    DeleteExpiredLogFiles(_logDirectory);
                    RotateLogFileIfNeeded(logFilePath);

                    using var stream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                    using var writer = new StreamWriter(stream, Encoding.UTF8);
                    writer.WriteLine(line);
                }
                finally
                {
                    if (hasMutex)
                        CrossProcessMutex.ReleaseMutex();
                }
            }
        }
        catch
        {
        }
    }

    private static List<string> ReadTailLines(string filePath, int lineCount)
    {
        var lines = new Queue<string>();
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return new List<string>();

            foreach (var path in EnumerateLogReadPaths(filePath))
            {
                try
                {
                    if (!File.Exists(path))
                        continue;

                    foreach (var line in File.ReadLines(path, Encoding.UTF8))
                    {
                        lines.Enqueue(line);
                        while (lines.Count > lineCount)
                            lines.Dequeue();
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
            return new List<string>();
        }

        return lines.ToList();
    }

    private static IEnumerable<string> EnumerateLogReadPaths(string filePath)
    {
        for (var index = BackupFileCount; index >= 1; index--)
            yield return GetBackupPath(filePath, index);

        yield return filePath;
    }

    private string GetCurrentLogFilePath()
    {
        return Path.Combine(_logDirectory, "mcp-calls-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".jsonl");
    }

    private static void RotateLogFileIfNeeded(string logFilePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(logFilePath) || !File.Exists(logFilePath))
                return;

            var info = new FileInfo(logFilePath);
            if (info.Length < MaxLogFileBytes)
                return;

            var oldestBackupPath = GetBackupPath(logFilePath, BackupFileCount);
            if (File.Exists(oldestBackupPath))
                File.Delete(oldestBackupPath);

            for (var index = BackupFileCount - 1; index >= 1; index--)
            {
                var sourcePath = GetBackupPath(logFilePath, index);
                if (!File.Exists(sourcePath))
                    continue;

                File.Move(sourcePath, GetBackupPath(logFilePath, index + 1));
            }

            File.Move(logFilePath, GetBackupPath(logFilePath, 1));
        }
        catch
        {
        }
    }

    private static string GetBackupPath(string logFilePath, int index)
    {
        return logFilePath + "." + index;
    }

    private static void DeleteExpiredLogFiles(string directory)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return;

            var cutoffUtc = DateTime.UtcNow.Date.AddDays(-RetentionDays);
            foreach (var path in Directory.EnumerateFiles(directory, "mcp-calls-*.jsonl*"))
            {
                try
                {
                    var fileName = Path.GetFileName(path);
                    if (string.IsNullOrWhiteSpace(fileName) || fileName.Length < "mcp-calls-yyyyMMdd".Length)
                        continue;

                    var dateText = fileName.Substring("mcp-calls-".Length, "yyyyMMdd".Length);
                    DateTime date;
                    if (!DateTime.TryParseExact(dateText, "yyyyMMdd", null, System.Globalization.DateTimeStyles.AssumeUniversal, out date))
                        continue;

                    if (date.Date < cutoffUtc)
                        File.Delete(path);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }
}
