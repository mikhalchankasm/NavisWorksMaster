using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.McpServer.Services;

internal sealed partial class HostBridgeClient
{
    public Task<FindItemsResponse> FindItemsAsync(FindItemsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<FindItemsResponse>(HostCommandNames.FindItems, request, cancellationToken, target);
    }

    public Task<SelectBySearchResponse> SelectBySearchAsync(SelectBySearchRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SelectBySearchResponse>(HostCommandNames.SelectBySearch, request, cancellationToken, target);
    }

    public Task<FindItemsResponse> FindRootItemsByNameAsync(FindRootItemsByNameRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<FindItemsResponse>(HostCommandNames.FindRootItemsByName, request, cancellationToken, target);
    }

    public Task<FindItemsByBboxResponse> FindItemsByBboxAsync(FindItemsByBboxRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<FindItemsByBboxResponse>(HostCommandNames.FindItemsByBbox, request, cancellationToken, target);
    }

    public Task<ListRootItemsResponse> ListRootItemsAsync(ListRootItemsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ListRootItemsResponse>(HostCommandNames.ListRootItems, request, cancellationToken, target);
    }

    public Task<ListItemChildrenResponse> ListItemChildrenAsync(ListItemChildrenRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ListItemChildrenResponse>(HostCommandNames.ListItemChildren, request, cancellationToken, target);
    }

    public Task<HostStatusResponse> HostStatusAsync(HostStatusRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<HostStatusResponse>(HostCommandNames.HostStatus, request, cancellationToken, target);
    }

    public Task<LastOperationStatusResponse> LastOperationStatusAsync(LastOperationStatusRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<LastOperationStatusResponse>(HostCommandNames.LastOperationStatus, request, cancellationToken, target);
    }

    public Task<SelectionStatusResponse> SelectionStatusAsync(SelectionStatusRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SelectionStatusResponse>(HostCommandNames.SelectionStatus, request, cancellationToken, target);
    }

    public Task<SelectionCopyNamesResponse> SelectionCopyNamesAsync(SelectionCopyNamesRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SelectionCopyNamesResponse>(HostCommandNames.SelectionCopyNames, request, cancellationToken, target);
    }

    public Task<DumpSubtreeNamesResponse> DumpSubtreeNamesAsync(DumpSubtreeNamesRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<DumpSubtreeNamesResponse>(HostCommandNames.DumpSubtreeNames, request, cancellationToken, target, 35000);
    }

    public Task<DumpSubtreeNamesJobStatusResponse> StartSubtreeNamesDumpAsync(DumpSubtreeNamesRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<DumpSubtreeNamesJobStatusResponse>(HostCommandNames.StartSubtreeNamesDump, request, cancellationToken, target);
    }

    public Task<DumpSubtreeNamesJobStatusResponse> DumpSubtreeNamesStatusAsync(DumpSubtreeNamesStatusRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<DumpSubtreeNamesJobStatusResponse>(HostCommandNames.DumpSubtreeNamesStatus, request, cancellationToken, target);
    }

    public Task<DumpSubtreeNamesJobStatusResponse> CancelSubtreeNamesDumpAsync(CancelSubtreeNamesDumpRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<DumpSubtreeNamesJobStatusResponse>(HostCommandNames.CancelSubtreeNamesDump, request, cancellationToken, target);
    }

    public Task<SelectionPropertyReportResponse> SelectionPropertyReportAsync(SelectionPropertyReportRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SelectionPropertyReportResponse>(HostCommandNames.SelectionPropertyReport, request, cancellationToken, target);
    }

    public Task<SelectionExportPropertiesResponse> SelectionExportPropertiesAsync(SelectionExportPropertiesRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SelectionExportPropertiesResponse>(HostCommandNames.SelectionExportProperties, request, cancellationToken, target);
    }

    public Task<SelectionDistinctPropertyValuesResponse> SelectionDistinctPropertyValuesAsync(SelectionDistinctPropertyValuesRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SelectionDistinctPropertyValuesResponse>(HostCommandNames.SelectionDistinctPropertyValues, request, cancellationToken, target);
    }

