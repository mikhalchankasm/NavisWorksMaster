using NavisHelper.Agent.Contracts;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #311: a WH_KEYBOARD thread hook gets every keyboard message of Navisworks' UI
// thread; the plugin acts only on the key-downs the old WH_KEYBOARD_LL hook passed on.
// lParam bits: 0-15 repeat count, 29 Alt context, 30 previous state, 31 transition.
public sealed class KeyboardHookPolicyTests
{
    private const int HcAction = 0;
    private const int HcNoRemove = 3;
    private const long RepeatOnce = 1;
    private const long AltContext = 1L << 29;
    private const long PreviouslyDown = 1L << 30;
    private const long KeyUp = 1L << 31;

    [Fact]
    public void KeyDownIsDispatched() =>
        Assert.True(KeyboardHookPolicy.ShouldDispatch(HcAction, RepeatOnce));

    [Fact]
    public void AutoRepeatIsDispatchedAsBefore() =>
        Assert.True(KeyboardHookPolicy.ShouldDispatch(HcAction, RepeatOnce | PreviouslyDown));

    [Fact]
    public void KeyUpIsNotDispatched() =>
        Assert.False(KeyboardHookPolicy.ShouldDispatch(HcAction, RepeatOnce | PreviouslyDown | KeyUp));

    [Fact]
    public void AltContextIsNotDispatchedLikeTheOldSysKeyDown() =>
        Assert.False(KeyboardHookPolicy.ShouldDispatch(HcAction, RepeatOnce | AltContext));

    [Theory]
    [InlineData(HcNoRemove)]
    [InlineData(-1)]
    public void OnlyHcActionIsDispatched(int nCode) =>
        Assert.False(KeyboardHookPolicy.ShouldDispatch(nCode, RepeatOnce));

    [Fact]
    public void HighBitsOfA64BitLParamDoNotMatter() =>
        Assert.True(KeyboardHookPolicy.ShouldDispatch(HcAction, unchecked((long)0xFFFFFFFF00000001)));
}
