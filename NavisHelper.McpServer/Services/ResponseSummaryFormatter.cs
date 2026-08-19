using System.Collections.Generic;
using System.Linq;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal static class ResponseSummaryFormatter
{
    internal static object Build(object response)
    {
        if (response == null)
            return null;

        var summary = new Dictionary<string, object>();

        var findItems = response as FindItemsResponse;
        if (findItems != null)
        {
            summary["matched_queries"] = findItems.Summary.MatchedQueries;
            summary["not_found_queries"] = findItems.Summary.NotFoundQueries;
            summary["total_items_in_matches"] = findItems.Summary.TotalItemsInMatches;
            summary["result_count"] = findItems.Results.Count;
            summary["scope"] = findItems.Scope;
            summary["match_depth"] = findItems.MatchDepth;
            summary["count_only"] = findItems.CountOnly;
            summary["matched_item_count"] = findItems.MatchedItemCount;
            summary["scanned_item_count"] = findItems.ScannedItemCount;
            summary["preflight"] = findItems.Preflight != null;
            return summary;
        }

        var listRootItems = response as ListRootItemsResponse;
        if (listRootItems != null)
        {
            summary["document_title"] = listRootItems.DocumentTitle;
            summary["root_item_count"] = listRootItems.RootItemCount;
            summary["returned_item_count"] = listRootItems.Items.Count;
            summary["truncated"] = listRootItems.Truncated;
            return summary;
        }

        var spatialSearch = response as FindItemsByBboxResponse;
        if (spatialSearch != null)
        {
            summary["coordinate_space"] = spatialSearch.CoordinateSpace;
            summary["match_mode"] = spatialSearch.MatchMode;
            summary["scanned_item_count"] = spatialSearch.ScannedItemCount;
            summary["matched_item_count"] = spatialSearch.MatchedItemCount;
            summary["returned_item_count"] = spatialSearch.ReturnedItemCount;
            summary["traversal_truncated"] = spatialSearch.TraversalTruncated;
            summary["results_truncated"] = spatialSearch.ResultsTruncated;
            return summary;
        }

        var listItemChildren = response as ListItemChildrenResponse;
        if (listItemChildren != null)
        {
            summary["document_title"] = listItemChildren.DocumentTitle;
            summary["parent_path"] = listItemChildren.ParentPath;
            summary["total_child_count"] = listItemChildren.TotalChildCount;
            summary["returned_child_count"] = listItemChildren.ReturnedChildCount;
            summary["truncated"] = listItemChildren.Truncated;
            return summary;
        }

        var hostStatus = response as HostStatusResponse;
        if (hostStatus != null)
        {
            summary["has_active_document"] = hostStatus.HasActiveDocument;
            summary["document_title"] = hostStatus.DocumentTitle;
            summary["model_count"] = hostStatus.ModelCount;
            summary["root_item_count"] = hostStatus.RootItemCount;
            summary["working_set_mb"] = hostStatus.WorkingSetMb;
            summary["plugin_version"] = hostStatus.PluginVersion;
            summary["protocol_version"] = hostStatus.ProtocolVersion;
            summary["host_log_file_path"] = hostStatus.HostLogFilePath;
            return summary;
        }

        var lastOperationStatus = response as LastOperationStatusResponse;
        if (lastOperationStatus != null)
        {
            summary["found"] = lastOperationStatus.Found;
            summary["command"] = lastOperationStatus.Command;
            summary["state"] = lastOperationStatus.State;
            summary["ok"] = lastOperationStatus.Ok;
            summary["error_code"] = lastOperationStatus.ErrorCode;
            summary["response_truncated"] = lastOperationStatus.ResponseTruncated;
            summary["elapsed_ms"] = lastOperationStatus.ElapsedMs;
            return summary;
        }

        var selectionStatus = response as SelectionStatusResponse;
        if (selectionStatus != null)
        {
            summary["has_selection"] = selectionStatus.HasSelection;
            summary["selected_item_count"] = selectionStatus.SelectedItemCount;
            summary["has_bounding_box"] = selectionStatus.BoundingBox != null;
            return summary;
        }

        var selectionCopyNames = response as SelectionCopyNamesResponse;
        if (selectionCopyNames != null)
        {
            summary["selected_item_count"] = selectionCopyNames.SelectedItemCount;
            summary["returned_item_count"] = selectionCopyNames.ReturnedItemCount;
            summary["truncated"] = selectionCopyNames.Truncated;
            return summary;
        }

        var dumpSubtreeNames = response as DumpSubtreeNamesResponse;
        if (dumpSubtreeNames != null)
        {
            summary["output_path"] = dumpSubtreeNames.OutputPath;
            summary["format"] = dumpSubtreeNames.Format;
            summary["root_name"] = dumpSubtreeNames.RootName;
            summary["item_count"] = dumpSubtreeNames.ItemCount;
            summary["skipped_hidden_item_count"] = dumpSubtreeNames.SkippedHiddenItemCount;
            summary["file_size_bytes"] = dumpSubtreeNames.FileSizeBytes;
            return summary;
        }

        var dumpSubtreeNamesJob = response as DumpSubtreeNamesJobStatusResponse;
        if (dumpSubtreeNamesJob != null)
        {
            summary["job_id"] = dumpSubtreeNamesJob.JobId;
            summary["instance_id"] = dumpSubtreeNamesJob.InstanceId;
            summary["state"] = dumpSubtreeNamesJob.State;
            summary["output_path"] = dumpSubtreeNamesJob.OutputPath;
            summary["partial_output_path"] = dumpSubtreeNamesJob.PartialOutputPath;
            summary["format"] = dumpSubtreeNamesJob.Format;
            summary["root_name"] = dumpSubtreeNamesJob.RootName;
            summary["item_count"] = dumpSubtreeNamesJob.ItemCount;
            summary["processed_item_count"] = dumpSubtreeNamesJob.ProcessedItemCount;
            summary["pending_item_count"] = dumpSubtreeNamesJob.PendingItemCount;
            summary["skipped_hidden_item_count"] = dumpSubtreeNamesJob.SkippedHiddenItemCount;
            summary["file_size_bytes"] = dumpSubtreeNamesJob.FileSizeBytes;
            summary["elapsed_ms"] = dumpSubtreeNamesJob.ElapsedMs;
            summary["error_message"] = dumpSubtreeNamesJob.ErrorMessage;
            summary["is_done"] = dumpSubtreeNamesJob.IsDone;
            return summary;
        }

        var selectionPropertyReport = response as SelectionPropertyReportResponse;
        if (selectionPropertyReport != null)
        {
            summary["selected_item_count"] = selectionPropertyReport.SelectedItemCount;
            summary["returned_item_count"] = selectionPropertyReport.ReturnedItemCount;
            summary["row_count"] = selectionPropertyReport.RowCount;
            summary["items_truncated"] = selectionPropertyReport.ItemsTruncated;
            summary["properties_truncated"] = selectionPropertyReport.PropertiesTruncated;
            summary["rows_truncated"] = selectionPropertyReport.RowsTruncated;
            return summary;
        }

        var selectionExportProperties = response as SelectionExportPropertiesResponse;
        if (selectionExportProperties != null)
        {
            summary["applied"] = selectionExportProperties.Applied;
            summary["output_path"] = selectionExportProperties.OutputPath;
            summary["format"] = selectionExportProperties.Format;
            summary["row_count"] = selectionExportProperties.RowCount;
            summary["file_size_bytes"] = selectionExportProperties.FileSizeBytes;
            return summary;
        }

        var selectionDistinctPropertyValues = response as SelectionDistinctPropertyValuesResponse;
        if (selectionDistinctPropertyValues != null)
        {
            summary["selected_item_count"] = selectionDistinctPropertyValues.SelectedItemCount;
            summary["scanned_item_count"] = selectionDistinctPropertyValues.ScannedItemCount;
            summary["matched_property_count"] = selectionDistinctPropertyValues.MatchedPropertyCount;
            summary["distinct_value_count"] = selectionDistinctPropertyValues.DistinctValueCount;
            summary["returned_value_count"] = selectionDistinctPropertyValues.ReturnedValueCount;
            summary["items_truncated"] = selectionDistinctPropertyValues.ItemsTruncated;
            summary["values_truncated"] = selectionDistinctPropertyValues.ValuesTruncated;
            return summary;
        }

        var selectionColorByProperty = response as SelectionColorByPropertyResponse;
        if (selectionColorByProperty != null)
        {
            summary["applied"] = selectionColorByProperty.Applied;
            summary["selected_item_count"] = selectionColorByProperty.SelectedItemCount;
            summary["scanned_item_count"] = selectionColorByProperty.ScannedItemCount;
            summary["matched_item_count"] = selectionColorByProperty.MatchedItemCount;
            summary["colored_item_count"] = selectionColorByProperty.ColoredItemCount;
            summary["distinct_value_count"] = selectionColorByProperty.DistinctValueCount;
            summary["returned_group_count"] = selectionColorByProperty.ReturnedGroupCount;
            summary["items_truncated"] = selectionColorByProperty.ItemsTruncated;
            summary["groups_truncated"] = selectionColorByProperty.GroupsTruncated;
            return summary;
        }

        var clashListTests = response as ClashListTestsResponse;
        if (clashListTests != null)
        {
            summary["total_test_count"] = clashListTests.TotalTestCount;
            summary["returned_test_count"] = clashListTests.ReturnedTestCount;
            summary["truncated"] = clashListTests.Truncated;
            return summary;
        }

        var clashListResults = response as ClashListResultsResponse;
        if (clashListResults != null)
        {
            summary["matched_test_count"] = clashListResults.MatchedTestCount;
            summary["total_result_count"] = clashListResults.TotalResultCount;
            summary["matched_result_count"] = clashListResults.MatchedResultCount;
            summary["returned_result_count"] = clashListResults.ReturnedResultCount;
            summary["result_offset"] = clashListResults.ResultOffset;
            summary["next_result_offset"] = clashListResults.NextResultOffset;
            summary["has_more_results"] = clashListResults.HasMoreResults;
            summary["truncated"] = clashListResults.Truncated;
            return summary;
        }

        var clashListClusters = response as ClashListClustersResponse;
        if (clashListClusters != null)
        {
            summary["matched_test_count"] = clashListClusters.MatchedTestCount;
            summary["raw_result_count"] = clashListClusters.RawResultCount;
            summary["cluster_count"] = clashListClusters.ClusterCount;
            summary["returned_cluster_count"] = clashListClusters.ReturnedClusterCount;
            summary["group_mode"] = clashListClusters.GroupMode;
            summary["cluster_distance_mm"] = clashListClusters.ClusterDistanceMm;
            summary["weak_association_count"] = clashListClusters.WeakAssociationCount;
            summary["excluded_by_item_name_count"] = clashListClusters.ExcludedByItemNameCount;
            summary["results_truncated"] = clashListClusters.ResultsTruncated;
            summary["truncated"] = clashListClusters.Truncated;
            return summary;
        }

        var clashGenerateReport = response as ClashGenerateReportResponse;
        if (clashGenerateReport != null)
        {
            summary["applied"] = clashGenerateReport.Applied;
            summary["operation_id"] = clashGenerateReport.OperationId;
            summary["cancelled"] = clashGenerateReport.Cancelled;
            summary["matched_test_count"] = clashGenerateReport.MatchedTestCount;
            summary["returned_result_count"] = clashGenerateReport.ReturnedResultCount;
            summary["result_offset"] = clashGenerateReport.ResultOffset;
            summary["next_result_offset"] = clashGenerateReport.NextResultOffset;
            summary["has_more_results"] = clashGenerateReport.HasMoreResults;
            summary["accumulated_result_count"] = clashGenerateReport.AccumulatedResultCount;
            summary["large_report_threshold"] = clashGenerateReport.LargeReportThreshold;
            summary["large_report_confirmation_required"] = clashGenerateReport.LargeReportConfirmationRequired;
            summary["excluded_by_item_name_count"] = clashGenerateReport.ExcludedByItemNameCount;
            summary["created_viewpoint_count"] = clashGenerateReport.CreatedViewpointCount;
            summary["screenshot_count"] = clashGenerateReport.ScreenshotCount;
            summary["screenshot_profile"] = clashGenerateReport.ScreenshotProfile;
            summary["screenshot_format"] = clashGenerateReport.ScreenshotFormat;
            summary["screenshot_max_width"] = clashGenerateReport.ScreenshotMaxWidth;
            summary["screenshot_max_height"] = clashGenerateReport.ScreenshotMaxHeight;
            summary["screenshot_jpeg_quality"] = clashGenerateReport.ScreenshotJpegQuality;
            summary["full_box_transparency_item_count"] = clashGenerateReport.FullBoxTransparencyItemCount;
            summary["output_directory"] = clashGenerateReport.OutputDirectory;
            summary["report_path"] = clashGenerateReport.ReportPath;
            summary["truncated"] = clashGenerateReport.Truncated;
            return summary;
        }

        var clashSaveViewpoints = response as ClashSaveViewpointsResponse;
        if (clashSaveViewpoints != null)
        {
            summary["applied"] = clashSaveViewpoints.Applied;
            summary["matched_test_count"] = clashSaveViewpoints.MatchedTestCount;
            summary["returned_result_count"] = clashSaveViewpoints.ReturnedResultCount;
            summary["result_offset"] = clashSaveViewpoints.ResultOffset;
            summary["next_result_offset"] = clashSaveViewpoints.NextResultOffset;
            summary["has_more_results"] = clashSaveViewpoints.HasMoreResults;
            summary["large_viewpoints_threshold"] = clashSaveViewpoints.LargeViewpointsThreshold;
            summary["large_viewpoints_confirmation_required"] = clashSaveViewpoints.LargeViewpointsConfirmationRequired;
            summary["excluded_by_item_name_count"] = clashSaveViewpoints.ExcludedByItemNameCount;
            summary["created_viewpoint_count"] = clashSaveViewpoints.CreatedViewpointCount;
            summary["reset_viewpoint_created"] = clashSaveViewpoints.ResetViewpointCreated;
            summary["full_box_transparency_item_count"] = clashSaveViewpoints.FullBoxTransparencyItemCount;
            summary["folder_path"] = clashSaveViewpoints.FolderPath;
            summary["truncated"] = clashSaveViewpoints.Truncated;
            return summary;
        }

        var modelColorScheme = response as ModelColorSchemeResponse;
        if (modelColorScheme != null)
        {
            summary["operation"] = modelColorScheme.Operation;
            summary["scope"] = modelColorScheme.Scope;
            summary["apply"] = modelColorScheme.Apply;
            summary["applied"] = modelColorScheme.Applied;
            summary["reset"] = modelColorScheme.Reset;
            summary["had_active_scheme"] = modelColorScheme.HadActiveScheme;
            summary["can_reset"] = modelColorScheme.CanReset;
            summary["traversed_item_count"] = modelColorScheme.TraversedItemCount;
            summary["eligible_item_count"] = modelColorScheme.EligibleItemCount;
            summary["matched_item_count"] = modelColorScheme.MatchedItemCount;
            summary["colored_item_count"] = modelColorScheme.ColoredItemCount;
            summary["unclassified_item_count"] = modelColorScheme.UnclassifiedItemCount;
            summary["analyzed_item_count"] = modelColorScheme.AnalyzedItemCount;
            summary["classified_item_count"] = modelColorScheme.ClassifiedItemCount;
            summary["unprocessed_item_count"] = modelColorScheme.UnprocessedItemCount;
            summary["items_truncated"] = modelColorScheme.ItemsTruncated;
            summary["analysis_truncated"] = modelColorScheme.AnalysisTruncated;
            summary["classification_truncated"] = modelColorScheme.ClassificationTruncated;
            summary["color_verification_sample_count"] = modelColorScheme.ColorVerificationSampleCount;
            summary["permanent_color_match_count"] = modelColorScheme.PermanentColorMatchCount;
            summary["active_color_match_count"] = modelColorScheme.ActiveColorMatchCount;
            summary["selection_cleared_item_count"] = modelColorScheme.SelectionClearedItemCount;
            summary["selection_restored"] = modelColorScheme.SelectionRestored;
            summary["returned_candidate_count"] = modelColorScheme.ReturnedCandidateCount;
            return summary;
        }

        var clashIsolateResult = response as ClashIsolateResultResponse;
        if (clashIsolateResult != null)
        {
            summary["apply"] = clashIsolateResult.Apply;
            summary["applied"] = clashIsolateResult.Applied;
            summary["result_handle"] = clashIsolateResult.ResultHandle;
            summary["test_name"] = clashIsolateResult.TestName;
            summary["box_mode"] = clashIsolateResult.BoxMode;
            summary["box_offset_mm"] = clashIsolateResult.BoxOffsetMm;
            summary["camera_mode"] = clashIsolateResult.CameraMode;
            summary["isolate_pair"] = clashIsolateResult.IsolatePair;
            summary["hidden_branch_count"] = clashIsolateResult.HiddenBranchCount;
            summary["screenshot_captured"] = clashIsolateResult.ScreenshotCaptured;
            summary["screenshot_path"] = clashIsolateResult.ScreenshotPath;
            summary["can_reset"] = clashIsolateResult.CanReset;
            return summary;
        }

        var clashResetIsolation = response as ClashResetIsolationResponse;
        if (clashResetIsolation != null)
        {
            summary["apply"] = clashResetIsolation.Apply;
            summary["had_active_isolation"] = clashResetIsolation.HadActiveIsolation;
            summary["reset"] = clashResetIsolation.Reset;
            return summary;
        }

        var captureCurrentView = response as CaptureCurrentViewResponse;
        if (captureCurrentView != null)
        {
            summary["apply"] = captureCurrentView.Apply;
            summary["captured"] = captureCurrentView.Captured;
            summary["output_path"] = captureCurrentView.OutputPath;
            summary["screenshot_profile"] = captureCurrentView.ScreenshotProfile;
            summary["screenshot_format"] = captureCurrentView.ScreenshotFormat;
            summary["file_size_bytes"] = captureCurrentView.FileSizeBytes;
            return summary;
        }

        var clashReportStatus = response as ClashReportStatusResponse;
        if (clashReportStatus != null)
        {
            summary["operation_id"] = clashReportStatus.OperationId;
            summary["state"] = clashReportStatus.State;
            summary["is_running"] = clashReportStatus.IsRunning;
            summary["cancel_requested"] = clashReportStatus.CancelRequested;
            summary["cancel_accepted"] = clashReportStatus.CancelAccepted;
            summary["processed_result_count"] = clashReportStatus.ProcessedResultCount;
            summary["total_batch_count"] = clashReportStatus.TotalBatchCount;
            summary["screenshot_count"] = clashReportStatus.ScreenshotCount;
            summary["output_directory"] = clashReportStatus.OutputDirectory;
            return summary;
        }

        var clashManageTests = response as ClashManageTestsResponse;
        if (clashManageTests != null)
        {
            summary["applied"] = clashManageTests.Applied;
            summary["operation"] = clashManageTests.Operation;
            summary["matched_test_count"] = clashManageTests.MatchedTestCount;
            summary["affected_test_count"] = clashManageTests.AffectedTestCount;
            return summary;
        }

        var clashBboxPairPlan = response as ClashBboxPairPlanResponse;
        if (clashBboxPairPlan != null)
        {
            summary["root_item_count"] = clashBboxPairPlan.TotalRootItems;
            summary["returned_root_item_count"] = clashBboxPairPlan.ReturnedRootItems;
            summary["root_pair_count"] = clashBboxPairPlan.RootPairCount;
            summary["candidate_pair_count"] = clashBboxPairPlan.CandidatePairCount;
            summary["skipped_pair_count"] = clashBboxPairPlan.SkippedPairCount;
            summary["output_path"] = clashBboxPairPlan.OutputPath;
            summary["calculated_output_path"] = clashBboxPairPlan.CalculatedOutputPath;
            summary["output_written"] = clashBboxPairPlan.OutputWritten;
            summary["artifact_status"] = clashBboxPairPlan.ArtifactStatus;
            summary["bytes_written"] = clashBboxPairPlan.BytesWritten;
            summary["unmatched_root_name_count"] = clashBboxPairPlan.UnmatchedRootNames.Count;
            summary["elapsed_ms"] = clashBboxPairPlan.ElapsedMs;
            return summary;
        }

        var clashPairTestsCreate = response as ClashPairTestsCreateResponse;
        if (clashPairTestsCreate != null)
        {
            summary["applied"] = clashPairTestsCreate.Applied;
            summary["input_pair_count"] = clashPairTestsCreate.InputPairCount;
            summary["planned_test_count"] = clashPairTestsCreate.PlannedTestCount;
            summary["created_test_count"] = clashPairTestsCreate.CreatedTestCount;
            summary["skipped_test_count"] = clashPairTestsCreate.SkippedTestCount;
            summary["conflict_test_count"] = clashPairTestsCreate.ConflictTestCount;
            return summary;
        }

        var clashCreateMatrix = response as ClashCreateMatrixFromSelectionResponse;
        if (clashCreateMatrix != null)
        {
            summary["applied"] = clashCreateMatrix.Applied;
            summary["selected_item_count"] = clashCreateMatrix.SelectedItemCount;
            summary["planned_pair_count"] = clashCreateMatrix.PlannedPairCount;
            summary["created_test_count"] = clashCreateMatrix.CreatedTestCount;
            summary["ran_test_count"] = clashCreateMatrix.RanTestCount;
            summary["removed_previous_test_count"] = clashCreateMatrix.RemovedPreviousTestCount;
            summary["elapsed_ms"] = clashCreateMatrix.ElapsedMs;
            return summary;
        }

        var selectedItemsPreview = response as SelectedItemsPreviewResponse;
        if (selectedItemsPreview != null)
        {
            summary["selected_item_count"] = selectedItemsPreview.SelectedItemCount;
            summary["returned_item_count"] = selectedItemsPreview.Items.Count;
            summary["truncated"] = selectedItemsPreview.Truncated;
            return summary;
        }

        var selectedItemsAncestry = response as SelectedItemsAncestryResponse;
        if (selectedItemsAncestry != null)
        {
            summary["selected_item_count"] = selectedItemsAncestry.SelectedItemCount;
            summary["returned_item_count"] = selectedItemsAncestry.Items.Count;
            summary["truncated"] = selectedItemsAncestry.Truncated;
            summary["max_chain_depth"] = selectedItemsAncestry.Items.Count == 0 ? 0 : selectedItemsAncestry.Items.Max(item => item.Chain.Count);
            return summary;
        }

        var selectedItemsTree = response as SelectedItemsTreeResponse;
        if (selectedItemsTree != null)
        {
            summary["selected_item_count"] = selectedItemsTree.SelectedItemCount;
            summary["returned_item_count"] = selectedItemsTree.ReturnedItemCount;
            summary["truncated"] = selectedItemsTree.Truncated;
            summary["depth_truncated"] = selectedItemsTree.DepthTruncated;
            summary["format"] = selectedItemsTree.Format;
            summary["root_count"] = selectedItemsTree.Roots.Count;
            summary["flat_item_count"] = selectedItemsTree.Items.Count;
            return summary;
        }

        var propertiesByHandle = response as ItemPropertiesByHandleResponse;
        if (propertiesByHandle != null)
        {
            summary["result_count"] = propertiesByHandle.Results.Count;
            summary["partial"] = propertiesByHandle.Partial;
            summary["returned_item_count"] = propertiesByHandle.Results.Sum(result => result.ReturnedItemCount);
            return summary;
        }

        var currentViewpointInfo = response as CurrentViewpointInfoResponse;
        if (currentViewpointInfo != null)
        {
            summary["has_active_view"] = currentViewpointInfo.HasActiveView;
            summary["has_current_viewpoint"] = currentViewpointInfo.HasCurrentViewpoint;
            summary["property_count"] = currentViewpointInfo.Properties.Count;
            return summary;
        }

        var savedViewpoints = response as ListSavedViewpointsResponse;
        if (savedViewpoints != null)
        {
            summary["total_item_count"] = savedViewpoints.TotalItemCount;
            summary["returned_item_count"] = savedViewpoints.ReturnedItemCount;
            summary["truncated"] = savedViewpoints.Truncated;
            return summary;
        }

        var savedViewpointsExport = response as SavedViewpointsExportResponse;
        if (savedViewpointsExport != null)
        {
            summary["output_path"] = savedViewpointsExport.OutputPath;
            summary["format"] = savedViewpointsExport.Format;
            summary["exported_item_count"] = savedViewpointsExport.ExportedItemCount;
            summary["folder_count"] = savedViewpointsExport.FolderCount;
            summary["viewpoint_count"] = savedViewpointsExport.ViewpointCount;
            return summary;
        }

        var savedViewpointsImport = response as SavedViewpointsImportResponse;
        if (savedViewpointsImport != null)
        {
            summary["apply"] = savedViewpointsImport.Apply;
            summary["input_path"] = savedViewpointsImport.InputPath;
            summary["target_folder_path"] = savedViewpointsImport.TargetFolderPath;
            summary["parsed_folder_count"] = savedViewpointsImport.ParsedFolderCount;
            summary["parsed_viewpoint_count"] = savedViewpointsImport.ParsedViewpointCount;
            summary["imported_viewpoint_count"] = savedViewpointsImport.ImportedViewpointCount;
            summary["skipped_item_count"] = savedViewpointsImport.SkippedItemCount;
            summary["warning_count"] = savedViewpointsImport.Warnings.Count;
            summary["changed"] = savedViewpointsImport.Changed;
            return summary;
        }

        var savedViewpointsManage = response as SavedViewpointsManageResponse;
        if (savedViewpointsManage != null)
        {
            summary["apply"] = savedViewpointsManage.Apply;
            summary["operation"] = savedViewpointsManage.Operation;
            summary["path"] = savedViewpointsManage.Path;
            summary["new_path"] = savedViewpointsManage.NewPath;
            summary["changed"] = savedViewpointsManage.Changed;
            summary["warning_count"] = savedViewpointsManage.Warnings.Count;
            return summary;
        }

        var savedViewpointsReorder = response as SavedViewpointsReorderResponse;
        if (savedViewpointsReorder != null)
        {
            summary["apply"] = savedViewpointsReorder.Apply;
            summary["folder_path"] = savedViewpointsReorder.FolderPath;
            summary["recursive"] = savedViewpointsReorder.Recursive;
            summary["processed_folder_count"] = savedViewpointsReorder.ProcessedFolderCount;
            summary["reordered_folder_count"] = savedViewpointsReorder.ReorderedFolderCount;
            summary["moved_item_count"] = savedViewpointsReorder.MovedItemCount;
            summary["changed"] = savedViewpointsReorder.Changed;
            return summary;
        }

        var selectionSets = response as ListSelectionSetsResponse;
        if (selectionSets != null)
        {
            summary["total_item_count"] = selectionSets.TotalItemCount;
            summary["filtered_item_count"] = selectionSets.FilteredItemCount;
            summary["offset"] = selectionSets.Offset;
            summary["next_offset"] = selectionSets.NextOffset;
            summary["returned_item_count"] = selectionSets.ReturnedItemCount;
            summary["truncated"] = selectionSets.Truncated;
            return summary;
        }

        var selectedSelectionSet = response as SelectSelectionSetResponse;
        if (selectedSelectionSet != null)
        {
            summary["apply"] = selectedSelectionSet.Apply;
            summary["path"] = selectedSelectionSet.Path;
            summary["selection_set_count"] = selectedSelectionSet.SelectionSetCount;
            summary["selected_item_count"] = selectedSelectionSet.SelectedItemCount;
            summary["folder_expansion_skipped"] = selectedSelectionSet.FolderExpansionSkipped;
            summary["selected"] = selectedSelectionSet.Selected;
            summary["warning_count"] = selectedSelectionSet.Warnings.Count;
            return summary;
        }

        var createdSearchSet = response as CreateSearchSetResponse;
        if (createdSearchSet != null)
        {
            summary["apply"] = createdSearchSet.Apply;
            summary["path"] = createdSearchSet.Path;
            summary["condition_count"] = createdSearchSet.ConditionCount;
            summary["matched_item_count"] = createdSearchSet.MatchedItemCount;
            summary["runtime_resolved_condition_count"] = createdSearchSet.RuntimeResolvedConditionCount;
            summary["created"] = createdSearchSet.Created;
            summary["overwritten"] = createdSearchSet.Overwritten;
            summary["name_conflict"] = createdSearchSet.NameConflict;
            summary["warning_count"] = createdSearchSet.Warnings.Count;
            return summary;
        }

        var selectionSetsManage = response as SelectionSetsManageResponse;
        if (selectionSetsManage != null)
        {
            summary["apply"] = selectionSetsManage.Apply;
            summary["operation"] = selectionSetsManage.Operation;
            summary["path"] = selectionSetsManage.Path;
            summary["new_path"] = selectionSetsManage.NewPath;
            summary["changed"] = selectionSetsManage.Changed;
            summary["warning_count"] = selectionSetsManage.Warnings.Count;
            return summary;
        }

        var clashTestsFromSets = response as ClashTestsFromSetsResponse;
        if (clashTestsFromSets != null)
        {
            summary["applied"] = clashTestsFromSets.Applied;
            summary["side_binding"] = clashTestsFromSets.SideBinding;
            summary["input_pair_count"] = clashTestsFromSets.InputPairCount;
            summary["planned_test_count"] = clashTestsFromSets.PlannedTestCount;
            summary["created_test_count"] = clashTestsFromSets.CreatedTestCount;
            summary["conflict_test_count"] = clashTestsFromSets.ConflictTestCount;
            summary["run_operation_id"] = clashTestsFromSets.RunOperationId;
            return summary;
        }

        var clashTestsExport = response as ClashTestsExportResponse;
        if (clashTestsExport != null)
        {
            summary["applied"] = clashTestsExport.Applied;
            summary["found_test_count"] = clashTestsExport.FoundTestCount;
            summary["exportable_test_count"] = clashTestsExport.ExportableTestCount;
            summary["unsupported_test_count"] = clashTestsExport.UnsupportedTestCount;
            summary["output_written"] = clashTestsExport.OutputWritten;
            summary["artifact_status"] = clashTestsExport.ArtifactStatus;
            summary["output_path"] = clashTestsExport.OutputPath;
            summary["bytes_written"] = clashTestsExport.BytesWritten;
            return summary;
        }

        var clashBatchtestImport = response as ClashBatchtestImportResponse;
        if (clashBatchtestImport != null)
        {
            summary["applied"] = clashBatchtestImport.Applied;
            summary["found_test_count"] = clashBatchtestImport.FoundTestCount;
            summary["supported_test_count"] = clashBatchtestImport.SupportedTestCount;
            summary["unsupported_test_count"] = clashBatchtestImport.UnsupportedTestCount;
            summary["planned_test_count"] = clashBatchtestImport.PlannedTestCount;
            summary["created_test_count"] = clashBatchtestImport.CreatedTestCount;
            summary["replaced_test_count"] = clashBatchtestImport.ReplacedTestCount;
            summary["rolled_back_test_count"] = clashBatchtestImport.RolledBackTestCount;
            summary["failed_test_count"] = clashBatchtestImport.FailedTestCount;
            return summary;
        }

        var clashRun = response as ClashRunBatchResponse;
        if (clashRun != null)
        {
            summary["operation_id"] = clashRun.OperationId;
            summary["state"] = clashRun.State;
            summary["processed_test_count"] = clashRun.ProcessedTestCount;
            summary["total_test_count"] = clashRun.TotalTestCount;
            summary["failed_test_count"] = clashRun.FailedTestCount;
            summary["timed_out_test_count"] = clashRun.TimedOutTestCount;
            summary["current_test_name"] = clashRun.CurrentTestName;
            return summary;
        }

        var closeNavisworks = response as CloseNavisworksResponse;
        if (closeNavisworks != null)
        {
            summary["mode"] = closeNavisworks.Mode;
            summary["apply"] = closeNavisworks.Apply;
            summary["exit_scheduled"] = closeNavisworks.ExitScheduled;
            summary["document_was_modified"] = closeNavisworks.DocumentWasModified;
            summary["document_path"] = closeNavisworks.DocumentPath;
            summary["saved_path"] = closeNavisworks.SavedPath;
            summary["discarded_unsaved_changes"] = closeNavisworks.DiscardedUnsavedChanges;
            summary["native_prompt_expected"] = closeNavisworks.NativePromptExpected;
            summary["message"] = closeNavisworks.Message;
            return summary;
        }

        var selectionSetsReorder = response as SelectionSetsReorderResponse;
        if (selectionSetsReorder != null)
        {
            summary["apply"] = selectionSetsReorder.Apply;
            summary["folder_path"] = selectionSetsReorder.FolderPath;
            summary["recursive"] = selectionSetsReorder.Recursive;
            summary["processed_folder_count"] = selectionSetsReorder.ProcessedFolderCount;
            summary["reordered_folder_count"] = selectionSetsReorder.ReorderedFolderCount;
            summary["moved_item_count"] = selectionSetsReorder.MovedItemCount;
            summary["changed"] = selectionSetsReorder.Changed;
            return summary;
        }

        var activatedViewpoint = response as ActivateSavedViewpointResponse;
        if (activatedViewpoint != null)
        {
            summary["apply"] = activatedViewpoint.Apply;
            summary["path"] = activatedViewpoint.Path;
            summary["activated"] = activatedViewpoint.Activated;
            return summary;
        }

        var selectItems = response as SelectItemsResponse;
        if (selectItems != null)
        {
            summary["selected_handle_count"] = selectItems.SelectedHandleCount;
            summary["selected_item_count"] = selectItems.SelectedItemCount;
            summary["partial"] = selectItems.Partial;
            return summary;
        }

        var hideUnselected = response as HideUnselectedResponse;
        if (hideUnselected != null)
        {
            summary["apply"] = hideUnselected.Apply;
            summary["selected_item_count"] = hideUnselected.SelectedItemCount;
            summary["would_hide_item_count"] = hideUnselected.WouldHideItemCount;
            summary["hidden_item_count"] = hideUnselected.HiddenItemCount;
            summary["affected_preview_count"] = hideUnselected.AffectedItemsPreview.Count;
            summary["affected_preview_truncated"] = hideUnselected.AffectedItemsPreviewTruncated;
            return summary;
        }

        var hideSelected = response as HideSelectedResponse;
        if (hideSelected != null)
        {
            summary["apply"] = hideSelected.Apply;
            summary["selected_item_count"] = hideSelected.SelectedItemCount;
            summary["would_hide_item_count"] = hideSelected.WouldHideItemCount;
            summary["hidden_item_count"] = hideSelected.HiddenItemCount;
            summary["affected_preview_count"] = hideSelected.AffectedItemsPreview.Count;
            summary["affected_preview_truncated"] = hideSelected.AffectedItemsPreviewTruncated;
            return summary;
        }

        var revealSelected = response as RevealSelectedResponse;
        if (revealSelected != null)
        {
            summary["apply"] = revealSelected.Apply;
            summary["selected_item_count"] = revealSelected.SelectedItemCount;
            summary["would_reveal_item_count"] = revealSelected.WouldRevealItemCount;
            summary["revealed_item_count"] = revealSelected.RevealedItemCount;
            summary["affected_preview_count"] = revealSelected.AffectedItemsPreview.Count;
            summary["affected_preview_truncated"] = revealSelected.AffectedItemsPreviewTruncated;
            return summary;
        }

        var unhideSelected = response as UnhideSelectedResponse;
        if (unhideSelected != null)
        {
            summary["apply"] = unhideSelected.Apply;
            summary["selected_item_count"] = unhideSelected.SelectedItemCount;
            summary["would_reveal_item_count"] = unhideSelected.WouldRevealItemCount;
            summary["revealed_item_count"] = unhideSelected.RevealedItemCount;
            summary["affected_preview_count"] = unhideSelected.AffectedItemsPreview.Count;
            summary["affected_preview_truncated"] = unhideSelected.AffectedItemsPreviewTruncated;
            return summary;
        }

        var isolateSelected = response as IsolateSelectedResponse;
        if (isolateSelected != null)
        {
            summary["apply"] = isolateSelected.Apply;
            summary["selected_item_count"] = isolateSelected.SelectedItemCount;
            summary["would_hide_item_count"] = isolateSelected.WouldHideItemCount;
            summary["hidden_item_count"] = isolateSelected.HiddenItemCount;
            summary["affected_preview_count"] = isolateSelected.AffectedItemsPreview.Count;
            summary["affected_preview_truncated"] = isolateSelected.AffectedItemsPreviewTruncated;
            return summary;
        }

        var showAll = response as ShowAllResponse;
        if (showAll != null)
        {
            summary["apply"] = showAll.Apply;
            summary["currently_hidden_item_count"] = showAll.CurrentlyHiddenItemCount;
            summary["would_reveal_item_count"] = showAll.WouldRevealItemCount;
            summary["revealed_item_count"] = showAll.RevealedItemCount;
            summary["affected_preview_count"] = showAll.AffectedItemsPreview.Count;
            summary["affected_preview_truncated"] = showAll.AffectedItemsPreviewTruncated;
            return summary;
        }

        var selectionSet = response as CreateSelectionSetResponse;
        if (selectionSet != null)
        {
            summary["apply"] = selectionSet.Apply;
            summary["name"] = selectionSet.Name;
            summary["path"] = selectionSet.Path;
            summary["selected_item_count"] = selectionSet.SelectedItemCount;
            summary["source"] = selectionSet.Source;
            summary["partial"] = selectionSet.Partial;
            summary["match_handle_result_count"] = selectionSet.MatchHandleResults.Count;
            summary["created"] = selectionSet.Created;
            summary["overwritten"] = selectionSet.Overwritten;
            summary["name_conflict"] = selectionSet.NameConflict;
            return summary;
        }

        var viewpoint = response as CreateViewpointResponse;
        if (viewpoint != null)
        {
            summary["apply"] = viewpoint.Apply;
            summary["name"] = viewpoint.Name;
            summary["folder_path"] = viewpoint.FolderPath;
            summary["created"] = viewpoint.Created;
            summary["name_conflict"] = viewpoint.NameConflict;
            return summary;
        }

        var markupSelection = response as MarkupSelectionResponse;
        if (markupSelection != null)
        {
            summary["apply"] = markupSelection.Apply;
            summary["path"] = markupSelection.Path;
            summary["selected_item_count"] = markupSelection.SelectedItemCount;
            summary["mark_style"] = markupSelection.MarkStyle;
            summary["mark_count"] = markupSelection.MarkCount;
            summary["solo_mark_count"] = markupSelection.SoloMarkCount;
            summary["merged_mark_count"] = markupSelection.MergedMarkCount;
            summary["ellipse_count"] = markupSelection.EllipseCount;
            summary["cluster_count"] = markupSelection.ClusterCount;
            summary["created"] = markupSelection.Created;
            summary["name_conflict"] = markupSelection.NameConflict;
            return summary;
        }

        var liveMarkers = response as LiveMarkersResponse;
        if (liveMarkers != null)
        {
            summary["apply"] = liveMarkers.Apply;
            summary["visible"] = liveMarkers.Visible;
            summary["active"] = liveMarkers.Active;
            summary["persistent"] = liveMarkers.Persistent;
            summary["style"] = liveMarkers.Style;
            summary["selected_item_count"] = liveMarkers.SelectedItemCount;
            summary["marker_count"] = liveMarkers.MarkerCount;
            summary["solo_marker_count"] = liveMarkers.SoloMarkerCount;
            summary["merged_marker_count"] = liveMarkers.MergedMarkerCount;
            return summary;
        }

        var sectionBoxViewpoint = response as SectionBoxViewpointResponse;
        if (sectionBoxViewpoint != null)
        {
            summary["apply"] = sectionBoxViewpoint.Apply;
            summary["path"] = sectionBoxViewpoint.Path;
            summary["selected_item_count"] = sectionBoxViewpoint.SelectedItemCount;
            summary["cluster_count"] = sectionBoxViewpoint.ClusterCount;
            summary["box_offset_mm"] = sectionBoxViewpoint.BoxOffsetMm;
            summary["created"] = sectionBoxViewpoint.Created;
            summary["name_conflict"] = sectionBoxViewpoint.NameConflict;
            return summary;
        }

        var zoomToSelection = response as ZoomToSelectionResponse;
        if (zoomToSelection != null)
        {
            summary["selected_item_count"] = zoomToSelection.SelectedItemCount;
            summary["zoom_applied"] = zoomToSelection.ZoomApplied;
            return summary;
        }

        var focusOnSelection = response as FocusOnSelectionResponse;
        if (focusOnSelection != null)
        {
            summary["selected_item_count"] = focusOnSelection.SelectedItemCount;
            summary["focus_applied"] = focusOnSelection.FocusApplied;
            return summary;
        }

        var fitAll = response as FitAllResponse;
        if (fitAll != null)
        {
            summary["fit_applied"] = fitAll.FitApplied;
            return summary;
        }

        return null;
    }

}
