using ModelContextProtocol.Server;

namespace NavisHelper.McpServer.Services;

/// <summary>
/// Selects which registered MCP tools are advertised in <c>tools/list</c>.
///
    /// The full surface is 108 tools and roughly 180 KB of JSON, which every client
/// carries in context on every request. A session that only queries the model
/// does not need the Clash Detective surface, and vice versa. Selecting a subset
/// removes that fixed cost without changing any tool contract: a tool is either
/// advertised exactly as before, or not advertised at all.
///
/// Selection comes from <c>--tools=&lt;spec&gt;</c> or the
/// <c>NAVISHELPER_MCP_TOOLS</c> environment variable. The spec is a
/// comma-separated list of set names, optionally prefixed with <c>-</c> to
/// subtract. The default is <see cref="AllSetName"/>, so an existing
/// installation behaves exactly as it did before this mechanism existed.
/// </summary>
internal static class McpToolProfile
{
    internal const string AllSetName = "all";
    internal const string CoreSetName = "core";
    internal const string EnvironmentVariableName = "NAVISHELPER_MCP_TOOLS";
    internal const string CommandLinePrefix = "--tools=";

    /// <summary>
    /// Tools that stay advertised under every selection. Without them a client
    /// cannot find the host, check protocol health, or discover which sets are
    /// active, so a narrow selection would be undiagnosable from the outside.
    /// </summary>
    internal static readonly IReadOnlyList<string> AlwaysOnSet = new[]
    {
        "host_status",
        "last_operation_status",
        "list_navisworks_hosts",
        "mcp_diagnostics",
        "mcp_error_contract",
        "mcp_health_check",
        "mcp_recent_calls",
        "mcp_task_timer_finish",
        "mcp_task_timer_start",
    };

    /// <summary>
    /// Named sets. Every tool name in the registered surface must appear in at
    /// least one set; <c>McpToolProfileTests</c> enforces that, so a new tool
    /// cannot silently fall outside every selection.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Sets =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["meta"] = AlwaysOnSet,

            ["query"] = new[]
            {
                "active_model_context",
                "cancel_subtree_names_dump",
                "dump_subtree_names",
                "dump_subtree_names_status",
                "find_items",
                "find_items_by_bbox",
                "find_root_items_by_name",
                "item_properties_by_handle",
                "list_item_children",
                "list_root_items",
                "start_subtree_names_dump",
            },

            ["selection"] = new[]
            {
                "hide_selected",
                "hide_unselected",
                "isolate_selected",
                "reveal_selected",
                "select_by_search",
                "select_items",
                "selected_items_ancestry",
                "selected_items_preview",
                "selected_items_tree",
                "selection_copy_names",
                "selection_status",
                "show_all",
                "unhide_selected",
            },

            ["view"] = new[]
            {
                "activate_saved_viewpoint",
                "capture_current_view",
                "create_viewpoint",
                "current_viewpoint_info",
                "fit_all",
                "focus_on_selection",
                "list_saved_viewpoints",
                "viewpoint_set_camera",
                "world_markers_list",
                "world_markers_manage",
                "world_markers_set",
                "zoom_to_selection",
            },

            ["sets"] = new[]
            {
                "create_search_set",
                "create_selection_set",
                "list_selection_sets",
                "select_selection_set",
                "selection_sets_build_viewpoints",
                "selection_sets_manage",
                "selection_sets_reorder",
            },

            ["viewpoints"] = new[]
            {
                "saved_viewpoints_export",
                "saved_viewpoints_import",
                "saved_viewpoints_manage",
                "saved_viewpoints_reorder",
            },

            ["markup"] = new[]
            {
                "build_mtr_viewpoints",
                "live_markers",
                "markup_selection",
            },

            ["sections"] = new[]
            {
                "get_current_section_box",
                "isolate_by_box",
                "section_box_viewpoint",
            },

            ["reports"] = new[]
            {
                "model_color_scheme",
                "selection_color_by_property",
                "selection_distinct_property_values",
                "selection_export_properties",
                "selection_property_report",
            },

            ["clash"] = new[]
            {
                "cancel_clash_report",
                "cancel_clash_run",
                "clash_batchtest_import",
                "clash_bbox_pair_plan",
                "clash_create_matrix_from_selection",
                "clash_export_points",
                "clash_generate_report",
                "clash_group_by_proximity",
                "clash_group_custom",
                "clash_group_results",
                "clash_ignore_rules",
                "clash_isolate_result",
                "clash_list_clusters",
                "clash_list_results",
                "clash_list_tests",
                "clash_manage_tests",
                "clash_pair_tests_create",
                "clash_renumber_results",
                "clash_report_status",
                "clash_reset_isolation",
                "clash_root_matrix",
                "clash_run_batch",
                "clash_run_resume",
                "clash_run_status",
                "clash_save_viewpoints",
                "clash_set_status",
                "clash_tests_export",
                "clash_tests_from_sets",
                "clash_ungroup",
            },

            ["scenarios"] = new[]
            {
                "delete_scenario",
                "get_scenario",
                "list_scenarios",
                "resolve_scenario",
                "save_scenario",
                "scenario_capabilities",
            },

