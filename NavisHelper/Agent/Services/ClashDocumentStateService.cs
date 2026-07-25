using System;
using Autodesk.Navisworks.Api;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal static class ClashDocumentStateService
    {
        public static ClashDocumentStateSnapshot Capture(Document document, string operationContext)
        {
            var snapshot = new ClashDocumentStateSnapshot();
            try
            {
                snapshot.Viewpoint = document.CurrentViewpoint == null ? null : document.CurrentViewpoint.CreateCopy();
                snapshot.Selection = new ModelItemCollection();
                if (document.CurrentSelection != null && document.CurrentSelection.SelectedItems != null)
                    snapshot.Selection.CopyFrom(document.CurrentSelection.SelectedItems);
                if (document.ActiveView != null)
                {
                    snapshot.ClippingPlanes = document.ActiveView.GetClippingPlanes();
                    snapshot.HasClippingPlanes = true;
                }
            }
            catch (Exception ex)
            {
                snapshot.Warning = "Could not capture original Navisworks view/selection/clipping before " + operationContext + ": " + ex.Message;
                Logger.Error(snapshot.Warning, "ClashMcp");
            }

            return snapshot;
        }

        public static void RestoreViewpoint(Document document, ClashDocumentStateSnapshot snapshot)
        {
            if (document == null || snapshot == null || snapshot.Viewpoint == null || document.CurrentViewpoint == null)
                return;

            document.CurrentViewpoint.CopyFrom(snapshot.Viewpoint);
        }

        public static string RestoreAll(Document document, ClashDocumentStateSnapshot snapshot, string operationContext)
        {
            try
            {
                RestoreClippingPlanes(document, snapshot != null && snapshot.HasClippingPlanes, snapshot == null ? string.Empty : snapshot.ClippingPlanes);
                RestoreViewpoint(document, snapshot);
                if (snapshot != null && snapshot.Selection != null)
                    document.CurrentSelection.CopyFrom(snapshot.Selection);
            }
            catch (Exception ex)
            {
                var warning = "Could not fully restore Navisworks view/selection/clipping after " + operationContext + ": " + ex.Message;
                Logger.Error(warning, "ClashMcp");
                return warning;
            }

            return string.Empty;
        }

        private static void RestoreClippingPlanes(Document document, bool hasOriginalClippingPlanes, string originalClippingPlanes)
        {
            try
            {
                if (document == null || document.ActiveView == null)
                    return;

                if (hasOriginalClippingPlanes && !string.IsNullOrWhiteSpace(originalClippingPlanes))
                {
                    if (!document.ActiveView.TrySetClippingPlanes(originalClippingPlanes))
                        document.ActiveView.SetClippingPlanes(originalClippingPlanes);
                    document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
                    return;
                }

                SectionBoxHelper.Disable();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to restore clash report clipping planes: " + ex.Message, "ClashMcp");
                try
                {
                    SectionBoxHelper.Disable();
                }
                catch (Exception disableEx)
                {
                    Logger.Error("Failed to disable section box after clipping restore failure: " + disableEx.Message, "ClashMcp");
                }
            }
        }
    }

    internal sealed class ClashDocumentStateSnapshot
    {
        public Viewpoint Viewpoint;
        public ModelItemCollection Selection;
        public string ClippingPlanes = string.Empty;
        public bool HasClippingPlanes;
        public string Warning = string.Empty;
    }
}
