namespace NavisHelper.Agent.Contracts
{
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
