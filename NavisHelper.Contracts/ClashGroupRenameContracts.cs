using System;
using System.Collections.Generic;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashGroupRenameRequest
    {
        public string TestHandle { get; set; }
        public string GroupHandle { get; set; }
        public string GroupName { get; set; }
        public string NewName { get; set; }
        public List<ClashGroupRenameItem> Renames { get; set; } = new List<ClashGroupRenameItem>();
        public bool? Apply { get; set; }
    }

    public sealed class ClashGroupRenameItem
    {
        public string GroupHandle { get; set; }
        public string GroupName { get; set; }
        public string NewName { get; set; }
    }

    public sealed class ClashGroupRenameResponse
    {
        public bool Applied { get; set; }
        public string TestHandle { get; set; }
        public string TestName { get; set; }
        public int RequestedRenameCount { get; set; }
        public int PlannedRenameCount { get; set; }
        public int UnchangedCount { get; set; }
        public int RenamedCount { get; set; }
        public string Message { get; set; }
        public List<ClashGroupRenamePlanItem> Items { get; set; } = new List<ClashGroupRenamePlanItem>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public sealed class ClashGroupRenamePlanItem
    {
        public int Index { get; set; }
        public string RequestedGroupHandle { get; set; }
        public string RequestedGroupName { get; set; }
        public string GroupHandle { get; set; }
        public string GroupPath { get; set; }
        public string OldName { get; set; }
        public string NewName { get; set; }
        public int ResultCount { get; set; }
        public bool Applied { get; set; }
        public string Status { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>One existing ClashResultGroup of a test, in the order that numbers its handle.</summary>
    public sealed class ClashGroupRenameCandidate
    {
        public string Handle { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string ParentKey { get; set; }
        public int ResultCount { get; set; }
    }

    public sealed class ClashGroupRenamePlan
    {
        public List<ClashGroupRenamePlanItem> Items { get; set; } = new List<ClashGroupRenamePlanItem>();

        /// <summary>Candidate index per item, or -1 when the item did not resolve to a group.</summary>
        public List<int> CandidateIndexes { get; set; } = new List<int>();

        public bool CanApply
        {
            get
            {
                return Items.All(item =>
                    item.Status == ClashGroupRenamePlanner.StatusPlanned ||
                    item.Status == ClashGroupRenamePlanner.StatusUnchanged);
            }
        }
    }

    /// <summary>
    /// Validates a batch of group renames against the groups of one test. The batch
    /// is all-or-nothing: the service applies it only when every item is planned or
    /// unchanged. Renaming moves nothing, so the group handles of one listing stay
    /// valid for the whole batch.
    /// </summary>
    public static class ClashGroupRenamePlanner
    {
        public const string StatusPlanned = "planned";
        public const string StatusUnchanged = "unchanged";
        public const string StatusNotFound = "not_found";
        public const string StatusAmbiguous = "ambiguous";
        public const string StatusInvalid = "invalid";
        public const string StatusDuplicate = "duplicate_target";
        public const string StatusNameConflict = "name_conflict";

        public static ClashGroupRenamePlan Build(IList<ClashGroupRenameCandidate> groups, IList<ClashGroupRenameItem> renames)
        {
            var candidates = groups ?? new List<ClashGroupRenameCandidate>();
            var plan = new ClashGroupRenamePlan();
            var items = renames ?? new List<ClashGroupRenameItem>();
            for (var index = 0; index < items.Count; index++)
            {
                var rename = items[index] ?? new ClashGroupRenameItem();
                var item = new ClashGroupRenamePlanItem
                {
                    Index = index + 1,
                    RequestedGroupHandle = (rename.GroupHandle ?? string.Empty).Trim(),
                    RequestedGroupName = (rename.GroupName ?? string.Empty).Trim(),
                    NewName = (rename.NewName ?? string.Empty).Trim(),
                    Status = StatusPlanned,
                };
                plan.Items.Add(item);
                plan.CandidateIndexes.Add(Resolve(candidates, item));
            }

            MarkDuplicateTargets(plan);
            MarkNameConflicts(candidates, plan);
            return plan;
        }

        private static int Resolve(IList<ClashGroupRenameCandidate> candidates, ClashGroupRenamePlanItem item)
        {
            if (item.NewName.Length == 0)
                return Fail(item, StatusInvalid, "newName is required.");
            if (item.RequestedGroupHandle.Length == 0 && item.RequestedGroupName.Length == 0)
                return Fail(item, StatusInvalid, "Specify groupHandle or groupName.");

            var found = -1;
            if (item.RequestedGroupHandle.Length > 0)
            {
                for (var index = 0; index < candidates.Count; index++)
                {
                    if (string.Equals(candidates[index].Handle, item.RequestedGroupHandle, StringComparison.OrdinalIgnoreCase))
                    {
                        found = index;
                        break;
                    }
                }
                if (found < 0)
                    return Fail(item, StatusNotFound, "Clash group was not found: " + item.RequestedGroupHandle);
                if (item.RequestedGroupName.Length > 0 && !SameName(candidates[found].Name, item.RequestedGroupName))
                {
                    Describe(item, candidates[found]);
                    return Fail(item, StatusInvalid, "groupHandle " + item.RequestedGroupHandle + " is now '" + item.OldName +
                        "', not '" + item.RequestedGroupName + "'. Group handles are tree positions; list the groups again.");
                }
            }
            else
            {
                var matches = new List<int>();
                for (var index = 0; index < candidates.Count; index++)
                {
                    if (SameName(candidates[index].Name, item.RequestedGroupName))
                        matches.Add(index);
                }
                if (matches.Count == 0)
                    return Fail(item, StatusNotFound, "Clash group was not found: " + item.RequestedGroupName);
                if (matches.Count > 1)
                    return Fail(item, StatusAmbiguous, matches.Count + " groups are named '" + item.RequestedGroupName + "'; use groupHandle.");
                found = matches[0];
            }

            Describe(item, candidates[found]);
            if (string.Equals(item.OldName, item.NewName, StringComparison.Ordinal))
                item.Status = StatusUnchanged;
            return found;
        }

        private static void MarkDuplicateTargets(ClashGroupRenamePlan plan)
        {
            var byCandidate = plan.CandidateIndexes
                .Select((candidate, position) => new { Candidate = candidate, Position = position })
                .Where(entry => entry.Candidate >= 0)
                .GroupBy(entry => entry.Candidate)
                .Where(group => group.Count() > 1);
            foreach (var group in byCandidate)
            {
                foreach (var entry in group)
                {
                    var item = plan.Items[entry.Position];
                    item.Status = StatusDuplicate;
                    item.ErrorMessage = "The batch renames " + item.GroupHandle + " more than once.";
                }
            }
        }

        /// <summary>Two sibling groups may not end up with the same name (case ignored), whether or not both are renamed.</summary>
        private static void MarkNameConflicts(IList<ClashGroupRenameCandidate> candidates, ClashGroupRenamePlan plan)
        {
            var finalNames = candidates.Select(candidate => candidate.Name ?? string.Empty).ToList();
            var renamed = new Dictionary<int, int>();
            for (var position = 0; position < plan.Items.Count; position++)
            {
                var candidate = plan.CandidateIndexes[position];
                if (candidate < 0 || plan.Items[position].Status != StatusPlanned)
                    continue;
                finalNames[candidate] = plan.Items[position].NewName;
                renamed[candidate] = position;
            }

            foreach (var entry in renamed)
            {
                var candidate = entry.Key;
                var parentKey = candidates[candidate].ParentKey ?? string.Empty;
                for (var other = 0; other < candidates.Count; other++)
                {
                    if (other == candidate ||
                        !string.Equals(candidates[other].ParentKey ?? string.Empty, parentKey, StringComparison.Ordinal) ||
                        !SameName(finalNames[other], finalNames[candidate]))
                        continue;
                    var item = plan.Items[entry.Value];
                    item.Status = StatusNameConflict;
                    item.ErrorMessage = "Group " + candidates[other].Handle + " in the same folder would also be named '" + item.NewName + "'.";
                    break;
                }
            }
        }

        private static void Describe(ClashGroupRenamePlanItem item, ClashGroupRenameCandidate candidate)
        {
            item.GroupHandle = candidate.Handle ?? string.Empty;
            item.GroupPath = candidate.Path ?? string.Empty;
            item.OldName = candidate.Name ?? string.Empty;
            item.ResultCount = candidate.ResultCount;
        }

        private static int Fail(ClashGroupRenamePlanItem item, string status, string message)
        {
            item.Status = status;
            item.ErrorMessage = message;
            return -1;
        }

        private static bool SameName(string left, string right)
        {
            return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
