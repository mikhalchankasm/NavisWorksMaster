using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NavisHelper.Core
{
    internal sealed class CooperativeSubscriptionDetachResult
    {
        internal CooperativeSubscriptionDetachResult(
            int attempted,
            IReadOnlyList<Exception> failures)
        {
            Attempted = attempted;
            Failures = failures ?? throw new ArgumentNullException(nameof(failures));
        }

        internal int Attempted { get; }
        internal IReadOnlyList<Exception> Failures { get; }
        internal bool Succeeded => Failures.Count == 0;
    }

    internal sealed class CooperativeSubscriptionSet
    {
        private sealed class Entry
        {
            internal Entry(Action unsubscribe)
            {
                Unsubscribe = unsubscribe;
            }

            internal Action Unsubscribe { get; }
            internal bool Active { get; set; } = true;
        }

        private readonly List<Entry> _entries = new List<Entry>();

        internal bool HasActive
        {
            get
            {
                foreach (var entry in _entries)
                {
                    if (entry.Active)
                        return true;
                }
                return false;
            }
        }

        internal int Count => _entries.Count;

        internal int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (var entry in _entries)
                {
                    if (entry.Active)
                        count++;
                }
                return count;
            }
        }

        internal void Add(Action unsubscribe)
        {
            _entries.Add(new Entry(
                unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe))));
        }

        internal CooperativeSubscriptionDetachResult DetachAll()
        {
            int attempted = 0;
            var failures = new List<Exception>();
            foreach (var entry in _entries)
            {
                if (!entry.Active)
                    continue;

                attempted++;
                try
                {
                    entry.Unsubscribe();
                    entry.Active = false;
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            return new CooperativeSubscriptionDetachResult(attempted, failures);
        }
    }

    internal sealed class CooperativeDepthFirstTraversal<T> : IDisposable
    {
        private readonly Func<T, IEnumerable<T>> _children;
        private readonly ISet<T> _visited;
        private readonly Stack<IEnumerator<T>> _enumerators =
            new Stack<IEnumerator<T>>();
        private T _root;
        private bool _rootPending;
        private bool _disposed;

        internal CooperativeDepthFirstTraversal(
            T root,
            Func<T, IEnumerable<T>> children,
            ISet<T> visited = null)
        {
            _root = root;
            _rootPending = true;
            _children = children ?? throw new ArgumentNullException(nameof(children));
            _visited = visited ?? new HashSet<T>();
        }

        internal bool TryTakeNext(out T item)
        {
            ThrowIfDisposed();
            while (true)
            {
                if (_rootPending)
                {
                    _rootPending = false;
                    if (TryAccept(_root, out item))
                        return true;
                }

                if (_enumerators.Count == 0)
                {
                    item = default(T);
                    return false;
                }

                var current = _enumerators.Peek();
                if (!current.MoveNext())
                {
                    DisposeEnumerator(_enumerators.Pop());
                    continue;
                }

                if (TryAccept(current.Current, out item))
                    return true;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            while (_enumerators.Count > 0)
                DisposeEnumerator(_enumerators.Pop());
            _root = default(T);
        }

        private bool TryAccept(T candidate, out T item)
        {
            if (ReferenceEquals(candidate, null) || !_visited.Add(candidate))
            {
                item = default(T);
                return false;
            }

            item = candidate;
            var children = _children(candidate);
            if (children != null)
                _enumerators.Push(children.GetEnumerator());
            return true;
        }

        private static void DisposeEnumerator(IEnumerator<T> enumerator)
        {
            (enumerator as IDisposable)?.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CooperativeDepthFirstTraversal<T>));
        }
    }

    internal sealed class CooperativeProgressOwnership : IDisposable
    {
        private Action _endProgress;

        internal CooperativeProgressOwnership(Action endProgress)
        {
            _endProgress = endProgress ?? throw new ArgumentNullException(nameof(endProgress));
        }

        public void Dispose()
        {
            var endProgress = Interlocked.Exchange(ref _endProgress, null);
            endProgress?.Invoke();
        }
    }

    internal static class CooperativeModelIsolationPolicy
    {
        internal static bool ShouldHide<T>(
            T item,
            ISet<T> selected,
            ISet<T> selectedAncestors,
            Func<T, T> parent)
        {
            if (ReferenceEquals(item, null) ||
                selected == null ||
                selectedAncestors == null ||
                parent == null)
            {
                throw new ArgumentNullException();
            }

            if (selected.Contains(item) || selectedAncestors.Contains(item))
                return false;

            var current = parent(item);
            while (!ReferenceEquals(current, null))
            {
                if (selected.Contains(current))
                    return false;
                current = parent(current);
            }
            return true;
        }
    }

    internal enum TaskAwareCommandOutcome
    {
        Completed,
        NotCompleted
    }

    internal static class TaskAwarePaletteCommandRunner
    {
        internal static async Task<TaskAwareCommandOutcome> ExecuteAsync(
            Func<Task<TaskAwareCommandOutcome>> execute,
            Action onCompleted)
        {
            if (execute == null)
                throw new ArgumentNullException(nameof(execute));
            if (onCompleted == null)
                throw new ArgumentNullException(nameof(onCompleted));

            var outcome = await execute();
            if (outcome == TaskAwareCommandOutcome.Completed)
                onCompleted();
            return outcome;
        }
    }
}
