using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NavisHelper.Agent.Services;

namespace NavisHelper.WPF
{
    internal sealed class DatasmithExportSection : IDisposable
    {
        private readonly PanelLocalizationBindings _bindings;
        private readonly Dispatcher _dispatcher;
        private readonly Action<string, Brush, object[]> _setStatusResource;
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _elapsed = new Stopwatch();
        private readonly StackPanel _root = new StackPanel();
        private readonly TextBlock _status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = UiTheme.TextSecondary,
            Margin = new Thickness(0, UiTheme.Gap, 0, UiTheme.Gap)
        };
        private readonly Button _export;
        private readonly Button _cancel;
        private DatasmithPanelExportJob _job;
        private string _outputPath;
        private string _statusKey;
        private object[] _statusArguments = new object[0];
        private bool _running;
        private bool _disposed;

        internal DatasmithExportSection(PanelLocalizationBindings bindings, Dispatcher dispatcher,
            Action<string, Brush, object[]> setStatusResource)
        {
            _bindings = bindings;
            _dispatcher = dispatcher;
            _setStatusResource = setStatusResource;
            _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += OnTick;
            _export = Button("Panel_Datasmith_Export_Action", OnExport);
            _cancel = Button("Panel_Datasmith_Cancel_Action", OnCancel);
            _cancel.IsEnabled = false;
            var actions = new WrapPanel();
            actions.Children.Add(_export);
            actions.Children.Add(_cancel);
            _root.Children.Add(actions);
            _root.Children.Add(_status);
            _bindings.BindAction(this, "DatasmithExport.Status", RefreshStatusText);
        }

        internal UIElement Build() => _root;

        private Button Button(string key, RoutedEventHandler handler)
        {
            var button = UiTheme.ToolButton(_bindings.Text(key), _bindings.Text(key), () => { });
            _bindings.BindContent(button, key);
            _bindings.BindToolTip(button, key);
            button.Click += handler;
            return button;
        }

        private void OnExport(object sender, RoutedEventArgs args)
        {
            if (_disposed || _running) return;
            try
            {
                var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
                var sourcePath = document == null || document.IsClear ? null : document.FileName;
                var extension = Path.GetExtension(sourcePath);
                if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)
                    || !(string.Equals(extension, ".nwd", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(extension, ".nwf", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(extension, ".nwc", StringComparison.OrdinalIgnoreCase)))
                {
                    ShowStatus("Panel_Datasmith_NotSaved_Status", UiTheme.Warning);
                    return;
                }
                var missing = DatasmithPanelExportJob.MissingDependency();
                if (missing != null)
                {
                    ShowStatus("Panel_Datasmith_Unavailable_Status", UiTheme.Warning, missing);
                    return;
                }
                var dialog = new SaveFileDialog
                {
                    Title = _bindings.Text("Panel_Datasmith_Dialog_Title"),
                    Filter = "Datasmith (*.udatasmith)|*.udatasmith",
                    DefaultExt = ".udatasmith",
                    AddExtension = true,
                    OverwritePrompt = true,
                    FileName = Path.GetFileNameWithoutExtension(sourcePath) + ".udatasmith",
                    InitialDirectory = Path.GetDirectoryName(sourcePath)
                };
                if (dialog.ShowDialog() != true || _disposed) return;
                _outputPath = dialog.FileName;
                _job = new DatasmithPanelExportJob();
                _job.Completed += OnCompleted;
                SetRunning(true);
                _elapsed.Restart();
                OnTick(this, EventArgs.Empty);
                _job.Start(sourcePath, _outputPath);
            }
            catch (Exception exception)
            {
                ReleaseJob();
                SetRunning(false);
                ShowStatus("Panel_Datasmith_Failed_Status", UiTheme.Error, exception.Message);
            }
        }

        private void OnCancel(object sender, RoutedEventArgs args)
        {
            if (_disposed || !_running) return;
            _cancel.IsEnabled = false;
            _job.Cancel();
        }

        private void OnTick(object sender, EventArgs args)
        {
            if (_disposed || !_running) return;
            ShowStatus("Panel_Datasmith_Running_Status", UiTheme.TextSecondary, (long)_elapsed.Elapsed.TotalSeconds);
        }

        private void OnCompleted(object sender, EventArgs args)
        {
            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed || !ReferenceEquals(sender, _job)) return;
                var job = _job;
                SetRunning(false);
                if (job.Canceled)
                    ShowStatus("Panel_Datasmith_Canceled_Status", UiTheme.Warning);
                else if (job.Ok)
                    ShowStatus("Panel_Datasmith_Completed_Status", UiTheme.Success,
                        _outputPath, (job.FileSizeBytes / (1024d * 1024d)).ToString("F2", CultureInfo.CurrentCulture));
                else
                    ShowStatus("Panel_Datasmith_Failed_Status", UiTheme.Error, job.Error);
                ReleaseJob();
            }));
        }

        private void SetRunning(bool running)
        {
            _running = running;
            _export.IsEnabled = !running;
            _cancel.IsEnabled = running;
            if (running) _timer.Start();
            else
            {
                _timer.Stop();
                _elapsed.Stop();
            }
        }

        private void ShowStatus(string key, Brush color, params object[] arguments)
        {
            _statusKey = key;
            _statusArguments = arguments;
            _status.Foreground = color;
            RefreshStatusText();
            _setStatusResource(key, color, arguments);
        }

        private void RefreshStatusText()
        {
            if (_disposed || _statusKey == null) return;
            _status.Text = string.Format(CultureInfo.CurrentCulture, _bindings.Text(_statusKey), _statusArguments);
        }

        private void ReleaseJob()
        {
            if (_job == null) return;
            _job.Completed -= OnCompleted;
            _job.Dispose();
            _job = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SetRunning(false);
            _timer.Tick -= OnTick;
            _export.Click -= OnExport;
            _cancel.Click -= OnCancel;
            ReleaseJob();
            _bindings.UnbindAction(this, "DatasmithExport.Status");
        }
    }
}
