using System.Text.Json;
using ModelContextProtocol.Client;
using NavisHelper.McpServer.Resources;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// The MCP call log must say which client (Claude, Codex, ChatGPT...) made a call:
// one client_connected entry per session, and client_name/client_version on
// every later entry.
[Collection("MCP stdio")]
public sealed class McpClientIdentityLogTests : IDisposable
{
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), "NavisHelper-ClientIdentity-" + Guid.NewGuid().ToString("N"));

    public McpClientIdentityLogTests() => Directory.CreateDirectory(_logDirectory);

    public void Dispose()
    {
        try { Directory.Delete(_logDirectory, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task SessionWritesClientConnectedEntryWithClientInfo()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using (var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "NavisHelper identity test",
            Command = "dotnet",
            Arguments = new[] { typeof(McpToolCatalogResources).Assembly.Location },
            EnvironmentVariables = new Dictionary<string, string> { ["NAVISHELPER_MCP_LOG_DIR"] = _logDirectory },
            ShutdownTimeout = TimeSpan.FromSeconds(5),
        }), new McpClientOptions { ClientInfo = new() { Name = "identity-probe", Version = "9.8.7" } }, cancellationToken: timeout.Token))
        {
            await client.ListToolsAsync(cancellationToken: timeout.Token);
        }

        var entries = Directory.GetFiles(_logDirectory, "mcp-calls-*.jsonl")
            .SelectMany(File.ReadAllLines)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .ToList();
        var connected = Assert.Single(entries, entry => entry.GetProperty("event_name").GetString() == "client_connected");
        Assert.Equal("identity-probe", connected.GetProperty("client_name").GetString());
        Assert.Equal("9.8.7", connected.GetProperty("client_version").GetString());
    }

    [Fact]
    public void LaterEntriesCarryTheClientOnceItIsKnown()
    {
        var previous = Environment.GetEnvironmentVariable("NAVISHELPER_MCP_LOG_DIR");
        Environment.SetEnvironmentVariable("NAVISHELPER_MCP_LOG_DIR", _logDirectory);
        try
        {
            var logger = new McpCallLogger();
            logger.Log(new { event_name = "before" });
            logger.SetClient("identity-probe", "9.8.7");
            logger.Log(new { event_name = "after", command = "host_status" });

            var lines = File.ReadAllLines(logger.LogFilePath).Select(line => JsonDocument.Parse(line).RootElement).ToList();
            var before = lines.Single(entry => entry.GetProperty("event_name").GetString() == "before");
            Assert.False(before.TryGetProperty("client_name", out var unknown) && unknown.ValueKind != JsonValueKind.Null);
            var after = lines.Single(entry => entry.GetProperty("event_name").GetString() == "after");
            Assert.Equal("identity-probe", after.GetProperty("client_name").GetString());
            Assert.Equal("9.8.7", after.GetProperty("client_version").GetString());
            Assert.Equal("host_status", after.GetProperty("command").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("NAVISHELPER_MCP_LOG_DIR", previous);
        }
    }
}
