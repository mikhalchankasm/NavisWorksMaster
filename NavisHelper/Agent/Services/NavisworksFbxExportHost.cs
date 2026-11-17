using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop.ComApi;
using NavisHelper.Agent.Contracts;
using Newtonsoft.Json.Linq;

namespace NavisHelper.Agent.Services
{
    internal sealed class ModelItemFbxNode : IFbxTreeNode
    {
        internal ModelItemFbxNode(ModelItem item)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
        }

        internal ModelItem Item { get; }
        public string Name => Item.DisplayName;
        public string ClassName => Item.ClassDisplayName;
        public bool IsHidden => Item.IsHidden;
        public IFbxTreeNode Parent => Item.Parent == null ? null : new ModelItemFbxNode(Item.Parent);

        public override bool Equals(object obj) => obj is ModelItemFbxNode other && Item.Equals(other.Item);
        public override int GetHashCode() => Item.GetHashCode();

        public IEnumerable<IFbxTreeNode> Children
        {
            get
            {
                // The plan retains these wrappers and their ModelItems for later steps.
                foreach (ModelItem child in Item.Children)
                    yield return new ModelItemFbxNode(child);
            }
        }
    }

    internal sealed class NavisworksFbxExportHost : IFbxExportHost, IItemExportHost
    {
        private readonly Document _document;
        private readonly object _runnerGate = new object();
        private KillOnCloseJob _runnerJob;
        private bool _runnerCanceled;
        internal readonly SemaphoreSlim ConversionReady = new SemaphoreSlim(0);
        internal bool HasConversions { get; set; }

        public NavisworksFbxExportHost(Document document, string operationId)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            TempFolder = Path.Combine(Path.GetTempPath(), "NavisHelper", "item-export", operationId);
        }

        public string TempFolder { get; }

        public void SetHidden(IReadOnlyList<IFbxTreeNode> nodes, bool hidden)
        {
            var items = new ModelItemCollection();
            foreach (IFbxTreeNode node in nodes)
                items.Add(((ModelItemFbxNode)node).Item);
            _document.Models.SetHidden(items, hidden);
        }

        public string Export(string path)
        {
            var state = ComApiBridge.State;
            var options = state.GetIOPluginOptions("LcFbxExporterPlugin");
            var status = state.DriveIOPlugin("LcFbxExporterPlugin", path, options);
            if (status != nwEExportStatus.eExport_OK)
                return status.ToString();
            return File.Exists(path) ? null : "The FBX exporter wrote no file.";
        }

        public bool FileExists(string path) => File.Exists(path);
        public long FileSize(string path) => new FileInfo(path).Length;

        public string PublishCopy(string path)
        {
            Directory.CreateDirectory(TempFolder);
            if (!Enum.TryParse("Navisworks2026", out DocumentFileVersion version))
                return "This Navisworks version cannot publish a 2026 NWD copy.";
            if (!DocumentCommandService.TryPublishWithEmbeddedTextures(_document, path, (int)version))
                return "Navisworks did not publish the isolated NWD copy: " + path;
            return File.Exists(path) ? null : "Navisworks published no NWD copy: " + path;
        }

        public string ExportDatasmith(string sourcePath, string outputPath)
        {
            var missing = DatasmithPanelExportJob.MissingDependency();
            if (missing != null)
                return "Datasmith export dependency was not found: " + missing;
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            const int timeoutMs = 30 * 60 * 1000;
            using (var job = new KillOnCloseJob(DatasmithRunnerCommandLine.JobNamePrefix))
            {
                // Copies are already published as 2026, so no conversion arguments are needed.
                var args = DatasmithRunnerCommandLine.BuildArguments(DatasmithPanelExportJob.NavisworksDirectory,
                    sourcePath, outputPath, null, null, false, true, job.Name);
                using (var process = new Process
                {
                    StartInfo = new ProcessStartInfo(DatasmithPanelExportJob.RunnerPath, DatasmithRunnerCommandLine.Join(args))
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                })
                {
                    var timer = Stopwatch.StartNew();
                    lock (_runnerGate)
                    {
                        if (_runnerCanceled) return "Datasmith export canceled.";
                        process.Start();
                        job.Assign(process);
                        _runnerJob = job;
                    }
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(timeoutMs)
                        || !Task.WaitAll(new Task[] { stdout, stderr },
                            Math.Max(0, timeoutMs - (int)timer.ElapsedMilliseconds)))
                        return "Datasmith export timed out after 30 minutes.";
                    var lines = stdout.Result.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length == 0)
                        return string.IsNullOrWhiteSpace(stderr.Result) ? "Datasmith runner returned no result." : stderr.Result.Trim();
                    var result = JObject.Parse(lines[lines.Length - 1]);
                    if (process.ExitCode != 0 || result.Value<bool?>("ok") != true)
                        return result.Value<string>("error") ?? "Datasmith runner failed; ExitCode=" + process.ExitCode;
                    return File.Exists(outputPath) ? null : "Datasmith exporter wrote no file: " + outputPath;
                }
            }
        }

        public void DeleteFile(string path) => File.Delete(path);

        internal void CancelConversion()
        {
            lock (_runnerGate)
            {
                _runnerCanceled = true;
                _runnerJob?.Dispose();
            }
        }

        internal void Cleanup()
        {
            // Delete only an empty operation folder; never remove unexpected files recursively.
            if (Directory.Exists(TempFolder))
                Directory.Delete(TempFolder);
        }

        public long FreeBytes(string folder)
        {
            ulong available, total, free;
            if (!GetDiskFreeSpaceEx(folder, out available, out total, out free))
                return long.MaxValue;
            return available > (ulong)long.MaxValue ? long.MaxValue : (long)available;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceEx(string directoryName,
            out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);
    }
}
