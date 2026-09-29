using System;

namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// The remaining time budget of the current host request, so a handler that
    /// can stop early and return a partial result knows when to stop instead of
    /// overrunning the client's timeout. The budget is set by the host around
    /// the routed handler call on the UI thread; <see cref="Current"/> is
    /// thread-static because that callback may run on either UI dispatcher.
    /// </summary>
    public sealed class HostRequestDeadline
    {
        private readonly long _budgetMs;
        private readonly Func<long> _elapsedMs;

        [ThreadStatic]
        private static HostRequestDeadline _current;

        public static HostRequestDeadline Current
        {
            get { return _current; }
        }

        private HostRequestDeadline(long budgetMs, Func<long> elapsedMs)
        {
            _budgetMs = budgetMs;
            _elapsedMs = elapsedMs ?? (delegate { return 0L; });
        }

        public long Remaining
        {
            get { return Math.Max(0, _budgetMs - _elapsedMs()); }
        }

        public bool Expired
        {
            get { return Remaining <= 0; }
        }

        public static IDisposable Begin(long budgetMs, Func<long> elapsedMs)
        {
            var previous = _current;
            _current = new HostRequestDeadline(budgetMs, elapsedMs);
            return new Scope(previous);
        }

        private sealed class Scope : IDisposable
        {
            private HostRequestDeadline _previous;

            public Scope(HostRequestDeadline previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                _current = _previous;
                _previous = null;
            }
        }
    }
}
