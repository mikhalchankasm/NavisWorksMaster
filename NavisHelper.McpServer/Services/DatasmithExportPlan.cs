using System.Globalization;
using System.Text.Json;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal static class DatasmithExportPlan
{
    internal const string DefaultNavisworksVersion = "2026";
    internal const string RunnerPathEnvironmentVariable = "NAVISHELPER_AUTOMATION_RUNNER";
    internal static IReadOnlyList<string> SupportedVersions { get; } = Array.AsReadOnly(new[] { "2024", "2025", "2026" });

    internal static string ResolveNavisworksDirectory(string version, string programFiles)
    {
        version = string.IsNullOrWhiteSpace(version) ? DefaultNavisworksVersion : version.Trim();
        if (!SupportedVersions.Contains(version, StringComparer.Ordinal))
            throw new ArgumentException("Supported Navisworks versions: 2024, 2025, 2026.", nameof(version));
        var directory = Path.Combine(programFiles, "Autodesk", "Navisworks Manage " + version);
        var assembly = Path.Combine(directory, "Autodesk.Navisworks.Automation.dll");
        if (!File.Exists(assembly))
            throw new FileNotFoundException("Navisworks Manage " + version + " Automation assembly not found: " + assembly, assembly);
        return directory;
    }

    internal static string ResolveRunnerPath(string pathOverride, string serverBaseDirectory) =>
        string.IsNullOrWhiteSpace(pathOverride)
            ? Path.Combine(serverBaseDirectory, "AutomationRunner", "NavisHelper.AutomationRunner.exe")
            : pathOverride.Trim();

    internal static int NormalizeTimeoutSeconds(int seconds)
    {
        if (seconds == 0)
            return 3600;
        if (seconds < 60 || seconds > 14400)
            throw new ArgumentException("timeoutSeconds must be between 60 and 14400, or 0 for the default.", nameof(seconds));
        return seconds;
    }

    internal static IReadOnlyList<string> BuildRunnerArguments(StartDatasmithExportRequest request, string navisworksDirectory)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.MergeLevel < 0)
            throw new ArgumentException("mergeLevel must be nonnegative when supplied.");
        if (request.Origin != null && (request.Origin.Length != 3 || request.Origin.Any(value => !double.IsFinite(value))))
            throw new ArgumentException("origin requires three finite numbers: x,y,z.");
        var arguments = new List<string>
        {
            "datasmith-export", "--navisworks-dir", navisworksDirectory,
            "--source", request.SourcePath, "--output", request.OutputPath,
        };
        if (request.MergeLevel.HasValue)
            arguments.AddRange(new[] { "--merge", request.MergeLevel.Value.ToString(CultureInfo.InvariantCulture) });
        if (request.Origin != null)
            arguments.AddRange(new[] { "--origin", string.Join(",", request.Origin.Select(value => value.ToString("R", CultureInfo.InvariantCulture))) });
        if (request.IncludeMetadata)
            arguments.Add("--include-metadata");
        if (request.Overwrite)
            arguments.Add("--overwrite");
        return arguments;
    }

    internal static DatasmithRunnerOutcome ParseRunnerOutput(string stdout, int exitCode)
    {
        var missing = new DatasmithRunnerOutcome(false, exitCode, null, 0,
            "Automation runner exited without a result line (code " + exitCode.ToString(CultureInfo.InvariantCulture) + ").");
        var line = (stdout ?? string.Empty).Split('\n').LastOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (line == null)
            return missing;
        try
        {
            using var document = JsonDocument.Parse(line);
            var result = document.RootElement;
            var ok = result.GetProperty("ok").GetBoolean();
            var exporter = result.GetProperty("exporterReturnCode");
            int? exporterCode = exporter.ValueKind == JsonValueKind.Null ? null : exporter.GetInt32();
            var size = result.GetProperty("fileSizeBytes").GetInt64();
            var error = result.GetProperty("error").GetString();
            if (ok && exitCode != 0)
                error = "Automation runner exited with code " + exitCode.ToString(CultureInfo.InvariantCulture) + ".";
            return new DatasmithRunnerOutcome(ok && exitCode == 0, exitCode, exporterCode, size, error);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            return missing;
        }
    }
}

internal sealed record DatasmithRunnerOutcome(bool Ok, int ExitCode, int? ExporterReturnCode, long FileSizeBytes, string Error);

internal sealed class RunnerStreamTail
{
    private readonly int _maxChars;
    private string _text = string.Empty;
    private bool _truncated;

    internal RunnerStreamTail(int maxChars)
    {
        if (maxChars <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxChars));
        _maxChars = maxChars;
    }

    internal string LastLine { get; private set; }
    internal string Text => _truncated ? "..." + _text : _text;

    internal void Append(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        if (line.Length > _maxChars)
        {
            line = line.Substring(line.Length - _maxChars);
            _truncated = true;
        }
        LastLine = line;
        // Retain only the portion of the old tail that fits beside this line.
        var keep = _maxChars - line.Length - 1;
        if (_text.Length > 0)
        {
            if (keep < 0)
            {
                _text = line;
                _truncated = true;
                return;
            }
            if (_text.Length > keep)
            {
                _text = _text.Substring(_text.Length - keep);
                _truncated = true;
            }
            _text += "\n" + line;
        }
        else
            _text = line;
    }
}

