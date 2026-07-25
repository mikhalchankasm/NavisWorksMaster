using System;
using System.Threading;
using System.Windows.Forms;
using NavisHelper.Agent.Host;
using NavisHelper.Core;

namespace NavisHelper.Agent
{
    internal static class AgentRuntime
    {
        private static readonly object Sync = new object();
        private static readonly object InteractiveBusySync = new object();
        private static AgentHostService _hostService;
        private static Control _backgroundControl;
        private static int _interactiveBusyCount;
        private static string _interactiveBusyReason = string.Empty;

        public static bool IsInteractiveBusy
        {
            get
            {
                lock (InteractiveBusySync)
                    return _interactiveBusyCount > 0;
            }
        }

        public static string InteractiveBusyReason
        {
            get
            {
                lock (InteractiveBusySync)
                    return _interactiveBusyReason ?? string.Empty;
            }
        }

        public static IDisposable BeginInteractiveOperation(string reason)
        {
            lock (InteractiveBusySync)
            {
                _interactiveBusyCount++;
                _interactiveBusyReason = reason ?? string.Empty;
            }

            return new InteractiveBusyScope();
        }

        public static void Initialize(SynchronizationContext synchronizationContext)
        {
            if (synchronizationContext == null)
            {
                Logger.Info("SynchronizationContext is null. AgentHost disabled.", "AgentHost");
                return;
            }

            lock (Sync)
            {
                if (_hostService != null)
                {
                    _hostService.AttachSynchronizationContext(synchronizationContext);
                    return;
                }

                var hostService = new AgentHostService();
                try
                {
                    hostService.Start(synchronizationContext);
                    _hostService = hostService;
                    Logger.Info("AgentRuntime initialized via SynchronizationContext.", "AgentHost");
                }
                catch (System.Exception ex)
                {
                    try
                    {
                        hostService.Dispose();
                    }
                    catch
                    {
                    }

                    Logger.Error("Failed to initialize AgentRuntime via SynchronizationContext: " + ex, "AgentHost");
                }
            }
        }

        public static void Initialize(Control control)
        {
            if (control == null)
            {
                Logger.Info("Control is null. AgentHost disabled.", "AgentHost");
                return;
            }

            lock (Sync)
            {
                if (_hostService != null)
                {
                    _hostService.AttachControl(control);
                    return;
                }

                var hostService = new AgentHostService();
                try
                {
                    hostService.Start(control);
                    _hostService = hostService;
                    Logger.Info("AgentRuntime initialized via Control.", "AgentHost");
                }
                catch (System.Exception ex)
                {
                    try
                    {
                        hostService.Dispose();
                    }
                    catch
                    {
                    }

                    Logger.Error("Failed to initialize AgentRuntime via Control: " + ex, "AgentHost");
                }
            }
        }

        public static void EnsureBackgroundHost()
        {
            RestoreBackgroundControl();
        }

        public static void RestoreBackgroundControl()
        {
            lock (Sync)
            {
                if (_backgroundControl == null || _backgroundControl.IsDisposed)
                {
                    _backgroundControl = new Control();
                    var unused = _backgroundControl.Handle;
                    Logger.Info("AgentRuntime background control created.", "AgentHost");
                }

                if (_hostService != null)
                {
                    _hostService.AttachControl(_backgroundControl);
                    return;
                }

                var hostService = new AgentHostService();
                try
                {
                    hostService.Start(_backgroundControl);
                    _hostService = hostService;
                    Logger.Info("AgentRuntime initialized via background control.", "AgentHost");
                }
                catch (Exception ex)
                {
                    try
                    {
                        hostService.Dispose();
                    }
                    catch
                    {
                    }

                    Logger.Error("Failed to initialize AgentRuntime via background control: " + ex, "AgentHost");
                }
            }
        }

        public static void Shutdown()
        {
            lock (Sync)
            {
                if (_hostService != null)
                {
                    _hostService.Dispose();
                    _hostService = null;
                }

                if (_backgroundControl != null)
                {
                    try
                    {
                        _backgroundControl.Dispose();
                    }
                    catch
                    {
                    }

                    _backgroundControl = null;
                }
            }
        }

        private sealed class InteractiveBusyScope : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                    return;

                lock (InteractiveBusySync)
                {
                    if (_interactiveBusyCount > 0)
                        _interactiveBusyCount--;

                    if (_interactiveBusyCount <= 0)
                    {
                        _interactiveBusyCount = 0;
                        _interactiveBusyReason = string.Empty;
                    }
                }
            }
        }
    }
}
