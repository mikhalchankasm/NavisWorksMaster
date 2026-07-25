using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Autodesk.Navisworks.Api;

namespace NavisHelper
{
    /// <summary>
    /// Операции навигации по дереву модели (Parent, Child, Sibling, Leaf, All Under).
    /// </summary>
    public static class TreeNavigation
    {
        public static ModelItemCollection GetSelection()
        {
            return new ModelItemCollection(
                Autodesk.Navisworks.Api.Application.ActiveDocument.CurrentSelection.SelectedItems);
        }

        public static void SetSelection(IEnumerable<ModelItem> items)
        {
            var list = items as List<ModelItem> ?? items.ToList();
            if (list.Count == 0) return;
            Autodesk.Navisworks.Api.Application.ActiveDocument.CurrentSelection.CopyFrom(list);
        }

        public static void SelectParents()
        {
            var result = new List<ModelItem>();
            foreach (var item in GetSelection())
            {
                if (item.Parent != null)
                    result.Add(item.Parent);
            }
            SetSelection(result);
        }

        public static void SelectChildren()
        {
            var result = new List<ModelItem>();
            foreach (var item in GetSelection())
            {
                foreach (var child in item.Children)
                    result.Add(child);
            }
            SetSelection(result);
        }

        public static void SelectSiblings()
        {
            var selection = GetSelection();
            if (selection.Count == 0) return;

            var parents = new List<ModelItem>();
            foreach (var item in selection)
            {
                if (item.Parent != null)
                    parents.Add(item.Parent);
            }

            var result = new List<ModelItem>();
            foreach (var parent in parents)
            {
                foreach (var child in parent.Children)
                    result.Add(child);
            }
            SetSelection(result);
        }

        public static void SelectLeafNodes()
        {
            var result = new List<ModelItem>();
            foreach (var item in GetSelection())
            {
                foreach (var descendant in item.Descendants)
                {
                    if (!descendant.Descendants.Any())
                        result.Add(descendant);
                }
            }
            SetSelection(result);
        }

        public static void SelectAllUnder()
        {
            var result = new List<ModelItem>();
            foreach (var item in GetSelection())
            {
                foreach (var descendant in item.Descendants)
                    result.Add(descendant);
            }
            SetSelection(result);
        }

        /// <summary>
        /// Безопасный вызов с обработкой ошибок.
        /// </summary>
        public static void SafeExecute(Action action)
        {
            try { action(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "NavisHelper"); }
        }
    }
}
