using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ApplicationParts;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using NavisHelper.Agent.Session;
using NavisHelper.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace NavisHelper.Agent.Host
{
    internal sealed partial class AgentHostService : IDisposable
    {

        private void RefreshDiscoveryFile(Document document)
        {
            var documentTitle = document == null || string.IsNullOrWhiteSpace(document.FileName)
                ? string.Empty
                : Path.GetFileName(document.FileName);

            if (string.Equals(documentTitle, _lastDiscoveryDocumentTitle, StringComparison.Ordinal))
                return;

            WriteDiscoveryFile(documentTitle);
        }

        private string GetUiDispatcherLabel()
        {
            if (_uiContext != null)
                return "synchronization_context";

            return GetAttachedControl() == null ? "none" : "control";
        }

        private static string GetDocumentTitleForLog(Document document)
        {
            try
            {
                if (document == null || string.IsNullOrWhiteSpace(document.FileName))
                    return string.Empty;

                return Path.GetFileName(document.FileName);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static int GetSelectedItemCountForLog(Document document)
        {
            try
            {
                return document == null || document.CurrentSelection == null || document.CurrentSelection.SelectedItems == null
                    ? 0
                    : document.CurrentSelection.SelectedItems.Count;
            }
            catch
            {
                return -1;
            }
        }

        private static string BuildPayloadSummaryForLog(JToken payloadToken)
        {
            var payload = payloadToken as JObject;
            if (payload == null)
                return "{}";

            var values = new List<string>();
            foreach (var property in payload.Properties())
            {
                if (values.Count >= 24)
                    break;
                var name = property.Name ?? string.Empty;
                var normalizedName = name.Replace("_", string.Empty).Replace("-", string.Empty);
                if (normalizedName.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalizedName.IndexOf("secret", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalizedName.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalizedName.IndexOf("apikey", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalizedName.IndexOf("credential", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    normalizedName.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                var value = property.Value as JValue;
                if (value == null)
                    continue;
                var text = Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty;
                text = text.Replace("\r", " ").Replace("\n", " ");
                if (text.Length > 160)
                    text = text.Substring(0, 160) + "...";
                values.Add(name + "=" + text);
            }
            return "{" + string.Join(",", values) + "}";
        }

        private static string GetLoadedAssemblyInfo()
        {
            try
            {
                var assemblyInfo = GetPluginAssemblyFileInfo();
                var lastWriteUtc = assemblyInfo.LastWriteUtc.HasValue
                    ? assemblyInfo.LastWriteUtc.Value.ToString("o", CultureInfo.InvariantCulture)
                    : string.Empty;
                var length = assemblyInfo.Length.HasValue
                    ? assemblyInfo.Length.Value.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                return "assembly_path=\"" + assemblyInfo.Path + "\" assembly_last_write_utc=" +
                       lastWriteUtc + " assembly_length=" + length;
            }
            catch (Exception ex)
            {
                return "assembly_info_error=\"" + ex.Message + "\"";
            }
        }

        private static PluginAssemblyFileInfo GetPluginAssemblyFileInfo()
        {
            var result = new PluginAssemblyFileInfo
            {
                Path = typeof(AgentHostService).Assembly.Location ?? string.Empty,
                Version = typeof(AgentHostService).Assembly.GetName().Version == null
                    ? string.Empty
                    : typeof(AgentHostService).Assembly.GetName().Version.ToString(),
            };

            try
            {
                if (!string.IsNullOrWhiteSpace(result.Path) && File.Exists(result.Path))
                {
                    var fileInfo = new FileInfo(result.Path);
                    result.LastWriteUtc = fileInfo.LastWriteTimeUtc;
                    result.Length = fileInfo.Length;
                }
            }
            catch
            {
            }

            return result;
        }

        private string GetDocumentTitleSafe()
        {
            try
            {
                var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (document == null || string.IsNullOrWhiteSpace(document.FileName))
                    return string.Empty;

                return Path.GetFileName(document.FileName);
            }
            catch
            {
                return string.Empty;
            }
        }

        private void CleanupOwnStaleDiscoveryFiles(int pid)
        {
            var directory = GetInstancesDirectory();
            if (!Directory.Exists(directory))
                return;

            foreach (var filePath in Directory.GetFiles(directory, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(filePath, Encoding.UTF8);
                    var record = JsonConvert.DeserializeObject<InstanceDiscoveryRecord>(json, JsonSettings);
                    if (record != null && record.Pid == pid)
                    {
                        File.Delete(filePath);
                    }
                }
                catch
                {
                }
            }
        }

        private void WriteDiscoveryFile(string documentTitle)
        {
            var directory = GetInstancesDirectory();
            Directory.CreateDirectory(directory);
            var pluginAssembly = GetPluginAssemblyFileInfo();

            var record = new InstanceDiscoveryRecord
            {
                ProtocolVersion = ProtocolConstants.CurrentProtocolVersion,
                InstanceId = _instanceId,
                PipeName = _pipeName,
                Pid = Process.GetCurrentProcess().Id,
                NavisworksVersion = DetectNavisworksVersion(),
                DocumentTitle = documentTitle ?? string.Empty,
                StartedAtUtc = _startedAtUtc == default(DateTime) ? DateTime.UtcNow : _startedAtUtc,
                ProcessStartedAtUtc = _processStartedAtUtc,
                PluginVersion = pluginAssembly.Version,
                PluginAssemblyPath = pluginAssembly.Path,
                PluginAssemblyLastWriteUtc = pluginAssembly.LastWriteUtc,
                PluginAssemblyLength = pluginAssembly.Length,
                HostLogFilePath = Logger.GetLogFilePath(),
            };

            var tempPath = _discoveryFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tempPath, JsonConvert.SerializeObject(record, JsonSettings), Encoding.UTF8);
                MoveDiscoveryFileIntoPlace(tempPath, _discoveryFilePath);
                _lastDiscoveryDocumentTitle = documentTitle ?? string.Empty;
            }
            catch (Exception ex)
            {
                TryDelete(tempPath);
                Logger.Error("Failed to refresh discovery file: " + ex.Message, "AgentHost");
            }
        }

        private static void MoveDiscoveryFileIntoPlace(string tempPath, string finalPath)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (File.Exists(finalPath))
                        File.Replace(tempPath, finalPath, null);
                    else
                        File.Move(tempPath, finalPath);
                    return;
                }
                catch (IOException) when (attempt < 2)
                {
                    Thread.Sleep(25);
                }
                catch (UnauthorizedAccessException) when (attempt < 2)
                {
                    Thread.Sleep(25);
                }
                catch (FileNotFoundException) when (attempt < 2)
                {
                    Thread.Sleep(25);
                }
            }

            if (File.Exists(finalPath))
                File.Replace(tempPath, finalPath, null);
            else
                File.Move(tempPath, finalPath);
        }

        private void DeleteDiscoveryFile()
        {
            if (string.IsNullOrWhiteSpace(_discoveryFilePath))
                return;

            try
            {
                if (File.Exists(_discoveryFilePath))
                    File.Delete(_discoveryFilePath);
            }
            catch
            {
            }
        }

        private static string GetInstancesDirectory()
        {
            var configuredDirectory = Environment.GetEnvironmentVariable("NAVISHELPER_INSTANCES_DIR");
            if (!string.IsNullOrWhiteSpace(configuredDirectory))
            {
                Directory.CreateDirectory(configuredDirectory);
                return configuredDirectory;
            }

            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NavisHelper",
                "Mcp",
                "instances");
            Directory.CreateDirectory(path);
            return path;
        }

        private static string DetectNavisworksVersion()
        {
            try
            {
                var executablePath = Process.GetCurrentProcess().MainModule.FileName;
                var match = Regex.Match(executablePath, @"Navisworks (?:Manage|Simulate) (\d{4})", RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value;
            }
            catch
            {
            }

            return "unknown";
        }

        private static DateTime? GetCurrentProcessStartTimeUtc()
        {
            try
            {
                return Process.GetCurrentProcess().StartTime.ToUniversalTime();
            }
            catch
            {
                return null;
            }
        }
    }
}
