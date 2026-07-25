using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class HeightMarkGroupNameHelperTests
{
    [Fact]
    public void Suggest_UsesInitialsForMultiWordNames()
    {
        var result = HeightMarkGroupNameHelper.Suggest(new[]
        {
            "Supply Air Duct",
            "Return Air Duct",
            "Exhaust Air Duct",
        });

        Assert.Equal("SAD + RAD + EAD", result);
    }

    [Fact]
    public void Suggest_TruncatesSingleLongName()
    {
        var result = HeightMarkGroupNameHelper.Suggest(new[] { "VeryLongObjectName" });

        Assert.Equal("VeryLongObje", result);
    }

    [Fact]
    public void Suggest_DeduplicatesAndLimitsSuggestions()
    {
        var result = HeightMarkGroupNameHelper.Suggest(new[]
        {
            "Pipe Support",
            "Pipe Support",
            "Cable Tray",
            "Equipment Base",
            "Ignored Name",
        });

        Assert.Equal("PS + CT + EB", result);
    }

    [Fact]
    public void Suggest_UsesFallbackForEmptyNames()
    {
        Assert.Equal("Группа высот", HeightMarkGroupNameHelper.Suggest(new[] { null, " " }));
    }
}
