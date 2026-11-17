using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed class HostBackgroundJobStore : IDisposable
{
    public const int Capacity = 32;
    private readonly object _gate = new();
    private readonly Dictionary<string, Job> _jobs = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    private sealed class Job
    {
        public HostBackgroundJobStatusResponse Status { get; init; }
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public HostBackgroundJobStatusResponse Start(string command, Func<CancellationToken, Task<object>> run)
    {
        ArgumentNullException.ThrowIfNull(run);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var job = new Job
            {
                Status = new HostBackgroundJobStatusResponse
                {
                    OperationId = "job-" + Guid.NewGuid().ToString("N"),
                    Found = true, Command = command, State = HostBackgroundJobStates.Running,
                    StartedAtUtc = DateTime.UtcNow,
                },
            };
            _jobs.Add(job.Status.OperationId, job);
            _order.Enqueue(job.Status.OperationId);
            if (_jobs.Count > Capacity)
                _jobs.Remove(_order.Dequeue());
            var started = Snapshot(job.Status);
            var token = _lifetime.Token;
            _ = Task.Run(() => RunAsync(job, run, token));
            return started;
        }
    }

    private async Task RunAsync(Job job, Func<CancellationToken, Task<object>> run, CancellationToken token)
    {
        object result = null;
        string errorCode = null, errorMessage = null;
        try
        {
            token.ThrowIfCancellationRequested();
            result = await run(token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            errorCode = exception is HostCallException host ? host.ErrorCode : ErrorCodes.CommandFailed;
            errorMessage = exception.Message;
        }
        lock (_gate)
        {
            job.Status.State = errorCode == null ? HostBackgroundJobStates.Succeeded : HostBackgroundJobStates.Failed;
            job.Status.Result = result;
            job.Status.ErrorCode = errorCode;
            job.Status.ErrorMessage = errorMessage;
            job.Status.CompletedAtUtc = DateTime.UtcNow;
            job.Completion.SetResult();
        }
    }

    public HostBackgroundJobStatusResponse Get(string operationId)
    {
        lock (_gate)
            return operationId != null && _jobs.TryGetValue(operationId, out var job)
                ? Snapshot(job.Status)
                : new HostBackgroundJobStatusResponse
                {
                    OperationId = operationId, Found = false,
                    Message = "Unknown background job; it may have been evicted or belong to an earlier server session.",
                };
    }

    public async Task<HostBackgroundJobStatusResponse> WaitAsync(string operationId, TimeSpan timeout)
    {
        Task completion;
        lock (_gate)
        {
            if (operationId == null || !_jobs.TryGetValue(operationId, out var job) || timeout <= TimeSpan.Zero)
                return Get(operationId);
            completion = job.Completion.Task;
        }
        try { await completion.WaitAsync(timeout).ConfigureAwait(false); }
        catch (TimeoutException) { }
        return Get(operationId);
    }

    private static HostBackgroundJobStatusResponse Snapshot(HostBackgroundJobStatusResponse status) => new()
    {
        OperationId = status.OperationId, Found = status.Found, Command = status.Command,
        State = status.State, ErrorCode = status.ErrorCode, ErrorMessage = status.ErrorMessage,
        StartedAtUtc = status.StartedAtUtc, CompletedAtUtc = status.CompletedAtUtc,
        ElapsedMs = Math.Max(0, (long)((status.CompletedAtUtc ?? DateTime.UtcNow) - status.StartedAtUtc.Value).TotalMilliseconds),
        Result = status.Result, Message = status.Message,
    };

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
