using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed class FbxLevelExportJobService
    {
        private readonly object _sync = new object();
        private readonly Action<Action> _postToUi;
        private FbxLevelExportJob _job;
        private string _serviceError;

        public FbxLevelExportJobService(Action<Action> postToUi)
        {
            _postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));
        }

        public FbxLevelExportStatusResponse Start(Document document, StartFbxLevelExportRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null || string.IsNullOrWhiteSpace(request.Folder) || !Path.IsPathRooted(request.Folder))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "An absolute folder path is required.");

            var folder = Path.GetFullPath(request.Folder);
            Directory.CreateDirectory(folder);
            var roots = new List<IFbxTreeNode>();
            foreach (ModelItem item in document.Models.RootItems)
                roots.Add(new ModelItemFbxNode(item));
            string error;
            var plan = FbxLevelExportPlan.Build(roots, request.Level, out error);
            if (error != null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, error);

            var host = new NavisworksFbxExportHost(document);
            var documentKey = BuildDocumentKey(document);
            FbxLevelExportJob job;
            lock (_sync)
            {
                try
                {
                    job = FbxLevelExportJob.Start("fbx-job-" + Guid.NewGuid().ToString("N"),
                        plan, folder, request.Overwrite, host, _job, DateTime.UtcNow);
                }
                catch (InvalidOperationException exception)
                {
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, exception.Message);
                }
                _job = job;
                _serviceError = null;
            }

            Schedule(job, host, documentKey);
            lock (_sync)
            {
                var response = BuildStatus(job);
                response.Message = "FBX export started; poll fbx_level_export_status.";
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
                job.Cancel(DateTime.UtcNow);
                var response = BuildStatus(job);
                response.Message = "Cancellation requested. The current export cannot be interrupted; "
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

        private void Schedule(FbxLevelExportJob job, IFbxExportHost host, string documentKey)
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

        private void ProcessNext(FbxLevelExportJob job, IFbxExportHost host, string documentKey)
        {
            if (!job.IsRunning)
                return;
            try
            {
                var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (!string.Equals(BuildDocumentKey(document), documentKey, StringComparison.Ordinal))
                {
                    Fail(job, host, "The active Navisworks document changed; the FBX export was stopped.");
                    return;
                }
                // Do not hold _sync during export: status and cancel must remain available.
                if (job.Step(host, () => DateTime.UtcNow))
                    Schedule(job, host, documentKey);
            }
            catch (Exception ex)
            {
                Fail(job, host, ex.Message);
            }
        }

        private void Fail(FbxLevelExportJob job, IFbxExportHost host, string message)
        {
            lock (_sync)
            {
                job.Cancel(DateTime.UtcNow);
                // A canceled step finishes without calling the Navisworks adapter.
                job.Step(host, () => DateTime.UtcNow);
                if (ReferenceEquals(job, _job))
                    _serviceError = message;
            }
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
