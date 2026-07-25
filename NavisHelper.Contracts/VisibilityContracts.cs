using System;
using System.Collections.Generic;

namespace NavisHelper.Agent.Contracts
{
    public sealed class SelectItemsRequest
    {
        public List<string> MatchHandles { get; set; } = new List<string>();
    }

    public sealed class SelectItemsResponse
    {
        public bool Partial { get; set; }
        public List<SelectItemsHandleResult> Results { get; set; } = new List<SelectItemsHandleResult>();
        public int SelectedHandleCount { get; set; }
        public int SelectedItemCount { get; set; }
    }

    public sealed class SelectItemsHandleResult
    {
        public string MatchHandle { get; set; }
        public string Status { get; set; }
        public int SelectedItemCount { get; set; }
    }

    public sealed class HideUnselectedRequest
    {
        public bool? Apply { get; set; }
        public int? PreviewLimit { get; set; }
    }

    public sealed class HideUnselectedResponse
    {
        public bool Apply { get; set; }
        public int SelectedItemCount { get; set; }
        public int WouldHideItemCount { get; set; }
        public int WouldKeepVisibleItemCount { get; set; }
        public int? HiddenItemCount { get; set; }
        public int AffectedRootCount { get; set; }
        public bool AffectedRootSummariesTruncated { get; set; }
        public List<VisibilityRootSummary> AffectedRootSummaries { get; set; } = new List<VisibilityRootSummary>();
        public bool AffectedItemsPreviewTruncated { get; set; }
        public List<VisibilityPreviewItem> AffectedItemsPreview { get; set; } = new List<VisibilityPreviewItem>();
    }

    public sealed class HideSelectedRequest
    {
        public bool? Apply { get; set; }
        public int? PreviewLimit { get; set; }
    }

    public sealed class HideSelectedResponse
    {
        public bool Apply { get; set; }
        public int SelectedItemCount { get; set; }
        public int WouldHideItemCount { get; set; }
        public int? HiddenItemCount { get; set; }
        public int AffectedRootCount { get; set; }
        public bool AffectedRootSummariesTruncated { get; set; }
        public List<VisibilityRootSummary> AffectedRootSummaries { get; set; } = new List<VisibilityRootSummary>();
        public bool AffectedItemsPreviewTruncated { get; set; }
        public List<VisibilityPreviewItem> AffectedItemsPreview { get; set; } = new List<VisibilityPreviewItem>();
    }

    public sealed class UnhideSelectedRequest
    {
        public bool? Apply { get; set; }
        public int? PreviewLimit { get; set; }
    }

    public sealed class UnhideSelectedResponse
    {
        public bool Apply { get; set; }
        public int SelectedItemCount { get; set; }
        public int WouldRevealItemCount { get; set; }
        public int? RevealedItemCount { get; set; }
        public int AffectedRootCount { get; set; }
        public bool AffectedRootSummariesTruncated { get; set; }
        public List<VisibilityRootSummary> AffectedRootSummaries { get; set; } = new List<VisibilityRootSummary>();
        public bool AffectedItemsPreviewTruncated { get; set; }
        public List<VisibilityPreviewItem> AffectedItemsPreview { get; set; } = new List<VisibilityPreviewItem>();
    }

    public sealed class RevealSelectedRequest
    {
        public bool? Apply { get; set; }
        public int? PreviewLimit { get; set; }
    }

    public sealed class RevealSelectedResponse
    {
        public bool Apply { get; set; }
        public int SelectedItemCount { get; set; }
        public int WouldRevealItemCount { get; set; }
        public int? RevealedItemCount { get; set; }
        public int AffectedRootCount { get; set; }
        public bool AffectedRootSummariesTruncated { get; set; }
        public List<VisibilityRootSummary> AffectedRootSummaries { get; set; } = new List<VisibilityRootSummary>();
        public bool AffectedItemsPreviewTruncated { get; set; }
        public List<VisibilityPreviewItem> AffectedItemsPreview { get; set; } = new List<VisibilityPreviewItem>();
    }

    public sealed class IsolateSelectedRequest
    {
        public bool? Apply { get; set; }
        public int? PreviewLimit { get; set; }
    }

    public sealed class IsolateSelectedResponse
    {
        public bool Apply { get; set; }
        public int SelectedItemCount { get; set; }
        public int PreviouslyHiddenItemCount { get; set; }
        public int WouldHideItemCount { get; set; }
        public int WouldKeepVisibleItemCount { get; set; }
        public int? RevealedItemCount { get; set; }
        public int? HiddenItemCount { get; set; }
        public int AffectedRootCount { get; set; }
        public bool AffectedRootSummariesTruncated { get; set; }
        public List<VisibilityRootSummary> AffectedRootSummaries { get; set; } = new List<VisibilityRootSummary>();
        public bool AffectedItemsPreviewTruncated { get; set; }
        public List<VisibilityPreviewItem> AffectedItemsPreview { get; set; } = new List<VisibilityPreviewItem>();
    }

    public sealed class ShowAllRequest
    {
        public bool? Apply { get; set; }
        public int? PreviewLimit { get; set; }
    }

    public sealed class ShowAllResponse
    {
        public bool Apply { get; set; }
        public int CurrentlyHiddenItemCount { get; set; }
        public int WouldRevealItemCount { get; set; }
        public int? RevealedItemCount { get; set; }
        public int AffectedRootCount { get; set; }
        public bool AffectedRootSummariesTruncated { get; set; }
        public List<VisibilityRootSummary> AffectedRootSummaries { get; set; } = new List<VisibilityRootSummary>();
        public bool AffectedItemsPreviewTruncated { get; set; }
        public List<VisibilityPreviewItem> AffectedItemsPreview { get; set; } = new List<VisibilityPreviewItem>();
    }

    public sealed class VisibilityPreviewItem
    {
        public string DisplayName { get; set; }
        public string Path { get; set; }
        public string SourceFile { get; set; }
        public bool IsHidden { get; set; }
    }

    public sealed class VisibilityRootSummary
    {
        public string RootDisplayName { get; set; }
        public string RootPath { get; set; }
        public string SourceFile { get; set; }
        public int AffectedItemCount { get; set; }
    }

    public sealed class SaveDocumentRequest
    {
    }

    public sealed class SaveDocumentAsRequest
    {
        public string Path { get; set; }
        public bool? Overwrite { get; set; }
    }

    public sealed class SaveDocumentResponse
    {
        public string Path { get; set; }
        public string Format { get; set; }
        public long FileSizeBytes { get; set; }
        public bool Overwritten { get; set; }
    }

    public sealed class CloseNavisworksRequest
    {
        public string Mode { get; set; }
        public string SavePath { get; set; }
        public bool? Overwrite { get; set; }
        public bool? Apply { get; set; }
        public bool? ConfirmClose { get; set; }
    }

    public sealed class CloseNavisworksResponse
    {
        public string Mode { get; set; }
        public bool Apply { get; set; }
        public bool ExitScheduled { get; set; }
        public bool DocumentWasModified { get; set; }
        public string DocumentPath { get; set; }
        public string SavedPath { get; set; }
        public bool DiscardedUnsavedChanges { get; set; }
        public bool NativePromptExpected { get; set; }
        public string Message { get; set; }
    }
}
