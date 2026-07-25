using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Autodesk.Navisworks.Api.DocumentParts;
using NavisHelper.Core;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisHelper.WPF
{
    /// <summary>
    /// Keeps buttons that require a model selection synchronized with the active document.
    /// Selection notifications are coalesced and never traverse model items.
    /// </summary>
    internal sealed class SelectionGatingController
    {
        private readonly Dispatcher _dispatcher;
        private readonly List<Button> _buttons = new List<Button>();
        private readonly object _updateSync = new object();
        private DocumentCurrentSelection _selection;
        private volatile bool _applicationEventsAttached;
        private bool _updatePending;
        private DispatcherOperation _pendingUpdate;

        public SelectionGatingController(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public void Register(Button button)
        {
            if (button == null || _buttons.Contains(button)) return;
            _buttons.Add(button);
            ToolTipService.SetShowOnDisabled(button, true);
            button.IsEnabled = _applicationEventsAttached && TryHasSelection();
        }

        public void Attach()
        {
            if (!_applicationEventsAttached)
            {
                NwApplication.ActiveDocumentChanged += OnActiveDocumentChanged;
                _applicationEventsAttached = true;
            }

            AttachCurrentSelectionChanged();
            UpdateButtons();
        }

        public void Detach()
        {
            if (_applicationEventsAttached)
            {
                _applicationEventsAttached = false;
                try
                {
                    NwApplication.ActiveDocumentChanged -= OnActiveDocumentChanged;
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to detach active-document event: " + ex, "SelectionGating");
                }
            }

            DetachCurrentSelectionChanged();
            CancelPendingUpdate();
        }

        private void OnActiveDocumentChanged(object sender, EventArgs e)
        {
            if (IsDispatcherUnavailable()) return;
            try
            {
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_applicationEventsAttached) return;
                    try
                    {
                        AttachCurrentSelectionChanged();
                        UpdateButtons();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error("Failed to update selection gating after document change: " + ex, "SelectionGating");
                    }
                }), DispatcherPriority.Background);
            }
            catch (InvalidOperationException)
            {
                // The host dispatcher is shutting down; no UI update is possible.
            }
        }

        private void AttachCurrentSelectionChanged()
        {
            var current = NwApplication.ActiveDocument?.CurrentSelection;
            if (ReferenceEquals(current, _selection)) return;

            DetachCurrentSelectionChanged();
            _selection = current;
            if (_selection != null)
            {
                try
                {
                    _selection.Changed += OnCurrentSelectionChanged;
                }
                catch
                {
                    _selection = null;
                    throw;
                }
            }
        }

        private void DetachCurrentSelectionChanged()
        {
            var selection = _selection;
            _selection = null;
            if (selection == null) return;

            try
            {
                selection.Changed -= OnCurrentSelectionChanged;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to detach current-selection event: " + ex, "SelectionGating");
            }
        }

        private void OnCurrentSelectionChanged(object sender, EventArgs e)
        {
            QueueUpdate();
        }

        private void QueueUpdate()
        {
            if (!_applicationEventsAttached || IsDispatcherUnavailable()) return;

            lock (_updateSync)
            {
                if (_updatePending) return;
                _updatePending = true;
                try
                {
                    _pendingUpdate = _dispatcher.BeginInvoke(new Action(() =>
                    {
                        lock (_updateSync)
                        {
                            _updatePending = false;
                            _pendingUpdate = null;
                        }

                        if (!_applicationEventsAttached) return;
                        try
                        {
                            UpdateButtons();
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Failed to update selection-dependent buttons: " + ex, "SelectionGating");
                        }
                    }), DispatcherPriority.Background);
                }
                catch (InvalidOperationException)
                {
                    _updatePending = false;
                    _pendingUpdate = null;
                }
            }
        }

        private void CancelPendingUpdate()
        {
            DispatcherOperation operation;
            lock (_updateSync)
            {
                operation = _pendingUpdate;
                _pendingUpdate = null;
                _updatePending = false;
            }

            if (operation != null && operation.Status == DispatcherOperationStatus.Pending)
                operation.Abort();
        }

        private void UpdateButtons()
        {
            var hasSelection = TryHasSelection();
            foreach (var button in _buttons)
            {
                if (button != null) button.IsEnabled = hasSelection;
            }
        }

        private static bool TryHasSelection()
        {
            try
            {
                var selection = NwApplication.ActiveDocument?.CurrentSelection?.SelectedItems;
                return selection != null && selection.Count > 0;
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to read current selection state: " + ex, "SelectionGating");
                return false;
            }
        }

        private bool IsDispatcherUnavailable()
        {
            return _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished;
        }
    }
}
