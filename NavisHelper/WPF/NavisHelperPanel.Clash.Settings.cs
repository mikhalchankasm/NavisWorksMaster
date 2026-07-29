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
        private void SaveClashViewpoint()
        {
            try
            {
                var doc = NwApplication.ActiveDocument;
                var selectedResults = GetClashResultsFromRow(_clashGrid?.SelectedItem);

                // Получаем имя текущей коллизии
                string vpName = "Clash Preview";
                try
                {
                    dynamic row = _clashGrid.SelectedItem;
                    if (row != null)
                    {
                        string name = row.Name as string;
                        string itemA = row.ItemA as string;
                        string itemB = row.ItemB as string;
                        vpName = $"{name ?? "Clash"} ({itemA} / {itemB})";
                    }
                }
                catch { }

                // Находим или создаём папку для viewpoints коллизий
                var savedVps = doc.SavedViewpoints;
                Autodesk.Navisworks.Api.FolderItem folder = null;

                foreach (Autodesk.Navisworks.Api.SavedItem item in savedVps.RootItem.Children)
                {
                    if (item.IsGroup && item.DisplayName == "NavisHelper Clashes")
                    {
                        folder = item as Autodesk.Navisworks.Api.FolderItem;
                        break;
                    }
                }

                if (folder == null)
                {
                    folder = new Autodesk.Navisworks.Api.FolderItem();
                    folder.DisplayName = "NavisHelper Clashes";
                    savedVps.AddCopy(folder);

                    // Перечитываем чтобы получить реальный объект
                    foreach (Autodesk.Navisworks.Api.SavedItem item in savedVps.RootItem.Children)
                    {
                        if (item.IsGroup && item.DisplayName == "NavisHelper Clashes")
                        {
                            folder = item as Autodesk.Navisworks.Api.FolderItem;
                            break;
                        }
                    }
                }

                if (selectedResults.Count == 0)
                {
                    SetGlobalStatusResource("Panel_Clash_Viewpoint_NoSelectedResult", Brushes.Orange);
                    return;
                }

                SaveClashSettings();
                ApplyCurrentClashPreviewSettings(usePreviewTransparency: false);
                _clashMgr.UseFixedIsoView = true;
                try
                {
                    if (selectedResults.Count > 1)
                        _clashMgr.ShowClashResults(selectedResults, vpName);
                    else
                        _clashMgr.ShowClashResult(selectedResults[0]);
                }
                finally
                {
                    _clashMgr.UseFixedIsoView = false;
                }

                if (!_clashMgr.LastSuccess)
                    throw new UiStatusResourceException(
                        PreviewManagerUiStatusMapper.ForClashPreview(
                            _clashMgr.LastUiOutcome));

                UiStatusResourceDescriptor redlineDebug;
                var drawGroupMarkers = _clashGroupMarkersForViewpoints?.IsChecked == true && selectedResults.Count > 1;
                var centers = GetClashCentersForRedlines(selectedResults, includeFallbackCenter: !drawGroupMarkers);
                if (drawGroupMarkers || centers.Count > 0)
                {
                    var drawn = ApplyClashCenterRedlines(doc, centers, out redlineDebug);
                    if (drawn == 0 && centers.Count == 0)
                        redlineDebug = new UiStatusResourceDescriptor(
                            "Panel_Clash_Viewpoint_NoCenterSuffix");
                }
                else
                {
                    redlineDebug = new UiStatusResourceDescriptor(
                        "Panel_Clash_Viewpoint_NoCenterSuffix");
                }

                var normalizedBaseName = NormalizeSavedItemName(vpName, "Clash Preview");
                var createTwoViewpoints = _clashDualViewpoints?.IsChecked == true;
                vpName = MakeUniqueSavedViewpointName(folder, createTwoViewpoints ? normalizedBaseName + " (1)" : normalizedBaseName);
                SavedViewpointAppearanceHelper.SaveCurrentViewWithAppearanceOverrides(doc, folder, folder.DisplayName, vpName);

                // Чистим redlines после сохранения
                ClearActiveViewRedlines(doc);

                var savedNameArguments = new List<object>
                {
                    UiLocalizedArgument.FromResource(
                        "Panel_Clash_Viewpoint_SavedNameEntry_Format",
                        vpName,
                        redlineDebug == null
                            ? (object)string.Empty
                            : redlineDebug.AsLocalizedArgument())
                };
                if (createTwoViewpoints && _clashMgr.LastExpandedBox != null)
                {
                    ViewpointCameraHelper.ApplyIsoOppositeViewToBox(doc, _clashMgr.LastExpandedBox, _clashMgr.LastClashCenter);
                    SectionBoxHelper.SetSectionBox(_clashMgr.LastExpandedBox);
                    if (drawGroupMarkers || centers.Count > 0)
                        ApplyClashCenterRedlines(doc, centers, out redlineDebug);
                    else
                        redlineDebug = new UiStatusResourceDescriptor(
                            "Panel_Clash_Viewpoint_NoCenterSuffix");

                    var secondName = MakeUniqueSavedViewpointName(folder, normalizedBaseName + " (2)");
                    SavedViewpointAppearanceHelper.SaveCurrentViewWithAppearanceOverrides(doc, folder, folder.DisplayName, secondName);
                    ClearActiveViewRedlines(doc);
                    savedNameArguments.Add(
                        UiLocalizedArgument.FromResource(
                            "Panel_Clash_Viewpoint_SavedNameEntry_Format",
                            secondName,
                            redlineDebug == null
                                ? (object)string.Empty
                                : redlineDebug.AsLocalizedArgument()));
                }

                SetGlobalStatusResource(
                    "Panel_Clash_Viewpoint_SavedNames_Format",
                    Brushes.DarkGreen,
                    UiLocalizedArgument.Join("; ", savedNameArguments));
            }
            catch (UiStatusResourceException ex)
            {
                SetGlobalStatusResource(
                    new UiStatusResourceDescriptor(
                        "Panel_Clash_Viewpoint_Failed_Format",
                        ex.Descriptor.AsLocalizedArgument()),
                    Brushes.Red);
            }
            catch (Exception ex)
            {
                SetGlobalStatusResource("Panel_Clash_Viewpoint_Failed_Format", Brushes.Red, ex.Message);
            }
        }

        private void SaveClashSettings()
        {
            try
            {
                var testGridStar = _clashTestGridRow?.ActualHeight > 0
                    ? _clashTestGridRow.ActualHeight
                    : (_clashTestGridRow?.Height.Value ?? 1);
                var clashAreaStar = _clashListRow?.ActualHeight > 0
                    ? _clashListRow.ActualHeight
                    : (_clashListRow?.Height.Value ?? 2);
                var groupPanelWidth = _clashGroupPanelSavedWidth;

                new ClashSettings
                {
                    ColorAIndex = _clashColorA?.SelectedIndex ?? 0,
                    ColorBIndex = _clashColorB?.SelectedIndex ?? 1,
                    OffsetMm = _clashOffsetSlider?.Value ?? 1000,
                    BoxMode = GetSelectedClashBoxMode(),
                    UseSectionBox = _clashUseSectionBox?.IsChecked == true,
                    UseContextTransparency = _clashContextTrans?.IsChecked == true,
                    UseFullBoxTransparencyForCapture = false,
                    UseRootContextTransparencyForViewpoints = false,
                    UseDualClashViewpoints = _clashDualViewpoints?.IsChecked == true,
                    UseGroupCenterMarkersForViewpoints = _clashGroupMarkersForViewpoints?.IsChecked == true,
                    UseResetViewpointForViewpoints = true,
                    CaptureAppearanceForViewpoints = true,
                    TransparencyPercent = _clashTransSlider?.Value ?? 70,
                    TestGridHeightStar = ClampClashStar(testGridStar, 1),
                    ClashAreaHeightStar = ClampClashStar(clashAreaStar, 2),
                    ClashGroupPanelWidth = Math.Max(280, groupPanelWidth),
                    ClashSettingsExpanded = true, // устарело: Expander заменён оверлеем, ключ сохранён для совместимости ini
                    ClashGroupPanelVisible = _clashGroupPanelVisible
                }.Save();
            }
            catch { }
        }

        private static string GetClashItemName(Autodesk.Navisworks.Api.ModelItemCollection items)
        {
            if (items == null || items.Count == 0)
                return UiLocalizationService.Current.GetString("Panel_Clash_Item_None");
            var item = items.First();
            // DisplayName
            if (!string.IsNullOrWhiteSpace(item.DisplayName)) return item.DisplayName;
            // ClassDisplayName
            if (!string.IsNullOrWhiteSpace(item.ClassDisplayName)) return $"[{item.ClassDisplayName}]";
            // Parent name
            if (item.Parent != null && !string.IsNullOrWhiteSpace(item.Parent.DisplayName))
                return $".../{item.Parent.DisplayName}";
            return UiLocalizationService.Current.Format("Panel_Clash_ItemFallback_Format", items.Count);
        }

        private static string FormatClashDistance(ClashResult result)
        {
            if (result == null)
                return string.Empty;

            try
            {
                var value = result.Distance;
                if (double.IsNaN(value) || double.IsInfinity(value))
                    return string.Empty;

                var units = string.Empty;
                try
                {
                    var doc = NwApplication.ActiveDocument;
                    if (doc != null)
                        units = " " + doc.Units;
                }
                catch
                {
                }

                return value.ToString("0.###", CultureInfo.InvariantCulture) + units;
            }
            catch
            {
                return string.Empty;
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
    }
}
