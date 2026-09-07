using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace NavisHelper.Core
{
    /// <summary>
    /// Reads a source only while its operation is current. Both expensive
    /// reads and visits count toward a slice; a yield never resumes source
    /// access before checking cancellation and document lifetime again.
    /// </summary>
    internal static class CooperativeModelWorkLoop
    {
        internal static async Task RunAsync<T>(
            IEnumerable<T> source,
            Action<T> visit,
            Action checkCanContinue,
            Func<Task> yield,
            CooperativeModelScanChunkPolicy policy,
            Action<int, TimeSpan> onChunk = null,
            Func<TimeSpan> clock = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (visit == null) throw new ArgumentNullException(nameof(visit));
            if (checkCanContinue == null) throw new ArgumentNullException(nameof(checkCanContinue));
            if (yield == null) throw new ArgumentNullException(nameof(yield));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            var timer = Stopwatch.StartNew();
            clock = clock ?? (() => timer.Elapsed);
            int processed = 0;
            var started = clock();
            checkCanContinue();
            using (var iterator = source.GetEnumerator())
            {
                try
                {
                    while (true)
                    {
                        checkCanContinue();
                        if (!iterator.MoveNext())
                            break;
                        visit(iterator.Current);
                        processed++;
                        var elapsed = clock() - started;
                        if (!policy.ShouldYield(processed, elapsed))
                            continue;
                        onChunk?.Invoke(processed, elapsed);
                        processed = 0;
                        await yield();
                        checkCanContinue();
                        started = clock();
                    }
                }
                finally
                {
                    if (processed > 0)
                        onChunk?.Invoke(processed, clock() - started);
                }
            }
        }
    }

    internal sealed class CooperativeModelOperationStoppedException : Exception
    {
        internal CooperativeModelOperationStoppedException(CooperativeModelScanStatus status)
        {
            Status = status;
        }

        internal CooperativeModelScanStatus Status { get; }
    }
}
