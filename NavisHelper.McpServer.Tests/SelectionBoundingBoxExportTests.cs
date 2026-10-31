using System.Globalization;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using NavisHelper.McpServer.Tools;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #427: bulk bounding boxes had no compact path. selected_items_tree with boxes pages at about
// 80 KB, and find_items_by_bbox shows at most 50 rows, so 10 000 boxes took a dozen pages of
// context. selection_export_bounding_boxes writes one CSV row per selected item and answers
// with a summary that does not grow with the selection.
public sealed class SelectionBoundingBoxExportTests
{
    [Fact]
    public void TheHeaderNamesOneColumnPerField()
    {
        Assert.Equal(
            "selection_index,name,path,source_file,min_x,min_y,min_z,max_x,max_y,max_z",
            SelectionBoundingBoxExportRules.Header);
    }

    [Fact]
    public void ARowWritesInvariantNumbersAndQuotesOnlyWhenNeeded()
    {
        var box = new BoundingBoxInfo
        {
            Min = new Point3Info { X = -1.5, Y = 0, Z = 2.25 },
            Max = new Point3Info { X = 1000.125, Y = 3, Z = 12.5 },
        };

        var previous = CultureInfo.CurrentCulture;
        try
        {
            // A comma decimal separator would split every coordinate into two columns.
            CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
            Assert.Equal(
                "7,Pump P-101,\"Site, A/Pump \"\"P\"\"\",a.nwc,-1.5,0,2.25,1000.125,3,12.5",
                SelectionBoundingBoxExportRules.Row(7, "Pump P-101", "Site, A/Pump \"P\"", "a.nwc", box));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AnItemWithoutABoxKeepsItsRowWithEmptyCoordinates()
    {
        Assert.Equal(
            "3,Group,Site/Group" + new string(',', 7),
            SelectionBoundingBoxExportRules.Row(3, "Group", "Site/Group", null, null));
    }

    [Theory]
    [InlineData("", "outputPath is required.")]
    [InlineData("   ", "outputPath is required.")]
    [InlineData("boxes.csv", "outputPath must be absolute.")]
    [InlineData(@"C:\out\boxes.json", "outputPath must end in .csv.")]
    [InlineData(@"C:\out\boxes.CSV", null)]
    public void RequestsAreValidated(string outputPath, string expected)
    {
        var request = new SelectionExportBoundingBoxesRequest { OutputPath = outputPath };
        Assert.Equal(expected, SelectionBoundingBoxExportRules.Describe(request));
    }

    [Fact]
    public void ANegativeOffsetIsRefused()
    {
        var request = new SelectionExportBoundingBoxesRequest { OutputPath = @"C:\out\boxes.csv", Offset = -1 };
        Assert.Equal("offset must be 0 or greater.", SelectionBoundingBoxExportRules.Describe(request));
    }

    [Theory]
    [InlineData(null, 10000)]
    [InlineData(0, 1)]
    [InlineData(500, 500)]
    [InlineData(200000, 100000)]
    public void TheItemCountIsBounded(int? requested, int expected)
    {
        Assert.Equal(expected, SelectionBoundingBoxExportRules.ClampMaxItems(requested));
    }

    [Fact]
    public void TheAnswerIsASummaryUnderTwoKilobytes()
    {
        foreach (var property in typeof(SelectionExportBoundingBoxesResponse).GetProperties())
        {
            var isCollection = property.PropertyType != typeof(string) &&
                typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType);
            Assert.False(isCollection, $"{property.Name} would grow the answer with the selection.");
        }

        var response = new SelectionExportBoundingBoxesResponse
        {
            Applied = true,
            OutputPath = @"C:\" + new string('я', 240) + ".csv",
            SelectedItemCount = 100000,
            Offset = 90000,
            NextOffset = 99999,
            ExportedItemCount = 100000,
            ItemsWithoutBoxCount = 100000,
            ItemsTruncated = true,
            DeadlineTruncated = true,
            FileSizeBytes = long.MaxValue,
            Sha256 = new string('f', 64),
            Units = "Millimeters",
            Message = new string('x', 200),
        };
        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

        // The timing block the server adds takes the rest of the 2 KB.
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) < 1536, json);
    }

    [Fact]
    public void TheToolIsADryRunFirstFileWriter()
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == "SelectionExportBoundingBoxes");
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
        Assert.Equal("selection_export_bounding_boxes", tool.ProtocolTool.Name);
        Assert.Equal(ToolEffects.Files, method.GetCustomAttribute<ToolCapabilitiesAttribute>()!.Effects);

        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!;
        var properties = schema["properties"]!;
        Assert.False(properties["apply"]!["default"]!.GetValue<bool>());
        Assert.False(properties["overwrite"]!["default"]!.GetValue<bool>());
        Assert.Equal(10000, properties["maxItems"]!["default"]!.GetValue<int>());
        Assert.Equal(0, properties["offset"]!["default"]!.GetValue<int>());
        Assert.Contains("outputPath", schema["required"]!.AsArray().Select(node => node!.GetValue<string>()));

        Assert.Equal("selection_export_bounding_boxes", HostCommandNames.SelectionExportBoundingBoxes);
        Assert.True(HostCommandEffects.IsKnown(HostCommandNames.SelectionExportBoundingBoxes));
    }

    [Fact]
    public void ThePluginWritesThroughTheVerifiedWriterAndPagesByOffset()
    {
        var service = File.ReadAllText(Path.Combine(
            RepositoryPaths.Root, "NavisHelper", "Agent", "Services", "SelectionBoundingBoxExportService.cs"));
        Assert.Contains("SelectionBoundingBoxExportRules.Describe(", service, StringComparison.Ordinal);
        Assert.Contains("SelectionBoundingBoxExportRules.ClampMaxItems(", service, StringComparison.Ordinal);
        Assert.Contains("SelectionBoundingBoxExportRules.Header", service, StringComparison.Ordinal);
        Assert.Contains("SelectionBoundingBoxExportRules.Row(", service, StringComparison.Ordinal);
        Assert.Contains("TryBuildBoundingBoxInfo(", service, StringComparison.Ordinal);
        Assert.Contains("VerifiedFileArtifactWriter.WriteUtf8(", service, StringComparison.Ordinal);
        // Without a byte order mark Excel reads the CSV in the ANSI code page and garbles
        // Cyrillic item names; selection_export_properties writes one for the same reason.
        Assert.Contains("csv.Append('\\uFEFF')", service, StringComparison.Ordinal);
        Assert.Contains("HostRequestDeadline.Current", service, StringComparison.Ordinal);
        Assert.Contains("SelectedItemsTreePaging.NextOffset(", service, StringComparison.Ordinal);
    }
}
