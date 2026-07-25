using System.Text.Json;
using System.Text.Json.Serialization;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ResponseSummaryFormatterTests
{
    [Fact]
    public void NullAndUnknownResponsesReturnNull()
    {
        Assert.Null(ResponseSummaryFormatter.Build(null));
        Assert.Null(ResponseSummaryFormatter.Build(new object()));
    }

    [Fact]
    public void EverySupportedResponseTypeHasAStableSummaryBranch()
    {
        var responseTypes = new[]
        {
            typeof(ActivateSavedViewpointResponse),
            typeof(ClashBboxPairPlanResponse),
            typeof(ClashCreateMatrixFromSelectionResponse),
            typeof(ClashGenerateReportResponse),
            typeof(ClashIsolateResultResponse),
            typeof(ClashListClustersResponse),
            typeof(ClashListResultsResponse),
            typeof(ClashListTestsResponse),
            typeof(ClashManageTestsResponse),
            typeof(ClashPairTestsCreateResponse),
            typeof(ClashReportStatusResponse),
            typeof(ClashResetIsolationResponse),
            typeof(ClashSaveViewpointsResponse),
            typeof(CaptureCurrentViewResponse),
            typeof(CloseNavisworksResponse),
            typeof(CreateSearchSetResponse),
            typeof(CreateSelectionSetResponse),
            typeof(CreateViewpointResponse),
            typeof(CurrentViewpointInfoResponse),
            typeof(DumpSubtreeNamesJobStatusResponse),
            typeof(DumpSubtreeNamesResponse),
            typeof(FindItemsResponse),
            typeof(FitAllResponse),
            typeof(FocusOnSelectionResponse),
            typeof(HideSelectedResponse),
            typeof(HideUnselectedResponse),
            typeof(HostStatusResponse),
            typeof(IsolateSelectedResponse),
            typeof(ItemPropertiesByHandleResponse),
            typeof(LastOperationStatusResponse),
            typeof(ListItemChildrenResponse),
            typeof(ListRootItemsResponse),
            typeof(ListSavedViewpointsResponse),
            typeof(ListSelectionSetsResponse),
            typeof(LiveMarkersResponse),
            typeof(MarkupSelectionResponse),
            typeof(ModelColorSchemeResponse),
            typeof(RevealSelectedResponse),
            typeof(SavedViewpointsExportResponse),
            typeof(SavedViewpointsImportResponse),
            typeof(SavedViewpointsManageResponse),
            typeof(SavedViewpointsReorderResponse),
            typeof(SectionBoxViewpointResponse),
            typeof(SelectedItemsAncestryResponse),
            typeof(SelectedItemsPreviewResponse),
            typeof(SelectedItemsTreeResponse),
            typeof(SelectionColorByPropertyResponse),
            typeof(SelectionCopyNamesResponse),
            typeof(SelectionDistinctPropertyValuesResponse),
            typeof(SelectionExportPropertiesResponse),
            typeof(SelectionPropertyReportResponse),
            typeof(SelectionSetsManageResponse),
            typeof(SelectionSetsReorderResponse),
            typeof(SelectionStatusResponse),
            typeof(SelectItemsResponse),
            typeof(SelectSelectionSetResponse),
            typeof(ShowAllResponse),
            typeof(UnhideSelectedResponse),
            typeof(ZoomToSelectionResponse),
        };

        foreach (var responseType in responseTypes)
        {
            var response = Activator.CreateInstance(responseType);
            var summary = ResponseSummaryFormatter.Build(response);

            Assert.True(
                summary is Dictionary<string, object>,
                $"No response summary branch for {responseType.Name}.");
        }
    }

    [Fact]
    public void CloseNavisworksSummaryCapturesSafetyOutcome()
    {
        var response = new CloseNavisworksResponse
        {
            Mode = NavisworksCloseModes.Discard,
            Apply = true,
            ExitScheduled = true,
            DocumentWasModified = true,
            DiscardedUnsavedChanges = true,
        };

        var summary = Assert.IsType<Dictionary<string, object>>(
            ResponseSummaryFormatter.Build(response));

        Assert.Equal(NavisworksCloseModes.Discard, summary["mode"]);
        Assert.Equal(true, summary["apply"]);
        Assert.Equal(true, summary["exit_scheduled"]);
        Assert.Equal(true, summary["document_was_modified"]);
        Assert.Equal(true, summary["discarded_unsaved_changes"]);
    }

    [Fact]
    public void FindItemsSummaryUsesResponseCountsAndPreservesKeyOrder()
    {
        var response = new FindItemsResponse
        {
            Scope = FindItemsScopes.CurrentSelection,
            MatchDepth = FindItemsMatchDepths.First,
            CountOnly = true,
            MatchedItemCount = 17,
            ScannedItemCount = 42,
            Preflight = new FindItemsPreflight(),
            Summary = new FindItemsSummary
            {
                MatchedQueries = 2,
                NotFoundQueries = 1,
                TotalItemsInMatches = 17,
            },
            Results = new List<FindItemsResult>
            {
                new FindItemsResult(),
                new FindItemsResult(),
                new FindItemsResult(),
            },
        };

        var summary = Assert.IsType<Dictionary<string, object>>(
            ResponseSummaryFormatter.Build(response));

        Assert.Equal(
            new[]
            {
                "matched_queries",
                "not_found_queries",
                "total_items_in_matches",
                "result_count",
                "scope",
                "match_depth",
                "count_only",
                "matched_item_count",
                "scanned_item_count",
                "preflight",
            },
            summary.Keys.ToArray());
        Assert.Equal(2, summary["matched_queries"]);
        Assert.Equal(1, summary["not_found_queries"]);
        Assert.Equal(17, summary["total_items_in_matches"]);
        Assert.Equal(3, summary["result_count"]);
        Assert.Equal(FindItemsScopes.CurrentSelection, summary["scope"]);
        Assert.Equal(FindItemsMatchDepths.First, summary["match_depth"]);
        Assert.Equal(true, summary["count_only"]);
        Assert.Equal(17, summary["matched_item_count"]);
        Assert.Equal(42, summary["scanned_item_count"]);
        Assert.Equal(true, summary["preflight"]);
    }

    [Fact]
    public void ListRootItemsSummaryRetainsNullThroughJsonSerialization()
    {
        var response = new ListRootItemsResponse
        {
            DocumentTitle = null,
            RootItemCount = 7,
            Truncated = true,
        };

        var summary = Assert.IsType<Dictionary<string, object>>(
            ResponseSummaryFormatter.Build(response));

        Assert.Equal(
            new[] { "document_title", "root_item_count", "returned_item_count", "truncated" },
            summary.Keys.ToArray());
        Assert.True(summary.ContainsKey("document_title"));
        Assert.Null(summary["document_title"]);
        Assert.Equal(
            "{\"document_title\":null,\"root_item_count\":7,\"returned_item_count\":0,\"truncated\":true}",
            JsonSerializer.Serialize(summary, CreateJsonOptions()));
    }

    [Fact]
    public void SelectedItemsPreviewUsesReturnedPreviewCount()
    {
        var response = new SelectedItemsPreviewResponse
        {
            SelectedItemCount = 12,
            Truncated = true,
            Items = new List<SelectedItemPreview>
            {
                new SelectedItemPreview(),
                new SelectedItemPreview(),
            },
        };

        var summary = Assert.IsType<Dictionary<string, object>>(
            ResponseSummaryFormatter.Build(response));

        Assert.Equal(12, summary["selected_item_count"]);
        Assert.Equal(2, summary["returned_item_count"]);
        Assert.Equal(true, summary["truncated"]);
    }

    [Fact]
    public void SelectedItemsAncestrySummaryComputesMaximumChainDepth()
    {
        var response = new SelectedItemsAncestryResponse
        {
            SelectedItemCount = 4,
            Truncated = false,
            Items = new List<SelectedItemAncestry>
            {
                new SelectedItemAncestry
                {
                    Chain = new List<SelectedItemHierarchyNode>
                    {
                        new SelectedItemHierarchyNode(),
                        new SelectedItemHierarchyNode(),
                    },
                },
                new SelectedItemAncestry
                {
                    Chain = new List<SelectedItemHierarchyNode>
                    {
                        new SelectedItemHierarchyNode(),
                    },
                },
            },
        };

        var summary = Assert.IsType<Dictionary<string, object>>(
            ResponseSummaryFormatter.Build(response));

        Assert.Equal(4, summary["selected_item_count"]);
        Assert.Equal(2, summary["returned_item_count"]);
        Assert.Equal(2, summary["max_chain_depth"]);
        Assert.Equal(false, summary["truncated"]);
    }

    [Fact]
    public void EmptySelectedItemsAncestryHasZeroMaximumChainDepth()
    {
        var response = new SelectedItemsAncestryResponse();

        var summary = Assert.IsType<Dictionary<string, object>>(
            ResponseSummaryFormatter.Build(response));

        Assert.Equal(0, summary["returned_item_count"]);
        Assert.Equal(0, summary["max_chain_depth"]);
    }

    [Fact]
    public void FindItemsWithMissingSummaryRetainsCurrentNullReferenceBehavior()
    {
        var response = new FindItemsResponse { Summary = null };

        Assert.Throws<NullReferenceException>(() =>
            ResponseSummaryFormatter.Build(response));
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        return new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }
}
