using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;
using NavisHelper.Core;
using Application = Autodesk.Navisworks.Api.Application;
using Color = Autodesk.Navisworks.Api.Color;

namespace NavisHelper
{
    /// <summary>
    /// Управление превью коллизий: подсветка, zoom, section box.
    /// </summary>
    public class ClashPreviewManager
    {
        public const string BoxModePoint = "point";
        public const string BoxModeItems = "items";
        private const int TransparencyBatchSize = 1000;
        private const int MaxPreviewGeometryItemsPerSide = 2000;

        private ModelItemCollection _prevColoredItems;
        private ModelItemCollection _prevTransparentItems;
        private Dictionary<ModelItem, Color> _origColors;
        private Dictionary<ModelItem, double> _origTransparency;
        private Viewpoint _originalViewpoint;
        private Document _pairIsolationDocument;
        private ModelItemCollection _pairIsolationHiddenBranches;
        private ModelItemCollection _pairIsolationRevealedPathItems;

        /// <summary>null = без подсветки.</summary>
        public Color ColorA { get; set; } = new Color(1.0, 0.15, 0.15);
        /// <summary>null = без подсветки.</summary>
        public Color ColorB { get; set; } = new Color(0.15, 0.4, 1.0);
        public double OffsetMm { get; set; } = 1000;
        public string BoxMode { get; set; } = BoxModePoint;
        public double ContextTransparency { get; set; } = 0.7;
        public bool UseContextTransparency { get; set; } = false;
        public bool UseSectionBox { get; set; } = true;
        public bool UseFixedIsoView { get; set; } = false;
        public bool UsePairIsolation { get; set; } = false;

        public string LastStatus { get; private set; } = "";
        public bool LastSuccess { get; private set; } = false;
        public string LastFullBoxTransparencyStatus { get; private set; } = "";
        public int LastPairIsolationHiddenBranchCount { get; private set; }
        public long LastPairIsolationElapsedMilliseconds { get; private set; }
        public string LastPairIsolationStatus { get; private set; } = "";

