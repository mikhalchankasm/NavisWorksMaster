using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using NavisHelper.Core;

namespace NavisHelper.WPF
{
    internal sealed class FbxLevelExportSection : IDisposable
    {
        private readonly PanelLocalizationBindings _bindings;
        private readonly Action<string, Brush, object[]> _setStatusResource;
        private readonly Action _clearSharedStatus;
        private readonly DispatcherTimer _timer;
        private readonly StackPanel _root = new StackPanel();
        private readonly FbxExportList<ModelItem> _list = new FbxExportList<ModelItem>();
        private Autodesk.Navisworks.Api.DocumentParts.DocumentModels _watchedModels;
        private int _modelsGeneration;
        private readonly ListBox _listBox = new ListBox { Height = 140, SelectionMode = SelectionMode.Extended };
        private readonly TextBlock _count = new TextBlock();
        private readonly Button _addSelected;
        private readonly Button _remove;
        private readonly Button _clear;
        private readonly CheckBox _fbx = new CheckBox { IsChecked = true };
        private readonly CheckBox _datasmith = new CheckBox();
        private readonly TextBox _folder = new TextBox();
        private readonly CheckBox _overwrite = new CheckBox();
        private readonly ProgressBar _progress = new ProgressBar { Height = 8, Minimum = 0 };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        private readonly Button _browse;
        private readonly Button _start;
        private readonly Button _cancel;
        private FbxLevelExportJobService _service;
        private string _operationId;
        private string _statusKey;
        private object[] _statusArguments = new object[0];
        private bool _disposed;
        private bool _running;

