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
        public ClashPairTestsCreateResponse ClashPairTestsCreate(Document document, ClashPairTestsCreateRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ClashPairTestsCreateRequest();
            var apply = request.Apply == true;
            var prefix = NormalizeClashPairTestPrefix(request.TestNamePrefix);
            var limit = ClampClashPairTestsCreateLimit(request.Limit);
            var requestedToleranceMm = request.ToleranceMm.HasValue
                ? NormalizeNonNegativeDouble(request.ToleranceMm.Value, "toleranceMm")
                : (double?)null;
            var requestedTestType = NormalizeClashTestType(request.TestType);
            var overwriteExisting = request.OverwriteExisting == true;
            var pairs = LoadClashPairTestInputPairs(request);
            var clash = document.GetClash();
            if (clash == null || clash.TestsData == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Clash Detective data is not available.");

            var existingTests = ClashApiCompat.GetClashTests(clash).ToList();
            ClashTest settingsSource = null;
            if (!string.IsNullOrWhiteSpace(request.SettingsFromTestName))
            {
                var sourceName = request.SettingsFromTestName.Trim();
                settingsSource = existingTests.FirstOrDefault(test =>
                    string.Equals(test.DisplayName, sourceName, StringComparison.OrdinalIgnoreCase));
                if (settingsSource == null)
                    throw new AgentCommandException(
                        ErrorCodes.SchemaViolation,
                        "settingsFromTestName must exactly match an existing Clash Test: " + sourceName);
            }

            var toleranceUnits = requestedToleranceMm.HasValue
                ? SectionBoxHelper.MmToDocUnits(requestedToleranceMm.Value)
                : settingsSource == null ? (double?)null : settingsSource.Tolerance;
            var testType = requestedTestType ??
                           (settingsSource == null ? ClashTestType.Hard : settingsSource.TestType);
            var rootItems = BuildClashBboxRootCandidates(document, new ClashBboxPairPlanRequest(), MaxClashBboxRootItems).Items;
            var rootsByPath = rootItems
                .Where(root => root != null && root.Item != null && !string.IsNullOrWhiteSpace(root.Info.Path))
                .GroupBy(root => root.Info.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var response = new ClashPairTestsCreateResponse
            {
                Applied = apply,
                TestNamePrefix = prefix,
                InputPairCount = pairs.Count,
                ToleranceMm = requestedToleranceMm ??
                              (settingsSource == null ? (double?)null : DocUnitsToMm(settingsSource.Tolerance)),
                TestType = testType.ToString(),
                SettingsSource = settingsSource == null
                    ? (requestedToleranceMm.HasValue || requestedTestType.HasValue ? "explicit" : "navisworks_default")
                    : BuildClashPairSettingsSource(
                        settingsSource.DisplayName,
                        !requestedToleranceMm.HasValue,
                        !requestedTestType.HasValue),
            };
            if (settingsSource == null &&
                !requestedToleranceMm.HasValue &&
                !requestedTestType.HasValue &&
                existingTests.Count > 0)
            {
                var nonDefaultCount = existingTests.Count(test =>
                    test.TestType != ClashTestType.Hard || Math.Abs(test.Tolerance) > 1e-12);
                if (nonDefaultCount > existingTests.Count / 2)
                {
                    const string settingsWarning =
                        "Most existing Clash Tests use non-default type or tolerance, but new tests would use Hard/0. " +
                        "Pass toleranceMm/testType or settingsFromTestName before apply=true.";
                    if (apply)
                        throw new AgentCommandException(ErrorCodes.SchemaViolation, settingsWarning);
                    response.Warnings.Add(settingsWarning);
                }
            }

            var sequence = 0;
            foreach (var pair in pairs.Take(limit))
            {
                sequence++;
                var item = BuildClashPairTestPlanItem(pair, prefix, sequence);
                response.Tests.Add(item);

                ClashBboxRootCandidate a;
                ClashBboxRootCandidate b;
                if (!TryResolveClashPairRoot(rootsByPath, pair == null ? null : pair.A, out a) ||
                    !TryResolveClashPairRoot(rootsByPath, pair == null ? null : pair.B, out b))
                {
                    item.Status = "skipped";
                    item.ErrorMessage = "Could not resolve pair root items in the active document.";
                    response.SkippedTestCount++;
                    continue;
                }

                item.SelectionAItemCount = 1;
                item.SelectionBItemCount = 1;
                var existing = existingTests.FirstOrDefault(test => string.Equals(test.DisplayName, item.TestName, StringComparison.OrdinalIgnoreCase));
                if (existing != null && !overwriteExisting)
                {
                    item.Status = "conflict";
                    item.ErrorMessage = "A Clash Detective test with this name already exists.";
                    response.ConflictTestCount++;
                    response.SkippedTestCount++;
                    continue;
                }

                item.Status = apply ? "pending" : "planned";
                response.PlannedTestCount++;
                if (!apply)
                    continue;

                try
                {
                    var existingCopy = existing == null ? null : existing.CreateCopy() as ClashTest;
                    var creationName = existing == null
                        ? item.TestName
                        : item.TestName + " [replacement " + Guid.NewGuid().ToString("N").Substring(0, 8) + "]";
                    var test = new ClashTest
                    {
                        DisplayName = creationName,
                        TestType = testType,
                    };
                    if (toleranceUnits.HasValue)
                        test.Tolerance = toleranceUnits.Value;

                    var selectionA = new ModelItemCollection();
                    selectionA.Add(a.Item);
                    var selectionB = new ModelItemCollection();
                    selectionB.Add(b.Item);
                    test.SelectionA.Selection.CopyFrom(selectionA);
                    test.SelectionB.Selection.CopyFrom(selectionB);
                    var created = AddClashTestCopyAndResolve(clash, test, creationName);
                    try
                    {
                        if (existing != null)
                        {
                            ClashTestMutationService.RemoveTest(clash.TestsData, existing);
                            existingTests.Remove(existing);
                            clash.TestsData.TestsEditDisplayName(created, item.TestName);
                        }
                    }
                    catch (Exception replaceEx)
                    {
                        try
                        {
                            ClashTestMutationService.RemoveTest(clash.TestsData, created);
                        }
                        catch (Exception rollbackEx)
                        {
                            var warning = item.TestName + ": failed to remove replacement Clash Test during rollback: " + rollbackEx.Message;
                            response.Warnings.Add(warning);
                            Logger.Error(warning, "ClashMcp");
                        }

                        if (existing != null && existingCopy != null)
                        {
                            string restoreError;
                            if (!TryRestoreClashTestCopy(clash.TestsData, existingCopy, out restoreError))
                            {
                                var warning = item.TestName + ": failed to restore replaced Clash Test during rollback" + (string.IsNullOrWhiteSpace(restoreError) ? "." : ": " + restoreError);
                                response.Warnings.Add(warning);
                                Logger.Error(warning, "ClashMcp");
                            }
                        }

                        Logger.Error(item.TestName + ": replacement failed; rollback attempted: " + replaceEx.Message, "ClashMcp");
                        throw;
                    }

                    existingTests = ClashApiCompat.GetClashTests(clash).ToList();
                    item.Applied = true;
                    item.Status = "created";
                    response.CreatedTestCount++;
                }
                catch (Exception ex)
                {
                    item.Status = "failed";
                    item.ErrorMessage = ex.Message;
                    response.Warnings.Add(item.TestName + ": " + ex.Message);
                    response.SkippedTestCount++;
                }
            }

            if (pairs.Count > limit)
                response.Warnings.Add("Input pair list was limited to the first " + limit.ToString(CultureInfo.InvariantCulture) + " pair(s).");
            if (!apply)
                response.Message = "Dry-run only. Pass apply=true to create Clash Detective tests.";
            else
                response.Message = "Created " + response.CreatedTestCount.ToString(CultureInfo.InvariantCulture) + " of " + response.PlannedTestCount.ToString(CultureInfo.InvariantCulture) + " planned Clash Detective test(s).";

            return response;
        }

        private static string BuildClashPairSettingsSource(
            string testName,
            bool copiedTolerance,
            bool copiedTestType)
        {
            if (!copiedTolerance && !copiedTestType)
                return "explicit (settingsFromTestName not used)";

            var fields = new List<string>();
            if (copiedTolerance)
                fields.Add("tolerance");
            if (copiedTestType)
                fields.Add("testType");
            return "test:" + (testName ?? string.Empty) + " (" + string.Join(",", fields) + ")";
        }

        public ClashCreateMatrixFromSelectionResponse ClashCreateMatrixFromSelection(Document document, ClashCreateMatrixFromSelectionRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            var stopwatch = Stopwatch.StartNew();
            request = request ?? new ClashCreateMatrixFromSelectionRequest();
            var apply = request.Apply == true;
            var includePairNames = request.IncludePairNames.GetValueOrDefault(true);
            var maxSelectedItems = ClampClashMatrixSelectedItems(request.MaxSelectedItems);
            var useGeneratedPrefix = request.UseGeneratedPrefix.GetValueOrDefault(false);
            var prefix = NormalizeClashMatrixPrefix(request.NamePrefix, useGeneratedPrefix);
            var toleranceMm = request.ToleranceMm.HasValue
                ? NormalizeNonNegativeDouble(request.ToleranceMm.Value, "toleranceMm")
                : (double?)null;
            var toleranceUnits = toleranceMm.HasValue ? SectionBoxHelper.MmToDocUnits(toleranceMm.Value) : (double?)null;
            var testType = NormalizeClashTestType(request.TestType).GetValueOrDefault(ClashTestType.Hard);
            var runAfterCreate = request.RunAfterCreate == true;
            var removePreviousGenerated = request.RemovePreviousGenerated == true;
            var pairNameTemplate = (request.PairNameTemplate ?? string.Empty).Trim();
            var pairNameStartIndex = request.PairNameStartIndex.GetValueOrDefault(1);
            if (pairNameStartIndex < 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "pairNameStartIndex must be non-negative.");
            var removePreviousPrefix = string.IsNullOrWhiteSpace(request.NamePrefix)
                ? (useGeneratedPrefix ? ClashMatrixGeneratedPrefix : string.Empty)
                : prefix;

            var clash = document.GetClash();
            if (clash == null || clash.TestsData == null || clash.TestsData.Value == null || clash.TestsData.Value.TestsRoot == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Clash Detective data is not available.");

            var explicitMatrixInput = HasExplicitClashMatrixInput(request);
            var matrixWarnings = new List<string>();
            var selectedItems = ResolveClashMatrixItems(document, request, maxSelectedItems, out matrixWarnings);

            var response = new ClashCreateMatrixFromSelectionResponse
            {
                Applied = apply,
                NamePrefix = prefix,
                UseGeneratedPrefix = useGeneratedPrefix,
                SelectedItemCount = selectedItems.Count,
                MatrixInputSource = explicitMatrixInput ? "matched_items" : "selection",
                LargeMatrixThreshold = ClashMatrixLargePairThreshold,
                ToleranceMm = toleranceMm,
                TestType = testType.ToString(),
                RunAfterCreate = runAfterCreate,
                RemovePreviousGenerated = removePreviousGenerated,
                PairNameTemplate = pairNameTemplate,
                PairNameStartIndex = pairNameStartIndex,
            };
            response.Warnings.AddRange(matrixWarnings);

            if (selectedItems.Count < 2)
            {
                response.Message = explicitMatrixInput
                    ? "Provide at least two matching matrix items."
                    : "Select at least two top-level items/groups in Navisworks.";
                stopwatch.Stop();
                response.ElapsedMs = stopwatch.ElapsedMilliseconds;
                return response;
            }

            if (selectedItems.Count > maxSelectedItems)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Selection contains " + selectedItems.Count.ToString(CultureInfo.InvariantCulture) + " items; maxSelectedItems is " + maxSelectedItems.ToString(CultureInfo.InvariantCulture) + ".");

            for (var i = 0; i < selectedItems.Count; i++)
            {
                response.SelectedItems.Add(new ClashMatrixSelectedItem
                {
                    SelectionIndex = i + 1,
                    Name = GetItemDisplayName(selectedItems[i]),
                    Path = BuildItemPath(selectedItems[i]),
                    SourceFile = TryGetSourceFile(selectedItems[i]) ?? string.Empty,
                });
            }

            response.PlannedPairCount = selectedItems.Count * (selectedItems.Count - 1) / 2;
            response.PlannedTestCount = response.PlannedPairCount;
            if (response.PlannedPairCount > MaxClashMatrixPairCount)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Matrix would create " + response.PlannedPairCount.ToString(CultureInfo.InvariantCulture) + " Clash Detective tests; maximum allowed is " + MaxClashMatrixPairCount.ToString(CultureInfo.InvariantCulture) + ". Select fewer items or use clash_bbox_pair_plan first.");

            if (removePreviousGenerated && string.IsNullOrWhiteSpace(removePreviousPrefix))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "removePreviousGenerated=true requires a generated prefix or an explicit non-empty namePrefix.");
            if (removePreviousGenerated && !apply)
            {
                response.RemovedPreviousTestCount = ClashMatrixMutationService.CountMatchingPreviousTests(
                    clash,
                    removePreviousPrefix,
                    !string.IsNullOrWhiteSpace(pairNameTemplate));
                response.PreviousTestsToRemove = ClashMatrixMutationService.FindMatchingPreviousTestNames(
                    clash,
                    removePreviousPrefix,
                    !string.IsNullOrWhiteSpace(pairNameTemplate),
                    50);
                response.PreviousTestsPreviewTruncated =
                    response.RemovedPreviousTestCount > response.PreviousTestsToRemove.Count;
                response.Warnings.Add(
                    "Dry-run: removePreviousGenerated would remove " +
                    response.RemovedPreviousTestCount.ToString(CultureInfo.InvariantCulture) +
                    " existing Clash Detective test(s); none were removed.");
            }

            if (response.PlannedPairCount > ClashMatrixLargePairThreshold && request.ConfirmLargeMatrix != true)
            {
                response.LargeMatrixConfirmationRequired = true;
                response.Message = "Matrix would create " + response.PlannedPairCount.ToString(CultureInfo.InvariantCulture) + " Clash Detective tests. Pass confirmLargeMatrix=true to proceed.";
                AddClashMatrixPreviewTests(response, selectedItems, prefix, pairNameTemplate, pairNameStartIndex, includePairNames, DefaultClashMatrixPreviewLimit);
                stopwatch.Stop();
                response.ElapsedMs = stopwatch.ElapsedMilliseconds;
                return response;
            }

            if (!apply)
            {
                AddClashMatrixPreviewTests(response, selectedItems, prefix, pairNameTemplate, pairNameStartIndex, includePairNames, DefaultClashMatrixPreviewLimit);
                response.Message = "Dry-run only. Pass apply=true to create Clash Detective matrix tests.";
                stopwatch.Stop();
                response.ElapsedMs = stopwatch.ElapsedMilliseconds;
                return response;
            }

            var mutationItems = new List<ClashMatrixMutationItem>();
            var createdByName = new Dictionary<string, ClashMatrixTestPlanItem>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var pairIndex = 0;
                for (var i = 0; i < selectedItems.Count; i++)
                {
                    for (var j = i + 1; j < selectedItems.Count; j++)
                    {
                        pairIndex++;
                        var planItem = BuildClashMatrixTestPlanItem(pairIndex, i, j, selectedItems[i], selectedItems[j], prefix, pairNameTemplate, pairNameStartIndex);
                        createdByName[planItem.TestName] = planItem;
                        if (includePairNames && response.Tests.Count < DefaultClashMatrixPreviewLimit)
                            response.Tests.Add(planItem);
                        else if (includePairNames)
                            response.PlannedTestsTruncated = true;

                        if (IsAncestorOrDescendant(selectedItems[i], selectedItems[j]))
                        {
                            planItem.Status = "skipped";
                            planItem.ErrorMessage = "Selected items are ancestor/descendant; skipped to avoid self-overlap clash noise.";
                            response.SkippedTestCount++;
                            response.PlannedTestCount--;
                            continue;
                        }

                        mutationItems.Add(new ClashMatrixMutationItem
                        {
                            ItemA = selectedItems[i],
                            ItemB = selectedItems[j],
                            PlanItem = planItem,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                response.Warnings.Add("Rolled back created Clash Detective tests after failure: " + ex.Message);
                response.Message = "Failed while creating Clash Detective matrix tests; created tests from this operation were rolled back and previous generated tests were restored when possible.";
                stopwatch.Stop();
                response.ElapsedMs = stopwatch.ElapsedMilliseconds;
                return response;
            }

            var mutationResult = ClashMatrixMutationService.Apply(
                clash,
                mutationItems,
                removePreviousGenerated,
                removePreviousPrefix,
                !string.IsNullOrWhiteSpace(pairNameTemplate),
                toleranceUnits,
                testType,
                response);
            if (mutationResult.Failed)
            {
                stopwatch.Stop();
                response.ElapsedMs = stopwatch.ElapsedMilliseconds;
                return response;
            }
            var createdTestNames = mutationResult.CreatedTestNames;

            if (runAfterCreate && createdTestNames.Count > 0)
            {
                RunClashTestsByNameWithProgress(clash, createdTestNames, "NavisHelper: run created Clash tests",
                    testName =>
                    {
                        ClashMatrixTestPlanItem planItem;
                        createdByName.TryGetValue(testName, out planItem);
                        response.RanTestCount++;
                        if (planItem != null)
                            planItem.Ran = true;
                    },
                    (testName, ex) =>
                    {
                        ClashMatrixTestPlanItem planItem;
                        createdByName.TryGetValue(testName, out planItem);
                        if (planItem != null)
                        {
                            planItem.Status = "run_failed";
                            planItem.ErrorMessage = ex.Message;
                        }
                        response.Warnings.Add(testName + ": " + ex.Message);
                    },
                    () => response.Warnings.Add("Run after create was cancelled before all created tests were run."));
            }

            response.Message = "Created " + response.CreatedTestCount.ToString(CultureInfo.InvariantCulture) +
                               " of " + response.PlannedTestCount.ToString(CultureInfo.InvariantCulture) +
                               " planned Clash Detective matrix test(s).";
            stopwatch.Stop();
            response.ElapsedMs = stopwatch.ElapsedMilliseconds;
            return response;
        }
    }
}
