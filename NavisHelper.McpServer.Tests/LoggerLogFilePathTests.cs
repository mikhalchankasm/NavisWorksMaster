using System;
using System.IO;
using NavisHelper.Core;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// `Logger.cs` is compiled into this test project, so a test run used to append to
/// the same `%TEMP%\navishelper_log.txt` a live Navisworks host writes to. Two
/// sessions reading that log to diagnose a live problem found each other's xUnit
/// stack traces interleaved with rig traffic.
/// </summary>
public sealed class LoggerLogFilePathTests
{
    [Fact]
    public void An_override_moves_the_default_log_file()
    {
        var target = Path.Combine(Path.GetTempPath(), "navishelper_override_probe.txt");

        Assert.Equal(target, Logger.ResolveDefaultLogFilePath(target));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_an_override_the_shared_path_is_unchanged(string overrideValue)
    {
        // The live plugin must keep writing where the host reports it writes, so an
        // unset or blank variable is not a behaviour change.
        var expected = Path.Combine(Path.GetTempPath(), "navishelper_log.txt");

        Assert.Equal(expected, Logger.ResolveDefaultLogFilePath(overrideValue));
    }

    [Fact]
    public void Surrounding_space_in_the_override_is_trimmed()
    {
        var target = Path.Combine(Path.GetTempPath(), "navishelper_override_probe.txt");

        Assert.Equal(target, Logger.ResolveDefaultLogFilePath("  " + target + "  "));
    }

    [Fact]
    public void This_assembly_is_redirected_away_from_the_host_log()
    {
        // The executed criterion for the defect: with the module initializer in
        // place, the path this assembly logs to is not the host's.
        var hostPath = Path.Combine(Path.GetTempPath(), "navishelper_log.txt");
        var actual = Logger.GetLogFilePath();

        Assert.NotEqual(hostPath, actual);
        Assert.False(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Logger.LogFileOverrideVariable)),
            "the module initializer did not set " + Logger.LogFileOverrideVariable +
            "; this assembly would write into the live host's log.");
    }

    [Fact]
    public void A_model_path_still_puts_the_log_beside_the_model()
    {
        // The override covers the default path only. A model-scoped log is a
        // deliberate per-model artifact and is not redirected.
        var modelPath = Path.Combine(Path.GetTempPath(), "SomeModel.nwd");

        var actual = Logger.GetLogFilePath(modelPath);

        Assert.Equal(Path.Combine(Path.GetTempPath(), "SomeModel_navishelper_log.txt"), actual);
    }
}
