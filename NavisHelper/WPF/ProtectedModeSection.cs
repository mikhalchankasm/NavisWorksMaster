using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NavisHelper.Agent;
using NavisHelper.Agent.Services;
using NavisHelper.Core;
using NavisHelper.Core.Localization;

namespace NavisHelper.WPF
{
    internal sealed class ProtectedModeSection : IDisposable
    {
        private readonly ProtectedModeApprovals _approvals = AgentRuntime.ProtectedModeApprovals;
        private readonly ProtectedModeSettingsStore _store = ProtectedModeSettingsStore.CreateDefault();
        private readonly PanelLocalizationBindings _bindings;
        private readonly UiLocalizationService _localization = UiLocalizationService.Current;
        private readonly Action<string, Brush, object[]> _setStatusResource;
        private readonly Dispatcher _dispatcher;
        private readonly StackPanel _root = new StackPanel();
        private readonly StackPanel _pending = new StackPanel();
        private readonly StackPanel _approved = new StackPanel();
        private readonly CheckBox _enabled = new CheckBox();
        private HashSet<PendingApproval> _seenPending = new HashSet<PendingApproval>();
        private bool _refreshing;
        private volatile bool _disposed;

        internal ProtectedModeSection(PanelLocalizationBindings bindings, Dispatcher dispatcher,
            Action<string, Brush, object[]> setStatusResource)
        {
            _bindings = bindings;
            _dispatcher = dispatcher;
            _setStatusResource = setStatusResource;
            _bindings.BindContent(_enabled, "Settings_ProtectedMode_Enable");
            _enabled.Checked += OnToggle;
            _enabled.Unchecked += OnToggle;
            _root.Children.Add(_enabled);
            _root.Children.Add(Label("Settings_ProtectedMode_Explanation"));
            _root.Children.Add(Label("Settings_ProtectedMode_Pending"));
            _root.Children.Add(_pending);
            _root.Children.Add(Label("Settings_ProtectedMode_Approved"));
            _root.Children.Add(_approved);
            _approvals.Changed += OnChanged;
            // One binding for all dynamic rows avoids retaining discarded controls on each refresh.
            _bindings.BindAction(this, "ProtectedMode.Rows", Refresh);
        }

        internal UIElement Build() => _root;

        private TextBlock Label(string key)
        {
            var text = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = UiTheme.TextSecondary,
                Margin = new Thickness(0, UiTheme.Gap, 0, UiTheme.Gap)
            };
            _bindings.BindText(text, key);
            return text;
        }

        private void OnToggle(object sender, RoutedEventArgs args)
        {
            if (_refreshing || _disposed) return;
            var enabled = _enabled.IsChecked == true;
            var persisted = _store.TryWriteEnabled(enabled);
            _approvals.Enabled = enabled;
            if (!persisted)
                _setStatusResource("Settings_ProtectedMode_PersistenceWarning", UiTheme.Warning, new object[0]);
        }

        private void OnChanged(object sender, EventArgs args)
        {
            if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
            _dispatcher.BeginInvoke(new Action(Refresh));
        }

        private void Refresh()
        {
            if (_disposed) return;
            _refreshing = true;
            try
            {
                _enabled.IsChecked = _approvals.Enabled;
                var pending = _approvals.Pending;
                var newRequest = pending.Any(request => !_seenPending.Contains(request));
                _seenPending = new HashSet<PendingApproval>(pending);
                _pending.Children.Clear();
                foreach (var request in pending)
                {
                    var row = Row(_localization.Format("Settings_ProtectedMode_RequestFormat",
                        request.ClientName, DocumentName(request.DocumentKey), request.Code));
                    row.Children.Add(ActionButton("Settings_ProtectedMode_Allow", () => _approvals.Approve(request.Code)));
                    row.Children.Add(ActionButton("Settings_ProtectedMode_Deny", () => _approvals.Deny(request.Code)));
                    _pending.Children.Add(row);
                }
                if (pending.Count == 0) AddEmpty(_pending);
                _approved.Children.Clear();
                var approved = _approvals.Approved;
                foreach (var grant in approved)
                {
                    var row = Row(_localization.Format("Settings_ProtectedMode_GrantFormat",
                        grant.ClientName, DocumentName(grant.DocumentKey)));
                    row.Children.Add(ActionButton("Settings_ProtectedMode_Revoke",
                        () => _approvals.Revoke(grant.DocumentKey, grant.SessionId)));
                    _approved.Children.Add(row);
                }
                if (approved.Count == 0) AddEmpty(_approved);
                if (newRequest)
                    _setStatusResource("Settings_ProtectedMode_RequestStatus", UiTheme.Warning, new object[0]);
            }
            finally { _refreshing = false; }
        }

        private string DocumentName(string documentKey)
        {
            if (string.IsNullOrEmpty(documentKey))
                return _bindings.Text("Settings_ProtectedMode_UnsavedDocument");
            try { return Path.GetFileName(documentKey); }
            catch (ArgumentException) { return documentKey; }
        }

        private static WrapPanel Row(string details)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, UiTheme.Gap) };
            row.Children.Add(new TextBlock
            {
                Text = details,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, UiTheme.Gap, 0)
            });
            return row;
        }

        private Button ActionButton(string key, Action action)
        {
            return UiTheme.ToolButton(_bindings.Text(key), _bindings.Text(key), action);
        }

        private void AddEmpty(StackPanel list)
        {
            list.Children.Add(new TextBlock
            {
                Text = _bindings.Text("Settings_ProtectedMode_Empty"), Foreground = UiTheme.TextMuted
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _approvals.Changed -= OnChanged;
            _enabled.Checked -= OnToggle;
            _enabled.Unchecked -= OnToggle;
            _bindings.UnbindAction(this, "ProtectedMode.Rows");
        }
    }
}