            ["lifecycle"] = new[]
            {
                "close_navisworks",
                "list_recent_navisworks_files",
                "open_latest_navisworks_file",
                "save_document",
                "save_document_as",
                "start_navisworks",
            },
        };

    /// <summary>
    /// Convenience aliases. <c>core</c> is the read-and-navigate surface: enough
    /// to find items, inspect them, select them, and move the camera.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Aliases =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [CoreSetName] = new[] { "meta", "query", "selection", "view" },
        };

    internal static string ReadSpec(IReadOnlyList<string> commandLineArguments)
    {
        if (commandLineArguments != null)
        {
            for (var index = commandLineArguments.Count - 1; index >= 0; index--)
            {
                var argument = commandLineArguments[index];
                if (!string.IsNullOrEmpty(argument) &&
                    argument.StartsWith(CommandLinePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    return argument.Substring(CommandLinePrefix.Length);
                }
            }
        }

        return Environment.GetEnvironmentVariable(EnvironmentVariableName);
    }

    /// <summary>
    /// Resolves a spec into the set of tool names to advertise. An empty or
    /// absent spec resolves to the full surface. An unknown set name is a
    /// configuration error and throws rather than silently narrowing the
    /// surface, because a silently missing tool is far harder to diagnose than a
    /// startup failure.
    /// </summary>
    internal static ToolProfileSelection Resolve(string spec, IEnumerable<string> registeredToolNames)
    {
        var registered = new HashSet<string>(
            registeredToolNames ?? Enumerable.Empty<string>(),
            StringComparer.Ordinal);

        var tokens = (spec ?? string.Empty)
            .Split(new[] { ',', ';', ' ', '+' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length > 0)
            .ToList();

        if (tokens.Count == 0)
            return new ToolProfileSelection(AllSetName, registered, Array.Empty<string>());

        var selected = new HashSet<string>(StringComparer.Ordinal);
        var resolvedSetNames = new List<string>();
        var isAll = false;

        foreach (var token in tokens)
        {
            var subtract = token.StartsWith("-", StringComparison.Ordinal);
            var name = subtract ? token.Substring(1) : token;
            if (name.Length == 0)
                throw new InvalidOperationException("Empty MCP tool set name in " + EnvironmentVariableName + " spec.");

            IReadOnlyList<string> names;
            if (string.Equals(name, AllSetName, StringComparison.OrdinalIgnoreCase))
            {
                names = registered.ToArray();
                isAll = !subtract;
            }
            else
            {
                names = ExpandSetName(name);
            }

            if (subtract)
            {
                foreach (var toolName in names)
                    selected.Remove(toolName);
                resolvedSetNames.Add("-" + name);
            }
            else
            {
                foreach (var toolName in names)
                    selected.Add(toolName);
                resolvedSetNames.Add(name);
            }
        }

        foreach (var toolName in AlwaysOnSet)
            selected.Add(toolName);

        selected.IntersectWith(registered);

        var description = isAll && resolvedSetNames.Count == 1 ? AllSetName : string.Join(",", resolvedSetNames);
        return new ToolProfileSelection(description, selected, resolvedSetNames);
    }

    private static IReadOnlyList<string> ExpandSetName(string name)
    {
        if (Aliases.TryGetValue(name, out var aliasSets))
        {
            var expanded = new List<string>();
            foreach (var setName in aliasSets)
                expanded.AddRange(ExpandSetName(setName));
            return expanded;
        }

        if (Sets.TryGetValue(name, out var names))
            return names;

        throw new InvalidOperationException(
            "Unknown MCP tool set '" + name + "'. Known sets: " +
            string.Join(", ", Sets.Keys.OrderBy(key => key, StringComparer.Ordinal)) +
            ". Known aliases: " +
            string.Join(", ", Aliases.Keys.OrderBy(key => key, StringComparer.Ordinal)) +
            ", " + AllSetName + ".");
    }

    /// <summary>
    /// Removes every tool outside <paramref name="selection"/> from the served
    /// collection. Returns the removed tool names.
    /// </summary>
    internal static IReadOnlyList<string> Apply(
        McpServerPrimitiveCollection<McpServerTool> toolCollection,
        ToolProfileSelection selection)
    {
        if (toolCollection == null)
            throw new ArgumentNullException(nameof(toolCollection));
        if (selection == null)
            throw new ArgumentNullException(nameof(selection));

        var removed = new List<string>();
        foreach (var tool in toolCollection.ToArray())
        {
            var name = tool.ProtocolTool?.Name;
            if (string.IsNullOrEmpty(name) || selection.ToolNames.Contains(name))
                continue;

            if (toolCollection.Remove(tool))
                removed.Add(name);
        }

        removed.Sort(StringComparer.Ordinal);
        return removed;
    }
}

internal sealed class ToolProfileSelection
{
    internal ToolProfileSelection(
        string description,
        ISet<string> toolNames,
        IReadOnlyList<string> resolvedSetNames)
    {
        Description = description;
        ToolNames = toolNames;
        ResolvedSetNames = resolvedSetNames;
    }

    internal string Description { get; }

    internal ISet<string> ToolNames { get; }

    internal IReadOnlyList<string> ResolvedSetNames { get; }

    internal bool IsFullSurface => ResolvedSetNames.Count == 0 ||
        string.Equals(Description, McpToolProfile.AllSetName, StringComparison.OrdinalIgnoreCase);
}
