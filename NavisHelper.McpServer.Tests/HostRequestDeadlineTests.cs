using System;
using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

public sealed class HostRequestDeadlineTests
{
    private sealed class FakeClock
    {
        public long ElapsedMs;

        public long Read()
        {
            return ElapsedMs;
        }
    }

    [Fact]
    public void Remaining_TracksTheInjectedClock()
    {
        var clock = new FakeClock();
        using (HostRequestDeadline.Begin(1000, clock.Read))
        {
            Assert.Equal(1000, HostRequestDeadline.Current.Remaining);
            clock.ElapsedMs = 400;
            Assert.Equal(600, HostRequestDeadline.Current.Remaining);
        }
    }

    [Fact]
    public void Remaining_CountsTimeSpentBeforeBeginOnlyOnce()
    {
        // A 60 s request with a 6 s reserve whose UI callback started 20 s late.
        var clock = new FakeClock { ElapsedMs = 20000 };
        using (HostRequestDeadline.Begin(60000 - 6000, clock.Read))
        {
            Assert.Equal(34000, HostRequestDeadline.Current.Remaining);
        }
    }

    [Fact]
    public void Expired_WhenTheBudgetIsSpent()
    {
        var clock = new FakeClock { ElapsedMs = 1500 };
        using (HostRequestDeadline.Begin(1000, clock.Read))
        {
            Assert.True(HostRequestDeadline.Current.Expired);
            Assert.Equal(0, HostRequestDeadline.Current.Remaining);
        }
    }

    [Fact]
    public void Expired_AtExactlyZeroRemaining()
    {
        var clock = new FakeClock { ElapsedMs = 1000 };
        using (HostRequestDeadline.Begin(1000, clock.Read))
        {
            Assert.True(HostRequestDeadline.Current.Expired);
        }
    }

    [Fact]
    public void NegativeBudget_IsExpiredImmediately()
    {
        using (HostRequestDeadline.Begin(-5000, () => 0))
        {
            Assert.True(HostRequestDeadline.Current.Expired);
            Assert.Equal(0, HostRequestDeadline.Current.Remaining);
        }
    }

    [Fact]
    public void NestedBegin_RestoresThePreviousDeadlineOnDispose()
    {
        var clock = new FakeClock();
        using (HostRequestDeadline.Begin(5000, clock.Read))
        {
            using (HostRequestDeadline.Begin(100, clock.Read))
            {
                Assert.Equal(100, HostRequestDeadline.Current.Remaining);
            }

            Assert.Equal(5000, HostRequestDeadline.Current.Remaining);
        }
    }

    [Fact]
    public void Current_IsNullOutsideAnyScope()
    {
        Assert.Null(HostRequestDeadline.Current);
        using (HostRequestDeadline.Begin(1000, () => 0))
        {
            Assert.NotNull(HostRequestDeadline.Current);
        }
        Assert.Null(HostRequestDeadline.Current);
    }

    [Fact]
    public void Begin_WithoutAClockNeverExpires()
    {
        using (HostRequestDeadline.Begin(1, null))
        {
            Assert.False(HostRequestDeadline.Current.Expired);
            Assert.Equal(1, HostRequestDeadline.Current.Remaining);
        }
    }
}
