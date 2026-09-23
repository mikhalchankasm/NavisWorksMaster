using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;

namespace NavisHelper.McpServer.Services;

internal sealed partial class ScenarioLibraryService
{
    private List<ScenarioStoredRecord> ReadAll(List<string> warnings)
    {
        var result = new List<ScenarioStoredRecord>();
        if (!Directory.Exists(_rootPath))
            return result;

        try
        {
            EnsureSafeRoot(create: false);
            foreach (var path in Directory.EnumerateFiles(_rootPath, "*.json", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (!Guid.TryParse(fileName, out _))
                    continue;
                try
                {
                    result.Add(ReadRecord(path));
                }
                catch (Exception ex)
                {
                    warnings.Add(Path.GetFileName(path) + ": " + SafeMessage(ex));
                }
            }
        }
        catch (Exception ex)
        {
            warnings.Add(SafeMessage(ex));
        }

        return result;
    }

    private ScenarioStoredRecord ReadById(string scenarioId, out string errorCode, out string errorMessage)
    {
        errorCode = string.Empty;
        errorMessage = string.Empty;
        if (!Guid.TryParse(scenarioId, out var id))
        {
            errorCode = "scenario_id_invalid";
            errorMessage = "Некорректный идентификатор сценария.";
            return null;
        }

        var path = GetScenarioPath(id.ToString("D").ToLowerInvariant());
        if (!File.Exists(path))
        {
            errorCode = "scenario_not_found";
            errorMessage = "Сценарий не найден.";
            return null;
        }

        try
        {
            EnsureSafeRoot(create: false);
            return ReadRecord(path);
        }
        catch (Exception ex)
        {
            errorCode = "scenario_invalid";
            errorMessage = "Файл сценария повреждён или небезопасен: " + SafeMessage(ex);
            return null;
        }
    }

    private ScenarioStoredRecord ReadRecord(string path)
    {
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("reparse-point files are forbidden");
        if (info.Length > MaxScenarioFileBytes)
            throw new InvalidDataException("scenario file exceeds 256 KiB");

        var bytes = File.ReadAllBytes(path);
        var scenario = JsonSerializer.Deserialize<ScenarioDocument>(bytes, StoreJsonOptions)
            ?? throw new InvalidDataException("scenario document is empty");
        NormalizeDocumentCollections(scenario);
        var fileId = Path.GetFileNameWithoutExtension(path);
        if (!string.Equals(fileId, scenario.ScenarioId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("filename does not match scenarioId");
        var validation = ValidateDraft(scenario);
        if (!validation.IsValid)
            throw new InvalidDataException(string.Join("; ", validation.Errors.Take(5)));

        return new ScenarioStoredRecord
        {
            Scenario = scenario,
            Sha256 = ComputeSha256(bytes),
            FilePath = path,
            Warnings = validation.Warnings,
        };
    }

    private ScenarioStoredRecord FindExactNameCollision(string name, string excludedId)
    {
        return ReadAll(new List<string>()).FirstOrDefault(record =>
            IsExactReplay(record.Scenario) &&
            !string.Equals(record.Scenario.ScenarioId, excludedId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(record.Scenario.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureSafeRoot(bool create)
    {
        if (create)
            Directory.CreateDirectory(_rootPath);
        if (!Directory.Exists(_rootPath))
            return;

        var info = new DirectoryInfo(_rootPath);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Scenario directory cannot be a reparse point.");
    }

    private static IDisposable AcquireStoreMutex()
    {
        var acquired = false;
        try
        {
            try
            {
                acquired = CrossProcessStoreMutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }

            if (!acquired)
                throw new IOException("Scenario store is busy. Try again.");
            return new StoreMutexLease();
        }
        catch
        {
            if (acquired)
                CrossProcessStoreMutex.ReleaseMutex();
            throw;
        }
    }

    private int CountScenarioFiles()
    {
        return Directory.EnumerateFiles(_rootPath, "*.json", SearchOption.TopDirectoryOnly)
            .Count(path => Guid.TryParse(Path.GetFileNameWithoutExtension(path), out _));
    }

    private string GetScenarioPath(string scenarioId)
    {
        var fileName = Guid.Parse(scenarioId).ToString("D").ToLowerInvariant() + ".json";
        var path = Path.GetFullPath(Path.Combine(_rootPath, fileName));
        var rootWithSeparator = _rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Scenario path escapes the scenario directory.");
        return path;
    }

    private static void AtomicWrite(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(content);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private sealed class StoreMutexLease : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            CrossProcessStoreMutex.ReleaseMutex();
        }
    }
}
