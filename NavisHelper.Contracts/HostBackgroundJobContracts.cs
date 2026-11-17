using System;

namespace NavisHelper.Agent.Contracts
{
    public static class HostBackgroundJobStates
    {
        public const string Running = "running";
        public const string Succeeded = "succeeded";
        public const string Failed = "failed";
    }

    public sealed class HostBackgroundJobStatusResponse
    {
        public string OperationId { get; set; }
        public bool Found { get; set; }
        public string Command { get; set; }
        public string State { get; set; }
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public long ElapsedMs { get; set; }
        public object Result { get; set; }
        public string Message { get; set; }
    }
}
