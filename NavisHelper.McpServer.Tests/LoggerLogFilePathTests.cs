using System;
using System.IO;
using NavisHelper.Core;
using Xunit;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// Covers the log-path override. Why it exists is documented once, on
/// <see cref="Logger.LogFileOverrideVariable"/>.
/// </summary>
public sealed class LoggerLogFilePathTests
{
    private static string HostLogPath => Path.Combine(Path.GetTempPath(), "navishelper_log.txt");

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
        // The live plugin must keep writing where host_status reports it writes, so an
        // unset or blank variable is not a behaviour change.
        Assert.Equal(HostLogPath, Logger.ResolveDefaultLogFilePath(overrideValue));
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
        Assert.NotEqual(HostLogPath, Logger.GetLogFilePath());
        Assert.False(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Logger.LogFileOverrideVariable)),
            "the module initializer did not set " + Logger.LogFileOverrideVariable +
            "; this assembly would write into the live host's log.");
    }

    [Fact]
    public void Neither_candidate_path_is_the_host_log()
    {
        // The redirect sets the fallback first and only then tries for the tidier
        // location, so a throw from CreateDirectory leaves the suite redirected rather
        // than pointed at the host's file. Both candidates therefore have to be safe,
        // not just the preferred one.
        Assert.NotEqual(HostLogPath, TestLogFileRedirect.PreferredPath(AppContext.BaseDirectory));
        Assert.NotEqual(HostLogPath, TestLogFileRedirect.FallbackPath(Path.GetTempPath()));
    }

    [Fact]
    public void The_fallback_needs_no_directory_created()
    {
        // It is reached because directory creation failed, so it must not need any.
        var fallback = TestLogFileRedirect.FallbackPath(Path.GetTempPath());

        Assert.Equal(
            Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(Path.GetDirectoryName(fallback)).TrimEnd(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void A_model_path_still_puts_the_log_beside_the_model()
    {
        // The override covers the default path only. A model-scoped log is a
        // deliberate per-model artifact and is not redirected.
        var modelPath = Path.Combine(Path.GetTempPath(), "SomeModel.nwd");

        Assert.Equal(
            Path.Combine(Path.GetTempPath(), "SomeModel_navishelper_log.txt"),
            Logger.GetLogFilePath(modelPath));
    }
}
