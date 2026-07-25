using System;

namespace NavisHelper.Agent.Contracts
{
    public static class SubtreeDumpJobPolicy
    {
        public const int DefaultMaxItemsPerPoll = 1000;
        public const int MaximumMaxItemsPerPoll = 10000;
        public const int DefaultMaxElapsedMs = 500;
        public const int MaximumMaxElapsedMs = 3000;

        public static TimeSpan CompletedJobRetention => TimeSpan.FromHours(2);
        public static TimeSpan MaximumRunningJobAge => TimeSpan.FromMinutes(30);

        public static int NormalizeMaxItemsPerPoll(int? value)
        {
            var requested = value.GetValueOrDefault(DefaultMaxItemsPerPoll);
            if (requested < 1)
                return DefaultMaxItemsPerPoll;

            return Math.Min(requested, MaximumMaxItemsPerPoll);
        }

        public static int NormalizeMaxElapsedMs(int? value)
        {
            var requested = value.GetValueOrDefault(DefaultMaxElapsedMs);
            if (requested < 1)
                return DefaultMaxElapsedMs;

            return Math.Min(requested, MaximumMaxElapsedMs);
        }

        public static bool IsRunning(string state)
        {
            return string.Equals(state, DumpSubtreeNamesJobStates.Running, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCompletedJobExpired(DateTime? completedAtUtc, DateTime nowUtc)
        {
            return completedAtUtc.HasValue && completedAtUtc.Value < nowUtc.Subtract(CompletedJobRetention);
        }

        public static bool IsRunningJobExpired(string state, DateTime updatedAtUtc, DateTime nowUtc)
        {
            return IsRunning(state) && updatedAtUtc < nowUtc.Subtract(MaximumRunningJobAge);
        }

        public static DumpSubtreeNamesJobStatusResponse BuildStatus(SubtreeDumpJobStatusValues values, DateTime nowUtc)
        {
            if (values == null)
                throw new ArgumentNullException(nameof(values));

            var completed = values.CompletedAtUtc;
            return new DumpSubtreeNamesJobStatusResponse
            {
                JobId = values.JobId,
                State = values.State,
                OutputPath = values.OutputPath,
                PartialOutputPath = values.PartialOutputPath,
                Format = values.Format,
                RootName = values.RootName,
                RootPath = values.RootPath,
                RootSourceFile = values.RootSourceFile,
                ItemCount = values.ItemCount,
                SkippedHiddenItemCount = values.SkippedHiddenItemCount,
                ProcessedItemCount = values.ProcessedItemCount,
                PendingItemCount = values.PendingItemCount,
                FileSizeBytes = values.FileSizeBytes,
                StartedAtUtc = values.StartedAtUtc,
                UpdatedAtUtc = values.UpdatedAtUtc,
                CompletedAtUtc = completed,
                ElapsedMs = (long)((completed ?? nowUtc) - values.StartedAtUtc).TotalMilliseconds,
                ErrorMessage = values.ErrorMessage ?? string.Empty,
                IsDone = !IsRunning(values.State),
            };
        }
    }

    public sealed class SubtreeDumpJobStatusValues
    {
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
        public string ErrorMessage { get; set; }
    }
}
