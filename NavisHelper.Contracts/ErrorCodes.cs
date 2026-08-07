namespace NavisHelper.Agent.Contracts
{
    public static class ErrorCodes
    {
        public const string NoActiveDocument = "no_active_document";
        public const string NoActiveView = "no_active_view";
        public const string HostUiContextUnavailable = "host_ui_context_unavailable";
        public const string MultipleHostsDetected = "multiple_hosts_detected";
        public const string InstanceNotFound = "instance_not_found";
        public const string HostBusy = "host_busy";
        public const string InteractiveBusy = "interactive_busy";
        public const string StaleMatchReference = "stale_match_reference";
        public const string SchemaViolation = "schema_violation";
        // Reserved for search v2 exact/variant matching semantics.
        public const string QueryTooAmbiguous = "query_too_ambiguous";
        public const string EmptyMatchHandles = "empty_match_handles";
        public const string NoSelection = "no_selection";
        public const string SectionBoxNotEnabled = "section_box_not_enabled";
        public const string SectionBoxModeUnsupported = "section_box_mode_unsupported";
        public const string SectionBoxPayloadUnsupported = "section_box_payload_unsupported";
        public const string SectionBoxUnitsMismatch = "section_box_units_mismatch";
        public const string SelectionSetNotFound = "selection_set_not_found";
        public const string SavedViewpointNotFound = "saved_viewpoint_not_found";
        public const string SavedItemAmbiguous = "saved_item_ambiguous";
        public const string SelectionSetNameConflict = "selection_set_name_conflict";
        public const string ViewpointNameConflict = "viewpoint_name_conflict";
        // Reserved for future request de-duplication if client retries become stateful.
        public const string DuplicateRequestId = "duplicate_request_id";
        public const string RequestTimeout = "request_timeout";
        public const string TransportConnectFailed = "transport_connect_failed";
        public const string CommandFailed = "command_failed";
        public const string ArtifactWriteFailed = "artifact_write_failed";
        public const string ClashTransferXmlMalformed = "clash_transfer_xml_malformed";
        public const string ClashTransferXmlUnsafe = "clash_transfer_xml_unsafe";
        public const string ClashTransferSchemaUnsupported = "clash_transfer_schema_unsupported";
    }
}