internal sealed class DatasmithExportJobState
{
    private DatasmithExportJobState(string operationId, string sourcePath, string outputPath, DateTime startedAtUtc,
        string state = DatasmithExportJobStates.Running, DateTime? completedAtUtc = null,
        int? runnerExitCode = null, int? exporterReturnCode = null, long fileSizeBytes = 0, string errorMessage = "")
    {
        OperationId = operationId;
        SourcePath = sourcePath;
        OutputPath = outputPath;
        StartedAtUtc = startedAtUtc;
        State = state;
        CompletedAtUtc = completedAtUtc;
        RunnerExitCode = runnerExitCode;
        ExporterReturnCode = exporterReturnCode;
        FileSizeBytes = fileSizeBytes;
        ErrorMessage = errorMessage ?? string.Empty;
    }

    internal string OperationId { get; }
    internal string SourcePath { get; }
    internal string OutputPath { get; }
    internal DateTime StartedAtUtc { get; }
    internal string State { get; }
    internal DateTime? CompletedAtUtc { get; }
    internal int? RunnerExitCode { get; }
    internal int? ExporterReturnCode { get; }
    internal long FileSizeBytes { get; }
    internal string ErrorMessage { get; }
    internal bool IsRunning => State == DatasmithExportJobStates.Running;

    internal static DatasmithExportJobState StartNew(string operationId, string sourcePath, string outputPath,
        DatasmithExportJobState previous, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(operationId))
            throw new ArgumentException("operationId is required.", nameof(operationId));
        if (previous?.IsRunning == true)
            throw new InvalidOperationException("A Datasmith export job is already running: " + previous.OperationId + ". Poll datasmith_export_status.");
        return new DatasmithExportJobState(operationId.Trim(), sourcePath, outputPath, nowUtc);
    }

    internal DatasmithExportJobState Complete(DatasmithRunnerOutcome outcome, DateTime nowUtc) =>
        Finish(outcome.Ok ? DatasmithExportJobStates.Completed : DatasmithExportJobStates.Failed, nowUtc,
            outcome.ExitCode, outcome.ExporterReturnCode, outcome.FileSizeBytes,
            outcome.Ok ? string.Empty : outcome.Error ?? "Automation runner failed.");

    internal DatasmithExportJobState TimeOut(int timeoutSeconds, DateTime nowUtc) =>
        Fail("Datasmith export timed out after " + timeoutSeconds.ToString(CultureInfo.InvariantCulture) + " s.", nowUtc);

    internal DatasmithExportJobState Fail(string error, DateTime nowUtc) =>
        Finish(DatasmithExportJobStates.Failed, nowUtc, null, null, 0, error);

    private DatasmithExportJobState Finish(string state, DateTime nowUtc, int? exitCode, int? exporterCode, long size, string error)
    {
        if (!IsRunning)
            throw new InvalidOperationException("Only a running Datasmith export job can finish; current state is " + State + ".");
        return new DatasmithExportJobState(OperationId, SourcePath, OutputPath, StartedAtUtc, state, nowUtc, exitCode, exporterCode, size, error);
    }

    internal StartDatasmithExportResponse BuildStartResponse(DateTime nowUtc) => new()
    {
        OperationId = OperationId, State = State, IsRunning = IsRunning,
        SourcePath = SourcePath, OutputPath = OutputPath, ElapsedMs = ElapsedMs(nowUtc),
        Message = "Datasmith export started. Poll datasmith_export_status with operationId " + OperationId + " until it reports completed or failed.",
    };

    internal DatasmithExportStatusResponse BuildStatusResponse(DateTime nowUtc) => new()
    {
        OperationId = OperationId, State = State, IsRunning = IsRunning,
        SourcePath = SourcePath, OutputPath = OutputPath, FileSizeBytes = FileSizeBytes,
        RunnerExitCode = RunnerExitCode, ExporterReturnCode = ExporterReturnCode,
        ElapsedMs = ElapsedMs(nowUtc), ErrorMessage = ErrorMessage,
        StartedAtUtc = StartedAtUtc, CompletedAtUtc = CompletedAtUtc,
        Message = IsRunning ? "Datasmith export is still running."
            : State == DatasmithExportJobStates.Completed ? "Datasmith export completed: " + OutputPath
            : "Datasmith export failed: " + ErrorMessage,
    };

    private long ElapsedMs(DateTime nowUtc) => Math.Max(0, (long)((CompletedAtUtc ?? nowUtc) - StartedAtUtc).TotalMilliseconds);
}
