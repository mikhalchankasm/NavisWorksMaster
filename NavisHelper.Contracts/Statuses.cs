namespace NavisHelper.Agent.Contracts
{
    public static class FindItemStatuses
    {
        public const string Matched = "matched";
        public const string NotFound = "not_found";
        // Reserved for search v2 exact/variant matching semantics.
        public const string Ambiguous = "ambiguous";
        // Reserved for search v2 exact/variant matching semantics.
        public const string QueryTooAmbiguous = "query_too_ambiguous";
    }

    public static class SelectHandleStatuses
    {
        public const string Selected = "selected";
        public const string Stale = "stale";
    }

    public static class HostCommandNames
    {
        public const string FindItems = "find_items";
        public const string FindItemsByBbox = "find_items_by_bbox";
        public const string FindRootItemsByName = "find_root_items_by_name";
        public const string SelectBySearch = "select_by_search";
        public const string ListRootItems = "list_root_items";
        public const string ListItemChildren = "list_item_children";
        public const string HostStatus = "host_status";
        public const string LastOperationStatus = "last_operation_status";
        public const string SelectionStatus = "selection_status";
        public const string SelectionCopyNames = "selection_copy_names";
        public const string DumpSubtreeNames = "dump_subtree_names";
        public const string StartSubtreeNamesDump = "start_subtree_names_dump";
        public const string DumpSubtreeNamesStatus = "dump_subtree_names_status";
        public const string CancelSubtreeNamesDump = "cancel_subtree_names_dump";
        public const string SelectionPropertyReport = "selection_property_report";
        public const string SelectionExportProperties = "selection_export_properties";
        public const string SelectionDistinctPropertyValues = "selection_distinct_property_values";
        public const string SelectionColorByProperty = "selection_color_by_property";
        public const string ModelColorScheme = "model_color_scheme";
        public const string ClashListTests = "clash_list_tests";
        public const string ClashListResults = "clash_list_results";
        public const string ClashListClusters = "clash_list_clusters";
        public const string ClashGroupResults = "clash_group_results";
        public const string ClashRootMatrix = "clash_root_matrix";
        public const string ClashGroupCustom = "clash_group_custom";
        public const string ClashUngroup = "clash_ungroup";
        public const string ClashGroupByProximity = "clash_group_by_proximity";
        public const string ClashSetStatus = "clash_set_status";
        public const string ClashIgnoreRules = "clash_ignore_rules";
        public const string ClashExportPoints = "clash_export_points";
        public const string ClashRenumberResults = "clash_renumber_results";
        public const string ClashGenerateReport = "clash_generate_report";
        public const string ClashSaveViewpoints = "clash_save_viewpoints";
        public const string ClashIsolateResult = "clash_isolate_result";
        public const string ClashResetIsolation = "clash_reset_isolation";
        public const string CaptureCurrentView = "capture_current_view";
        public const string ClashReportStatus = "clash_report_status";
        public const string CancelClashReport = "cancel_clash_report";
        public const string ClashManageTests = "clash_manage_tests";
        public const string ClashBboxPairPlan = "clash_bbox_pair_plan";
        public const string ClashPairTestsCreate = "clash_pair_tests_create";
        public const string ClashCreateMatrixFromSelection = "clash_create_matrix_from_selection";
        public const string ClashTestsFromSets = "clash_tests_from_sets";
        public const string ClashTestsExport = "clash_tests_export";
        public const string ClashBatchtestImport = "clash_batchtest_import";
        public const string ClashRunBatch = "clash_run_batch";
        public const string ClashRunResume = "clash_run_resume";
        public const string ClashRunStatus = "clash_run_status";
        public const string CancelClashRun = "cancel_clash_run";
        public const string SelectedItemsPreview = "selected_items_preview";
        public const string SelectedItemsAncestry = "selected_items_ancestry";
        public const string SelectedItemsTree = "selected_items_tree";
        public const string ItemPropertiesByHandle = "item_properties_by_handle";
        public const string CurrentViewpointInfo = "current_viewpoint_info";
        public const string ListSavedViewpoints = "list_saved_viewpoints";
        public const string SavedViewpointsExport = "saved_viewpoints_export";
        public const string SavedViewpointsImport = "saved_viewpoints_import";
        public const string SavedViewpointsManage = "saved_viewpoints_manage";
        public const string SavedViewpointsReorder = "saved_viewpoints_reorder";
        public const string ListSelectionSets = "list_selection_sets";
        public const string SelectSelectionSet = "select_selection_set";
        public const string CreateSearchSet = "create_search_set";
        public const string SelectionSetsManage = "selection_sets_manage";
        public const string SelectionSetsReorder = "selection_sets_reorder";
        public const string ActivateSavedViewpoint = "activate_saved_viewpoint";
        public const string SelectItems = "select_items";
        public const string HideUnselected = "hide_unselected";
        public const string HideSelected = "hide_selected";
        public const string UnhideSelected = "unhide_selected";
        public const string RevealSelected = "reveal_selected";
        public const string IsolateSelected = "isolate_selected";
        public const string ShowAll = "show_all";
        public const string CreateSelectionSet = "create_selection_set";
        public const string CreateViewpoint = "create_viewpoint";
        public const string SaveDocument = "save_document";
        public const string SaveDocumentAs = "save_document_as";
        public const string CloseNavisworks = "close_navisworks";
        public const string BuildMtrViewpoints = "build_mtr_viewpoints";
        public const string SelectionSetsBuildViewpoints = "selection_sets_build_viewpoints";
        public const string MarkupSelection = "markup_selection";
        public const string LiveMarkers = "live_markers";
        public const string SectionBoxViewpoint = "section_box_viewpoint";
        public const string ZoomToSelection = "zoom_to_selection";
        public const string FocusOnSelection = "focus_on_selection";
        public const string FitAll = "fit_all";
    }

    public static class NavisworksCloseModes
    {
        public const string Prompt = "prompt";
        public const string Save = "save";
        public const string Discard = "discard";
    }

    public static class DumpSubtreeNamesJobStates
    {
        public const string Running = "running";
        public const string Done = "done";
        public const string Failed = "failed";
        public const string Cancelled = "cancelled";
    }

    public static class FindItemsComparisons
    {
        public const string Equal = "equals";
        public const string NotEquals = "not_equals";
        public const string Contains = "contains";
        public const string StartsWith = "starts_with";
        public const string EndsWith = "ends_with";
        public const string Wildcard = "wildcard";
        public const string Defined = "defined";
        public const string NotDefined = "not_defined";
    }

    public static class FindItemsCombineOperators
    {
        public const string All = "all";
        public const string Any = "any";
    }

    public static class SelectionSetViewpointStepKinds
    {
        public const string Overview = "overview";
        public const string Markup = "markup";
        public const string SectionBox = "sectionbox";
    }

    public static class SelectionClusterModes
    {
        public const string None = "none";
        public const string Distance = "distance";
        public const string Count = "count";
        public const string Grid = "grid";
    }
}
