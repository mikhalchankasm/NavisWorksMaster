namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Decides when the host should force a full collection after its own work, from the
    /// generation-0 collection count alone, so the decision is testable without a GC.
    ///
    /// The count stands in for "how much was allocated": a whole-model walk of 41 000
    /// items causes about six generation-0 collections and `host_status` none. The
    /// baseline is taken after each forced collection, so the collections it performs
    /// itself never count towards the next one. `CollectionCount(0)` counts every
    /// collection, whatever generation it reached.
    /// </summary>
    public sealed class HeavyWorkCollectionPolicy
    {
        public const int Threshold = 4;

        private int _lastCollectionCount;

        public HeavyWorkCollectionPolicy(int startingCollectionCount)
        {
            _lastCollectionCount = startingCollectionCount;
        }

        public bool ShouldCollect(int gen0Now)
        {
            return CollectionsSinceLast(gen0Now) >= Threshold;
        }

        public int CollectionsSinceLast(int gen0Now)
        {
            return gen0Now - _lastCollectionCount;
        }

        public void RecordCollection(int gen0Now)
        {
            _lastCollectionCount = gen0Now;
        }
    }
}
