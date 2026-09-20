using System;
using System.IO;
using System.Runtime.CompilerServices;
using NavisHelper.Core;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// Keeps this test assembly out of the live host's log file.
///
/// `NavisHelper/Core/Logger.cs` is compiled into this project, so without this the
/// suite appends to the same `%TEMP%\navishelper_log.txt` that a running Navisworks
/// host writes to. That is not cosmetic: two sessions diagnosing a live problem
/// from that log found each other's xUnit stack traces interleaved with rig
/// traffic, and had to identify which worktree each trace came from before the file
/// was usable. The log exists to answer questions about the host.
///
/// The redirect runs before any test, and before anything a test touches can log,
/// because a module initializer runs at assembly load.
/// </summary>
internal static class TestLogFileRedirect
{
    [ModuleInitializer]
    internal static void Redirect()
    {
        try
        {
            // Already set by a runner or a developer: leave it alone. Overriding an
            // explicit choice is worse than the pollution this exists to stop.
            var existing = Environment.GetEnvironmentVariable(Logger.LogFileOverrideVariable);
            if (!string.IsNullOrWhiteSpace(existing))
                return;

            var directory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(directory);
            Environment.SetEnvironmentVariable(
                Logger.LogFileOverrideVariable,
                Path.Combine(directory, "navishelper_tests_log.txt"));
        }
        catch
        {
            // A test run must not fail because a log could not be redirected. The
            // cost of failing here is worse than the pollution: the suite is the
            // gate, and the log is a diagnostic.
        }
    }
}
