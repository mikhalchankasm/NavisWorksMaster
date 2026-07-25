using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class HostStatusRequest
    {
    }

    public sealed class HostStatusResponse
    {
        public string ProtocolVersion { get; set; }
        public string InstanceId { get; set; }
        public int Pid { get; set; }
        public string NavisworksVersion { get; set; }
        public bool HasActiveDocument { get; set; }
        public string DocumentTitle { get; set; }
        public string DocumentFileName { get; set; }
        public int ModelCount { get; set; }
        public int RootItemCount { get; set; }
        public double WorkingSetMb { get; set; }
        public string PluginVersion { get; set; }
        public string PluginAssemblyPath { get; set; }
        public DateTime? PluginAssemblyLastWriteUtc { get; set; }
        public long? PluginAssemblyLength { get; set; }
        public string HostLogFilePath { get; set; }
    }

    public sealed class LastOperationStatusRequest
    {
        public string RequestId { get; set; }
    }

    public sealed class LastOperationStatusResponse
    {
        public string RequestId { get; set; }
        public bool Found { get; set; }
        public string Command { get; set; }
        public string State { get; set; }
        public bool? Ok { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public bool ResponseTruncated { get; set; }
        public string ResponseType { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public long ElapsedMs { get; set; }
        public string Message { get; set; }
    }

    public sealed class SelectionStatusRequest
    {
        public bool? IncludeBoundingBox { get; set; }
    }

    public sealed class SelectionStatusResponse
    {
        public bool HasSelection { get; set; }
        public int SelectedItemCount { get; set; }
        public BoundingBoxInfo BoundingBox { get; set; }
    }

    public sealed class SelectionCopyNamesRequest
    {
        public int? Limit { get; set; }
        public bool? IncludePaths { get; set; }
        public bool? IncludeSourceFiles { get; set; }
    }

    public sealed class SelectionCopyNamesResponse
    {
        public int SelectedItemCount { get; set; }
        public int ReturnedItemCount { get; set; }
        public bool Truncated { get; set; }
        public List<string> Names { get; set; } = new List<string>();
        public List<SelectionCopyNameItem> Items { get; set; } = new List<SelectionCopyNameItem>();
    }

    public sealed class SelectionCopyNameItem
    {
        public string DisplayName { get; set; }
        public string Path { get; set; }
        public string SourceFile { get; set; }
    }

    public sealed class DumpSubtreeNamesRequest
    {
        public string RootName { get; set; }
        public string SourceFile { get; set; }
        public string OutputPath { get; set; }
        public string Format { get; set; }
        public bool? IncludePath { get; set; }
        public bool? IncludeSourceFile { get; set; }
        public bool? IncludeHidden { get; set; }
        public bool? Overwrite { get; set; }
    }

    public sealed class DumpSubtreeNamesResponse
    {
        public string OutputPath { get; set; }
        public string Format { get; set; }
        public string RootName { get; set; }
        public string RootPath { get; set; }
        public string RootSourceFile { get; set; }
        public int ItemCount { get; set; }
        public int SkippedHiddenItemCount { get; set; }
        public long FileSizeBytes { get; set; }
    }

    public sealed class DumpSubtreeNamesStatusRequest
    {
        public string JobId { get; set; }
        public int? MaxItemsPerPoll { get; set; }
        public int? MaxElapsedMs { get; set; }
    }

    public sealed class CancelSubtreeNamesDumpRequest
    {
        public string JobId { get; set; }
    }

    public sealed class DumpSubtreeNamesJobStatusResponse
    {
        public string InstanceId { get; set; }
        public string JobId { get; set; }
        public string State { get; set; }
        public string OutputPath { get; set; }
        public string PartialOutputPath { get; set; }
        public string Format { get; set; }
        public string RootName { get; set; }
        public string RootPath { get; set; }
        public string RootSourceFile { get; set; }
        public int ItemCount { get; set; }
        public int SkippedHiddenItemCount { get; set; }
        public int ProcessedItemCount { get; set; }
        public int PendingItemCount { get; set; }
        public long FileSizeBytes { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public long ElapsedMs { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsDone { get; set; }
    }
}
