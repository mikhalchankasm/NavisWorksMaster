using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NavisHelper.AI
{
    internal enum AiWorkerRunFailureKind
    {
        None = 0,
        Missing,
        StartupFailed,
        RuntimeMissing,
        NonZeroExit,
        Cancelled
    }

    internal sealed class AiWorkerRunResult
    {
        private AiWorkerRunResult(
            AiWorkerRunFailureKind failureKind,
            string standardOutput,
            int? exitCode)
        {
            FailureKind = failureKind;
            StandardOutput = standardOutput ?? string.Empty;
            ExitCode = exitCode;
        }

        internal AiWorkerRunFailureKind FailureKind { get; }
        internal string StandardOutput { get; }
        internal int? ExitCode { get; }
        internal bool IsSuccess => FailureKind == AiWorkerRunFailureKind.None;

        internal static AiWorkerRunResult Success(string standardOutput)
        {
            return new AiWorkerRunResult(
                AiWorkerRunFailureKind.None,
                standardOutput,
                0);
        }

        internal static AiWorkerRunResult Failure(
            AiWorkerRunFailureKind failureKind,
            int? exitCode = null)
        {
            return new AiWorkerRunResult(failureKind, string.Empty, exitCode);
        }
    }

    internal interface IAiWorkerProcessRunner
    {
        Task<AiWorkerRunResult> RunAsync(
            string workerPath,
            string requestJson,
            string key,
            CancellationToken cancellationToken);
    }

    internal sealed class AiWorkerProcessRunner : IAiWorkerProcessRunner
    {
        public async Task<AiWorkerRunResult> RunAsync(
            string workerPath,
            string requestJson,
            string key,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return AiWorkerRunResult.Failure(
                    AiWorkerRunFailureKind.Cancelled);
            if (string.IsNullOrWhiteSpace(workerPath) ||
                !File.Exists(workerPath))
                return AiWorkerRunResult.Failure(
                    AiWorkerRunFailureKind.Missing);

            using (var process = new Process())
            {
                process.StartInfo = CreateStartInfo(workerPath, key);
                var exit = new TaskCompletionSource<int>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                process.EnableRaisingEvents = true;
                process.Exited += (sender, args) =>
                {
                    try
                    {
                        exit.TrySetResult(process.ExitCode);
                    }
                    catch (InvalidOperationException)
                    {
                        exit.TrySetResult(-1);
                    }
                };

                try
                {
                    if (!process.Start())
                        return AiWorkerRunResult.Failure(
                            AiWorkerRunFailureKind.StartupFailed);
                }
                catch (Win32Exception)
                {
                    return AiWorkerRunResult.Failure(
                        AiWorkerRunFailureKind.StartupFailed);
                }
                catch (InvalidOperationException)
                {
                    return AiWorkerRunResult.Failure(
                        AiWorkerRunFailureKind.StartupFailed);
                }

                var standardOutputTask = process.StandardOutput.ReadToEndAsync();
                var standardErrorTask = process.StandardError.ReadToEndAsync();
                var cancelled = new TaskCompletionSource<bool>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                using (cancellationToken.Register(() =>
                       {
                           cancelled.TrySetResult(true);
                       }))
                {
                    try
                    {
                        await process.StandardInput.WriteAsync(
                                requestJson ?? string.Empty)
                            .ConfigureAwait(false);
                        process.StandardInput.Close();
                    }
                    catch (Exception ex) when (
                        ex is IOException ||
                        ex is InvalidOperationException ||
                        ex is ObjectDisposedException)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return AiWorkerRunResult.Failure(
                                AiWorkerRunFailureKind.Cancelled);
                        var earlyExit = await Task.WhenAny(
                                exit.Task,
                                Task.Delay(TimeSpan.FromSeconds(2)))
                            .ConfigureAwait(false);
                        if (earlyExit == exit.Task)
                            return await CreateExitedResultAsync(
                                    exit.Task,
                                    standardOutputTask,
                                    standardErrorTask)
                                .ConfigureAwait(false);
                        TryKill(process);
                        return AiWorkerRunResult.Failure(
                            AiWorkerRunFailureKind.StartupFailed);
                    }

                    var completed = await Task.WhenAny(
                            exit.Task,
                            cancelled.Task)
                        .ConfigureAwait(false);
                    if (completed == cancelled.Task ||
                        cancellationToken.IsCancellationRequested)
                    {
                        TryKill(process);
                        await Task.WhenAny(
                                exit.Task,
                                Task.Delay(TimeSpan.FromSeconds(2)))
                            .ConfigureAwait(false);
                        await Task.WhenAny(
                                Task.WhenAll(
                                    standardOutputTask,
                                    standardErrorTask),
                                Task.Delay(TimeSpan.FromSeconds(2)))
                            .ConfigureAwait(false);
                        return AiWorkerRunResult.Failure(
                            AiWorkerRunFailureKind.Cancelled);
                    }

                    return await CreateExitedResultAsync(
                            exit.Task,
                            standardOutputTask,
                            standardErrorTask)
                        .ConfigureAwait(false);
                }
            }
        }

        internal static ProcessStartInfo CreateStartInfo(
            string workerPath,
            string key)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = workerPath,
                Arguments = string.Empty,
                WorkingDirectory = Path.GetDirectoryName(workerPath) ??
                                   string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.EnvironmentVariables[
                AiWorkerProtocol.KeyEnvironmentVariable] = key ?? string.Empty;
            return startInfo;
        }

        private static bool LooksLikeMissingRuntime(string standardError)
        {
            var value = standardError ?? string.Empty;
            return value.IndexOf(
                       "You must install or update .NET",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf(
                       "hostfxr",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   value.IndexOf(
                       "Microsoft.NETCore.App",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static async Task<AiWorkerRunResult> CreateExitedResultAsync(
            Task<int> exitTask,
            Task<string> standardOutputTask,
            Task<string> standardErrorTask)
        {
            var exitCode = await exitTask.ConfigureAwait(false);
            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);
            if (exitCode != 0)
            {
                return AiWorkerRunResult.Failure(
                    LooksLikeMissingRuntime(standardError)
                        ? AiWorkerRunFailureKind.RuntimeMissing
                        : AiWorkerRunFailureKind.NonZeroExit,
                    exitCode);
            }
            return AiWorkerRunResult.Success(standardOutput);
        }

        private static void TryKill(Process process)
        {
            if (process == null)
                return;
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }
    }
}
