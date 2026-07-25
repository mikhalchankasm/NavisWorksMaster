using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ApplicationParts;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using NavisHelper.Agent.Session;
using NavisHelper.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace NavisHelper.Agent.Host
{
    internal sealed partial class AgentHostService : IDisposable
    {

        private HostStatusResponse BuildHostStatus(Document document)
        {
            var process = Process.GetCurrentProcess();
            var hasDocument = document != null;
            var documentFileName = hasDocument ? document.FileName ?? string.Empty : string.Empty;
            var documentTitle = string.IsNullOrWhiteSpace(documentFileName)
                ? string.Empty
                : Path.GetFileName(documentFileName);
            var modelCount = hasDocument && document.Models != null
                ? document.Models.Count()
                : 0;
            var pluginAssembly = GetPluginAssemblyFileInfo();

            return new HostStatusResponse
            {
                ProtocolVersion = ProtocolConstants.CurrentProtocolVersion,
                InstanceId = _instanceId ?? string.Empty,
                Pid = process.Id,
                NavisworksVersion = DetectNavisworksVersion(),
                HasActiveDocument = hasDocument,
                DocumentTitle = documentTitle,
                DocumentFileName = documentFileName,
                ModelCount = modelCount,
                RootItemCount = hasDocument ? _searchService.GetRootItemCount(document) : 0,
                WorkingSetMb = Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 1),
                PluginVersion = pluginAssembly.Version,
                PluginAssemblyPath = pluginAssembly.Path,
                PluginAssemblyLastWriteUtc = pluginAssembly.LastWriteUtc,
                PluginAssemblyLength = pluginAssembly.Length,
                // AgentHost writes centralized request diagnostics to the shared
                // temp log so the path remains valid even for unsaved models.
                HostLogFilePath = Logger.GetLogFilePath(),
            };
        }

        private void SubscribeToApplicationEvents()
        {
            Autodesk.Navisworks.Api.Application.ActiveDocumentChanging += OnActiveDocumentChanging;
            Autodesk.Navisworks.Api.Application.ActiveDocumentChanged += OnActiveDocumentChanged;
        }

        private void UnsubscribeApplicationEvents()
        {
            Autodesk.Navisworks.Api.Application.ActiveDocumentChanging -= OnActiveDocumentChanging;
            Autodesk.Navisworks.Api.Application.ActiveDocumentChanged -= OnActiveDocumentChanged;
        }

        private void OnActiveDocumentChanging(object sender, EventArgs e)
        {
            _clashIsolationService.ResetForDocumentChange(Autodesk.Navisworks.Api.Application.ActiveDocument);
            _modelColorSchemeService.DiscardForDocumentChange();
            _matchSessionStore.Clear();
            _searchService.InvalidateRootSearchIndex();
            _commandService.FailRunningSubtreeNameDumps("Active document changed while dump job was running.");
            _commandService.FailRunningClashReport("Active document changed while clash report was running.");
            DetachTrackedDocument();
            UpdateTrackedDocumentMetadata(null);
            RefreshDiscoveryFile(null);
        }

        private void OnActiveDocumentChanged(object sender, EventArgs e)
        {
            var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            _clashIsolationService.DiscardForDocumentChange();
            _modelColorSchemeService.DiscardForDocumentChange();
            _matchSessionStore.Clear();
            _searchService.InvalidateRootSearchIndex();
            AttachTrackedDocument(document);
            RefreshDiscoveryFile(document);
        }

        private void OnTrackedDocumentFileNameChanged(object sender, EventArgs e)
        {
            var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
            _clashIsolationService.HandleDocumentFileNameChanged(document);
            _modelColorSchemeService.HandleDocumentFileNameChanged(document);
            _matchSessionStore.Clear();
            _searchService.InvalidateRootSearchIndex();
            _commandService.FailRunningSubtreeNameDumps("Active document file name changed while dump job was running.");
            _commandService.FailRunningClashReport("Active document file name changed while clash report was running.");
            AttachTrackedDocument(document);
            RefreshDiscoveryFile(document);
        }

        private void AttachTrackedDocument(Document document)
        {
            if (ReferenceEquals(_trackedDocument, document))
            {
                UpdateTrackedDocumentMetadata(document);
                return;
            }

            DetachTrackedDocument();
            _trackedDocument = document;

            if (_trackedDocument != null)
                _trackedDocument.FileNameChanged += OnTrackedDocumentFileNameChanged;

            UpdateTrackedDocumentMetadata(document);
        }

        private void DetachTrackedDocument()
        {
            if (_trackedDocument != null)
            {
                _trackedDocument.FileNameChanged -= OnTrackedDocumentFileNameChanged;
                _trackedDocument = null;
            }
        }

        private void UpdateTrackedDocumentMetadata(Document document)
        {
            _lastDocumentFileName = document == null
                ? string.Empty
                : document.FileName ?? string.Empty;
        }
    }
}
