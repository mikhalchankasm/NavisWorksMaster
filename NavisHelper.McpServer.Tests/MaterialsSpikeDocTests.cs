using Xunit;

namespace NavisHelper.McpServer.Tests;

// #361 starts with an API spike: what the Navisworks .NET and COM APIs expose for Autodesk
// materials, textures and texture mapping in 2024-2027, before any tool is designed. The
// spike's result is a research note that answers each wanted capability from the issue
// and names what can only be settled live.
public sealed class MaterialsSpikeDocTests
{
    private static readonly string Note = Path.Combine(RepositoryPaths.Root, "docs", "research", "MATERIALS_TEXTURE_API.md");

    [Fact]
    public void TheNoteExistsWithItsSections()
    {
        Assert.True(File.Exists(Note), "docs/research/MATERIALS_TEXTURE_API.md is missing");
        var text = File.ReadAllText(Note);
        foreach (var heading in new[] { "## Reachable", "## Not reachable", "## By Navisworks version", "## Proposed tools", "## Live checks" })
            Assert.Contains(heading, text, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryWantedCapabilityIsAnswered()
    {
        var text = File.ReadAllText(Note);
        // The five capabilities #361 asks for, in its order.
        foreach (var capability in new[] { "mapping per item", "list materials", "create a material", "texture transform", "fit texture to item" })
            Assert.Contains(capability, text, StringComparison.OrdinalIgnoreCase);
        foreach (var api in new[] { "MaterialOverride", "AppearanceOverrides", "LcOpTextureSpace", "InwOaMaterial" })
            Assert.Contains(api, text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNoteIsLinkedFromTheApiNotes()
    {
        var notes = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "docs", "research", "navisworks-api-notes.md"));
        Assert.Contains("MATERIALS_TEXTURE_API.md", notes, StringComparison.Ordinal);
    }
}
