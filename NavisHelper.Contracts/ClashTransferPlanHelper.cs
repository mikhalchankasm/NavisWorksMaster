using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashTransferPlanHelper
    {
        public static void Validate(ClashTestTransferPlan plan)
        {
            if (plan == null)
                throw new ClashTransferParseException(ClashTransferParseErrorCodes.InvalidPlan, "Transfer plan is required.");
            if (!string.Equals(plan.Schema, ClashTransferConstants.Schema, StringComparison.Ordinal))
                throw new ClashTransferParseException(ClashTransferParseErrorCodes.UnsupportedSchema, "Unsupported transfer plan schema: " + (plan.Schema ?? string.Empty));
            if (plan.Version != ClashTransferConstants.Version)
                throw new ClashTransferParseException(ClashTransferParseErrorCodes.UnsupportedSchema, "Unsupported transfer plan version: " + plan.Version + ".");
            if (plan.Tests == null)
                throw new ClashTransferParseException(ClashTransferParseErrorCodes.InvalidPlan, "Transfer plan tests array is required.");
        }

        public static List<ClashSetPair> ToPairs(ClashTestTransferPlan plan, bool includeUnsupported)
        {
            Validate(plan);
            var result = new List<ClashSetPair>();
            foreach (var test in plan.Tests)
            {
                if (test == null || (!includeUnsupported && !test.Supported))
                    continue;

                result.Add(new ClashSetPair
                {
                    Name = test.Name,
                    A = ToReference(test.A),
                    B = ToReference(test.B),
                    TestType = test.TestType,
                    ToleranceMm = test.ToleranceMm,
                    IgnoreRules = test.IgnoreRules,
                    ASelfIntersect = test.A == null ? null : test.A.SelfIntersect,
                    BSelfIntersect = test.B == null ? null : test.B.SelfIntersect,
                });
            }
            return result;
        }

        public static bool IsPortableSide(ClashTestTransferSide side)
        {
            if (side == null || !side.Supported)
                return false;
            if (string.Equals(side.Kind, ClashTransferSideKinds.ModelRoot, StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrWhiteSpace(side.RootName) || !string.IsNullOrWhiteSpace(side.SourceFile);
            if (string.Equals(side.Kind, ClashTransferSideKinds.SelectionSet, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(side.Kind, ClashTransferSideKinds.SearchSet, StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrWhiteSpace(side.Path);
            return false;
        }

        public static void RefreshSupport(ClashTestTransferDefinition test)
        {
            if (test == null)
                return;
            test.UnsupportedSettings = test.UnsupportedSettings ?? new List<string>();
            test.Warnings = test.Warnings ?? new List<string>();
            test.Supported = !string.IsNullOrWhiteSpace(test.Name) &&
                             IsSupportedTestType(test.TestType) &&
                             IsPortableSide(test.A) &&
                             IsPortableSide(test.B);
        }

        public static bool IsSupportedTestType(string value)
        {
            var normalized = ClashTestTypeHelper.NormalizeTestType(value);
            return normalized == ClashTestTypeHelper.Hard ||
                   normalized == ClashTestTypeHelper.HardConservative ||
                   normalized == ClashTestTypeHelper.Clearance ||
                   normalized == ClashTestTypeHelper.Duplicate;
        }

        private static SelectionSetReference ToReference(ClashTestTransferSide side)
        {
            if (side == null)
                return null;
            if (string.Equals(side.Kind, ClashTransferSideKinds.ModelRoot, StringComparison.OrdinalIgnoreCase))
            {
                return new SelectionSetReference
                {
                    RootName = side.RootName,
                    SourceFile = side.SourceFile,
                };
            }

            // itemId is deliberately diagnostic-only for transfer plans. Exact full path
            // is the portable identity across documents.
            return new SelectionSetReference
            {
                Path = side.Path,
                Name = side.Name,
            };
        }
    }
}
