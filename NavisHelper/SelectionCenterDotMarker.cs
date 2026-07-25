using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using Application = Autodesk.Navisworks.Api.Application;

namespace NavisHelper
{
    [Plugin("SelectionCenterDotMarker", "CBC", DisplayName = "Точки центров")]
    [AddInPlugin(AddInLocation.None)]
    public sealed class SelectionCenterDotMarker : AddInPlugin
    {
        public override int Execute(params string[] parameters)
        {
            const string title = "Точки центров";
            var document = Application.ActiveDocument;
            var selection = document == null || document.CurrentSelection == null ? null : document.CurrentSelection.SelectedItems;
            if (selection == null || selection.Count == 0)
            {
                MessageBox.Show("Нет выделенных элементов.", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            var visuals = new List<ClashMarkerVisual>();
            foreach (ModelItem item in selection)
            {
                try
                {
                    var bounds = item == null ? null : item.BoundingBox();
                    if (bounds != null)
                        visuals.Add(new ClashMarkerVisual(bounds.Center, null));
                }
                catch
                {
                }
            }

            if (visuals.Count == 0)
            {
                MessageBox.Show("Не удалось определить центры выделенных элементов.", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 0;
            }

            if (ClashMarkerTool.IsActive)
                ClashMarkerTool.Hide();
            ClashMarkerTool.ShowMany(visuals, ClashMarkerTool.CenterDotStyle, 14);
            if (!string.IsNullOrWhiteSpace(ClashMarkerTool.LastError))
                MessageBox.Show("Не удалось включить маркеры: " + ClashMarkerTool.LastError, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 0;
        }
    }
}
