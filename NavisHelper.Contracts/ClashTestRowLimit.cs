using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class ClashTestRowLimit
    {
        public const int DefaultMaxRows = 25;
        public const int MaxRows = 5000;

        public static int Clamp(int? value)
        {
            return Math.Max(0, Math.Min(MaxRows, value ?? DefaultMaxRows));
        }

        public static bool IsProblem(string status, string errorMessage, bool exceededTimebox)
        {
            if (!string.IsNullOrWhiteSpace(errorMessage) || exceededTimebox)
                return true;
            if (string.IsNullOrEmpty(status))
                return false;
            return new[] { "fail", "conflict", "skip", "timeout", "timed_out", "error", "not_supported", "not_found" }
                .Any(marker => status.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static void Apply(ClashRunBatchResponse response, int? maxRows)
        {
            if (response == null)
                return;
            response.TotalTestRowCount = response.Tests == null ? 0 : response.Tests.Count;
            var limit = Clamp(maxRows);
            response.TestsTruncated = response.TotalTestRowCount > limit;
            if (response.TestsTruncated)
                response.Tests = Limit(response.Tests, limit,
                    row => IsProblem(row.Status, row.ErrorMessage, row.ExceededTimebox));
        }

        public static void Apply(ClashPairTestsCreateResponse response, int? maxRows)
        {
            if (response == null)
                return;
            response.TotalTestRowCount = response.Tests == null ? 0 : response.Tests.Count;
            var limit = Clamp(maxRows);
            response.TestsTruncated = response.TotalTestRowCount > limit;
            if (response.TestsTruncated)
                response.Tests = Limit(response.Tests, limit,
                    row => IsProblem(row.Status, row.ErrorMessage, false));
        }

        public static void Apply(ClashManageTestsResponse response, int? maxRows)
        {
            if (response == null)
                return;
            response.TotalTestRowCount = response.Tests == null ? 0 : response.Tests.Count;
            var limit = Clamp(maxRows);
            response.TestsTruncated = response.TotalTestRowCount > limit;
            if (response.TestsTruncated)
                response.Tests = Limit(response.Tests, limit,
                    row => IsProblem(row.Status, row.ErrorMessage, false));
        }

        private static List<T> Limit<T>(List<T> rows, int limit, Func<T, bool> isProblem)
        {
            return rows.OrderByDescending(isProblem).Take(limit).ToList();
        }
    }
}
