using System.Collections;
using System.Runtime.CompilerServices;
using NavisHelper.Core;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ExceptionObservationTests
{
    private sealed class Events : ExceptionObservation.IEvents
    {
        private UnhandledExceptionEventHandler _unhandled;
        private EventHandler<UnobservedTaskExceptionEventArgs> _task;
        public bool FailTaskSubscription;
        public int UnhandledCount => _unhandled?.GetInvocationList().Length ?? 0;
        public int TaskCount => _task?.GetInvocationList().Length ?? 0;
        public event UnhandledExceptionEventHandler Unhandled
        {
            add => _unhandled += value;
            remove => _unhandled -= value;
        }
        public event EventHandler<UnobservedTaskExceptionEventArgs> UnobservedTask
        {
            add
            {
                if (FailTaskSubscription) throw new InvalidOperationException();
                _task += value;
            }
            remove => _task -= value;
        }
        public void Raise(UnhandledExceptionEventArgs args) => _unhandled?.Invoke(this, args);
        public void Raise(UnobservedTaskExceptionEventArgs args) => _task?.Invoke(this, args);
    }

    private sealed class PayloadException : Exception
    {
        public override string Message => throw new InvalidOperationException("Message was read");
        public override IDictionary Data => throw new InvalidOperationException("Data was read");
        public override string ToString() => throw new InvalidOperationException("ToString was read");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception Captured(Exception error)
    {
        try { throw error; }
        catch (Exception result) { return result; }
    }

    [Fact]
    public void Lifecycle_is_idempotent_and_detaches_both_events()
    {
        var events = new Events();
        var lines = new List<string>();
        using var observer = new ExceptionObservation(events, lines.Add);
        observer.Start();
        observer.Start();
        Assert.Equal(1, events.UnhandledCount);
        Assert.Equal(1, events.TaskCount);
        observer.Dispose();
        observer.Dispose();
        observer.Start();
        events.Raise(new UnhandledExceptionEventArgs(Captured(new Exception()), true));
        observer.ReportRibbonRetry(new Exception());
        Assert.Empty(lines);
        Assert.Equal(0, events.UnhandledCount);
        Assert.Equal(0, events.TaskCount);
    }

    [Fact]
    public void Failed_subscription_rolls_back_and_can_retry()
    {
        var events = new Events { FailTaskSubscription = true };
        using var observer = new ExceptionObservation(events, _ => { });
        observer.Start();
        Assert.Equal(0, events.UnhandledCount);
        Assert.Equal(0, events.TaskCount);
        events.FailTaskSubscription = false;
        observer.Start();
        Assert.Equal(1, events.UnhandledCount);
        Assert.Equal(1, events.TaskCount);
    }

    [Fact]
    public void Nested_owned_failure_reports_metadata_without_accessing_payload_getters()
    {
        var events = new Events();
        var lines = new List<string>();
        using var observer = new ExceptionObservation(events, lines.Add);
        observer.Start();
        var args = new UnobservedTaskExceptionEventArgs(
            new AggregateException("private outer", Captured(new PayloadException())));
        events.Raise(args);
        var line = Assert.Single(lines);
        Assert.Contains("event=UnobservedTaskException", line);
        Assert.Contains(nameof(PayloadException), line);
        Assert.Contains(nameof(Captured), line);
        Assert.Contains("hresult=0x", line);
        Assert.DoesNotContain("private outer", line);
        Assert.False(args.Observed);
    }

    [Fact]
    public void Message_data_source_paths_and_stack_file_names_are_omitted()
    {
        var events = new Events();
        var lines = new List<string>();
        using var observer = new ExceptionObservation(events, lines.Add);
        observer.Start();
        const string secret = "sk-test-secret-never-log";
        var error = new Exception(secret) { Source = @"C:\Private\Customer.nwd" };
        error.Data["apiKey"] = secret;
        var args = new UnhandledExceptionEventArgs(Captured(error), true);
        events.Raise(args);
        var line = Assert.Single(lines);
        Assert.Contains("terminating=True", line);
        Assert.DoesNotContain(secret, line);
        Assert.DoesNotContain("Customer", line);
        Assert.DoesNotContain(".cs:", line);
        Assert.DoesNotContain("\\", line);
        Assert.Same(error, args.ExceptionObject);
        Assert.True(args.IsTerminating);
    }

    [Fact]
    public void Unattributed_and_non_exception_payloads_are_ignored_without_spending_budget()
    {
        var events = new Events();
        var lines = new List<string>();
        using var observer = new ExceptionObservation(events, lines.Add);
        observer.Start();
        for (int index = 0; index < 30; index++)
            events.Raise(new UnhandledExceptionEventArgs(new Exception(), false));
        events.Raise(new UnhandledExceptionEventArgs(new object(), true));
        Assert.Empty(lines);
        events.Raise(new UnhandledExceptionEventArgs(Captured(new Exception()), true));
        Assert.Single(lines);
    }

    [Fact]
    public void Ribbon_retry_records_once_even_without_a_captured_stack()
    {
        var lines = new List<string>();
        using var observer = new ExceptionObservation(new Events(), lines.Add);
        for (int index = 0; index < 100; index++)
            observer.ReportRibbonRetry(new PayloadException());
        Assert.Contains("event=RibbonInitializationRetry", Assert.Single(lines));
    }

    [Fact]
    public void A_thrown_exception_with_only_foreign_frames_is_ignored()
    {
        var events = new Events();
        var lines = new List<string>();
        using var observer = new ExceptionObservation(events, lines.Add);
        observer.Start();
        var wrapped = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            typeof(int).GetMethod(nameof(int.Parse), new[] { typeof(string) })
                .Invoke(null, new object[] { "not-a-number" }));
        var trace = new System.Diagnostics.StackTrace(wrapped.InnerException, false);
        Assert.True(trace.FrameCount > 0);
        Assert.All(trace.GetFrames(), frame => Assert.NotEqual(
            typeof(ExceptionObservation).Assembly, frame.GetMethod()?.DeclaringType?.Assembly));
        events.Raise(new UnhandledExceptionEventArgs(wrapped.InnerException, false));
        Assert.Empty(lines);
    }

    [Fact]
    public void Owned_frame_beyond_the_exception_scan_bound_does_not_claim_attribution()
    {
        var events = new Events();
        var lines = new List<string>();
        using var observer = new ExceptionObservation(events, lines.Add);
        observer.Start();
        var error = Captured(new Exception());
        for (int index = 0; index < 100; index++) error = new Exception("private", error);
        events.Raise(new UnhandledExceptionEventArgs(error, true));
        Assert.Empty(lines);
        observer.ReportRibbonRetry(error);
        Assert.Contains("[bounded]", Assert.Single(lines));
    }

    [Fact]
    public void Sink_failure_does_not_escape_or_change_task_observation()
    {
        var events = new Events();
        int calls = 0;
        using var observer = new ExceptionObservation(events, _ =>
        {
            calls++;
            throw new IOException();
        });
        observer.Start();
        var args = new UnobservedTaskExceptionEventArgs(new AggregateException(Captured(new Exception())));
        events.Raise(args);
        events.Raise(args);
        Assert.Equal(2, calls);
        Assert.False(args.Observed);
    }

    [Fact]
    public void Reentrant_reporting_is_dropped()
    {
        var events = new Events();
        var args = new UnhandledExceptionEventArgs(Captured(new Exception()), false);
        int calls = 0;
        using var observer = new ExceptionObservation(events, _ => { calls++; events.Raise(args); });
        observer.Start();
        events.Raise(args);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Report_budget_does_not_hide_a_later_terminating_failure()
    {
        var events = new Events();
        var lines = new List<string>();
        using var observer = new ExceptionObservation(events, lines.Add);
        observer.Start();
        var error = Captured(new Exception());
        for (int index = 0; index < 100; index++)
            events.Raise(new UnhandledExceptionEventArgs(error, false));
        Assert.Equal(20, lines.Count);
        events.Raise(new UnhandledExceptionEventArgs(error, true));
        Assert.Equal(21, lines.Count);
    }

    [Fact]
    public void Large_aggregate_is_bounded_and_discloses_truncation()
    {
        var lines = new List<string>();
        using var observer = new ExceptionObservation(new Events(), lines.Add);
        var errors = Enumerable.Range(0, 5000).Select(_ => new PayloadException());
        observer.ReportRibbonRetry(new AggregateException(errors));
        var line = Assert.Single(lines);
        Assert.Contains("[bounded]", line);
        Assert.True(line.Length < 4096);
    }

    [Fact]
    public void Diagnostic_drops_a_write_when_another_thread_owns_the_log_mutex()
    {
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        // A private mutex exercises the production write path without blocking a live host.
        using var mutex = new Mutex(false);
        string marker = "contention-" + Guid.NewGuid().ToString("N");
        var holder = new Thread(() =>
        {
            bool owns = false;
            try
            {
                try { owns = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
                catch (AbandonedMutexException) { owns = true; }
                if (owns) held.Set();
                release.Wait();
            }
            finally { if (owns) mutex.ReleaseMutex(); }
        }) { IsBackground = true };
        var writer = new Thread(() => Logger.Diagnostic(marker, "ExceptionObservation", mutex: mutex))
            { IsBackground = true };
        bool completed = false;
        bool writerStarted = false;
        holder.Start();
        try
        {
            Assert.True(held.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            writer.Start();
            writerStarted = true;
            completed = writer.Join(TimeSpan.FromSeconds(1));
        }
        finally
        {
            release.Set();
            holder.Join(TimeSpan.FromSeconds(10));
            if (writerStarted) writer.Join(TimeSpan.FromSeconds(10));
        }
        Assert.True(completed, "Diagnostic waited for the logger mutex instead of dropping the write.");
        if (File.Exists(Logger.GetLogFilePath()))
            Assert.DoesNotContain(marker, File.ReadAllText(Logger.GetLogFilePath()));
    }
}
