using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;
using NavisHelper.McpServer.Services;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #419: five animation workflows as MCP prompts. Unlike review_clashes and audit_selection_properties they change
// the document (viewpoints, animations) and write export files, so they preview with apply=false and apply only
// after the user confirms; read-only mode and a profile without their tools return configuration guidance.
[Collection("MCP stdio")]
public sealed class McpAnimationPromptTests
{
    internal static readonly string[] AllPromptNames =
    {
        "animation_clash_walkthrough", "animation_export_video", "animation_from_viewpoints", "animation_route_flythrough",
        "animation_section_tour", "audit_selection_properties", "review_clashes",
    };

    [Theory]
    [InlineData("animation_route_flythrough", "core,sections", "viewpoints_per_item")]
    [InlineData("animation_section_tour", "core,sections", "viewpoints_per_item")]
    [InlineData("animation_clash_walkthrough", "core,sections,clash", "clash_save_viewpoints")]
    [InlineData("animation_from_viewpoints", "core,sections", "list_saved_viewpoints")]
    [InlineData("animation_export_video", "core,sections", "viewpoint_animation_export")]
    public async Task AnimationPromptsPreviewBeforeApplyingAndReferenceOnlyAvailableTools(string name, string profile, string sourceTool)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpCatalogResourceTests.ConnectAsync(profile, false, timeout.Token);
        var prompts = await client.ListPromptsAsync(cancellationToken: timeout.Token);
        Assert.Contains(prompts, prompt => prompt.Name == name && !string.IsNullOrWhiteSpace(prompt.Description));
        var result = await client.GetPromptAsync(name, cancellationToken: timeout.Token);
        var message = Assert.Single(result.Messages);
        Assert.Equal(Role.User, message.Role);
        var text = Assert.IsType<TextContentBlock>(message.Content).Text;
        Assert.DoesNotContain("Workflow unavailable", text);
        foreach (var expected in new[] { "not an executed change", "apply=false", "confirm", "instanceId", "Navisworks 2026", "document changed", "ffmpeg" })
            Assert.Contains(expected, text);
        var protocolTools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        var tools = protocolTools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        var referenced = Regex.Matches(text, "`([a-z][a-z0-9_]*)`").Select(match => match.Groups[1].Value).Distinct().ToArray();
        Assert.Contains(sourceTool, referenced);
        Assert.Contains("viewpoint_animation_export", referenced);
        if (name != "animation_export_video") Assert.Contains("viewpoint_animation", referenced);
        Assert.All(referenced, tool => Assert.Contains(tool, tools));
        var parameters = protocolTools.Where(tool => referenced.Contains(tool.Name))
            .SelectMany(tool => tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject())
            .Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        Assert.All(Regex.Matches(text, @"(?<![-\w])([a-zA-Z][a-zA-Z0-9]*)=")
            .Select(match => match.Groups[1].Value), parameter => Assert.Contains(parameter, parameters));
        Assert.DoesNotContain("save_document", text);
    }

    [Theory]
    [InlineData("animation_route_flythrough", "all", true)]
    [InlineData("animation_section_tour", "all", true)]
    [InlineData("animation_clash_walkthrough", "core,sections", false)]
    [InlineData("animation_from_viewpoints", "meta", false)]
    [InlineData("animation_export_video", "all", true)]
    public async Task ReadOnlyModeOrAMissingGroupReturnsConfigurationGuidance(string name, string profile, bool readOnly)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var client = await McpCatalogResourceTests.ConnectAsync(profile, readOnly, timeout.Token);
        var text = Assert.IsType<TextContentBlock>(Assert.Single((await client.GetPromptAsync(name, cancellationToken: timeout.Token)).Messages).Content).Text;
        Assert.Contains("Workflow unavailable", text);
        Assert.Contains(McpToolProfile.CommandLinePrefix, text);
        Assert.Empty(Regex.Matches(text, "`([a-z][a-z0-9_]*)`"));
    }
}
