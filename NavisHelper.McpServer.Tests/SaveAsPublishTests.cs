using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using NavisHelper.Agent.Contracts;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #363: an NWD converted from an NWF should carry its textures, as the Publish dialog's
// "Embed Textures" does, and the owner's nightly NWF -> NWD script also needs the 2018
// format. save_document_as gets embedTextures and fileVersion; Automation scripts get the
// same through an AddInPlugin, since NavisworksApplication only exposes SaveFile(path).
public sealed class SaveAsPublishTests
{
    [Theory]
    [InlineData("", true, null)]
    [InlineData(" ", true, null)]
    [InlineData("2018", true, 2018)]
    [InlineData("2027", true, 2027)]
    [InlineData("2014", false, null)]
    [InlineData("2028", false, null)]
    [InlineData("18", false, null)]
    [InlineData("current", false, null)]
    public void FileVersionsAreYears(string value, bool ok, int? expected)
    {
        Assert.Equal(ok, SaveAsOptionsRules.TryParseFileVersion(value, out var year));
        Assert.Equal(expected, year);
    }

    [Theory]
    [InlineData(@"D:\out\a.nwd", false, "", null)]
    [InlineData(@"D:\out\a.nwd", true, "2018", null)]
    [InlineData(@"D:\out\a.NWD", true, "", null)]
    [InlineData(@"D:\out\a.nwf", true, "", ".nwd")]
    [InlineData(@"D:\out\a.nwf", false, "2018", ".nwd")]
    [InlineData(@"D:\out\a.nwf", false, "", null)]
    [InlineData(@"D:\out\a.nwd", false, "2014", "fileVersion")]
    public void PublishOptionsNeedAnNwdTarget(string path, bool embedTextures, string fileVersion, string expectedError)
    {
        var error = SaveAsOptionsRules.DescribeError(path, embedTextures, fileVersion);
        if (expectedError == null)
            Assert.Null(error);
        else
            Assert.Contains(expectedError, error, StringComparison.Ordinal);
    }

    [Fact]
    public void ContractsCarryTheOptions()
    {
        var request = new SaveDocumentAsRequest { Path = @"D:\a.nwd", EmbedTextures = true, FileVersion = "2018" };
        Assert.True(request.EmbedTextures);
        var response = new SaveDocumentResponse { Published = true, TexturesEmbedded = true, FileVersion = "2018" };
        Assert.Equal("2018", response.FileVersion);
    }

    [Fact]
    public void TheToolExposesBothOptionsOff()
    {
        var method = McpReadOnlyMode.RegisteredToolMethods().Single(candidate => candidate.Name == "SaveDocumentAs");
        var tool = McpServerTool.Create(method, _ => throw new InvalidOperationException("not invoked"));
        var schema = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText())!;
        Assert.False(schema["properties"]!["embedTextures"]!["default"]!.GetValue<bool>());
        Assert.Equal("", schema["properties"]!["fileVersion"]!["default"]!.GetValue<string>());
    }

    [Fact]
    public void ThePluginPublishesWithEmbeddedTexturesAndKeepsTheFileResavable()
    {
        var services = Path.Combine(RepositoryPaths.Root, "NavisHelper", "Agent", "Services");
        var save = File.ReadAllText(Path.Combine(services, "DocumentCommandService.DocumentSave.cs"));
        Assert.Contains("SaveAsOptionsRules.DescribeError(", save, StringComparison.Ordinal);
        // Through reflection: NwdExportOptions and this TryPublishFile overload exist from 2026 only,
        // and one source builds for 2024-2027 (found by the L2 build of #363).
        Assert.Contains("\"TryPublishFile\"", save, StringComparison.Ordinal);
        Assert.Contains("SetEmbedTextures", save, StringComparison.Ordinal);
        Assert.Contains("AllowResave = true", save, StringComparison.Ordinal);
        Assert.Contains("DocumentFileVersion", save, StringComparison.Ordinal);

        var addIn = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "NavisHelper", "NwdPublishAddIn.cs"));
        Assert.Contains("AddInPlugin", addIn, StringComparison.Ordinal);
        Assert.Contains("\"PublishNwd\"", addIn, StringComparison.Ordinal);
    }
}
