using System.Reflection;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class McpToolProfileTests
{
    /// <summary>
    /// The registered tool surface, derived the same way
    /// <c>scripts/check_mcp_command_catalog.py</c> derives it: every method
    /// carrying <c>[McpServerTool]</c>, with the method name converted to
    /// snake_case. That conversion is already CI-enforced against the live
    /// surface, so reusing it keeps this test honest without starting a server.
    /// </summary>
    private static IReadOnlyList<string> RegisteredToolNames()
    {
        var names = typeof(McpToolProfile).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .Where(method => method.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false).Length > 0)
            .Select(method => PascalToSnake(method.Name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(names);
        return names;
    }

    private static string PascalToSnake(string value)
    {
        value = Regex.Replace(value, "(.)([A-Z][a-z]+)", "$1_$2");
        value = Regex.Replace(value, "([a-z0-9])([A-Z])", "$1_$2");
        return value.ToLowerInvariant();
    }

    [Fact]
    public void Sets_CoverEveryRegisteredTool()
    {
        var registered = RegisteredToolNames();
        var covered = McpToolProfile.Sets.Values
            .SelectMany(names => names)
            .ToHashSet(StringComparer.Ordinal);

        var uncovered = registered.Where(name => !covered.Contains(name)).ToList();
        Assert.True(
            uncovered.Count == 0,
            "These MCP tools belong to no set, so no profile can advertise them: " + string.Join(", ", uncovered));
    }

    [Fact]
    public void Sets_DoNotNameUnknownTools()
    {
        var registered = RegisteredToolNames().ToHashSet(StringComparer.Ordinal);
        var unknown = McpToolProfile.Sets
            .SelectMany(entry => entry.Value.Select(name => new { Set = entry.Key, Tool = name }))
            .Where(item => !registered.Contains(item.Tool))
            .Select(item => item.Set + ":" + item.Tool)
            .ToList();

        Assert.True(
            unknown.Count == 0,
            "These set entries name tools that do not exist: " + string.Join(", ", unknown));
    }

    [Fact]
    public void Sets_AreDisjoint()
    {
        var duplicates = McpToolProfile.Sets
            .SelectMany(entry => entry.Value)
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            "These tools appear in more than one set: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void Resolve_WithoutSpec_AdvertisesFullSurface()
    {
        var registered = RegisteredToolNames();

        foreach (var spec in new[] { null, string.Empty, "   " })
        {
            var selection = McpToolProfile.Resolve(spec, registered);
            Assert.True(selection.IsFullSurface);
            Assert.Equal(registered.Count, selection.ToolNames.Count);
        }
    }

    [Fact]
    public void Resolve_All_AdvertisesFullSurface()
    {
        var registered = RegisteredToolNames();
        var selection = McpToolProfile.Resolve(McpToolProfile.AllSetName, registered);

        Assert.True(selection.IsFullSurface);
        Assert.Equal(registered.Count, selection.ToolNames.Count);
    }

    [Fact]
    public void Resolve_EverySetTogether_EqualsFullSurface()
    {
        var registered = RegisteredToolNames();
        var spec = string.Join(",", McpToolProfile.Sets.Keys);
        var selection = McpToolProfile.Resolve(spec, registered);

        Assert.Equal(
            registered.OrderBy(name => name, StringComparer.Ordinal),
            selection.ToolNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Resolve_Core_IsStrictSubsetAndKeepsAlwaysOnTools()
    {
        var registered = RegisteredToolNames();
        var selection = McpToolProfile.Resolve(McpToolProfile.CoreSetName, registered);

        Assert.False(selection.IsFullSurface);
        Assert.True(selection.ToolNames.Count < registered.Count);
        Assert.All(McpToolProfile.AlwaysOnSet, name => Assert.Contains(name, selection.ToolNames));
        Assert.Contains("find_items", selection.ToolNames);
        Assert.DoesNotContain("clash_generate_report", selection.ToolNames);
    }

    [Fact]
    public void Resolve_KeepsAlwaysOnToolsEvenWhenSubtracted()
    {
        var registered = RegisteredToolNames();
        var selection = McpToolProfile.Resolve("clash,-meta", registered);

        Assert.All(McpToolProfile.AlwaysOnSet, name => Assert.Contains(name, selection.ToolNames));
        Assert.Contains("clash_generate_report", selection.ToolNames);
    }

    [Fact]
    public void Resolve_Subtraction_RemovesTheSubtractedSet()
    {
        var registered = RegisteredToolNames();
        var selection = McpToolProfile.Resolve("all,-clash", registered);

        Assert.DoesNotContain("clash_generate_report", selection.ToolNames);
        Assert.Contains("find_items", selection.ToolNames);
        Assert.False(selection.IsFullSurface);
    }

    [Fact]
    public void Resolve_UnknownSet_Throws()
    {
        var registered = RegisteredToolNames();
        var exception = Assert.Throws<InvalidOperationException>(
            () => McpToolProfile.Resolve("core,does_not_exist", registered));

        Assert.Contains("does_not_exist", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Known sets", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_NeverAdvertisesUnregisteredTools()
    {
        var registered = new[] { "find_items", "mcp_health_check" };
        var selection = McpToolProfile.Resolve("core,clash", registered);

        Assert.Equal(
            new[] { "find_items", "mcp_health_check" },
            selection.ToolNames.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void ReadSpec_PrefersCommandLineOverEnvironment()
    {
        var previous = Environment.GetEnvironmentVariable(McpToolProfile.EnvironmentVariableName);
        try
        {
            Environment.SetEnvironmentVariable(McpToolProfile.EnvironmentVariableName, "clash");

            Assert.Equal("core", McpToolProfile.ReadSpec(new[] { "--tools=core" }));
            Assert.Equal("clash", McpToolProfile.ReadSpec(Array.Empty<string>()));

            Environment.SetEnvironmentVariable(McpToolProfile.EnvironmentVariableName, null);
            Assert.Null(McpToolProfile.ReadSpec(Array.Empty<string>()));
        }
        finally
        {
            Environment.SetEnvironmentVariable(McpToolProfile.EnvironmentVariableName, previous);
        }
    }

    [Fact]
    public void ReadSpec_UsesTheLastCommandLineOccurrence()
    {
        Assert.Equal(
            "clash",
            McpToolProfile.ReadSpec(new[] { "--tools=core", "--other", "--tools=clash" }));
    }
}
