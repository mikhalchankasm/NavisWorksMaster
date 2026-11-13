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
            var parsed = DatasmithExportArguments.Parse(args, out error);
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
            try
            {
                var assembly = Assembly.LoadFrom(automationPath);
                var type = assembly.GetType("Autodesk.Navisworks.Api.Automation.NavisworksApplication", true);
                object application = Activator.CreateInstance(type);
                try
                {
                    try { type.GetProperty("Visible")?.SetValue(application, false, null); }
                    catch (Exception) { /* Optional on supported Automation API versions. */ }
                    try { type.GetMethod("DisableProgress", Type.EmptyTypes)?.Invoke(application, null); }
                    catch (Exception) { /* Progress suppression must not prevent an export. */ }

                    type.GetMethod("OpenFile", new[] { typeof(string), typeof(string[]) })
                        .Invoke(application, new object[] { parsed.SourcePath, new string[0] });
                    result.ExporterReturnCode = (int)type.GetMethod("ExecuteAddInPlugin", new[] { typeof(string), typeof(string[]) })
                        .Invoke(application, new object[] { DatasmithExporter.PluginId, parsed.ToExporterParameters() });
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
                    ? "Datasmith exporter returned " + result.ExporterReturnCode + "."
                    : output.Exists
                        ? "Datasmith exporter did not rewrite the output file; it still holds an earlier export."
                        : "Datasmith exporter did not create the output file.");
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
