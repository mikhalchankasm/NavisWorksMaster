using System;
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
    [Fact]
    public void A_printed_path_round_trips_when_names_begin_with_a_slash()
    {
        // Exactly what list_root_items prints for this model.
        var printed = "6501.5.nwd / /STORE";

        var segments = FindItemsPathSegments.Split(printed).ToList();

        Assert.Equal(new[] { "6501.5.nwd", "/STORE" }, segments);
    }

    [Fact]
    public void Every_segment_of_a_deep_printed_path_survives()
    {
        // From a live find_items preview on the same model.
        var printed =
            "6501.5.nwd / /STORE / /6501.5-S / /6501.5-S.ТХ / /Вариант_1_U-221 / "
            + "/Copy-of-6501.5-15-SA02-2179-S3C1-N";

        var segments = FindItemsPathSegments.Split(printed).ToList();

        Assert.Equal(
            new[]
            {
                "6501.5.nwd",
                "/STORE",
                "/6501.5-S",
                "/6501.5-S.ТХ",
                "/Вариант_1_U-221",
                "/Copy-of-6501.5-15-SA02-2179-S3C1-N",
            },
            segments);
    }

    [Fact]
    public void A_name_containing_a_slash_in_the_middle_survives()
    {
        // Real shape from the same model: GASKET 1 of BRANCH /150.=79338/61632.1
        var printed = "6501.5.nwd / GASKET 1 of BRANCH /150.=79338/61632.1";

        var segments = FindItemsPathSegments.Split(printed).ToList();

        Assert.Equal(new[] { "6501.5.nwd", "GASKET 1 of BRANCH /150.=79338/61632.1" }, segments);
    }

    [Fact]
    public void Paths_without_the_separator_still_split_on_slashes()
    {
        // Backward compatibility: a caller passing a plain slash-delimited path,
        // which is what the old behaviour accepted, must keep working.
        var segments = FindItemsPathSegments.Split("Model/Level/Item").ToList();

        Assert.Equal(new[] { "Model", "Level", "Item" }, segments);
    }

    [Fact]
    public void Backslashes_are_still_accepted_as_a_separator()
    {
        var segments = FindItemsPathSegments.Split(@"Model\Level\Item").ToList();

        Assert.Equal(new[] { "Model", "Level", "Item" }, segments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" / ")]
    public void Empty_input_yields_nothing(string path)
    {
        Assert.Empty(FindItemsPathSegments.Split(path));
    }

    [Fact]
    public void Separator_is_the_one_the_paths_are_built_with()
    {
        // If BuildItemPath's separator ever changes, this is the single place that
        // has to change with it.
        Assert.Equal(" / ", FindItemsPathSegments.Separator);
    }
}
