using System;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashBboxOptionHelper
    {
        public const int DefaultMaxRootItems = 500;
        public const int MaxRootItems = 2000;
        public const int DefaultMaxCandidatePairs = 50000;
        public const int MaxCandidatePairs = 250000;
        public const int DefaultPreviewLimit = 200;
        public const int MaxPreviewLimit = 2000;
        public const int DefaultMatrixMaxSelectedItems = 100;
        public const int MaxMatrixSelectedItems = 1000;
        public const string RootModeTopLevelFiles = "top_level_files";

        public static string NormalizeRootMode(string rootMode)
        {
            var value = string.IsNullOrWhiteSpace(rootMode)
                ? RootModeTopLevelFiles
                : rootMode.Trim().ToLowerInvariant();

            switch (value)
            {
                case RootModeTopLevelFiles:
                case "top_level":
                case "roots":
                    return RootModeTopLevelFiles;
                default:
                    return null;
            }
        }

        public static bool TryNormalizeRefineDepth(int? refineDepth, out int value)
        {
            value = refineDepth.GetValueOrDefault(1);
            return value >= 0 && value <= 2;
        }

        public static int ClampRootItems(int? value)
        {
            return Clamp(value.GetValueOrDefault(DefaultMaxRootItems), 1, MaxRootItems);
        }

        public static int ClampCandidatePairs(int? value)
        {
            return Clamp(value.GetValueOrDefault(DefaultMaxCandidatePairs), 1, MaxCandidatePairs);
        }

        public static int ClampPreviewLimit(int? value)
        {
            return Clamp(value.GetValueOrDefault(DefaultPreviewLimit), 1, MaxPreviewLimit);
        }

        public static int ClampPairTestsCreateLimit(int? value)
        {
            return Clamp(value.GetValueOrDefault(DefaultPreviewLimit), 1, MaxCandidatePairs);
        }

        public static int ClampMatrixSelectedItems(int? value)
        {
            return Clamp(value.GetValueOrDefault(DefaultMatrixMaxSelectedItems), 2, MaxMatrixSelectedItems);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }
    }
}
