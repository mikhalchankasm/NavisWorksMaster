using System;
using System.IO;
using System.Runtime.CompilerServices;
using NavisHelper.Core;

namespace NavisHelper.McpServer.Tests;

/// <summary>
/// Points this assembly's logging at its own file. Why that matters is documented
/// once, on <see cref="Logger.LogFileOverrideVariable"/>.
///
/// Runs at assembly load, so nothing a test touches can log before it.
/// </summary>
internal static class TestLogFileRedirect
{
    /// <summary>
    /// Where the suite logs when its output directory is writable.
    /// </summary>
    internal static string PreferredPath(string baseDirectory)
    {
        return Path.Combine(baseDirectory, "logs", "navishelper_tests_log.txt");
    }

    /// <summary>
    /// Where it logs when the preferred directory cannot be created — a read-only
    /// output directory, for instance. Still not the host's file, which is the
    /// property that matters, and it needs no directory created.
    /// </summary>
    internal static string FallbackPath(string temporaryDirectory)
    {
        return Path.Combine(temporaryDirectory, "navishelper_tests_log.txt");
    }

    [ModuleInitializer]
    internal static void Redirect()
    {
        try
        {
            // Already set by a runner or a developer: leave it alone. Overriding an
            // explicit choice is worse than what this prevents.
            var existing = Environment.GetEnvironmentVariable(Logger.LogFileOverrideVariable);
            if (!string.IsNullOrWhiteSpace(existing))
                return;

            Environment.SetEnvironmentVariable(
                Logger.LogFileOverrideVariable,
                FallbackPath(Path.GetTempPath()));

            // Only now try for the tidier location. Setting the fallback first means a
            // throw here leaves the suite redirected rather than pointed at the host's
            // log: swallowing the failure without a fallback would let every test that
            // logs before the guard test runs pollute the file this exists to protect.
            var preferred = PreferredPath(AppContext.BaseDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(preferred));
            Environment.SetEnvironmentVariable(Logger.LogFileOverrideVariable, preferred);
        }
        catch
        {
            // A test run must not fail because a log could not be redirected. The
            // suite is the gate; the log is a diagnostic. If even the fallback could
            // not be set, LoggerLogFilePathTests fails and says so.
        }
    }
}
