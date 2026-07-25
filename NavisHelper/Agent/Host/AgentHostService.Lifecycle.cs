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

        public void Start(SynchronizationContext uiContext)
        {
            if (uiContext == null)
                throw new ArgumentNullException(nameof(uiContext));

            lock (_listenerSync)
            {
                if (_isStarted)
                    return;

                _uiContext = uiContext;
                _uiControl = null;
                _shutdownCts = new CancellationTokenSource();

                var startedAt = DateTime.UtcNow;
                var version = DetectNavisworksVersion();
                var pid = Process.GetCurrentProcess().Id;
                _startedAtUtc = startedAt;
                _processStartedAtUtc = GetCurrentProcessStartTimeUtc();

                _instanceId = string.Format("nw-{0}-{1}-{2:yyyyMMddTHHmmssZ}", version, pid, startedAt);
                _pipeName = "navishelper-mcp-" + pid;
                _discoveryFilePath = Path.Combine(GetInstancesDirectory(), _instanceId + ".json");

                CleanupOwnStaleDiscoveryFiles(pid);
                WriteDiscoveryFile(GetDocumentTitleSafe());
                SubscribeToApplicationEvents();
                AttachTrackedDocument(Autodesk.Navisworks.Api.Application.ActiveDocument);

                for (var index = 0; index < ListenerSlots; index++)
                {
                    StartListener();
                }

                _isStarted = true;
                Logger.Info("AgentHost started. Pipe=" + _pipeName + " " + GetLoadedAssemblyInfo(), "AgentHost");
            }
        }

        public void Start(Control uiControl)
        {
            if (uiControl == null)
                throw new ArgumentNullException(nameof(uiControl));

            lock (_listenerSync)
            {
                if (_isStarted)
                    return;

                _uiContext = null;
                _uiControl = uiControl;
                _shutdownCts = new CancellationTokenSource();

                var startedAt = DateTime.UtcNow;
                var version = DetectNavisworksVersion();
                var pid = Process.GetCurrentProcess().Id;
                _startedAtUtc = startedAt;
                _processStartedAtUtc = GetCurrentProcessStartTimeUtc();

                _instanceId = string.Format("nw-{0}-{1}-{2:yyyyMMddTHHmmssZ}", version, pid, startedAt);
                _pipeName = "navishelper-mcp-" + pid;
                _discoveryFilePath = Path.Combine(GetInstancesDirectory(), _instanceId + ".json");

                CleanupOwnStaleDiscoveryFiles(pid);
                WriteDiscoveryFile(GetDocumentTitleSafe());
                SubscribeToApplicationEvents();
                AttachTrackedDocument(Autodesk.Navisworks.Api.Application.ActiveDocument);

                for (var index = 0; index < ListenerSlots; index++)
                {
                    StartListener();
                }

                _isStarted = true;
                Logger.Info("AgentHost started. Pipe=" + _pipeName + " " + GetLoadedAssemblyInfo(), "AgentHost");
            }
        }

        public void Stop()
        {
            lock (_listenerSync)
            {
                if (!_isStarted)
                    return;

                _shutdownCts.Cancel();

                foreach (var listener in _listeners.ToArray())
                {
                    try
                    {
                        listener.Dispose();
                    }
                    catch
                    {
                    }
                }

                _listeners.Clear();
                _matchSessionStore.Clear();
                _searchService.InvalidateRootSearchIndex();
                _commandService.FailRunningSubtreeNameDumps("Agent host stopped while dump job was running.");
                _commandService.FailRunningClashReport("Agent host stopped while clash report was running.");
                _clashIsolationService.ResetForDocumentChange(Autodesk.Navisworks.Api.Application.ActiveDocument);
                _modelColorSchemeService.DiscardForDocumentChange();
                DetachTrackedDocument();
                UnsubscribeApplicationEvents();
                DeleteDiscoveryFile();

                _shutdownCts.Dispose();
                _shutdownCts = null;
                _uiContext = null;
                _uiControl = null;
                _lastDocumentFileName = string.Empty;
                _lastDiscoveryDocumentTitle = string.Empty;
                _isStarted = false;

                Logger.Info("AgentHost stopped.", "AgentHost");
            }
        }

        public void AttachSynchronizationContext(SynchronizationContext uiContext)
        {
            if (uiContext == null)
                return;

            lock (_listenerSync)
            {
                _uiContext = uiContext;
                _uiControl = null;
                Logger.Info("AgentHost synchronization context updated and preferred over the background control dispatcher.", "AgentHost");
            }
        }

        public void AttachControl(Control uiControl)
        {
            if (uiControl == null)
                return;

            lock (_listenerSync)
            {
                _uiControl = uiControl;
                Logger.Info("AgentHost control dispatcher attached.", "AgentHost");
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
