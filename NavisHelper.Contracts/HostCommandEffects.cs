using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public static class HostCommandEffects
    {
        // Explicit host-command table; approval follows same-name MCP Document, Files or Host effects.
        private static readonly Dictionary<string, bool> ApprovalTable =
            new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                { HostCommandNames.FindItems, false },
                { HostCommandNames.FindItemsByBbox, false },
                { HostCommandNames.FindRootItemsByName, false },
                { HostCommandNames.SelectBySearch, false },
                { HostCommandNames.ListRootItems, false },
                { HostCommandNames.ListItemChildren, false },
                { HostCommandNames.HostStatus, false },
                { HostCommandNames.LastOperationStatus, false },
                { HostCommandNames.SelectionStatus, false },
                { HostCommandNames.SelectionCopyNames, false },
                { HostCommandNames.DumpSubtreeNames, true },
                { HostCommandNames.StartSubtreeNamesDump, true },
                { HostCommandNames.DumpSubtreeNamesStatus, true },
                { HostCommandNames.CancelSubtreeNamesDump, true },
                { HostCommandNames.SelectionPropertyReport, false },
                { HostCommandNames.SelectionExportProperties, true },
                { HostCommandNames.SelectionDistinctPropertyValues, false },
                { HostCommandNames.SelectionColorByProperty, true },
                { HostCommandNames.ModelColorScheme, true },
                { HostCommandNames.AppearanceProfileApply, true },
                { HostCommandNames.ClashListTests, false },
                { HostCommandNames.ClashListResults, false },
                { HostCommandNames.ClashListClusters, false },
                { HostCommandNames.ClashGroupResults, true },
                { HostCommandNames.ClashRootMatrix, false },
                { HostCommandNames.ClashGroupCustom, true },
                { HostCommandNames.ClashUngroup, true },
                { HostCommandNames.ClashGroupRename, true },
                { HostCommandNames.ClashZonesExport, true },
                { HostCommandNames.ClashZonesMarkers, true },
                { HostCommandNames.ClashGroupByProximity, true },
                { HostCommandNames.ClashSetStatus, true },
                { HostCommandNames.ClashIgnoreRules, true },
                { HostCommandNames.ClashExportPoints, true },
                { HostCommandNames.ClashRenumberResults, true },
                { HostCommandNames.ClashGenerateReport, true },
                { HostCommandNames.ClashSaveViewpoints, true },
                { HostCommandNames.ClashIsolateResult, true },
                { HostCommandNames.ClashResetIsolation, false },
                { HostCommandNames.CaptureCurrentView, true },
                { HostCommandNames.ClashReportStatus, false },
                { HostCommandNames.CancelClashReport, false },
                { HostCommandNames.ClashManageTests, true },
                { HostCommandNames.ClashBboxPairPlan, true },
                { HostCommandNames.ClashPairTestsCreate, true },
                { HostCommandNames.ClashCreateMatrixFromSelection, true },
                { HostCommandNames.ClashTestsFromSets, true },
                { HostCommandNames.ClashOneVsRest, true },
                { HostCommandNames.ClashTestsExport, true },
                { HostCommandNames.ClashBatchtestImport, true },
                { HostCommandNames.ClashRunBatch, true },
                { HostCommandNames.ClashRunResume, true },
                { HostCommandNames.ClashRunStatus, false },
                { HostCommandNames.CancelClashRun, false },
                { HostCommandNames.SelectedItemsPreview, false },
                { HostCommandNames.SelectedItemsAncestry, false },
                { HostCommandNames.SelectedItemsTree, false },
                { HostCommandNames.ItemPropertiesByHandle, false },
                { HostCommandNames.MatchHandleItems, false },
                { HostCommandNames.CurrentViewpointInfo, false },
                { HostCommandNames.ListSavedViewpoints, false },
                { HostCommandNames.SavedViewpointsExport, true },
                { HostCommandNames.SavedViewpointsImport, true },
                { HostCommandNames.SavedViewpointsManage, true },
                { HostCommandNames.SavedViewpointsReorder, true },
                { HostCommandNames.ListSelectionSets, false },
                { HostCommandNames.SelectSelectionSet, false },
                { HostCommandNames.CreateSearchSet, true },
                { HostCommandNames.SelectionSetsManage, true },
                { HostCommandNames.SelectionSetsReorder, true },
                { HostCommandNames.ActivateSavedViewpoint, false },
                { HostCommandNames.SelectItems, false },
                { HostCommandNames.HideUnselected, false },
                { HostCommandNames.HideSelected, false },
                { HostCommandNames.UnhideSelected, false },
                { HostCommandNames.RevealSelected, false },
                { HostCommandNames.IsolateSelected, false },
                { HostCommandNames.GetCurrentSectionBox, false },
                { HostCommandNames.IsolateByBox, false },
                { HostCommandNames.ShowAll, false },
                { HostCommandNames.CreateSelectionSet, true },
                { HostCommandNames.CreateViewpoint, true },
                { HostCommandNames.ViewpointSetCamera, true },
                { HostCommandNames.GetViewDisplaySettings, false },
                { HostCommandNames.SetViewDisplaySettings, true },
                { HostCommandNames.SaveDocument, true },
                { HostCommandNames.SaveDocumentAs, true },
                { HostCommandNames.StartSaveDocument, true },
                { HostCommandNames.SaveDocumentStatus, false },
                { HostCommandNames.CloseNavisworks, true },
                { HostCommandNames.BuildMtrViewpoints, true },
                { HostCommandNames.SelectionSetsBuildViewpoints, true },
                { HostCommandNames.MarkupSelection, true },
                { HostCommandNames.LiveMarkers, false },
                { HostCommandNames.SectionBoxViewpoint, true },
                { HostCommandNames.ZoomToSelection, false },
                { HostCommandNames.FocusOnSelection, false },
                { HostCommandNames.FitAll, false },
                { HostCommandNames.WorldMarkersSet, false },
                { HostCommandNames.WorldMarkersManage, false },
                { HostCommandNames.WorldMarkersList, false },
            };

        public static bool IsKnown(string command)
        {
            return command != null && ApprovalTable.ContainsKey(command);
        }

        public static bool RequiresApproval(string command)
        {
            bool required;
            return command == null || !ApprovalTable.TryGetValue(command, out required) || required;
        }
    }
}
