using System;
using System.Runtime.InteropServices;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;
using Application = Autodesk.Navisworks.Api.Application;

namespace NavisHelper.Agent.Services
{
    internal sealed class NavisworksApplicationCloseService
    {
        private const int WmClose = 0x0010;
        private readonly DocumentCommandService _documentCommandService;

        internal NavisworksApplicationCloseService(DocumentCommandService documentCommandService)
        {
            _documentCommandService = documentCommandService ??
                                      throw new ArgumentNullException(nameof(documentCommandService));
        }

        internal CloseNavisworksResponse Prepare(Document document, CloseNavisworksRequest request)
        {
            request = request ?? new CloseNavisworksRequest();
            string mode;
            try
            {
                mode = NavisworksCloseOptionsHelper.NormalizeMode(request.Mode);
                NavisworksCloseOptionsHelper.ValidateAuthorization(
                    request.Apply.GetValueOrDefault(false),
                    request.ConfirmClose.GetValueOrDefault(false));
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }

            var apply = request.Apply.GetValueOrDefault(false);
            var wasModified = document != null && document.IsModified;
            var response = new CloseNavisworksResponse
            {
                Mode = mode,
                Apply = apply,
                DocumentWasModified = wasModified,
                DocumentPath = document == null ? string.Empty : document.FileName ?? string.Empty,
                NativePromptExpected = mode == NavisworksCloseModes.Prompt && wasModified,
                Message = BuildPlanMessage(mode, apply, wasModified),
            };

            if (!apply)
                return response;

            if (mode == NavisworksCloseModes.Save)
            {
                if (document == null)
                    throw new AgentCommandException(ErrorCodes.NoActiveDocument, "There is no active document to save.");

                SaveDocumentResponse saveResult;
                if (string.IsNullOrWhiteSpace(request.SavePath))
                {
                    saveResult = _documentCommandService.SaveDocument(document, new SaveDocumentRequest());
                }
                else
                {
                    saveResult = _documentCommandService.SaveDocumentAs(
                        document,
                        new SaveDocumentAsRequest
                        {
                            Path = request.SavePath,
                            Overwrite = request.Overwrite,
                        });
                }

                if (document.IsModified)
                {
                    throw new AgentCommandException(
                        ErrorCodes.CommandFailed,
                        "The document is still marked as modified after saving; Navisworks will not be closed.");
                }
                response.SavedPath = saveResult.Path;
            }
            else if (mode == NavisworksCloseModes.Discard && document != null && !document.IsClear)
            {
                document.Clear();
                if (!document.IsClear || document.IsModified)
                {
                    throw new AgentCommandException(
                        ErrorCodes.CommandFailed,
                        "The discard operation already ran, but Navisworks did not reach a clean, unmodified document state; exit was not scheduled.");
                }
                response.DiscardedUnsavedChanges = wasModified;
            }

            response.ExitScheduled = true;
            response.Message = mode == NavisworksCloseModes.Prompt
                ? "Navisworks exit is scheduled; the native save prompt may remain open."
                : "Navisworks exit is scheduled.";
            return response;
        }

        internal static void ExecuteScheduledExit()
        {
            try
            {
                var mainWindow = Application.Gui == null ? null : Application.Gui.MainWindow;
                if (mainWindow == null || mainWindow.Handle == IntPtr.Zero)
                    throw new InvalidOperationException("Navisworks main window handle is unavailable.");
                if (!PostMessage(mainWindow.Handle, WmClose, IntPtr.Zero, IntPtr.Zero))
                    throw new InvalidOperationException("WM_CLOSE could not be posted to the Navisworks main window.");
            }
            catch (Exception ex)
            {
                Logger.Error("Scheduled Navisworks exit failed: " + ex, "CloseNavisworks");
            }
        }

        private static string BuildPlanMessage(string mode, bool apply, bool wasModified)
        {
            if (apply)
                return "Navisworks close preparation is running.";
            if (mode == NavisworksCloseModes.Save)
                return "Preview: save the active document, then close Navisworks.";
            if (mode == NavisworksCloseModes.Discard)
                return wasModified
                    ? "Preview: discard unsaved changes, then close Navisworks."
                    : "Preview: close Navisworks without saving.";
            return "Preview: request normal Navisworks exit and allow its native save prompt.";
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(
            IntPtr windowHandle,
            int message,
            IntPtr wParam,
            IntPtr lParam);
    }
}
