using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.IO.Compression;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.VisualBasic;
using Path = System.IO.Path;
using NavisHelper.Core;
using NavisHelper.Core.Localization;
using NavisHelper.Interfaces;
using NavisHelper.Agent.Services;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api.ComApi;
using Autodesk.Navisworks.Api.Interop;
using WpfColor = System.Windows.Media.Color;
// DevExpress убран — crash при загрузке в Navisworks
// using DevExpress.Xpf.Grid;
// using DevExpress.Xpf.Core;
using NwApplication = Autodesk.Navisworks.Api.Application;
using NwColor = Autodesk.Navisworks.Api.Color;

namespace NavisHelper.WPF
{
    public partial class NavisHelperPanel : UserControl
    {
        private ListBoxItem CreateSchemeListItem(ColorSchemeType scheme)
        {
            var palette = ColorSchemes.GetPalette(scheme);
            var colorDots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
            string[] colors = palette ?? new[] { "200,80,80", "80,200,80", "80,80,200", "200,200,80", "200,80,200" };
            for (int i = 0; i < Math.Min(5, colors.Length); i++)
            {
                var parts = colors[i].Split(',');
                if (parts.Length != 3) continue;
                colorDots.Children.Add(new Ellipse
                {
                    Width = 12, Height = 12,
                    Fill = new SolidColorBrush(WpfColor.FromRgb(byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]))),
                    Margin = new Thickness(1, 0, 1, 0)
                });
            }
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(new TextBlock
            {
                Text = $"{(int)scheme}. {ColorSchemeUiText.GetName(UiLocalizationService.Current, scheme)}",
                VerticalAlignment = VerticalAlignment.Center,
                Width = 150
            });
            content.Children.Add(colorDots);
            return new ListBoxItem { Content = content, Tag = scheme };
        }

        private void OnSchemeSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateColorPreview();

        private void RefreshSchemeListItemTexts()
        {
            if (_schemeListBox == null)
                return;

            foreach (ListBoxItem item in _schemeListBox.Items.OfType<ListBoxItem>())
            {
                var content = item.Content as StackPanel;
                var label = content?.Children.OfType<TextBlock>().FirstOrDefault();
                if (label == null || !(item.Tag is ColorSchemeType))
                    continue;

                var scheme = (ColorSchemeType)item.Tag;
                label.Text =
                    $"{(int)scheme}. {ColorSchemeUiText.GetName(UiLocalizationService.Current, scheme)}";
            }
        }

        private void UpdateColorPreview()
        {
            if (_previewPanel == null || _schemeListBox == null) return;
            _previewPanel.Children.Clear();
            var selected = _schemeListBox.SelectedItem as ListBoxItem;
            if (selected == null) return;
            var scheme = (ColorSchemeType)selected.Tag;
            var palette = ColorSchemes.GetPalette(scheme);
            if (palette == null)
            {
                var rng = new Random(42);
                palette = Enumerable.Range(0, 10).Select(_ => $"{rng.Next(50, 256)},{rng.Next(50, 256)},{rng.Next(50, 256)}").ToArray();
            }
            foreach (var colorStr in palette)
            {
                var parts = colorStr.Split(',');
                if (parts.Length != 3) continue;
                _previewPanel.Children.Add(new Border
                {
                    Width = 28, Height = 28, Margin = new Thickness(1), CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(WpfColor.FromRgb(byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2])))
                });
            }
        }

        private void OnApplyColorScheme(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = _schemeListBox.SelectedItem as ListBoxItem;
                if (selected == null)
                {
                    MessageBox.Show(
                        PanelUi("Panel_Colors_SelectScheme"),
                        PanelUi("Panel_Colors_Ai_Title"));
                    return;
                }
                var scheme = (ColorSchemeType)selected.Tag;
                AIConfig.Instance.SetColorScheme((int)scheme);

                if (_aiResponseLog != null)
                {
                    var model = _modelCombo?.SelectedItem as string ?? AIConfig.Instance.ModelName;
                    var thinking = _thinkingCheck?.IsChecked == true;
                    _aiResponseLog.Text = UiLocalizationService.Current.Format(
                        "Panel_Colors_Ai_Starting_Format",
                        model,
                        thinking
                            ? PanelUi("Panel_Common_Enabled")
                            : PanelUi("Panel_Common_Disabled"),
                        ColorSchemeUiText.GetName(
                            UiLocalizationService.Current,
                            scheme));
                }

                NwApplication.Plugins.ExecuteAddInPlugin("AIColorObjects.CBC");
            }
            catch (Exception ex) { MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Colors_Ai_Title"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        /// <summary>
        /// Обновляет лог ответа AI на панели. Вызывается из AIColorObjects после получения результата.
        /// </summary>

        public void UpdateAIResponseLog(Dictionary<string, string> colors, string rawResponse = null)
        {
            if (_aiResponseLog == null) return;
            _aiResponseLog.Text = FormatAIResult(colors);
            _aiResponseLog.ScrollToEnd();
        }

        /// <summary>
        /// Форматирует результат AI-окраски в читаемую таблицу
        /// </summary>

        private static string FormatAIResult(Dictionary<string, string> colors)
        {
            var sb = new System.Text.StringBuilder();

            if (colors == null || colors.Count == 0)
            {
                sb.AppendLine(UiLocalizationService.Current.GetString(
                    "Panel_Colors_Ai_NoColors"));
                return sb.ToString();
            }

            // Группируем по цвету
            var byColor = new Dictionary<string, List<string>>();
            foreach (var kvp in colors)
            {
                if (!byColor.ContainsKey(kvp.Value))
                    byColor[kvp.Value] = new List<string>();
                byColor[kvp.Value].Add(kvp.Key);
            }

            sb.AppendLine(UiLocalizationService.Current.Format(
                "Panel_Colors_Ai_ResultSummary_Format",
                colors.Count,
                byColor.Count));
            sb.AppendLine(new string('=', 50));
            sb.AppendLine();

            // Таблица в markdown-подобном стиле
            // Определяем ширину колонок
            string groupHeader = UiLocalizationService.Current.GetString(
                "Panel_Colors_Ai_GroupColumn");
            int maxGroupName = groupHeader.Length;
            foreach (var g in byColor)
            {
                var nameLen = g.Value.Count > 1
                    ? UiLocalizationService.Current.Format(
                        "Panel_Colors_Ai_ObjectCount_Format",
                        g.Value.Count).Length
                    : g.Value[0].Length;
                if (nameLen > maxGroupName) maxGroupName = nameLen;
            }
            maxGroupName = Math.Min(maxGroupName, 30);

            sb.AppendLine(
                "#".PadRight(3) + " | " +
                groupHeader.PadRight(maxGroupName) + " | " +
                UiLocalizationService.Current.GetString(
                    "Panel_Colors_Ai_RgbColumn").PadRight(15) + " | " +
                UiLocalizationService.Current.GetString(
                    "Panel_Colors_Ai_CountColumn"));
            sb.AppendLine(new string('-', 3) + "-+-" + new string('-', maxGroupName) + "-+-" + new string('-', 15) + "-+-------");

            int groupNum = 1;
            foreach (var group in byColor)
            {
                var firstName = group.Value[0];
                var groupLabel = group.Value.Count == 1
                    ? firstName
                    : ExtractGroupLabel(group.Value);

                if (groupLabel.Length > maxGroupName)
                    groupLabel = groupLabel.Substring(0, maxGroupName - 2) + "..";

                sb.AppendLine(groupNum.ToString().PadRight(3) + " | " + groupLabel.PadRight(maxGroupName) + " | " + ("RGB(" + group.Key + ")").PadRight(15) + " | " + group.Value.Count);
                groupNum++;
            }

            sb.AppendLine(new string('-', 3) + "-+-" + new string('-', maxGroupName) + "-+-" + new string('-', 15) + "-+-------");
            sb.AppendLine();

            // Детальный список
            sb.AppendLine(UiLocalizationService.Current.GetString(
                "Panel_Colors_Ai_GroupDetails"));
            sb.AppendLine(new string('-', 50));
            groupNum = 1;
            foreach (var group in byColor)
            {
                sb.AppendLine(UiLocalizationService.Current.Format(
                    "Panel_Colors_Ai_GroupDetail_Format",
                    groupNum,
                    group.Key));
                foreach (var name in group.Value)
                    sb.AppendLine($"  {name}");
                sb.AppendLine();
                groupNum++;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Извлекает читаемое имя группы из списка объектов.
        /// Example: two related ventilation-system names are collapsed to a shared label and count.
        /// Example: numbered casing names are collapsed to the casing label and count.
        /// </summary>

        private static string ExtractGroupLabel(List<string> names)
        {
            if (names == null || names.Count == 0) return "";
            if (names.Count == 1) return names[0];

            // Берём первый элемент и извлекаем корень
            var sample = names[0].TrimStart('/');

            // Пробуем найти после _ (для ОВ_ВЕ1.3, АС_Окна)
            var underscoreIdx = sample.LastIndexOf('_');
            var dashParts = sample.Split('-');
            string candidate;

            if (dashParts.Length >= 3)
            {
                // Use the segment after the last dash.
                var lastDash = dashParts[dashParts.Length - 1];
                var uParts = lastDash.Split('_');
                candidate = uParts.Length >= 2 ? uParts[0] + "_" + uParts[1] : lastDash;
            }
            else if (underscoreIdx > 0)
            {
                // Use the segment after the first underscore following the system code.
                var afterPrefix = sample.Substring(sample.IndexOf('_') + 1);
                candidate = afterPrefix;
            }
            else
            {
                candidate = dashParts[dashParts.Length - 1];
            }

            // Извлекаем буквенный корень
            var root = new System.Text.StringBuilder();
            foreach (var ch in candidate)
            {
                if (char.IsLetter(ch))
                    root.Append(ch);
                else
                    break;
            }

            var label = root.Length > 0 ? root.ToString() : candidate;
            return UiLocalizationService.Current.Format(
                "Panel_Colors_Ai_GroupLabel_Format",
                label,
                names.Count);
        }

        private void OnSaveAIResponse(object sender, RoutedEventArgs e)
        {
            try
            {
                var text = _aiResponseLog?.Text;
                if (string.IsNullOrEmpty(text)) { MessageBox.Show(PanelUi("Panel_Colors_Ai_NoDataToSave"), PanelUi("Panel_Colors_Ai_Title")); return; }

                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Title = PanelUi("Panel_Colors_AiResponse_Save_Title"),
                    Filter = PanelUi("Panel_Colors_Ai_ResponseFileFilter"),
                    DefaultExt = ".txt",
                    FileName = $"AI_Colors_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (dlg.ShowDialog() == true)
                {
                    System.IO.File.WriteAllText(dlg.FileName, text, System.Text.Encoding.UTF8);
                    MessageBox.Show(
                        UiLocalizationService.Current.Format("Panel_Common_Saved_Format", dlg.FileName),
                        PanelUi("Panel_Colors_Ai_Title"));
                }
            }
            catch (Exception ex) { MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_SaveFailed_Format", ex.Message), PanelUi("Panel_Colors_Ai_Title"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // ============================================================
        //  Match Color (пипетка)
        // ============================================================

        private void OnPickColor(object sender, RoutedEventArgs e)
        {
            try
            {
                var doc = NwApplication.ActiveDocument;
                if (doc == null) return;

                var sel = doc.CurrentSelection.SelectedItems;
                if (sel == null || sel.Count == 0)
                {
                    MessageBox.Show(PanelUi("Panel_Colors_Match_SelectSource"), PanelUi("Panel_Colors_Match_Title"));
                    return;
                }

                var item = sel.First;
                // Считываем цвет: сначала пробуем ActiveColor из геометрии, потом OriginalColor
                Autodesk.Navisworks.Api.Color nwCol = null;
                if (item.Geometry != null)
                {
                    nwCol = item.Geometry.ActiveColor;
                    if (nwCol == null)
                        nwCol = item.Geometry.OriginalColor;
                }

                if (nwCol == null)
                {
                    MessageBox.Show(PanelUi("Panel_Colors_Match_ReadFailed"), PanelUi("Panel_Colors_Match_Title"));
                    return;
                }

                int r = (int)(nwCol.R * 255.0);
                int g = (int)(nwCol.G * 255.0);
                int b = (int)(nwCol.B * 255.0);
                _matchColorRgb = $"{r},{g},{b}";

                _matchColorSwatch.Background = new SolidColorBrush(WpfColor.FromRgb((byte)r, (byte)g, (byte)b));
                _matchColorText.Text = _matchColorRgb;

                // Считываем прозрачность
                _matchTransparency = item.Geometry.PermanentTransparency;
                if (_matchTransText != null)
                    _matchTransText.Text = ((int)(_matchTransparency * 100)).ToString() + "%";
            }
            catch (Exception ex) { MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Colors_Match_Title"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void OnPickAndApplyColor()
        {
            try
            {
                OnPickColor(null, null);
                OnPasteColor(null, null);
                if (!string.IsNullOrEmpty(_matchColorRgb))
                    SetGlobalStatusResource("Panel_Colors_Match_Applied_Format", Brushes.DarkGreen, _matchColorRgb);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Colors_Match_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnReadManualColor()
        {
            var input = _manualColorBox?.Text;
            try
            {
                var color = ColorParser.ParseColor(input);
                _matchColorRgb = $"{color.R},{color.G},{color.B}";

                if (_matchColorSwatch != null)
                    _matchColorSwatch.Background = new SolidColorBrush(WpfColor.FromRgb(color.R, color.G, color.B));
                if (_matchColorText != null)
                    _matchColorText.Text = _matchColorRgb;
                ApplyManualColorTransparency(input, color);

                SetGlobalStatusResource("Panel_Colors_Match_Read_Format", Brushes.DarkGreen, _matchColorRgb);
            }
            catch (Exception ex)
            {
                SetGlobalStatusResource("Panel_Colors_Match_ParseFailed_Format", Brushes.DarkRed, ex.Message);
            }
        }

        private void ApplyManualColorTransparency(string input, System.Drawing.Color color)
        {
            var trimmed = (input ?? string.Empty).Trim();
            var hex = trimmed.StartsWith("#", StringComparison.Ordinal) ? trimmed.Substring(1) : string.Empty;
            if (hex.Length == 8)
            {
                _matchTransparency = 1.0 - color.A / 255.0;
                if (_matchTransText != null)
                    _matchTransText.Text = ((int)Math.Round(_matchTransparency * 100)).ToString(CultureInfo.InvariantCulture) + "%";
                return;
            }

            _matchTransparency = -1;
            if (_matchTransText != null)
                _matchTransText.Text = "-";
        }

        private void OnColorByProperty()
        {
            try
            {
                var doc = NwApplication.ActiveDocument;
                if (doc == null) return;

                var selection = doc.CurrentSelection.SelectedItems;
                if (selection == null || selection.Count == 0)
                {
                    MessageBox.Show(PanelUi("Panel_Colors_Property_SelectItems"), PanelUi("Panel_Colors_Property_Title"));
                    return;
                }

                var groups = new Dictionary<string, ModelItemCollection>(StringComparer.OrdinalIgnoreCase);
                var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var objectNames = new List<string>();

                foreach (var item in selection)
                {
                    if (item == null) continue;
                    string value = FindPropertyValue(item, PropertyAliases);
                    if (string.IsNullOrWhiteSpace(value))
                        value = PanelUi("Panel_Colors_Property_NoValue");

                    if (!groups.TryGetValue(value, out var group))
                    {
                        group = new ModelItemCollection();
                        groups[value] = group;
                    }
                    group.Add(item);

                    var objectName = string.IsNullOrWhiteSpace(item.DisplayName)
                        ? UiLocalizationService.Current.Format(
                            "Panel_Colors_Property_Unnamed_Format",
                            objectNames.Count + 1)
                        : item.DisplayName;
                    objectNames.Add(objectName);
                }

                int groupIdx = 0;
                foreach (var kv in groups)
                {
                    var color = GenerateColorByIndex(groupIdx++);
                    double r = color.Item1 / 255.0;
                    double g = color.Item2 / 255.0;
                    double b = color.Item3 / 255.0;

                    var nwColor = new NwColor(r, g, b);
                    doc.Models.OverridePermanentColor(kv.Value, nwColor);

                    string rgbText = $"{color.Item1},{color.Item2},{color.Item3}";
                    foreach (var item in kv.Value)
                    {
                        string key = string.IsNullOrWhiteSpace(item.DisplayName)
                            ? UiLocalizationService.Current.Format(
                                "Panel_Colors_Property_Unnamed_Format",
                                colors.Count + 1)
                            : item.DisplayName;
                        while (colors.ContainsKey(key))
                            key = $"{key}_{colors.Count + 1}";
                        colors[key] = rgbText;
                    }
                }

                AddColorHistory(objectNames, colors, CopyModelItems(selection));
                SetGlobalStatusResource("Panel_Colors_Property_Result_Format", Brushes.DarkGreen, groups.Count, objectNames.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Colors_Property_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetAllOverrides()
        {
            try
            {
                var doc = NwApplication.ActiveDocument;
                if (doc == null) return;

                _selMgr.ResetOverrides();
                _selMgr.ResetView();
                _clashMgr.ResetView();
                try { doc.CurrentSelection.Clear(); } catch { }
                try { doc.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All); } catch { }

                SetGlobalStatusResource("Panel_Colors_OverridesReset", Brushes.DarkGreen);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Colors_Overrides_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnSelectByPropertyValue()
        {
            try
            {
                var doc = NwApplication.ActiveDocument;
                if (doc == null) return;

                var selected = doc.CurrentSelection.SelectedItems;
                if (selected == null || selected.Count == 0)
                {
                    MessageBox.Show(PanelUi("Panel_Colors_SelectByProperty_SelectItems"), PanelUi("Panel_Colors_SelectByProperty_Title"));
                    return;
                }

                var sourceValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in selected)
                {
                    var value = FindPropertyValue(item, PropertyAliases);
                    if (!string.IsNullOrWhiteSpace(value))
                        sourceValues.Add(value);
                }

                var suggestion = string.Join(", ", sourceValues.Take(6));
                var input = Microsoft.VisualBasic.Interaction.InputBox(
                    UiLocalizationService.Current.Format(
                        "Panel_Colors_SelectByProperty_Prompt_Format",
                        suggestion),
                    PanelUi("Panel_Colors_SelectByProperty_Title"),
                    sourceValues.FirstOrDefault() ?? string.Empty);

                if (string.IsNullOrWhiteSpace(input))
                    return;

                input = input.Trim();

                var allItems = CollectModelItems(doc);
                var result = new Autodesk.Navisworks.Api.ModelItemCollection();
                foreach (var item in allItems)
                {
                    var value = FindPropertyValue(item, PropertyAliases);
                    if (string.Equals(value, input, StringComparison.OrdinalIgnoreCase))
                        result.Add(item);
                }

                if (result.Count == 0)
                {
                    SetGlobalStatusResource("Panel_Colors_SelectByProperty_NoMatches", Brushes.Orange);
                    MessageBox.Show(PanelUi("Panel_Colors_SelectByProperty_NoMatches"), PanelUi("Panel_Colors_SelectByProperty_Title"));
                    return;
                }

                doc.CurrentSelection.Clear();
                doc.CurrentSelection.CopyFrom(result);
                SetGlobalStatusResource("Panel_Colors_SelectByProperty_Result_Format", Brushes.DarkGreen, result.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Colors_SelectByProperty_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCreateSearchSelectionSet()
        {
            try
            {
                var doc = NwApplication.ActiveDocument;
                if (doc == null || doc.IsClear)
                {
                    MessageBox.Show(
                        PanelUi("Panel_Colors_SearchSet_NoActiveModel"),
                        PanelUi("Panel_Colors_SearchSet_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                if (doc.SelectionSets == null || doc.SelectionSets.RootItem == null)
                {
                    MessageBox.Show(
                        PanelUi("Panel_Colors_SearchSet_Unavailable"),
                        PanelUi("Panel_Colors_SearchSet_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var options = ShowSearchSelectionSetDialog(doc);
                if (options == null)
                    return;

                var search = BuildSearchSelectionSetSearch(options);
                var matchedItems = search.FindAll(doc, false);
                var existingTargetFolder = FindSelectionSetFolder(doc, options.FolderPath);
                var existing = existingTargetFolder == null ? null : FindChildSavedItemByName(existingTargetFolder, options.SetName);
                if (existing != null && !(existing is SelectionSet))
                {
                    MessageBox.Show(
                        PanelUi("Panel_Colors_SearchSet_NameConflict"),
                        PanelUi("Panel_Colors_SearchSet_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (existing != null && !options.OverwriteExisting)
                {
                    MessageBox.Show(
                        PanelUi("Panel_Colors_SearchSet_Existing"),
                        PanelUi("Panel_Colors_SearchSet_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (existing != null)
                {
                    var overwriteResult = MessageBox.Show(
                        UiLocalizationService.Current.Format(
                            "Panel_Colors_SearchSet_Replace_Format",
                            options.SetName),
                        PanelUi("Panel_Colors_SearchSet_Title"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (overwriteResult != MessageBoxResult.Yes)
                        return;
                }

                var targetFolder = FindOrCreateSelectionSetFolder(doc, options.FolderPath, out var createdFolderCount);
                var searchSet = new SelectionSet(search)
                {
                    DisplayName = options.SetName
                };

                if (existing != null)
                {
                    var existingIndex = targetFolder.Children.IndexOf(existing);
                    if (existingIndex < 0)
                        throw new InvalidOperationException(
                            PanelUi("Panel_Colors_SearchSet_ExistingNotFound"));

                    doc.SelectionSets.ReplaceWithCopy(targetFolder, existingIndex, searchSet);
                }
                else
                {
                    doc.SelectionSets.InsertCopy(targetFolder, targetFolder.Children.Count, searchSet);
                }

                if (options.SelectAfterCreate)
                {
                    doc.CurrentSelection.Clear();
                    doc.CurrentSelection.CopyFrom(matchedItems);
                }

                object folderText = string.IsNullOrWhiteSpace(options.FolderPath)
                    ? LocalizedStatusArgument("Panel_Colors_SearchSet_Root")
                    : (object)options.FolderPath;
                SetGlobalStatusResource(
                    existing == null
                        ? "Panel_Colors_SearchSet_Created_Format"
                        : "Panel_Colors_SearchSet_Updated_Format",
                    matchedItems.Count > 0 ? Brushes.DarkGreen : Brushes.Orange,
                    folderText,
                    options.SetName,
                    matchedItems.Count);

                if (createdFolderCount > 0)
                    Logger.Info($"Selection Sets folders created: {createdFolderCount}; Search Set: {options.FolderPath}/{options.SetName}", "NavisHelperPanel");
            }
            catch (Exception ex)
            {
                Logger.Error($"Search Set creation failed: {ex}", "NavisHelperPanel");
                MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Colors_SearchSet_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private NativeSearchSetOptions ShowSearchSelectionSetDialog(Document doc)
        {
            var defaults = BuildDefaultSearchSetOptions(doc);
            var dialog = new Window
            {
                Title = PanelUi("Panel_Colors_SearchSet_Save_Title"),
                Owner = Window.GetWindow(this),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                SizeToContent = SizeToContent.WidthAndHeight
            };

            var root = new StackPanel { Margin = new Thickness(14), Width = 430 };
            var form = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBox folderBox = AddSearchSetTextRow(form, 0, "Panel_Folder", defaults.FolderPath);
            TextBox nameBox = AddSearchSetTextRow(form, 1, "Panel_SetName", defaults.SetName);
            TextBox categoryBox = AddSearchSetTextRow(form, 2, "Panel_Category", defaults.Category);
            TextBox propertyBox = AddSearchSetTextRow(form, 3, "Panel_Property", defaults.Property);

            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var opLabel = new TextBlock { Text = PanelUi("Panel_Operator"), Margin = new Thickness(0, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(opLabel, 4);
            Grid.SetColumn(opLabel, 0);
            form.Children.Add(opLabel);
            var operatorCombo = new ComboBox { Margin = new Thickness(0, 4, 0, 4), Height = 24 };
            operatorCombo.Items.Add(PanelUi("Panel_Contains"));
            operatorCombo.Items.Add(PanelUi("Panel_ExactlyEquals"));
            operatorCombo.Items.Add("Wildcard");
            operatorCombo.Items.Add(PanelUi("Panel_PropertyExists"));
            operatorCombo.SelectedIndex = 0;
            Grid.SetRow(operatorCombo, 4);
            Grid.SetColumn(operatorCombo, 1);
            form.Children.Add(operatorCombo);

            TextBox valueBox = AddSearchSetTextRow(form, 5, "Panel_Value", defaults.Value);

            root.Children.Add(form);

            var overwriteCheck = new CheckBox
            {
                Content = PanelUi("Panel_Colors_SearchSet_Overwrite_ToolTip"),
                IsChecked = false,
                Margin = new Thickness(120, 0, 0, 6)
            };
            root.Children.Add(overwriteCheck);

            var selectCheck = new CheckBox
            {
                Content = PanelUi("Panel_Colors_SearchSet_SelectAfterCreate_ToolTip"),
                IsChecked = true,
                Margin = new Thickness(120, 0, 0, 12)
            };
            root.Children.Add(selectCheck);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okButton = new Button { Content = PanelUi("Panel_Create"), Width = 92, Height = 28, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancelButton = new Button { Content = PanelUi("Panel_Cancel"), Width = 82, Height = 28, IsCancel = true };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);
            root.Children.Add(buttons);

            okButton.Click += (sender, args) =>
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text) ||
                    string.IsNullOrWhiteSpace(categoryBox.Text) ||
                    string.IsNullOrWhiteSpace(propertyBox.Text))
                {
                    MessageBox.Show(
                        dialog,
                        PanelUi("Panel_Colors_SearchSet_RequiredFields"),
                        PanelUi("Panel_Colors_SearchSet_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var selectedOperator = operatorCombo.SelectedIndex;
                if (selectedOperator != 3 && string.IsNullOrWhiteSpace(valueBox.Text))
                {
                    MessageBox.Show(
                        dialog,
                        PanelUi("Panel_Colors_SearchSet_ValueRequired"),
                        PanelUi("Panel_Colors_SearchSet_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                dialog.DialogResult = true;
            };

            dialog.Content = root;

            if (dialog.ShowDialog() != true)
                return null;

            return new NativeSearchSetOptions
            {
                FolderPath = NormalizeSearchSetFolderPath(folderBox.Text),
                SetName = NormalizeSavedItemName(
                    nameBox.Text,
                    PersistedModelNames.SearchSetFallback),
                Category = categoryBox.Text.Trim(),
                Property = propertyBox.Text.Trim(),
                Operator = GetSearchSetOperator(operatorCombo.SelectedIndex),
                Value = valueBox.Text == null ? string.Empty : valueBox.Text.Trim(),
                UseInternalPropertyNames = IsDefaultSearchSetItemNameTarget(categoryBox.Text, propertyBox.Text),
                InternalCategory = defaults.InternalCategory,
                InternalProperty = defaults.InternalProperty,
                OverwriteExisting = overwriteCheck.IsChecked == true,
                SelectAfterCreate = selectCheck.IsChecked == true
            };
        }

        private TextBox AddSearchSetTextRow(
            Grid form,
            int row,
            string labelResourceKey,
            string value)
        {
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var textLabel = new TextBlock
            {
                Text = PanelUi(labelResourceKey),
                Margin = new Thickness(0, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(textLabel, row);
            Grid.SetColumn(textLabel, 0);
            form.Children.Add(textLabel);

            var textBox = new TextBox
            {
                Text = value ?? string.Empty,
                Margin = new Thickness(0, 4, 0, 4),
                Height = 24
            };
            Grid.SetRow(textBox, row);
            Grid.SetColumn(textBox, 1);
            form.Children.Add(textBox);

            return textBox;
        }

        private static NativeSearchSetOptions BuildDefaultSearchSetOptions(Document doc)
        {
            var value = string.Empty;
            try
            {
                var selected = doc?.CurrentSelection?.SelectedItems;
                if (selected != null && selected.Count > 0 && selected.First != null)
                    value = selected.First.DisplayName ?? string.Empty;
            }
            catch { value = string.Empty; }

            var setName = string.IsNullOrWhiteSpace(value)
                ? "Search Set " + DateTime.Now.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture)
                : value;

            return new NativeSearchSetOptions
            {
                FolderPath = "NavisHelper Searches",
                SetName = NormalizeSavedItemName(setName, "Search Set"),
                Category = GetDefaultItemCategoryDisplayName(),
                Property = GetDefaultItemNameDisplayName(),
                Operator = "contains",
                Value = value,
                UseInternalPropertyNames = true,
                InternalCategory = SearchSetItemInternalCategory,
                InternalProperty = SearchSetItemNameInternalProperty
            };
        }

        private static string GetDefaultItemCategoryDisplayName()
        {
            return IsRussianUiCulture() ? "Элемент" : "Item";
        }

        private static string GetDefaultItemNameDisplayName()
        {
            return IsRussianUiCulture() ? "Имя" : "Name";
        }

        private static bool IsRussianUiCulture()
        {
            try
            {
                return string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ru", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsDefaultSearchSetItemNameTarget(string category, string property)
        {
            var normalizedCategory = NormalizeSearchSetComparableText(category);
            var normalizedProperty = NormalizeSearchSetComparableText(property);
            if (normalizedProperty != "name" && normalizedProperty != "имя")
                return false;

            return string.IsNullOrEmpty(normalizedCategory) ||
                normalizedCategory == "item" ||
                normalizedCategory == "элемент";
        }

        private static string NormalizeSearchSetComparableText(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }

        private static string GetSearchSetOperator(int selectedIndex)
        {
            switch (selectedIndex)
            {
                case 1:
                    return "equals";
                case 2:
                    return "wildcard";
                case 3:
                    return "defined";
                default:
                    return "contains";
            }
        }

        private static Search BuildSearchSelectionSetSearch(NativeSearchSetOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            var search = new Search();
            search.Selection.SelectAll();
            search.Locations = SearchLocations.DescendantsAndSelf;

            var baseCondition = options.UseInternalPropertyNames
                ? SearchCondition.HasPropertyByName(options.InternalCategory, options.InternalProperty)
                : SearchCondition.HasPropertyByDisplayName(options.Category, options.Property);
            SearchCondition condition;
            switch ((options.Operator ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "equals":
                    condition = baseCondition
                        .EqualValue(VariantData.FromDisplayString(options.Value ?? string.Empty))
                        .IgnoreStringValueCase();
                    break;
                case "wildcard":
                    condition = baseCondition
                        .DisplayStringWildcard(options.Value ?? string.Empty)
                        .IgnoreStringValueCase();
                    break;
                case "defined":
                    condition = baseCondition;
                    break;
                case "contains":
                default:
                    condition = baseCondition
                        .DisplayStringContains(options.Value ?? string.Empty)
                        .IgnoreStringValueCase();
                    break;
            }

            search.SearchConditions.Add(condition);
            return search;
        }

        private static Autodesk.Navisworks.Api.GroupItem FindSelectionSetFolder(Document doc, string folderPath)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (doc.SelectionSets == null || doc.SelectionSets.RootItem == null)
                throw new InvalidOperationException("Selection Sets are not available.");

            Autodesk.Navisworks.Api.GroupItem currentFolder = doc.SelectionSets.RootItem;
            foreach (var segment in SplitSearchSetFolderPath(folderPath))
            {
                var nextFolder = FindChildGroupByName(currentFolder, segment);
                if (nextFolder == null)
                    return null;
                currentFolder = nextFolder;
            }

            return currentFolder;
        }

        private static Autodesk.Navisworks.Api.GroupItem FindOrCreateSelectionSetFolder(Document doc, string folderPath, out int createdFolderCount)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (doc.SelectionSets == null || doc.SelectionSets.RootItem == null)
                throw new InvalidOperationException("Selection Sets are not available.");

            createdFolderCount = 0;
            Autodesk.Navisworks.Api.GroupItem currentFolder = doc.SelectionSets.RootItem;
            foreach (var segment in SplitSearchSetFolderPath(folderPath))
            {
                var nextFolder = FindChildGroupByName(currentFolder, segment);
                if (nextFolder != null)
                {
                    currentFolder = nextFolder;
                    continue;
                }

                var folder = new FolderItem { DisplayName = segment };
                doc.SelectionSets.InsertCopy(currentFolder, currentFolder.Children.Count, folder);
                nextFolder = FindChildGroupByName(currentFolder, segment);
                if (nextFolder == null)
                    throw new InvalidOperationException(
                        UiLocalizationService.Current.Format(
                            "Panel_Colors_SelectionSetFolderFailed_Format",
                            segment));

                createdFolderCount++;
                currentFolder = nextFolder;
            }

            return currentFolder;
        }

        private static Autodesk.Navisworks.Api.GroupItem FindChildGroupByName(Autodesk.Navisworks.Api.GroupItem parent, string name)
        {
            if (parent == null || string.IsNullOrWhiteSpace(name))
                return null;

            foreach (SavedItem item in parent.Children)
            {
                var group = item as Autodesk.Navisworks.Api.GroupItem;
                if (group != null && string.Equals(group.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                    return group;
            }

            return null;
        }

        private static SavedItem FindChildSavedItemByName(Autodesk.Navisworks.Api.GroupItem parent, string name)
        {
            if (parent == null || string.IsNullOrWhiteSpace(name))
                return null;

            foreach (SavedItem item in parent.Children)
                if (string.Equals(item.DisplayName, name, StringComparison.OrdinalIgnoreCase))
                    return item;

            return null;
        }

        private static string NormalizeSearchSetFolderPath(string folderPath)
        {
            var segments = SplitSearchSetFolderPath(folderPath);
            return segments.Length == 0 ? string.Empty : string.Join("/", segments);
        }

        private static string[] SplitSearchSetFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                return new string[0];

            return folderPath
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => NormalizeSavedItemName(segment, "Folder"))
                .Where(segment => !string.IsNullOrWhiteSpace(segment))
                .ToArray();
        }

        private sealed class NativeSearchSetOptions
        {
            public string FolderPath { get; set; }
            public string SetName { get; set; }
            public string Category { get; set; }
            public string Property { get; set; }
            public string Operator { get; set; }
            public string Value { get; set; }
            public bool UseInternalPropertyNames { get; set; }
            public string InternalCategory { get; set; }
            public string InternalProperty { get; set; }
            public bool OverwriteExisting { get; set; }
            public bool SelectAfterCreate { get; set; }
        }

        private void OnPasteColor(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_matchColorRgb))
                {
                    MessageBox.Show(
                        PanelUi("Panel_Colors_Match_ReadFirst"),
                        PanelUi("Panel_Colors_Match_Title"));
                    return;
                }

                var doc = NwApplication.ActiveDocument;
                if (doc == null) return;

                var sel = doc.CurrentSelection.SelectedItems;
                if (sel == null || sel.Count == 0)
                {
                    MessageBox.Show(
                        PanelUi("Panel_Colors_Match_SelectTargets"),
                        PanelUi("Panel_Colors_Match_Title"));
                    return;
                }

                var parts = _matchColorRgb.Split(',');
                var nwColor = new NwColor(
                    double.Parse(parts[0]) / 255.0,
                    double.Parse(parts[1]) / 255.0,
                    double.Parse(parts[2]) / 255.0
                );

                doc.Models.OverridePermanentColor(sel, nwColor);

                // Применяем прозрачность если чекбокс включён
                if (_matchTransCheck?.IsChecked == true && _matchTransparency >= 0)
                    doc.Models.OverridePermanentTransparency(sel, _matchTransparency);
            }
            catch (Exception ex) { MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Colors_Match_Title"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // ============================================================
        //  История покрасок
        // ============================================================

        /// <summary>
        /// Добавляет запись в историю покрасок
        /// </summary>

        public void AddColorHistory(List<string> objectNames, Dictionary<string, string> colors, Autodesk.Navisworks.Api.ModelItemCollection savedItems = null)
        {
            if (objectNames == null || objectNames.Count == 0 || _historyListBox == null) return;

            var uniqueColors = new HashSet<string>(colors.Values).Count;
            var entry = new ColorHistoryEntry
            {
                ObjectCount = objectNames.Count,
                ColorGroupCount = uniqueColors,
                ObjectNames = new List<string>(objectNames),
                Colors = new Dictionary<string, string>(colors),
                Time = DateTime.Now,
                SavedSelection = savedItems
            };

            _colorHistory.Insert(0, entry);
            if (_colorHistory.Count > 10)
                _colorHistory.RemoveAt(_colorHistory.Count - 1);

            RefreshColorHistoryItems();
        }

        private void RefreshColorHistoryItems()
        {
            if (_historyListBox == null)
                return;

            _historyListBox.Items.Clear();
            if (_colorHistory.Count == 0)
            {
                _historyListBox.Items.Add(new ListBoxItem
                {
                    Content = PanelUi("Panel_NoEntries"),
                    IsEnabled = false,
                    Foreground = Brushes.Gray,
                    FontStyle = FontStyles.Italic
                });
                return;
            }

            foreach (var h in _colorHistory)
            {
                var item = new ListBoxItem
                {
                    Content = UiLocalizationService.Current.Format(
                        "Panel_Colors_HistoryEntry_Format",
                        h.Time,
                        h.ObjectCount,
                        h.ColorGroupCount),
                    Tag = h,
                    FontSize = 11
                };
                _historyListBox.Items.Add(item);
            }
        }

        private void OnSelectFromHistory(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = _historyListBox?.SelectedItem as ListBoxItem;
                if (selected == null) { MessageBox.Show(PanelUi("Panel_Colors_HistorySelectEntry"), PanelUi("Panel_Colors_Ai_Title")); return; }
                var entry = selected.Tag as ColorHistoryEntry;
                if (entry == null) return;

                SelectFromHistory(entry);
            }
            catch (Exception ex) { MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Colors_Ai_Title"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void OnRecolorFromHistory(object sender, RoutedEventArgs e)
        {
            try
            {
                var selected = _historyListBox?.SelectedItem as ListBoxItem;
                if (selected == null) { MessageBox.Show(PanelUi("Panel_Colors_HistorySelectEntry"), PanelUi("Panel_Colors_Ai_Title")); return; }
                var entry = selected.Tag as ColorHistoryEntry;
                if (entry == null) return;

                SelectFromHistory(entry);
                OnApplyColorScheme(sender, e);
            }
            catch (Exception ex) { MessageBox.Show(UiLocalizationService.Current.Format("Panel_Common_Error_Format", ex.Message), PanelUi("Panel_Colors_Ai_Title"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        /// <summary>
        /// Выделяет объекты из записи истории (мгновенно — без поиска по модели)
        /// </summary>

        private static void SelectFromHistory(ColorHistoryEntry entry)
        {
            var doc = NwApplication.ActiveDocument;
            if (doc == null) return;

            if (entry.SavedSelection != null && entry.SavedSelection.Count > 0)
            {
                doc.CurrentSelection.CopyFrom(entry.SavedSelection);
            }
        }

        // ============================================================
        //  Colors tab sections
        // ============================================================

        private StackPanel CreateMatchColorSection()
        {
            var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            stack.Children.Add(CreateGroupHeader("Panel_MatchColor"));

            stack.Children.Add(BindPanelText(new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 3)
            }, "Panel_Colors_ManualInput_Label"));
            var manualRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var readManualButton = ActionBtn("match_color_manual", "\U0001F3A8", "Panel_Read", "Panel_Colors_ManualRead_ToolTip", OnReadManualColor, 82);
            DockPanel.SetDock(readManualButton, Dock.Right);
            manualRow.Children.Add(readManualButton);
            _manualColorBox = new TextBox
            {
                Height = 28,
                FontSize = 11,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "#B11A1A · #80B11A1A · 177,26,26 · RAL 3000",
                Margin = new Thickness(0, 2, 6, 2)
            };
            _manualColorBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    OnReadManualColor();
                    e.Handled = true;
                }
            };
            manualRow.Children.Add(_manualColorBox);
            stack.Children.Add(manualRow);

            var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            actionRow.Children.Add(ActionBtn("match_color_pick", "\U0001F489", "Panel_FromObject", "Panel_Colors_Match_Read_ToolTip", () => OnPickColor(null, null)));
            actionRow.Children.Add(ActionBtn("match_color_apply", "\u25B6", "Panel_Apply", "Panel_Colors_Match_Apply_ToolTip", () => OnPasteColor(null, null), 0, ButtonKind.Primary, true));
            stack.Children.Add(actionRow);

            var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            _matchColorSwatch = new Border
            {
                Width = 34,
                Height = 22,
                Margin = new Thickness(0, 0, 8, 0),
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                Background = Brushes.LightGray,
                CornerRadius = new CornerRadius(2)
            };
            _matchColorText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Width = 140 };
            _panelLocalizationBindings.BindAction(
                _matchColorText,
                "MatchColor.CurrentValue",
                () => _matchColorText.Text = string.IsNullOrWhiteSpace(_matchColorRgb)
                    ? PanelUi("Panel_NotSelected")
                    : _matchColorRgb);
            _matchTransCheck = new CheckBox { IsChecked = true, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _panelLocalizationBindings.BindContent(
                _matchTransCheck,
                "Panel_ApplyTransparency");
            _matchTransText = new TextBlock { Text = "-", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 45, FontSize = 11 };

            colorRow.Children.Add(_matchColorSwatch);
            colorRow.Children.Add(_matchColorText);
            colorRow.Children.Add(_matchTransCheck);
            colorRow.Children.Add(_matchTransText);
            stack.Children.Add(colorRow);

            return stack;
        }

        private StackPanel CreateColorTransferSection()
        {
            var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            stack.Children.Add(CreateGroupHeader("Panel_ColorTransfer"));

            stack.Children.Add(BindPanelText(
                new TextBlock { FontSize = 11, Margin = new Thickness(0, 2, 0, 4) },
                "Panel_Colors_Folder_Label"));
            var pathRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            _folderPathBox = new TextBox
            {
                FontSize = 11,
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _panelLocalizationBindings.BindToolTip(
                _folderPathBox,
                "Panel_Colors_FolderPath_ToolTip");
            var browseBtn = new Button
            {
                Content = "\U0001F4C1",
                Width = 36, Height = 28,
                Margin = new Thickness(4, 0, 0, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(ButtonKind.Neutral)
            };
            _panelLocalizationBindings.BindToolTip(
                browseBtn,
                "Panel_Colors_FolderSelect_Modern");
            browseBtn.Click += OnBrowseFolder;
            DockPanel.SetDock(browseBtn, Dock.Right);
            pathRow.Children.Add(browseBtn);
            pathRow.Children.Add(_folderPathBox);
            stack.Children.Add(pathRow);

            _overwriteCheck = new CheckBox
            {
                IsChecked = true,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _panelLocalizationBindings.BindContent(
                _overwriteCheck,
                "Panel_Colors_Overwrite_Label");
            _panelLocalizationBindings.BindToolTip(
                _overwriteCheck,
                "Panel_Colors_ExportOverwrite_ToolTip");
            stack.Children.Add(_overwriteCheck);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(ActionBtn("export_colors", "\U00002B06", "Panel_ExportColors", "Panel_Colors_Export_ToolTip", () => ExecutePlugin("ExportColors.CBC")));
            actions.Children.Add(ActionBtn("import_colors", "\U00002B07", "Panel_ImportColors", "Panel_Colors_Import_ToolTip", () => ExecutePlugin("ImportColors.CBC"), 0, ButtonKind.Primary));
            stack.Children.Add(actions);

            return stack;
        }

        private StackPanel CreateColorHistorySection()
        {
            var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            stack.Children.Add(CreateGroupHeader("Panel_ColoringHistory"));

            _historyListBox = new ListBox { Height = 160, Margin = new Thickness(0, 0, 0, 6), FontSize = 11 };
            stack.Children.Add(_historyListBox);
            _panelLocalizationBindings.BindAction(
                _historyListBox,
                "Colors.HistoryItems",
                RefreshColorHistoryItems);

            var actions = new StackPanel { Orientation = Orientation.Horizontal };
            actions.Children.Add(ActionBtn("history_select", "\U0001F50D", "Panel_Select", "Panel_Colors_HistorySelect_ToolTip", () => OnSelectFromHistory(null, null)));
            actions.Children.Add(ActionBtn("history_apply", "\U0001F4AA", "Panel_Apply", "Panel_Colors_HistoryApply_ToolTip", () => OnRecolorFromHistory(null, null), 0, ButtonKind.Primary, true));
            stack.Children.Add(actions);

            return stack;
        }

        private void OnBrowseFolder(object sender, RoutedEventArgs e)
        {
            var path = FolderPickerDialog.Show(
                PanelUi("Panel_Colors_FolderSelect_Prompt"),
                _folderPathBox.Text);
            if (path != null)
                _folderPathBox.Text = path;
        }

        private static (byte R, byte G, byte B) GenerateColorByIndex(int index)
        {
            var palette = new (byte, byte, byte)[]
            {
                (237, 28, 36),
                (0, 176, 80),
                (0, 112, 192),
                (255, 192, 0),
                (255, 127, 39),
                (112, 48, 160),
                (0, 176, 240),
                (153, 217, 234),
                (146, 208, 80),
                (255, 105, 180),
                (255, 102, 0),
                (255, 165, 0),
                (128, 0, 128),
                (0, 128, 128),
                (34, 177, 76),
                (63, 72, 204),
                (255, 201, 14),
                (136, 0, 21)
            };

            if (index < 0) index = Math.Abs(index);
            return palette[index % palette.Length];
        }

        private static ModelItemCollection CopyModelItems(ModelItemCollection source)
        {
            var result = new ModelItemCollection();
            if (source == null) return result;
            foreach (var item in source)
                if (item != null)
                    result.Add(item);
            return result;
        }

        private static ModelItemCollection CollectModelItems(Autodesk.Navisworks.Api.Document doc)
        {
            var result = new ModelItemCollection();
            if (doc == null) return result;
            var roots = doc.Models.CreateCollectionFromRootItems();
            var seen = new HashSet<ModelItem>();
            foreach (var root in roots)
                CollectModelItemsRecursive(root, result, seen);
            return result;
        }

        private static void CollectModelItemsRecursive(ModelItem item, ModelItemCollection result, HashSet<ModelItem> seen)
        {
            if (item == null) return;
            if (!seen.Add(item)) return;
            result.Add(item);
            if (item.Children != null)
                foreach (var child in item.Children)
                    CollectModelItemsRecursive(child, result, seen);
        }

        private static string FindPropertyValue(ModelItem item, (string Category, string Name)[] aliases)
        {
            if (item == null) return null;
            if (item.PropertyCategories == null) return null;

            foreach (var alias in aliases)
            {
                string aliasCat = string.IsNullOrWhiteSpace(alias.Category) ? null : alias.Category;
                foreach (var category in item.PropertyCategories)
                {
                    if (category == null) continue;
                    bool categoryMatch = aliasCat == null;
                    if (!categoryMatch)
                    {
                        categoryMatch = string.Equals(category.Name, aliasCat, StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(category.DisplayName, aliasCat, StringComparison.OrdinalIgnoreCase);
                    }
                    if (!categoryMatch) continue;

                    if (category.Properties == null) continue;
                    foreach (var prop in category.Properties)
                    {
                        if (prop == null) continue;
                        if (!string.Equals(prop.Name, alias.Name, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(prop.DisplayName, alias.Name, StringComparison.OrdinalIgnoreCase))
                            continue;

                        string value = null;
                        try { value = prop.Value?.ToDisplayString(); } catch { try { value = prop.Value?.ToString(); } catch { value = null; } }
                        if (string.IsNullOrWhiteSpace(value))
                            continue;
                        return value.Trim();
                    }
                }
            }

            return null;
        }

        private static string BuildItemPath(ModelItem item)
        {
            if (item == null) return string.Empty;
            var parts = new Stack<string>();
            var current = item;
            while (current != null)
            {
                if (!string.IsNullOrWhiteSpace(current.DisplayName))
                    parts.Push(current.DisplayName);
                current = current.Parent;
            }
            return string.Join("\\", parts);
        }

        private static string EscapeCsv(string value)
        {
            if (value == null) return "\"\"";
            var v = value.Replace("\"", "\"\"");
            if (v.Contains(";") || v.Contains("\"") || v.Contains("\n") || v.Contains("\r"))
                return $"\"{v}\"";
            return $"\"{v}\"";
        }

        private static string TryGetClashAssignedTo(ClashResult clash)
        {
            if (clash == null) return string.Empty;
            try
            {
                var property = clash.GetType().GetProperty("AssignedTo");
                if (property == null) return string.Empty;
                return (property.GetValue(clash, null) as string) ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string EncodeXml(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            try
            {
                return System.Security.SecurityElement.Escape(value) ?? string.Empty;
            }
            catch { return value ?? string.Empty; }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "clash";
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
            return clean.Substring(0, Math.Min(80, clean.Length)).Trim();
        }

        // ============================================================
        //  ВКЛАДКА: Данные
        // ============================================================
    }
}
