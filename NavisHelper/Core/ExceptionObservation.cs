using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NavisHelper.Core
{
    // Passive, best-effort diagnostics: never changes exception handling policy.
    internal sealed class ExceptionObservation : IDisposable
    {
        internal interface IEvents
        {
            event UnhandledExceptionEventHandler Unhandled;
            event EventHandler<UnobservedTaskExceptionEventArgs> UnobservedTask;
        }

        private sealed class RuntimeEvents : IEvents
        {
            public event UnhandledExceptionEventHandler Unhandled
            {
                add { AppDomain.CurrentDomain.UnhandledException += value; }
                remove { AppDomain.CurrentDomain.UnhandledException -= value; }
            }
            public event EventHandler<UnobservedTaskExceptionEventArgs> UnobservedTask
            {
                add { TaskScheduler.UnobservedTaskException += value; }
                remove { TaskScheduler.UnobservedTaskException -= value; }
            }
        }

        private readonly object _lifecycle = new object();
        private readonly IEvents _events;
        private readonly Action<string> _write;
        private readonly Action<string> _writeRetry;
        private readonly Assembly _owner;
        private bool _started;
        private int _disposed;
        private int _reporting;
        private int _reports;
        private int _ribbonReported;

        internal ExceptionObservation(IEvents events = null, Action<string> write = null)
        {
            _events = events ?? new RuntimeEvents();
            _write = write ?? (line => Logger.Diagnostic(line, "ExceptionObservation"));
            _writeRetry = write ?? (line => Logger.Diagnostic(line, "ExceptionObservation", transient: true));
            _owner = typeof(ExceptionObservation).Assembly;
        }

        internal void Start()
        {
            lock (_lifecycle)
            {
                if (_started || _disposed != 0)
                    return;
                try
                {
                    _events.Unhandled += OnUnhandled;
                    _events.UnobservedTask += OnUnobservedTask;
                    _started = true;
                }
                catch
                {
                    // Observation must not prevent plugin startup, even if event access fails.
                    Detach();
                }
            }
        }

        public void Dispose()
        {
            lock (_lifecycle)
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;
                Detach();
                _started = false;
            }
        }

        private void Detach()
        {
            try { _events.Unhandled -= OnUnhandled; } catch { }
            try { _events.UnobservedTask -= OnUnobservedTask; } catch { }
        }

        internal void ReportRibbonRetry(Exception error)
        {
            if (Interlocked.Exchange(ref _ribbonReported, 1) == 0)
                Report(error, "RibbonInitializationRetry", false, false);
        }

        private void OnUnhandled(object sender, UnhandledExceptionEventArgs args)
        {
            Report(args.ExceptionObject as Exception, "UnhandledException", args.IsTerminating, true);
        }

        private void OnUnobservedTask(object sender, UnobservedTaskExceptionEventArgs args)
        {
            Report(args.Exception, "UnobservedTaskException", false, true);
            // Do not call SetObserved: runtime/host policy remains authoritative.
        }

        private void Report(Exception error, string operation, bool terminating, bool requireOwner)
        {
            if (error == null || Volatile.Read(ref _disposed) != 0 ||
                Interlocked.CompareExchange(ref _reporting, 1, 0) != 0)
                return;
            try
            {
                if (_reports >= 20 && !terminating)
                    return;
                bool hasOwnedFrame;
                string detail = Describe(error, out hasOwnedFrame);
                if (requireOwner && !hasOwnedFrame)
                    return;
                _reports++;
                var sink = requireOwner ? _write : _writeRetry;
                sink("event=" + operation + "; terminating=" + terminating + "; " + detail);
            }
            catch
            {
                // Formatting and the sink must not replace the original failure.
            }
            finally
            {
                Volatile.Write(ref _reporting, 0);
            }
        }

        private string Describe(Exception error, out bool hasOwnedFrame)
        {
            hasOwnedFrame = false;
            var pending = new Queue<Exception>();
            var visited = new List<Exception>();
            var text = new StringBuilder();
            bool bounded = false;
            pending.Enqueue(error);
            while (pending.Count > 0 && visited.Count < 16 && text.Length < 3500)
            {
                var current = pending.Dequeue();
                if (visited.Exists(item => ReferenceEquals(item, current)))
                    continue;
                visited.Add(current);
                text.Append("type=").Append(Identifier(current.GetType().FullName));
                text.Append("; hresult=0x").Append(current.HResult.ToString("X8"));
                var trace = new StackTrace(current, false);
                bounded |= trace.FrameCount > 128;
                int recorded = 0;
                for (int index = 0; index < Math.Min(trace.FrameCount, 128); index++)
                {
                    var method = trace.GetFrame(index)?.GetMethod();
                    var type = method?.DeclaringType;
                    if (type == null || type.Assembly != _owner)
                        continue;
                    hasOwnedFrame = true;
                    text.Append("; at=").Append(Identifier(type.FullName));
                    text.Append('.').Append(Identifier(method.Name));
                    if (++recorded == 8 || text.Length >= 3500)
                    {
                        bounded |= index + 1 < trace.FrameCount;
                        break;
                    }
                }
                text.Append(" | ");
                var aggregate = current as AggregateException;
                if (aggregate != null)
                {
                    foreach (var inner in aggregate.InnerExceptions)
                    {
                        if (pending.Count + visited.Count >= 16)
                        {
                            bounded = true;
                            break;
                        }
                        pending.Enqueue(inner);
                    }
                }
                else if (current.InnerException != null)
                {
                    if (pending.Count + visited.Count < 16)
                        pending.Enqueue(current.InnerException);
                    else
                        bounded = true;
                }
            }
            // Deliberately never reads Message, Data, Source, ToString or source file paths.
            if (bounded || pending.Count > 0 || text.Length >= 3500)
                text.Append("[bounded]");
            return text.ToString();
        }

        private static string Identifier(string value)
        {
            var result = new StringBuilder();
            foreach (char character in value ?? string.Empty)
            {
                if (result.Length == 160)
                    break;
                result.Append(char.IsLetterOrDigit(character) || "._+`<>".IndexOf(character) >= 0
                    ? character : '_');
            }
            return result.ToString();
        }
    }
}
