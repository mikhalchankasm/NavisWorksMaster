using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed class SaveDocumentJobService
    {
        private readonly object _sync = new object();
        private readonly Action<Action> _postToUi;
        private SaveDocumentJobState _job;

        public SaveDocumentJobService(Action<Action> postToUi)
        {
            _postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));
        }

        public StartSaveDocumentResponse Start(Document document, StartSaveDocumentRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var currentPath = DocumentCommandService.NormalizeExistingDocumentPath(document.FileName);
            var operationId = "save-job-" + Guid.NewGuid().ToString("N");
            var nowUtc = DateTime.UtcNow;
            SaveDocumentJobState job;
            lock (_sync)
            {
                try
                {
                    job = SaveDocumentJobState.StartNew(operationId, currentPath, _job, nowUtc);
                }
                catch (InvalidOperationException exception)
                {
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, exception.Message);
                }

                _job = job;
            }

            var documentKey = BuildDocumentKey(document);
            ScheduleSave(job, documentKey);
            return job.BuildStartResponse(DateTime.UtcNow);
        }

        public SaveDocumentStatusResponse Status(SaveDocumentStatusRequest request)
        {
            var operationId = request == null ? null : request.OperationId;
            if (string.IsNullOrWhiteSpace(operationId))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "operationId is required.");

            lock (_sync)
            {
                var job = _job;
                if (job == null || !string.Equals(job.OperationId, operationId.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Document save job was not found: " + operationId);
                return job.BuildStatusResponse(DateTime.UtcNow);
            }
        }

        private void ScheduleSave(SaveDocumentJobState job, string documentKey)
        {
            try
            {
                _postToUi(() => RunSave(job, documentKey));
            }
            catch (Exception ex)
            {
                // The job is failed so a new start is allowed, and start itself fails:
                // it must not report a save that will never run as running.
                var message = "The save could not be scheduled on the UI thread: " + ex.Message;
                Transition(nowUtc => job.Fail(message, nowUtc));
                if (ex is AgentCommandException)
                    throw;
                throw new AgentCommandException(ErrorCodes.CommandFailed, message);
            }
        }

        private void RunSave(SaveDocumentJobState job, string documentKey)
        {
            try
            {
                var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (!string.Equals(BuildDocumentKey(document), documentKey, StringComparison.Ordinal))
                    throw new InvalidOperationException("The active Navisworks document changed; the save was stopped without touching the new document.");

                var saved = DocumentCommandService.SaveDocumentToPath(document, job.Path, false);
                Transition(nowUtc => job.Complete(saved.Path, saved.Format, saved.FileSizeBytes, nowUtc));
            }
            catch (Exception ex)
            {
                Logger.Error("Asynchronous document save failed: " + ex, "SaveJob");
                Transition(nowUtc => job.Fail(ex.Message, nowUtc));
            }
        }

        private void Transition(Func<DateTime, SaveDocumentJobState> transition)
        {
            lock (_sync)
            {
                if (_job != null && _job.IsRunning)
                    _job = transition(DateTime.UtcNow);
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
