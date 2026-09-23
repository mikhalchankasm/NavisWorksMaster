using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Tools;

namespace NavisHelper.McpServer.Services;

internal sealed partial class ScenarioLibraryService
{
    private static ToolDescriptor GetToolDescriptor(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return null;
        return ToolDescriptors.TryGetValue(toolName, out var descriptor) ? descriptor : null;
    }

    private static IReadOnlyDictionary<string, ToolDescriptor> CreateToolDescriptors()
    {
        var descriptors = new Dictionary<string, ToolDescriptor>(StringComparer.Ordinal)
        {
            ["mcp_health_check"] = CreateToolDescriptor(typeof(NavisworksHostTools), nameof(NavisworksHostTools.McpHealthCheck), 1, false, false, Array.Empty<string>()),
            ["active_model_context"] = CreateToolDescriptor(typeof(NavisworksHostTools), nameof(NavisworksHostTools.ActiveModelContext), 1, false, false, Array.Empty<string>()),
            ["list_root_items"] = CreateToolDescriptor(typeof(NavisworksModelTools), nameof(NavisworksModelTools.ListRootItems), 1, false, false, Array.Empty<string>()),
            ["find_root_items_by_name"] = CreateToolDescriptor(typeof(NavisworksModelTools), nameof(NavisworksModelTools.FindRootItemsByName), 1, false, false, Array.Empty<string>()),
            ["find_items"] = CreateToolDescriptor(typeof(NavisworksModelTools), nameof(NavisworksModelTools.FindItems), 2, false, false, Array.Empty<string>()),
            ["find_items_by_bbox"] = CreateToolDescriptor(typeof(NavisworksModelTools), nameof(NavisworksModelTools.FindItemsByBbox), 1, false, false, Array.Empty<string>()),
            ["selection_status"] = CreateToolDescriptor(typeof(NavisworksSelectionTools), nameof(NavisworksSelectionTools.SelectionStatus), 1, false, false, Array.Empty<string>()),
            ["selected_items_preview"] = CreateToolDescriptor(typeof(NavisworksSelectionTools), nameof(NavisworksSelectionTools.SelectedItemsPreview), 1, false, false, Array.Empty<string>()),
            ["item_properties_by_handle"] = CreateToolDescriptor(typeof(NavisworksModelTools), nameof(NavisworksModelTools.ItemPropertiesByHandle), 1, false, false, Array.Empty<string>()),
            ["current_viewpoint_info"] = CreateToolDescriptor(typeof(NavisworksViewpointTools), nameof(NavisworksViewpointTools.CurrentViewpointInfo), 1, false, false, Array.Empty<string>()),
            ["list_selection_sets"] = CreateToolDescriptor(typeof(NavisworksSelectionSetTools), nameof(NavisworksSelectionSetTools.ListSelectionSets), 1, false, false, Array.Empty<string>()),
            ["selection_property_report"] = CreateToolDescriptor(typeof(NavisworksSelectionReportTools), nameof(NavisworksSelectionReportTools.SelectionPropertyReport), 1, false, false, Array.Empty<string>()),
            ["selection_distinct_property_values"] = CreateToolDescriptor(typeof(NavisworksSelectionReportTools), nameof(NavisworksSelectionReportTools.SelectionDistinctPropertyValues), 1, false, false, Array.Empty<string>()),
            ["model_color_scheme"] = CreateToolDescriptor(typeof(NavisworksModelColorSchemeTools), nameof(NavisworksModelColorSchemeTools.ModelColorScheme), 1, true, false, Array.Empty<string>()),
            ["select_selection_set"] = CreateToolDescriptor(typeof(NavisworksSelectionSetTools), nameof(NavisworksSelectionSetTools.SelectSelectionSet), 1, false, false, new[] { "pathOrName" }),
            ["isolate_selected"] = CreateToolDescriptor(typeof(NavisworksVisibilityTools), nameof(NavisworksVisibilityTools.IsolateSelected), 1, true, false, Array.Empty<string>()),
            ["isolate_by_box"] = CreateToolDescriptor(typeof(NavisworksSectionBoxTools), nameof(NavisworksSectionBoxTools.IsolateByBox), 1, true, false, new[] { "box", "maxScannedItems", "maxDurationSeconds" }),
            ["zoom_to_selection"] = CreateToolDescriptor(typeof(NavisworksViewNavigationTools), nameof(NavisworksViewNavigationTools.ZoomToSelection), 1, false, false, Array.Empty<string>()),
            ["clash_list_tests"] = CreateToolDescriptor(typeof(NavisworksClashListingTools), nameof(NavisworksClashListingTools.ClashListTests), 1, false, false, Array.Empty<string>()),
            ["clash_list_results"] = CreateToolDescriptor(typeof(NavisworksClashListingTools), nameof(NavisworksClashListingTools.ClashListResults), 1, false, false, Array.Empty<string>()),
            ["clash_list_clusters"] = CreateToolDescriptor(typeof(NavisworksClashListingTools), nameof(NavisworksClashListingTools.ClashListClusters), 1, false, false, Array.Empty<string>()),
            ["clash_bbox_pair_plan"] = CreateToolDescriptor(typeof(NavisworksClashTestTools), nameof(NavisworksClashTestTools.ClashBboxPairPlan), 3, false, true, Array.Empty<string>()),
            ["clash_pair_tests_create"] = CreateToolDescriptor(typeof(NavisworksClashTestTools), nameof(NavisworksClashTestTools.ClashPairTestsCreate), 2, true, false, Array.Empty<string>()),
            ["clash_manage_tests"] = CreateToolDescriptor(typeof(NavisworksClashTestTools), nameof(NavisworksClashTestTools.ClashManageTests), 2, true, false, new[] { "operation" }),
            ["clash_group_results"] = CreateToolDescriptor(typeof(NavisworksClashResultTools), nameof(NavisworksClashResultTools.ClashGroupResults), 1, true, false, Array.Empty<string>()),
            ["clash_root_matrix"] = CreateToolDescriptor(typeof(NavisworksClashRootMatrixTools), nameof(NavisworksClashRootMatrixTools.ClashRootMatrix), 1, false, false, Array.Empty<string>()),
            ["clash_group_by_proximity"] = CreateToolDescriptor(typeof(NavisworksClashResultTools), nameof(NavisworksClashResultTools.ClashGroupByProximity), 1, true, false, Array.Empty<string>()),
            ["clash_ignore_rules"] = CreateToolDescriptor(typeof(NavisworksClashResultTools), nameof(NavisworksClashResultTools.ClashIgnoreRules), 1, true, false, Array.Empty<string>()),
            ["clash_export_points"] = CreateToolDescriptor(typeof(NavisworksClashResultTools), nameof(NavisworksClashResultTools.ClashExportPoints), 1, false, true, Array.Empty<string>()),
            ["clash_renumber_results"] = CreateToolDescriptor(typeof(NavisworksClashResultTools), nameof(NavisworksClashResultTools.ClashRenumberResults), 1, true, false, Array.Empty<string>()),
            ["clash_create_matrix_from_selection"] = CreateToolDescriptor(typeof(NavisworksClashTestTools), nameof(NavisworksClashTestTools.ClashCreateMatrixFromSelection), 2, true, false, Array.Empty<string>()),
            ["clash_tests_from_sets"] = CreateToolDescriptor(typeof(NavisworksClashSetTools), nameof(NavisworksClashSetTools.ClashTestsFromSets), 1, true, false, Array.Empty<string>()),
            ["clash_tests_export"] = CreateToolDescriptor(typeof(NavisworksClashTransferTools), nameof(NavisworksClashTransferTools.ClashTestsExport), 1, false, true, Array.Empty<string>()),
            ["clash_batchtest_import"] = CreateToolDescriptor(typeof(NavisworksClashTransferTools), nameof(NavisworksClashTransferTools.ClashBatchtestImport), 1, true, false, new[] { "inputPath" }),
            ["clash_run_batch"] = CreateToolDescriptor(typeof(NavisworksClashSetTools), nameof(NavisworksClashSetTools.ClashRunBatch), 1, true, false, Array.Empty<string>()),
            ["select_by_search"] = CreateToolDescriptor(typeof(NavisworksScenarioWorkflowTools), nameof(NavisworksScenarioWorkflowTools.SelectBySearch), 1, false, false, new[] { "conditions" }),
            ["clash_report_status"] = CreateToolDescriptor(typeof(NavisworksClashReportTools), nameof(NavisworksClashReportTools.ClashReportStatus), 1, false, false, Array.Empty<string>()),
            ["selection_export_properties"] = CreateToolDescriptor(
                typeof(NavisworksSelectionReportTools),
                nameof(NavisworksSelectionReportTools.SelectionExportProperties),
                contractVersion: 1,
                mutatesModel: false,
                writesFiles: true,
                requiredArguments: new[] { "outputPath" }),
            ["selection_sets_build_viewpoints"] = CreateToolDescriptor(
                typeof(NavisworksMarkupTools),
                nameof(NavisworksMarkupTools.SelectionSetsBuildViewpoints),
                contractVersion: 1,
                mutatesModel: true,
                writesFiles: false,
                requiredArguments: new[] { "folderPrefix", "steps" }),
            ["clash_generate_report"] = CreateToolDescriptor(
                typeof(NavisworksClashReportTools),
                nameof(NavisworksClashReportTools.ClashGenerateReport),
                contractVersion: 1,
                mutatesModel: true,
                writesFiles: true,
                requiredArguments: Array.Empty<string>()),
            ["clash_save_viewpoints"] = CreateToolDescriptor(
                typeof(NavisworksClashReportTools),
                nameof(NavisworksClashReportTools.ClashSaveViewpoints),
                contractVersion: 1,
                mutatesModel: true,
                writesFiles: false,
                requiredArguments: Array.Empty<string>()),
            ["clash_isolate_result"] = CreateToolDescriptor(
                typeof(NavisworksClashIsolationTools),
                nameof(NavisworksClashIsolationTools.ClashIsolateResult),
                contractVersion: 1,
                mutatesModel: true,
                writesFiles: true,
                requiredArguments: new[] { "resultHandle" }),
            ["clash_reset_isolation"] = CreateToolDescriptor(
                typeof(NavisworksClashIsolationTools),
                nameof(NavisworksClashIsolationTools.ClashResetIsolation),
                contractVersion: 1,
                mutatesModel: true,
                writesFiles: false,
                requiredArguments: Array.Empty<string>()),
            ["capture_current_view"] = CreateToolDescriptor(
                typeof(NavisworksClashIsolationTools),
                nameof(NavisworksClashIsolationTools.CaptureCurrentView),
                contractVersion: 1,
                mutatesModel: false,
                writesFiles: true,
                requiredArguments: new[] { "outputPath" }),
        };
        descriptors["select_by_search"].ChangesSelection = true;
        descriptors["find_items"].SupportedContractVersions.Add(1);
        descriptors["find_items"].OutputProjections.UnionWith(new[] { "matchedItemCount", "depthHistogram", "sampleValuesFromModel", "results" });
        descriptors["clash_bbox_pair_plan"].SupportedContractVersions.Add(2);
        descriptors["clash_create_matrix_from_selection"].SupportedContractVersions.Add(1);
        descriptors["select_by_search"].OutputProjections.UnionWith(new[] { "matchedItemCount", "selectedItemCount", "preview" });
        descriptors["clash_bbox_pair_plan"].OutputProjections.UnionWith(new[] { "pairs", "candidatePairCount", "outputPath" });
        descriptors["clash_pair_tests_create"].OutputProjections.UnionWith(new[] { "tests", "createdTestCount" });
        descriptors["clash_create_matrix_from_selection"].OutputProjections.UnionWith(new[] { "tests", "createdTestCount", "plannedPairCount" });
        descriptors["clash_tests_from_sets"].OutputProjections.UnionWith(new[] { "tests", "createdTestCount", "runOperationId" });
        descriptors["clash_tests_export"].OutputProjections.UnionWith(new[] { "plan", "outputWritten", "artifactStatus", "bytesWritten", "sha256" });
        descriptors["clash_batchtest_import"].OutputProjections.UnionWith(new[] { "plan", "tests", "createdTestCount", "rolledBackTestCount" });
        descriptors["clash_run_batch"].OutputProjections.UnionWith(new[] { "operationId", "state", "processedTestCount" });
        descriptors["clash_list_tests"].OutputProjections.UnionWith(new[] { "tests", "returnedTestCount" });
        foreach (var descriptor in descriptors)
        {
            var responseType = descriptor.Value.Method.ReturnType;
            if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Task<>))
                responseType = responseType.GetGenericArguments()[0];
            foreach (var projection in descriptor.Value.OutputProjections)
            {
                var root = projection.Split(new[] { '.', '[' }, 2)[0];
                if (responseType.GetProperty(root, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) == null)
                {
                    throw new InvalidOperationException(
                        "Scenario output projection does not exist on the tool response: " +
                        descriptor.Key + "." + projection);
                }
            }
        }
        return descriptors;
    }

    public ScenarioCapabilitiesResponse GetCapabilities()
    {
        var response = new ScenarioCapabilitiesResponse
        {
            ParameterTypes = ParameterTypes.OrderBy(value => value, StringComparer.Ordinal).ToList(),
            PairNameTokens = new List<string> { "{index}", "{aName}", "{bName}", "{aCode}", "{bCode}" },
            PairNameTransforms = new List<string> { "zeroPad:N", "strip:#regex#", "replace:#regex#replacement#", "upper", "lower" },
            ForeachRules = "schemaVersion 2 only; over must reference an allowlisted prior $stepResult projection; maxIterations is required (1..1000); maximum nesting depth is 1; body has 1..16 steps.",
        };
        response.AllowedTools = ToolDescriptors
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new ScenarioToolCapability
            {
                Tool = item.Key,
                ScenarioContractVersion = item.Value.ContractVersion,
                HasApply = item.Value.HasApply,
                MutatesModel = item.Value.MutatesModel,
                WritesFiles = item.Value.WritesFiles,
                ChangesSelection = item.Value.ChangesSelection,
                ReviewedWrites = item.Value.ReviewedWrites.OrderBy(value => value, StringComparer.Ordinal).ToList(),
                OutputProjections = item.Value.OutputProjections.OrderBy(value => value, StringComparer.Ordinal).ToList(),
            })
            .ToList();
        response.Example = new ScenarioDraft
        {
            SchemaVersion = 2,
            Name = "Матрица коллизий по прямым потомкам",
            Description = "Select discipline groups below one building, rebuild and run a cleanly named clash matrix, then remove zero-result tests.",
            Parameters = new List<ScenarioParameterDefinition>
            {
                new()
                {
                    Name = "namePrefix",
                    Type = "string",
                    Required = false,
                    Default = JsonSerializer.SerializeToElement("100000-XXX1-YY"),
                    Pattern = "^[0-9A-Za-zА-Яа-я()\\- ]+$",
                },
            },
            Steps = new List<ScenarioStepDefinition>
            {
                new()
                {
                    StepId = "select_marks",
                    Tool = "select_by_search",
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["conditions"] = JsonSerializer.SerializeToElement(new[]
                        {
                            new Dictionary<string, object>
                            {
                                ["category"] = "Item", ["property"] = "Name", ["operator"] = "wildcard",
                                ["value"] = "/100000-XXX1-YY-01*", ["ignoreCase"] = true,
                            },
                        }),
                        ["scope"] = JsonSerializer.SerializeToElement("direct_children_of"),
                        ["parentConditions"] = JsonSerializer.SerializeToElement(new[]
                        {
                            new Dictionary<string, object>
                            {
                                ["category"] = "Item", ["property"] = "Name", ["operator"] = "equals",
                                ["value"] = "/100000-XXX1-YY-01",
                            },
                        }),
                        ["replaceSelection"] = JsonSerializer.SerializeToElement(true),
                        ["maxMatchedItems"] = JsonSerializer.SerializeToElement(100),
                    },
                },
                new()
                {
                    StepId = "build_and_run",
                    Tool = "clash_create_matrix_from_selection",
                    ScenarioContractVersion = 2,
                    ReviewedWrites = new List<string> { "removePreviousGenerated", "runAfterCreate" },
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["namePrefix"] = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["$parameter"] = "namePrefix" }),
                        ["testType"] = JsonSerializer.SerializeToElement("hard"),
                        ["runAfterCreate"] = JsonSerializer.SerializeToElement(true),
                        ["removePreviousGenerated"] = JsonSerializer.SerializeToElement(true),
                        ["pairNameTemplate"] = JsonSerializer.SerializeToElement("{index|zeroPad:3} 100000-XXX1-YY {aName|strip:#^/100000-XXX1-YY-01[-_]#}-{bName|strip:#^/100000-XXX1-YY-01[-_]#}"),
                        ["pairNameStartIndex"] = JsonSerializer.SerializeToElement(1),
                        ["includePairNames"] = JsonSerializer.SerializeToElement(false),
                    },
                },
                new()
                {
                    StepId = "delete_zero",
                    Tool = "clash_manage_tests",
                    ScenarioContractVersion = 2,
                    Arguments = new Dictionary<string, JsonElement>
                    {
                        ["operation"] = JsonSerializer.SerializeToElement("delete"),
                        ["namePrefix"] = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["$parameter"] = "namePrefix" }),
                        ["onlyWithTotal"] = JsonSerializer.SerializeToElement(0),
                    },
                },
            },
        };
        return response;
    }

    private static ToolDescriptor CreateToolDescriptor(
        Type toolType,
        string methodName,
        int contractVersion,
        bool mutatesModel,
        bool writesFiles,
        IEnumerable<string> requiredArguments)
    {
        var method = toolType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "Scenario allowlist method was not found: " +
                toolType.Name +
                "." +
                methodName);
        var apply = method.GetParameters().FirstOrDefault(parameter =>
            string.Equals(parameter.Name, "apply", StringComparison.OrdinalIgnoreCase));
        var required = method.GetParameters()
            .Where(parameter =>
                parameter.ParameterType != typeof(CancellationToken) &&
                !parameter.IsOptional &&
                !parameter.HasDefaultValue &&
                !IsAuthorizationArgument(parameter.Name) &&
                !RuntimeIdentityArguments.Contains(parameter.Name))
            .Select(parameter => parameter.Name)
            .Concat(requiredArguments ?? Array.Empty<string>());
        var authorizationArguments = method.GetParameters()
            .Where(parameter =>
                parameter.ParameterType == typeof(bool) &&
                parameter.Name.StartsWith("confirm", StringComparison.OrdinalIgnoreCase) &&
                !parameter.Name.StartsWith("confirmLarge", StringComparison.OrdinalIgnoreCase))
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var scaleAuthorizationArguments = method.GetParameters()
            .Where(parameter =>
                parameter.ParameterType == typeof(bool) &&
                parameter.Name.StartsWith("confirmLarge", StringComparison.OrdinalIgnoreCase))
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reviewedWrites = method.GetParameters()
            .Where(parameter => ReviewedWriteBehaviorArguments.Contains(parameter.Name))
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new ToolDescriptor
        {
            Method = method,
            ContractVersion = contractVersion,
            SupportedContractVersions = new HashSet<int> { contractVersion },
            HasApply = apply != null,
            ApplyParameterName = apply?.Name ?? "apply",
            MutatesModel = mutatesModel,
            WritesFiles = writesFiles,
            RequiredArguments = new HashSet<string>(required, StringComparer.OrdinalIgnoreCase),
            AuthorizationArguments = authorizationArguments,
            ScaleAuthorizationArguments = scaleAuthorizationArguments,
            ReviewedWrites = reviewedWrites,
        };
    }

    private static bool IsAuthorizationArgument(string name)
    {
        return string.Equals(name, "apply", StringComparison.OrdinalIgnoreCase) ||
               (name?.StartsWith("confirm", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool IsCredentialName(string name)
    {
        var normalized = new string((name ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return CredentialNameTerms.Any(normalized.Contains);
    }

    private sealed class ToolDescriptor
    {
        public MethodInfo Method { get; set; }
        public int ContractVersion { get; set; }
        public HashSet<int> SupportedContractVersions { get; set; } = new HashSet<int>();
        public bool HasApply { get; set; }
        public string ApplyParameterName { get; set; }
        public bool MutatesModel { get; set; }
        public bool WritesFiles { get; set; }
        public bool ChangesSelection { get; set; }
        public IReadOnlySet<string> RequiredArguments { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> AuthorizationArguments { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> ScaleAuthorizationArguments { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> ReviewedWrites { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> OutputProjections { get; set; } = new HashSet<string>(StringComparer.Ordinal);
    }
}
