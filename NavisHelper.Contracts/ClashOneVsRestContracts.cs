using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashOneVsRestRequest
    {
        public bool? Apply { get; set; }
        public string TargetMatchHandle { get; set; }
        public string TargetPath { get; set; }
        public string Mode { get; set; }
        public List<string> IncludeNameMasks { get; set; } = new List<string>();
        public List<string> ExcludeNameMasks { get; set; } = new List<string>();
        public bool? IncludeHidden { get; set; }
        public string SetFolder { get; set; }
        public string PairNameTemplate { get; set; }
        public string TestType { get; set; }
        public double? ToleranceMm { get; set; }
        public List<ClashOneVsRestOverride> PairOverrides { get; set; } = new List<ClashOneVsRestOverride>();
        public bool? BboxPrefilter { get; set; }
        public bool? OverwriteExisting { get; set; }
        public int? Limit { get; set; }
    }

    /// <summary>
    /// A per-pair profile: the first override whose mask matches side B's name sets
    /// that test's type and tolerance, so a structure-vs-piping pair and a
    /// maintenance-clearance pair can differ inside one call.
    /// </summary>
    public sealed class ClashOneVsRestOverride
    {
        public string NameMask { get; set; }
        public string TestType { get; set; }
        public double? ToleranceMm { get; set; }
    }

    public sealed class ClashOneVsRestResponse
    {
        public bool Applied { get; set; }
        public string Mode { get; set; }
        public string TargetName { get; set; }
        public string TargetPath { get; set; }
        public string ParentPath { get; set; }
        public string SetFolder { get; set; }
        public int RestCandidateCount { get; set; }
        public int ExcludedByMaskCount { get; set; }
        public int BboxSkippedCount { get; set; }
        public int PlannedTestCount { get; set; }
        public int CreatedTestCount { get; set; }
        public int SkippedTestCount { get; set; }
        public int CreatedSetCount { get; set; }
        public List<ClashOneVsRestSet> Sets { get; set; } = new List<ClashOneVsRestSet>();
        public List<ClashOneVsRestPair> Pairs { get; set; } = new List<ClashOneVsRestPair>();
        public List<string> BboxSkippedNames { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
        public string Message { get; set; }
    }

    public sealed class ClashOneVsRestSet
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public int ItemCount { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
    }

    public sealed class ClashOneVsRestPair
    {
        public int Index { get; set; }
        public string AName { get; set; }
        public string BName { get; set; }
        public string TestName { get; set; }
        public string TestType { get; set; }
        public double? ToleranceMm { get; set; }
        public string SetAPath { get; set; }
        public string SetBPath { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
        public string TestHandle { get; set; }
    }

    public static class ClashOneVsRestStatuses
    {
        public const string Planned = "planned";
        public const string Created = "created";
        public const string Replaced = "replaced";
        public const string Exists = "exists";
        public const string Skipped = "skipped";
        public const string Conflict = "conflict";
        public const string Failed = "failed";
    }

    public static class ClashOneVsRestReasons
    {
        public const string BboxDisjoint = "bbox_disjoint";
        public const string TestExists = "test_exists";
        public const string SetExists = "set_exists";
        public const string SetFailed = "set_failed";
    }
}