        internal FbxLevelExportSection(PanelLocalizationBindings bindings, Dispatcher dispatcher,
            Action<string, Brush, object[]> setStatusResource, Action clearSharedStatus)
        {
            _bindings = bindings;
            _setStatusResource = setStatusResource;
            _clearSharedStatus = clearSharedStatus;
            _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += OnTick;

            _bindings.BindContent(_fbx, "Panel_Export_FormatFbx_Label");
            _bindings.BindContent(_datasmith, "Panel_Export_FormatDatasmith_Label");
            var missing = DatasmithPanelExportJob.MissingDependency();
            _datasmith.IsEnabled = missing == null;
            if (missing != null)
            {
                ToolTipService.SetShowOnDisabled(_datasmith, true);
                _bindings.BindAction(this, "ExportList.Dependency", () =>
                    _datasmith.ToolTip = string.Format(CultureInfo.CurrentCulture,
                        _bindings.Text("Panel_Datasmith_Unavailable_Status"), missing));
            }
            var formats = new StackPanel { Orientation = Orientation.Vertical };
            _fbx.Margin = new Thickness(0, 2, 0, 6);
            _datasmith.Margin = new Thickness(0, 0, 0, 2);
            formats.Children.Add(_fbx);
            formats.Children.Add(_datasmith);
            var formatsGroup = new GroupBox
            {
                Content = formats,
                Padding = new Thickness(8, 5, 8, 6),
                Margin = new Thickness(0, 0, 0, UiTheme.Gap * 2),
                FontSize = 11,
                FontWeight = FontWeights.Normal
            };
            _bindings.BindHeader(formatsGroup, "Panel_Export_Formats_Header");
            _root.Children.Add(formatsGroup);
            _root.Children.Add(_listBox);
            _bindings.BindFormattedText(_count, "Panel_Model_FbxListCount_Label", () => new object[] { _list.Count });
            _root.Children.Add(_count);
            _addSelected = Button("Panel_Model_FbxAddSelected_Action", OnAddSelected);
            _remove = Button("Panel_Model_FbxRemove_Action", OnRemove);
            _clear = Button("Panel_Model_FbxClear_Action", OnClear);
            var listActions = new StackPanel { Orientation = Orientation.Horizontal };
            listActions.Children.Add(_addSelected);
            listActions.Children.Add(_remove);
            listActions.Children.Add(_clear);
            _root.Children.Add(listActions);
            _root.Children.Add(Label("Panel_Model_FbxFolder_Label"));

            _browse = Button("CommonBrowse", OnBrowse);
            var folderRow = new DockPanel { Margin = new Thickness(0, 0, 0, UiTheme.Gap) };
            DockPanel.SetDock(_browse, Dock.Right);
            folderRow.Children.Add(_browse);
            folderRow.Children.Add(_folder);
            _root.Children.Add(folderRow);
            _bindings.BindContent(_overwrite, "Panel_Model_FbxOverwrite_Label");
            _root.Children.Add(_overwrite);

            _start = Button("Panel_Model_FbxStart_Action", OnStart);
            _cancel = Button("Panel_Model_FbxCancel_Action", OnCancel);
            _cancel.IsEnabled = false;
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, UiTheme.Gap, 0, UiTheme.Gap)
            };
            actions.Children.Add(_start);
            actions.Children.Add(_cancel);
            _root.Children.Add(actions);
            _root.Children.Add(_progress);
            _status.Foreground = UiTheme.TextSecondary;
            _status.Margin = new Thickness(0, UiTheme.Gap, 0, UiTheme.Gap);
            _root.Children.Add(_status);
            _bindings.BindAction(this, "FbxLevelExport.Status", RefreshStatusText);
        }

        internal UIElement Build() => _root;

        private TextBlock Label(string key)
        {
            var label = new TextBlock
            {
                Foreground = UiTheme.TextSecondary,
                Margin = new Thickness(0, UiTheme.Gap, 0, UiTheme.Gap)
            };
            _bindings.BindText(label, key);
            return label;
        }

        private Button Button(string key, RoutedEventHandler handler)
        {
            var button = UiTheme.ToolButton(_bindings.Text(key), _bindings.Text(key), () => { });
            _bindings.BindContent(button, key);
            _bindings.BindToolTip(button, key);
            button.Click += handler;
            return button;
        }

        private void OnBrowse(object sender, RoutedEventArgs args)
        {
            Execute(() =>
            {
                var folder = FolderPickerDialog.Show(_bindings.Text("Panel_Model_FbxFolder_Prompt"), _folder.Text);
                if (!string.IsNullOrEmpty(folder)) _folder.Text = folder;
            });
        }

        private void OnAddSelected(object sender, RoutedEventArgs args)
        {
            Execute(() =>
            {
                var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (document == null || document.IsClear)
                {
                    ShowStatus("Panel_Model_FbxNoDocument_Status", UiTheme.Warning);
                    return;
                }
                var reset = _list.BindTo(OwnerKey(document));
                var selection = document.CurrentSelection.SelectedItems;
                var added = _list.Add(selection);
                if (selection.Count == 0)
                    ShowStatus("Panel_Model_FbxNothingSelected_Status", UiTheme.Warning);
                // A duplicate-only Add leaves the rows as they are (Codex review P3).
                if (added > 0 || reset)
                {
                    ClearStaleStatus();
                    RefreshList();
                }
                if (reset) ShowStatus("Panel_Model_FbxListReset_Status", UiTheme.Warning);
            });
        }

        private void OnRemove(object sender, RoutedEventArgs args)
        {
            Execute(() =>
            {
                _list.Remove(_listBox.SelectedItems.Cast<ListBoxItem>().Select(row => (ModelItem)row.Tag));
                ClearStaleStatus();
                RefreshList();
            });
        }

        private void OnClear(object sender, RoutedEventArgs args)
        {
            Execute(() => { _list.Clear(); ClearStaleStatus(); RefreshList(); });
        }

        // Navisworks keeps one Document instance when another file is opened, the same file is
        // reopened or a model is swapped, so the list is bound to the document's identity and a
        // generation bumped by every change of its models. Not the file name: Save As renames the
        // document while its items stay valid (Codex reviews of epsilon3).
        private void Watch(Document document)
        {
            if (!ReferenceEquals(_watchedModels, document.Models))
            {
                if (_watchedModels != null)
                    _watchedModels.CollectionChanged -= OnModelsChanged;
                _watchedModels = document.Models;
                _watchedModels.CollectionChanged += OnModelsChanged;
            }
        }

        private void OnModelsChanged(object sender, EventArgs args)
        {
            _modelsGeneration++;
            // Drop the old ModelItems at once: Remove and the row text must never read them after
            // their model was unloaded (third Codex review, P2).
            if (_disposed || _list.Count == 0)
                return;
            _list.Clear();
            ClearStaleStatus();
            RefreshList();
            ShowStatus("Panel_Model_FbxListReset_Status", UiTheme.Warning);
        }

        private string OwnerKey(Document document)
        {
            Watch(document);
            return RuntimeHelpers.GetHashCode(document).ToString(CultureInfo.InvariantCulture)
                + "|" + _modelsGeneration.ToString(CultureInfo.InvariantCulture);
        }

        private void RefreshList()
        {
            _listBox.Items.Clear();
            foreach (var item in _list.Items)
            {
                var text = string.IsNullOrEmpty(item.DisplayName) ? item.ClassDisplayName : item.DisplayName;
                if (item.Parent != null) text += " — " + item.Parent.DisplayName;
                _listBox.Items.Add(new ListBoxItem { Content = text, ToolTip = text, Tag = item });
            }
            _bindings.Refresh();
        }

        private void OnStart(object sender, RoutedEventArgs args)
        {
            _progress.Value = 0;
            Execute(() =>
            {
                var formats = ItemExportFormats.None;
                if (_fbx.IsChecked == true) formats |= ItemExportFormats.Fbx;
                if (_datasmith.IsEnabled && _datasmith.IsChecked == true) formats |= ItemExportFormats.Datasmith;
                if (formats == ItemExportFormats.None)
                {
                    ShowStatus("Panel_Export_NoFormat_Status", UiTheme.Warning);
                    return;
                }
                _service = AgentRuntime.FbxLevelExportJobs;
                if (_service == null)
                {
                    ShowStatus("Panel_Model_FbxUnavailable_Status", UiTheme.Warning);
                    return;
                }
                var document = Autodesk.Navisworks.Api.Application.ActiveDocument;
                if (document == null || document.IsClear)
                {
                    ShowStatus("Panel_Model_FbxNoDocument_Status", UiTheme.Warning);
                    return;
                }
                if (_list.BindTo(OwnerKey(document)))
                {
                    RefreshList();
                    ShowStatus("Panel_Model_FbxListReset_Status", UiTheme.Warning);
                    return;
                }
                if (_list.Count == 0)
                {
                    ShowStatus("Panel_Model_FbxListEmpty_Status", UiTheme.Warning);
                    return;
                }
                var response = _service.Start(document, _list.Items, 0,
                    _folder.Text, _overwrite.IsChecked == true, formats);
                _service.Changed -= OnJobChanged;
                _service.Changed += OnJobChanged;
                Update(response);
            });
        }

        private void OnCancel(object sender, RoutedEventArgs args)
        {
            Execute(() => Update(_service.Cancel(new CancelFbxLevelExportRequest
            {
                OperationId = _operationId
            })));
        }

        private void OnTick(object sender, EventArgs args)
        {
            Execute(() => Update(_service.Status(new FbxLevelExportStatusRequest
            {
                OperationId = _operationId
            })));
        }

        private void OnJobChanged(object sender, EventArgs args)
        {
            Execute(() => Update(_service.Status(new FbxLevelExportStatusRequest
            {
                OperationId = _operationId
            })));
        }

        private void Execute(Action action)
        {
            if (_disposed) return;
            try { action(); }
            catch (AgentCommandException exception) { Fail(exception.Message); }
            catch (Exception exception) { Fail(exception.Message); }
        }

        private void Update(FbxLevelExportStatusResponse response)
        {
            _operationId = response.OperationId;
            var running = response.State == FbxLevelExportJobStates.Running;
            if (!running) _service.Changed -= OnJobChanged;
            SetRunning(running);
            var done = response.ExportedItems + response.SkippedItems;
            _progress.Maximum = Math.Max(1, response.TotalItems);
            _progress.Value = done;
            if (running)
            {
                ShowStatus("Panel_Model_FbxProgress_Status", UiTheme.TextSecondary,
                    done, response.TotalItems, response.CurrentItem);
                _timer.Start();
            }
            else if (response.State == FbxLevelExportJobStates.Completed)
                ShowStatus("Panel_Model_FbxCompleted_Status", UiTheme.Success,
                    response.ExportedItems, response.SkippedItems);
            else if (response.State == FbxLevelExportJobStates.Canceled)
                ShowStatus("Panel_Model_FbxCanceled_Status", UiTheme.Warning, done, response.TotalItems);
            else
                Fail(response.ErrorMessage);
        }

        private void SetRunning(bool running)
        {
            _running = running;
            _start.IsEnabled = !running;
            _cancel.IsEnabled = running;
            if (!running) _timer.Stop();
        }

        private void Fail(string message)
        {
            SetRunning(false);
            ShowStatus("Panel_Model_FbxFailed_Status", UiTheme.Error, message);
        }

        // An edit of the list makes the last job's result and its full progress bar stale (#444
        // L3); a running job keeps its progress line.
        private void ClearStaleStatus()
        {
            if (_running) return;
            _statusKey = null;
            _status.Text = string.Empty;
            _progress.Value = 0;
            _clearSharedStatus?.Invoke();
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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_service != null) _service.Changed -= OnJobChanged;
            _timer.Stop();
            _timer.Tick -= OnTick;
            _browse.Click -= OnBrowse;
            _start.Click -= OnStart;
            _cancel.Click -= OnCancel;
            if (_watchedModels != null) _watchedModels.CollectionChanged -= OnModelsChanged;
            _addSelected.Click -= OnAddSelected;
            _remove.Click -= OnRemove;
            _clear.Click -= OnClear;
            _bindings.UnbindAction(this, "FbxLevelExport.Status");
            _bindings.UnbindAction(this, "ExportList.Dependency");
        }
    }
}
