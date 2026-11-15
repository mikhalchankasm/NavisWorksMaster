using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace NavisHelper.AutomationRunner
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var timer = Stopwatch.StartNew();
            var result = new RunnerResult();
            TextWriter stdout = Console.Out;
            try
            {
                // Keep managed dependency diagnostics out of the single-line result protocol.
                Console.SetOut(TextWriter.Null);
                Run(args, result);
            }
            catch (Exception exception)
            {
                while (exception.InnerException != null)
                    exception = exception.InnerException;
                result.Ok = false;
                result.ExitCode = RunnerExitCodes.Crashed;
                result.Error = exception.Message;
            }
            finally
            {
                Console.SetOut(stdout);
            }
            result.ElapsedMs = timer.ElapsedMilliseconds;
            stdout.WriteLine(result.ToJsonLine());
            return result.ExitCode;
        }

        private static void Run(string[] args, RunnerResult result)
        {
            string error;
            NwdCopyArguments copy = null;
            DatasmithExportArguments parsed;
            if (args != null && args.Length > 0 && args[0] == "nwd-copy")
            {
                copy = NwdCopyArguments.Parse(args, out error);
                parsed = copy == null ? null : new DatasmithExportArguments { NavisworksDirectory = copy.NavisworksDirectory,
                    SourcePath = copy.SourcePath, OutputPath = copy.OutputPath, JobName = copy.JobName };
            }
            else
            {
                try { NwdCopyArguments.SweepStaleCopies(Path.GetTempPath(), DateTime.UtcNow); }
                catch (Exception) { /* Stale-copy cleanup must never prevent an export. */ }
                parsed = DatasmithExportArguments.Parse(args, out error);
            }
            if (parsed == null)
            {
                Fail(result, RunnerExitCodes.BadArguments, error);
                return;
            }
            result.OutputPath = parsed.OutputPath;
            string automationPath = Path.Combine(parsed.NavisworksDirectory, "Autodesk.Navisworks.Automation.dll");
            if (!File.Exists(automationPath))
            {
                Fail(result, RunnerExitCodes.NavisworksNotFound, "Automation assembly not found: " + automationPath);
                return;
            }
            if (!File.Exists(parsed.SourcePath))
            {
                Fail(result, RunnerExitCodes.BadArguments, "--source file does not exist: " + parsed.SourcePath);
                return;
            }
            if (File.Exists(parsed.OutputPath) && !parsed.Overwrite)
            {
                Fail(result, RunnerExitCodes.BadArguments, "--output already exists; use --overwrite to replace it.");
                return;
            }

            var before = new FileInfo(parsed.OutputPath);
            DateTime? writeUtcBeforeExport = before.Exists ? before.LastWriteTimeUtc : (DateTime?)null;
            ResolveEventHandler resolve = (sender, eventArgs) =>
            {
                string name = new AssemblyName(eventArgs.Name).Name;
                if (string.IsNullOrEmpty(name) || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                    return null;
                string dependency = Path.Combine(parsed.NavisworksDirectory, name + ".dll");
                return File.Exists(dependency) ? Assembly.LoadFrom(dependency) : null;
            };
            AppDomain.CurrentDomain.AssemblyResolve += resolve;
            string temporaryCopy = null;
            try
            {
                var assembly = Assembly.LoadFrom(automationPath);
                var type = assembly.GetType("Autodesk.Navisworks.Api.Automation.NavisworksApplication", true);
                object application;
                using (var joiner = parsed.JobName != null ? new NavisworksJobJoiner(parsed.JobName) : null)
                {
                    if (joiner != null)
                    {
                        joiner.Snapshot();
                        joiner.Start();
                    }
                    try { application = Activator.CreateInstance(type); }
                    finally { if (joiner != null) joiner.Stop(); }
                }
                try
                {
                    try { type.GetProperty("Visible")?.SetValue(application, false, null); }
                    catch (Exception) { /* Optional on supported Automation API versions. */ }
                    try { type.GetMethod("DisableProgress", Type.EmptyTypes)?.Invoke(application, null); }
                    catch (Exception) { /* Progress suppression must not prevent an export. */ }

                    Action<string> open = path => type.GetMethod("OpenFile", new[] { typeof(string), typeof(string[]) })
                        .Invoke(application, new object[] { path, new string[0] });
                    try { open(parsed.SourcePath); }
                    catch (Exception openError)
                    {
                        if (parsed.ConvertNavisworksDirectory == null) throw;
                        try
                        {
                            temporaryCopy = NwdCopyArguments.TempCopyPath(Path.GetTempPath(), parsed.SourcePath, Guid.NewGuid());
                            Directory.CreateDirectory(Path.GetDirectoryName(temporaryCopy));
                            RunFallback(NwdCopyArguments.ForFallback(parsed, temporaryCopy));
                            open(temporaryCopy);
                        }
                        catch (Exception copyError)
                        {
                            throw new InvalidOperationException("Navisworks could not open the file: " + openError.GetBaseException().Message +
                                "; the copy through " + Path.GetFileName(parsed.ConvertNavisworksDirectory.TrimEnd('\\', '/')) +
                                " failed: " + copyError.GetBaseException().Message);
                        }
                    }
                    result.ExporterReturnCode = (int)type.GetMethod("ExecuteAddInPlugin", new[] { typeof(string), typeof(string[]) })
                        .Invoke(application, new object[] { copy == null ? DatasmithExporter.PluginId : NwdCopyArguments.PublishPluginId,
                            copy == null ? parsed.ToExporterParameters() : copy.ToPublishParameters() });
                }
                finally
                {
                    var disposable = application as IDisposable;
                    if (disposable != null)
                        disposable.Dispose();
                    else
                        type.GetMethod("Dispose", Type.EmptyTypes).Invoke(application, null);
                }
            }
            finally
            {
                AppDomain.CurrentDomain.AssemblyResolve -= resolve;
                if (temporaryCopy != null)
                {
                    try { NwdCopyArguments.DeleteCopyDirectory(Path.GetTempPath(), Path.GetDirectoryName(temporaryCopy)); }
                    catch (Exception) { /* A locked copy can be swept on a later run. */ }
                }
            }

            var output = new FileInfo(parsed.OutputPath);
            if (DatasmithExporter.IsFreshExport(result.ExporterReturnCode, output.Exists, output.Exists ? output.LastWriteTimeUtc : DateTime.MinValue, writeUtcBeforeExport))
            {
                result.FileSizeBytes = output.Length;
                result.Ok = true;
                result.ExitCode = RunnerExitCodes.Ok;
            }
            else
            {
                Fail(result, RunnerExitCodes.ExportFailed, result.ExporterReturnCode != 0
                    ? (copy == null ? "Datasmith exporter" : "PublishNwd") + " returned " + result.ExporterReturnCode + "."
                    : output.Exists
                        ? "Datasmith exporter did not rewrite the output file; it still holds an earlier export."
                        : (copy == null ? "Datasmith exporter" : "PublishNwd") + " did not create the output file.");
            }
        }

        private static void RunFallback(NwdCopyArguments copy)
        {
            var start = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,
                NwdCopyArguments.JoinCommandLine(copy.ToCommandLine()))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            };
            // No breakaway: the child inherits the parent's kill-on-close job.
            using (var child = Process.Start(start))
            {
                if (child == null) throw new InvalidOperationException("The nwd-copy runner did not start.");
                string lastLine = null, line;
                while ((line = child.StandardOutput.ReadLine()) != null)
                    if (!string.IsNullOrWhiteSpace(line)) lastLine = line;
                child.WaitForExit();
                var outcome = RunnerResult.ParseJsonLine(lastLine);
                if (outcome == null || !outcome.Ok || outcome.ExitCode != 0 || child.ExitCode != 0 || !File.Exists(copy.OutputPath))
                    throw new InvalidOperationException(outcome != null && !string.IsNullOrEmpty(outcome.Error)
                        ? outcome.Error : "The nwd-copy runner failed (exit " + child.ExitCode + ") or did not create the copy.");
            }
        }

        private static void Fail(RunnerResult result, int exitCode, string error)
        {
            result.Ok = false;
            result.ExitCode = exitCode;
            result.Error = error;
        }
    }
}
