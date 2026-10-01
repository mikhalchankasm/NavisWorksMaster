using System.Text.Json.Nodes;
using NavisHelper.McpConfigurator;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ZCodeConfigTests
{
    private const string ServerPath = @"C:\Tools\NavisHelper\NavisHelper.McpServer.exe";

    [Fact]
    public void SetServer_CreatesMcpServersTreeInEmptyConfig()
    {
        var root = new JsonObject();

        var changed = ZCodeConfig.SetServer(root, ServerPath);

        Assert.True(changed);
        Assert.Equal(ServerPath, (string)root["mcp"]?["servers"]?["navishelper"]?["command"]);
        Assert.Empty(root["mcp"]!["servers"]!["navishelper"]!["args"]!.AsArray());
    }

    [Fact]
    public void SetServer_KeepsOtherServersAndTopLevelKeys()
    {
        var root = JsonNode.Parse("""
            {
              "plugins": { "core": true },
              "mcp": {
                "timeout": 30,
                "servers": {
                  "other": { "command": "other.exe", "args": [ "--flag" ] }
                }
              }
            }
            """)!.AsObject();

        var changed = ZCodeConfig.SetServer(root, ServerPath);

        Assert.True(changed);
        Assert.True(root["plugins"]!["core"]!.GetValue<bool>());
        Assert.Equal(30, root["mcp"]!["timeout"]!.GetValue<int>());
        Assert.Equal("other.exe", (string)root["mcp"]!["servers"]!["other"]!["command"]);
        Assert.Equal(ServerPath, (string)root["mcp"]!["servers"]!["navishelper"]!["command"]);
    }

    [Fact]
    public void SetServer_ReplacesOldServerPath()
    {
        var root = new JsonObject();
        ZCodeConfig.SetServer(root, @"C:\Old\NavisHelper.McpServer.exe");

        var changed = ZCodeConfig.SetServer(root, ServerPath);

        Assert.True(changed);
        Assert.Equal(ServerPath, (string)root["mcp"]?["servers"]?["navishelper"]?["command"]);
    }

    [Fact]
    public void SetServer_SecondRunWithSamePathChangesNothing()
    {
        var root = new JsonObject();
        ZCodeConfig.SetServer(root, ServerPath);
        var snapshot = root.ToJsonString();

        var changed = ZCodeConfig.SetServer(root, ServerPath);

        Assert.False(changed);
        Assert.Equal(snapshot, root.ToJsonString());
    }

    [Fact]
    public void RemoveServer_RemovesEntryAndKeepsSiblingKeys()
    {
        var root = JsonNode.Parse("""
            {
              "plugins": { "core": true },
              "mcp": {
                "servers": {
                  "navishelper": { "command": "C:\\Old\\NavisHelper.McpServer.exe", "args": [] },
                  "other": { "command": "other.exe" }
                }
              }
            }
            """)!.AsObject();

        var changed = ZCodeConfig.RemoveServer(root);

        Assert.True(changed);
        Assert.Null(root["mcp"]!["servers"]!["navishelper"]);
        Assert.Equal("other.exe", (string)root["mcp"]!["servers"]!["other"]!["command"]);
        Assert.True(root["plugins"]!["core"]!.GetValue<bool>());
    }

    [Fact]
    public void RemoveServer_WithoutEntryChangesNothing()
    {
        var root = new JsonObject();

        var changed = ZCodeConfig.RemoveServer(root);

        Assert.False(changed);
        Assert.Null(root["mcp"]);
    }
}