        /// <summary>Показать коллизию: подсветка + zoom + section box.</summary>
        public void ShowClashResult(ClashResult cr)
        {
            var doc = Application.ActiveDocument;
            if (!ClearPairIsolation(false))
            {
                LastStatus = "Не удалось восстановить предыдущий режим «Только пара»";
                LastSuccess = false;
                return;
            }
            LastExpandedBox = null;
            LastClashCenter = null;

            var items1 = new ModelItemCollection();
            var items2 = new ModelItemCollection();
            AddClashSideItems(items1, cr.Item1, cr.Selection1);
            AddClashSideItems(items2, cr.Item2, cr.Selection2);

            if ((items1 == null || items1.Count == 0) && (items2 == null || items2.Count == 0))
            {
                LastStatus = $"Нет элементов: {cr.DisplayName}";
                LastSuccess = false;
                return;
            }

            // Сброс предыдущих overrides
            ResetOverrides();
            CaptureOriginalViewpointIfNeeded(doc);

            // Собираем элементы
            var allItems = new ModelItemCollection();
            AddUniqueItems(allItems, items1);
            AddUniqueItems(allItems, items2);

            // Сохраняем оригинальные цвета (включая потомков с геометрией)
            _origColors = new Dictionary<ModelItem, Color>();
            foreach (var item in allItems)
            {
                try
                {
                    if (item.HasGeometry)
                        _origColors[item] = item.Geometry.PermanentColor;
                }
                catch { }
            }

            // Подсветка (null = без подсветки)
            if (ColorA != null && items1 != null && items1.Count > 0)
                doc.Models.OverridePermanentColor(items1, ColorA);
            if (ColorB != null && items2 != null && items2.Count > 0)
                doc.Models.OverridePermanentColor(items2, ColorB);

            _prevColoredItems = allItems;
            ApplyPairIsolation(doc, allItems);

            // Не выделяем — selection перекрашивает поверх наших цветов
            doc.CurrentSelection.Clear();

            // Offset
            double offsetUnits = SectionBoxHelper.MmToDocUnits(OffsetMm);

            // Zoom + section box
            var bbox = allItems.BoundingBox();
            BoundingBox3D expandedBox = null;
            var clashCenter = cr.Center ?? (bbox != null ? bbox.Center : null);
            if (bbox != null)
            {
                expandedBox = BuildClashBox(cr, bbox, offsetUnits, BoxMode);

                if (UseFixedIsoView)
                {
                    ViewpointCameraHelper.ApplyIso1ViewToBox(doc, expandedBox, clashCenter);
                }
                else
                {
                    var vp = doc.CurrentViewpoint.CreateCopy();
                    vp.ZoomBox(expandedBox);
                    SetViewpointFocalPoint(vp, clashCenter);
                    doc.CurrentViewpoint.CopyFrom(vp);
                }

                LastExpandedBox = expandedBox;
                if (UseSectionBox)
                    SectionBoxHelper.SetSectionBox(expandedBox);
            }

            // Прозрачность контекста
            if (UseContextTransparency && bbox != null)
            {
                try
                {
                    // Сохраняем оригинальную прозрачность clash items
                    foreach (var item in allItems)
                    {
                        try
                        {
                            if (item.HasGeometry && (_origTransparency == null || !_origTransparency.ContainsKey(item)))
                            {
                                if (_origTransparency == null) _origTransparency = new Dictionary<ModelItem, double>();
                                _origTransparency[item] = item.Geometry.PermanentTransparency;
                            }
                        }
                        catch { }
                    }

                    ApplyContextTransparency(doc, allItems);
                    // Гарантируем что элементы коллизии НЕ прозрачны
                    doc.Models.OverridePermanentTransparency(allItems, 0);

                    // Добавляем clash items в _prevTransparentItems для корректного reset
                    if (_prevTransparentItems == null) _prevTransparentItems = new ModelItemCollection();
                    foreach (var item in allItems)
                        if (!_prevTransparentItems.Contains(item))
                            _prevTransparentItems.Add(item);
                }
                catch { }
            }

            // Точка коллизии — используем центр bbox (гарантированно видимые координаты)
            string markerInfo = "";
            try
            {
                // cr.Center — точка пересечения (Autodesk ClashMarkers sample использует именно её)
                LastClashCenter = clashCenter;
                if (LastClashCenter != null)
                {
                    if (ClashMarkerTool.IsActive)
                    {
                        // Обновляем позицию, сохраняя текущий размер
                        ClashMarkerTool.MarkerPoint = LastClashCenter;
                        try { Application.ActiveDocument.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All); } catch { }
                    }
                    markerInfo = $"\n  Clash point: {LastClashCenter.X:F3}, {LastClashCenter.Y:F3}, {LastClashCenter.Z:F3}";
                }
            }
            catch { }

            var name1 = items1.Count > 0 ? items1.First().DisplayName : "?";
            var name2 = items2.Count > 0 ? items2.First().DisplayName : "?";
            LastStatus = $"{cr.DisplayName}\n  A: {name1}\n  B: {name2}{markerInfo}";
            LastSuccess = true;
        }

