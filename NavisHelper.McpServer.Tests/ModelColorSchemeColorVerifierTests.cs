using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class ModelColorSchemeColorVerifierTests
{
    // Colors from the 2026-09-26 apply that reported 0 of 100 matches while the
    // view showed every group in its requested color.
    [Theory]
    [InlineData(0x20, 0x92, 0xC8)]
    [InlineData(0xF9, 0xFC, 0x41)]
    [InlineData(0xDB, 0x46, 0xC2)]
    [InlineData(0x12, 0x45, 0xFC)]
    [InlineData(0xD8, 0x41, 0x79)]
    public void ChannelsMatch_AcceptsSinglePrecisionReadBack(int r, int g, int b)
    {
        var requested = FromBytes(r, g, b);
        var readBack = SinglePrecision(requested);

        Assert.NotEqual(requested.R, readBack.R);
        Assert.True(ModelColorSchemeColorVerifier.ChannelsMatch(requested, readBack));
    }

    [Fact]
    public void ChannelsMatch_RejectsOneStepDifference()
    {
        Assert.False(ModelColorSchemeColorVerifier.ChannelsMatch(
            FromBytes(0x20, 0x20, 0x20),
            FromBytes(0x20, 0x21, 0x20)));
    }

    [Fact]
    public void ChannelsMatch_RejectsNonFiniteChannels()
    {
        var requested = FromBytes(0x20, 0x92, 0xC8);

        Assert.False(ModelColorSchemeColorVerifier.ChannelsMatch(
            requested,
            new ModelColorSchemeRgb(double.NaN, requested.G, requested.B)));
        Assert.False(ModelColorSchemeColorVerifier.ChannelsMatch(
            new ModelColorSchemeRgb(double.NaN, 0, 0),
            new ModelColorSchemeRgb(double.NaN, 0, 0)));
    }

    [Fact]
    public void Tally_SuccessfulApplyMatchesEverySampleWithoutWarning()
    {
        var response = new ModelColorSchemeResponse();
        var samples = new List<ModelColorSchemeColorSample>();
        for (var index = 0; index < 100; index++)
        {
            var requested = FromBytes(0x20 + index, 0x92, 0xC8);
            samples.Add(new ModelColorSchemeColorSample
            {
                Requested = requested,
                Permanent = SinglePrecision(requested),
                Active = SinglePrecision(requested),
            });
        }

        ModelColorSchemeColorVerifier.Tally(samples, response);

        Assert.Equal(100, response.ColorVerificationSampleCount);
        Assert.Equal(100, response.PermanentColorMatchCount);
        Assert.Equal(100, response.ActiveColorMatchCount);
        Assert.Empty(response.Warnings);
    }

    [Fact]
    public void Tally_RetainedOriginalColorStillWarns()
    {
        var response = new ModelColorSchemeResponse();
        var requested = FromBytes(0x20, 0x92, 0xC8);
        var original = SinglePrecision(FromBytes(0x80, 0x80, 0x80));
        var samples = new[]
        {
            new ModelColorSchemeColorSample
            {
                Requested = requested,
                Permanent = SinglePrecision(requested),
                Active = SinglePrecision(requested),
            },
            new ModelColorSchemeColorSample
            {
                Requested = requested,
                Permanent = original,
                Active = original,
            },
        };

        ModelColorSchemeColorVerifier.Tally(samples, response);

        Assert.Equal(2, response.ColorVerificationSampleCount);
        Assert.Equal(1, response.PermanentColorMatchCount);
        Assert.Equal(1, response.ActiveColorMatchCount);
        var warning = Assert.Single(response.Warnings);
        Assert.StartsWith(ModelColorSchemeColorVerifier.PermanentMismatchWarning, warning);
        Assert.Contains("1 of 2 samples differ", warning);
        Assert.Contains("requested #2092C8, read #808080", warning);
    }

    [Fact]
    public void Tally_MaskedActiveColorWarnsSeparately()
    {
        var response = new ModelColorSchemeResponse();
        var requested = FromBytes(0x20, 0x92, 0xC8);
        var samples = new[]
        {
            new ModelColorSchemeColorSample
            {
                Requested = requested,
                Permanent = SinglePrecision(requested),
                Active = FromBytes(0x00, 0x00, 0xFF),
            },
        };

        ModelColorSchemeColorVerifier.Tally(samples, response);

        Assert.Equal(1, response.PermanentColorMatchCount);
        Assert.Equal(0, response.ActiveColorMatchCount);
        var warning = Assert.Single(response.Warnings);
        Assert.StartsWith(ModelColorSchemeColorVerifier.ActiveMismatchWarning, warning);
        Assert.Contains("requested #2092C8, read #0000FF", warning);
    }

    [Fact]
    public void Tally_UnreadableColorCountsAsMismatchAndReportsTheError()
    {
        var response = new ModelColorSchemeResponse();
        var samples = new[]
        {
            new ModelColorSchemeColorSample
            {
                Requested = FromBytes(0x20, 0x92, 0xC8),
                ReadError = "Object has been disposed.",
            },
        };

        ModelColorSchemeColorVerifier.Tally(samples, response);

        Assert.Equal(1, response.ColorVerificationSampleCount);
        Assert.Equal(0, response.PermanentColorMatchCount);
        var warning = Assert.Single(response.Warnings);
        Assert.Contains("read unreadable (Object has been disposed.)", warning);
    }

    private static ModelColorSchemeRgb FromBytes(int r, int g, int b)
    {
        return new ModelColorSchemeRgb(r / 255.0, g / 255.0, b / 255.0);
    }

    private static ModelColorSchemeRgb SinglePrecision(ModelColorSchemeRgb color)
    {
        return new ModelColorSchemeRgb((float)color.R, (float)color.G, (float)color.B);
    }
}