    public Task<SelectionColorByPropertyResponse> SelectionColorByPropertyAsync(SelectionColorByPropertyRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SelectionColorByPropertyResponse>(HostCommandNames.SelectionColorByProperty, request, cancellationToken, target);
    }

    public Task<ModelColorSchemeResponse> ModelColorSchemeAsync(ModelColorSchemeRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ModelColorSchemeResponse>(HostCommandNames.ModelColorScheme, request, cancellationToken, target);
    }

    public Task<ClashListTestsResponse> ClashListTestsAsync(ClashListTestsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashListTestsResponse>(HostCommandNames.ClashListTests, request, cancellationToken, target);
    }

    public Task<ClashListResultsResponse> ClashListResultsAsync(ClashListResultsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashListResultsResponse>(HostCommandNames.ClashListResults, request, cancellationToken, target);
    }

    public Task<ClashListClustersResponse> ClashListClustersAsync(ClashListClustersRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashListClustersResponse>(HostCommandNames.ClashListClusters, request, cancellationToken, target, 600000);
    }

    public Task<ClashGroupResultsResponse> ClashGroupResultsAsync(ClashGroupResultsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashGroupResultsResponse>(HostCommandNames.ClashGroupResults, request, cancellationToken, target, 600000);
    }

    public Task<ClashRootMatrixResponse> ClashRootMatrixAsync(ClashRootMatrixRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashRootMatrixResponse>(HostCommandNames.ClashRootMatrix, request, cancellationToken, target, 600000);
    }

    public Task<ClashGroupCustomResponse> ClashGroupCustomAsync(ClashGroupCustomRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashGroupCustomResponse>(HostCommandNames.ClashGroupCustom, request, cancellationToken, target, 600000);
    }

    public Task<ClashUngroupResponse> ClashUngroupAsync(ClashUngroupRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashUngroupResponse>(HostCommandNames.ClashUngroup, request, cancellationToken, target, 600000);
    }

    public Task<ClashSetStatusResponse> ClashSetStatusAsync(ClashSetStatusRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashSetStatusResponse>(HostCommandNames.ClashSetStatus, request, cancellationToken, target, 600000);
    }

    public Task<ClashGroupByProximityResponse> ClashGroupByProximityAsync(ClashGroupByProximityRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashGroupByProximityResponse>(HostCommandNames.ClashGroupByProximity, request, cancellationToken, target, 600000);
    }

    public Task<ClashIgnoreRulesResponse> ClashIgnoreRulesAsync(ClashIgnoreRulesRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashIgnoreRulesResponse>(HostCommandNames.ClashIgnoreRules, request, cancellationToken, target, 600000);
    }

    public Task<ClashExportPointsResponse> ClashExportPointsAsync(ClashExportPointsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashExportPointsResponse>(HostCommandNames.ClashExportPoints, request, cancellationToken, target, 600000);
    }

    public Task<ClashRenumberResultsResponse> ClashRenumberResultsAsync(ClashRenumberResultsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashRenumberResultsResponse>(HostCommandNames.ClashRenumberResults, request, cancellationToken, target, 600000);
    }

    public Task<ClashGenerateReportResponse> ClashGenerateReportAsync(ClashGenerateReportRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashGenerateReportResponse>(HostCommandNames.ClashGenerateReport, request, cancellationToken, target, 600000);
    }

    public Task<ClashSaveViewpointsResponse> ClashSaveViewpointsAsync(ClashSaveViewpointsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashSaveViewpointsResponse>(HostCommandNames.ClashSaveViewpoints, request, cancellationToken, target, 600000);
    }

    public Task<ClashIsolateResultResponse> ClashIsolateResultAsync(ClashIsolateResultRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashIsolateResultResponse>(HostCommandNames.ClashIsolateResult, request, cancellationToken, target);
    }

    public Task<ClashResetIsolationResponse> ClashResetIsolationAsync(ClashResetIsolationRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashResetIsolationResponse>(HostCommandNames.ClashResetIsolation, request, cancellationToken, target);
    }

