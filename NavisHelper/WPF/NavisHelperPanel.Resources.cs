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
        private static string _iconsDir;
        private static readonly FontFamily ButtonIconFont = new FontFamily("Segoe MDL2 Assets");
        private static readonly IReadOnlyDictionary<string, string> ButtonIconGlyphs =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "parent", "\uE74A" },
                { "child", "\uE74B" },
                { "sibling", "\uE8AB" },
                { "leaf", "\uE8BE" },
                { "all_under", "\uE8B7" },
                { "selection_invert", "\uE8AB" },
                { "selection_isolate", "\uE71C" },
                { "selection_unhide", "\uE8F4" },
                { "selection_set_prop", "\uE721" },
                { "selection_search_set", "\uE8A5" },
                { "copy_names", "\uE8C8" },
                { "selection_bounds_info", "\uE809" },
                { "filter", "\uE71C" },
                { "selection_save", "\uE74E" },
                { "selection_recall", "\uE72B" },
                { "csv_import", "\uE8B5" },
                { "import_ps", "\uE8B5" },
                { "save_hierarchy", "\uE74E" },
                { "save_nwd2018", "\uE792" },
                { "export_selected_props", "\uE9F9" },
                { "colors_by_name", "\uE790" },
                { "color_by_property", "\uE2B1" },
                { "override_pdms", "\uE90F" },
                { "reset_color_overrides", "\uE777" },
                { "match_color_manual", "\uE790" },
                { "match_color_pick", "\uE7C9" },
                { "match_color_apply", "\uE768" },
                { "export_colors", "\uE72A" },
                { "import_colors", "\uE72B" },
                { "history_select", "\uE721" },
                { "history_apply", "\uE72E" },
                { "ai_color", "\uE99A" },
                { "markup_viewpoint", "\uE91B" },
                { "top_view", "\uE7F8" },
                { "top_view_bbox", "\uE809" },
                { "top_view_hatch", "\uE81E" },
                { "selection_center_dot_marker", "\uE915" },
                { "selection_hatch_marker", "\uE81E" },
                { "selection_bounds_hatch_marker", "\uE809" },
                { "sort_viewpoints", "\uE8CB" },
                { "save_viewpoints", "\uE74E" },
                { "selection_section_show", "\uE7F8" },
                { "selection_section_reset", "\uE777" },
                { "dev_run", "\uE768" }
            };

        /// <summary>Путь к папке icons/ рядом с DLL.</summary>

        private static string IconsDir
        {
            get
            {
                if (_iconsDir == null)
                {
                    var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    _iconsDir = Path.Combine(asmDir, "icons");
                }
                return _iconsDir;
            }
        }

        /// <summary>Загружает PNG из папки icons/. Возвращает null если не найден.</summary>

        private static BitmapImage LoadIcon(string name)
        {
            try
            {
                var filePath = Path.Combine(IconsDir, name + ".png");
                if (!File.Exists(filePath)) return null;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(filePath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelHeight = 20;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { return null; }
        }

        /// <summary>
        /// Creates button content with a monochrome vector glyph that inherits Foreground.
        /// Legacy PNG and emoji paths remain as fallbacks for unknown icon identifiers.
        /// </summary>

        private static object MakeButtonContent(string iconName, string fallbackEmoji, string text)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };

            string glyph;
            if (!string.IsNullOrEmpty(iconName) && ButtonIconGlyphs.TryGetValue(iconName, out glyph))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = glyph,
                    FontFamily = ButtonIconFont,
                    FontSize = 15,
                    Width = 20,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            else
            {
                var icon = string.IsNullOrEmpty(iconName) ? null : LoadIcon(iconName);
                if (icon != null)
                {
                    panel.Children.Add(new Image
                    {
                        Source = icon,
                        Width = 20,
                        Height = 20,
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }
                else if (!string.IsNullOrEmpty(fallbackEmoji))
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = fallbackEmoji,
                        FontSize = 14,
                        Margin = new Thickness(0, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }
            }

            panel.Children.Add(new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            });

            return panel;
        }

        private static TabItem WrapInTab(string header, UIElement content)
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content
            };
            return new TabItem { Header = header, Content = scroll };
        }

        private static TextBlock CreateGroupHeader(string text)
        {
            return new TextBlock
            {
                Text = text, FontSize = UiTheme.FontHeader, FontWeight = FontWeights.SemiBold,
                Foreground = UiTheme.TextPrimary,
                Margin = new Thickness(0, 6, 0, 6)
            };
        }

        private static Border CreateSeparator()
        {
            return new Border
            {
                Height = 1,
                Background = UiTheme.Border,
                Margin = new Thickness(0, 8, 0, 8)
            };
        }

        /// <summary>Кнопка с иконкой для вызова плагина. iconName — имя файла без .png.</summary>

        private Button Btn(string iconName, string fallbackEmoji, string text, string tooltip, string pluginId, ButtonKind kind = ButtonKind.Neutral, bool requiresSelection = false)
        {
            var btn = new Button
            {
                Content = MakeButtonContent(iconName, fallbackEmoji, text),
                ToolTip = tooltip,
                Height = 32,
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(8, 0, 8, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontSize = 12, Cursor = Cursors.Hand, Tag = pluginId,
                Style = UiTheme.ButtonStyle(kind)
            };
            btn.Click += OnButtonClick;
            if (requiresSelection) _selectionGating.Register(btn);
            return btn;
        }

        /// <summary>Кнопка навигации.</summary>

        private Button NavBtn(string iconName, string fallbackEmoji, string text, string hotkey, string tooltip, Action action, ButtonKind kind = ButtonKind.Neutral, bool requiresSelection = false)
        {
            var label = hotkey != null ? $"{text}  ({hotkey})" : text;
            var btn = new Button
            {
                Content = MakeButtonContent(iconName, fallbackEmoji, label),
                ToolTip = tooltip,
                Height = 36, Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(8, 0, 8, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontSize = 13, Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(kind)
            };
            btn.Click += (s, e) => TreeNavigation.SafeExecute(action);
            if (requiresSelection) _selectionGating.Register(btn);
            return btn;
        }

        /// <summary>Кнопка-действие.</summary>

        private Button ActionBtn(string iconName, string fallbackEmoji, string text, string tooltip, Action action, int width = 0, ButtonKind kind = ButtonKind.Neutral, bool requiresSelection = false)
        {
            var btn = new Button
            {
                Content = MakeButtonContent(iconName, fallbackEmoji, text),
                ToolTip = tooltip,
                Height = 34, Margin = new Thickness(0, 2, 4, 2),
                Padding = new Thickness(8, 0, 8, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontSize = 12, Cursor = Cursors.Hand,
                Style = UiTheme.ButtonStyle(kind)
            };
            if (width > 0) btn.Width = width;
            btn.Click += (s, e) =>
            {
                try { action(); }
                catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message, "NavisHelper", MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            if (requiresSelection) _selectionGating.Register(btn);
            return btn;
        }

        private static void ExecutePlugin(string pluginId)
        {
            try
            {
                if (PluginCommandExecutor.TryExecuteDirect(pluginId)) return;
                Autodesk.Navisworks.Api.Application.Plugins.ExecuteAddInPlugin(pluginId);
            }
            catch (Exception ex) { MessageBox.Show("Ошибка: " + ex.Message, "NavisHelper", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private static void OnButtonClick(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var pluginId = btn.Tag as string;
            if (string.IsNullOrEmpty(pluginId)) return;
            ExecutePlugin(pluginId);
        }
    
    }
}
