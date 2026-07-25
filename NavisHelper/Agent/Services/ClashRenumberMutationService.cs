using System;
using System.Collections.Generic;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal static class ClashRenumberMutationService
    {
        public static ClashRenumberMutationResult ApplyRenames(
            DocumentClashTests testsData,
            IEnumerable<ClashRenumberMutationItem> entries)
        {
            if (testsData == null)
                throw new InvalidOperationException("Clash Detective data is not available.");

            var result = new ClashRenumberMutationResult();
            foreach (var entry in entries ?? new List<ClashRenumberMutationItem>())
            {
                if (entry == null || entry.SavedItem == null || entry.Item == null ||
                    string.Equals(entry.Item.Status, "unchanged", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    testsData.TestsEditDisplayName(entry.SavedItem, entry.Item.NewName);
                    entry.Item.Applied = true;
                    entry.Item.Status = "renamed";
                    result.RenamedCount++;
                }
                catch (Exception ex)
                {
                    entry.Item.Applied = false;
                    entry.Item.Status = "error";
                    entry.Item.ErrorMessage = ex.Message;
                    result.Warnings.Add((entry.Item.TestName ?? entry.Item.TestHandle) + " / " + (entry.Item.OldName ?? string.Empty) + ": " + ex.Message);
                }
            }

            return result;
        }
    }

    internal sealed class ClashRenumberMutationItem
    {
        public SavedItem SavedItem;
        public ClashRenumberPlanItem Item;
    }

    internal sealed class ClashRenumberMutationResult
    {
        public int RenamedCount;
        public List<string> Warnings = new List<string>();
    }
}
