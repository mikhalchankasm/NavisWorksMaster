using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Services;
using NavisHelper.Core;
using NavisHelper.Core.Localization;
using NwApplication = Autodesk.Navisworks.Api.Application;

namespace NavisHelper.WPF
{
    public partial class NavisHelperPanel
    {
        private readonly ObservableCollection<HeightMarkSessionGroup> _heightMarkGroups =
            new ObservableCollection<HeightMarkSessionGroup>();

        private DataGrid _heightObjectsGrid;
        private TextBox _heightTargetZText;
        private TextBox _heightViewpointName;
        private bool _heightMarksBusy;
        private bool _suppressHeightGroupSelectionSync;
        private Document _heightMarksDocument;
        private List<ModelItem> _heightMarksDocumentRoots;

        private TabItem CreateHeightMarksTab()
        {
            var root = new Grid { Margin = new Thickness(8) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var intro = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 8),
            };
            _panelLocalizationBindings.BindText(
                intro,
                "Panel_HeightMarks_Introduction");
            root.Children.Add(intro);
            Grid.SetRow(intro, 0);

            var objectsHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            objectsHeader.Children.Add(CreateGroupHeader("Panel_HeightMarks_Group_SessionObjects"));
            var objectButtons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            objectButtons.Children.Add(CreateHeightActionButton(
                "Panel_AddGroup",
                "Panel_HeightMarks_AddGroup_ToolTip",
                AddHeightGroupFromSelection, ButtonKind.Neutral, "\U00002795", true));
            objectButtons.Children.Add(CreateHeightActionButton(
                "Panel_DeleteGroups",
                "Panel_HeightMarks_DeleteGroups_ToolTip",
                RemoveSelectedHeightGroups, ButtonKind.Destructive, "\U0001F5D1"));
            objectButtons.Children.Add(CreateHeightActionButton(
                "Panel_Clear",
                "Panel_HeightMarks_ClearGroups_ToolTip",
                ClearHeightGroups, ButtonKind.Destructive, "\U0001F9F9"));
            DockPanel.SetDock(objectButtons, Dock.Right);
            objectsHeader.Children.Add(objectButtons);
            root.Children.Add(objectsHeader);
            Grid.SetRow(objectsHeader, 1);

