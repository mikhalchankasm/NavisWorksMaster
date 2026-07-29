using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NavisHelper.Core.Localization;

namespace NavisHelper.WPF
{
    internal sealed class PanelLocalizationBindings : IDisposable
    {
        private readonly UiLocalizationService _localization;
        private readonly Dispatcher _dispatcher;
        private readonly UiLocalizationBindingRegistry _registry =
            new UiLocalizationBindingRegistry();
        private bool _isAttached;
        private bool _isDisposed;

        internal PanelLocalizationBindings(
            UiLocalizationService localization,
            Dispatcher dispatcher)
        {
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        internal int Count => _registry.Count;

        internal string Text(string resourceKey)
        {
            return _localization.GetString(resourceKey);
        }

        internal void BindText(TextBlock target, string resourceKey)
        {
            Register(target, "Text", resourceKey, value => target.Text = value);
        }

        internal void BindContent(ContentControl target, string resourceKey)
        {
            Register(target, "Content", resourceKey, value => target.Content = value);
        }

        internal void BindHeader(HeaderedContentControl target, string resourceKey)
        {
            Register(target, "Header", resourceKey, value => target.Header = value);
        }

        internal void BindHeader(HeaderedItemsControl target, string resourceKey)
        {
            Register(target, "Header", resourceKey, value => target.Header = value);
        }

        internal void BindToolTip(FrameworkElement target, string resourceKey)
        {
            Register(target, "ToolTip", resourceKey, value => target.ToolTip = value);
        }

        internal void BindColumnHeader(DataGridColumn target, string resourceKey)
        {
            Register(target, "Header", resourceKey, value => target.Header = value);
        }

        internal void BindFormattedText(
            TextBlock target,
            string resourceKey,
            Func<object[]> arguments)
        {
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));

            _registry.Register(
                target,
                "Text:" + resourceKey,
                () => target.Text = _localization.Format(resourceKey, arguments()));
        }

        internal void BindAction(object owner, string semanticId, Action refresh)
        {
            _registry.Register(owner, "Action:" + semanticId, refresh);
        }

        internal void UnbindAction(object owner, string semanticId)
        {
            _registry.Unregister(owner, "Action:" + semanticId);
        }

        internal void Attach()
        {
            if (_isDisposed || _isAttached)
                return;

            _localization.LanguageChanged += OnLanguageChanged;
            _isAttached = true;
            Refresh();
        }

        internal void Detach()
        {
            if (!_isAttached)
                return;

            _localization.LanguageChanged -= OnLanguageChanged;
            _isAttached = false;
        }

        internal void Refresh()
        {
            if (_isDisposed)
                return;

            if (_dispatcher.CheckAccess())
            {
                _registry.Refresh();
                return;
            }

            _ = _dispatcher.BeginInvoke(new Action(_registry.Refresh));
        }

        public void Dispose()
        {
            if (_isDisposed)
                return;

            Detach();
            _registry.Dispose();
            _isDisposed = true;
        }

        private void Register(
            object target,
            string slot,
            string resourceKey,
            Action<string> setter)
        {
            if (string.IsNullOrWhiteSpace(resourceKey))
                throw new ArgumentException("A semantic resource key is required.", nameof(resourceKey));

            _registry.Register(
                target,
                slot,
                () => setter(_localization.GetString(resourceKey)));
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            Refresh();
        }
    }
}
