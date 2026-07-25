using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class SelectionSetReference
    {
        public string ItemId { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public int? Occurrence { get; set; }
        public string RootName { get; set; }
        public string SourceFile { get; set; }
    }

    public sealed class ClashSetPair
    {
        public SelectionSetReference A { get; set; }
        public SelectionSetReference B { get; set; }
        public string Name { get; set; }
    }

    public sealed class ClashTestsFromSetsRequest
    {
        public bool? Apply { get; set; }
        public List<ClashSetPair> Pairs { get; set; } = new List<ClashSetPair>();
        public string PlanPath { get; set; }
        public string TestType { get; set; }
        public double? ToleranceMm { get; set; }
        public string PairNameTemplate { get; set; }
        public int? PairNameStartIndex { get; set; }
        public bool? OverwriteExisting { get; set; }
        public bool? RunAfterCreate { get; set; }
        public int? Limit { get; set; }
        public int? RunBatchSize { get; set; }
        public int? PerTestTimeoutSeconds { get; set; }
        public ClashNativeIgnoreRules IgnoreRules { get; set; }
    }

    public sealed class ClashNativeIgnoreRules
    {
        public bool? SameFile { get; set; }
    }

    public sealed class ClashTestsFromSetsResponse
    {
        public bool Applied { get; set; }
        public string SideBinding { get; set; }
        public int InputPairCount { get; set; }
        public int PlannedTestCount { get; set; }
        public int CreatedTestCount { get; set; }
        public int ReplacedTestCount { get; set; }
        public int ConflictTestCount { get; set; }
        public int SkippedTestCount { get; set; }
        public bool RunAfterCreate { get; set; }
        public string RunOperationId { get; set; }
        public string PairNameTemplate { get; set; }
        public int PairNameStartIndex { get; set; }
        public double? ToleranceMm { get; set; }
        public string TestType { get; set; }
        public bool IgnoreSameFile { get; set; }
        public int IgnoreSameFileVerifiedTestCount { get; set; }
        public string Message { get; set; }
        public List<ClashSetTestPlanItem> Tests { get; set; } = new List<ClashSetTestPlanItem>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashSetTestPlanItem
    {
        public int PairIndex { get; set; }
        public string TestName { get; set; }
        public string AItemId { get; set; }
        public string AName { get; set; }
        public string APath { get; set; }
        public string AType { get; set; }
        public string BItemId { get; set; }
        public string BName { get; set; }
        public string BPath { get; set; }
        public string BType { get; set; }
        public int SelectionAItemCount { get; set; }
        public int SelectionBItemCount { get; set; }
        public string SideBinding { get; set; }
        public bool Applied { get; set; }
        public bool Replaced { get; set; }
        public string TestHandle { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
    }

    public sealed class ClashRunBatchRequest
    {
        public bool? Apply { get; set; }
        public List<string> TestNames { get; set; } = new List<string>();
        public List<string> TestHandles { get; set; } = new List<string>();
        public string NamePrefix { get; set; }
        public int? FirstN { get; set; }
        public int? BatchSize { get; set; }
        public int? PerTestTimeoutSeconds { get; set; }
    }

    public sealed class ClashRunResumeRequest
    {
        public string OperationId { get; set; }
        public int? BatchSize { get; set; }
    }

    public sealed class ClashRunStatusRequest
    {
        public string OperationId { get; set; }
        public int? WaitSeconds { get; set; }
        public bool? WaitForCompletion { get; set; }
    }

    public sealed class CancelClashRunRequest
    {
        public string OperationId { get; set; }
    }

    public sealed class ClashRunBatchResponse
    {
        public bool Applied { get; set; }
        public string OperationId { get; set; }
        public string State { get; set; }
        public bool IsRunning { get; set; }
        public bool CancelRequested { get; set; }
        public bool CancelAccepted { get; set; }
        public int TotalTestCount { get; set; }
        public int ProcessedTestCount { get; set; }
        public int SucceededTestCount { get; set; }
        public int FailedTestCount { get; set; }
        public int TimedOutTestCount { get; set; }
        public int RemainingTestCount { get; set; }
        public int BatchSize { get; set; }
        public int BatchProcessedCount { get; set; }
        public int PerTestTimeoutSeconds { get; set; }
        public string CurrentTestName { get; set; }
        public long CurrentTestElapsedMs { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? UpdatedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public long ElapsedMs { get; set; }
        public string Message { get; set; }
        public List<ClashRunTestResult> Tests { get; set; } = new List<ClashRunTestResult>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashRunTestResult
    {
        public int Index { get; set; }
        public string TestName { get; set; }
        public string TestHandle { get; set; }
        public string Status { get; set; }
        public long ElapsedMs { get; set; }
        public bool ExceededTimebox { get; set; }
        public string ErrorMessage { get; set; }
    }
}
