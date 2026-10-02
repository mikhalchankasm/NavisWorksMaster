using System.Text.Json.Nodes;
using NavisHelper.McpConfigurator;
using Xunit;
using Configurator = NavisHelper.McpConfigurator.Program;

namespace NavisHelper.McpServer.Tests;

public sealed class ConfiguratorFileSafetyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NavisHelperTests", Guid.NewGuid().ToString("N"));

    public ConfiguratorFileSafetyTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("[1,2]")]
    [InlineData("\"settings\"")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("null")]
    public void ReadJsonObject_RejectsUnsupportedRootWithoutChangingFile(string json)
    {
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, json);

        Assert.Throws<InvalidDataException>(() => Configurator.ReadJsonObject(path));

        Assert.Equal(json, File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"custom\"")]
    public void EnsureObject_RejectsUnsupportedContainerWithoutChangingTree(string value)
    {
        var root = JsonNode.Parse("{\"mcpServers\":" + value + ",\"theme\":\"dark\"}").AsObject();
        var before = root.ToJsonString();

        Assert.Throws<InvalidDataException>(() => Configurator.EnsureObject(root, "mcpServers"));

        Assert.Equal(before, root.ToJsonString());
    }

    [Fact]
    public void ZCode_RejectsUnsupportedContainerWithoutChangingTree()
    {
        var root = JsonNode.Parse("{\"mcp\":{\"servers\":[\"custom\"]},\"theme\":\"dark\"}").AsObject();
        var before = root.ToJsonString();

        Assert.Throws<InvalidDataException>(() => ZCodeConfig.SetServer(root, "server.exe"));

        Assert.Equal(before, root.ToJsonString());
    }

    [Fact]
    public void WriteTextAtomic_FailedCommitCleansOwnedTemporaryFile()
    {
        var destination = Path.Combine(_directory, "config.json");
        Directory.CreateDirectory(destination);
        var sibling = Path.Combine(_directory, "unrelated.tmp");
        File.WriteAllText(sibling, "keep");

        var error = Record.Exception(() => Configurator.WriteTextAtomic(destination, "replacement"));
        Assert.True(error is IOException or UnauthorizedAccessException);

        Assert.True(Directory.Exists(destination));
        Assert.Equal("keep", File.ReadAllText(sibling));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void SupportedObject_RoundTripPreservesOtherSettings()
    {
        var path = Path.Combine(_directory, "config.json");
        File.WriteAllText(path, "{\"theme\":\"dark\",\"mcpServers\":{\"other\":{\"command\":\"other.exe\"}}}");
        var root = Configurator.ReadJsonObject(path);
        Configurator.EnsureObject(Configurator.EnsureObject(root, "mcpServers"), "navishelper")["command"] = "server.exe";

        Configurator.WriteTextAtomic(path, root.ToJsonString());

        var saved = Configurator.ReadJsonObject(path);
        Assert.Equal("dark", (string)saved["theme"]);
        Assert.Equal("other.exe", (string)saved["mcpServers"]["other"]["command"]);
        Assert.Equal("server.exe", (string)saved["mcpServers"]["navishelper"]["command"]);
        Assert.Single(Directory.GetFiles(_directory));
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
