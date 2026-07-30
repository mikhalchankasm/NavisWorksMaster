using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Plugins;
using NavisHelper.Core.Localization;
using Application = Autodesk.Navisworks.Api.Application;
using NavisHelper.Agent.Contracts;

namespace NavisHelper
{
    [Plugin("SelectionHatchMarker", "CBC", DisplayName = "Маркер выделения")]
    [AddInPlugin(AddInLocation.None)]
    public class SelectionHatchMarker : AddInPlugin
    {
        protected virtual bool UseObjectBoundingCorners => false;
        protected virtual string CommandTitle =>
            UiLocalizationService.Current.GetString("SelectionMarkerTitle");

        public override int Execute(params string[] parameters)
        {
            var document = Application.ActiveDocument;
            var selection = document == null || document.CurrentSelection == null ? null : document.CurrentSelection.SelectedItems;
            if (selection == null || selection.Count == 0)
            {
                MessageBox.Show(
                    UiLocalizationService.Current.GetString("CommonNoSelection"),
                    CommandTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return 0;
            }

            var bounds = selection.BoundingBox();
            if (bounds == null)
            {
                MessageBox.Show(
                    UiLocalizationService.Current.GetString("SelectionBoundsUnavailable"),
                    CommandTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return 0;
            }

            // Centre mode connects item centres. Bounds mode contributes every
            // bbox corner, so the overlay follows the overall object envelope.
            var outlinePoints = new List<Point3D>();
            foreach (ModelItem item in selection)
            {
                try
                {
                    var itemBounds = item == null ? null : item.BoundingBox();
                    if (itemBounds != null)
                    {
                        if (UseObjectBoundingCorners)
                        {
                            foreach (var x in new[] { itemBounds.Min.X, itemBounds.Max.X })
                            foreach (var y in new[] { itemBounds.Min.Y, itemBounds.Max.Y })
                            foreach (var z in new[] { itemBounds.Min.Z, itemBounds.Max.Z })
                                outlinePoints.Add(new Point3D(x, y, z));
                        }
                        else
                        {
                            outlinePoints.Add(itemBounds.Center);
                        }
                    }
                }
                catch
                {
                }
            }

            if (outlinePoints.Count < 3)
            {
                foreach (var x in new[] { bounds.Min.X, bounds.Max.X })
                foreach (var y in new[] { bounds.Min.Y, bounds.Max.Y })
                foreach (var z in new[] { bounds.Min.Z, bounds.Max.Z })
                    outlinePoints.Add(new Point3D(x, y, z));
            }

            // Rebind even when a previous selection marker is active. This avoids
            // stale overlay state after changing the Navisworks selection.
            if (ClashMarkerTool.IsActive)
                ClashMarkerTool.Hide();
            ClashMarkerTool.ShowMany(
                new[] { new ClashMarkerVisual(bounds.Center, bounds, outlinePoints) },
                MarkupRedlineJsonBuilder.HatchStyle,
                10);
            if (!string.IsNullOrWhiteSpace(ClashMarkerTool.LastError))
                MessageBox.Show(
                    UiLocalizationService.Current.Format(
                        "SelectionMarkerEnableFailedFormat",
                        ClashMarkerTool.LastError),
                    CommandTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            return 0;
        }
    }
}
