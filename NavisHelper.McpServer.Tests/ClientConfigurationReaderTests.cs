using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NavisHelper.McpConfigurator;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ClientConfigurationReaderTests : IDisposable
{
    private const string Server = @"C:\Users\Test\AppData\Local\NavisHelper\McpServer-deadbee\NavisHelper.McpServer.exe";
    private const string Secret = "SENTINEL_TOKEN_DO_NOT_DISCLOSE";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NavisHelperTests", Guid.NewGuid().ToString("N"));

    public ClientConfigurationReaderTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("claude-desktop")]
    [InlineData("cursor")]
    [InlineData("kimi")]
    [InlineData("opencode")]
    [InlineData("zcode")]
    [InlineData("codex")]
    public void SupportedClient_ReturnsOnlyBinding_AndPreservesBytes(string client)
    {
        var content = client == "codex"
            ? "\uFEFF[mcp_servers.\"navishelper\"]\ncommand = '" + Server +
              "'\nargs = ['--token', '" + Secret + "']\n[mcp_servers.navishelper.env]\nKEY = '" + Secret + "'\n"
            : Config(client, new JsonObject
            {
                ["command"] = client == "opencode" ? new JsonArray(Server, "--token", Secret) : JsonValue.Create(Server),
                ["args"] = client == "opencode" ? null : new JsonArray("--token", Secret),
                ["env"] = new JsonObject { ["KEY"] = Secret }
            });
        var result = Inspect(client, content);
        Assert.Equal(new ClientBinding("configured", Server), result);
    }

    [Theory]
    [InlineData("dotnet")]
    [InlineData("dotnet.exe")]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe")]
    public void DotnetDllBinding_IsRecognized(string command)
    {
        var dll = Path.ChangeExtension(Server, ".dll");
        var result = Inspect("cursor", Config("cursor", new JsonObject
        {
            ["command"] = command, ["args"] = new JsonArray(dll, "--token", Secret)
        }));
        Assert.Equal(new ClientBinding("configured", dll), result);
    }

    [Theory]
    [InlineData("codex", "mcp_servers.navishelper.command = 'C:\\bin\\NavisHelper.McpServer.exe'")]
    [InlineData("codex", "mcp_servers = { navishelper = { command = 'C:\\bin\\NavisHelper.McpServer.exe' } }")]
    [InlineData("cursor", "{\"mcpServers\":{\"navishelper\":{\"command\":\"C:/bin/NavisHelper.McpServer.exe\"}}}")]
    public void AlternateSupportedSyntax_IsRecognized(string client, string content) =>
        Assert.Equal("configured", Inspect(client, content).Status);

    [Theory]
    [InlineData("cursor", "{}")]
    [InlineData("cursor", "")]
    [InlineData("zcode", "{\"mcp\":{\"servers\":{}}}")]
    [InlineData("codex", "theme = 'dark'")]
    public void MissingBinding_IsNotConfigured(string client, string content) =>
        Assert.Equal("not_configured", Inspect(client, content).Status);

    [Theory]
    [InlineData("opencode", "{\"mcp\":{\"navishelper\":{\"enabled\":false}}}")]
    [InlineData("codex", "[mcp_servers.navishelper]\nenabled = false")]
    public void DisabledBinding_DoesNotRequireExecutable(string client, string content) =>
        Assert.Equal(new ClientBinding("disabled"), Inspect(client, content));

    [Theory]
    [InlineData("cursor", "{\"mcpServers\":{\"navishelper\":{\"disabled\":true}}}")]
    [InlineData("claude-desktop", "{\"mcpServers\":{\"navishelper\":{\"enabled\":false}}}")]
    [InlineData("codex", "[mcp_servers.navishelper]\ndisabled = true")]
    [InlineData("codex", "[mcp_servers.navishelper]\nexperimental_environment = 'remote'\ncommand = 'C:\\bin\\NavisHelper.McpServer.exe'")]
    public void UndocumentedFlags_AreNotGuessed(string client, string content) =>
        Assert.Equal(new ClientBinding("unsupported_setting"), Inspect(client, content));

    [Theory]
    [InlineData("cursor", "[]")]
    [InlineData("cursor", "{\"mcpServers\":null}")]
    [InlineData("cursor", "{\"mcpServers\":{\"navishelper\":[]}}")]
    [InlineData("cursor", "{\"mcpServers\":{\"navishelper\":{\"command\":42}}}")]
    [InlineData("cursor", "{\"mcpServers\":{\"navishelper\":{\"enabled\":\"false\"}}}")]
    [InlineData("cursor", "{\"mcpServers\":{\"navishelper\":{\"command\":\"exe\",\"args\":[1]}}}")]
    [InlineData("cursor", "{\"mcpServers\":{},\"mcpServers\":{}}")]
    [InlineData("codex", "mcp_servers = 42")]
    [InlineData("codex", "[mcp_servers.navishelper]\ncommand = 'x'\ncommand = 'y'")]
    [InlineData("codex", "[mcp_servers.navishelper]\ncommand = 42")]
    [InlineData("codex", "[mcp_servers.navishelper]\ncommand = 'x'\nargs = [true]")]
    [InlineData("codex", "value = 9999999999999999999999999999999999999999999")]
    [InlineData("codex", "value = 9999-99-99T99:99:99Z")]
    public void InvalidShape_IsSanitized(string client, string content) =>
        Assert.Equal(new ClientBinding("malformed_config"), Inspect(client, content));

    [Theory]
    [InlineData("cmd", "/c")]
    [InlineData("powershell", "-Command")]
    [InlineData("dotnet", "exec")]
    [InlineData("NavisHelper.McpServer.exe", "")]
    [InlineData(@"\\server\share\NavisHelper.McpServer.exe", "")]
    [InlineData(@"C:\bin\Other.exe", "")]
    [InlineData("\"C:\\bin\\NavisHelper.McpServer.exe\"", "")]
    [InlineData("C:\\bad\npath\\NavisHelper.McpServer.exe", "")]
    [InlineData("C:\\bad\u202Epath\\NavisHelper.McpServer.exe", "")]
    [InlineData(@"C:\bin\NavisHelper.McpServer.exe --token", "")]
    public void UnsupportedCommand_NeverEchoesPayload(string command, string argument)
    {
        var result = Inspect("cursor", Config("cursor", new JsonObject
        {
            ["command"] = command,
            ["args"] = new JsonArray(argument, Server, Secret)
        }));
        Assert.Equal(new ClientBinding("unsupported_command"), result);
    }

    [Theory]
    [InlineData("cursor", "{\"mcpServers\":{\"navishelper\": SENTINEL_TOKEN_DO_NOT_DISCLOSE }}")]
    [InlineData("codex", "SENTINEL_TOKEN_DO_NOT_DISCLOSE = [")]
    public void ParseErrors_NeverEchoSourceText(string client, string content) =>
        Assert.Equal(new ClientBinding("malformed_config"), Inspect(client, content));

    [Fact]
    public void ExcessiveNesting_UsesExistingParserDepthLimits()
    {
        Assert.Equal(new ClientBinding("malformed_config"),
            Inspect("codex", "value = " + new string('[', 1000) + "0" + new string(']', 1000)));
        Assert.Equal(new ClientBinding("malformed_config"),
            Inspect("cursor", new string('[', 1000) + "0" + new string(']', 1000)));
    }

    [Fact]
    public void AmbiguousRemoteBinding_IsNotReportedAsLocal()
    {
        foreach (var field in new[] { "type", "url" })
        {
            var result = Inspect("cursor", Config("cursor", new JsonObject
            {
                ["command"] = Server, [field] = Secret
            }));
            Assert.Equal(new ClientBinding("unsupported_command"), result);
        }
    }

    [Fact]
    public void OversizedAndInvalidUtf8_AreBoundedAndSanitized()
    {
        Assert.Equal(new ClientBinding("config_too_large"), Inspect("cursor", new string(' ', ClientConfigurationReader.MaximumBytes + 1)));
        var path = Path.Combine(_directory, "invalid.json");
        File.WriteAllBytes(path, [0xC0, 0xAF]);
        Assert.Equal(new ClientBinding("malformed_config"), ClientConfigurationReader.Read("cursor", path));
        Assert.Equal(new byte[] { 0xC0, 0xAF }, File.ReadAllBytes(path));
    }

    [Fact]
    public void MissingFileAndUnsupportedClients_DoNotCreateAnything()
    {
        var missing = Path.Combine(_directory, "missing", "config.json");
        Assert.Equal(new ClientBinding("no_config"), ClientConfigurationReader.Read("cursor", missing));
        Assert.Equal(new ClientBinding("unsupported_client"), ClientConfigurationReader.Read("claude-code", missing));
        Assert.Equal(new ClientBinding("unsupported_client"), ClientConfigurationReader.Read("unknown", missing));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    private ClientBinding Inspect(string client, string content)
    {
        var path = Path.Combine(_directory, "config");
        var bytes = new UTF8Encoding(false).GetBytes(content);
        File.WriteAllBytes(path, bytes);
        var result = ClientConfigurationReader.Read(client, path);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(_directory));
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result));
        return result;
    }

    private static string Config(string client, JsonObject server) => (client switch
    {
        "opencode" => new JsonObject { ["mcp"] = new JsonObject { ["navishelper"] = server } },
        "zcode" => new JsonObject { ["mcp"] = new JsonObject { ["servers"] = new JsonObject { ["navishelper"] = server } } },
        _ => new JsonObject { ["mcpServers"] = new JsonObject { ["navishelper"] = server,
            ["other"] = new JsonObject { ["command"] = Secret } } }
    }).ToJsonString();

    public void Dispose() => Directory.Delete(_directory, true);
}