        /// <summary>Показать группу коллизий как один user-facing конфликт.</summary>
        public void ShowClashResults(IEnumerable<ClashResult> results, string groupName, ModelItem groupItem = null, bool? groupItemIsA = null)
        {
            if (!ClearPairIsolation(false))
            {
                LastStatus = "Не удалось восстановить предыдущий режим «Только пара»";
                LastSuccess = false;
                return;
            }
            LastExpandedBox = null;
            LastClashCenter = null;

            var list = results == null ? new List<ClashResult>() : results.Where(result => result != null).ToList();
            if (list.Count == 0)
            {
                LastStatus = "Нет коллизий в группе";
                LastSuccess = false;
                return;
            }

            if (list.Count == 1)
            {
                ShowClashResult(list[0]);
                return;
            }

            var doc = Application.ActiveDocument;
            var items1 = new ModelItemCollection();
            var items2 = new ModelItemCollection();

            if (groupItem != null && groupItemIsA.HasValue)
            {
                if (groupItemIsA.Value)
                {
                    AddGeometryItems(items1, groupItem);
                    foreach (var result in list)
                        AddClashSideItems(items2, result.Item2, result.Selection2);
                }
                else
                {
                    foreach (var result in list)
                        AddClashSideItems(items1, result.Item1, result.Selection1);
                    AddGeometryItems(items2, groupItem);
                }
            }
            else
            {
                foreach (var result in list)
                {
                    AddClashSideItems(items1, result.Item1, result.Selection1);
                    AddClashSideItems(items2, result.Item2, result.Selection2);
                }
            }

            if (items1.Count == 0 && items2.Count == 0)
            {
                LastStatus = $"Нет элементов: {groupName}";
                LastSuccess = false;
                return;
            }

            ResetOverrides();
            CaptureOriginalViewpointIfNeeded(doc);

            var allItems = new ModelItemCollection();
            AddUniqueItems(allItems, items1);
            AddUniqueItems(allItems, items2);

            _origColors = new Dictionary<ModelItem, Color>();
            foreach (var item in allItems)
            {
                try
                {
                    if (item.HasGeometry)
                        _origColors[item] = item.Geometry.PermanentColor;
                }
                catch { }
            }

            if (ColorA != null && items1.Count > 0)
                doc.Models.OverridePermanentColor(items1, ColorA);
            if (ColorB != null && items2.Count > 0)
                doc.Models.OverridePermanentColor(items2, ColorB);

            _prevColoredItems = allItems;
            ApplyPairIsolation(doc, allItems);
            doc.CurrentSelection.Clear();

            double offsetUnits = SectionBoxHelper.MmToDocUnits(OffsetMm);
            var bbox = allItems.BoundingBox();
            var clusterCenter = GetClusterCenter(list, bbox);
            var expandedBox = BuildClusterBox(list, bbox, clusterCenter, offsetUnits, BoxMode);
            if (expandedBox != null)
            {
                if (UseFixedIsoView)
                {
                    ViewpointCameraHelper.ApplyIso1ViewToBox(doc, expandedBox, clusterCenter);
                }
                else
                {
                    var vp = doc.CurrentViewpoint.CreateCopy();
                    vp.ZoomBox(expandedBox);
                    SetViewpointFocalPoint(vp, clusterCenter);
                    doc.CurrentViewpoint.CopyFrom(vp);
                }

                LastExpandedBox = expandedBox;
                if (UseSectionBox)
                    SectionBoxHelper.SetSectionBox(expandedBox);
            }

            if (UseContextTransparency && expandedBox != null)
            {
                try
                {
                    foreach (var item in allItems)
                    {
                        try
                        {
                            if (item.HasGeometry && (_origTransparency == null || !_origTransparency.ContainsKey(item)))
                            {
                                if (_origTransparency == null) _origTransparency = new Dictionary<ModelItem, double>();
                                _origTransparency[item] = item.Geometry.PermanentTransparency;
                            }
                        }
                        catch { }
                    }

                    ApplyContextTransparency(doc, allItems);
                    doc.Models.OverridePermanentTransparency(allItems, 0);

                    if (_prevTransparentItems == null) _prevTransparentItems = new ModelItemCollection();
                    foreach (var item in allItems)
                        if (!_prevTransparentItems.Contains(item))
                            _prevTransparentItems.Add(item);
                }
                catch { }
            }

            try
            {
                LastClashCenter = clusterCenter;
                if (LastClashCenter != null && ClashMarkerTool.IsActive)
                {
                    ClashMarkerTool.MarkerPoint = LastClashCenter;
                    try { Application.ActiveDocument.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All); } catch { }
                }
            }
            catch { }

