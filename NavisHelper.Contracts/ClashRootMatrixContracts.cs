using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashRootMatrixRequest
    {
        public string TestName { get; set; }
        public List<string> TestNames { get; set; } = new List<string>();
        public List<string> TestHandles { get; set; } = new List<string>();
        public List<string> StatusFilters { get; set; } = new List<string>();
        public bool? IncludeAllStatuses { get; set; }
        public bool? IgnoreSameFile { get; set; }
        public int? Offset { get; set; }
        public int? Limit { get; set; }
    }

    public sealed class ClashRootMatrixResponse
    {
        public int MatchedTestCount { get; set; }
        public int AnalyzedResultCount { get; set; }
        public int SameFileIgnoredCount { get; set; }
        public int UnresolvedRootCount { get; set; }
        public int PlannedPairCount { get; set; }
        public int ReturnedPairCount { get; set; }
        public int Offset { get; set; }
        public int Limit { get; set; }
        public int? NextOffset { get; set; }
        public bool PairsTruncated { get; set; }
        public bool Truncated { get; set; }
        public List<ClashRootMatrixPair> Pairs { get; set; } = new List<ClashRootMatrixPair>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashRootMatrixPair
    {
        public string RootAId { get; set; }
        public string RootAName { get; set; }
        public string RootASourceFile { get; set; }
        public string RootBId { get; set; }
        public string RootBName { get; set; }
        public string RootBSourceFile { get; set; }
        public int ClashCount { get; set; }
        public Dictionary<string, int> StatusCounts { get; set; } = new Dictionary<string, int>();
    }
}
