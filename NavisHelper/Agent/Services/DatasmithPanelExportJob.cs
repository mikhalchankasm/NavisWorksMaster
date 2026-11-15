using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using NavisHelper.Agent.Contracts;
using Newtonsoft.Json.Linq;

namespace NavisHelper.Agent.Services
{
    internal sealed class DatasmithPanelExportJob : IDisposable
    {
        private readonly object _sync = new object();
        private KillOnCloseJob _job;
        private string _lastOutput;
        private string _lastError;
        private bool _started;
        private bool _finished;
        private bool _disposed;

        public event EventHandler Completed;
        public bool Canceled { get; private set; }
        public bool Ok { get; private set; }
        public int? ExporterReturnCode { get; private set; }
        public long FileSizeBytes { get; private set; }
        public string Error { get; private set; }

        // Same override rule as the server's DatasmithExportPlan.ResolveRunnerPath: a blank variable is unset.
        internal static string RunnerPath
        {
            get
            {
                var overridePath = Environment.GetEnvironmentVariable("NAVISHELPER_AUTOMATION_RUNNER");
                return string.IsNullOrWhiteSpace(overridePath)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "NavisHelper", "McpServer", "AutomationRunner", "NavisHelper.AutomationRunner.exe")
                    : overridePath.Trim();
            }
        }

        internal static string NavisworksDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Autodesk", "Navisworks Manage 2026");

        internal static string MissingDependency()
        {
            if (!File.Exists(RunnerPath)) return RunnerPath;
            var automation = Path.Combine(NavisworksDirectory, "Autodesk.Navisworks.Automation.dll");
            return File.Exists(automation) ? null : automation;
        }

        public void Start(string sourcePath, string outputPath)
        {
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(DatasmithPanelExportJob));
                if (_started) throw new InvalidOperationException(nameof(Start));
                _started = true;
                Process process = null;
                try
                {
                    _job = new KillOnCloseJob(@"Local\NavisHelper-Datasmith-");
                    var convertDirectory = DatasmithRunnerCommandLine.FindConvertNavisworksDirectory(
                        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
                    var args = DatasmithRunnerCommandLine.BuildArguments(NavisworksDirectory,
                        sourcePath, outputPath, null, null, false, true, _job.Name,
                        convertDirectory, convertDirectory == null ? (int?)null : 2026);
                    process = new Process
                    {
                        StartInfo = new ProcessStartInfo(RunnerPath, DatasmithRunnerCommandLine.Join(args))
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true
                        }
                    };
                    process.OutputDataReceived += OnOutput;
                    process.ErrorDataReceived += OnError;
                    process.Start();
                    _job.Assign(process);
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    var startedProcess = process;
                    Task.Run(() => WaitForCompletion(startedProcess));
                }
                catch
                {
                    _finished = true;
                    _job?.Dispose();
                    _job = null;
                    process?.Dispose();
                    throw;
                }
            }
        }

        private void OnOutput(object sender, DataReceivedEventArgs args)
        {
            if (args.Data == null) return;
            lock (_sync) _lastOutput = args.Data;
        }

        private void OnError(object sender, DataReceivedEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.Data)) return;
            lock (_sync) _lastError = args.Data;
        }

        private void WaitForCompletion(Process process)
        {
            try
            {
                // The parameterless wait also drains both asynchronous output readers.
                process.WaitForExit();
                lock (_sync)
                {
                    if (!Canceled)
                    {
                        if (string.IsNullOrWhiteSpace(_lastOutput))
                            Error = _lastError ?? "ExitCode=" + process.ExitCode.ToString(CultureInfo.InvariantCulture);
                        else
                        {
                            var result = JObject.Parse(_lastOutput);
                            ExporterReturnCode = result.Value<int?>("exporterReturnCode");
                            FileSizeBytes = result.Value<long?>("fileSizeBytes") ?? 0;
                            Ok = result.Value<bool?>("ok") == true && process.ExitCode == 0;
                            Error = result.Value<string>("error");
                            if (!Ok && string.IsNullOrWhiteSpace(Error))
                                Error = _lastError ?? "ExitCode=" + process.ExitCode.ToString(CultureInfo.InvariantCulture)
                                    + "; exporterReturnCode=" + ExporterReturnCode;
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                lock (_sync)
                {
                    Ok = false;
                    Error = exception.Message;
                }
            }
            finally
            {
                lock (_sync)
                {
                    _finished = true;
                    _job?.Dispose();
                    _job = null;
                }
                process.OutputDataReceived -= OnOutput;
                process.ErrorDataReceived -= OnError;
                process.Dispose();
            }
            Completed?.Invoke(this, EventArgs.Empty);
        }

        public void Cancel()
        {
            lock (_sync)
            {
                if (_finished) return;
                Canceled = true;
                _job?.Dispose();
                _job = null;
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (_disposed) return;
                _disposed = true;
                Cancel();
            }
        }
    }
}
