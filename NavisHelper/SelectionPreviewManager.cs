using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Core;
using Application = Autodesk.Navisworks.Api.Application;

namespace NavisHelper
{
    /// <summary>
    /// Section box + прозрачность контекста для текущего выделения.
    /// Аналог ClashPreviewManager, но без привязки к коллизиям.
    /// </summary>
    public class SelectionPreviewManager
    {
        private ModelItemCollection _prevTransparentItems;
        private Dictionary<ModelItem, double> _origTransparency;
        private Document _selectionAnchorDocument;
        private ModelItemCollection _selectionAnchorItems;
        private BoundingBox3D _selectionAnchorBounds;
        private List<ModelItem> _selectionAnchorRoots;

        public double CommonOffsetMm { get; set; } = 1000;
        public double OffsetXMm { get; set; }
        public double OffsetYMm { get; set; }
        public double OffsetZMm { get; set; }
        public double ShiftXMm { get; set; }
        public double ShiftYMm { get; set; }
        public double ShiftZMm { get; set; }
        public double ContextTransparency { get; set; } = 0.7;
        public bool UseContextTransparency { get; set; } = false;
        public bool UseSectionBox { get; set; } = true;

        public string LastStatus { get; private set; } = "";
        public bool LastSuccess { get; private set; } = false;
        public BoundingBox3D LastExpandedBox { get; private set; }

        /// <summary>
        /// Section box + прозрачность по текущему выделению.
        /// </summary>
        public void ShowSelection(bool useCurrentSelectionAsAnchor = true)
        {
            LastSuccess = false;
            var doc = Application.ActiveDocument;
            if (doc == null)
            {
                LastStatus = "Нет активного документа";
                LastSuccess = false;
                return;
            }

            var currentSelection = doc.CurrentSelection?.SelectedItems;
            var hasCurrentSelection = currentSelection != null && currentSelection.Count > 0;
            ModelItemCollection selection;
            BoundingBox3D bbox;
            var reusedAnchor = false;

            if (useCurrentSelectionAsAnchor && hasCurrentSelection)
            {
                selection = currentSelection;
                bbox = selection.BoundingBox();
                if (bbox == null)
                {
                    LastStatus = "Не удалось определить габариты";
                    LastSuccess = false;
                    return;
                }

                _selectionAnchorDocument = doc;
                _selectionAnchorItems = new ModelItemCollection();
                _selectionAnchorItems.CopyFrom(selection);
                _selectionAnchorBounds = bbox;
                _selectionAnchorRoots = GetDistinctModelRoots(selection);
            }
            else if (IsSelectionAnchorValid(doc) &&
                     _selectionAnchorItems != null &&
                     _selectionAnchorItems.Count > 0 &&
                     _selectionAnchorBounds != null)
            {
                selection = _selectionAnchorItems;
                bbox = _selectionAnchorBounds;
                reusedAnchor = true;
            }
            else if (hasCurrentSelection)
            {
                selection = currentSelection;
                bbox = selection.BoundingBox();
                if (bbox == null)
                {
                    LastStatus = "Не удалось определить габариты";
                    return;
                }

                _selectionAnchorDocument = doc;
                _selectionAnchorItems = new ModelItemCollection();
                _selectionAnchorItems.CopyFrom(selection);
                _selectionAnchorBounds = bbox;
                _selectionAnchorRoots = GetDistinctModelRoots(selection);
            }
            else
            {
                LastStatus = "Нет выделенных объектов и сохранённой области Section Box";
                LastSuccess = false;
                return;
            }

            // Сброс предыдущих overrides
            ResetOverrides();

            double commonOffsetMm = Math.Max(0, CommonOffsetMm);
            double offsetXUnits = SectionBoxHelper.MmToDocUnits(commonOffsetMm + Math.Max(0, OffsetXMm));
            double offsetYUnits = SectionBoxHelper.MmToDocUnits(commonOffsetMm + Math.Max(0, OffsetYMm));
            double offsetZUnits = SectionBoxHelper.MmToDocUnits(commonOffsetMm + Math.Max(0, OffsetZMm));
            double shiftXUnits = SectionBoxHelper.MmToDocUnits(ShiftXMm);
            double shiftYUnits = SectionBoxHelper.MmToDocUnits(ShiftYMm);
            double shiftZUnits = SectionBoxHelper.MmToDocUnits(ShiftZMm);

            var center = bbox.Center;
            double padX = Math.Max((bbox.Max.X - bbox.Min.X) / 2.0, 0.1) + offsetXUnits;
            double padY = Math.Max((bbox.Max.Y - bbox.Min.Y) / 2.0, 0.1) + offsetYUnits;
            double padZ = Math.Max((bbox.Max.Z - bbox.Min.Z) / 2.0, 0.1) + offsetZUnits;

            var expandedBox = new BoundingBox3D(
                new Point3D(center.X + shiftXUnits - padX, center.Y + shiftYUnits - padY, center.Z + shiftZUnits - padZ),
                new Point3D(center.X + shiftXUnits + padX, center.Y + shiftYUnits + padY, center.Z + shiftZUnits + padZ));

            // Zoom
            var vp = doc.CurrentViewpoint.CreateCopy();
            vp.ZoomBox(expandedBox);
            doc.CurrentViewpoint.CopyFrom(vp);

            LastExpandedBox = expandedBox;

            // Section box
            if (UseSectionBox)
                SectionBoxHelper.SetSectionBox(expandedBox);

            // Прозрачность контекста
            if (UseContextTransparency)
            {
                try
                {
                    ApplyContextTransparency(doc, selection, expandedBox);
                    // Выделенные элементы — непрозрачны
                    var selectedGeom = new ModelItemCollection();
                    foreach (var item in selection)
                        foreach (var d in item.DescendantsAndSelf)
                            if (d.HasGeometry) selectedGeom.Add(d);
                    if (selectedGeom.Count > 0)
                        doc.Models.OverridePermanentTransparency(selectedGeom, 0);
                }
                catch { }
            }

            LastStatus =
                $"Section box: {selection.Count} объектов{(reusedAnchor ? " (сохранённая область)" : "")}; общее расширение {(int)CommonOffsetMm} мм, " +
                $"добавка X/Y/Z {(int)OffsetXMm}/{(int)OffsetYMm}/{(int)OffsetZMm} мм; смещение " +
                $"{(int)ShiftXMm}/{(int)ShiftYMm}/{(int)ShiftZMm} мм";
            LastSuccess = true;
        }