    public Task<CaptureCurrentViewResponse> CaptureCurrentViewAsync(CaptureCurrentViewRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<CaptureCurrentViewResponse>(HostCommandNames.CaptureCurrentView, request, cancellationToken, target);
    }

    public Task<ClashReportStatusResponse> ClashReportStatusAsync(ClashReportStatusRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashReportStatusResponse>(HostCommandNames.ClashReportStatus, request, cancellationToken, target);
    }

    public Task<ClashReportStatusResponse> CancelClashReportAsync(CancelClashReportRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashReportStatusResponse>(HostCommandNames.CancelClashReport, request, cancellationToken, target);
    }

    public Task<ClashManageTestsResponse> ClashManageTestsAsync(ClashManageTestsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashManageTestsResponse>(HostCommandNames.ClashManageTests, request, cancellationToken, target, 600000);
    }

    public Task<ClashBboxPairPlanResponse> ClashBboxPairPlanAsync(ClashBboxPairPlanRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashBboxPairPlanResponse>(HostCommandNames.ClashBboxPairPlan, request, cancellationToken, target, 600000);
    }

    public Task<ClashPairTestsCreateResponse> ClashPairTestsCreateAsync(ClashPairTestsCreateRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashPairTestsCreateResponse>(HostCommandNames.ClashPairTestsCreate, request, cancellationToken, target, 600000);
    }

    public Task<ClashTestsFromSetsResponse> ClashTestsFromSetsAsync(ClashTestsFromSetsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashTestsFromSetsResponse>(HostCommandNames.ClashTestsFromSets, request, cancellationToken, target, 600000);
    }

    public Task<ClashTestsExportResponse> ClashTestsExportAsync(ClashTestsExportRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashTestsExportResponse>(HostCommandNames.ClashTestsExport, request, cancellationToken, target, 600000);
    }

    public Task<ClashBatchtestImportResponse> ClashBatchtestImportAsync(ClashBatchtestImportRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashBatchtestImportResponse>(HostCommandNames.ClashBatchtestImport, request, cancellationToken, target, 600000);
    }

    public Task<ClashRunBatchResponse> ClashRunBatchAsync(ClashRunBatchRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashRunBatchResponse>(HostCommandNames.ClashRunBatch, request, cancellationToken, target);
    }

    public Task<ClashRunBatchResponse> ClashRunResumeAsync(ClashRunResumeRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashRunBatchResponse>(HostCommandNames.ClashRunResume, request, cancellationToken, target);
    }

