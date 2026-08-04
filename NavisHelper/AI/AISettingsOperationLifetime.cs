using System;
using System.Threading;

namespace NavisHelper.AI
{
    internal sealed class AISettingsOperationLease
    {
        internal AISettingsOperationLease(
            int operationGeneration,
            int keyGeneration,
            CancellationToken cancellationToken)
        {
            OperationGeneration = operationGeneration;
            KeyGeneration = keyGeneration;
            CancellationToken = cancellationToken;
        }

        internal int OperationGeneration { get; }
        internal int KeyGeneration { get; }
        internal CancellationToken CancellationToken { get; }
    }

    internal sealed class AISettingsOperationLifetime : IDisposable
    {
        private readonly object _sync = new object();
        private CancellationTokenSource _cancellation;
        private int _generation;
        private bool _isDisposed;

        internal AISettingsOperationLease Begin(int keyGeneration)
        {
            CancellationTokenSource previous;
            AISettingsOperationLease lease;
            lock (_sync)
            {
                if (_isDisposed)
                    return null;
                previous = _cancellation;
                _cancellation = new CancellationTokenSource();
                lease = new AISettingsOperationLease(
                    ++_generation,
                    keyGeneration,
                    _cancellation.Token);
            }
            CancelAndDispose(previous);
            return lease;
        }

        internal bool IsCurrent(AISettingsOperationLease lease)
        {
            if (lease == null || lease.CancellationToken.IsCancellationRequested)
                return false;
            lock (_sync)
            {
                return !_isDisposed &&
                       lease.OperationGeneration == _generation &&
                       !lease.CancellationToken.IsCancellationRequested;
            }
        }

        internal bool TryExecuteCurrent<T>(
            AISettingsOperationLease lease,
            Func<T> action,
            out T result)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            lock (_sync)
            {
                if (_isDisposed ||
                    lease == null ||
                    lease.OperationGeneration != _generation ||
                    lease.CancellationToken.IsCancellationRequested)
                {
                    result = default(T);
                    return false;
                }
                result = action();
                return true;
            }
        }

        internal void CancelPendingOperations()
        {
            CancellationTokenSource cancellation;
            lock (_sync)
            {
                if (_isDisposed)
                    return;
                ++_generation;
                cancellation = _cancellation;
                _cancellation = null;
            }
            CancelAndDispose(cancellation);
        }

        public void Dispose()
        {
            CancellationTokenSource cancellation;
            lock (_sync)
            {
                if (_isDisposed)
                    return;
                _isDisposed = true;
                ++_generation;
                cancellation = _cancellation;
                _cancellation = null;
            }
            CancelAndDispose(cancellation);
        }

        private static void CancelAndDispose(
            CancellationTokenSource cancellation)
        {
            if (cancellation == null)
                return;
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            cancellation.Dispose();
        }
    }
}
