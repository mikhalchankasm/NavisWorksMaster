using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Interfaces;
using Application = Autodesk.Navisworks.Api.Application;
using Color = Autodesk.Navisworks.Api.Color;

namespace NavisHelper.Dev
{
    internal static class ClashScriptsCompat
    {
        public static List<ClashTest> GetClashTests(DocumentClash clash)
        {
            var children = clash?.TestsData?.Value?.TestsRoot?.Children;
            if (children == null)
                return new List<ClashTest>();

            return children.Cast<SavedItem>().OfType<ClashTest>().ToList();
        }
    }

    /// <summary>
    /// Показывает список тестов коллизий с количеством результатов по статусам.
    /// </summary>
    public class ClashTestListScript : IDevScript
    {
        public string Name => "Список тестов коллизий";

        public void Run()
        {
            var doc = Application.ActiveDocument;
            var clash = doc.GetClash();
            var tests = ClashScriptsCompat.GetClashTests(clash);

            if (tests.Count == 0)
            {
                MessageBox.Show("Нет тестов коллизий в документе.", Name);
                return;
            }

            var lines = new List<string>();
            foreach (var test in tests)
            {
                int total = 0, newCount = 0, active = 0, reviewed = 0, approved = 0, resolved = 0;
                foreach (var child in test.Children)
                {
                    if (child is ClashResult cr)
                    {
                        total++;
                        switch (cr.Status)
                        {
                            case ClashResultStatus.New: newCount++; break;
                            case ClashResultStatus.Active: active++; break;
                            case ClashResultStatus.Reviewed: reviewed++; break;
                            case ClashResultStatus.Approved: approved++; break;
                            case ClashResultStatus.Resolved: resolved++; break;
                        }
                    }
                    else if (child is ClashResultGroup grp)
                    {
                        total += grp.Children.Count;
                    }
                }

                lines.Add($"{test.DisplayName}");
                lines.Add($"  Всего: {total} | New: {newCount} | Active: {active} | Reviewed: {reviewed} | Approved: {approved} | Resolved: {resolved}");
                lines.Add("");
            }

            MessageBox.Show(string.Join("\n", lines), $"{Name} ({tests.Count} тестов)");
        }
    }

    /// <summary>
    /// Превью одной коллизии: зум + подсветка двух элементов + section box.
    /// Показывает окно со списком коллизий, по клику — превью.
    /// </summary>
    public class ClashPreviewScript : IDevScript
    {
        public string Name => "Превью коллизий";

        public void Run()
        {
            var doc = Application.ActiveDocument;
            var clash = doc.GetClash();
            var tests = ClashScriptsCompat.GetClashTests(clash);

            if (tests.Count == 0)
            {
                MessageBox.Show("Нет тестов коллизий.", Name);
                return;
            }

            var window = new ClashPreviewWindow(doc, tests);
            System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(window);
            window.Show();
        }
    }

    /// <summary>
    /// Сброс: убирает цветовые переопределения и отключает section box.
    /// </summary>
    public class ClashResetViewScript : IDevScript
    {
        public string Name => "Сброс вида (цвета + сечение)";

        public void Run()
        {
            var doc = Application.ActiveDocument;

            // Сбрасываем все цвета/прозрачность
            doc.Models.ResetAllPermanentMaterials();

            // Идемпотентное отключение section
            NavisHelper.Core.SectionBoxHelper.Disable();

            MessageBox.Show("Цвета сброшены, сечение отключено.", Name);
        }

        private static Type FindType(string fullName)
        {
            // Сначала через AppDomain
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = asm.GetType(fullName);
                    if (type != null) return type;
                }
                catch { }
            }