            _heightObjectsGrid = new DataGrid
            {
                ItemsSource = _heightMarkGroups,
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionMode = DataGridSelectionMode.Extended,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                MinHeight = 190,
            };
            _panelLocalizationBindings.BindToolTip(
                _heightObjectsGrid,
                "Panel_HeightMarks_Grid_ToolTip");
            _panelLocalizationBindings.BindAction(
                _heightObjectsGrid,
                "HeightMarks.GroupContents",
                RefreshHeightGroupLocalizedContents);
            var groupColumn = new DataGridTextColumn
            {
                Binding = new Binding("Name"),
                Width = new DataGridLength(190),
            };
            _panelLocalizationBindings.BindColumnHeader(groupColumn, "Panel_Group");
            _heightObjectsGrid.Columns.Add(groupColumn);
            var itemCountColumn = new DataGridTextColumn
            {
                Binding = new Binding("ItemCount"),
                Width = new DataGridLength(80),
            };
            _panelLocalizationBindings.BindColumnHeader(itemCountColumn, "Panel_Objects");
            _heightObjectsGrid.Columns.Add(itemCountColumn);
            var contentsColumn = new DataGridTextColumn
            {
                Binding = new Binding("Contents"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            };
            _panelLocalizationBindings.BindColumnHeader(contentsColumn, "Panel_Contents");
            _heightObjectsGrid.Columns.Add(contentsColumn);
            _heightObjectsGrid.SelectionChanged += OnHeightGroupSelectionChanged;
            _heightObjectsGrid.PreviewMouseLeftButtonDown += OnHeightGroupPreviewMouseLeftButtonDown;
            root.Children.Add(_heightObjectsGrid);
            Grid.SetRow(_heightObjectsGrid, 2);

            var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            footer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var targetLabel = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 5),
            };
            _panelLocalizationBindings.BindText(targetLabel, "Panel_HeightMarks_TargetZ_Label");
            _panelLocalizationBindings.BindToolTip(
                targetLabel,
                "Panel_HeightMarks_TargetZ_ToolTip");
            footer.Children.Add(targetLabel);
            _heightTargetZText = new TextBox
            {
                Text = "0",
                MinWidth = 150,
                Margin = new Thickness(0, 0, 8, 5),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            _panelLocalizationBindings.BindToolTip(
                _heightTargetZText,
                "Panel_HeightMarks_TargetZ_ToolTip");
            footer.Children.Add(_heightTargetZText);
            Grid.SetColumn(_heightTargetZText, 1);
            Grid.SetColumnSpan(_heightTargetZText, 2);

            var nameLabel = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 5),
            };
            _panelLocalizationBindings.BindText(nameLabel, "Panel_ViewpointName");
            footer.Children.Add(nameLabel);
            Grid.SetRow(nameLabel, 1);

            _heightViewpointName = new TextBox
            {
                Text = DefaultHeightViewpointName(),
                MinWidth = 210,
                Margin = new Thickness(0, 0, 8, 5),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            footer.Children.Add(_heightViewpointName);
            Grid.SetRow(_heightViewpointName, 1);
            Grid.SetColumn(_heightViewpointName, 1);
            Grid.SetColumnSpan(_heightViewpointName, 2);

            var runButtons = new WrapPanel();
            runButtons.Children.Add(CreateHeightActionButton(
                "Panel_ZElevation",
                "Panel_HeightMarks_Elevation_ToolTip",
                () => RunElevationMarkers(false), ButtonKind.Primary, "\U0001F4CF", true));
            runButtons.Children.Add(CreateHeightActionButton(
                "Panel_HeightMarks_DimensionToZ_Action",
                "Panel_HeightMarks_Dimension_ToolTip",
                () => RunElevationMarkers(true), ButtonKind.Primary, "\U0001F4D0", true));
            runButtons.Children.Add(CreateHeightActionButton(
                "Panel_GraphicsMarker",
                "Panel_HeightMarks_Graphics_ToolTip",
                ShowHeightGraphicsMarkers, ButtonKind.Neutral, "\U0001F3F7", true));
            runButtons.Children.Add(CreateHeightActionButton(
                "Panel_HideGraphics",
                "Panel_HeightMarks_HideGraphics_ToolTip",
                HideHeightGraphicsMarkers, ButtonKind.Neutral, "\U0001F6AB"));
            runButtons.Children.Add(CreateHeightActionButton(
                "Panel_SaveScreenshot",
                "Panel_HeightMarks_Screenshot_ToolTip",
                SaveHeightScreenshot, ButtonKind.Neutral, "\U0001F4F7"));
            footer.Children.Add(runButtons);
            Grid.SetRow(runButtons, 2);
            Grid.SetColumnSpan(runButtons, 3);

            root.Children.Add(footer);
            Grid.SetRow(footer, 3);

            var tab = new TabItem
            {
                Content = root,
            };
            _panelLocalizationBindings.BindHeader(tab, "Panel_ZElevations");
            return tab;
        }

        internal bool ShowHeightMarksTab()
        {
            if (_mainTabControl == null || _viewsTab == null)
                return false;

            _mainTabControl.SelectedItem = _viewsTab;
            _selectViewsSegment?.Invoke(1);
            EnsureHeightSessionDocument(NwApplication.ActiveDocument);
            return true;
        }

        private Button CreateHeightActionButton(
            string textResourceKey,
            string tooltipResourceKey,
            Action action,
            ButtonKind kind = ButtonKind.Neutral,
            string emoji = null,
            bool requiresSelection = false)
        {
            var button = new Button
            {
                Content = MakeLocalizedButtonContent(null, emoji, textResourceKey),
                Height = 28,
                Margin = new Thickness(3, 1, 0, 1),
                Padding = new Thickness(8, 0, 8, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(kind)
            };
            _panelLocalizationBindings.BindToolTip(button, tooltipResourceKey);
            button.Click += (sender, args) => action();
            if (requiresSelection) _selectionGating.Register(button);
            return button;
        }

        private void AddHeightGroupFromSelection()
        {
            if (_heightMarksBusy)
                return;

            var document = NwApplication.ActiveDocument;
            var selection = document?.CurrentSelection?.SelectedItems;
            if (document == null || selection == null || selection.Count == 0)
            {
                SetGlobalStatusResource("Panel_HeightMarks_SelectItemsForGroup", Brushes.Orange);
                return;
            }
            if (selection.Count > ElevationMarkerPlanHelper.DefaultMaximumItemCount)
            {
                SetGlobalStatusResource(
                    "Panel_HeightMarks_GroupLimit_Format",
                    Brushes.Orange,
                    ElevationMarkerPlanHelper.DefaultMaximumItemCount);
                return;
            }

            EnsureHeightSessionDocument(document);
            var items = new ModelItemCollection();
            items.CopyFrom(selection);
            var names = items.Cast<ModelItem>().Select(HeightItemName).ToList();
            var suggestedName = HeightMarkGroupNameHelper.Suggest(names);
            var groupName = Microsoft.VisualBasic.Interaction.InputBox(
                PanelUi("Panel_HeightMarks_GroupName_Prompt"),
                PanelUi("Panel_HeightMarks_AddGroup_Title"),
                suggestedName).Trim();
            if (string.IsNullOrWhiteSpace(groupName))
            {
                SetGlobalStatusResource("Panel_HeightMarks_AddGroupCancelled", Brushes.Gray);
                return;
            }
            if (_heightMarkGroups.Any(existingGroup =>
                    string.Equals(existingGroup.Name, groupName, StringComparison.OrdinalIgnoreCase)))
            {
                SetGlobalStatusResource("Panel_HeightMarks_DuplicateGroup", Brushes.Orange);
                return;
            }

            var group = new HeightMarkSessionGroup
            {
                Name = groupName,
                Items = items,
                Contents = BuildHeightGroupContents(names),
            };
            _suppressHeightGroupSelectionSync = true;
            try
            {
                _heightMarkGroups.Add(group);
                _heightObjectsGrid.SelectedItem = group;
                _heightObjectsGrid.ScrollIntoView(group);
            }
            finally
            {
                _suppressHeightGroupSelectionSync = false;
            }

            SelectHeightGroupsInModel(new[] { group });
            SetGlobalStatusResource(
                "Panel_HeightMarks_GroupAdded_Format",
                Brushes.DarkGreen,
                groupName,
                items.Count);
        }

        private void RemoveSelectedHeightGroups()
        {
            if (_heightMarksBusy || _heightObjectsGrid == null)
                return;

            var selected = _heightObjectsGrid.SelectedItems
                .OfType<HeightMarkSessionGroup>()
                .ToList();
            _suppressHeightGroupSelectionSync = true;
            try
            {
                foreach (var group in selected)
                    _heightMarkGroups.Remove(group);
            }
            finally
            {
                _suppressHeightGroupSelectionSync = false;
            }
            SelectHeightGroupsInModel(Enumerable.Empty<HeightMarkSessionGroup>());
            SetGlobalStatusResource(
                "Panel_HeightMarks_GroupsDeleted_Format",
                null,
                selected.Count);
        }

        private void ClearHeightGroups()
        {
            if (_heightMarksBusy)
                return;

            _suppressHeightGroupSelectionSync = true;
            try
            {
                _heightMarkGroups.Clear();
            }
            finally
            {
                _suppressHeightGroupSelectionSync = false;
            }
            SelectHeightGroupsInModel(Enumerable.Empty<HeightMarkSessionGroup>());
            SetGlobalStatusResource("Panel_HeightMarks_GroupsCleared");
        }

        private void OnHeightGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressHeightGroupSelectionSync || _heightObjectsGrid == null)
                return;

            var selectedGroups = _heightObjectsGrid.SelectedItems
                .OfType<HeightMarkSessionGroup>()
                .ToList();
            SelectHeightGroupsInModel(selectedGroups);
        }

        private void OnHeightGroupPreviewMouseLeftButtonDown(
            object sender,
            MouseButtonEventArgs e)
        {
            if (_suppressHeightGroupSelectionSync ||
                _heightObjectsGrid == null ||
                e.ClickCount != 1 ||
                _heightObjectsGrid.SelectedItems.Count != 1)
                return;

            var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row == null || !row.IsSelected)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_heightObjectsGrid.SelectedItems.Count != 1)
                    return;

                var selectedGroup =
                    _heightObjectsGrid.SelectedItem as HeightMarkSessionGroup;
                if (selectedGroup != null)
                    SelectHeightGroupsInModel(new[] { selectedGroup });
            }));
        }

        private void SelectHeightGroupsInModel(IEnumerable<HeightMarkSessionGroup> groups)
        {
            try
            {
                var document = NwApplication.ActiveDocument;
                if (!IsHeightSessionDocument(document))
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        EnsureHeightSessionDocument(NwApplication.ActiveDocument);
                        SetGlobalStatusResource(
                            "Panel_HeightMarks_DocumentChanged",
                            Brushes.Orange);
                    }));
                    return;
                }

                var uniqueItems = new HashSet<ModelItem>();
                var selection = new ModelItemCollection();
                foreach (var group in groups ?? Enumerable.Empty<HeightMarkSessionGroup>())
                {
                    if (group?.Items == null)
                        continue;
                    foreach (ModelItem item in group.Items)
                    {
                        if (item != null && uniqueItems.Add(item))
                            selection.Add(item);
                    }
                }

                if (selection.Count > 0)
                    document.CurrentSelection.CopyFrom(selection);
                else
                    document.CurrentSelection.Clear();

                if (selection.Count == 0)
                {
                    SetGlobalStatusResource(
                        "Panel_HeightMarks_NoGroupsSelected",
                        Brushes.Gray);
                }
                else if (selection.Count > ElevationMarkerPlanHelper.DefaultMaximumItemCount)
                {
                    SetGlobalStatusResource(
                        "Panel_HeightMarks_ModelSelectionOverLimit_Format",
                        Brushes.Orange,
                        selection.Count,
                        ElevationMarkerPlanHelper.DefaultMaximumItemCount);
                }
                else
                {
                    SetGlobalStatusResource(
                        "Panel_HeightMarks_ModelSelection_Format",
                        Brushes.DarkGreen,
                        selection.Count);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Could not select the elevation group: " + ex, "ElevationMarker");
                SetGlobalStatusResource(
                    "Panel_HeightMarks_GroupSelectionFailed_Format",
                    Brushes.Red,
                    ex.Message);
            }
        }

        private void RunElevationMarkers(bool includeDimensionLine)
        {
            if (_heightMarksBusy)
                return;

            Document document;
            List<ModelItem> selectedItems;
            if (!TryGetCurrentHeightSelection(out document, out selectedItems))
                return;

            double targetZ;
            if (!TryReadTargetZ(out targetZ))
                return;
            var viewpointName = _heightViewpointName == null
                ? null
                : (_heightViewpointName.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(viewpointName))
            {
                SetGlobalStatusResource(
                    "Panel_HeightMarks_ViewpointNameRequired",
                    Brushes.Orange);
                return;
            }

            _heightMarksBusy = true;
            SetGlobalStatusResource(
                includeDimensionLine
                    ? "Panel_HeightMarks_CreatingDimensions"
                    : "Panel_HeightMarks_CreatingElevations",
                Brushes.DarkOrange);
            SetGlobalBusy(true);
            try
            {
                var result = ElevationMarkerService.Create(
                    document,
                    selectedItems,
                    targetZ,
                    viewpointName,
                    includeDimensionLine);

                SetGlobalStatusResource(
                    includeDimensionLine
                        ? "Panel_HeightMarks_DimensionsCreated_Format"
                        : "Panel_HeightMarks_ElevationsCreated_Format",
                    Brushes.DarkGreen,
                    result.ItemCount,
                    result.FolderName,
                    result.ViewpointName);
                _heightViewpointName.Text = DefaultHeightViewpointName();
            }
            catch (Exception ex)
            {
                Logger.Error("Z elevation marker failure: " + ex, "ElevationMarker");
                SetGlobalStatusResource(
                    "Panel_HeightMarks_Failed_Format",
                    Brushes.Red,
                    ex.Message);
            }
            finally
            {
                _heightMarksBusy = false;
                SetGlobalBusy(false);
            }
        }

        private void ShowHeightGraphicsMarkers()
        {
            if (ElevationGraphicsMarkerTool.ShowFromCurrentSelection())
            {
                SetGlobalStatusResource(
                    "Panel_HeightMarks_GraphicsShown_Format",
                    Brushes.DarkGreen,
                    ElevationGraphicsMarkerTool.MarkerCount);
                return;
            }

            SetGlobalStatusResource(
                "Panel_HeightMarks_GraphicsFailed_Format",
                Brushes.Orange,
                ElevationGraphicsMarkerTool.LastError ?? string.Empty);
        }

        private void HideHeightGraphicsMarkers()
        {
            ElevationGraphicsMarkerTool.Hide();
            SetGlobalStatusResource("Panel_HeightMarks_GraphicsHidden", Brushes.Gray);
        }

        private void SaveHeightScreenshot()
        {
            if (NwApplication.ActiveDocument?.ActiveView == null)
            {
                SetGlobalStatusResource("Panel_Common_NoActiveView", Brushes.Orange);
                return;
            }

            var defaultName = PanelUi("Panel_HeightMarks_ScreenshotFilePrefix") + " " +
                DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture) +
                ".png";
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = PanelUi("Panel_HeightMarks_SaveScreenshot_Title"),
                FileName = defaultName,
                DefaultExt = ".png",
                Filter = PanelUi("Panel_HeightMarks_ScreenshotFileFilter"),
                AddExtension = true,
                OverwritePrompt = true,
            };

            var modelPath = NwApplication.ActiveDocument.FileName;
            if (!string.IsNullOrWhiteSpace(modelPath))
            {
                var directory = Path.GetDirectoryName(modelPath);
                if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                    dialog.InitialDirectory = directory;
            }

            if (dialog.ShowDialog() != true)
                return;

            string warning;
            if (CurrentViewScreenshotService.TrySave(dialog.FileName, out warning))
            {
                SetGlobalStatusResource(
                    "Panel_HeightMarks_ScreenshotSaved_Format",
                    Brushes.DarkGreen,
                    dialog.FileName);
                return;
            }

            SetGlobalStatusResource(
                "Panel_HeightMarks_ScreenshotFailed_Format",
                Brushes.Red,
                warning);
            MessageBox.Show(
                warning,
                PanelUi("Panel_HeightMarks_SaveScreenshot_Title"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private bool TryGetCurrentHeightSelection(
            out Document document,
            out List<ModelItem> selectedItems)
        {
            document = NwApplication.ActiveDocument;
            selectedItems = document?.CurrentSelection?.SelectedItems?
                .Cast<ModelItem>()
                .Where(item => item != null)
                .ToList() ?? new List<ModelItem>();

            if (document == null)
            {
                SetGlobalStatusResource("Panel_Common_NoActiveDocument", Brushes.Orange);
                return false;
            }
            if (selectedItems.Count == 0)
            {
                SetGlobalStatusResource(
                    "Panel_HeightMarks_SelectItems",
                    Brushes.Orange);
                return false;
            }
            if (selectedItems.Count > ElevationMarkerPlanHelper.DefaultMaximumItemCount)
            {
                SetGlobalStatusResource(
                    "Panel_HeightMarks_SelectionLimit_Format",
                    Brushes.Orange,
                    ElevationMarkerPlanHelper.DefaultMaximumItemCount);
                return false;
            }

            return true;
        }

        private bool TryReadTargetZ(out double targetZ)
        {
            targetZ = 0;
            var text = _heightTargetZText == null
                ? "0"
                : (_heightTargetZText.Text ?? string.Empty).Trim();
            if (double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.CurrentCulture,
                    out targetZ) ||
                double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out targetZ))
            {
                if (!double.IsNaN(targetZ) && !double.IsInfinity(targetZ))
                    return true;
            }

            SetGlobalStatusResource(
                "Panel_HeightMarks_InvalidTargetZ",
                Brushes.Orange);
            return false;
        }

        private void EnsureHeightSessionDocument(Document document)
        {
            if (IsHeightSessionDocument(document))
                return;

            _suppressHeightGroupSelectionSync = true;
            try
            {
                _heightMarkGroups.Clear();
                _heightMarksDocument = document;
                _heightMarksDocumentRoots = CaptureHeightDocumentRoots(document);
            }
            finally
            {
                _suppressHeightGroupSelectionSync = false;
            }
        }

        private bool IsHeightSessionDocument(Document document)
        {
            if (document == null ||
                !ReferenceEquals(_heightMarksDocument, document) ||
                _heightMarksDocumentRoots == null)
                return false;

            try
            {
                var currentRoots = CaptureHeightDocumentRoots(document);
                return currentRoots.Count == _heightMarksDocumentRoots.Count &&
                       _heightMarksDocumentRoots.All(
                           saved => currentRoots.Any(
                               current => ReferenceEquals(saved, current) || Equals(saved, current)));
            }
            catch
            {
                return false;
            }
        }

        private static List<ModelItem> CaptureHeightDocumentRoots(Document document)
        {
            var roots = new List<ModelItem>();
            if (document == null)
                return roots;

            foreach (Model model in document.Models)
            {
                if (model?.RootItem != null)
                    roots.Add(model.RootItem);
            }
            return roots;
        }

        private static string BuildHeightGroupContents(IList<string> names)
        {
            if (names == null || names.Count == 0)
                return UiLocalizationService.Current.GetString("Panel_HeightMarks_NoObjects");

            var visibleNames = names.Take(3).ToList();
            var result = string.Join("; ", visibleNames);
            if (names.Count > visibleNames.Count)
                result += "; +" + (names.Count - visibleNames.Count).ToString(CultureInfo.CurrentCulture);
            return result;
        }

        private static string HeightItemName(ModelItem item)
        {
            if (item == null)
                return UiLocalizationService.Current.GetString("Panel_HeightMarks_ItemUnavailable");
            var name = string.IsNullOrWhiteSpace(item.DisplayName)
                ? item.ClassDisplayName
                : item.DisplayName;
            var parent = item.Parent == null ? null : item.Parent.DisplayName;
            return !string.IsNullOrWhiteSpace(parent) &&
                   !string.Equals(parent, name, StringComparison.OrdinalIgnoreCase)
                ? parent + " / " + name
                : name;
        }

        private void RefreshHeightGroupLocalizedContents()
        {
            foreach (var group in _heightMarkGroups)
            {
                var names = group.Items == null
                    ? new List<string>()
                    : group.Items.Cast<ModelItem>().Select(HeightItemName).ToList();
                group.Contents = BuildHeightGroupContents(names);
            }

            _heightObjectsGrid?.Items.Refresh();
        }

        private static string DefaultHeightViewpointName()
        {
            return ElevationViewpointNamingHelper.BuildDefaultViewpointName(DateTime.Now);
        }
    }
}