    public async Task<ClashRunBatchResponse> ClashRunStatusAsync(ClashRunStatusRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        var requestedWaitSeconds = request?.WaitSeconds ?? 0;
        var waitSeconds = Math.Max(0, Math.Min(300, request?.WaitForCompletion == true && requestedWaitSeconds == 0 ? 30 : requestedWaitSeconds));
        var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
        ClashRunBatchResponse response;
        do
        {
            response = await CallHostAsync<ClashRunBatchResponse>(HostCommandNames.ClashRunStatus, request, cancellationToken, target).ConfigureAwait(false);
            if (waitSeconds == 0 || response == null || !response.IsRunning || request?.WaitForCompletion != true || DateTime.UtcNow >= deadline)
                return response;
            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
        while (true);
    }

    public Task<ClashRunBatchResponse> CancelClashRunAsync(CancelClashRunRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashRunBatchResponse>(HostCommandNames.CancelClashRun, request, cancellationToken, target);
    }

    public Task<ClashCreateMatrixFromSelectionResponse> ClashCreateMatrixFromSelectionAsync(ClashCreateMatrixFromSelectionRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ClashCreateMatrixFromSelectionResponse>(HostCommandNames.ClashCreateMatrixFromSelection, request, cancellationToken, target, 600000);
    }

    public Task<SelectedItemsPreviewResponse> SelectedItemsPreviewAsync(SelectedItemsPreviewRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SelectedItemsPreviewResponse>(HostCommandNames.SelectedItemsPreview, request, cancellationToken, target);
    }

    public Task<SelectedItemsAncestryResponse> SelectedItemsAncestryAsync(SelectedItemsAncestryRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SelectedItemsAncestryResponse>(HostCommandNames.SelectedItemsAncestry, request, cancellationToken, target);
    }

    public Task<SelectedItemsTreeResponse> SelectedItemsTreeAsync(SelectedItemsTreeRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SelectedItemsTreeResponse>(HostCommandNames.SelectedItemsTree, request, cancellationToken, target);
    }

    public Task<ItemPropertiesByHandleResponse> ItemPropertiesByHandleAsync(ItemPropertiesByHandleRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ItemPropertiesByHandleResponse>(HostCommandNames.ItemPropertiesByHandle, request, cancellationToken, target);
    }

    public Task<CurrentViewpointInfoResponse> CurrentViewpointInfoAsync(CurrentViewpointInfoRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<CurrentViewpointInfoResponse>(HostCommandNames.CurrentViewpointInfo, request, cancellationToken, target);
    }

    public Task<ListSavedViewpointsResponse> ListSavedViewpointsAsync(ListSavedViewpointsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ListSavedViewpointsResponse>(HostCommandNames.ListSavedViewpoints, request, cancellationToken, target);
    }

    public Task<SavedViewpointsExportResponse> SavedViewpointsExportAsync(SavedViewpointsExportRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SavedViewpointsExportResponse>(HostCommandNames.SavedViewpointsExport, request, cancellationToken, target);
    }

    public Task<SavedViewpointsImportResponse> SavedViewpointsImportAsync(SavedViewpointsImportRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SavedViewpointsImportResponse>(HostCommandNames.SavedViewpointsImport, request, cancellationToken, target, 600000);
    }

    public Task<SavedViewpointsManageResponse> SavedViewpointsManageAsync(SavedViewpointsManageRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SavedViewpointsManageResponse>(HostCommandNames.SavedViewpointsManage, request, cancellationToken, target);
    }

    public Task<SavedViewpointsReorderResponse> SavedViewpointsReorderAsync(SavedViewpointsReorderRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SavedViewpointsReorderResponse>(HostCommandNames.SavedViewpointsReorder, request, cancellationToken, target);
    }

    public Task<ListSelectionSetsResponse> ListSelectionSetsAsync(ListSelectionSetsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ListSelectionSetsResponse>(HostCommandNames.ListSelectionSets, request, cancellationToken, target);
    }

    public Task<SelectSelectionSetResponse> SelectSelectionSetAsync(SelectSelectionSetRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SelectSelectionSetResponse>(HostCommandNames.SelectSelectionSet, request, cancellationToken, target);
    }

    public Task<CreateSearchSetResponse> CreateSearchSetAsync(CreateSearchSetRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<CreateSearchSetResponse>(HostCommandNames.CreateSearchSet, request, cancellationToken, target);
    }

    public Task<SelectionSetsManageResponse> SelectionSetsManageAsync(SelectionSetsManageRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SelectionSetsManageResponse>(HostCommandNames.SelectionSetsManage, request, cancellationToken, target);
    }

    public Task<SelectionSetsReorderResponse> SelectionSetsReorderAsync(SelectionSetsReorderRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SelectionSetsReorderResponse>(HostCommandNames.SelectionSetsReorder, request, cancellationToken, target);
    }

    public Task<ActivateSavedViewpointResponse> ActivateSavedViewpointAsync(ActivateSavedViewpointRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ActivateSavedViewpointResponse>(HostCommandNames.ActivateSavedViewpoint, request, cancellationToken, target);
    }

    public Task<SelectItemsResponse> SelectItemsAsync(SelectItemsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SelectItemsResponse>(HostCommandNames.SelectItems, request, cancellationToken, target);
    }

    public Task<HideUnselectedResponse> HideUnselectedAsync(HideUnselectedRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<HideUnselectedResponse>(HostCommandNames.HideUnselected, request, cancellationToken, target);
    }

    public Task<HideSelectedResponse> HideSelectedAsync(HideSelectedRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<HideSelectedResponse>(HostCommandNames.HideSelected, request, cancellationToken, target);
    }

    public Task<UnhideSelectedResponse> UnhideSelectedAsync(UnhideSelectedRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<UnhideSelectedResponse>(HostCommandNames.UnhideSelected, request, cancellationToken, target);
    }

    public Task<RevealSelectedResponse> RevealSelectedAsync(RevealSelectedRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<RevealSelectedResponse>(HostCommandNames.RevealSelected, request, cancellationToken, target);
    }

    public Task<IsolateSelectedResponse> IsolateSelectedAsync(IsolateSelectedRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<IsolateSelectedResponse>(HostCommandNames.IsolateSelected, request, cancellationToken, target);
    }

    public Task<ShowAllResponse> ShowAllAsync(ShowAllRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ShowAllResponse>(HostCommandNames.ShowAll, request, cancellationToken, target);
    }

    public Task<CreateSelectionSetResponse> CreateSelectionSetAsync(CreateSelectionSetRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<CreateSelectionSetResponse>(HostCommandNames.CreateSelectionSet, request, cancellationToken, target);
    }

    public Task<CreateViewpointResponse> CreateViewpointAsync(CreateViewpointRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<CreateViewpointResponse>(HostCommandNames.CreateViewpoint, request, cancellationToken, target);
    }

    public Task<SaveDocumentResponse> SaveDocumentAsync(SaveDocumentRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SaveDocumentResponse>(HostCommandNames.SaveDocument, request, cancellationToken, target, 600000);
    }

    public Task<SaveDocumentResponse> SaveDocumentAsAsync(SaveDocumentAsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SaveDocumentResponse>(HostCommandNames.SaveDocumentAs, request, cancellationToken, target, 600000);
    }

    public Task<CloseNavisworksResponse> CloseNavisworksAsync(CloseNavisworksRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<CloseNavisworksResponse>(HostCommandNames.CloseNavisworks, request, cancellationToken, target, 600000);
    }

    public Task<BuildMtrViewpointsResponse> BuildMtrViewpointsAsync(BuildMtrViewpointsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<BuildMtrViewpointsResponse>(HostCommandNames.BuildMtrViewpoints, request, cancellationToken, target, 600000);
    }

    public Task<SelectionSetsBuildViewpointsResponse> SelectionSetsBuildViewpointsAsync(SelectionSetsBuildViewpointsRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SelectionSetsBuildViewpointsResponse>(HostCommandNames.SelectionSetsBuildViewpoints, request, cancellationToken, target, 600000);
    }

    public Task<MarkupSelectionResponse> MarkupSelectionAsync(MarkupSelectionRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<MarkupSelectionResponse>(HostCommandNames.MarkupSelection, request, cancellationToken, target);
    }

    public Task<LiveMarkersResponse> LiveMarkersAsync(LiveMarkersRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<LiveMarkersResponse>(HostCommandNames.LiveMarkers, request, cancellationToken, target);
    }

    public Task<SectionBoxViewpointResponse> SectionBoxViewpointAsync(SectionBoxViewpointRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<SectionBoxViewpointResponse>(HostCommandNames.SectionBoxViewpoint, request, cancellationToken, target);
    }

    public Task<ZoomToSelectionResponse> ZoomToSelectionAsync(ZoomToSelectionRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<ZoomToSelectionResponse>(HostCommandNames.ZoomToSelection, request, cancellationToken, target);
    }

    public Task<FocusOnSelectionResponse> FocusOnSelectionAsync(FocusOnSelectionRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<FocusOnSelectionResponse>(HostCommandNames.FocusOnSelection, request, cancellationToken, target);
    }

    public Task<FitAllResponse> FitAllAsync(FitAllRequest request, CancellationToken cancellationToken, HostTargetOptions target = null)
    {
        return CallHostAsync<FitAllResponse>(HostCommandNames.FitAll, request, cancellationToken, target);
    }
}