            // Fallback: загружаем DLL из папки Navisworks напрямую
            try
            {
                var navisDir = System.IO.Path.GetDirectoryName(
                    typeof(Application).Assembly.Location);
                foreach (var dll in System.IO.Directory.GetFiles(navisDir, "*.dll"))
                {
                    try
                    {
                        var asm = System.Reflection.Assembly.LoadFrom(dll);
                        var type = asm.GetType(fullName);
                        if (type != null) return type;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }
    }

    /// <summary>
    /// Окно превью коллизий со списком тестов, фильтром по статусам и навигацией.
    /// </summary>
    public class ClashPreviewWindow : Window
    {
        private Document _doc;
        private ListBox _testList;
        private ListBox _clashList;
        private TextBlock _statusText;
        private Slider _transparencySlider;
        private TextBox _offsetBox;
        private CheckBox _chkNew, _chkActive, _chkReviewed, _chkApproved, _chkResolved;
        private CheckBox _chkContextTransparency;

        // Цвета подсветки
        private ComboBox _colorACombo;
        private ComboBox _colorBCombo;

        private static readonly (string Name, byte R, byte G, byte B)[] PresetColors = new[]
        {
            ("Красный",       (byte)255, (byte)50,  (byte)50),
            ("Синий",         (byte)50,  (byte)100, (byte)255),
            ("Зелёный",       (byte)50,  (byte)200, (byte)50),
            ("Оранжевый",     (byte)255, (byte)165, (byte)0),
            ("Жёлтый",        (byte)255, (byte)255, (byte)50),
            ("Фиолетовый",    (byte)180, (byte)50,  (byte)255),
            ("Голубой",       (byte)50,  (byte)200, (byte)255),
            ("Розовый",       (byte)255, (byte)100, (byte)180),
            ("Белый",         (byte)255, (byte)255, (byte)255),
        };

        public ClashPreviewWindow(Document doc, IReadOnlyList<ClashTest> tests)
        {
            _doc = doc;
            Title = "Превью коллизий";
            Width = 500;
            Height = 650;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });  // 0: header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });                   // 1: test list
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(5) });                     // 2: filters
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // 3: clash list
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });  // 4: colors
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });  // 5: transparency
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });  // 6: offset
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Auto) });  // 7: status
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });                    // 8: buttons

            // === Заголовок ===
            var header = new TextBlock
            {
                Text = "Тесты коллизий:",
                FontSize = 13, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 8, 8, 4)
            };
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // === Список тестов ===
            _testList = new ListBox { Margin = new Thickness(8, 0, 8, 0), FontSize = 12 };
            foreach (var test in tests)
            {
                _testList.Items.Add(new ListBoxItem
                {
                    Content = $"{test.DisplayName} ({test.Children.Count})",
                    Tag = test
                });
            }
            _testList.SelectionChanged += OnTestSelected;
            Grid.SetRow(_testList, 1);
            root.Children.Add(_testList);

            // === Фильтры статусов ===
            var filterPanel = new WrapPanel { Margin = new Thickness(8, 4, 8, 4) };
            filterPanel.Children.Add(new TextBlock { Text = "Статусы: ", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            _chkNew = AddFilterCheck(filterPanel, "New", true, Brushes.Red);
            _chkActive = AddFilterCheck(filterPanel, "Active", true, Brushes.OrangeRed);
            _chkReviewed = AddFilterCheck(filterPanel, "Reviewed", true, Brushes.DodgerBlue);
            _chkApproved = AddFilterCheck(filterPanel, "Approved", false, Brushes.Green);
            _chkResolved = AddFilterCheck(filterPanel, "Resolved", false, Brushes.Gray);
            Grid.SetRow(filterPanel, 2);
            root.Children.Add(filterPanel);

            // === Список коллизий ===
            _clashList = new ListBox { Margin = new Thickness(8, 0, 8, 0), FontSize = 11 };
            _clashList.MouseDoubleClick += OnClashDoubleClick;
            Grid.SetRow(_clashList, 3);
            root.Children.Add(_clashList);

            // === Цвета подсветки ===
            var colorsPanel = new StackPanel { Margin = new Thickness(8, 4, 8, 0) };
            var colorsRow = new StackPanel { Orientation = Orientation.Horizontal };
            colorsRow.Children.Add(new TextBlock { Text = "Цвет A:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            _colorACombo = CreateColorCombo(0); // Красный по умолчанию
            colorsRow.Children.Add(_colorACombo);
            colorsRow.Children.Add(new TextBlock { Text = "  Цвет B:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 4, 0) });
            _colorBCombo = CreateColorCombo(1); // Синий по умолчанию
            colorsRow.Children.Add(_colorBCombo);
            colorsPanel.Children.Add(colorsRow);

            // Чекбокс прозрачности контекста
            _chkContextTransparency = new CheckBox
            {
                Content = "Прозрачность контекста (элементы внутри бокса)",
                IsChecked = false,
                Margin = new Thickness(0, 4, 0, 0),
                ToolTip = "Применить прозрачность к соседним элементам внутри section box.\nЭлементы коллизии останутся непрозрачными."
            };
            colorsPanel.Children.Add(_chkContextTransparency);

            Grid.SetRow(colorsPanel, 4);
            root.Children.Add(colorsPanel);

            // === Прозрачность ===
            var transPanel = new DockPanel { Margin = new Thickness(8, 4, 8, 0) };
            transPanel.Children.Add(new TextBlock { Text = "Прозрачность контекста:", VerticalAlignment = VerticalAlignment.Center, Width = 170 });
            var transLabel = new TextBlock { Text = "70%", Width = 35, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(transLabel, Dock.Right);
            transPanel.Children.Add(transLabel);
            _transparencySlider = new Slider { Minimum = 0, Maximum = 100, Value = 70, TickFrequency = 5, IsSnapToTickEnabled = true };
            _transparencySlider.ValueChanged += (s, e) => transLabel.Text = $"{(int)_transparencySlider.Value}%";
            transPanel.Children.Add(_transparencySlider);
            Grid.SetRow(transPanel, 5);
            root.Children.Add(transPanel);

            // === Отступ section box (Slider) ===
            var offsetPanel = new StackPanel { Margin = new Thickness(8, 4, 8, 0) };
            var offsetHeader = new DockPanel();
            offsetHeader.Children.Add(new TextBlock { Text = "Отступ section box:", VerticalAlignment = VerticalAlignment.Center });
            var offsetLabel = new TextBlock { Text = "1000 мм", Width = 70, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(offsetLabel, Dock.Right);
            offsetHeader.Children.Add(offsetLabel);
            offsetPanel.Children.Add(offsetHeader);

            var offsetSlider = new Slider
            {
                Minimum = 0, Maximum = 10000, Value = 1000,
                TickFrequency = 100, IsSnapToTickEnabled = true,
                ToolTip = "Расширение section box от элементов коллизии (мм).\nЧем больше — тем больше контекста видно."
            };
            offsetSlider.ValueChanged += (s, ev) =>
            {
                offsetLabel.Text = $"{(int)offsetSlider.Value} мм";
                _offsetBox.Text = ((int)offsetSlider.Value).ToString();
            };
            offsetPanel.Children.Add(offsetSlider);

            // Скрытый TextBox для совместимости с ShowClashResult
            _offsetBox = new TextBox { Text = "1000", Visibility = Visibility.Collapsed };
            offsetPanel.Children.Add(_offsetBox);

            Grid.SetRow(offsetPanel, 6);
            root.Children.Add(offsetPanel);

            // === Статус ===
            _statusText = new TextBlock
            {
                Text = "Выберите тест коллизий",
                FontSize = 11, Foreground = Brushes.Gray,
                Margin = new Thickness(8, 4, 8, 4), TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(_statusText, 7);
            root.Children.Add(_statusText);

            // === Кнопки ===
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4, 8, 8) };

            var previewBtn = new Button { Content = "Показать коллизию", Width = 140, Height = 30, Margin = new Thickness(0, 0, 4, 0) };
            previewBtn.Click += OnPreviewClash;
            btnPanel.Children.Add(previewBtn);

            var sectionBtn = new Button { Content = "Section On/Off", Width = 105, Height = 30, Margin = new Thickness(0, 0, 4, 0), ToolTip = "Включить/выключить Section Box (toggle)" };
            sectionBtn.Click += (s, ev) => NavisHelper.Core.SectionBoxHelper.Toggle();
            btnPanel.Children.Add(sectionBtn);

            var resetBtn = new Button { Content = "Сброс вида", Width = 85, Height = 30, Margin = new Thickness(0, 0, 4, 0) };
            resetBtn.Click += OnResetView;
            btnPanel.Children.Add(resetBtn);

            var nextBtn = new Button { Content = ">>", Width = 40, Height = 30, ToolTip = "Следующая коллизия (стрелка вправо)" };
            nextBtn.Click += OnNextClash;
            btnPanel.Children.Add(nextBtn);

            Grid.SetRow(btnPanel, 8);
            root.Children.Add(btnPanel);

            Content = root;

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) Close();
                if (e.Key == Key.Right || e.Key == Key.Down) OnNextClash(s, e);
                if (e.Key == Key.Left || e.Key == Key.Up) OnPrevClash(s, e);
                if (e.Key == Key.Enter || e.Key == Key.Space) OnPreviewClash(s, e);
            };
        }

        private ComboBox CreateColorCombo(int defaultIndex)
        {
            var combo = new ComboBox { Width = 120, Height = 24 };
            foreach (var (name, r, g, b) in PresetColors)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new Border
                {
                    Width = 16, Height = 16,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b)),
                    Margin = new Thickness(0, 0, 6, 0),
                    CornerRadius = new CornerRadius(2)
                });
                sp.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
                combo.Items.Add(new ComboBoxItem { Content = sp, Tag = new byte[] { r, g, b } });
            }
            combo.SelectedIndex = defaultIndex;
            return combo;
        }

        private Color GetSelectedColor(ComboBox combo, Color fallback)
        {
            var item = combo.SelectedItem as ComboBoxItem;
            if (item?.Tag is byte[] rgb && rgb.Length == 3)
                return new Color(rgb[0] / 255.0, rgb[1] / 255.0, rgb[2] / 255.0);
            return fallback;
        }

        private CheckBox AddFilterCheck(WrapPanel panel, string text, bool isChecked, Brush color)
        {
            var cb = new CheckBox
            {
                Content = text, IsChecked = isChecked,
                Foreground = color, FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center
            };
            cb.Checked += (s, e) => RefreshClashList();
            cb.Unchecked += (s, e) => RefreshClashList();
            panel.Children.Add(cb);
            return cb;
        }

        private void OnTestSelected(object sender, SelectionChangedEventArgs e)
        {
            RefreshClashList();
        }

        private void RefreshClashList()
        {
            _clashList.Items.Clear();
            var selected = _testList.SelectedItem as ListBoxItem;
            if (selected == null) return;
            var test = selected.Tag as ClashTest;
            if (test == null) return;

            int shown = 0;
            foreach (var child in test.Children)
            {
                if (child is ClashResult cr && ShouldShow(cr.Status))
                {
                    var item = new ListBoxItem
                    {
                        Content = $"[{StatusLabel(cr.Status)}] {cr.DisplayName}",
                        Tag = cr,
                        Foreground = StatusBrush(cr.Status)
                    };
                    _clashList.Items.Add(item);
                    shown++;
                }
                else if (child is ClashResultGroup grp)
                {
                    foreach (var gc in grp.Children)
                    {
                        if (gc is ClashResult gcr && ShouldShow(gcr.Status))
                        {
                            _clashList.Items.Add(new ListBoxItem
                            {
                                Content = $"[{StatusLabel(gcr.Status)}] {grp.DisplayName} / {gcr.DisplayName}",
                                Tag = gcr,
                                Foreground = StatusBrush(gcr.Status)
                            });
                            shown++;
                        }
                    }
                }
            }

            _statusText.Text = $"Показано коллизий: {shown}";
            _statusText.Foreground = Brushes.DarkGreen;
        }

        private bool ShouldShow(ClashResultStatus status)
        {
            switch (status)
            {
                case ClashResultStatus.New: return _chkNew.IsChecked == true;
                case ClashResultStatus.Active: return _chkActive.IsChecked == true;
                case ClashResultStatus.Reviewed: return _chkReviewed.IsChecked == true;
                case ClashResultStatus.Approved: return _chkApproved.IsChecked == true;
                case ClashResultStatus.Resolved: return _chkResolved.IsChecked == true;
                default: return true;
            }
        }

        private static string StatusLabel(ClashResultStatus s)
        {
            switch (s)
            {
                case ClashResultStatus.New: return "NEW";
                case ClashResultStatus.Active: return "ACT";
                case ClashResultStatus.Reviewed: return "REV";
                case ClashResultStatus.Approved: return "APR";
                case ClashResultStatus.Resolved: return "RES";
                default: return s.ToString();
            }
        }

        private static Brush StatusBrush(ClashResultStatus s)
        {
            switch (s)
            {
                case ClashResultStatus.New: return Brushes.Red;
                case ClashResultStatus.Active: return Brushes.OrangeRed;
                case ClashResultStatus.Reviewed: return Brushes.DodgerBlue;
                case ClashResultStatus.Approved: return Brushes.Green;
                case ClashResultStatus.Resolved: return Brushes.Gray;
                default: return Brushes.Black;
            }
        }

        private void OnPreviewClash(object sender, RoutedEventArgs e)
        {
            var selected = _clashList.SelectedItem as ListBoxItem;
            if (selected == null) { _statusText.Text = "Выберите коллизию из списка"; return; }
            var cr = selected.Tag as ClashResult;
            if (cr == null) return;

            ShowClashResult(cr);
        }

        private void OnClashDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            OnPreviewClash(sender, e);
        }

        private void OnNextClash(object sender, RoutedEventArgs e)
        {
            if (_clashList.Items.Count == 0) return;
            var idx = _clashList.SelectedIndex + 1;
            if (idx >= _clashList.Items.Count) idx = 0;
            _clashList.SelectedIndex = idx;
            _clashList.ScrollIntoView(_clashList.SelectedItem);
            OnPreviewClash(sender, e);
        }

        private void OnPrevClash(object sender, RoutedEventArgs e)
        {
            if (_clashList.Items.Count == 0) return;
            var idx = _clashList.SelectedIndex - 1;
            if (idx < 0) idx = _clashList.Items.Count - 1;
            _clashList.SelectedIndex = idx;
            _clashList.ScrollIntoView(_clashList.SelectedItem);
            OnPreviewClash(sender, e);
        }

        // Элементы с override (для точечного сброса)
        private ModelItemCollection _prevColoredItems;
        private ModelItemCollection _prevTransparentItems;
        // Оригинальные значения для восстановления
        private System.Collections.Generic.Dictionary<ModelItem, Color> _origColors;
        private System.Collections.Generic.Dictionary<ModelItem, double> _origTransparency;

        private void ApplyContextTransparency(ModelItemCollection clashItems, BoundingBox3D clashBbox, double offsetUnits)
        {
            double trans = _transparencySlider.Value / 100.0;
            if (trans <= 0) return;

            var center = clashBbox.Center;
            double extra = NavisHelper.Core.SectionBoxHelper.MmToDocUnits(500); // доп. запас
            double padX = Math.Max((clashBbox.Max.X - clashBbox.Min.X) / 2.0, 0.1) + offsetUnits + extra;
            double padY = Math.Max((clashBbox.Max.Y - clashBbox.Min.Y) / 2.0, 0.1) + offsetUnits + extra;
            double padZ = Math.Max((clashBbox.Max.Z - clashBbox.Min.Z) / 2.0, 0.1) + offsetUnits + extra;

            var searchBox = new BoundingBox3D(
                new Point3D(center.X - padX, center.Y - padY, center.Z - padZ),
                new Point3D(center.X + padX, center.Y + padY, center.Z + padZ));

            // Собираем соседей через родителей (быстро, без сканирования всей модели)
            var contextItems = new ModelItemCollection();
            var clashSet = new System.Collections.Generic.HashSet<ModelItem>();
            foreach (var item in clashItems)
                clashSet.Add(item);

            foreach (var item in clashItems)
            {
                // Поднимаемся на 2 уровня вверх и берём потомков
                var ancestor = item.Parent?.Parent ?? item.Parent;
                if (ancestor == null) continue;

                foreach (var desc in ancestor.DescendantsAndSelf)
                {
                    if (clashSet.Contains(desc)) continue;

                    try
                    {
                        var itemBox = desc.BoundingBox();
                        if (itemBox != null &&
                            itemBox.Min.X < searchBox.Max.X && itemBox.Max.X > searchBox.Min.X &&
                            itemBox.Min.Y < searchBox.Max.Y && itemBox.Max.Y > searchBox.Min.Y &&
                            itemBox.Min.Z < searchBox.Max.Z && itemBox.Max.Z > searchBox.Min.Z)
                        {
                            contextItems.Add(desc);
                            if (contextItems.Count > 500) break; // Лимит
                        }
                    }
                    catch { }
                }
                if (contextItems.Count > 500) break;
            }

            if (contextItems.Count > 0)
            {
                // Сохраняем оригинальную прозрачность перед override
                _origTransparency = new System.Collections.Generic.Dictionary<ModelItem, double>();
                foreach (var item in contextItems)
                {
                    try
                    {
                        if (item.HasGeometry)
                            _origTransparency[item] = item.Geometry.PermanentTransparency;
                    }
                    catch { }
                }

                _doc.Models.OverridePermanentTransparency(contextItems, trans);
                _prevTransparentItems = contextItems;
            }
        }

        private void ShowClashResult(ClashResult cr)
        {
            try
            {
                var items1 = cr.Selection1;
                var items2 = cr.Selection2;

                if ((items1 == null || items1.Count == 0) && (items2 == null || items2.Count == 0))
                {
                    _statusText.Text = $"Нет элементов в коллизии: {cr.DisplayName}";
                    _statusText.Foreground = Brushes.Red;
                    return;
                }

                // Снимаем override с предыдущих элементов
                ResetPreviousOverrides();

                // Собираем элементы коллизии
                var allItems = new ModelItemCollection();
                if (items1 != null) allItems.AddRange(items1);
                if (items2 != null) allItems.AddRange(items2);

                // Сохраняем оригинальные цвета перед override
                _origColors = new System.Collections.Generic.Dictionary<ModelItem, Color>();
                foreach (var item in allItems)
                {
                    try
                    {
                        if (item.HasGeometry)
                            _origColors[item] = item.Geometry.PermanentColor;
                    }
                    catch { }
                }

                // Подсветка выбранными цветами
                var colorA = GetSelectedColor(_colorACombo, new Color(1.0, 0.15, 0.15));
                var colorB = GetSelectedColor(_colorBCombo, new Color(0.15, 0.4, 1.0));

                if (items1 != null && items1.Count > 0)
                    _doc.Models.OverridePermanentColor(items1, colorA);
                if (items2 != null && items2.Count > 0)
                    _doc.Models.OverridePermanentColor(items2, colorB);

                // Запоминаем для следующего сброса
                _prevColoredItems = allItems;

                // НЕ выделяем — selection перекрашивает поверх наших цветов
                _doc.CurrentSelection.Clear();

                // Отступ
                double offsetMm = 1000;
                double.TryParse(_offsetBox.Text, out offsetMm);
                double offsetUnits = NavisHelper.Core.SectionBoxHelper.MmToDocUnits(offsetMm);

                // Зум + section box с отступом
                var bbox = allItems.BoundingBox();
                if (bbox != null)
                {
                    var center = bbox.Center;
                    double padX = Math.Max((bbox.Max.X - bbox.Min.X) / 2.0, 0.1) + offsetUnits;
                    double padY = Math.Max((bbox.Max.Y - bbox.Min.Y) / 2.0, 0.1) + offsetUnits;
                    double padZ = Math.Max((bbox.Max.Z - bbox.Min.Z) / 2.0, 0.1) + offsetUnits;

                    var expandedBox = new BoundingBox3D(
                        new Point3D(center.X - padX, center.Y - padY, center.Z - padZ),
                        new Point3D(center.X + padX, center.Y + padY, center.Z + padZ));

                    // Зум камеры
                    var vp = _doc.CurrentViewpoint.CreateCopy();
                    vp.ZoomBox(expandedBox);
                    _doc.CurrentViewpoint.CopyFrom(vp);

                    // Section box напрямую по expandedBox (без FIT_SELECTION)
                    NavisHelper.Core.SectionBoxHelper.SetSectionBox(expandedBox);
                }

                // Прозрачность контекста (элементы внутри бокса, кроме коллизии)
                if (_chkContextTransparency.IsChecked == true && bbox != null)
                {
                    try
                    {
                        ApplyContextTransparency(allItems, bbox, offsetUnits);
                    }
                    catch { }
                }

                var name1 = items1?.Count > 0 ? items1.First().DisplayName : "?";
                var name2 = items2?.Count > 0 ? items2.First().DisplayName : "?";
                _statusText.Text = $"{cr.DisplayName}\n  A: {name1}\n  B: {name2}";
                _statusText.Foreground = Brushes.DarkGreen;
            }
            catch (Exception ex)
            {
                _statusText.Text = $"Ошибка: {ex.Message}";
                _statusText.Foreground = Brushes.Red;
            }
        }

        private void ResetPreviousOverrides()
        {
            // Восстанавливаем оригинальные цвета
            if (_prevColoredItems != null && _prevColoredItems.Count > 0)
            {
                if (_origColors != null)
                {
                    foreach (var item in _prevColoredItems)
                    {
                        try
                        {
                            Color orig;
                            if (_origColors.TryGetValue(item, out orig))
                                _doc.Models.OverridePermanentColor(new ModelItemCollection { item }, orig);
                        }
                        catch { }
                    }
                    _origColors = null;
                }
                _prevColoredItems = null;
            }
            // Восстанавливаем оригинальную прозрачность
            if (_prevTransparentItems != null && _prevTransparentItems.Count > 0)
            {
                if (_origTransparency != null)
                {
                    foreach (var item in _prevTransparentItems)
                    {
                        try
                        {
                            double orig;
                            if (_origTransparency.TryGetValue(item, out orig))
                                _doc.Models.OverridePermanentTransparency(new ModelItemCollection { item }, orig);
                            else
                                _doc.Models.OverridePermanentTransparency(new ModelItemCollection { item }, 0);
                        }
                        catch { }
                    }
                    _origTransparency = null;
                }
                else
                {
                    _doc.Models.OverridePermanentTransparency(_prevTransparentItems, 0);
                }
                _prevTransparentItems = null;
            }
        }

        private void OnResetView(object sender, RoutedEventArgs e)
        {
            try
            {
                ResetPreviousOverrides();
                _doc.CurrentSelection.Clear();

                // Идемпотентное отключение section (не toggle!)
                NavisHelper.Core.SectionBoxHelper.Disable();

                _statusText.Text = "Вид сброшен";
                _statusText.Foreground = Brushes.Gray;
            }
            catch (Exception ex)
            {
                _statusText.Text = $"Ошибка сброса: {ex.Message}";
                _statusText.Foreground = Brushes.Red;
            }
        }

        private List<string> _debugLog = new List<string>();

        /// <summary>
        /// Включает section box и вписывает по выделению. С дебагом.
        /// </summary>
        private void EnableSectionBox()
        {
            _debugLog.Clear();

            try
            {
                var fwType = FindType("Autodesk.Navisworks.Internal.ApiImplementation.LcRmFrameworkInterface");
                var ctxType = FindType("Autodesk.Navisworks.Internal.ApiImplementation.LcUCIPExecutionContext");

                _debugLog.Add($"fwType: {(fwType != null ? "OK" : "NULL")}");
                _debugLog.Add($"ctxType: {(ctxType != null ? "OK" : "NULL")}");
                if (fwType == null || ctxType == null) { ShowDebug(); return; }

                var toolbarValue = Enum.Parse(ctxType, "eTOOLBAR");
                _debugLog.Add($"toolbar: {toolbarValue}");

                // Поиск метода — пробуем разные варианты
                var execMethod = fwType.GetMethod("ExecuteCommand",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static,
                    null,
                    new Type[] { typeof(string), ctxType },
                    null);

                _debugLog.Add($"execMethod (2 args): {(execMethod != null ? "OK" : "NULL")}");

                if (execMethod == null)
                {
                    // Пробуем найти все перегрузки
                    var allMethods = fwType.GetMethods(
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Static);
                    foreach (var m in allMethods)
                    {
                        if (m.Name.Contains("Execute") || m.Name.Contains("Command"))
                        {
                            var pars = m.GetParameters();
                            _debugLog.Add($"  method: {m.Name}({string.Join(", ", pars.Select(p => p.ParameterType.Name))})");
                        }
                    }
                    ShowDebug();
                    return;
                }

                // Проверяем состояние section
                bool isSectionEnabled = false;
                string sectionCheckResult = "not checked";
                try
                {
                    var mainWindow = Application.Gui.MainWindow;
                    if (mainWindow != null)
                    {
                        var ribbonControl = mainWindow.GetType()
                            .GetProperty("RibbonControl")
                            ?.GetValue(mainWindow);
                        if (ribbonControl != null)
                        {
                            var findItem = ribbonControl.GetType().GetMethod("FindItem");
                            if (findItem != null)
                            {
                                dynamic btn = findItem.Invoke(ribbonControl, new object[] { "ID_SECTION_BOX" });
                                if (btn != null)
                                {
                                    isSectionEnabled = btn.IsChecked;
                                    sectionCheckResult = $"IsChecked={isSectionEnabled}";
                                }
                                else sectionCheckResult = "button=NULL";
                            }
                            else sectionCheckResult = "FindItem=NULL";
                        }
                        else sectionCheckResult = "RibbonControl=NULL";
                    }
                    else sectionCheckResult = "MainWindow=NULL";
                }
                catch (Exception ex) { sectionCheckResult = $"error: {ex.Message}"; }

                _debugLog.Add($"section state: {sectionCheckResult}");

                // Выполняем команды
                var commands = new List<string>();

                if (!isSectionEnabled)
                    commands.Add("RoamerGUI_OM_SECTION_MASTER_ENABLE");

                commands.Add("RoamerGUI_OM_SECTION_Mode_Box");
                commands.Add("RoamerGUI_OM_SECTION_FIT_SELECTION");

                foreach (var cmd in commands)
                {
                    try
                    {
                        var result = execMethod.Invoke(null, new object[] { cmd, toolbarValue });
                        _debugLog.Add($"  {cmd} -> OK (ret={result})");
                    }
                    catch (Exception ex)
                    {
                        _debugLog.Add($"  {cmd} -> ERROR: {ex.InnerException?.Message ?? ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _debugLog.Add($"EXCEPTION: {ex.Message}");
            }

            ShowDebug();
        }

        private void ShowDebug()
        {
            if (_debugLog.Count > 0)
            {
                var msg = "--- Section debug ---\n" + string.Join("\n", _debugLog);
                _statusText.Text += "\n" + msg;
                MessageBox.Show(msg, "Section Box Debug", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static Type FindType(string fullName)
        {
            // Сначала через AppDomain
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var type = asm.GetType(fullName);
                    if (type != null) return type;
                }
                catch { }
            }

            // Fallback: загружаем DLL из папки Navisworks напрямую
            try
            {
                var navisDir = System.IO.Path.GetDirectoryName(
                    typeof(Application).Assembly.Location);
                foreach (var dll in System.IO.Directory.GetFiles(navisDir, "*.dll"))
                {
                    try
                    {
                        var asm = System.Reflection.Assembly.LoadFrom(dll);
                        var type = asm.GetType(fullName);
                        if (type != null) return type;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }
    }
}
