using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    /// <summary>
    /// Renames ClashResultGroup folders of one test. Only the display name changes:
    /// no result is moved and no status is touched, so group membership and the
    /// group handles of a listing stay as they were.
    /// </summary>
    internal sealed class ClashGroupRenameService
    {
        public ClashGroupRenameResponse Execute(Document document, ClashGroupRenameRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ClashGroupRenameRequest();
            var clash = document.GetClash();
            if (clash == null)
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Clash Detective is not available for the active document.");
            var tests = ClashApiCompat.GetClashTests(clash).ToList();
            int testIndex;
            if (!ClashHandleHelper.TryParseTestHandle(request.TestHandle, out testIndex) || testIndex < 1 || testIndex > tests.Count)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Clash test was not found for handle: " + (request.TestHandle ?? string.Empty));
            var test = tests[testIndex - 1];

            var renames = BuildRenameList(request);
            var groups = new List<ClashResultGroup>();
            var candidates = new List<ClashGroupRenameCandidate>();
            CollectGroups(test, string.Empty, "test", testIndex, groups, candidates);

            var plan = ClashGroupRenamePlanner.Build(candidates, renames);
            var response = new ClashGroupRenameResponse
            {
                TestHandle = ClashHandleHelper.BuildTestHandle(testIndex),
                TestName = test.DisplayName ?? string.Empty,
                RequestedRenameCount = renames.Count,
                PlannedRenameCount = plan.Items.Count(item => item.Status == ClashGroupRenamePlanner.StatusPlanned),
                UnchangedCount = plan.Items.Count(item => item.Status == ClashGroupRenamePlanner.StatusUnchanged),
            };
            response.Items.AddRange(plan.Items);

            if (!plan.CanApply)
            {
                response.Message = "No changes were applied: fix the items whose status is not planned or unchanged.";
                return response;
            }

            if (request.Apply != true)
            {
                response.Message = "Dry-run only. Pass apply=true to rename " +
                    response.PlannedRenameCount.ToString(CultureInfo.InvariantCulture) + " group(s).";
                return response;
            }

            var testsData = clash.TestsData;
            using (var transaction = document.BeginTransaction("NavisHelper MCP Clash Group Rename"))
            {
                for (var position = 0; position < plan.Items.Count; position++)
                {
                    var item = plan.Items[position];
                    if (item.Status != ClashGroupRenamePlanner.StatusPlanned)
                        continue;
                    testsData.TestsEditDisplayName(groups[plan.CandidateIndexes[position]], item.NewName);
                    item.Applied = true;
                    item.Status = "renamed";
                    response.RenamedCount++;
                }
                transaction.Commit();
            }

            response.Applied = true;
            response.Message = "Renamed " + response.RenamedCount.ToString(CultureInfo.InvariantCulture) + " clash group(s).";
            return response;
        }

        private static List<ClashGroupRenameItem> BuildRenameList(ClashGroupRenameRequest request)
        {
            var batch = (request.Renames ?? new List<ClashGroupRenameItem>()).Where(item => item != null).ToList();
            var hasSingle = !string.IsNullOrWhiteSpace(request.GroupHandle) ||
                            !string.IsNullOrWhiteSpace(request.GroupName) ||
                            !string.IsNullOrWhiteSpace(request.NewName);
            if (batch.Count > 0 && hasSingle)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Use either groupHandle/groupName with newName, or renames, not both.");
            if (batch.Count > 0)
                return batch;
            if (!hasSingle)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Specify groupHandle or groupName with newName, or a renames list.");
            return new List<ClashGroupRenameItem>
            {
                new ClashGroupRenameItem { GroupHandle = request.GroupHandle, GroupName = request.GroupName, NewName = request.NewName },
            };
        }

        // Same pre-order walk that numbers clash-group handles in clash_list_results and clash_ungroup.
        private static void CollectGroups(
            GroupItem parent,
            string parentPath,
            string parentKey,
            int testIndex,
            List<ClashResultGroup> groups,
            List<ClashGroupRenameCandidate> candidates)
        {
            if (parent == null || parent.Children == null)
                return;
            foreach (SavedItem child in parent.Children)
            {
                var group = child as ClashResultGroup;
                if (group == null)
                    continue;
                var name = group.DisplayName ?? string.Empty;
                var path = string.IsNullOrEmpty(parentPath) ? name : parentPath + " / " + name;
                var handle = ClashHandleHelper.BuildGroupHandle(testIndex, groups.Count + 1);
                groups.Add(group);
                candidates.Add(new ClashGroupRenameCandidate
                {
                    Handle = handle,
                    Name = name,
                    Path = path,
                    ParentKey = parentKey,
                    ResultCount = ClashWorkflowService.EnumerateResults(group).Count,
                });
                CollectGroups(group, path, handle, testIndex, groups, candidates);
            }
        }
    }
}
