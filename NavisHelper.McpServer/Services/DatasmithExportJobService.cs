using System.Diagnostics;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed class DatasmithExportJobService
{
    private const int MaxDiagnosticsChars = 2000;
    private readonly object _gate = new();
    private readonly Dictionary<string, DatasmithExportJobState> _jobs = new(StringComparer.Ordinal);
    private readonly Queue<string> _history = new();
    private DatasmithExportJobState _current;
    private string _cleanupBlockedOperationId;

    public StartDatasmithExportResponse Start(StartDatasmithExportRequest request)
    {
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
            try
            {
                if (!process.Start())
                    throw new InvalidOperationException("Automation runner did not start.");
            }
            catch (Exception error)
            {
                process.Dispose();
                Update(job.Fail("Automation runner could not start: " + error.Message, DateTime.UtcNow));
                throw;
            }
            var response = job.BuildStartResponse(DateTime.UtcNow);
            _ = Task.Run(() => MonitorAsync(process, job, timeout));
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

    private async Task MonitorAsync(Process process, DatasmithExportJobState job, int timeoutSeconds)
    {
        using (process)
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
        {
            try
            {
                // Drain both pipes concurrently while waiting; dependencies may write diagnostics.
                var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
                var stderr = process.StandardError.ReadToEndAsync(deadline.Token);
                await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(deadline.Token)).ConfigureAwait(false);
                var outcome = DatasmithExportPlan.ParseRunnerOutput(await stdout.ConfigureAwait(false), process.ExitCode);
                var diagnostics = (await stderr.ConfigureAwait(false)).Trim();
                // Keep the status reply bounded: only the tail of stderr names the failure.
                if (diagnostics.Length > MaxDiagnosticsChars)
                    diagnostics = "..." + diagnostics.Substring(diagnostics.Length - MaxDiagnosticsChars);
                if (!outcome.Ok && diagnostics.Length > 0)
                    outcome = outcome with { Error = outcome.Error + " " + diagnostics };
                lock (_gate)
                    Update(job.Complete(outcome, DateTime.UtcNow));
            }
            catch (Exception error)
            {
                var failed = deadline.IsCancellationRequested
                    ? job.TimeOut(timeoutSeconds, DateTime.UtcNow)
                    : job.Fail("Automation runner failed: " + error.Message, DateTime.UtcNow);
                try
                {
                    // This handle belongs only to the runner launched for this job.
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (process.HasExited) { }
                catch (Exception cleanupError)
                {
                    failed = job.Fail(failed.ErrorMessage + " Runner cleanup failed: " + cleanupError.Message, DateTime.UtcNow);
                    lock (_gate)
                        _cleanupBlockedOperationId = job.OperationId;
                }
                lock (_gate)
                    Update(failed);
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
