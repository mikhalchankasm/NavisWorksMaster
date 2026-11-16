using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed class FbxLevelExportJobService
    {
        private readonly object _sync = new object();
        private readonly Action<Action> _postToUi;
        private FbxLevelExportJob _job;
        private NavisworksFbxExportHost _host;
        private string _serviceError;

        public event EventHandler Changed;

        public FbxLevelExportJobService(Action<Action> postToUi)
        {
            _postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));
        }

        public FbxLevelExportStatusResponse Start(Document document, StartFbxLevelExportRequest request,
            MatchSessionStore sessionStore, ItemExportFormats formats = ItemExportFormats.Fbx)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null || !FbxLevelExportPlan.IsFullyQualifiedFolder(request.Folder))
                throw new AgentCommandException(ErrorCodes.SchemaViolation,
                    @"A fully qualified folder path is required, such as D:\export\fbx or \\server\share\fbx.");

            var error = ItemAppearanceRules.DescribeTargetError(request.MatchHandles, request.UseSelection);
            if (error != null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, error);
            // Repeated handles and items are dropped before wrapping (Codex review P3): a handle of
            // 5000 items passed 1000 times must not become five million wrappers.
            var items = new List<ModelItem>();
            var seen = new HashSet<ModelItem>();
            if (request.UseSelection == true)
                items.AddRange(document.CurrentSelection.SelectedItems.Where(item => item != null && seen.Add(item)));
            else
            {
                foreach (var handle in request.MatchHandles.Where(value => !string.IsNullOrWhiteSpace(value))
                             .Distinct(StringComparer.Ordinal))
                {
                    IList<ModelItem> resolved;
                    if (!sessionStore.TryGet(handle, out resolved) || resolved == null)
                        throw new AgentCommandException(ErrorCodes.StaleMatchReference,
                            "matchHandle is stale or was not found: " + handle + ". Re-run find_items and retry.");
                    items.AddRange(resolved.Where(item => item != null && seen.Add(item)));
                }
            }
            return Start(document, items, request.Depth, request.Folder, request.Overwrite, formats);
        }

        public FbxLevelExportStatusResponse Start(Document document, IReadOnlyList<ModelItem> items,
            int depth, string folder, bool overwrite, ItemExportFormats formats = ItemExportFormats.Fbx)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (!FbxLevelExportPlan.IsFullyQualifiedFolder(folder))
                throw new AgentCommandException(ErrorCodes.SchemaViolation,
                    @"A fully qualified folder path is required, such as D:\export\fbx or \\server\share\fbx.");

            folder = Path.GetFullPath(folder);
            var roots = new List<IFbxTreeNode>();
            foreach (ModelItem item in document.Models.RootItems)
                roots.Add(new ModelItemFbxNode(item));
            string error;
            var nodes = items.Select(item => (IFbxTreeNode)new ModelItemFbxNode(item)).ToList();
            var plan = FbxLevelExportPlan.BuildFromItems(roots, nodes, depth, out error);
            if (error != null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, error);

            var operationId = "fbx-job-" + Guid.NewGuid().ToString("N");
            var host = new NavisworksFbxExportHost(document, operationId)
                { HasConversions = (formats & ItemExportFormats.Datasmith) != 0 };
            var documentKey = BuildDocumentKey(document);
            FbxLevelExportJob job;
            lock (_sync)
            {
                try
                {
                    job = FbxLevelExportJob.Start(operationId,
                        plan, folder, overwrite, host, _job, DateTime.UtcNow, formats);
                }
                catch (InvalidOperationException exception)
                {
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, exception.Message);
                }
                // Only an accepted start creates the folder (#444 L3: a refused one left it empty).
                // A folder that cannot be created throws here, before the job is published.
                Directory.CreateDirectory(folder);
                _job = job;
                _host = host;
                _serviceError = null;
            }

            if (host.HasConversions)
                _ = Task.Run(() => ConvertCopies(job, host));
            Schedule(job, host, documentKey);
            lock (_sync)
            {
                var response = BuildStatus(job);
                response.Message = "FBX export started; poll fbx_export_status.";
                return response;
            }
        }

        public FbxLevelExportStatusResponse Status(FbxLevelExportStatusRequest request)
        {
            lock (_sync)
                return BuildStatus(FindJob(request == null ? null : request.OperationId));
        }

        public FbxLevelExportStatusResponse Cancel(CancelFbxLevelExportRequest request)
        {
            lock (_sync)
            {
                var job = FindJob(request == null ? null : request.OperationId);
                if (job.Cancel(DateTime.UtcNow) && _host.HasConversions)
                {
                    _host.CancelConversion();
                    _host.ConversionReady.Release();
                }
                var response = BuildStatus(job);
                response.Message = _host.HasConversions ? "Cancellation requested; Datasmith conversion is stopping." :
                    "Cancellation requested. The current export cannot be interrupted; "
                    + "cancellation takes effect before the next item.";
                return response;
            }
        }

        private FbxLevelExportJob FindJob(string operationId)
        {
            if (string.IsNullOrWhiteSpace(operationId))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "operationId is required.");
            if (_job == null || !string.Equals(_job.OperationId, operationId.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new AgentCommandException(ErrorCodes.CommandFailed, "FBX export job was not found: " + operationId);
            return _job;
        }

        private FbxLevelExportStatusResponse BuildStatus(FbxLevelExportJob job)
        {
            var response = job.BuildStatus(DateTime.UtcNow);
            if (ReferenceEquals(job, _job) && _serviceError != null)
            {
                response.State = FbxLevelExportJobStates.Failed;
                response.IsRunning = false;
                response.ErrorMessage = _serviceError;
            }
            return response;
        }

        private void Schedule(FbxLevelExportJob job, NavisworksFbxExportHost host, string documentKey)
        {
            try
            {
                _postToUi(() => ProcessNext(job, host, documentKey));
            }
            catch (Exception ex)
            {
                Fail(job, host, "The FBX export could not be scheduled on the UI thread: " + ex.Message);
            }
        }

        private void ScheduleAfterInput(FbxLevelExportJob job, NavisworksFbxExportHost host, string documentKey)
        {
            try
            {
                // WM_TIMER runs after posted, input and paint messages.
                var timer = new System.Windows.Forms.Timer { Interval = 1 };
                timer.Tick += (sender, args) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    ProcessNext(job, host, documentKey);
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Fail(job, host, "The FBX export could not be scheduled on the UI thread: " + ex.Message);
            }
        }

        private void ProcessNext(FbxLevelExportJob job, NavisworksFbxExportHost host, string documentKey)
        {
            try
            {
                if (!job.IsRunning)
                    return;
                var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (!string.Equals(BuildDocumentKey(document), documentKey, StringComparison.Ordinal))
                {
                    Fail(job, host, "The active Navisworks document changed; the FBX export was stopped.");
                    return;
                }
                // Do not hold _sync during export: status and cancel must remain available.
                if (job.Step(host, () => DateTime.UtcNow))
                    ScheduleAfterInput(job, host, documentKey);
            }
            catch (Exception ex)
            {
                Fail(job, host, ex.Message);
            }
            finally
            {
                if (!job.IsRunning && !host.HasConversions)
                    Cleanup(host);
                if (host.HasConversions)
                {
                    if (!job.IsRunning) host.CancelConversion();
                    host.ConversionReady.Release();
                }
                RaiseChanged();
            }
        }

        private async Task ConvertCopies(FbxLevelExportJob job, NavisworksFbxExportHost host)
        {
            try
            {
                bool more;
                do
                {
                    await host.ConversionReady.WaitAsync().ConfigureAwait(false);
                    more = job.ConvertNext(host, () => DateTime.UtcNow);
                    RaiseChanged();
                } while (more);
            }
            catch (Exception ex) { Fail(job, host, ex.Message); }
            finally { Cleanup(host); }
        }

        private void RaiseChanged()
        {
            try { Changed?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex)
            {
                Logger.Warn("FBX export change handler failed: " + ex.Message, "FbxLevelExport");
            }
        }

        private static void Cleanup(NavisworksFbxExportHost host)
        {
            try { host.Cleanup(); }
            catch (Exception ex)
            {
                Logger.Warn("Item export temporary folder cleanup failed: " + ex.Message, "FbxLevelExport");
            }
        }

        private void Fail(FbxLevelExportJob job, NavisworksFbxExportHost host, string message)
        {
            lock (_sync)
            {
                job.Cancel(DateTime.UtcNow);
                // A canceled step finishes without calling the Navisworks adapter.
                job.Step(host, () => DateTime.UtcNow);
                if (ReferenceEquals(job, _job))
                    _serviceError = message;
            }
            if (host.HasConversions)
            {
                host.CancelConversion();
                host.ConversionReady.Release();
            }
            else Cleanup(host);
        }

        private static string BuildDocumentKey(Document document)
        {
            if (document == null)
                return "none";
            string fileName;
            try { fileName = document.FileName ?? string.Empty; }
            catch { fileName = string.Empty; }
            return RuntimeHelpers.GetHashCode(document).ToString(CultureInfo.InvariantCulture) + "|" + fileName;
        }
    }
}
