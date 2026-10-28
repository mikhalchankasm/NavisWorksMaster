using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using NavisHelper.Agent.Session;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed class ItemViewpointsService
    {
        private const string EmptyRedlines = "{\"Type\":\"RedlineCollection\",\"Version\":1,\"Values\":[]}";

        public ItemViewpointsResponse Execute(Document document, ItemViewpointsRequest request, MatchSessionStore sessionStore)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            request = request ?? new ItemViewpointsRequest();
            var error = ItemViewpointRules.Describe(request);
            if (error != null) throw new AgentCommandException(ErrorCodes.SchemaViolation, error);
            var view = document.ActiveView;
            if (view == null || document.CurrentViewpoint == null)
                throw new AgentCommandException(ErrorCodes.NoActiveView, "There is no active view.");
            if (document.SavedViewpoints == null || document.SavedViewpoints.RootItem == null)
                throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, "The active document has no saved viewpoints collection.");

            var allItems = ResolveItems(document, request.MatchHandles, sessionStore);
            if (allItems.Count == 0)
                throw new AgentCommandException(ErrorCodes.NoSelection, "There are no selected or matched items.");
            var items = allItems.Take(ItemViewpointRules.ClampMaxItems(request.MaxItems)).ToList();
            var names = ItemViewpointRules.PlanNames(items.Select(item => item.DisplayName).ToList(), request.NameTemplate);
            var folderPath = DocumentCommandService.NormalizeFolderPath(request.FolderPath);
            var response = new ItemViewpointsResponse
            {
                Apply = request.Apply == true, ItemCount = allItems.Count,
                Truncated = allItems.Count > items.Count, FolderPath = folderPath,
                Items = items.Select((item, index) => new ItemViewpointResult
                {
                    ItemName = item.DisplayName, Path = SearchService.PrintItemPath(item), ViewpointName = names[index],
                }).ToList(),
            };
            GroupItem folder;
            bool folderExists;
            int createdFolders;
            DocumentCommandService.TryResolveSavedViewpointFolder(document.SavedViewpoints, folderPath, false,
                out folder, out folderExists, out createdFolders);
            var conflict = folderExists && names.Any(name => DocumentCommandService.SavedItemExists(folder, name));
            if (conflict && request.Overwrite != true && response.Apply)
                throw new AgentCommandException(ErrorCodes.ViewpointNameConflict, "Viewpoint with the same name already exists in the target folder.");
            var markStyle = request.MarkStyle ?? "target";
            var style = string.IsNullOrWhiteSpace(markStyle) ? null : SelectionMarkupViewpointService.CreateStyle(
                request.EllipseColor, request.Thickness, null, null, null, null, null, markStyle,
                null, null, null, null, null, null, null);
            if (response.Truncated) response.Warnings.Add("The item limit truncated the viewpoint plan.");
            if (conflict) response.Warnings.Add(request.Overwrite == true
                ? "Existing viewpoints with matching names will be updated because overwrite=true."
                : "A viewpoint with the same name already exists in the target folder; apply=true will fail.");
            if (!response.Apply)
            {
                response.Message = "Dry-run: no viewpoints were saved and the active view was not changed.";
                return response;
            }

            using (var savedSelection = new ModelItemCollection())
            using (var savedViewpoint = document.CurrentViewpoint.CreateCopy())
            {
                savedSelection.AddRange(document.CurrentSelection.SelectedItems);
                var savedClipping = view.GetClippingPlanes();
                var savedRedlines = view.GetRedlines();
                try
                {
                    if (!DocumentCommandService.TryResolveSavedViewpointFolder(document.SavedViewpoints, folderPath, true,
                        out folder, out folderExists, out createdFolders))
                        throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to resolve the target viewpoint folder.");
                    for (var index = 0; index < items.Count; index++)
                    {
                        var item = items[index];
                        var result = response.Items[index];
                        try
                        {
                            using (var selection = new ModelItemCollection())
                            {
                                selection.Add(item);
                                document.CurrentSelection.CopyFrom(selection);
                            }
                            using (var box = item.BoundingBox())
                            {
                                if (box == null || box.IsEmpty) throw new InvalidOperationException("no bounding box");
                                var section = string.IsNullOrEmpty(request.Section) ? "none" : request.Section.ToLowerInvariant();
                                if (section == "plane")
                                {
                                    var plane = SectionPlaneRules.Resolve(new SectionPlaneSpec
                                    {
                                        Index = 1, Direction = "top", FromSelection = "max", OffsetMm = request.PlaneOffsetMm ?? 100,
                                    }, null, Vector(box.Min), Vector(box.Max), SectionBoxHelper.MmToDocUnits(1.0), out error);
                                    if (plane == null) throw new InvalidOperationException(error);
                                    SetClipping(view, SectionPlaneClippingJson.Write("", new[] { plane }, false, true));
                                }
                                else if (section == "box")
                                {
                                    using (var sectionBox = Expand(box, request.BoxOffsetMm ?? 1000))
                                        SectionBoxHelper.SetSectionBox(sectionBox);
                                }
                                else SectionBoxHelper.Disable();
                                using (var cameraBox = Expand(box, request.ContextMm ?? 2000))
                                    ViewpointCameraHelper.ApplyPresetViewToBox(document, cameraBox, request.Camera ?? "top");
                                var redlines = EmptyRedlines;
                                if (style != null)
                                {
                                    using (var camera = document.CurrentViewpoint.CreateCopy())
                                    {
                                        var geometry = SelectionMarkupViewpointService.BuildGeometry(new[] { item }, camera, view, style);
                                        redlines = geometry.MarkCount == 0 ? EmptyRedlines : geometry.RedlinesJson;
                                        result.MarkCount = geometry.MarkCount;
                                        if (geometry.MarkCount == 0)
                                            response.Warnings.Add("No mark could be projected for " + result.ViewpointName + ".");
                                    }
                                }
                                RedlineJsonSanitizer.SetSupportedRedlines(view, redlines, response.Warnings);
                                DocumentCommandService.SaveCurrentViewpointUsingCreateViewpointPath(document, folderPath,
                                    result.ViewpointName, request.Overwrite == true);
                                response.CreatedCount++;
                            }
                        }
                        catch (Exception exception) { result.Error = exception.Message; }
                    }
                }
                finally
                {
                    Restore(() => document.CurrentViewpoint.CopyFrom(savedViewpoint), "camera", response);
                    Restore(() => SetClipping(view, savedClipping), "clipping", response);
                    Restore(() => view.SetRedlines(savedRedlines), "redlines", response);
                    Restore(() => document.CurrentSelection.CopyFrom(savedSelection), "selection", response);
                }
            }
            response.Applied = response.CreatedCount > 0;
            response.Message = "Saved " + response.CreatedCount + " item viewpoints.";
            return response;
        }

        private static List<ModelItem> ResolveItems(Document document, IList<string> handles, MatchSessionStore store)
        {
            var items = new List<ModelItem>();
            var seen = new HashSet<ModelItem>();
            if (handles == null || handles.Count == 0)
                return document.CurrentSelection.SelectedItems.Cast<ModelItem>().Distinct().ToList();
            foreach (var handle in handles)
            {
                IList<ModelItem> matches;
                if (!store.TryGet(handle, out matches) || matches == null)
                    throw new AgentCommandException(ErrorCodes.StaleMatchReference,
                        "matchHandle is stale or was not found: " + handle + ". Re-run find_items and retry.");
                foreach (var item in matches)
                    if (item != null && seen.Add(item)) items.Add(item);
            }
            return items;
        }

        private static BoundingBox3D Expand(BoundingBox3D box, double millimetres)
        {
            var offset = SectionBoxHelper.MmToDocUnits(millimetres);
            return new BoundingBox3D(new Point3D(box.Min.X - offset, box.Min.Y - offset, box.Min.Z - offset),
                new Point3D(box.Max.X + offset, box.Max.Y + offset, box.Max.Z + offset));
        }

        private static BoxVector3 Vector(Point3D point) => new BoxVector3 { X = point.X, Y = point.Y, Z = point.Z };

        private static void SetClipping(Autodesk.Navisworks.Api.View view, string json)
        {
            if (!view.TrySetClippingPlanes(json)) view.SetClippingPlanes(json);
        }

        private static void Restore(Action action, string state, ItemViewpointsResponse response)
        {
            try { action(); }
            catch (Exception exception) { response.Warnings.Add("Could not restore " + state + ": " + exception.Message); }
        }
    }
}
