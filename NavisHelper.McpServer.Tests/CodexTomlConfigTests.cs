using NavisHelper.McpConfigurator;
using System;
using System.IO;
using System.Text;
using Tomlyn;
using Tomlyn.Model;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class CodexTomlConfigTests
{
    [Theory]
    [InlineData("[mcp_servers.NavisHelper]\ncommand = 'other'\n")]
    [InlineData("# comment\r\nmodel = 'keep'  \r\n\r\n")]
    [InlineData("[unrelated]\nmcp_servers.navishelper.command = 'keep'\n")]
    [InlineData("instructions = '''\n[mcp_servers.navishelper]\nkeep = 'text'\n'''\n[mcp_servers.other]\ncommand = 'other'\n")]
    public void Remove_WithoutTargetIsByteIdentical(string text)
    {
        Assert.Equal(text, Remove(text));
    }

    [Fact]
    public void Remove_PreservesFollowingArrayTables()
    {
        const string text = "[mcp_servers.navishelper]\ncommand = 'old'\n[[tools]]\nname = 'keep'\n";

        var result = Remove(text);

        Assert.DoesNotContain("command = 'old'", result);
        Assert.Contains("[[tools]]\nname = 'keep'", result);
    }

    [Theory]
    [InlineData("[\"mcp_servers\".'navishelper']\ncommand = 'old'\n")]
    [InlineData("[mcp_servers.\"navishelper\"]\ncommand = 'old'\n")]
    [InlineData("[mcp_servers.navishelper]\ncommand = 'old'\n")]
    [InlineData("[mcp_servers.\"navis\\u0068elper\"]\ncommand = 'old'\n")]
    [InlineData("mcp_servers.navishelper.command = 'old'\n")]
    [InlineData("[mcp_servers.navishelper.env]\nKEY = 'value'\n")]
    [InlineData("[mcp_servers.navishelper]\ncommand = 'old'")]
    [InlineData("[mcp_servers.navishelper]")]
    [InlineData("[mcp_servers.navishelper]\nargs = [\n'one',\n 'two'\n]\n[[mcp_servers.navishelper.custom]]\nvalue = 1\n")]
    public void Remove_RecognizesEquivalentQuotedKeys(string text)
    {
        Assert.Equal(string.Empty, Remove(text).Trim());
    }

    [Fact]
    public void Remove_RecognizesParentTableRelativeKeys()
    {
        const string text = "[mcp_servers]\nnavishelper.command = 'old'\nother.command = 'keep'\n";

        var result = Remove(text);

        Assert.DoesNotContain("navishelper.command", result);
        Assert.Contains("other.command = 'keep'", result);
    }

    private static string Remove(string text) => CodexTomlConfig.Remove(text);

    [Theory]
    [InlineData("[mcp_servers.navishelper_old]\ncommand = 'keep'\n")]
    [InlineData("['mcp_servers.navishelper']\ncommand = 'keep'\n")]
    [InlineData("# other server\n[mcp_servers.other]\ncommand = 'keep'\n")]
    [InlineData("[[tools]]\nname = 'keep'\n[[tools]]\nname = 'also keep'\n")]
    public void Remove_PreservesUnrelatedSyntaxAfterTarget(string remaining)
    {
        Assert.Equal(remaining, Remove("[mcp_servers.navishelper]\ncommand = 'old'\n" + remaining));
    }

    [Fact]
    public void Remove_HandlesInlineServerInsideRegularParent()
    {
        Assert.Equal("[mcp_servers]\nother = { command = 'keep' }\n", Remove(
            "[mcp_servers]\nnavishelper = { command = 'old' }\nother = { command = 'keep' }\n"));
    }

    [Theory]
    [InlineData("mcp_servers = { navishelper = { command = 'old' }, other = { command = 'keep' } }\n")]
    [InlineData("[mcp_servers.navishelper\ncommand = 'old'\n")]
    [InlineData("mcp_servers = []\n")]
    [InlineData("[mcp_servers]\nnavishelper = 1\n")]
    [InlineData("value = 1\nvalue = 2\n")]
    [InlineData("[mcp_servers]\n[mcp_servers]\n")]
    public void UnsupportedOrMalformed_RejectsBeforeWriting(string text)
    {
        Assert.Throws<InvalidDataException>(() => Remove(text));
        Assert.Throws<InvalidDataException>(() => CodexTomlConfig.Configure(text, "new"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\uFEFF# preserved\r\nmodel = 'keep'  \r\n\r\n")]
    [InlineData("[mcp_servers]\nnavishelper.command = 'old'\nother.command = 'keep'\n")]
    [InlineData("[mcp_servers.other]\ncommand = 'keep'\n")]
    [InlineData("notes = '''\n[mcp_servers.navishelper]\nkeep\n'''\n")]
    public void Configure_PreservesOtherSettingsAndIsByteIdempotent(string text)
    {
        const string command = "C:\\Программа\\a\"b\t\n.exe";
        var result = CodexTomlConfig.Configure(text, command);
        Assert.Equal(result, CodexTomlConfig.Configure(result, command));
        var parsed = TomlSerializer.Deserialize<TomlTable>(result.TrimStart('\uFEFF'));
        var server = (TomlTable)((TomlTable)parsed["mcp_servers"])["navishelper"];
        Assert.Equal(command, server["command"]);
        Assert.Empty((TomlArray)server["args"]);
        if (!text.Contains("navishelper.command"))
            Assert.StartsWith(text, result);
        if (text.Contains("\r\n"))
            Assert.DoesNotContain("\n", result.Replace("\r\n", ""));
    }

    [Fact]
    public void ReadFile_PreservesBomAndRejectsInvalidUtf8()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[] { 0xef, 0xbb, 0xbf, 35 });
            Assert.Equal("\uFEFF#", CodexTomlConfig.ReadFile(path));
            var invalid = new byte[] { 0xff, 0xfe, 65, 0 };
            File.WriteAllBytes(path, invalid);
            Assert.Throws<DecoderFallbackException>(() => CodexTomlConfig.ReadFile(path));
            Assert.Equal(invalid, File.ReadAllBytes(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Configure_ChangingCommandDoesNotAccumulateWhitespace()
    {
        const string original = "# preserved\nmodel = 'keep'\n";
        Assert.Equal(CodexTomlConfig.Configure(original, "new"),
            CodexTomlConfig.Configure(CodexTomlConfig.Configure(original, "old"), "new"));
    }

    [Fact]
    public void Remove_PreservesCrLfAndForeignTrailingComments()
    {
        const string remaining = "# other\r\n[mcp_servers.other]\r\ncommand = 'keep' # note\r\n";
        Assert.Equal(remaining, Remove("[mcp_servers.navishelper]\r\ncommand = 'old' # own\r\n" + remaining));
    }
}
