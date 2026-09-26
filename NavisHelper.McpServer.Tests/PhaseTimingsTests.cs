using System.Diagnostics;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class PhaseTimingsTests
{
    [Fact]
    public void Add_AccumulatesTicksWithinOnePhase()
    {
        var timings = new PhaseTimings();

        timings.Add("collect", Stopwatch.Frequency / 2);
        timings.Add("collect", Stopwatch.Frequency);

        Assert.Equal(1500L, timings.GetMilliseconds("collect"));
    }

    [Fact]
    public void Format_KeepsFirstAddedOrderAndOneEntryPerPhase()
    {
        var timings = new PhaseTimings();

        timings.Add("select", Stopwatch.Frequency);
        timings.Add("collect", Stopwatch.Frequency / 2);
        timings.Add("collect", Stopwatch.Frequency / 2);

        Assert.Equal("select_ms=1000 collect_ms=1000", timings.Format());
    }

    [Fact]
    public void GetMilliseconds_ReturnsZeroForUnknownPhase()
    {
        var timings = new PhaseTimings();
        timings.Add("collect", Stopwatch.Frequency);

        Assert.Equal(0L, timings.GetMilliseconds("match"));
        Assert.Equal(string.Empty, new PhaseTimings().Format());
    }

    [Fact]
    public void GetMilliseconds_ConvertsTicksToFloorMilliseconds()
    {
        var timings = new PhaseTimings();

        timings.Add("match", Stopwatch.Frequency + 1);
        timings.Add("name_path", 1);

        Assert.Equal(1000L, timings.GetMilliseconds("match"));
        Assert.Equal(0L, timings.GetMilliseconds("name_path"));
    }
}
