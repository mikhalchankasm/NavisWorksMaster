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
                Text =
                    "Метки создаются по текущему выделению модели. Таблица хранит именованные группы " +
                    "только в текущей сессии: выберите группу, чтобы восстановить её объекты в модели.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 8),
            };
            root.Children.Add(intro);
            Grid.SetRow(intro, 0);

            var objectsHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            objectsHeader.Children.Add(CreateGroupHeader("Сессионные группы объектов"));
            var objectButtons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            objectButtons.Children.Add(CreateHeightActionButton(
                "Добавить группу",
                "Сохранить текущее выделение модели как одну именованную группу.",
                AddHeightGroupFromSelection, ButtonKind.Neutral, "\U00002795", true));
            objectButtons.Children.Add(CreateHeightActionButton(
                "Удалить группы",
                "Удалить выбранные группы из списка текущей сессии.",
                RemoveSelectedHeightGroups, ButtonKind.Destructive, "\U0001F5D1"));
            objectButtons.Children.Add(CreateHeightActionButton(
                "Очистить",
                "Очистить все сессионные группы.",
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
                ToolTip = "Ctrl/Shift — выбрать несколько групп. Их объекты будут выделены в модели.",
            };
            _heightObjectsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Группа",
                Binding = new Binding("Name"),
                Width = new DataGridLength(190),
            });
            _heightObjectsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Объектов",
                Binding = new Binding("ItemCount"),
                Width = new DataGridLength(80),
            });
            _heightObjectsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Состав",
                Binding = new Binding("Contents"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            });
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
                Text = "Целевой уровень Z, мм:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 5),
                ToolTip = "Глобальная Z документа; используется размерной линией.",
            };
            footer.Children.Add(targetLabel);
            _heightTargetZText = new TextBox
            {
                Text = "0",
                MinWidth = 150,
                Margin = new Thickness(0, 0, 8, 5),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Глобальная Z документа; используется размерной линией.",
            };
            footer.Children.Add(_heightTargetZText);
            Grid.SetColumn(_heightTargetZText, 1);
            Grid.SetColumnSpan(_heightTargetZText, 2);

            var nameLabel = new TextBlock
            {
                Text = "Имя точки обзора:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 5),
            };
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
                "Отметка Z",
                "Создать сохранённую точку обзора с redline-подписями для текущего выделения.",
                () => RunElevationMarkers(false), ButtonKind.Primary, "\U0001F4CF", true));
            runButtons.Children.Add(CreateHeightActionButton(
                "Размерная линия до Z",
                "Создать сохранённую точку обзора с размерными линиями для текущего выделения.",
                () => RunElevationMarkers(true), ButtonKind.Primary, "\U0001F4D0", true));
            runButtons.Children.Add(CreateHeightActionButton(
                "Graphics-метка",
                "Показать временные Graphics.Text2D-метки для текущего выделения, не меняя камеру.",
                ShowHeightGraphicsMarkers, ButtonKind.Neutral, "\U0001F3F7", true));
            runButtons.Children.Add(CreateHeightActionButton(
                "Скрыть Graphics",
                "Убрать временные Graphics-метки.",
                HideHeightGraphicsMarkers, ButtonKind.Neutral, "\U0001F6AB"));
            runButtons.Children.Add(CreateHeightActionButton(
                "Сохранить скрин",
                "Сохранить текущий вид в изображение (Ctrl+Shift+H).",
                SaveHeightScreenshot, ButtonKind.Neutral, "\U0001F4F7"));
            footer.Children.Add(runButtons);
            Grid.SetRow(runButtons, 2);
            Grid.SetColumnSpan(runButtons, 3);

            root.Children.Add(footer);
            Grid.SetRow(footer, 3);

            return new TabItem
            {
                Header = "↕ Высоты Z",
                Content = root,
            };
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

        private Button CreateHeightActionButton(string text, string tooltip, Action action, ButtonKind kind = ButtonKind.Neutral, string emoji = null, bool requiresSelection = false)
        {
            var button = new Button
            {
                Content = MakeButtonContent(null, emoji, text),
                ToolTip = tooltip,
                Height = 28,
                Margin = new Thickness(3, 1, 0, 1),
                Padding = new Thickness(8, 0, 8, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(kind)
            };
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
                SetGlobalStatus("Выберите объекты для новой группы", Brushes.Orange);
                return;
            }
            if (selection.Count > ElevationMarkerPlanHelper.DefaultMaximumItemCount)
            {
                SetGlobalStatus(
                    "В одной группе допускается не более " +
                    ElevationMarkerPlanHelper.DefaultMaximumItemCount +
                    " объектов",
                    Brushes.Orange);
                return;
            }

            EnsureHeightSessionDocument(document);
            var items = new ModelItemCollection();
            items.CopyFrom(selection);
            var names = items.Cast<ModelItem>().Select(HeightItemName).ToList();
            var suggestedName = HeightMarkGroupNameHelper.Suggest(names);
            var groupName = Microsoft.VisualBasic.Interaction.InputBox(
                "Имя группы выделенных объектов:",
                "Добавить группу высот",
                suggestedName).Trim();
            if (string.IsNullOrWhiteSpace(groupName))
            {
                SetGlobalStatus("Добавление группы отменено", Brushes.Gray);
                return;
            }
            if (_heightMarkGroups.Any(existingGroup =>
                    string.Equals(existingGroup.Name, groupName, StringComparison.OrdinalIgnoreCase)))
            {
                SetGlobalStatus("Группа с таким именем уже существует", Brushes.Orange);
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
            SetGlobalStatus(
                "Добавлена группа \"" + groupName + "\": " +
                items.Count.ToString(CultureInfo.CurrentCulture) +
                " объектов",
                Brushes.DarkGreen);
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
            SetGlobalStatus(
                "Удалено групп: " + selected.Count.ToString(CultureInfo.CurrentCulture));
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
            SetGlobalStatus("Сессионные группы высот очищены");
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
                        SetGlobalStatus(
                            "Документ изменился. Сессионные группы очищены.",
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

                SetGlobalStatus(
                    selection.Count > 0
                        ? "В модели выделено объектов из групп: " +
                          selection.Count.ToString(CultureInfo.CurrentCulture) +
                          (selection.Count > ElevationMarkerPlanHelper.DefaultMaximumItemCount
                              ? "; для одной операции меток оставьте не более " +
                                ElevationMarkerPlanHelper.DefaultMaximumItemCount
                              : string.Empty)
                        : "Группы не выбраны",
                    selection.Count > ElevationMarkerPlanHelper.DefaultMaximumItemCount
                        ? Brushes.Orange
                        : selection.Count > 0
                            ? Brushes.DarkGreen
                            : Brushes.Gray);
            }
            catch (Exception ex)
            {
                Logger.Error("Не удалось выбрать группу высот: " + ex, "ElevationMarker");
                SetGlobalStatus("Не удалось выбрать объекты группы: " + ex.Message, Brushes.Red);
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
                SetGlobalStatus("Укажите имя точки обзора", Brushes.Orange);
                return;
            }

            _heightMarksBusy = true;
            SetGlobalBusy(
                true,
                includeDimensionLine
                    ? "Создание размерных линий до Z..."
                    : "Создание отметок Z...");
            try
            {
                var result = ElevationMarkerService.Create(
                    document,
                    selectedItems,
                    targetZ,
                    viewpointName,
                    includeDimensionLine);

                SetGlobalStatus(
                    (includeDimensionLine ? "Создано размерных линий: " : "Создано отметок Z: ") +
                    result.ItemCount.ToString(CultureInfo.CurrentCulture) +
                    "; папка \"" + result.FolderName + "\"" +
                    "; точка обзора \"" + result.ViewpointName + "\"",
                    Brushes.DarkGreen);
                _heightViewpointName.Text = DefaultHeightViewpointName();
            }
            catch (Exception ex)
            {
                Logger.Error("Ошибка высотных меток Z: " + ex, "ElevationMarker");
                SetGlobalStatus("Высотные метки Z: " + ex.Message, Brushes.Red);
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
                SetGlobalStatus(
                    "Показано Graphics-меток: " +
                    ElevationGraphicsMarkerTool.MarkerCount.ToString(CultureInfo.CurrentCulture),
                    Brushes.DarkGreen);
                return;
            }

            SetGlobalStatus(
                ElevationGraphicsMarkerTool.LastError ?? "Не удалось показать Graphics-метки",
                Brushes.Orange);
        }

        private void HideHeightGraphicsMarkers()
        {
            ElevationGraphicsMarkerTool.Hide();
            SetGlobalStatus("Graphics-метки скрыты", Brushes.Gray);
        }

        private void SaveHeightScreenshot()
        {
            if (NwApplication.ActiveDocument?.ActiveView == null)
            {
                SetGlobalStatus("Нет активного окна вида", Brushes.Orange);
                return;
            }

            var defaultName = "Высоты Z " +
                DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture) +
                ".png";
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Сохранить скрин текущего вида",
                FileName = defaultName,
                DefaultExt = ".png",
                Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|Bitmap (*.bmp)|*.bmp",
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
                SetGlobalStatus("Скрин сохранён: " + dialog.FileName, Brushes.DarkGreen);
                return;
            }

            SetGlobalStatus("Не удалось сохранить скрин: " + warning, Brushes.Red);
            MessageBox.Show(
                warning,
                "Сохранение скрина",
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
                SetGlobalStatus("Нет активного документа", Brushes.Orange);
                return false;
            }
            if (selectedItems.Count == 0)
            {
                SetGlobalStatus("Выберите объекты для высотных меток", Brushes.Orange);
                return false;
            }
            if (selectedItems.Count > ElevationMarkerPlanHelper.DefaultMaximumItemCount)
            {
                SetGlobalStatus(
                    "Выбрано больше " +
                    ElevationMarkerPlanHelper.DefaultMaximumItemCount +
                    " объектов",
                    Brushes.Orange);
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

            SetGlobalStatus("Целевой уровень Z должен быть числом в миллиметрах", Brushes.Orange);
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
                return "Нет объектов";

            var visibleNames = names.Take(3).ToList();
            var result = string.Join("; ", visibleNames);
            if (names.Count > visibleNames.Count)
                result += "; +" + (names.Count - visibleNames.Count).ToString(CultureInfo.CurrentCulture);
            return result;
        }

        private static string HeightItemName(ModelItem item)
        {
            if (item == null)
                return "Объект недоступен";
            var name = string.IsNullOrWhiteSpace(item.DisplayName)
                ? item.ClassDisplayName
                : item.DisplayName;
            var parent = item.Parent == null ? null : item.Parent.DisplayName;
            return !string.IsNullOrWhiteSpace(parent) &&
                   !string.Equals(parent, name, StringComparison.OrdinalIgnoreCase)
                ? parent + " / " + name
                : name;
        }

        private static string DefaultHeightViewpointName()
        {
            return ElevationViewpointNamingHelper.BuildDefaultViewpointName(DateTime.Now);
        }
    }
}