        /// <summary>Сброс прозрачности.</summary>
        public void ResetOverrides()
        {
            var doc = Application.ActiveDocument;

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

        /// <summary>Полный сброс: прозрачность + section box.</summary>
        public void ResetView()
        {
            ResetOverrides();
            SectionBoxHelper.Disable();
            LastExpandedBox = null;
            _selectionAnchorDocument = null;
            _selectionAnchorItems = null;
            _selectionAnchorBounds = null;
            _selectionAnchorRoots = null;
            LastStatus = "Section box по выделению сброшен";
            LastSuccess = true;
        }

        private bool IsSelectionAnchorValid(Document document)
        {
            if (!ReferenceEquals(_selectionAnchorDocument, document) ||
                _selectionAnchorRoots == null ||
                _selectionAnchorRoots.Count == 0)
                return false;

            try
            {
                var currentRoots = new List<ModelItem>();
                foreach (Model model in document.Models)
                {
                    if (model?.RootItem != null)
                        currentRoots.Add(model.RootItem);
                }

                return _selectionAnchorRoots.All(
                    anchorRoot => currentRoots.Any(
                        currentRoot => ReferenceEquals(anchorRoot, currentRoot) || Equals(anchorRoot, currentRoot)));
            }
            catch
            {
                return false;
            }
        }

        private static List<ModelItem> GetDistinctModelRoots(ModelItemCollection items)
        {
            var roots = new List<ModelItem>();
            foreach (ModelItem item in items)
            {
                var root = item;
                while (root?.Parent != null)
                    root = root.Parent;

                if (root != null && !roots.Any(existing => ReferenceEquals(existing, root) || Equals(existing, root)))
                    roots.Add(root);
            }

            return roots;
        }

        /// <summary>Прозрачность по родителям выделенных элементов.</summary>
        private void ApplyContextTransparency(Document doc, ModelItemCollection selectedItems,
            BoundingBox3D searchBox)
        {
            double trans = ContextTransparency;
            if (trans <= 0) return;

            // Множество выделенных (и потомков) — их не трогаем
            var selectedSet = new HashSet<ModelItem>();
            foreach (var item in selectedItems)
                foreach (var d in item.DescendantsAndSelf)
                    selectedSet.Add(d);

            var contextItems = new ModelItemCollection();

            // Поиск через родителей (быстро, как в ClashPreviewManager)
            foreach (var item in selectedItems)
            {
                var ancestor = item.Parent?.Parent ?? item.Parent;
                if (ancestor == null) continue;

                foreach (var desc in ancestor.DescendantsAndSelf)
                {
                    if (selectedSet.Contains(desc)) continue;
                    try
                    {
                        var itemBox = desc.BoundingBox();
                        if (itemBox != null && desc.HasGeometry &&
                            itemBox.Min.X < searchBox.Max.X && itemBox.Max.X > searchBox.Min.X &&
                            itemBox.Min.Y < searchBox.Max.Y && itemBox.Max.Y > searchBox.Min.Y &&
                            itemBox.Min.Z < searchBox.Max.Z && itemBox.Max.Z > searchBox.Min.Z)
                        {
                            contextItems.Add(desc);
                            if (contextItems.Count > 500) break;
                        }
                    }
                    catch { }
                }
                if (contextItems.Count > 500) break;
            }

            ApplyTransparencyToItems(doc, contextItems, trans);
        }

        /// <summary>
        /// Полное сканирование section box — вызывается отдельной кнопкой.
        /// </summary>
        public int ApplyFullBoxTransparency(BoundingBox3D sectionBox)
        {
            var doc = Application.ActiveDocument;
            double trans = ContextTransparency;
            if (trans <= 0) return 0;

            // Восстанавливаем предыдущую прозрачность
            ResetOverrides();

            // Выделенные — исключаем
            var selectedSet = new HashSet<ModelItem>();
            var selection = doc.CurrentSelection.SelectedItems;
            foreach (var s in selection)
                foreach (var d in s.DescendantsAndSelf)
                    selectedSet.Add(d);

            var contextItems = new ModelItemCollection();

            foreach (var model in doc.Models)
            {
                foreach (var item in model.RootItem.DescendantsAndSelf)
                {
                    if (selectedSet.Contains(item)) continue;
                    if (!item.HasGeometry) continue;

                    try
                    {
                        var itemBox = item.BoundingBox();
                        if (itemBox == null) continue;

                        if (itemBox.Min.X < sectionBox.Max.X && itemBox.Max.X > sectionBox.Min.X &&
                            itemBox.Min.Y < sectionBox.Max.Y && itemBox.Max.Y > sectionBox.Min.Y &&
                            itemBox.Min.Z < sectionBox.Max.Z && itemBox.Max.Z > sectionBox.Min.Z)
                        {
                            contextItems.Add(item);
                            if (contextItems.Count > 5000) break;
                        }
                    }
                    catch { }
                }
                if (contextItems.Count > 5000) break;
            }

            ApplyTransparencyToItems(doc, contextItems, trans);

            // Выделенные — непрозрачны
            var selectedGeom = new ModelItemCollection();
            foreach (var s in selection)
                foreach (var d in s.DescendantsAndSelf)
                    if (d.HasGeometry) selectedGeom.Add(d);
            if (selectedGeom.Count > 0)
                doc.Models.OverridePermanentTransparency(selectedGeom, 0);

            return contextItems.Count;
        }

        /// <summary>
        /// Прозрачность по первому уровню — все потомки первого предка.
        /// </summary>
        public int ApplyTransparencyByFirstLevel()
        {
            var doc = Application.ActiveDocument;
            double trans = ContextTransparency;
            if (trans <= 0) return 0;

            var selection = doc.CurrentSelection.SelectedItems;
            if (selection.Count == 0) return 0;

            // Сбрасываем предыдущую прозрачность
            ResetOverrides();

            var selectedSet = new HashSet<ModelItem>();
            foreach (var s in selection)
                foreach (var d in s.DescendantsAndSelf)
                    selectedSet.Add(d);

            var contextItems = new ModelItemCollection();
            var processedRoots = new HashSet<ModelItem>();

            foreach (var selected in selection)
            {
                var level1 = GetFirstLevelAncestor(selected);
                if (level1 == null || processedRoots.Contains(level1)) continue;
                processedRoots.Add(level1);

                foreach (var desc in level1.DescendantsAndSelf)
                {
                    if (selectedSet.Contains(desc)) continue;
                    if (!desc.HasGeometry) continue;
                    contextItems.Add(desc);
                    if (contextItems.Count > 5000) break;
                }
                if (contextItems.Count > 5000) break;
            }

            ApplyTransparencyToItems(doc, contextItems, trans);

            // Выделенные — непрозрачны
            var selectedGeom = new ModelItemCollection();
            foreach (var s in selection)
                foreach (var d in s.DescendantsAndSelf)
                    if (d.HasGeometry) selectedGeom.Add(d);
            if (selectedGeom.Count > 0)
                doc.Models.OverridePermanentTransparency(selectedGeom, 0);

            return contextItems.Count;
        }

        private static ModelItem GetFirstLevelAncestor(ModelItem item)
        {
            if (item == null) return null;
            var current = item;
            while (current.Parent != null && current.Parent.Parent != null)
                current = current.Parent;
            return current;
        }

        private void ApplyTransparencyToItems(Document doc, ModelItemCollection items, double trans)
        {
            if (items.Count == 0) return;

            _origTransparency = new Dictionary<ModelItem, double>();
            foreach (var item in items)
            {
                try { _origTransparency[item] = item.Geometry.PermanentTransparency; }
                catch { }
            }
            doc.Models.OverridePermanentTransparency(items, trans);
            _prevTransparentItems = items;

            try { doc.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All); }
            catch { }
        }
    }
}
