using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;
using Newtonsoft.Json;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        private const int DefaultClashListTestsLimit = 200;
        private const int MaxClashListTestsLimit = 10000;
        private const int DefaultClashListResultsLimit = 500;
        private const int MaxClashListResultsLimit = 50000;
        private const int DefaultClashClusterLimit = ClashClusterKeyHelper.DefaultClusterLimit;
        private const int MaxClashClusterLimit = ClashClusterKeyHelper.MaxClusterLimit;
        private const int DefaultClashClusterPreviewRowsPerCluster = ClashClusterKeyHelper.DefaultPreviewRowsPerCluster;
        private const int MaxClashClusterPreviewRowsPerCluster = ClashClusterKeyHelper.MaxPreviewRowsPerCluster;
        private const double DefaultClashClusterDistanceMm = 300;
        private const int DefaultClashGroupPreviewRowsPerGroup = 5;
        private const int MaxClashGroupPreviewRowsPerGroup = 50;
        private const int DefaultClashGroupPageLimit = 500;
        private const int MaxClashGroupPageLimit = 5000;
        private const int DefaultClashGroupMinSize = 2;
        private const int MaxClashGroupAncestorLevelsUp = 20;
        private const int LargeClashGroupingConfirmationThreshold = 1000;
        private const int DefaultClashRenumberLimit = ClashRenumberPlanHelper.DefaultLimit;
        private const int MaxClashRenumberLimit = ClashRenumberPlanHelper.MaxLimit;
        private const int MaxClashRenumberWidth = ClashRenumberPlanHelper.MaxNumberWidth;
        private const string ClashRenumberScopeTopLevel = ClashRenumberPlanHelper.ScopeTopLevel;
        private const string ClashRenumberScopeRecursive = ClashRenumberPlanHelper.ScopeRecursive;
        private const string ClashRenumberOrderCurrent = ClashRenumberPlanHelper.OrderCurrent;
        private const string ClashRenumberOrderName = ClashRenumberPlanHelper.OrderName;
        private const string ClashGroupSideTagA = ClashGroupNameHelper.SideTagA;
        private const string ClashGroupSideTagB = ClashGroupNameHelper.SideTagB;
        private const string ClashGroupNameModeOwnerName = ClashGroupNameHelper.GroupNameModeOwnerName;
        private const string ClashGroupNameModeOwnerPath = ClashGroupNameHelper.GroupNameModeOwnerPath;
        private const string ClashClusterModeSpatial = ClashClusterKeyHelper.ModeSpatial;
        private const string ClashClusterModeObjectPair = ClashClusterKeyHelper.ModeObjectPair;
        private const string ClashClusterModeHybrid = ClashClusterKeyHelper.ModeHybrid;
        private const string ClashClusterModeNone = ClashClusterKeyHelper.ModeNone;
        private const int DefaultClashBboxMaxRootItems = ClashBboxOptionHelper.DefaultMaxRootItems;
        private const int MaxClashBboxRootItems = ClashBboxOptionHelper.MaxRootItems;
        private const int DefaultClashBboxMaxCandidatePairs = ClashBboxOptionHelper.DefaultMaxCandidatePairs;
        private const int MaxClashBboxCandidatePairs = ClashBboxOptionHelper.MaxCandidatePairs;
        private const int DefaultClashBboxPreviewLimit = ClashBboxOptionHelper.DefaultPreviewLimit;
        private const int MaxClashBboxPreviewLimit = ClashBboxOptionHelper.MaxPreviewLimit;
        private const int MaxClashBboxRefinePairChecksPerRootPair = 100000;
        private const int MaxClashBboxRefineSurvivorPairsPerDepth = 10000;
        private const int DefaultClashMatrixMaxSelectedItems = ClashBboxOptionHelper.DefaultMatrixMaxSelectedItems;
        private const int MaxClashMatrixSelectedItems = ClashBboxOptionHelper.MaxMatrixSelectedItems;
        private const int ClashMatrixLargePairThreshold = 300;
        private const int MaxClashMatrixPairCount = 10000;
        private const int DefaultClashMatrixPreviewLimit = 200;
        private const int MaxClashMatrixTraversalItems = 100000;
        private const int MaxClashMatrixTraversalMs = 10000;
        private const string ClashMatrixGeneratedPrefix = ClashTestNamePrefixHelper.MatrixGeneratedPrefix;
        private const int LargeClashReportConfirmationThreshold = ClashReportOptionHelper.LargeReportConfirmationThreshold;
        private const double DefaultClashReportBoxOffsetMm = 1500;
        private const double DefaultClashReportContextTransparency = 0.5;
        private const string ClashBoxModePoint = ClashReportOptionHelper.BoxModePoint;
        private const string ClashBoxModeItems = ClashReportOptionHelper.BoxModeItems;
        private const string ClashReportMarkerFileName = ClashReportOutputHelper.MarkerFileName;
        private readonly ClashReportOperationTracker _clashReportOperations = new ClashReportOperationTracker();
        private static readonly JsonSerializerSettings ClashReportJsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
        };










        private sealed class ClashBboxRootCandidate
        {
            public ModelItem Item { get; set; }
            public BoundingBox3D Box { get; set; }
            public ClashBboxRootItem Info { get; set; }
        }

        private sealed class ClashBboxRootCandidateSet
        {
            public int TotalCount { get; set; }
            public bool Truncated { get; set; }
            public List<ClashBboxRootCandidate> Items { get; set; } = new List<ClashBboxRootCandidate>();
            public List<string> Warnings { get; set; } = new List<string>();
            public List<string> RequestedRootNames { get; set; } = new List<string>();
            public List<string> MatchedRootNames { get; set; } = new List<string>();
            public List<string> UnmatchedRootNames { get; set; } = new List<string>();
            public List<string> NotEvaluatedRootNames { get; set; } = new List<string>();
        }

        private sealed class ClashBboxPairRefineStats
        {
            public int CheckedPairCount { get; set; }
            public int IntersectingPairCount { get; set; }
            public bool LimitReached { get; set; }
        }

        private sealed class ClashBboxRefineNode
        {
            public ModelItem Item { get; set; }
            public BoundingBox3D Box { get; set; }
        }




        private sealed class ClashGroupTestPlan
        {
            public ClashTest Test;
            public int TestIndex;
            public List<ClashGroupPlan> Groups = new List<ClashGroupPlan>();
        }

        private sealed class ClashGroupPlan
        {
            public ClashGroupPlanItem Item;
            public List<ClashResult> Results = new List<ClashResult>();
        }

        private sealed class ClashGroupWorkItem
        {
            public ClashReportWorkItem WorkItem;
            public ModelItem Owner;
            public string OwnerName;
            public string OwnerPath;
            public string RootId;
            public string RootName;
            public string SourceFile;
        }

        private sealed class ClashRenumberEntry
        {
            public SavedItem SavedItem;
            public ClashRenumberPlanItem Item;
            public int TestIndex;
            public string TestName;
            public string GroupPath;
            public string OldName;
            public string ItemType;
        }

        private sealed class ClashClusterRow
        {
            public ClashReportWorkItem WorkItem;
            public ClashAssociation Association;
            public Point3D ClashPoint;
            public BoundingBox3D ClashBox;
            public string Item1Name;
            public string Item2Name;
            public string Item1Path;
            public string Item2Path;
        }

        private sealed class ClashAssociation
        {
            public string PairKey;
            public ClashAssociationSide A;
            public ClashAssociationSide B;
            public bool Weak;
        }

        private sealed class ClashAssociationSide
        {
            public string Key;
            public string DisplayName;
            public string Level;
            public string SourceFile;
            public bool Weak;
        }

        private sealed class ClashClusterBuild
        {
            public string GroupMode;
            public List<ClashClusterRow> Rows = new List<ClashClusterRow>();
        }

        private sealed class ClashReportClusterContext
        {
            public List<ClashClusterSummary> Summaries = new List<ClashClusterSummary>();
            public List<ClashReportClusterExecution> Executions = new List<ClashReportClusterExecution>();
            public Dictionary<string, ClashReportClusterAssignment> Assignments = new Dictionary<string, ClashReportClusterAssignment>(StringComparer.OrdinalIgnoreCase);
            public int WeakAssociationCount;
        }

        private sealed class ClashReportClusterExecution
        {
            public ClashClusterSummary Summary;
            public List<ClashReportWorkItem> WorkItems = new List<ClashReportWorkItem>();
        }

        private sealed class ClashReportClusterAssignment
        {
            public int ClusterIndex;
            public string ClusterId;
            public string ClusterName;
        }

        private sealed class ClashReportWorkItem
        {
            public ClashResult Result;
            public int TestIndex;
            public int ResultIndex;
            public string TestName;
            public string GroupPath;
            public string ResultName;
            public string Status;
            public string AssignedTo;
        }
    }
}
