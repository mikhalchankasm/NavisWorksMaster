using System;
using System.Collections.Generic;
using System.Linq;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// The path the tools print must be the path `scopeNodePath` accepts.
///
/// Every tool that reports a model item reports a `path` built by joining
/// `DisplayName` up the ancestor chain with `" / "`, and
/// `scope=under_named_node` is documented as taking that path back — it is called
/// "the fast deterministic named-scope option". Measured live on `6501.5.nwd`,
/// passing `list_root_items`' own output straight back produced
/// `schema_violation: Named search scope must resolve to exactly one node;
/// resolved 0`, and the documented fallback `scopeNodeName` failed with
/// `Resolving scopeNodeName exceeded 10 seconds`. The only option that worked was
/// a handle, which is one extra round trip on every scoped call.
///
/// The cause is that the split is on the single character `/` while the join is on
/// `" / "`, and names in a plant model legitimately begin with `/`. `/STORE`
/// becomes `STORE`, which matches no node.
/// </summary>
public sealed class FindItemsPathSegmentsTests
{
    /// <summary>
    /// Walks a printed path the way SearchService.ResolveFrom does: one node at a
    /// time, each level offering the names that node actually carries. Returns the
    /// names consumed, so a walk that reaches the target can be compared against
    /// the levels the path was meant to address.
    /// </summary>
    private static List<string> Walk(string printed, params string[] namesByLevel)
    {
        var consumed = new List<string>();
        var remaining = printed;

        foreach (var name in namesByLevel)
        {
            var rest = FindItemsPathSegments.TryConsume(new[] { name }, remaining);
            if (rest == null)
                return consumed;

            consumed.Add(name);
            remaining = rest;
            if (rest.Length == 0)
                break;
        }

        // A walk that ends with path left over has not reached the target.
        Assert.Equal(string.Empty, remaining);
        return consumed;
    }

    [Fact]
    public void A_printed_path_round_trips_when_names_begin_with_a_slash()
    {
        // Exactly what list_root_items prints for this model.
        var consumed = Walk("6501.5.nwd / /STORE", "6501.5.nwd", "/STORE");

        Assert.Equal(new[] { "6501.5.nwd", "/STORE" }, consumed);
    }

    [Fact]
    public void Every_level_of_a_deep_printed_path_is_reached()
    {
        // From a live find_items preview on the same model.
        var levels = new[]
        {
            "6501.5.nwd",
            "/STORE",
            "/6501.5-S",
            "/6501.5-S.ТХ",
            "/Вариант_1_U-221",
            "/Copy-of-6501.5-15-SA02-2179-S3C1-N",
        };

        var consumed = Walk(string.Join(FindItemsPathSegments.Separator, levels), levels);

        Assert.Equal(levels, consumed);
    }

    [Fact]
    public void A_name_containing_a_slash_in_the_middle_survives()
    {
        // Real shape from the same model: GASKET 1 of BRANCH /150.=79338/61632.1
        // Splitting the text would tear this name into three.
        var consumed = Walk(
            "6501.5.nwd / GASKET 1 of BRANCH /150.=79338/61632.1",
            "6501.5.nwd",
            "GASKET 1 of BRANCH /150.=79338/61632.1");

        Assert.Equal(new[] { "6501.5.nwd", "GASKET 1 of BRANCH /150.=79338/61632.1" }, consumed);
    }

    [Fact]
    public void A_caller_authored_slash_path_still_splits_on_slashes()
    {
        // Backward compatibility: a plain slash-delimited path, which is what the
        // old behaviour accepted and the only thing that still reaches the split.
        var segments = FindItemsPathSegments.SplitSlashPath("Model/Level/Item").ToList();

        Assert.Equal(new[] { "Model", "Level", "Item" }, segments);
    }

    [Fact]
    public void Backslashes_are_still_accepted_as_a_separator()
    {
        var segments = FindItemsPathSegments.SplitSlashPath(@"Model\Level\Item").ToList();

        Assert.Equal(new[] { "Model", "Level", "Item" }, segments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" / ")]
    public void Empty_input_yields_nothing(string path)
    {
        Assert.Empty(FindItemsPathSegments.SplitSlashPath(path));
    }

    [Fact]
    public void A_node_whose_own_name_contains_the_separator_resolves()
    {
        // BuildItemPath emits "Supply / Return / Child" for a node called
        // "Supply / Return", and those bytes are indistinguishable from two levels.
        // Splitting up front guesses wrong, so resolution consumes one node at a
        // time against the names the tree actually offers.
        var rest = FindItemsPathSegments.TryConsume(new[] { "Supply / Return" }, "Supply / Return / Child");

        Assert.Equal("Child", rest);
    }

    [Fact]
    public void The_longest_matching_name_wins()
    {
        // A node can offer several names - DisplayName, ClassDisplayName, source
        // file. If the shorter one were taken, the deeper match would be lost.
        var rest = FindItemsPathSegments.TryConsume(
            new[] { "Supply", "Supply / Return" },
            "Supply / Return / Child");

        Assert.Equal("Child", rest);
    }

    [Fact]
    public void Consuming_the_whole_remainder_reports_the_node_itself()
    {
        Assert.Equal(string.Empty, FindItemsPathSegments.TryConsume(new[] { "/STORE" }, "/STORE"));
    }

    [Fact]
    public void A_name_that_is_not_a_segment_boundary_does_not_match()
    {
        // "/STO" is a prefix of the text but not of a segment, so it must not
        // consume anything.
        Assert.Null(FindItemsPathSegments.TryConsume(new[] { "/STO" }, "/STORE / /Child"));
    }

    [Fact]
    public void A_printed_root_segment_is_consumed_then_the_child_follows()
    {
        var afterRoot = FindItemsPathSegments.TryConsume(new[] { "6501.5.nwd" }, "6501.5.nwd / /STORE");
        Assert.Equal("/STORE", afterRoot);

        var afterChild = FindItemsPathSegments.TryConsume(new[] { "/STORE" }, afterRoot);
        Assert.Equal(string.Empty, afterChild);
    }

    [Theory]
    [InlineData(null, "a / b")]
    [InlineData("x", null)]
    [InlineData("x", "")]
    public void TryConsume_is_null_safe(string name, string remaining)
    {
        var names = name == null ? null : new[] { name };
        Assert.Null(FindItemsPathSegments.TryConsume(names, remaining));
    }

    [Fact]
    public void Splitting_a_printed_path_on_slashes_is_why_it_is_not_done()
    {
        // The reason SplitSlashPath is named for what it accepts. Handing it a
        // printed path tears one real node name into three, which is exactly the
        // defect this whole file exists to prevent; SearchService therefore sends
        // printed paths to TryConsume and never here.
        var torn = FindItemsPathSegments.SplitSlashPath(
            "6501.5.nwd / GASKET 1 of BRANCH /150.=79338/61632.1").ToList();

        Assert.Equal(
            new[] { "6501.5.nwd", "GASKET 1 of BRANCH", "150.=79338", "61632.1" },
            torn);
    }

    [Fact]
    public void Separator_is_the_one_the_paths_are_built_with()
    {
        // If BuildItemPath's separator ever changes, this is the single place that
        // has to change with it.
        Assert.Equal(" / ", FindItemsPathSegments.Separator);
    }
}
