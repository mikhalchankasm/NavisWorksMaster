using System.Diagnostics;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed class DatasmithExportJobService : IDisposable
{
    private const int MaxDiagnosticsChars = 2000;
    private readonly object _gate = new();
    private readonly Dictionary<string, DatasmithExportJobState> _jobs = new(StringComparer.Ordinal);
    private readonly Queue<string> _history = new();
    private readonly Dictionary<string, RunnerJobObject> _runnerJobs = new(StringComparer.Ordinal);
    private DatasmithExportJobState _current;
    private string _cleanupBlockedOperationId;
    private bool _disposed;

    public StartDatasmithExportResponse Start(StartDatasmithExportRequest request)
    {
        lock (_gate)
            ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        var timeout = DatasmithExportPlan.NormalizeTimeoutSeconds(request.TimeoutSeconds);
        var directory = DatasmithExportPlan.ResolveNavisworksDirectory(request.NavisworksVersion,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        var runner = DatasmithExportPlan.ResolveRunnerPath(
            Environment.GetEnvironmentVariable(DatasmithExportPlan.RunnerPathEnvironmentVariable), AppContext.BaseDirectory);
        if (!File.Exists(runner))
            throw new FileNotFoundException("Automation runner not found: " + runner, runner);
        if (string.IsNullOrWhiteSpace(request.SourcePath) || !File.Exists(request.SourcePath))
            throw new FileNotFoundException("Saved source model not found: " + request.SourcePath, request.SourcePath);
        if (!Path.IsPathFullyQualified(request.SourcePath) || !Path.IsPathFullyQualified(request.OutputPath ?? string.Empty))
            throw new ArgumentException("sourcePath and outputPath must be absolute paths.");
        var startInfo = new ProcessStartInfo(runner)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in DatasmithExportPlan.BuildRunnerArguments(request, directory))
            startInfo.ArgumentList.Add(argument);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cleanupBlockedOperationId != null)
                throw new InvalidOperationException("Runner cleanup failed for job " + _cleanupBlockedOperationId + "; no further export can start in this server.");
            var job = DatasmithExportJobState.StartNew("datasmith-" + Guid.NewGuid().ToString("N"),
                request.SourcePath, request.OutputPath, _current, DateTime.UtcNow);
            _current = job;
            _jobs.Add(job.OperationId, job);
            _history.Enqueue(job.OperationId);
            while (_history.Count > 16)
                _jobs.Remove(_history.Dequeue());

            var process = new Process { StartInfo = startInfo };
            RunnerJobObject runnerJob = null;
            var started = false;
            try
            {
                runnerJob = new RunnerJobObject();
                if (!process.Start())
                    throw new InvalidOperationException("Automation runner did not start.");
                started = true;
                runnerJob.Assign(process);
            }
            catch (Exception error)
            {
                var message = "Automation runner could not start: " + error.Message;
                try
                {
                    if (started)
                        EndRunner(process);
                }
                catch (Exception cleanupError)
                {
                    message += " Runner cleanup failed: " + cleanupError.Message;
                    _cleanupBlockedOperationId = job.OperationId;
                }
                finally
                {
                    runnerJob?.Dispose();
                    process.Dispose();
                }
                Update(job.Fail(message, DateTime.UtcNow));
                throw;
            }
            _runnerJobs.Add(job.OperationId, runnerJob);
            var response = job.BuildStartResponse(DateTime.UtcNow);
            _ = Task.Run(() => MonitorAsync(process, runnerJob, job, timeout));
            return response;
        }
    }

    public DatasmithExportStatusResponse Status(string operationId)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(operationId) || !_jobs.TryGetValue(operationId.Trim(), out var job))
                throw new ArgumentException("Unknown Datasmith export operationId: " + operationId, nameof(operationId));
            return job.BuildStatusResponse(DateTime.UtcNow);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (var runnerJob in _runnerJobs.Values)
                runnerJob.Dispose();
            foreach (var job in _jobs.Values.Where(value => value.IsRunning).ToArray())
                Update(job.Fail("The MCP server stopped during the export.", DateTime.UtcNow));
        }
    }

    private static async Task ReadTailAsync(StreamReader reader, RunnerStreamTail tail, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            tail.Append(line);
    }

    private static void EndRunner(Process process)
    {
        try
        {
            // This handle belongs only to the runner launched for this job.
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) when (process.HasExited) { }
    }

    private async Task MonitorAsync(Process process, RunnerJobObject runnerJob, DatasmithExportJobState job, int timeoutSeconds)
    {
        using (process)
        using (runnerJob)
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            Task stdout = Task.CompletedTask;
            Task stderr = Task.CompletedTask;
            try
            {
                // Drain both pipes concurrently while waiting; dependencies may write diagnostics.
                var stdoutTail = new RunnerStreamTail(64 * 1024);
                var stderrTail = new RunnerStreamTail(MaxDiagnosticsChars);
                stdout = ReadTailAsync(process.StandardOutput, stdoutTail, deadline.Token);
                stderr = ReadTailAsync(process.StandardError, stderrTail, deadline.Token);
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                // Descendants may still hold the pipes after the runner has exited.
                runnerJob.Dispose();
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
                var outcome = DatasmithExportPlan.ParseRunnerOutput(stdoutTail.LastLine ?? "", process.ExitCode);
                var diagnostics = stderrTail.Text;
                if (!outcome.Ok && diagnostics.Length > 0)
                    outcome = outcome with { Error = outcome.Error + " " + diagnostics };
                lock (_gate)
                    if (!_disposed)
                        Update(job.Complete(outcome, DateTime.UtcNow));
            }
            catch (Exception error)
            {
                var failed = deadline.IsCancellationRequested
                    ? job.TimeOut(timeoutSeconds, DateTime.UtcNow)
                    : job.Fail("Automation runner failed: " + error.Message, DateTime.UtcNow);
                try
                {
                    runnerJob.Dispose();
                    EndRunner(process);
                }
                catch (Exception cleanupError)
                {
                    failed = job.Fail(failed.ErrorMessage + " Runner cleanup failed: " + cleanupError.Message, DateTime.UtcNow);
                    lock (_gate)
                        _cleanupBlockedOperationId = job.OperationId;
                }
                lock (_gate)
                    if (!_disposed)
                        Update(failed);
            }
            finally
            {
                // Cancel and observe readers even if waiting for the runner failed.
                deadline.Cancel();
                try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
                catch (Exception) { /* The job already carries the monitor failure. */ }
                lock (_gate)
                    _runnerJobs.Remove(job.OperationId);
            }
        }
    }

    // The caller holds _gate; status always reads an immutable snapshot.
    private void Update(DatasmithExportJobState state)
    {
        _jobs[state.OperationId] = state;
        _current = state;
    }
}
