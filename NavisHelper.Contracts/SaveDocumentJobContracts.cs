using System;

namespace NavisHelper.Agent.Contracts
{
    public static class SaveDocumentJobStates
    {
        public const string Running = "running";
        public const string Completed = "completed";
        public const string Failed = "failed";
    }

    public sealed class StartSaveDocumentRequest
    {
    }

    public sealed class StartSaveDocumentResponse
    {
        public string OperationId { get; set; }
        public string State { get; set; }
        public bool IsRunning { get; set; }
        public string Path { get; set; }
        public long ElapsedMs { get; set; }
        public string Message { get; set; }
    }

    public sealed class SaveDocumentStatusRequest
    {
        public string OperationId { get; set; }
    }

    public sealed class SaveDocumentStatusResponse
    {
        public string OperationId { get; set; }
        public string State { get; set; }
        public bool IsRunning { get; set; }
        public string Path { get; set; }
        public string Format { get; set; }
        public long FileSizeBytes { get; set; }
        public long ElapsedMs { get; set; }
        public string ErrorMessage { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime? CompletedAtUtc { get; set; }
        public string Message { get; set; }
    }

    public sealed class SaveDocumentJobState
    {
        private SaveDocumentJobState(
            string operationId,
            string state,
            DateTime startedAtUtc,
            DateTime? completedAtUtc,
            string path,
            string format,
            long fileSizeBytes,
            string errorMessage)
        {
            OperationId = operationId;
            State = state;
            StartedAtUtc = startedAtUtc;
            CompletedAtUtc = completedAtUtc;
            Path = path;
            Format = format;
            FileSizeBytes = fileSizeBytes;
            ErrorMessage = errorMessage;
        }

        public string OperationId { get; }
        public string State { get; }
        public DateTime StartedAtUtc { get; }
        public DateTime? CompletedAtUtc { get; }
        public string Path { get; }
        public string Format { get; }
        public long FileSizeBytes { get; }
        public string ErrorMessage { get; }

        public bool IsRunning
        {
            get { return string.Equals(State, SaveDocumentJobStates.Running, StringComparison.OrdinalIgnoreCase); }
        }

        public static SaveDocumentJobState StartNew(string operationId, string path, SaveDocumentJobState previous, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(operationId))
                throw new ArgumentException("operationId is required.", nameof(operationId));
            if (previous != null && previous.IsRunning)
                throw new InvalidOperationException(
                    "A document save job is already running: " + previous.OperationId +
                    ". Poll save_document_status until it reports completed or failed.");

            return new SaveDocumentJobState(
                operationId.Trim(),
                SaveDocumentJobStates.Running,
                nowUtc,
                null,
                string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
                null,
                0,
                null);
        }

        public SaveDocumentJobState Complete(string path, string format, long fileSizeBytes, DateTime nowUtc)
        {
            EnsureRunning("complete");
            return new SaveDocumentJobState(
                OperationId,
                SaveDocumentJobStates.Completed,
                StartedAtUtc,
                nowUtc,
                path,
                format,
                fileSizeBytes,
                null);
        }

        public SaveDocumentJobState Fail(string errorMessage, DateTime nowUtc)
        {
            EnsureRunning("fail");
            return new SaveDocumentJobState(
                OperationId,
                SaveDocumentJobStates.Failed,
                StartedAtUtc,
                nowUtc,
                Path,
                Format,
                FileSizeBytes,
                string.IsNullOrWhiteSpace(errorMessage) ? string.Empty : errorMessage);
        }

        public long ElapsedMs(DateTime nowUtc)
        {
            return (long)((CompletedAtUtc ?? nowUtc) - StartedAtUtc).TotalMilliseconds;
        }

        public StartSaveDocumentResponse BuildStartResponse(DateTime nowUtc)
        {
            return new StartSaveDocumentResponse
            {
                OperationId = OperationId,
                State = State,
                IsRunning = IsRunning,
                Path = Path,
                ElapsedMs = ElapsedMs(nowUtc),
                Message = "Save started. Poll save_document_status with operationId " + OperationId + " until it reports completed or failed.",
            };
        }

        public SaveDocumentStatusResponse BuildStatusResponse(DateTime nowUtc)
        {
            return new SaveDocumentStatusResponse
            {
                OperationId = OperationId,
                State = State,
                IsRunning = IsRunning,
                Path = Path,
                Format = Format,
                FileSizeBytes = FileSizeBytes,
                ElapsedMs = ElapsedMs(nowUtc),
                ErrorMessage = ErrorMessage ?? string.Empty,
                StartedAtUtc = StartedAtUtc,
                CompletedAtUtc = CompletedAtUtc,
                Message = BuildMessage(),
            };
        }

        private void EnsureRunning(string action)
        {
            if (!IsRunning)
                throw new InvalidOperationException("Only a running save job can " + action + "; current state is " + State + ".");
        }

        private string BuildMessage()
        {
            if (IsRunning)
                return "Document save is still running on the Navisworks UI thread.";
            if (string.Equals(State, SaveDocumentJobStates.Completed, StringComparison.OrdinalIgnoreCase))
                return "Document save completed: " + (Path ?? string.Empty);
            return "Document save failed: " + (ErrorMessage ?? string.Empty);
        }
    }
}
