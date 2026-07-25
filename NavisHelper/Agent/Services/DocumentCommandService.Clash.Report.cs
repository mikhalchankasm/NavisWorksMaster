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
        public ClashReportStatusResponse ClashReportStatus(ClashReportStatusRequest request)
        {
            var operationId = request == null ? string.Empty : request.OperationId;
            return _clashReportOperations.GetStatus(operationId);
        }

        public ClashReportStatusResponse CancelClashReport(CancelClashReportRequest request)
        {
            var operationId = request == null ? string.Empty : request.OperationId;
            return _clashReportOperations.Cancel(operationId);
        }

        public void FailRunningClashReport(string reason)
        {
            _clashReportOperations.FailRunning(reason);
        }

        public ClashGenerateReportResponse ClashGenerateReport(Document document, ClashGenerateReportRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ClashGenerateReportRequest();
            var apply = request.Apply == true;
            var runTestsRequested = request.RunTests == true;
            var limit = ClampClashReportLimit(request.Limit);
            var resultOffset = ClampNonNegative(request.ResultOffset);
            var append = request.Append == true;
            var confirmLargeReport = request.ConfirmLargeReport == true;
            var responseVerbosity = NormalizeClashReportVerbosity(request.Verbosity);
            var excludeItemNameContains = NormalizeTextFilters(request.ExcludeItemNameContains);
            var statusFilterPlan = ClashStatusFilterHelper.Normalize(request.StatusFilters, request.IncludeAllStatuses == true, true);
            var statusFilters = statusFilterPlan.StatusFilters;
            var includeAllStatuses = statusFilterPlan.IncludeAllStatuses;

            if (runTestsRequested && !apply)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "runTests requires apply=true because it changes Clash Detective state.");
            if (append && request.Overwrite == true)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "append=true cannot be combined with overwrite=true.");
            if (append && runTestsRequested)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "append=true cannot be combined with runTests=true because rerunning Clash Detective can change result offsets between batches.");

            var outputDirectory = NormalizeClashReportOutputDirectory(document, request.OutputDirectory);
            var artifactPaths = ClashReportOutputHelper.BuildArtifactPaths(outputDirectory);
            var response = new ClashGenerateReportResponse
            {
                Applied = apply,
                RunTestsRequested = runTestsRequested,
                RequestedTestName = FormatRequestedTestNames(request.TestName, request.TestNames, null),
                ExcludeItemNameContains = excludeItemNameContains,
                ResultOffset = resultOffset,
                LargeReportThreshold = LargeClashReportConfirmationThreshold,
                Verbosity = responseVerbosity,
                OutputDirectory = artifactPaths.OutputDirectory,
                ReportPath = artifactPaths.ReportPath,
                ManifestPath = artifactPaths.ManifestPath,
                ClashBoxesPath = artifactPaths.ClashBoxesPath,
            };
            if (statusFilterPlan.ShouldWarnIncludeAllOverrides)
                response.Warnings.Add("includeAllStatuses=true overrides the provided statusFilters.");

            ClashGenerateReportResponse previousReport = null;
            var previousReportError = string.Empty;
            if (append && File.Exists(response.ManifestPath))
            {
                try
                {
                    previousReport = JsonConvert.DeserializeObject<ClashGenerateReportResponse>(File.ReadAllText(response.ManifestPath, Encoding.UTF8));
                }
                catch (Exception ex)
                {
                    previousReportError = ex.Message;
                }
            }
            if (apply && append && previousReport == null)
            {
                var reason = string.IsNullOrWhiteSpace(previousReportError)
                    ? "Existing manifest was not found."
                    : "Existing manifest could not be read: " + previousReportError;
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "append=true requires a valid existing manifest.json in outputDirectory. " + reason);
            }

            if (Directory.Exists(outputDirectory) && apply && request.Overwrite != true && !append && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Output directory already exists and is not empty. Pass overwrite=true or choose another outputDirectory.");
            if (Directory.Exists(outputDirectory) && apply && request.Overwrite == true)
                ClearClashReportOutputDirectory(outputDirectory);

            ClashReportOperationState operation = null;
            if (apply)
            {
                operation = StartClashReportOperation(outputDirectory, response.ReportPath, response.ManifestPath, resultOffset, 0);
                response.OperationId = operation.OperationId;
            }

            try
            {
            var clash = document.GetClash();
            var tests = ClashApiCompat.GetClashTests(clash).ToList();
            var matchedTests = ResolveClashTests(tests, request.TestName, request.TestNames).ToList();
            if (runTestsRequested)
            {
                UpdateClashReportOperationState(operation, ClashReportOperationTracker.RunningTests, "Clash Detective tests are running before report generation.");
                if (HasRequestedClashTestScope(request.TestName, request.TestNames))
                {
                    RunClashTestsWithProgress(clash.TestsData, matchedTests, "NavisHelper: run Clash tests for report",
                        null,
                        (test, ex) => response.Warnings.Add(((test == null ? null : test.DisplayName) ?? "clash-test") + ": " + ex.Message),
                        () => response.Warnings.Add("Clash report test run was cancelled before all matched tests were run."),
                        () => IsClashReportCancellationRequested(operation));
                }
                else
                {
                    RunClashTestsWithProgress(clash.TestsData, tests, "NavisHelper: run all Clash tests for report",
                        null,
                        (test, ex) => response.Warnings.Add(((test == null ? null : test.DisplayName) ?? "clash-test") + ": " + ex.Message),
                        () => response.Warnings.Add("Clash report test run was cancelled before all tests were run."),
                        () => IsClashReportCancellationRequested(operation));
                }

                response.TestsRun = true;
                tests = ClashApiCompat.GetClashTests(clash).ToList();
                matchedTests = ResolveClashTests(tests, request.TestName, request.TestNames).ToList();
            }
            UpdateClashReportOperationState(operation, ClashReportOperationTracker.Running, "Clash report batch is preparing result scope.");

            var scopePage = ClashReportScopePageService.Build(
                response,
                tests,
                matchedTests,
                resultOffset,
                limit,
                includeAllStatuses,
                statusFilters,
                excludeItemNameContains,
                LargeClashReportConfirmationThreshold,
                confirmLargeReport,
                test => EnumerateClashReportWorkItems(test, test.DisplayName ?? string.Empty, string.Empty),
                GetClashTestIndex,
                row => row.Status,
                (row, value) => row.TestIndex = value,
                (row, value) => row.ResultIndex = value,
                row => row.Result,
                MatchesExcludedClashItemName,
                SortClashReportRows,
                IncrementStatusCount);
            var matchedRows = scopePage.MatchedRows;
            var rows = scopePage.Rows;
            var page = scopePage.Page;
            if (response.LargeReportConfirmationRequired && apply)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Filtered report scope contains " + matchedRows.Count.ToString(CultureInfo.InvariantCulture) + " clashes. Ask the user to confirm a large report, then retry with confirmLargeReport=true and batched generation.");
            var offsetMm = NormalizePositiveDouble(request.BoxOffsetMm, DefaultClashReportBoxOffsetMm, "boxOffsetMm");
            var boxMode = NormalizeClashBoxMode(request.BoxMode);
            response.BoxOffsetMm = offsetMm;
            response.BoxMode = boxMode;
            var contextTransparency = NormalizeUnitDouble(request.ContextTransparency, DefaultClashReportContextTransparency, "contextTransparency");
            var useFullBoxTransparency = request.UseFullBoxTransparency == true;
            var reportGroupMode = NormalizeClashReportClusterMode(request.GroupMode);
            var artifactGranularity = NormalizeClashReportArtifactGranularity(request.ArtifactGranularity);
            var clusterArtifacts = string.Equals(artifactGranularity, ClashReportOptionHelper.ArtifactGranularityCluster, StringComparison.OrdinalIgnoreCase);
            if (clusterArtifacts && string.Equals(reportGroupMode, ClashClusterModeNone, StringComparison.OrdinalIgnoreCase))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "artifactGranularity=cluster requires groupMode=hybrid, object_pair, or spatial.");
            if (clusterArtifacts && append)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "artifactGranularity=cluster does not support append=true. Generate the complete filtered scope in one call.");
            if (clusterArtifacts && resultOffset != 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "artifactGranularity=cluster requires resultOffset=0.");
            var clusterDistanceMm = NormalizePositiveDouble(request.ClusterDistanceMm, DefaultClashClusterDistanceMm, "clusterDistanceMm");
            var includeClusterMembers = request.IncludeClusterMembers.GetValueOrDefault(true);
            var maxMembersPerClusterInHtml = includeClusterMembers
                ? ClampClashReportClusterMembersInHtml(request.MaxMembersPerClusterInHtml)
                : 0;
            response.GroupMode = reportGroupMode;
            response.ArtifactGranularity = artifactGranularity;
            response.ClusterDistanceMm = clusterDistanceMm;
            if (useFullBoxTransparency && contextTransparency <= 0)
                response.Warnings.Add("useFullBoxTransparency=true with contextTransparency=0 leaves context objects unchanged.");
            if (useFullBoxTransparency && contextTransparency > 0)
                response.Warnings.Add("useFullBoxTransparency now uses safe owner-level context transparency instead of scanning all objects inside the clash box.");
            ClashReportClusterContext clusterContext = null;
            if (!string.Equals(reportGroupMode, ClashClusterModeNone, StringComparison.OrdinalIgnoreCase))
            {
                if (response.LargeReportConfirmationRequired)
                {
                    response.Warnings.Add("Report clustering skipped for large dry-run preview. Confirm the large report first, then retry with confirmLargeReport=true to build cluster summaries.");
                }
                else
                {
                    clusterContext = BuildClashReportClusterContext(matchedRows, reportGroupMode, clusterDistanceMm, maxMembersPerClusterInHtml);
                    response.ClusterCount = clusterContext.Summaries.Count;
                    response.ReturnedClusterCount = clusterContext.Summaries.Count;
                    response.Clusters.AddRange(clusterContext.Summaries);
                    if (clusterContext.WeakAssociationCount > 0)
                        response.Warnings.Add("Some report clusters use weak association fallbacks. Review associationLevelA/B before treating clusters as authoritative assignments.");
                }
            }
            if (clusterArtifacts && response.HasMoreResults)
            {
                if (apply)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "artifactGranularity=cluster requires the complete filtered result scope in one call. Increase limit to at least " + matchedRows.Count.ToString(CultureInfo.InvariantCulture) + " (maximum 5000), or narrow the test/status filters.");
                response.Warnings.Add("Cluster artifacts require the complete filtered result scope in one call. Increase limit to at least " + matchedRows.Count.ToString(CultureInfo.InvariantCulture) + " before apply=true.");
            }
            var colorA = ParseReportColor(request.ColorAHex, Autodesk.Navisworks.Api.Color.FromByteRGB(255, 38, 38), "colorAHex");
            var colorB = ParseReportColor(request.ColorBHex, Autodesk.Navisworks.Api.Color.FromByteRGB(38, 102, 255), "colorBHex");
            var createViewpoints = request.CreateViewpoints.GetValueOrDefault(true);
            var captureScreenshots = request.CaptureScreenshots.GetValueOrDefault(true);
            var includeClashPointMarker = request.IncludeClashPointMarker == true;
            var captureTopViewScreenshots = request.CaptureTopViewScreenshots == true;
            var screenshotOptions = NormalizeScreenshotExportOptions(request);
            response.ScreenshotProfile = screenshotOptions.Profile;
            response.ScreenshotFormat = screenshotOptions.Format;
            response.ScreenshotMaxWidth = screenshotOptions.MaxWidth;
            response.ScreenshotMaxHeight = screenshotOptions.MaxHeight;
            response.ScreenshotJpegQuality = screenshotOptions.JpegQuality;
            var folderName = "NavisHelper Clash Report " + DateTime.Now.ToString("yyyyMMdd HHmmss", CultureInfo.InvariantCulture);
            if (!apply)
            {
                var dryIndex = 0;
                foreach (var row in rows)
                {
                    dryIndex++;
                    var clusterAssignment = FindClashReportClusterAssignment(clusterContext, row);
                    response.Items.Add(BuildClashReportItem(row, resultOffset + dryIndex, null, offsetMm, boxMode, string.Empty, false, string.Empty, false, string.Empty, clusterAssignment: clusterAssignment));
                }

                response.Message = response.HasMoreResults
                    ? "Dry-run only. Pass apply=true to create viewpoints, screenshots, and report files. Continue with resultOffset=" + response.NextResultOffset + "."
                    : "Dry-run only. Pass apply=true to create viewpoints, screenshots, and report files.";
                ClashReportTransportCompactionHelper.Apply(response, responseVerbosity);
                return response;
            }

            UpdateClashReportOperationBatchScope(operation, rows.Count);
            Directory.CreateDirectory(outputDirectory);
            EnsureClashReportOutputMarker(outputDirectory);
            var imagesDirectory = artifactPaths.ImagesDirectory;
            Directory.CreateDirectory(imagesDirectory);

            var originalState = ClashDocumentStateService.Capture(document, "clash report generation");
            if (!string.IsNullOrWhiteSpace(originalState.Warning))
                response.Warnings.Add(originalState.Warning);

            ClashSavedViewpointTarget targetViewpointFolder = null;
            if (createViewpoints)
            {
                targetViewpointFolder = ClashSavedViewpointCreationService.ResolveTargetFolder(document, folderName, out _);
            }

            var preview = new ClashPreviewManager
            {
                OffsetMm = offsetMm,
                ContextTransparency = contextTransparency,
                UseContextTransparency = false,
                UseSectionBox = true,
                UseFixedIsoView = true,
                ColorA = colorA,
                ColorB = colorB,
            };
            response.Warnings.Add("Saved viewpoints are created with local appearance and visibility overrides when Navisworks COM saved-view APIs are available.");

            if (createViewpoints && targetViewpointFolder != null && targetViewpointFolder.Folder != null)
            {
                ClashSavedViewpointCreationService.SaveResetViewpoint(document, targetViewpointFolder);
                response.CreatedViewpointCount++;
            }

            if (clusterArtifacts)
            {
                if (clusterContext == null || (rows.Count > 0 && clusterContext.Executions.Count == 0))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Cluster artifact plan is empty for a non-empty report scope.");

                var processedByClusterId = new Dictionary<string, ClashReportItemProcessingResult>(StringComparer.OrdinalIgnoreCase);
                foreach (var execution in clusterContext.Executions)
                {
                    if (execution == null || execution.Summary == null)
                        continue;
                    if (IsClashReportCancellationRequested(operation))
                    {
                        response.Warnings.Add("Clash report cancellation requested before cluster " + execution.Summary.Index.ToString(CultureInfo.InvariantCulture) + ". Partial report artifacts were written.");
                        break;
                    }

                    var representative = execution.WorkItems.FirstOrDefault();
                    if (representative != null)
                        UpdateClashReportOperationCurrent(operation, representative, execution.Summary.Index);
                    var processed = ClashReportClusterProcessingService.Process(
                        document,
                        execution.WorkItems,
                        execution.Summary,
                        execution.Summary.Index,
                        originalState,
                        preview,
                        createViewpoints,
                        targetViewpointFolder,
                        useFullBoxTransparency,
                        contextTransparency,
                        captureScreenshots,
                        imagesDirectory,
                        screenshotOptions,
                        captureTopViewScreenshots,
                        includeClashPointMarker,
                        item => item.Result,
                        BuildSafeClusterViewpointName);

                    response.CreatedViewpointCount += processed.CreatedViewpointCount;
                    response.FullBoxTransparencyItemCount += processed.FullBoxTransparencyItemCount;
                    response.ScreenshotCount += processed.ScreenshotCount;
                    response.Warnings.AddRange(processed.Warnings);
                    ApplyClusterArtifactResult(execution.Summary, processed);
                    processedByClusterId[execution.Summary.ClusterId ?? string.Empty] = processed;
                }

                for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var row = rows[rowIndex];
                    var clusterAssignment = FindClashReportClusterAssignment(clusterContext, row);
                    if (clusterAssignment == null)
                        throw new AgentCommandException(ErrorCodes.CommandFailed, "A report row has no cluster assignment: " + (row.ResultName ?? string.Empty));

                    ClashReportItemProcessingResult processed;
                    if (!processedByClusterId.TryGetValue(clusterAssignment.ClusterId ?? string.Empty, out processed))
                    {
                        if (IsClashReportCancellationRequested(operation))
                            continue;
                        throw new AgentCommandException(ErrorCodes.CommandFailed, "A report cluster was not processed: " + (clusterAssignment.ClusterId ?? string.Empty));
                    }

                    var globalIndex = resultOffset + rowIndex + 1;
                    response.Items.Add(BuildClashReportItem(
                        row,
                        globalIndex,
                        processed.Box,
                        offsetMm,
                        boxMode,
                        processed.ViewpointName,
                        processed.ViewpointCreated,
                        processed.ViewpointPath,
                        processed.ScreenshotCaptured,
                        processed.ScreenshotPath,
                        processed.TopViewScreenshotCaptured,
                        processed.TopViewScreenshotPath,
                        0,
                        processed.ErrorMessage,
                        clusterAssignment));
                }
                UpdateClashReportOperationProgress(operation, response);
            }
            else
            {
                var index = 0;
                foreach (var row in rows)
                {
                    index++;
                    var globalIndex = resultOffset + index;
                    if (IsClashReportCancellationRequested(operation))
                    {
                        response.Warnings.Add("Clash report cancellation requested before clash " + globalIndex.ToString(CultureInfo.InvariantCulture) + ". Partial report artifacts were written.");
                        break;
                    }

                    UpdateClashReportOperationCurrent(operation, row, globalIndex);
                    var processed = ClashReportItemProcessingService.Process(
                        document,
                        row,
                        globalIndex,
                        originalState,
                        preview,
                        createViewpoints,
                        targetViewpointFolder,
                        offsetMm,
                        boxMode,
                        useFullBoxTransparency,
                        contextTransparency,
                        captureScreenshots,
                        imagesDirectory,
                        screenshotOptions,
                        captureTopViewScreenshots,
                        includeClashPointMarker,
                        item => item.Result,
                        BuildClashResultBox,
                        BuildSafeViewpointName);

                    response.CreatedViewpointCount += processed.CreatedViewpointCount;
                    response.FullBoxTransparencyItemCount += processed.FullBoxTransparencyItemCount;
                    response.ScreenshotCount += processed.ScreenshotCount;
                    response.Warnings.AddRange(processed.Warnings);
                    var clusterAssignment = FindClashReportClusterAssignment(clusterContext, row);
                    response.Items.Add(BuildClashReportItem(
                        row,
                        globalIndex,
                        processed.Box,
                        offsetMm,
                        boxMode,
                        processed.ViewpointName,
                        processed.ViewpointCreated,
                        processed.ViewpointPath,
                        processed.ScreenshotCaptured,
                        processed.ScreenshotPath,
                        processed.TopViewScreenshotCaptured,
                        processed.TopViewScreenshotPath,
                        processed.FullBoxTransparencyItemCount,
                        processed.ErrorMessage,
                        clusterAssignment));
                    UpdateClashReportOperationProgress(operation, response);
                }
            }

            try
            {
                preview.ResetOverrides();
                var warning = ClashDocumentStateService.RestoreAll(document, originalState, "clash report generation");
                if (!string.IsNullOrWhiteSpace(warning))
                    response.Warnings.Add(warning);
            }
            catch (Exception ex)
            {
                var warning = "Could not fully restore Navisworks view/selection/clipping after clash report generation: " + ex.Message;
                response.Warnings.Add(warning);
                Logger.Error(warning, "ClashMcp");
            }

            var cancelled = IsClashReportCancellationRequested(operation) && response.Items.Count < rows.Count;
            response.Cancelled = cancelled;
            response.ReturnedResultCount = response.Items.Count;
            if (clusterArtifacts && cancelled)
            {
                response.NextResultOffset = 0;
                response.HasMoreResults = false;
                response.Truncated = true;
            }
            else
            {
                page = ClashResultPagingHelper.CreateReturnedPage(resultOffset, limit, matchedRows.Count, response.Items.Count, cancelled);
                response.NextResultOffset = page.NextOffset;
                response.HasMoreResults = page.HasMore;
                response.Truncated = response.HasMoreResults;
            }
            var reportFileResponse = BuildClashReportFileResponse(response, append ? previousReport : null);
            response.AccumulatedResultCount = reportFileResponse.Items.Count;
            response.Message = clusterArtifacts && cancelled
                ? "Cluster report generation was cancelled. Partial artifacts were written; rerun the complete filtered scope from resultOffset=0 with overwrite=true to rebuild all cluster artifacts."
                : cancelled
                ? "Clash report batch cancelled. Partial report artifacts were written. Continue manually with resultOffset=" + response.NextResultOffset + " and append=true if needed."
                : response.HasMoreResults
                ? "Clash report batch generated. Continue with resultOffset=" + response.NextResultOffset + " and append=true."
                : "Clash report generated.";
            reportFileResponse.Message = response.Message;
            ClashReportArtifactWriter.WriteArtifacts(
                response,
                reportFileResponse,
                ClashReportHtmlRenderer.Render(reportFileResponse),
                ClashReportJsonSettings);

            CompleteClashReportOperation(operation, cancelled, response.Message);
            ClashReportTransportCompactionHelper.Apply(response, responseVerbosity);
            return response;
            }
            catch (Exception ex)
            {
                FailClashReportOperation(operation, ex.Message);
                throw;
            }
        }

        public ClashSaveViewpointsResponse ClashSaveViewpoints(Document document, ClashSaveViewpointsRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ClashSaveViewpointsRequest();
            var apply = request.Apply == true;
            var limit = ClampClashReportLimit(request.Limit);
            var resultOffset = ClampNonNegative(request.ResultOffset);
            var confirmLargeViewpoints = request.ConfirmLargeViewpoints == true;
            var excludeItemNameContains = NormalizeTextFilters(request.ExcludeItemNameContains);
            var statusFilterPlan = ClashStatusFilterHelper.Normalize(request.StatusFilters, request.IncludeAllStatuses == true, true);
            var statusFilters = statusFilterPlan.StatusFilters;
            var includeAllStatuses = statusFilterPlan.IncludeAllStatuses;

            var targetFolderPath = NormalizeClashViewpointFolderPath(request.FolderPath);
            var response = new ClashSaveViewpointsResponse
            {
                Applied = apply,
                RequestedTestName = FormatRequestedTestNames(request.TestName, request.TestNames, null),
                ResultOffset = resultOffset,
                LargeViewpointsThreshold = LargeClashReportConfirmationThreshold,
                ExcludeItemNameContains = excludeItemNameContains,
                FolderPath = targetFolderPath,
            };
            if (statusFilterPlan.ShouldWarnIncludeAllOverrides)
                response.Warnings.Add("includeAllStatuses=true overrides the provided statusFilters.");

            var clash = document.GetClash();
            var tests = ClashApiCompat.GetClashTests(clash).ToList();
            var matchedTests = ResolveClashTests(tests, request.TestName, request.TestNames).ToList();
            var scopePage = ClashReportScopePageService.BuildForSavedViewpoints(
                response,
                tests,
                matchedTests,
                resultOffset,
                limit,
                includeAllStatuses,
                statusFilters,
                excludeItemNameContains,
                LargeClashReportConfirmationThreshold,
                confirmLargeViewpoints,
                test => EnumerateClashReportWorkItems(test, test.DisplayName ?? string.Empty, string.Empty),
                GetClashTestIndex,
                row => row.Status,
                (row, value) => row.TestIndex = value,
                (row, value) => row.ResultIndex = value,
                row => row.Result,
                MatchesExcludedClashItemName,
                SortClashReportRows,
                IncrementStatusCount);
            var matchedRows = scopePage.MatchedRows;
            var rows = scopePage.Rows;
            var page = scopePage.Page;
            if (response.LargeViewpointsConfirmationRequired && apply)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Filtered scope contains " + matchedRows.Count.ToString(CultureInfo.InvariantCulture) + " clashes. Ask the user to confirm a large viewpoint operation, then retry with confirmLargeViewpoints=true and batching.");

            var offsetMm = NormalizePositiveDouble(request.BoxOffsetMm, DefaultClashReportBoxOffsetMm, "boxOffsetMm");
            var boxMode = NormalizeClashBoxMode(request.BoxMode);
            response.BoxOffsetMm = offsetMm;
            response.BoxMode = boxMode;
            var contextTransparency = NormalizeUnitDouble(request.ContextTransparency, DefaultClashReportContextTransparency, "contextTransparency");
            var requestedFullBoxTransparency = request.UseFullBoxTransparency == true;
            var requestedRootContextTransparency = request.UseRootContextTransparency == true;
            var useRootContextTransparency = false;
            var createOppositeViewpoints = request.CreateOppositeViewpoints == true;
            if (requestedFullBoxTransparency || requestedRootContextTransparency)
                response.Warnings.Add("Transparency is disabled for Saved Viewpoints. Viewpoints keep colors, section box, markers, and the required base reset view.");
            var colorA = ParseReportColor(request.ColorAHex, Autodesk.Navisworks.Api.Color.FromByteRGB(255, 38, 38), "colorAHex");
            var colorB = ParseReportColor(request.ColorBHex, Autodesk.Navisworks.Api.Color.FromByteRGB(38, 102, 255), "colorBHex");
            var includeClashPointMarker = request.IncludeClashPointMarker == true;
            var createResetViewpoint = true;

            if (!apply)
            {
                var dryIndex = 0;
                foreach (var row in rows)
                {
                    dryIndex++;
                    var globalIndex = resultOffset + dryIndex;
                    var plannedName = BuildSafeViewpointName(globalIndex, row);
                    if (createOppositeViewpoints)
                    {
                        var plannedName1 = plannedName + " (1)";
                        var plannedName2 = plannedName + " (2)";
                        response.Items.Add(BuildClashSavedViewpointItem(row, globalIndex, null, offsetMm, boxMode, plannedName1, false, string.IsNullOrWhiteSpace(targetFolderPath) ? plannedName1 : targetFolderPath + "/" + plannedName1, 0));
                        response.Items.Add(BuildClashSavedViewpointItem(row, globalIndex, null, offsetMm, boxMode, plannedName2, false, string.IsNullOrWhiteSpace(targetFolderPath) ? plannedName2 : targetFolderPath + "/" + plannedName2, 0));
                    }
                    else
                    {
                        var plannedPath = string.IsNullOrWhiteSpace(targetFolderPath) ? plannedName : targetFolderPath + "/" + plannedName;
                        response.Items.Add(BuildClashSavedViewpointItem(row, globalIndex, null, offsetMm, boxMode, plannedName, false, plannedPath, 0));
                    }
                }

                response.Message = response.HasMoreResults
                    ? "Dry-run only. Pass apply=true to create Saved Viewpoints. Continue with resultOffset=" + response.NextResultOffset + "."
                    : "Dry-run only. Pass apply=true to create Saved Viewpoints.";
                return response;
            }

            if (rows.Count == 0)
            {
                response.Message = "No matching Clash Detective results found for Saved Viewpoint creation.";
                return response;
            }

            if (document.SavedViewpoints == null || document.SavedViewpoints.RootItem == null)
                throw new AgentCommandException(ErrorCodes.NoActiveDocument, "Saved Viewpoints are not available.");

            ClashDocumentStateSnapshot originalState = null;
            ClashPreviewManager preview = null;
            var processedRows = 0;
            var cancelled = false;
            var progress = Autodesk.Navisworks.Api.Application.BeginProgress("NavisHelper: save clash viewpoints");
            try
            {
                originalState = ClashDocumentStateService.Capture(document, "saving clash viewpoints");
                if (!string.IsNullOrWhiteSpace(originalState.Warning))
                    response.Warnings.Add(originalState.Warning);

                string targetFolderWarning;
                var targetViewpointFolder = ClashSavedViewpointCreationService.ResolveTargetFolder(document, targetFolderPath, out targetFolderWarning);
                if (!string.IsNullOrWhiteSpace(targetFolderWarning))
                {
                    response.FolderPath = string.Empty;
                    response.Warnings.Add(targetFolderWarning);
                }
                targetFolderPath = response.FolderPath;

                if (createResetViewpoint && resultOffset == 0 && targetViewpointFolder != null && targetViewpointFolder.Folder != null)
                {
                    var resetViewpoint = ClashSavedViewpointCreationService.SaveResetViewpoint(document, targetViewpointFolder);
                    response.ResetViewpointCreated = resetViewpoint.Created;
                    response.ResetViewpointName = resetViewpoint.ViewpointName;
                }

                preview = new ClashPreviewManager
                {
                    OffsetMm = offsetMm,
                    ContextTransparency = contextTransparency,
                    UseContextTransparency = false,
                    UseSectionBox = true,
                    UseFixedIsoView = true,
                    ColorA = colorA,
                    ColorB = colorB,
                };

                var index = 0;
                foreach (var row in rows)
                {
                    index++;
                    if (progress.IsCanceled)
                    {
                        response.Warnings.Add("Operation cancelled before clash " + (resultOffset + index).ToString(CultureInfo.InvariantCulture) + ".");
                        cancelled = true;
                        break;
                    }

                    var globalIndex = resultOffset + index;
                    var viewpointName = string.Empty;
                    var viewpointPath = string.Empty;
                    var viewpointCreated = false;
                    var fullBoxTransparencyItemCount = 0;
                    BoundingBoxInfo clashBox = null;

                    try
                    {
                        ClashDocumentStateService.RestoreViewpoint(document, originalState);

                        preview.ShowClashResult(row.Result);
                        var box = BuildClashResultBox(row.Result, offsetMm, boxMode) ?? preview.LastExpandedBox;
                        if (box != null)
                        {
                            clashBox = ToBoundingBoxInfo(box);
                            ViewpointCameraHelper.ApplyIso1ViewToBox(document, box, row.Result == null ? null : row.Result.Center);
                            SectionBoxHelper.SetSectionBox(box);
                        }

                        if (useRootContextTransparency && contextTransparency > 0)
                        {
                            fullBoxTransparencyItemCount = preview.ApplyClashRootContextTransparency();
                            response.FullBoxTransparencyItemCount += fullBoxTransparencyItemCount;
                        }

                        if (includeClashPointMarker && row.Result != null && row.Result.Center != null)
                        {
                            string markerWarning;
                            if (!ClashReportMarkerViewService.TrySetClashPointMarker(document, row.Result.Center, out markerWarning) && !string.IsNullOrWhiteSpace(markerWarning))
                                response.Warnings.Add("Clash " + globalIndex + " marker: " + markerWarning);
                        }

                        viewpointName = BuildSafeViewpointName(globalIndex, row);
                        if (createOppositeViewpoints)
                            viewpointName += " (1)";
                        var savedViewpoint = ClashSavedViewpointCreationService.SaveCurrentViewpointWithAppearance(
                            document,
                            targetViewpointFolder,
                            viewpointName);
                        viewpointName = savedViewpoint.ViewpointName;
                        viewpointPath = savedViewpoint.ViewpointPath;
                        viewpointCreated = savedViewpoint.Created;
                        response.CreatedViewpointCount++;

                        if (includeClashPointMarker)
                            ClashReportMarkerViewService.ClearActiveViewRedlines(document);

                        response.Items.Add(BuildClashSavedViewpointItem(row, globalIndex, clashBox, offsetMm, boxMode, viewpointName, viewpointCreated, viewpointPath, fullBoxTransparencyItemCount));

                        if (createOppositeViewpoints && box != null)
                        {
                            if (includeClashPointMarker)
                                ClashReportMarkerViewService.ClearActiveViewRedlines(document);

                            ViewpointCameraHelper.ApplyIsoOppositeViewToBox(document, box, row.Result == null ? null : row.Result.Center);
                            SectionBoxHelper.SetSectionBox(box);

                            if (includeClashPointMarker && row.Result != null && row.Result.Center != null)
                            {
                                string markerWarning;
                                if (!ClashReportMarkerViewService.TrySetClashPointMarker(document, row.Result.Center, out markerWarning) && !string.IsNullOrWhiteSpace(markerWarning))
                                    response.Warnings.Add("Clash " + globalIndex + " opposite marker: " + markerWarning);
                            }

                            var oppositeViewpoint = ClashSavedViewpointCreationService.SaveCurrentViewpointWithAppearance(
                                document,
                                targetViewpointFolder,
                                BuildSafeViewpointName(globalIndex, row) + " (2)");
                            var oppositeName = oppositeViewpoint.ViewpointName;
                            var oppositePath = oppositeViewpoint.ViewpointPath;
                            response.CreatedViewpointCount++;

                            if (includeClashPointMarker)
                                ClashReportMarkerViewService.ClearActiveViewRedlines(document);

                            response.Items.Add(BuildClashSavedViewpointItem(row, globalIndex, clashBox, offsetMm, boxMode, oppositeName, true, oppositePath, fullBoxTransparencyItemCount));
                        }
                    }
                    catch (Exception ex)
                    {
                        if (includeClashPointMarker)
                            ClashReportMarkerViewService.ClearActiveViewRedlines(document);
                        response.Warnings.Add("Clash " + globalIndex + " failed: " + ex.Message);
                        response.Items.Add(BuildClashSavedViewpointItem(row, globalIndex, clashBox, offsetMm, boxMode, viewpointName, viewpointCreated, viewpointPath, fullBoxTransparencyItemCount, ex.Message));
                    }

                    processedRows++;
                    progress.Update(rows.Count == 0 ? 1.0 : (double)index / rows.Count);
                }
            }
            finally
            {
                Autodesk.Navisworks.Api.Application.EndProgress();
                try
                {
                    if (preview != null)
                        preview.ResetOverrides();
                    var warning = ClashDocumentStateService.RestoreAll(document, originalState, "saving clash viewpoints");
                    if (!string.IsNullOrWhiteSpace(warning))
                        response.Warnings.Add(warning);
                }
                catch (Exception ex)
                {
                    var warning = "Could not fully restore Navisworks view/selection/clipping after saving clash viewpoints: " + ex.Message;
                    response.Warnings.Add(warning);
                    Logger.Error(warning, "ClashMcp");
                }
            }

            response.ReturnedResultCount = processedRows;
            page = ClashResultPagingHelper.CreateReturnedPage(resultOffset, limit, matchedRows.Count, processedRows, cancelled);
            response.NextResultOffset = page.NextOffset;
            response.HasMoreResults = page.HasMore;
            response.Truncated = response.HasMoreResults;
            response.ReturnedStatusCounts.Clear();
            foreach (var row in rows.Take(processedRows))
                IncrementStatusCount(response.ReturnedStatusCounts, row.Status);
            response.Message = cancelled
                ? "Saved clash viewpoints batch cancelled. Continue with resultOffset=" + response.NextResultOffset + "."
                : response.HasMoreResults
                ? "Saved clash viewpoints batch created. Continue with resultOffset=" + response.NextResultOffset + "."
                : "Saved clash viewpoints created.";
            return response;
        }
    }
}
