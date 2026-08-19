using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;

namespace NavisHelper.Agent.Host
{
    internal sealed partial class AgentHostService
    {
        private CommandRouter CreateCommandRouter()
        {
            var router = new CommandRouter();

            router.Register<HostStatusRequest>(
                HostCommandNames.HostStatus,
                false,
                payloadToken => new HostStatusRequest(),
                (document, request) => BuildHostStatus(document));

            router.Register<FindRootItemsByNameRequest>(
            HostCommandNames.FindRootItemsByName,
            true,
            DeserializePayload<FindRootItemsByNameRequest>,
            (document, request) => _searchService.FindRootItemsByName(document, request, _matchSessionStore));

        router.Register<SelectBySearchRequest>(
            HostCommandNames.SelectBySearch,
            true,
            DeserializePayload<SelectBySearchRequest>,
            (document, request) => _searchService.SelectBySearch(document, request));

        router.Register<FindItemsByBboxRequest>(
            HostCommandNames.FindItemsByBbox,
            true,
            DeserializePayload<FindItemsByBboxRequest>,
            (document, request) => _searchService.FindItemsByBbox(document, request, _matchSessionStore));

        router.Register<ListRootItemsRequest>(
            HostCommandNames.ListRootItems,
            true,
            DeserializePayload<ListRootItemsRequest>,
            (document, request) => _searchService.ListRootItems(document, request));

        router.Register<ListItemChildrenRequest>(
            HostCommandNames.ListItemChildren,
            true,
            DeserializePayload<ListItemChildrenRequest>,
            (document, request) => _searchService.ListItemChildren(document, request, _matchSessionStore));

        router.Register<SelectionStatusRequest>(
            HostCommandNames.SelectionStatus,
            true,
            DeserializePayload<SelectionStatusRequest>,
            (document, request) => _commandService.SelectionStatus(document, request));

        router.Register<SelectionCopyNamesRequest>(
            HostCommandNames.SelectionCopyNames,
            true,
            DeserializePayload<SelectionCopyNamesRequest>,
            (document, request) => _commandService.SelectionCopyNames(document, request));

        router.Register<DumpSubtreeNamesRequest>(
            HostCommandNames.DumpSubtreeNames,
            true,
            DeserializePayload<DumpSubtreeNamesRequest>,
            (document, request) => _commandService.DumpSubtreeNames(document, request));

        router.Register<DumpSubtreeNamesRequest>(
            HostCommandNames.StartSubtreeNamesDump,
            true,
            DeserializePayload<DumpSubtreeNamesRequest>,
            (document, request) => AttachInstanceId(_commandService.StartSubtreeNamesDump(document, request)));

        router.Register<DumpSubtreeNamesStatusRequest>(
            HostCommandNames.DumpSubtreeNamesStatus,
            false,
            DeserializePayload<DumpSubtreeNamesStatusRequest>,
            (document, request) => AttachInstanceId(_commandService.DumpSubtreeNamesStatus(document, request)));

        router.Register<SelectionPropertyReportRequest>(
            HostCommandNames.SelectionPropertyReport,
            true,
            DeserializePayload<SelectionPropertyReportRequest>,
            (document, request) => _commandService.SelectionPropertyReport(document, request));

        router.Register<SelectionExportPropertiesRequest>(
            HostCommandNames.SelectionExportProperties,
            true,
            DeserializePayload<SelectionExportPropertiesRequest>,
            (document, request) => _commandService.SelectionExportProperties(document, request));

        router.Register<SelectionDistinctPropertyValuesRequest>(
            HostCommandNames.SelectionDistinctPropertyValues,
            true,
            DeserializePayload<SelectionDistinctPropertyValuesRequest>,
            (document, request) => _commandService.SelectionDistinctPropertyValues(document, request));

        router.Register<SelectionColorByPropertyRequest>(
            HostCommandNames.SelectionColorByProperty,
            true,
            DeserializePayload<SelectionColorByPropertyRequest>,
            (document, request) => _commandService.SelectionColorByProperty(document, request));

        router.Register<ModelColorSchemeRequest>(
            HostCommandNames.ModelColorScheme,
            true,
            DeserializePayload<ModelColorSchemeRequest>,
            (document, request) => _modelColorSchemeService.Execute(document, request));

        router.Register<ClashListTestsRequest>(
            HostCommandNames.ClashListTests,
            true,
            DeserializePayload<ClashListTestsRequest>,
            (document, request) => _commandService.ClashListTests(document, request));

        router.Register<ClashListResultsRequest>(
            HostCommandNames.ClashListResults,
            true,
            DeserializePayload<ClashListResultsRequest>,
            (document, request) => _commandService.ClashListResults(document, request));

        router.Register<ClashListClustersRequest>(
            HostCommandNames.ClashListClusters,
            true,
            DeserializePayload<ClashListClustersRequest>,
            (document, request) => _commandService.ClashListClusters(document, request));

        router.Register<ClashGroupResultsRequest>(
            HostCommandNames.ClashGroupResults,
            true,
            DeserializePayload<ClashGroupResultsRequest>,
            (document, request) => _commandService.ClashGroupResults(document, request));

        router.Register<ClashRootMatrixRequest>(
            HostCommandNames.ClashRootMatrix,
            true,
            DeserializePayload<ClashRootMatrixRequest>,
            (document, request) => new ClashRootMatrixService().Execute(document, request));

        router.Register<ClashGroupCustomRequest>(
            HostCommandNames.ClashGroupCustom,
            true,
            DeserializePayload<ClashGroupCustomRequest>,
            (document, request) => _commandService.ClashGroupCustom(document, request));

        router.Register<ClashUngroupRequest>(
            HostCommandNames.ClashUngroup,
            true,
            DeserializePayload<ClashUngroupRequest>,
            (document, request) => _commandService.ClashUngroup(document, request));

        router.Register<ClashSetStatusRequest>(
            HostCommandNames.ClashSetStatus,
            true,
            DeserializePayload<ClashSetStatusRequest>,
            (document, request) => _commandService.ClashSetStatus(document, request));

        router.Register<ClashGroupByProximityRequest>(
            HostCommandNames.ClashGroupByProximity,
            true,
            DeserializePayload<ClashGroupByProximityRequest>,
            (document, request) => _commandService.ClashGroupByProximity(document, request));

        router.Register<ClashIgnoreRulesRequest>(
            HostCommandNames.ClashIgnoreRules,
            true,
            DeserializePayload<ClashIgnoreRulesRequest>,
            (document, request) => _commandService.ClashIgnoreRules(document, request));

        router.Register<ClashExportPointsRequest>(
            HostCommandNames.ClashExportPoints,
            true,
            DeserializePayload<ClashExportPointsRequest>,
            (document, request) => _commandService.ClashExportPoints(document, request));

        router.Register<ClashRenumberResultsRequest>(
            HostCommandNames.ClashRenumberResults,
            true,
            DeserializePayload<ClashRenumberResultsRequest>,
            (document, request) => _commandService.ClashRenumberResults(document, request));

        router.Register<ClashGenerateReportRequest>(
            HostCommandNames.ClashGenerateReport,
            true,
            DeserializePayload<ClashGenerateReportRequest>,
            (document, request) => _commandService.ClashGenerateReport(document, request));

        router.Register<ClashSaveViewpointsRequest>(
            HostCommandNames.ClashSaveViewpoints,
            true,
            DeserializePayload<ClashSaveViewpointsRequest>,
            (document, request) => _commandService.ClashSaveViewpoints(document, request));

        router.Register<ClashIsolateResultRequest>(
            HostCommandNames.ClashIsolateResult,
            true,
            DeserializePayload<ClashIsolateResultRequest>,
            (document, request) => _clashIsolationService.Isolate(document, request));

        router.Register<ClashResetIsolationRequest>(
            HostCommandNames.ClashResetIsolation,
            true,
            DeserializePayload<ClashResetIsolationRequest>,
            (document, request) => _clashIsolationService.Reset(document, request));

        router.Register<CaptureCurrentViewRequest>(
            HostCommandNames.CaptureCurrentView,
            true,
            DeserializePayload<CaptureCurrentViewRequest>,
            (document, request) => _clashIsolationService.CaptureCurrentView(document, request));

        router.Register<ClashManageTestsRequest>(
            HostCommandNames.ClashManageTests,
            true,
            DeserializePayload<ClashManageTestsRequest>,
            (document, request) => _commandService.ClashManageTests(document, request));

        router.Register<ClashBboxPairPlanRequest>(
            HostCommandNames.ClashBboxPairPlan,
            true,
            DeserializePayload<ClashBboxPairPlanRequest>,
            (document, request) => _commandService.ClashBboxPairPlan(document, request));

        router.Register<ClashPairTestsCreateRequest>(
            HostCommandNames.ClashPairTestsCreate,
            true,
            DeserializePayload<ClashPairTestsCreateRequest>,
            (document, request) => _commandService.ClashPairTestsCreate(document, request));

        router.Register<ClashTestsFromSetsRequest>(
            HostCommandNames.ClashTestsFromSets,
            true,
            DeserializePayload<ClashTestsFromSetsRequest>,
            (document, request) => CreateClashTestsFromSets(document, request));

        RegisterClashTransferCommands(router);

        router.Register<ClashRunBatchRequest>(
            HostCommandNames.ClashRunBatch,
            true,
            DeserializePayload<ClashRunBatchRequest>,
            (document, request) => _clashBatchRunService.Start(document, request));

        router.Register<ClashRunResumeRequest>(
            HostCommandNames.ClashRunResume,
            false,
            DeserializePayload<ClashRunResumeRequest>,
            (document, request) => _clashBatchRunService.Resume(request));

        router.Register<ClashCreateMatrixFromSelectionRequest>(
            HostCommandNames.ClashCreateMatrixFromSelection,
            true,
            DeserializePayload<ClashCreateMatrixFromSelectionRequest>,
            (document, request) => _commandService.ClashCreateMatrixFromSelection(document, request));

        router.Register<SelectedItemsPreviewRequest>(
            HostCommandNames.SelectedItemsPreview,
            true,
            DeserializePayload<SelectedItemsPreviewRequest>,
            (document, request) => _commandService.SelectedItemsPreview(document, request));

        router.Register<SelectedItemsAncestryRequest>(
            HostCommandNames.SelectedItemsAncestry,
            true,
            DeserializePayload<SelectedItemsAncestryRequest>,
            (document, request) => _commandService.SelectedItemsAncestry(document, request));

        router.Register<SelectedItemsTreeRequest>(
            HostCommandNames.SelectedItemsTree,
            true,
            DeserializePayload<SelectedItemsTreeRequest>,
            (document, request) => _commandService.SelectedItemsTree(document, request));

        router.Register<ItemPropertiesByHandleRequest>(
            HostCommandNames.ItemPropertiesByHandle,
            true,
            DeserializePayload<ItemPropertiesByHandleRequest>,
            (document, request) => _commandService.ItemPropertiesByHandle(document, request, _matchSessionStore));

        router.Register<CurrentViewpointInfoRequest>(
            HostCommandNames.CurrentViewpointInfo,
            true,
            DeserializePayload<CurrentViewpointInfoRequest>,
            (document, request) => _commandService.CurrentViewpointInfo(document, request));

        router.Register<ListSavedViewpointsRequest>(
            HostCommandNames.ListSavedViewpoints,
            true,
            DeserializePayload<ListSavedViewpointsRequest>,
            (document, request) => _commandService.ListSavedViewpoints(document, request));

        router.Register<SavedViewpointsExportRequest>(
            HostCommandNames.SavedViewpointsExport,
            true,
            DeserializePayload<SavedViewpointsExportRequest>,
            (document, request) => _commandService.SavedViewpointsExport(document, request));

        router.Register<SavedViewpointsImportRequest>(
            HostCommandNames.SavedViewpointsImport,
            true,
            DeserializePayload<SavedViewpointsImportRequest>,
            (document, request) => _commandService.SavedViewpointsImport(document, request));

        router.Register<SavedViewpointsManageRequest>(
            HostCommandNames.SavedViewpointsManage,
            true,
            DeserializePayload<SavedViewpointsManageRequest>,
            (document, request) => _commandService.SavedViewpointsManage(document, request));

        router.Register<SavedViewpointsReorderRequest>(
            HostCommandNames.SavedViewpointsReorder,
            true,
            DeserializePayload<SavedViewpointsReorderRequest>,
            (document, request) => _commandService.SavedViewpointsReorder(document, request));

        router.Register<ListSelectionSetsRequest>(
            HostCommandNames.ListSelectionSets,
            true,
            DeserializePayload<ListSelectionSetsRequest>,
            (document, request) => _commandService.ListSelectionSets(document, request));

        router.Register<SelectSelectionSetRequest>(
            HostCommandNames.SelectSelectionSet,
            true,
            DeserializePayload<SelectSelectionSetRequest>,
            (document, request) => _commandService.SelectSelectionSet(document, request));

        router.Register<CreateSearchSetRequest>(
            HostCommandNames.CreateSearchSet,
            true,
            DeserializePayload<CreateSearchSetRequest>,
            (document, request) => _commandService.CreateSearchSet(document, request));

        router.Register<SelectionSetsManageRequest>(
            HostCommandNames.SelectionSetsManage,
            true,
            DeserializePayload<SelectionSetsManageRequest>,
            (document, request) => _commandService.SelectionSetsManage(document, request));

        router.Register<SelectionSetsReorderRequest>(
            HostCommandNames.SelectionSetsReorder,
            true,
            DeserializePayload<SelectionSetsReorderRequest>,
            (document, request) => _commandService.SelectionSetsReorder(document, request));

        router.Register<ActivateSavedViewpointRequest>(
            HostCommandNames.ActivateSavedViewpoint,
            true,
            DeserializePayload<ActivateSavedViewpointRequest>,
            (document, request) => _commandService.ActivateSavedViewpoint(document, request));

        router.Register<SelectItemsRequest>(
            HostCommandNames.SelectItems,
            true,
            DeserializePayload<SelectItemsRequest>,
            (document, request) => _commandService.SelectItems(document, request, _matchSessionStore));

        router.Register<HideUnselectedRequest>(
            HostCommandNames.HideUnselected,
            true,
            DeserializePayload<HideUnselectedRequest>,
            (document, request) => _commandService.HideUnselected(document, request));

        router.Register<HideSelectedRequest>(
            HostCommandNames.HideSelected,
            true,
            DeserializePayload<HideSelectedRequest>,
            (document, request) => _commandService.HideSelected(document, request));

        router.Register<UnhideSelectedRequest>(
            HostCommandNames.UnhideSelected,
            true,
            DeserializePayload<UnhideSelectedRequest>,
            (document, request) => _commandService.UnhideSelected(document, request));

        router.Register<RevealSelectedRequest>(
            HostCommandNames.RevealSelected,
            true,
            DeserializePayload<RevealSelectedRequest>,
            (document, request) => _commandService.RevealSelected(document, request));

        router.Register<IsolateSelectedRequest>(
            HostCommandNames.IsolateSelected,
            true,
            DeserializePayload<IsolateSelectedRequest>,
            (document, request) => _commandService.IsolateSelected(document, request));

        router.Register<ShowAllRequest>(
            HostCommandNames.ShowAll,
            true,
            DeserializePayload<ShowAllRequest>,
            (document, request) => _commandService.ShowAll(document, request));

        router.Register<CreateSelectionSetRequest>(
            HostCommandNames.CreateSelectionSet,
            true,
            DeserializePayload<CreateSelectionSetRequest>,
            (document, request) => _commandService.CreateSelectionSet(document, request, _matchSessionStore));

        router.Register<CreateViewpointRequest>(
            HostCommandNames.CreateViewpoint,
            true,
            DeserializePayload<CreateViewpointRequest>,
            (document, request) => _commandService.CreateViewpoint(document, request));

        router.Register<SaveDocumentRequest>(
            HostCommandNames.SaveDocument,
            true,
            DeserializePayload<SaveDocumentRequest>,
            (document, request) => _commandService.SaveDocument(document, request));

        router.Register<SaveDocumentAsRequest>(
            HostCommandNames.SaveDocumentAs,
            true,
            DeserializePayload<SaveDocumentAsRequest>,
            (document, request) => _commandService.SaveDocumentAs(document, request));

        router.Register<CloseNavisworksRequest>(
            HostCommandNames.CloseNavisworks,
            false,
            DeserializePayload<CloseNavisworksRequest>,
            (document, request) => _applicationCloseService.Prepare(document, request));

        router.Register<BuildMtrViewpointsRequest>(
            HostCommandNames.BuildMtrViewpoints,
            true,
            DeserializePayload<BuildMtrViewpointsRequest>,
            (document, request) => _commandService.BuildMtrViewpoints(document, request));

        router.Register<SelectionSetsBuildViewpointsRequest>(
            HostCommandNames.SelectionSetsBuildViewpoints,
            true,
            DeserializePayload<SelectionSetsBuildViewpointsRequest>,
            (document, request) => _commandService.SelectionSetsBuildViewpoints(document, request));

        router.Register<MarkupSelectionRequest>(
            HostCommandNames.MarkupSelection,
            true,
            DeserializePayload<MarkupSelectionRequest>,
            (document, request) => _commandService.MarkupSelection(document, request));

        router.Register<LiveMarkersRequest>(
            HostCommandNames.LiveMarkers,
            true,
            DeserializePayload<LiveMarkersRequest>,
            (document, request) => _commandService.LiveMarkers(document, request));

        router.Register<SectionBoxViewpointRequest>(
            HostCommandNames.SectionBoxViewpoint,
            true,
            DeserializePayload<SectionBoxViewpointRequest>,
            (document, request) => _commandService.SectionBoxViewpoint(document, request));

        router.Register<ZoomToSelectionRequest>(
            HostCommandNames.ZoomToSelection,
            true,
            DeserializePayload<ZoomToSelectionRequest>,
            (document, request) => _commandService.ZoomToSelection(document, request));

        router.Register<FocusOnSelectionRequest>(
            HostCommandNames.FocusOnSelection,
            true,
            DeserializePayload<FocusOnSelectionRequest>,
            (document, request) => _commandService.FocusOnSelection(document, request));

        router.Register<FitAllRequest>(
            HostCommandNames.FitAll,
            true,
            DeserializePayload<FitAllRequest>,
            (document, request) => _commandService.FitAll(document, request));

            return router;
        }
    }
}
