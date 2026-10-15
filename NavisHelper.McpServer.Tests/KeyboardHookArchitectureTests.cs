using System.Text.RegularExpressions;
using Xunit;

namespace NavisHelper.McpServer.Tests;

// #311: WH_KEYBOARD_LL made every keystroke on the machine wait for Navisworks' UI
// thread, and Windows silently drops such a hook after repeated timeouts. The plugin
// is not loadable here, so the shape of KeyboardHook.cs is checked as source.
public sealed class KeyboardHookArchitectureTests
{
    private static readonly string Source = File.ReadAllText(
        Path.Combine(RepositoryPaths.Root, "NavisHelper", "Core", "KeyboardHook.cs"));

    private static string Callback()
    {
        var match = Regex.Match(Source, @"private\s+IntPtr\s+HookCallback\s*\([^)]*\)\s*\{");
        Assert.True(match.Success, "HookCallback(int, IntPtr, IntPtr) was not found.");
        var depth = 0;
        for (var index = match.Index + match.Length - 1; index < Source.Length; index++)
        {
            if (Source[index] == '{') depth++;
            else if (Source[index] == '}' && --depth == 0)
                return Source.Substring(match.Index, index - match.Index + 1);
        }
        throw new InvalidOperationException("HookCallback body is not closed.");
    }

    [Fact]
    public void TheHookIsAThreadHookNotAGlobalLowLevelHook()
    {
        Assert.DoesNotContain("WH_KEYBOARD_LL", Source, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"SetWindowsHookEx\s*\(\s*13\b", Source);
        Assert.Matches(@"const\s+int\s+WH_KEYBOARD\s*=\s*2\s*;", Source);
        Assert.Matches(@"SetWindowsHookEx\s*\(\s*WH_KEYBOARD\s*,[^;]*GetCurrentThreadId\s*\(\s*\)\s*\)", Source);
    }

    [Fact]
    public void TheCallbackUsesThePolicyAndNoPerKeyProcessLookup()
    {
        var callback = Callback();
        Assert.Contains("KeyboardHookPolicy.ShouldDispatch(", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.GetCurrentProcess", callback, StringComparison.Ordinal);
        Assert.Contains("CallNextHookEx(", callback, StringComparison.Ordinal);
    }
}
