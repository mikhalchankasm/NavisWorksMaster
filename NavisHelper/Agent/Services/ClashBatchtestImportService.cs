using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed class ClashBatchtestImportService
    {
        private readonly ClashTestsFromSetsService _creationService;

        public ClashBatchtestImportService(ClashTestsFromSetsService creationService)
        {
            _creationService = creationService ?? throw new ArgumentNullException(nameof(creationService));
        }

        public ClashBatchtestImportResponse Execute(Document document, ClashBatchtestImportRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            request = request ?? new ClashBatchtestImportRequest();
            var inputPath = NormalizeInputPath(request.InputPath);
            var limit = Math.Max(1, Math.Min(ClashTransferConstants.MaximumTestLimit, request.Limit.GetValueOrDefault(ClashTransferConstants.DefaultTestLimit)));
            var info = new FileInfo(inputPath);
            if (!info.Exists)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "inputPath does not exist: " + inputPath);
            if (info.Length > ClashTransferConstants.MaximumInputBytes)
                throw new AgentCommandException(ErrorCodes.ClashTransferXmlMalformed, "batchtest XML exceeds 10 MB.");

            ClashTestTransferPlan plan;
            try
            {
                using (var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    plan = ClashBatchtestXmlParser.Parse(stream, new ClashBatchtestParseOptions
                    {
                        MaximumTestCount = limit,
                        MaximumCharactersInDocument = ClashTransferConstants.MaximumInputBytes,
                        SourceDocument = inputPath,
                    });
                }
            }
            catch (ClashTransferParseException ex)
            {
                throw new AgentCommandException(MapParseErrorCode(ex.Code), ex.Message);
            }

            var pairs = ClashTransferPlanHelper.ToPairs(plan, false);
            var creation = _creationService.Execute(document, new ClashTestsFromSetsRequest
            {
                Apply = request.Apply == true,
                Pairs = pairs,
                TestType = ClashTestTypeHelper.Hard,
                ToleranceMm = -1,
                PairNameTemplate = "{index|zeroPad:3} {aName}-{bName}",
                PairNameStartIndex = 1,
                OverwriteExisting = request.OverwriteExisting == true,
                RunAfterCreate = false,
                Limit = limit,
                ContinueOnError = request.ContinueOnError == true,
            });

            UpdatePlanResolution(plan, creation);
            var unsupportedCount = plan.Tests.Count(test => test == null || !test.Supported);
            var failedCount = creation.Tests.Count(test => test != null &&
                (string.Equals(test.Status, "failed", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(test.Status, "conflict", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(test.Status, "not_evaluated_after_failure", StringComparison.OrdinalIgnoreCase)));
            var response = new ClashBatchtestImportResponse
            {
                Applied = request.Apply == true,
                InputPath = inputPath,
                Schema = plan.Schema,
                Version = plan.Version,
                FoundTestCount = plan.Tests.Count,
                SupportedTestCount = plan.Tests.Count - unsupportedCount,
                UnsupportedTestCount = unsupportedCount,
                PlannedTestCount = creation.PlannedTestCount,
                CreatedTestCount = creation.CreatedTestCount,
                ReplacedTestCount = creation.ReplacedTestCount,
                RolledBackTestCount = creation.RolledBackTestCount,
                FailedTestCount = failedCount + unsupportedCount,
                DocumentMutated = request.Apply == true && creation.CreatedTestCount > 0,
                Plan = plan,
                Tests = creation.Tests,
            };
            response.Warnings.AddRange(plan.Warnings);
            response.Warnings.AddRange(creation.Warnings);
            foreach (var unsupported in plan.Tests.Where(test => test == null || !test.Supported))
                response.Warnings.Add("Unsupported batchtest definition '" + (unsupported == null ? string.Empty : unsupported.Name ?? string.Empty) + "' was not passed to the mutation service.");
            response.Message = request.Apply == true
                ? "Created or replaced " + creation.CreatedTestCount.ToString(CultureInfo.InvariantCulture) + " Clash Test definition(s); tests were not run and old results were not imported."
                : "Dry-run only. The XML was parsed and portable references were resolved without document mutation.";
            return response;
        }

        private static void UpdatePlanResolution(ClashTestTransferPlan plan, ClashTestsFromSetsResponse creation)
        {
            var supported = plan.Tests.Where(test => test != null && test.Supported).ToList();
            for (var index = 0; index < supported.Count && index < creation.Tests.Count; index++)
            {
                var definition = supported[index];
                var outcome = creation.Tests[index];
                if (definition.A != null)
                {
                    definition.A.ResolutionStatus = outcome == null || string.IsNullOrWhiteSpace(outcome.APath) ? "failed" : "resolved";
                    definition.A.Kind = ResolvedSideKind(outcome == null ? null : outcome.AType);
                    definition.A.CurrentMemberCount = outcome == null ? (int?)null : outcome.SelectionAItemCount;
                }
                if (definition.B != null)
                {
                    definition.B.ResolutionStatus = outcome == null || string.IsNullOrWhiteSpace(outcome.BPath) ? "failed" : "resolved";
                    definition.B.Kind = ResolvedSideKind(outcome == null ? null : outcome.BType);
                    definition.B.CurrentMemberCount = outcome == null ? (int?)null : outcome.SelectionBItemCount;
                }
            }
        }

        private static string ResolvedSideKind(string type)
        {
            if (string.Equals(type, "ModelRoot", StringComparison.OrdinalIgnoreCase))
                return ClashTransferSideKinds.ModelRoot;
            return string.Equals(type, "SearchSet", StringComparison.OrdinalIgnoreCase)
                ? ClashTransferSideKinds.SearchSet
                : ClashTransferSideKinds.SelectionSet;
        }

        private static string NormalizeInputPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "inputPath is required.");
            var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
            if (!Path.IsPathRooted(expanded))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "inputPath must be absolute.");
            return Path.GetFullPath(expanded);
        }

        private static string MapParseErrorCode(string code)
        {
            switch (code)
            {
                case ClashTransferParseErrorCodes.UnsafeXml: return ErrorCodes.ClashTransferXmlUnsafe;
                case ClashTransferParseErrorCodes.UnsupportedSchema: return ErrorCodes.ClashTransferSchemaUnsupported;
                case ClashTransferParseErrorCodes.MalformedXml:
                case ClashTransferParseErrorCodes.InputTooLarge:
                case ClashTransferParseErrorCodes.TestLimitExceeded:
                default: return ErrorCodes.ClashTransferXmlMalformed;
            }
        }
    }
}
