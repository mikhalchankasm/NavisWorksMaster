using System.Collections.Concurrent;
using System.Linq;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed class McpTaskTimerService
{
    private static readonly TimeSpan TimerTtl = TimeSpan.FromHours(24);
    private const int MaxTimerCount = 256;

    private readonly ConcurrentDictionary<string, TimerRecord> _timers = new ConcurrentDictionary<string, TimerRecord>();

    public McpTaskTimerStartResponse Start(string taskName)
    {
        var startedAtUtc = DateTime.UtcNow;
        CleanupExpired(startedAtUtc);

        var record = new TimerRecord
        {
            TimerId = "timer-" + Guid.NewGuid().ToString("N"),
            TaskName = (taskName ?? string.Empty).Trim(),
            StartedAtUtc = startedAtUtc,
        };

        _timers[record.TimerId] = record;
        TrimOverflow();

        return new McpTaskTimerStartResponse
        {
            TimerId = record.TimerId,
            TaskName = record.TaskName,
            StartedAtUtc = record.StartedAtUtc,
            Message = "Timer started.",
        };
    }

    public McpTaskTimerFinishResponse Finish(string timerId, string taskName)
    {
        if (string.IsNullOrWhiteSpace(timerId))
            throw new InvalidOperationException("timerId is required.");

        var normalizedTimerId = timerId.Trim();
        var completedAtUtc = DateTime.UtcNow;
        CleanupExpired(completedAtUtc);

        if (!_timers.TryRemove(normalizedTimerId, out var record))
            throw new InvalidOperationException("Timer was not found or has expired: " + timerId);

        var elapsedMs = (long)(completedAtUtc - record.StartedAtUtc).TotalMilliseconds;
        var effectiveTaskName = string.IsNullOrWhiteSpace(taskName) ? record.TaskName : taskName.Trim();
        var elapsedHuman = ElapsedTimeFormatter.Format(elapsedMs);
        var shouldReport = elapsedMs >= ElapsedTimeFormatter.ReportThresholdMs;

        return new McpTaskTimerFinishResponse
        {
            TimerId = record.TimerId,
            TaskName = effectiveTaskName,
            StartedAtUtc = record.StartedAtUtc,
            CompletedAtUtc = completedAtUtc,
            ElapsedMs = elapsedMs,
            ElapsedHuman = elapsedHuman,
            ShouldReportToUser = shouldReport,
            UserMessage = shouldReport
                ? ElapsedTimeFormatter.BuildUserMessage(elapsedMs)
                : string.Empty,
        };
    }

    private void CleanupExpired(DateTime utcNow)
    {
        foreach (var pair in _timers)
        {
            if (utcNow - pair.Value.StartedAtUtc >= TimerTtl)
                _timers.TryRemove(pair.Key, out _);
        }
    }

    private void TrimOverflow()
    {
        var overflowCount = _timers.Count - MaxTimerCount;
        if (overflowCount <= 0)
            return;

        foreach (var pair in _timers.OrderBy(pair => pair.Value.StartedAtUtc).Take(overflowCount))
            _timers.TryRemove(pair.Key, out _);
    }

    private sealed class TimerRecord
    {
        public string TimerId { get; set; }
        public string TaskName { get; set; }
        public DateTime StartedAtUtc { get; set; }
    }
}
