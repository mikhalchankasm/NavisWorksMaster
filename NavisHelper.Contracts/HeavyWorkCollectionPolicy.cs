namespace NavisHelper.Agent.Contracts
{
    /// <summary>
    /// Decides when the host should force a full collection after its own work, from the
    /// generation-0 collection count and command duration, so the decision is testable
    /// without a GC. A slow command triggers collection on its own; fast commands
    /// need four generation-0 collections since the last forced collection.
    ///
    /// The count stands in for "how much was allocated": a whole-model walk of 41 000
    /// items causes about six generation-0 collections and `host_status` none. Only
    /// collections that happen while a request runs count, so the caller opens each
    /// request with <see cref="BeginRequest"/> and, when no collection is due, closes it
    /// with <see cref="EndRequest"/>; what Navisworks collects on its own in
    /// between says nothing about the work the host was asked to do, and charging it to
    /// the next request makes a cheap command pay for a full collection. The baseline
    /// is taken after each forced collection, so the collections it performs itself
    /// never count towards the next one. `CollectionCount(0)` counts every collection,
    /// whatever generation it reached.
    /// </summary>
    public sealed class HeavyWorkCollectionPolicy
    {
        public const int Threshold = 4;
        public const int SlowCommandMilliseconds = 500;

        private int _requestStartCollectionCount;
        private int _collectedInEarlierRequests;

        public HeavyWorkCollectionPolicy(int startingCollectionCount)
        {
            _requestStartCollectionCount = startingCollectionCount;
        }

        public void BeginRequest(int gen0Now)
        {
            _requestStartCollectionCount = gen0Now;
        }

        public bool ShouldCollect(int gen0Now)
        {
            return IsDue(gen0Now, false);
        }

        public bool ShouldCollect(int gen0Now, long commandMilliseconds)
        {
            return IsDue(gen0Now, commandMilliseconds >= SlowCommandMilliseconds);
        }

        public int CollectionsSinceLast(int gen0Now)
        {
            return _collectedInEarlierRequests + gen0Now - _requestStartCollectionCount;
        }

        public void RecordCollection(int gen0Now)
        {
            _collectedInEarlierRequests = 0;
            _requestStartCollectionCount = gen0Now;
        }

        /// <summary>
        /// Closes a request that did not lead to a forced collection: its generation-0
        /// collections are carried towards the next one.
        /// </summary>
        public void EndRequest(int gen0Now)
        {
            _collectedInEarlierRequests = CollectionsSinceLast(gen0Now);
            _requestStartCollectionCount = gen0Now;
        }

        private bool IsDue(int gen0Now, bool slowCommand)
        {
            return CollectionsSinceLast(gen0Now) >= Threshold || slowCommand;
        }
    }
}
