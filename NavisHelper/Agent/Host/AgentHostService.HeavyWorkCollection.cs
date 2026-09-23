using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Host
{
    internal sealed partial class AgentHostService
    {
        private readonly HeavyWorkCollectionPolicy _heavyWorkCollectionPolicy =
            new HeavyWorkCollectionPolicy(GC.CollectionCount(0));
        private int _heavyWorkCollectionInFlight;
        private Task _heavyWorkCollectionTask;

        private void ScheduleHeavyWorkCollection()
        {
            var gen0Now = GC.CollectionCount(0);
            if (!_heavyWorkCollectionPolicy.ShouldCollect(gen0Now))
                return;

            if (Interlocked.CompareExchange(ref _heavyWorkCollectionInFlight, 1, 0) != 0)
                return;

            var gen0CollectionsSinceLast = _heavyWorkCollectionPolicy.CollectionsSinceLast(gen0Now);
            _heavyWorkCollectionTask = Task.Run(() =>
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();

                    _heavyWorkCollectionPolicy.RecordCollection(GC.CollectionCount(0));
                    Logger.Info(
                        "heavy_work_collection gen0_collections_since_last=" + gen0CollectionsSinceLast +
                        " elapsed_ms=" + stopwatch.ElapsedMilliseconds,
                        "AgentHost");
                }
                finally
                {
                    Interlocked.Exchange(ref _heavyWorkCollectionInFlight, 0);
                }
            });
        }

        private void WaitForHeavyWorkCollection(string requestId, string command)
        {
            var collectionTask = _heavyWorkCollectionTask;
            if (collectionTask == null || collectionTask.IsCompleted)
                return;

            var stopwatch = Stopwatch.StartNew();
            var completed = collectionTask.Wait(10000);
            Logger.Info(
                "request_id=" + (requestId ?? "<null>") +
                " command=" + (command ?? "<null>") +
                " heavy_work_collection_wait waited_ms=" + stopwatch.ElapsedMilliseconds +
                " cap_hit=" + (completed ? "false" : "true"),
                "AgentHost");
        }
    }
}
