using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>States of an FBX export job.</summary>
    public static class FbxLevelExportJobStates
    {
        public const string Running = "running";
        public const string Completed = "completed";
        public const string Failed = "failed";
        public const string Canceled = "canceled";
    }

    /// <summary>Starts exporting Selection Tree items at the requested level.</summary>
    public sealed class StartFbxLevelExportRequest
    {
        public int Level { get; set; }
        public string Folder { get; set; }
        public bool Overwrite { get; set; }
    }

    /// <summary>Identifies the export job to poll.</summary>
    public sealed class FbxLevelExportStatusRequest
    {
        public string OperationId { get; set; }
    }

    /// <summary>Identifies the export job to cancel.</summary>
    public sealed class CancelFbxLevelExportRequest
    {
        public string OperationId { get; set; }
    }

    /// <summary>An exported file and its measurements.</summary>
    public sealed class FbxLevelExportFile
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public long Bytes { get; set; }
        public long ElapsedMs { get; set; }
    }

    /// <summary>Progress and results of an FBX export job.</summary>
    public sealed class FbxLevelExportStatusResponse
    {
        public string OperationId { get; set; }
        /// <summary>The host that owns the job; poll with it as <c>instanceId</c>.</summary>
        public string InstanceId { get; set; }
        public string State { get; set; }
        public bool IsRunning { get; set; }
        public int Level { get; set; }
        public string Folder { get; set; }
        public int TotalItems { get; set; }
        public int ExportedItems { get; set; }
        public int SkippedItems { get; set; }
        public string CurrentItem { get; set; }
        public List<FbxLevelExportFile> Files { get; set; }
        public string ErrorMessage { get; set; }
        public long ElapsedMs { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string Message { get; set; }
    }
}
