using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Host
{
    /// <summary>
    /// Every `ModelItem`, `ModelItemCollection` and `BoundingBox3D` the API hands out is a
    /// `NativeHandle` holding a native weak reference that only `Dispose` or its finalizer
    /// releases (read from `Autodesk.Navisworks.Api.dll` by reflection), and a walk
    /// disposes none of them. Until a full collection runs their finalizers, each later
    /// walk gets slower: identical whole-model `find_items_by_bbox`
    /// calls on `6513.nwd` grew from 1.0 to 5.5 s in one process, and a full collection
    /// before each call kept them flat. See "Throughput falls call after call" in
    /// `docs/MCP_TOOL_BASELINE.md`.
    ///
    /// After heavy work, as judged by <see cref="HeavyWorkCollectionPolicy"/>, the host
    /// collects on a pool thread instead of under the request gate, because the gate
    /// rejects rather than queues. The next gated request waits for that collection
    /// before it starts, since a collection that overlaps a walk slows both.
    /// </summary>
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
