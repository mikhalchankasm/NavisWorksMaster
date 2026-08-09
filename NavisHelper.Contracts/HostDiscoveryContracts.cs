using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class InstanceDiscoveryRecord
    {
        public string ProtocolVersion { get; set; }
        public string InstanceId { get; set; }
        public string PipeName { get; set; }
        public int Pid { get; set; }
        public string NavisworksVersion { get; set; }
        public string DocumentTitle { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? ProcessStartedAtUtc { get; set; }
        public string PluginVersion { get; set; }
        public string PluginAssemblyPath { get; set; }
        public DateTime? PluginAssemblyLastWriteUtc { get; set; }
        public long? PluginAssemblyLength { get; set; }
        public string HostLogFilePath { get; set; }
    }

    public sealed class ListNavisworksHostsResponse
    {
        public List<NavisworksHostInfo> Hosts { get; set; } = new List<NavisworksHostInfo>();
    }

    public sealed class NavisworksHostInfo
    {
        public string ProtocolVersion { get; set; }
        public string InstanceId { get; set; }
        public string PipeName { get; set; }
        public int Pid { get; set; }
        public string NavisworksVersion { get; set; }
        public string DocumentTitle { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? ProcessStartedAtUtc { get; set; }
        public string PluginVersion { get; set; }
        public string PluginAssemblyPath { get; set; }
        public DateTime? PluginAssemblyLastWriteUtc { get; set; }
        public long? PluginAssemblyLength { get; set; }
        public string HostLogFilePath { get; set; }
    }

    public sealed class McpDiagnosticsResponse
    {
        public string McpServerVersion { get; set; }
        public string ProtocolVersion { get; set; }
        public string LogFilePath { get; set; }
        public string InstancesDirectory { get; set; }
        public List<NavisworksHostInfo> Hosts { get; set; } = new List<NavisworksHostInfo>();
    }

    public sealed class McpRecentCallsResponse
    {
        public string LogFilePath { get; set; }
        public int RequestedLineCount { get; set; }
        public int ReturnedLineCount { get; set; }
        public List<string> Lines { get; set; } = new List<string>();
    }

    public sealed class McpErrorContractResponse
    {
        public List<McpErrorContractItem> Errors { get; set; } = new List<McpErrorContractItem>();
    }

    public sealed class McpErrorContractItem
    {
        public string ErrorCode { get; set; }
        public string Meaning { get; set; }
        public string RecommendedAction { get; set; }
        public bool Retryable { get; set; }
    }

    public sealed class NavisworksRecentFilesResponse
    {
        public string RequestedNavisworksVersion { get; set; }
        public int ReturnedFileCount { get; set; }
        public List<NavisworksRecentFileInfo> Files { get; set; } = new List<NavisworksRecentFileInfo>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class NavisworksRecentFileInfo
    {
        public string NavisworksVersion { get; set; }
        public string RegistryVersion { get; set; }
        public int Slot { get; set; }
        public string DisplayName { get; set; }
        public string Path { get; set; }
        public DateTime? LastOpenedUtc { get; set; }
        public DateTime? LastOpenedLocal { get; set; }
        public bool Exists { get; set; }
    }

    public sealed class StartNavisworksResponse
    {
        public bool Started { get; set; }
        public bool ProcessCreated { get; set; }
        public bool ProcessExited { get; set; }
        public int? ExitCode { get; set; }
        public int? ProcessId { get; set; }
        public string NavisworksVersion { get; set; }
        public string RoamerPath { get; set; }
        public string FilePath { get; set; }
        public bool OpenedRecentFile { get; set; }
        public NavisworksRecentFileInfo RecentFile { get; set; }
        public bool WaitedForHost { get; set; }
        public bool HostReady { get; set; }
        public NavisworksHostInfo Host { get; set; }
        public string Outcome { get; set; }
        public string FailureReason { get; set; }
        public long StartupElapsedMs { get; set; }
        public long ElapsedMs { get; set; }
        public string ElapsedHuman { get; set; }
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public static class StartNavisworksOutcomes
    {
        public const string ProcessCreated = "process_created";
        public const string HostReady = "host_ready";
        public const string ProcessExited = "process_exited";
        public const string HostTimeout = "host_timeout";
    }

    public sealed class McpTaskTimerStartResponse
    {
        public string TimerId { get; set; }
        public string TaskName { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public string Message { get; set; }
    }

    public sealed class McpTaskTimerFinishResponse
    {
        public string TimerId { get; set; }
        public string TaskName { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime CompletedAtUtc { get; set; }
        public long ElapsedMs { get; set; }
        public string ElapsedHuman { get; set; }
        public bool ShouldReportToUser { get; set; }
        public string UserMessage { get; set; }
    }

    public sealed class ActiveModelContextResponse
    {
        public HostStatusResponse HostStatus { get; set; }
        public ListRootItemsResponse RootItems { get; set; }
        public int? SavedViewpointTotalItemCount { get; set; }
        public int? SavedViewpointReturnedItemCount { get; set; }
        public int? SelectionSetTotalItemCount { get; set; }
        public int? SelectionSetReturnedItemCount { get; set; }
        public List<string> SearchGuidance { get; set; } = new List<string>();
        public List<string> RecommendedWorkflow { get; set; } = new List<string>();
    }

    public sealed class McpHealthCheckResponse
    {
        public bool Ok { get; set; }
        public string Verdict { get; set; }
        public string ProtocolVersion { get; set; }
        public string InstanceId { get; set; }
        public int? Pid { get; set; }
        public string NavisworksVersion { get; set; }
        public string DocumentTitle { get; set; }
        public string McpServerVersion { get; set; }
        public string PluginVersion { get; set; }
        public string PluginAssemblyPath { get; set; }
        public DateTime? PluginAssemblyLastWriteUtc { get; set; }
        public long? PluginAssemblyLength { get; set; }
        public string HostLogFilePath { get; set; }
        public double? WorkingSetMb { get; set; }
        public int? RootItemCount { get; set; }
        public string LogFilePath { get; set; }
        public List<McpHealthCheckStep> Checks { get; set; } = new List<McpHealthCheckStep>();
        public List<string> RecommendedActions { get; set; } = new List<string>();
    }

    public sealed class McpHealthCheckStep
    {
        public string Name { get; set; }
        public bool Ok { get; set; }
        public long ElapsedMs { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
    }
}
