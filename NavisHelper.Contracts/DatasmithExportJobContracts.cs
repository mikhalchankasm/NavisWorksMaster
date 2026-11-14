using System;

namespace NavisHelper.Agent.Contracts
{
    public static class DatasmithExportJobStates
    {
        public const string Running = "running";
        public const string Completed = "completed";
        public const string Failed = "failed";
    }

    public sealed class StartDatasmithExportRequest
    {
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public string NavisworksVersion { get; set; }
        public int? MergeLevel { get; set; }
        public double[] Origin { get; set; }
        public bool IncludeMetadata { get; set; }
        public bool Overwrite { get; set; }
        public int TimeoutSeconds { get; set; }
    }

    public sealed class StartDatasmithExportResponse
    {
        public string OperationId { get; set; }
        public string State { get; set; }
        public bool IsRunning { get; set; }
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public long ElapsedMs { get; set; }
        public string Message { get; set; }
    }

    public sealed class DatasmithExportStatusResponse
    {
        public string OperationId { get; set; }
        public string State { get; set; }
        public bool IsRunning { get; set; }
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public long FileSizeBytes { get; set; }
        public int? RunnerExitCode { get; set; }
        public int? ExporterReturnCode { get; set; }
        public long ElapsedMs { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string Message { get; set; }
    }
}