            var displayName = string.IsNullOrWhiteSpace(groupName) ? "Группа коллизий" : groupName;
            LastStatus = $"{displayName}\n  Коллизий: {list.Count}\n  A: {items1.Count}\n  B: {items2.Count}";
            LastSuccess = true;
        }

        private static void SetViewpointFocalPoint(Viewpoint viewpoint, Point3D point)
        {
            if (viewpoint == null || point == null)
                return;

            try { viewpoint.RightOffsetAtFocalDistance = 0; } catch { }
            try { viewpoint.UpOffsetAtFocalDistance = 0; } catch { }
            try { viewpoint.RightOffsetFactor = 0; } catch { }
            try { viewpoint.UpOffsetFactor = 0; } catch { }

            try { viewpoint.PointAt(point); } catch { }

            try
            {
                var p = viewpoint.Position;
                var dx = p.X - point.X;
                var dy = p.Y - point.Y;
                var dz = p.Z - point.Z;
                viewpoint.FocalDistance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            catch { }
        }

        /// <summary>Сброс overrides — только затронутые элементы, не вся модель.</summary>
        public void ResetOverrides()
        {
            var doc = Application.ActiveDocument;

            // Восстанавливаем оригинальные цвета
            if (_prevColoredItems != null && _prevColoredItems.Count > 0)
            {
                foreach (var item in _prevColoredItems)
                {
                    try
                    {
                        Color orig;
                        if (_origColors != null && _origColors.TryGetValue(item, out orig))
                            doc.Models.OverridePermanentColor(new ModelItemCollection { item }, orig);
                        else
                        {
                            // Fallback: берём текущий цвет геометрии
                            if (item.HasGeometry && item.Geometry.OriginalColor != null)
                                doc.Models.OverridePermanentColor(new ModelItemCollection { item }, item.Geometry.OriginalColor);
                        }
                    }
                    catch { }
                }
                _origColors = null;
                _prevColoredItems = null;
            }

            // Восстанавливаем оригинальную прозрачность
            if (_prevTransparentItems != null && _prevTransparentItems.Count > 0)
            {
                if (_origTransparency != null && _origTransparency.Count > 0)
                {
                    foreach (var item in _prevTransparentItems)
                    {
                        try
                        {
                            double orig;
                            if (_origTransparency.TryGetValue(item, out orig))
                                doc.Models.OverridePermanentTransparency(new ModelItemCollection { item }, orig);
                            else
                                doc.Models.OverridePermanentTransparency(new ModelItemCollection { item }, 0);
                        }
                        catch { }
                    }
                }
                else
                {
                    doc.Models.OverridePermanentTransparency(_prevTransparentItems, 0);
                }
                _origTransparency = null;
                _prevTransparentItems = null;
            }
        }

        /// <summary>Полный сброс: overrides + selection + section.</summary>
        public void ResetView()
        {
            var doc = Application.ActiveDocument;
            ClearPairIsolation();
            ResetOverrides();
            ClearMarkers();
            if (_originalViewpoint != null && doc != null && doc.CurrentViewpoint != null)
            {
                try { doc.CurrentViewpoint.CopyFrom(_originalViewpoint); } catch { }
            }
            _originalViewpoint = null;
            if (doc != null && doc.CurrentSelection != null)
                doc.CurrentSelection.Clear();
            SectionBoxHelper.Disable();
            LastClashCenter = null;
            LastExpandedBox = null;
        }

        /// <summary>
        /// Restores only branches temporarily hidden by Clash pair isolation.
        /// Visibility that existed before isolation is left unchanged.
        /// </summary>
        public bool ClearPairIsolation(bool requestRedraw = true)
        {
            var isolationDocument = _pairIsolationDocument;
            var hiddenBranches = _pairIsolationHiddenBranches;
            var revealedPathItems = _pairIsolationRevealedPathItems;

            if (isolationDocument == null)
            {
                ResetPairIsolationTracking();
                return true;
            }

            if ((hiddenBranches == null || hiddenBranches.Count == 0) &&
                (revealedPathItems == null || revealedPathItems.Count == 0))
            {
                ResetPairIsolationTracking();
                return true;
            }

            try
            {
                if (hiddenBranches != null && hiddenBranches.Count > 0)
                    isolationDocument.Models.SetHidden(hiddenBranches, false);
                if (revealedPathItems != null && revealedPathItems.Count > 0)
                    isolationDocument.Models.SetHidden(revealedPathItems, true);
                if (requestRedraw)
                {
                    try { isolationDocument.ActiveView?.RequestDelayedRedraw(ViewRedrawRequests.All); }
                    catch (Exception ex) { Logger.Error("Failed to redraw after Clash pair isolation restore: " + ex.Message, "ClashPreview"); }
                }
                ResetPairIsolationTracking();
                return true;
            }
            catch (Exception ex)
            {
                // Keep the handles so a later reset can retry the restore.
                Logger.Error("Failed to restore Clash pair isolation: " + ex.Message, "ClashPreview");
                LastPairIsolationStatus = "не удалось восстановить видимость: " + ex.Message;
                return false;
            }
        }

        private void ApplyPairIsolation(Document document, ModelItemCollection pairItems)
        {
            LastPairIsolationStatus = "";
            if (!UsePairIsolation || document == null || pairItems == null || pairItems.Count == 0)
                return;

            var timer = Stopwatch.StartNew();
            var pairSet = new HashSet<ModelItem>();
            var pathSet = new HashSet<ModelItem>();
            foreach (var item in pairItems)
            {
                if (item == null)
                    continue;

                pairSet.Add(item);
                var current = item.Parent;
                while (current != null)
                {
                    pathSet.Add(current);
                    current = current.Parent;
                }
            }

            var roots = document.Models.CreateCollectionFromRootItems();
            var hasActiveRoot = roots.Cast<ModelItem>().Any(root => pairSet.Contains(root) || pathSet.Contains(root));
            if (!hasActiveRoot)
            {
                timer.Stop();
                LastPairIsolationElapsedMilliseconds = timer.ElapsedMilliseconds;
                LastPairIsolationStatus = "изоляция пропущена: объекты A/B не принадлежат активной модели";
                return;
            }

            var revealedPathItems = new ModelItemCollection();
            var detachedVisibilityItemCount = 0;
            foreach (var item in pairSet.Concat(pathSet).Distinct())
            {
                try
                {
                    if (item.IsHidden)
                        revealedPathItems.Add(item);
                }
                catch (InvalidOperationException)
                {
                    // Clash selections can contain detached proxy ModelItems. They are usable
                    // for result geometry but have no document visibility state to restore.
                    detachedVisibilityItemCount++;
                }
                catch (Exception ex)
                {
                    Logger.Error("Failed to inspect Clash pair path visibility: " + ex.Message, "ClashPreview");
                }
            }
            var hiddenBranches = new ModelItemCollection();
            foreach (ModelItem root in roots)
                CollectPairIsolationFrontier(root, pairSet, pathSet, hiddenBranches);

            // Store restore handles before the first visibility mutation. If an API call fails,
            // ClearPairIsolation can roll back the partial operation or retry later.
            _pairIsolationDocument = document;
            _pairIsolationHiddenBranches = hiddenBranches;
            _pairIsolationRevealedPathItems = revealedPathItems;
            try
            {
                if (revealedPathItems.Count > 0)
                    document.Models.SetHidden(revealedPathItems, false);
                if (hiddenBranches.Count > 0)
                    document.Models.SetHidden(hiddenBranches, true);

                timer.Stop();
                LastPairIsolationHiddenBranchCount = hiddenBranches.Count;
                LastPairIsolationElapsedMilliseconds = timer.ElapsedMilliseconds;
                LastPairIsolationStatus = "скрыто ветвей " + hiddenBranches.Count.ToString();
                if (detachedVisibilityItemCount > 0)
                {
                    LastPairIsolationStatus += "; пропущено proxy items без состояния видимости " +
                                               detachedVisibilityItemCount.ToString();
                }
                try { document.ActiveView?.RequestDelayedRedraw(ViewRedrawRequests.All); }
                catch { }
            }
            catch
            {
                ClearPairIsolation(false);
                throw;
            }
        }

        private void ResetPairIsolationTracking()
        {
            _pairIsolationDocument = null;
            _pairIsolationHiddenBranches = null;
            _pairIsolationRevealedPathItems = null;
            LastPairIsolationHiddenBranchCount = 0;
            LastPairIsolationElapsedMilliseconds = 0;
            LastPairIsolationStatus = "";
        }

        private static void CollectPairIsolationFrontier(
            ModelItem item,
            ISet<ModelItem> pairItems,
            ISet<ModelItem> pairPaths,
            ModelItemCollection hiddenBranches)
        {
            if (item == null || hiddenBranches == null)
                return;

            // A selected clash item keeps its complete subtree visible.
            if (pairItems.Contains(item))
                return;

            // A node outside both selected paths can hide its whole subtree in one API operation.
            if (!pairPaths.Contains(item))
            {
                try
                {
                    if (!item.IsHidden)
                        hiddenBranches.Add(item);
                }
                catch
                {
                    // Unknown visibility must not be changed because it cannot be restored safely.
                }
                return;
            }

            if (item.Children == null)
                return;

            foreach (ModelItem child in item.Children)
                CollectPairIsolationFrontier(child, pairItems, pairPaths, hiddenBranches);
        }

        private void CaptureOriginalViewpointIfNeeded(Document doc)
        {
            if (_originalViewpoint != null || doc == null || doc.CurrentViewpoint == null)
                return;

            try { _originalViewpoint = doc.CurrentViewpoint.CreateCopy(); } catch { }
        }

        /// <summary>Безопасная прозрачность — только владельцы текущих clash-элементов на два уровня вверх.</summary>
        private void ApplyContextTransparency(Document doc, ModelItemCollection clashItems)
        {
            ApplyOwnerTransparency(doc, clashItems, clashItems, "контекст");
        }

        private static BoundingBox3D BuildClashBox(ClashResult result, BoundingBox3D itemBox, double offsetUnits, string boxMode)
        {
            if (itemBox == null)
                return null;

            if (string.Equals(boxMode, BoxModeItems, StringComparison.OrdinalIgnoreCase))
            {
                const double minimumHalfExtent = 0.1;
                var itemCenter = itemBox.Center;
                var halfX = Math.Max((itemBox.Max.X - itemBox.Min.X) / 2.0 + offsetUnits, minimumHalfExtent);
                var halfY = Math.Max((itemBox.Max.Y - itemBox.Min.Y) / 2.0 + offsetUnits, minimumHalfExtent);
                var halfZ = Math.Max((itemBox.Max.Z - itemBox.Min.Z) / 2.0 + offsetUnits, minimumHalfExtent);
                return new BoundingBox3D(
                    new Point3D(itemCenter.X - halfX, itemCenter.Y - halfY, itemCenter.Z - halfZ),
                    new Point3D(itemCenter.X + halfX, itemCenter.Y + halfY, itemCenter.Z + halfZ));
            }

            var center = result != null && result.Center != null ? result.Center : itemBox.Center;
            var halfSize = Math.Max(offsetUnits, 0.1);
            return new BoundingBox3D(
                new Point3D(center.X - halfSize, center.Y - halfSize, center.Z - halfSize),
                new Point3D(center.X + halfSize, center.Y + halfSize, center.Z + halfSize));
        }

        public static BoundingBox3D PlanClashBox(ClashResult result, double offsetMm, string boxMode)
        {
            if (result == null)
                return null;

            var items = new ModelItemCollection();
            AddClashSideItems(items, result.Item1, result.Selection1);
            AddClashSideItems(items, result.Item2, result.Selection2);
            var itemBox = items.Count == 0 ? null : items.BoundingBox();
            return BuildClashBox(result, itemBox, SectionBoxHelper.MmToDocUnits(offsetMm), boxMode);
        }

        private static void AddClashSideItems(ModelItemCollection target, ModelItem primaryItem, ModelItemCollection sideItems)
        {
            if (target == null)
                return;

            var before = target.Count;
            if (sideItems != null)
            {
                foreach (var item in sideItems)
                    AddGeometryItems(target, item);
            }

            if (primaryItem != null)
                AddGeometryItems(target, primaryItem);

            if (target.Count == before && sideItems != null)
                AddUniqueItems(target, sideItems);
        }

        private static void AddUniqueItem(ModelItemCollection target, ModelItem item)
        {
            if (target == null || item == null || target.Contains(item))
                return;

            target.Add(item);
        }

        private static void AddGeometryItems(ModelItemCollection target, ModelItem item)
        {
            if (target == null || item == null)
                return;

            var added = false;
            var capped = false;
            try
            {
                foreach (var descendant in item.DescendantsAndSelf)
                {
                    if (target.Count >= MaxPreviewGeometryItemsPerSide)
                    {
                        capped = true;
                        break;
                    }

                    if (descendant != null && descendant.HasGeometry)
                    {
                        AddUniqueItem(target, descendant);
                        added = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to expand Clash preview geometry items: " + ex.Message, "ClashPreview");
            }

            if (capped)
            {
                Logger.Error("Clash preview geometry expansion reached cap of " + MaxPreviewGeometryItemsPerSide.ToString() + " items for one side.", "ClashPreview");
            }

            if (!added && !capped)
                AddUniqueItem(target, item);
        }

        private static void AddUniqueItems(ModelItemCollection target, ModelItemCollection source)
        {
            if (target == null || source == null)
                return;

            foreach (var item in source)
                if (item != null && !target.Contains(item))
                    target.Add(item);
        }

        private static Point3D GetClusterCenter(IList<ClashResult> results, BoundingBox3D fallbackBox)
        {
            var centers = results
                .Where(result => result != null && result.Center != null)
                .Select(result => result.Center)
                .ToList();

            if (centers.Count > 0)
                return new Point3D(
                    centers.Average(point => point.X),
                    centers.Average(point => point.Y),
                    centers.Average(point => point.Z));

            return fallbackBox != null ? fallbackBox.Center : null;
        }

        private static BoundingBox3D BuildClusterBox(IList<ClashResult> results, BoundingBox3D itemBox, Point3D center, double offsetUnits, string boxMode)
        {
            if (itemBox == null && center == null)
                return null;

            if (string.Equals(boxMode, BoxModePoint, StringComparison.OrdinalIgnoreCase))
            {
                var points = results
                    .Where(result => result != null && result.Center != null)
                    .Select(result => result.Center)
                    .ToList();

                if (points.Count > 0)
                {
                    var minX = points.Min(point => point.X) - offsetUnits;
                    var minY = points.Min(point => point.Y) - offsetUnits;
                    var minZ = points.Min(point => point.Z) - offsetUnits;
                    var maxX = points.Max(point => point.X) + offsetUnits;
                    var maxY = points.Max(point => point.Y) + offsetUnits;
                    var maxZ = points.Max(point => point.Z) + offsetUnits;
                    return new BoundingBox3D(new Point3D(minX, minY, minZ), new Point3D(maxX, maxY, maxZ));
                }
            }

            return ExpandBox(itemBox, offsetUnits);
        }

        private static BoundingBox3D ExpandBox(BoundingBox3D box, double offsetUnits)
        {
            if (box == null)
                return null;

            var offset = Math.Max(offsetUnits, 0.1);
            return new BoundingBox3D(
                new Point3D(box.Min.X - offset, box.Min.Y - offset, box.Min.Z - offset),
                new Point3D(box.Max.X + offset, box.Max.Y + offset, box.Max.Z + offset));
        }

        /// <summary>
        /// Делает прозрачными только ближайших владельцев текущих clash-элементов.
        /// Без обхода потомков и без сканирования модели.
        /// </summary>
        public int ApplyClashRootContextTransparency()
        {
            LastFullBoxTransparencyStatus = "";
            var doc = Application.ActiveDocument;
            double trans = ContextTransparency;
            if (doc == null || trans <= 0)
            {
                LastFullBoxTransparencyStatus = doc == null ? "нет активного документа" : "уровень 0%";
                return 0;
            }

            if (_prevColoredItems == null || _prevColoredItems.Count == 0)
            {
                LastFullBoxTransparencyStatus = "clash items не найдены";
                return 0;
            }

            return ApplyOwnerTransparency(doc, _prevColoredItems, _prevColoredItems, "владельцы A/B");
        }

        /// <summary>Последний bbox коллизии.</summary>
        public BoundingBox3D LastExpandedBox { get; private set; }

        /// <summary>Последний центр коллизии (для маркера).</summary>
        public Point3D LastClashCenter { get; private set; }

        /// <summary>Убирает маркер.</summary>
        public void ClearMarkers()
        {
            ClashMarkerTool.Hide();
        }

        /// <summary>
        /// Drops native handles retained for the previous document without applying
        /// any changes to the newly active document.
        /// </summary>
        public void ForgetDocumentState()
        {
            _prevColoredItems = null;
            _prevTransparentItems = null;
            _origColors = null;
            _origTransparency = null;
            _originalViewpoint = null;
            ResetPairIsolationTracking();
            LastExpandedBox = null;
            LastClashCenter = null;
            LastStatus = string.Empty;
            LastSuccess = false;
            LastFullBoxTransparencyStatus = string.Empty;
        }

        /// <summary>
        /// Прозрачность по выделенным в дереве: только владельцы на два уровня вверх.
        /// Возвращает количество обработанных владельцев.
        /// </summary>
        public int ApplyTransparencyToSelection()
        {
            var doc = Application.ActiveDocument;
            double trans = ContextTransparency;
            if (trans <= 0) return 0;

            var selection = doc.CurrentSelection.SelectedItems;
            if (selection.Count == 0) return 0;

            return ApplyOwnerTransparency(doc, selection, _prevColoredItems, "выделение");
        }

        private int ApplyOwnerTransparency(Document doc, IEnumerable<ModelItem> sourceItems, IEnumerable<ModelItem> opaqueItems, string statusPrefix)
        {
            LastFullBoxTransparencyStatus = "";
            if (doc == null || sourceItems == null || ContextTransparency <= 0)
                return 0;

            RestorePreviousTransparency(doc);

            var ownerItems = new ModelItemCollection();
            foreach (var item in sourceItems)
                AddUniqueItem(ownerItems, GetAncestorUp(item, 2) ?? item);

            if (ownerItems.Count == 0)
            {
                LastFullBoxTransparencyStatus = statusPrefix + ": владельцы не найдены";
                return 0;
            }

            var opaqueCollection = new ModelItemCollection();
            if (opaqueItems != null)
                foreach (var item in opaqueItems)
                    AddUniqueItem(opaqueCollection, item);

            _origTransparency = new Dictionary<ModelItem, double>();
            var affected = new ModelItemCollection();
            foreach (var item in ownerItems)
            {
                AddUniqueItem(affected, item);
                CaptureOriginalTransparency(_origTransparency, item);
            }
            foreach (var item in opaqueCollection)
            {
                AddUniqueItem(affected, item);
                CaptureOriginalTransparency(_origTransparency, item);
            }

            ApplyTransparencyOverrideInBatches(doc, ownerItems, ContextTransparency);
            if (opaqueCollection.Count > 0)
                ApplyTransparencyOverrideInBatches(doc, opaqueCollection, 0);

            _prevTransparentItems = affected;

            try { doc.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All); }
            catch { }

            LastFullBoxTransparencyStatus =
                $"{statusPrefix}: владельцев {ownerItems.Count}, текущих непрозрачных {opaqueCollection.Count}";

            return ownerItems.Count;
        }

        /// <summary>Поднимается от элемента на заданное количество родительских уровней.</summary>
        private static ModelItem GetAncestorUp(ModelItem item, int levels)
        {
            if (item == null) return null;
            var current = item;
            for (var i = 0; i < levels && current.Parent != null; i++)
            {
                current = current.Parent;
            }

            return current;
        }

        private void RestorePreviousTransparency(Document doc)
        {
            if (doc == null || _prevTransparentItems == null || _prevTransparentItems.Count == 0)
                return;

            if (_origTransparency != null)
            {
                foreach (var item in _prevTransparentItems)
                {
                    try
                    {
                        double orig;
                        if (_origTransparency.TryGetValue(item, out orig))
                            doc.Models.OverridePermanentTransparency(new ModelItemCollection { item }, orig);
                        else
                            doc.Models.OverridePermanentTransparency(new ModelItemCollection { item }, 0);
                    }
                    catch { }
                }
            }
            else
            {
                doc.Models.OverridePermanentTransparency(_prevTransparentItems, 0);
            }

            _prevTransparentItems = null;
            _origTransparency = null;
        }

        public void ClearPreviewTransparency()
        {
            RestorePreviousTransparency(Application.ActiveDocument);
        }

        private static void CaptureOriginalTransparency(Dictionary<ModelItem, double> target, ModelItem item)
        {
            if (target == null || item == null || target.ContainsKey(item))
                return;

            try
            {
                target[item] = item.HasGeometry ? item.Geometry.PermanentTransparency : 0;
            }
            catch
            {
                target[item] = 0;
            }
        }

        private static void ApplyTransparencyOverrideInBatches(Document doc, ModelItemCollection items, double transparency)
        {
            if (doc == null || items == null || items.Count == 0)
                return;

            var batch = new ModelItemCollection();
            foreach (var item in items)
            {
                if (item == null)
                    continue;

                batch.Add(item);
                if (batch.Count >= TransparencyBatchSize)
                {
                    doc.Models.OverridePermanentTransparency(batch, transparency);
                    batch = new ModelItemCollection();
                }
            }

            if (batch.Count > 0)
                doc.Models.OverridePermanentTransparency(batch, transparency);
        }
    }
}
