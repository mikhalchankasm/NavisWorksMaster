using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.ComApi;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        public CurrentViewpointInfoResponse CurrentViewpointInfo(Document document, CurrentViewpointInfoRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            var response = new CurrentViewpointInfoResponse
            {
                HasActiveView = document.ActiveView != null,
                HasCurrentViewpoint = document.CurrentViewpoint != null,
            };

            if (document.CurrentViewpoint == null)
                return response;

            var viewpoint = GetObjectProperty(document.CurrentViewpoint, "Value") ?? document.CurrentViewpoint.CreateCopy();
            response.Position = ToPoint3InfoFromObject(GetObjectProperty(viewpoint, "Position"));
            response.Rotation = ToRotationInfoFromObject(GetObjectProperty(viewpoint, "Rotation"));
            AddViewpointProperty(response.Properties, viewpoint, "Projection");
            AddViewpointProperty(response.Properties, viewpoint, "RenderStyle");
            AddViewpointProperty(response.Properties, viewpoint, "Lighting");
            AddViewpointProperty(response.Properties, viewpoint, "FieldOfView");
            AddViewpointProperty(response.Properties, viewpoint, "HeightField");
            AddViewpointProperty(response.Properties, viewpoint, "NearDistance");
            AddViewpointProperty(response.Properties, viewpoint, "FarDistance");
            AddViewpointProperty(response.Properties, viewpoint, "LinearSpeed");
            AddViewpointProperty(response.Properties, viewpoint, "AngularSpeed");

            return response;
        }

        public ListSavedViewpointsResponse ListSavedViewpoints(Document document, ListSavedViewpointsRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            request = request ?? new ListSavedViewpointsRequest();
            var limit = ClampSavedViewpointsLimit(request.Limit);
            var allItems = new List<SavedViewpointItemInfo>();

            if (document.SavedViewpoints != null && document.SavedViewpoints.RootItem != null)
            {
                allItems = BuildSavedViewpointNodes(document.SavedViewpoints.RootItem)
                    .Select(node => node.Info)
                    .ToList();
            }
            if (request.IncludeItemIds == false)
            {
                foreach (var item in allItems)
                    item.ItemId = null;
            }

            return new ListSavedViewpointsResponse
            {
                TotalItemCount = allItems.Count,
                ReturnedItemCount = Math.Min(allItems.Count, limit),
                Truncated = allItems.Count > limit,
                Items = allItems.Take(limit).ToList(),
            };
        }

        public ActivateSavedViewpointResponse ActivateSavedViewpoint(Document document, ActivateSavedViewpointRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (document.ActiveView == null || document.CurrentViewpoint == null)
                throw new AgentCommandException(ErrorCodes.NoActiveView, "There is no active view.");
            if (document.SavedViewpoints == null || document.SavedViewpoints.RootItem == null)
                throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, "The active document has no saved viewpoints.");
            if (string.IsNullOrWhiteSpace(request.PathOrName))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Saved viewpoint path or name is required.");

            var apply = request.Apply == true;
            var resolvedItem = ResolveSingleSavedItem(
                document.SavedViewpoints.RootItem,
                request.PathOrName,
                item => item is SavedViewpoint,
                ErrorCodes.SavedViewpointNotFound,
                "Saved viewpoint was not found.");
            var savedViewpoint = resolvedItem.Item as SavedViewpoint;

            if (apply)
            {
                document.SavedViewpoints.CurrentSavedViewpoint = savedViewpoint;
                document.CurrentViewpoint.CopyFrom(savedViewpoint.Viewpoint.CreateCopy());
                document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
            }

            return new ActivateSavedViewpointResponse
            {
                Apply = apply,
                Name = savedViewpoint.DisplayName ?? string.Empty,
                Path = resolvedItem.Path,
                Type = savedViewpoint.GetType().Name,
                Activated = apply ? (bool?)true : null,
            };
        }

        public CreateViewpointResponse CreateViewpoint(Document document, CreateViewpointRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var apply = request.Apply == true;
            if (string.IsNullOrWhiteSpace(request.Name))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Viewpoint name is required.");
            if (document.ActiveView == null || document.CurrentViewpoint == null)
                throw new AgentCommandException(ErrorCodes.NoActiveView, "There is no active view.");

            var sanitizedName = request.Name.Trim();
            var normalizedFolderPath = NormalizeFolderPath(request.FolderPath);
            var folderExists = true;
            var createdFolderCount = 0;
            GroupItem targetFolder;

            if (!TryResolveSavedViewpointFolder(document.SavedViewpoints, normalizedFolderPath, false, out targetFolder, out folderExists, out createdFolderCount))
                targetFolder = document.SavedViewpoints.RootItem;

            var nameConflict = SavedItemExists(targetFolder, sanitizedName);
            if (apply && nameConflict)
                throw new AgentCommandException(ErrorCodes.ViewpointNameConflict, "Viewpoint with the same name already exists in the target folder.");

            if (apply)
            {
                if (!TryResolveSavedViewpointFolder(document.SavedViewpoints, normalizedFolderPath, true, out targetFolder, out folderExists, out createdFolderCount))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to resolve the target viewpoint folder.");

                var savedViewpoint = new SavedViewpoint(document.CurrentViewpoint.CreateCopy())
                {
                    DisplayName = sanitizedName,
                };

                document.SavedViewpoints.AddCopy(targetFolder, savedViewpoint);
            }

            return new CreateViewpointResponse
            {
                Apply = apply,
                Name = sanitizedName,
                FolderPath = normalizedFolderPath,
                NameConflict = nameConflict,
                FolderExists = folderExists,
                CreatedFolderCount = apply ? (int?)createdFolderCount : null,
                Created = apply ? (bool?)true : null,
            };
        }

        public ZoomToSelectionResponse ZoomToSelection(Document document, ZoomToSelectionRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (document.ActiveView == null)
                throw new AgentCommandException(ErrorCodes.NoActiveView, "There is no active view.");

            var selectedItems = document.CurrentSelection.SelectedItems;
            if (selectedItems == null || selectedItems.Count == 0)
                throw new AgentCommandException(ErrorCodes.NoSelection, "There is no active selection.");
            var selectedCount = selectedItems.Count;
            var selectionBounds = selectedItems.BoundingBox();
            if (selectionBounds == null)
                throw new AgentCommandException(ErrorCodes.NoSelection, "Unable to determine selection bounds.");

            var viewpoint = document.CurrentViewpoint.CreateCopy();
            viewpoint.ZoomBox(selectionBounds);
            document.CurrentViewpoint.CopyFrom(viewpoint);
            document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);

            return new ZoomToSelectionResponse
            {
                SelectedItemCount = selectedCount,
                ZoomApplied = true,
            };
        }

        public FocusOnSelectionResponse FocusOnSelection(Document document, FocusOnSelectionRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (document.ActiveView == null)
                throw new AgentCommandException(ErrorCodes.NoActiveView, "There is no active view.");

            var selectedItems = document.CurrentSelection.SelectedItems;
            if (selectedItems == null || selectedItems.Count == 0)
                throw new AgentCommandException(ErrorCodes.NoSelection, "There is no active selection.");

            document.ActiveView.FocusOnCurrentSelection();
            document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);

            return new FocusOnSelectionResponse
            {
                SelectedItemCount = selectedItems.Count,
                FocusApplied = true,
            };
        }

        public FitAllResponse FitAll(Document document, FitAllRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (document.ActiveView == null)
                throw new AgentCommandException(ErrorCodes.NoActiveView, "There is no active view.");

            ExecuteComOperation(() => ComApiBridge.State.ViewAll());
            document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);

            return new FitAllResponse
            {
                FitApplied = true,
            };
        }

        private static void ExecuteComOperation(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                throw new AgentCommandException(ErrorCodes.CommandFailed, ex.Message);
            }
        }
    }
}
