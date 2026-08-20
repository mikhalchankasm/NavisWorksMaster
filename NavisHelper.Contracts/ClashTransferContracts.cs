using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashTransferConstants
    {
        public const string Schema = "navishelper.clash-test-transfer";
        public const int Version = 1;
        public const string JsonFormat = "navishelper_json";
        public const string SelectionSetLocatorPrefix = "lcop_selection_set_tree/";
        public const int DefaultTestLimit = 200;
        public const int MaximumTestLimit = 500;
        public const long MaximumInputBytes = 10L * 1024L * 1024L;
    }

    public static class ClashTransferSideKinds
    {
        public const string SelectionSet = "selection_set";
        public const string SearchSet = "search_set";
        public const string ModelRoot = "model_root";
        public const string Unsupported = "unsupported";
    }

    public static class ClashTransferArtifactStatuses
    {
        public const string NotRequested = "not_requested";
        public const string NotWrittenDryRun = "not_written_dry_run";
        public const string WrittenVerified = "written_verified";
    }

    public sealed class ClashTestTransferPlan
    {
        public string Schema { get; set; } = ClashTransferConstants.Schema;
        public int Version { get; set; } = ClashTransferConstants.Version;
        public DateTime CreatedAtUtc { get; set; }
        public string SourceDocument { get; set; }
        public List<ClashTestTransferDefinition> Tests { get; set; } = new List<ClashTestTransferDefinition>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashTestTransferDefinition
    {
        public string Name { get; set; }
        public string SourceTestHandle { get; set; }
        public string TestType { get; set; }
        public double? ToleranceMm { get; set; }
        public ClashTestTransferSide A { get; set; }
        public ClashTestTransferSide B { get; set; }
        public ClashNativeIgnoreRules IgnoreRules { get; set; }
        public bool Supported { get; set; } = true;
        public List<string> UnsupportedSettings { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashTestTransferSide
    {
        public string Side { get; set; }
        public string Kind { get; set; }
        public string ItemId { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string RootName { get; set; }
        public string SourceFile { get; set; }
        public string Locator { get; set; }
        public bool? SelfIntersect { get; set; }
        public int? CurrentMemberCount { get; set; }
        public bool Supported { get; set; } = true;
        public string ResolutionStatus { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashTestsExportRequest
    {
        public List<string> TestNames { get; set; } = new List<string>();
        public List<string> TestHandles { get; set; } = new List<string>();
        public string NamePrefix { get; set; }
        public string OutputPath { get; set; }
        public string Format { get; set; }
        public bool? OverwriteExisting { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class ClashTestsExportResponse
    {
        public bool Applied { get; set; }
        public string Format { get; set; }
        public int FoundTestCount { get; set; }
        public int ExportableTestCount { get; set; }
        public int UnsupportedTestCount { get; set; }
        public string CalculatedOutputPath { get; set; }
        public string OutputPath { get; set; }
        public bool OutputWritten { get; set; }
        public string ArtifactStatus { get; set; }
        public long BytesWritten { get; set; }
        public string Sha256 { get; set; }
        public ClashTestTransferPlan Plan { get; set; }
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashBatchtestImportRequest
    {
        public string InputPath { get; set; }
        public bool? Apply { get; set; }
        public bool? OverwriteExisting { get; set; }
        public int? Limit { get; set; }
        public bool? ContinueOnError { get; set; }
    }

    public sealed class ClashBatchtestImportResponse
    {
        public bool Applied { get; set; }
        public string InputPath { get; set; }
        public string Schema { get; set; }
        public int Version { get; set; }
        public int FoundTestCount { get; set; }
        public int SupportedTestCount { get; set; }
        public int UnsupportedTestCount { get; set; }
        public int PlannedTestCount { get; set; }
        public int CreatedTestCount { get; set; }
        public int ReplacedTestCount { get; set; }
        public int RolledBackTestCount { get; set; }
        public int FailedTestCount { get; set; }
        public bool DocumentMutated { get; set; }
        public ClashTestTransferPlan Plan { get; set; }
        public List<ClashSetTestPlanItem> Tests { get; set; } = new List<ClashSetTestPlanItem>();
        public string Message { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashBatchtestParseOptions
    {
        public int MaximumTestCount { get; set; } = ClashTransferConstants.MaximumTestLimit;
        public long MaximumCharactersInDocument { get; set; } = ClashTransferConstants.MaximumInputBytes;
        public string SourceDocument { get; set; }
    }

    public sealed class ClashTransferParseException : Exception
    {
        public ClashTransferParseException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public ClashTransferParseException(string code, string message, Exception innerException)
            : base(message, innerException)
        {
            Code = code;
        }

        public string Code { get; private set; }
    }

    public static class ClashTransferParseErrorCodes
    {
        public const string MalformedXml = "clash_transfer_xml_malformed";
        public const string UnsafeXml = "clash_transfer_xml_unsafe";
        public const string UnsupportedSchema = "clash_transfer_schema_unsupported";
        public const string InputTooLarge = "clash_transfer_input_too_large";
        public const string TestLimitExceeded = "clash_transfer_test_limit_exceeded";
        public const string InvalidPlan = "clash_transfer_plan_invalid";
    }
}
