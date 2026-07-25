using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Interop;
using NavisHelper.Agent.Contracts;
using NavisHelper.Core;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        private const string MarkupSelectionCurrentSelectionSource = "current_selection";

        public MarkupSelectionResponse MarkupSelection(Document document, MarkupSelectionRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (document.ActiveView == null || document.CurrentViewpoint == null)
                throw new AgentCommandException(ErrorCodes.NoActiveView, "There is no active view.");
            if (document.SavedViewpoints == null || document.SavedViewpoints.RootItem == null)
                throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, "The active document has no saved viewpoints collection.");

            var source = NormalizeMarkupSelectionSource(request.Source);
            var name = NormalizeMarkupViewpointName(request.Name);
            if (string.IsNullOrWhiteSpace(name))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Viewpoint name is required.");

            var selectedItems = document.CurrentSelection.SelectedItems;
            if (selectedItems == null || selectedItems.Count == 0)
                throw new AgentCommandException(ErrorCodes.NoSelection, "There is no active selection to mark up.");

            var apply = request.Apply == true;
            var overwrite = request.Overwrite == true;
            var autoTopView = request.AutoTopView.GetValueOrDefault(true);
            var fitToSelection = request.FitToSelection.GetValueOrDefault(true);
            var fitMarginFactor = NormalizeMarkupFitMarginFactor(request.FitMarginFactor);
            var folderPath = NormalizeFolderPath(request.FolderPath);
            var style = SelectionMarkupViewpointService.CreateStyle(
                request.EllipseColor,
                request.Thickness,
                request.PaddingFactor,
                request.MinRadiusPixels,
                request.MinMarkSizeMm,
                request.MarkSoloMinSizeMm,
                request.MarkMergeGapMm,
                request.MarkStyle,
                request.ArrowCallout,
                request.ArrowLengthMm,
                request.TargetCrosshair,
                request.HatchAngleDeg,
                request.HatchSpacingPx,
                request.HatchSpacingMm,
                request.HatchThickness);
            var clusterPlan = SelectionViewpointClusterService.Build(selectedItems.Cast<ModelItem>().ToList(), request);
            if (clusterPlan.Clusters.Count == 0 || clusterPlan.Clusters.Any(cluster => cluster.Bounds == null))
                throw new AgentCommandException(ErrorCodes.NoSelection, "Unable to determine the bounding box of the active selection.");
            foreach (var cluster in clusterPlan.Clusters)
                SelectionMarkupViewpointService.ValidateGroupingSafety(cluster.Items, style);
            var clusterCapApplied = clusterPlan.ClusterCapApplied;
            var usesArrowCallouts = style.ArrowCallout || string.Equals(style.MarkStyle, MarkupRedlineJsonBuilder.ArrowStyle, StringComparison.Ordinal);
            var effectiveFitMarginFactor = fitMarginFactor;
            if (fitToSelection && usesArrowCallouts)
            {
                // Reserve enough frame space for the padded mark plus the callout.
                // Explicit lengths may be clamped as high as 15% of HeightField.
                var arrowHeightFraction = style.ArrowLengthMm <= 1e-9 ? 0.08 : 0.15;
                var requiredFitMargin = (1.0 + style.PaddingFactor) / (0.97 - 2.0 * arrowHeightFraction) - 1.0;
                effectiveFitMarginFactor = Math.Max(effectiveFitMarginFactor, Math.Min(requiredFitMargin, 5.0));
            }

            var viewpointNames = clusterPlan.Clusters
                .Select((cluster, index) => BuildClusterViewpointName(name, index + 1, clusterPlan.Clusters.Count))
                .ToList();

            GroupItem targetFolder;
            bool folderExists;
            int createdFolderCount;
            if (!TryResolveSavedViewpointFolder(document.SavedViewpoints, folderPath, false, out targetFolder, out folderExists, out createdFolderCount))
                targetFolder = document.SavedViewpoints.RootItem;

            var nameConflict = folderExists && viewpointNames.Any(viewpointName => SavedItemExists(targetFolder, viewpointName));
            if (apply && overwrite && folderExists)
                ValidateOverwriteTargets(targetFolder, viewpointNames);
            if (apply && nameConflict && !overwrite)
                throw new AgentCommandException(ErrorCodes.ViewpointNameConflict, "Viewpoint with the same name already exists in the target folder.");

            var response = new MarkupSelectionResponse
            {
                Apply = apply,
                Name = name,
                FolderPath = folderPath,
                Path = string.IsNullOrWhiteSpace(folderPath) ? name : folderPath + "/" + name,
                Source = source,
                AutoTopView = autoTopView,
                FitToSelection = fitToSelection,
                FitMarginFactor = effectiveFitMarginFactor,
                MarkStyle = style.MarkStyle,
                ArrowCallout = style.ArrowCallout,
                ArrowLengthMm = style.ArrowLengthMm,
                TargetCrosshair = style.TargetCrosshair,
                MarkSoloMinSizeMm = style.MarkSoloMinSizeMm,
                MarkMergeGapMm = style.MarkMergeGapMm,
                SelectedItemCount = selectedItems.Count,
                NameConflict = nameConflict,
                Overwrite = overwrite,
                FolderExists = folderExists,
                ClusterBy = clusterPlan.ClusterBy,
                ClusterCount = clusterPlan.Clusters.Count,
                DroppedClusterCount = clusterPlan.DroppedClusterCount,
                UncoveredItemCount = clusterPlan.UncoveredItemCount,
            };
            AddSelectionClusterInfo(response.Clusters, clusterPlan.Clusters, viewpointNames, folderPath);
            foreach (var warning in clusterPlan.Warnings)
                response.Warnings.Add(warning);
            if (clusterCapApplied)
                response.Warnings.Add("Cluster output was capped; the arrowCallout setting was preserved without automatic override.");
            if (effectiveFitMarginFactor > fitMarginFactor + 1e-9)
                response.Warnings.Add("fitMarginFactor was increased to reserve visible frame space for arrow callouts.");

            if (!apply)
            {
                response.Warnings.Add("Dry-run did not change the active view or calculate projected frame geometry. Pass apply=true to create the top-view markup viewpoint.");
                if (nameConflict && !overwrite)
                    response.Warnings.Add("A viewpoint with the same name already exists in the target folder; apply=true will fail.");
                else if (nameConflict)
                    response.Warnings.Add("An existing viewpoint with the same name will be updated in place because overwrite=true.");
                return response;
            }

            if (!TryResolveSavedViewpointFolder(document.SavedViewpoints, folderPath, true, out targetFolder, out folderExists, out createdFolderCount))
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to resolve the target viewpoint folder.");

            for (var index = 0; index < clusterPlan.Clusters.Count; index++)
            {
                var cluster = clusterPlan.Clusters[index];
                ApplyMarkupSelectionCamera(document, cluster.Bounds, autoTopView, fitToSelection, effectiveFitMarginFactor);
                if (autoTopView)
                    SectionBoxHelper.Disable();

                // Build against the camera snapshot, not the live viewer. The latter
                // can still represent the previous cluster until Navisworks redraws.
                var geometry = SelectionMarkupViewpointService.BuildGeometry(cluster.Items, document.CurrentViewpoint.CreateCopy(), document.ActiveView, style);
                if (geometry.MarkCount == 0)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "No selected items in cluster " + (index + 1).ToString() + " could be projected into the active camera for markup.");

                RedlineJsonSanitizer.SetSupportedRedlines(document.ActiveView, geometry.RedlinesJson, response.Warnings);
                document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
                SaveCurrentViewpointUsingCreateViewpointPath(document, folderPath, viewpointNames[index], overwrite);
                response.MarkCount = response.MarkCount.GetValueOrDefault() + geometry.MarkCount;
                response.SoloMarkCount = response.SoloMarkCount.GetValueOrDefault() + geometry.SoloMarkCount;
                response.MergedMarkCount = response.MergedMarkCount.GetValueOrDefault() + geometry.MergedMarkCount;
                response.EllipseCount = response.EllipseCount.GetValueOrDefault() + geometry.EllipseCount;
                response.ArrowCount = response.ArrowCount.GetValueOrDefault() + geometry.ArrowCount;
                if (usesArrowCallouts && geometry.ArrowCount < geometry.MarkCount)
                    AddUniqueWarning(response.Warnings, "Some arrow callouts were omitted because the active camera had insufficient visible frame space.");
                response.SkippedItemCount = response.SkippedItemCount.GetValueOrDefault() + geometry.SkippedItemCount;
            }

            response.CreatedFolderCount = createdFolderCount;
            response.Created = true;
            response.Warnings.Add("Viewpoint was saved through the create_viewpoint-compatible path and then updated with the active redlines. Verify redlines by activating it in a clean session.");
            if (response.SkippedItemCount.GetValueOrDefault() > 0)
                response.Warnings.Add(response.SkippedItemCount.Value.ToString() + " selected item(s) had no usable bounding box or were outside the camera projection and received no frame.");
            return response;
        }

        public LiveMarkersResponse LiveMarkers(Document document, LiveMarkersRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var apply = request.Apply == true;
            var visible = request.Visible.GetValueOrDefault(true);
            var style = NormalizeLiveMarkerStyle(request.Style);
            var markerRadiusPixels = request.MarkerRadiusPixels.GetValueOrDefault(10);
            if (markerRadiusPixels < 5 || markerRadiusPixels > 200)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "markerRadiusPixels must be from 5 to 200.");
            var markSoloMinSizeMm = NormalizeNonNegativeMarkupDistanceMm(request.MarkSoloMinSizeMm, 1500, "markSoloMinSizeMm");
            var markMergeGapMm = NormalizeNonNegativeMarkupDistanceMm(request.MarkMergeGapMm, 1000, "markMergeGapMm");
            var selectedItems = document.CurrentSelection == null
                ? new List<ModelItem>()
                : document.CurrentSelection.SelectedItems.Cast<ModelItem>().Where(item => item != null).ToList();

            var response = new LiveMarkersResponse
            {
                Apply = apply,
                Visible = visible,
                Active = global::NavisHelper.ClashMarkerTool.IsActive,
                Persistent = false,
                Style = style,
                MarkerRadiusPixels = markerRadiusPixels,
                MarkSoloMinSizeMm = markSoloMinSizeMm,
                MarkMergeGapMm = markMergeGapMm,
                SelectedItemCount = selectedItems.Count,
            };

            if (!visible)
            {
                if (apply)
                    global::NavisHelper.ClashMarkerTool.Hide();
                response.Active = apply ? false : global::NavisHelper.ClashMarkerTool.IsActive;
                response.Warnings.Add("Live overlay markers are runtime-only and are not stored in saved viewpoints or Navisworks files.");
                return response;
            }

            if (selectedItems.Count == 0)
                throw new AgentCommandException(ErrorCodes.NoSelection, "Select at least one model item before planning live markers.");

            var bounds = new List<MarkupFrameBounds>();
            foreach (var item in selectedItems)
            {
                try
                {
                    var box = item.BoundingBox();
                    if (box == null)
                    {
                        response.SkippedItemCount++;
                        continue;
                    }

                    bounds.Add(new MarkupFrameBounds
                    {
                        MinX = box.Min.X,
                        MinY = box.Min.Y,
                        MinZ = box.Min.Z,
                        MaxX = box.Max.X,
                        MaxY = box.Max.Y,
                        MaxZ = box.Max.Z,
                    });
                }
                catch
                {
                    response.SkippedItemCount++;
                }
            }

            var groups = MarkupFrameGroupingHelper.Group(
                bounds,
                SectionBoxHelper.MmToDocUnits(markSoloMinSizeMm),
                SectionBoxHelper.MmToDocUnits(markMergeGapMm));
            if (groups.Count == 0)
                throw new AgentCommandException(ErrorCodes.CommandFailed, "No selected items had usable bounding boxes for live markers.");
            var visuals = groups.Select(group =>
            {
                var box = new BoundingBox3D(
                    new Point3D(group.Bounds.MinX, group.Bounds.MinY, group.Bounds.MinZ),
                    new Point3D(group.Bounds.MaxX, group.Bounds.MaxY, group.Bounds.MaxZ));
                var center = new Point3D(
                    (group.Bounds.MinX + group.Bounds.MaxX) / 2,
                    (group.Bounds.MinY + group.Bounds.MaxY) / 2,
                    (group.Bounds.MinZ + group.Bounds.MaxZ) / 2);
                return new global::NavisHelper.ClashMarkerVisual(center, box);
            }).ToList();

            response.MarkerCount = visuals.Count;
            response.SoloMarkerCount = groups.Count(group => group.IsSolo);
            response.MergedMarkerCount = groups.Count(group => !group.IsSolo);
            if (apply)
            {
                global::NavisHelper.ClashMarkerTool.ShowMany(visuals, style, markerRadiusPixels);
                if (!string.IsNullOrWhiteSpace(global::NavisHelper.ClashMarkerTool.LastError))
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Failed to activate live markers: " + global::NavisHelper.ClashMarkerTool.LastError);
                response.Active = global::NavisHelper.ClashMarkerTool.IsActive;
            }

            response.Warnings.Add("Live overlay markers are runtime-only and are not stored in saved viewpoints or Navisworks files. Use markup_selection for persistent deliverables.");
            return response;
        }

        public SectionBoxViewpointResponse SectionBoxViewpoint(Document document, SectionBoxViewpointRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (document.ActiveView == null || document.CurrentViewpoint == null)
                throw new AgentCommandException(ErrorCodes.NoActiveView, "There is no active view.");
            if (document.SavedViewpoints == null || document.SavedViewpoints.RootItem == null)
                throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, "The active document has no saved viewpoints collection.");

            var source = NormalizeMarkupSelectionSource(request.Source);
            var name = NormalizeMarkupViewpointName(request.Name);
            if (string.IsNullOrWhiteSpace(name))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Viewpoint name is required.");

            var selectedItems = document.CurrentSelection.SelectedItems;
            if (selectedItems == null || selectedItems.Count == 0)
                throw new AgentCommandException(ErrorCodes.NoSelection, "There is no active selection to section.");

            var apply = request.Apply == true;
            var overwrite = request.Overwrite == true;
            var folderPath = NormalizeFolderPath(request.FolderPath);
            var boxOffsetMm = NormalizeNonNegativeMarkupDistanceMm(request.BoxOffsetMm, 1000, "boxOffsetMm");
            var hasMarkup = request.ArrowCallout.GetValueOrDefault(false) || !string.IsNullOrWhiteSpace(request.MarkStyle);
            var style = hasMarkup
                ? SelectionMarkupViewpointService.CreateStyle(
                    request.EllipseColor,
                    request.Thickness,
                    request.PaddingFactor,
                    request.MinRadiusPixels,
                    request.MinMarkSizeMm,
                    request.MarkSoloMinSizeMm,
                    request.MarkMergeGapMm,
                    request.ArrowCallout.GetValueOrDefault(false) && string.IsNullOrWhiteSpace(request.MarkStyle)
                        ? MarkupRedlineJsonBuilder.ArrowStyle
                        : request.MarkStyle,
                    request.ArrowCallout,
                    request.ArrowLengthMm,
                    request.TargetCrosshair,
                    request.HatchAngleDeg,
                    request.HatchSpacingPx,
                    request.HatchSpacingMm,
                    request.HatchThickness)
                : null;
            var clusterPlan = SelectionViewpointClusterService.Build(selectedItems.Cast<ModelItem>().ToList(), request);
            if (clusterPlan.Clusters.Count == 0 || clusterPlan.Clusters.Any(cluster => cluster.Bounds == null))
                throw new AgentCommandException(ErrorCodes.NoSelection, "Unable to determine the bounding box of the active selection.");
            if (style != null)
            {
                foreach (var cluster in clusterPlan.Clusters)
                    SelectionMarkupViewpointService.ValidateGroupingSafety(cluster.Items, style);
            }

            var viewpointNames = clusterPlan.Clusters
                .Select((cluster, index) => BuildClusterViewpointName(name, index + 1, clusterPlan.Clusters.Count))
                .ToList();

            GroupItem targetFolder;
            bool folderExists;
            int createdFolderCount;
            if (!TryResolveSavedViewpointFolder(document.SavedViewpoints, folderPath, false, out targetFolder, out folderExists, out createdFolderCount))
                targetFolder = document.SavedViewpoints.RootItem;

            var nameConflict = folderExists && viewpointNames.Any(viewpointName => SavedItemExists(targetFolder, viewpointName));
            if (apply && overwrite && folderExists)
                ValidateOverwriteTargets(targetFolder, viewpointNames);
            if (apply && nameConflict && !overwrite)
                throw new AgentCommandException(ErrorCodes.ViewpointNameConflict, "Viewpoint with the same name already exists in the target folder.");

            var response = new SectionBoxViewpointResponse
            {
                Apply = apply,
                Name = name,
                FolderPath = folderPath,
                Path = string.IsNullOrWhiteSpace(folderPath) ? name : folderPath + "/" + name,
                Source = source,
                BoxOffsetMm = boxOffsetMm,
                SelectedItemCount = selectedItems.Count,
                NameConflict = nameConflict,
                Overwrite = overwrite,
                FolderExists = folderExists,
                ClusterBy = clusterPlan.ClusterBy,
                ClusterCount = clusterPlan.Clusters.Count,
                DroppedClusterCount = clusterPlan.DroppedClusterCount,
                UncoveredItemCount = clusterPlan.UncoveredItemCount,
                MarkStyle = style == null ? null : style.MarkStyle,
                ArrowCallout = style != null && style.ArrowCallout,
            };
            AddSelectionClusterInfo(response.Clusters, clusterPlan.Clusters, viewpointNames, folderPath);
            foreach (var warning in clusterPlan.Warnings)
                response.Warnings.Add(warning);

            if (!apply)
            {
                response.Warnings.Add("Dry-run did not change the active view or section box. Pass apply=true to create section-box viewpoints without hiding or isolating items.");
                if (nameConflict && !overwrite)
                    response.Warnings.Add("A viewpoint with the same name already exists in the target folder; apply=true will fail.");
                else if (nameConflict)
                    response.Warnings.Add("An existing viewpoint with the same name will be updated in place because overwrite=true.");
                return response;
            }

            if (!TryResolveSavedViewpointFolder(document.SavedViewpoints, folderPath, true, out targetFolder, out folderExists, out createdFolderCount))
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Unable to resolve the target viewpoint folder.");

            var boxOffsetUnits = SectionBoxHelper.MmToDocUnits(boxOffsetMm);
            for (var index = 0; index < clusterPlan.Clusters.Count; index++)
            {
                var box = ExpandBoundingBox(clusterPlan.Clusters[index].Bounds, boxOffsetUnits);
                ViewpointCameraHelper.ApplyIso1ViewToBox(document, box, box.Center);
                SectionBoxHelper.SetSectionBox(box);
                if (style == null)
                {
                    RedlineJsonSanitizer.SetSupportedRedlines(document.ActiveView, "{\"Type\":\"RedlineCollection\",\"Version\":1,\"Values\":[]}", response.Warnings);
                }
                else
                {
                    var geometry = SelectionMarkupViewpointService.BuildGeometry(clusterPlan.Clusters[index].Items, document.CurrentViewpoint.CreateCopy(), document.ActiveView, style);
                    if (geometry.MarkCount == 0)
                    {
                        RedlineJsonSanitizer.SetSupportedRedlines(document.ActiveView, "{\"Type\":\"RedlineCollection\",\"Version\":1,\"Values\":[]}", response.Warnings);
                        response.Warnings.Add("Section-box cluster " + (index + 1).ToString() + " was saved without markup because no selected item could be projected into the final camera.");
                    }
                    else
                    {
                        RedlineJsonSanitizer.SetSupportedRedlines(document.ActiveView, geometry.RedlinesJson, response.Warnings);
                        response.MarkCount = response.MarkCount.GetValueOrDefault() + geometry.MarkCount;
                        response.SoloMarkCount = response.SoloMarkCount.GetValueOrDefault() + geometry.SoloMarkCount;
                        response.MergedMarkCount = response.MergedMarkCount.GetValueOrDefault() + geometry.MergedMarkCount;
                        response.EllipseCount = response.EllipseCount.GetValueOrDefault() + geometry.EllipseCount;
                        response.ArrowCount = response.ArrowCount.GetValueOrDefault() + geometry.ArrowCount;
                        if ((style.ArrowCallout || string.Equals(style.MarkStyle, MarkupRedlineJsonBuilder.ArrowStyle, StringComparison.Ordinal)) && geometry.ArrowCount < geometry.MarkCount)
                            AddUniqueWarning(response.Warnings, "Some arrow callouts were omitted because the final section-box camera had insufficient visible frame space.");
                        response.SkippedItemCount = response.SkippedItemCount.GetValueOrDefault() + geometry.SkippedItemCount;
                    }
                }
                document.ActiveView.RequestDelayedRedraw(ViewRedrawRequests.All);
                SaveCurrentViewpointUsingCreateViewpointPath(document, folderPath, viewpointNames[index], overwrite);
            }

            response.CreatedFolderCount = createdFolderCount;
            response.Created = true;
            response.Warnings.Add("Viewpoint was saved through the create_viewpoint-compatible path. Verify persisted clipping by activating it in a clean session.");
            return response;
        }

        private static void SaveCurrentViewpointUsingCreateViewpointPath(Document document, string folderPath, string name, bool overwrite)
        {
            try
            {
                GroupItem targetFolder;
                bool folderExists;
                int ignoredCreatedFolderCount;
                if (!TryResolveSavedViewpointFolder(document.SavedViewpoints, folderPath, false, out targetFolder, out folderExists, out ignoredCreatedFolderCount) || !folderExists)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "The target viewpoint folder could not be resolved before saving.");

                var existing = targetFolder.Children
                    .OfType<SavedViewpoint>()
                    .Where(item => string.Equals(item.DisplayName, name, StringComparison.Ordinal))
                    .ToList();
                if (existing.Count > 1)
                    throw new AgentCommandException(ErrorCodes.SavedItemAmbiguous, "More than one saved viewpoint with the target name exists. Delete duplicates or use saved_viewpoints_manage before overwrite.");

                var updateExisting = overwrite && existing.Count == 1;
                SavedViewpoint savedViewpoint = null;
                if (!updateExisting)
                {
                    // AddCopy is the proven create_viewpoint path. Do not call
                    // ReplaceFromCurrentView, CurrentSavedViewpoint, or Move here.
                    document.SavedViewpoints.AddCopy(targetFolder, new SavedViewpoint(document.CurrentViewpoint.CreateCopy())
                    {
                        DisplayName = name,
                    });
                }

                GroupItem liveTargetFolder;
                bool liveFolderExists;
                int ignoredLiveCreatedFolderCount;
                if (!TryResolveSavedViewpointFolder(document.SavedViewpoints, folderPath, false, out liveTargetFolder, out liveFolderExists, out ignoredLiveCreatedFolderCount) || !liveFolderExists)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "The target viewpoint folder could not be re-resolved after creation.");

                if (updateExisting)
                {
                    savedViewpoint = liveTargetFolder.Children
                        .OfType<SavedViewpoint>()
                        .SingleOrDefault(item => string.Equals(item.DisplayName, name, StringComparison.Ordinal));
                }
                else
                {
                    savedViewpoint = liveTargetFolder.Children
                        .OfType<SavedViewpoint>()
                        .LastOrDefault(item => string.Equals(item.DisplayName, name, StringComparison.Ordinal));
                }
                if (savedViewpoint == null)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "The saved viewpoint could not be resolved after creation.");

                // Redlines belong to the active View rather than Viewpoint. This internal
                // update path captures the active view into the saved item without changing
                // CurrentSavedViewpoint (which fails in the MCP host context).
                var updateStatus = LcOpSavedViewsElement.TryUpdateViewCopy(document.State, savedViewpoint);
                if (updateStatus != LcOpSavedItemsElementStatus.eSTATUS_OK)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Navisworks could not persist the active view state: " + updateStatus + ".");
            }
            catch (Exception ex)
            {
                Logger.Error("MTR saved viewpoint creation failed: " + ex, "MtrViewpoint");
                throw;
            }
        }

        private static void ValidateOverwriteTargets(GroupItem targetFolder, IEnumerable<string> viewpointNames)
        {
            if (targetFolder == null)
                return;
            foreach (var name in viewpointNames ?? Enumerable.Empty<string>())
            {
                var matches = targetFolder.Children
                    .OfType<SavedViewpoint>()
                    .Count(item => string.Equals(item.DisplayName, name, StringComparison.Ordinal));
                if (matches > 1)
                    throw new AgentCommandException(ErrorCodes.SavedItemAmbiguous, "More than one saved viewpoint named '" + name + "' exists. Delete duplicates before overwrite.");
            }
        }

        private static void AddSelectionClusterInfo(
            IList<SelectionViewpointClusterInfo> target,
            IList<SelectionViewpointCluster> clusters,
            IList<string> viewpointNames,
            string folderPath)
        {
            if (target == null || clusters == null || viewpointNames == null)
                return;

            const int previewLimit = 5;
            for (var index = 0; index < clusters.Count && index < viewpointNames.Count; index++)
            {
                var names = clusters[index].Items
                    .Select(item => string.IsNullOrWhiteSpace(item.DisplayName) ? item.ClassDisplayName : item.DisplayName)
                    .Where(itemName => !string.IsNullOrWhiteSpace(itemName))
                    .Take(previewLimit + 1)
                    .ToList();
                var truncated = names.Count > previewLimit;
                if (truncated)
                    names.RemoveAt(names.Count - 1);

                target.Add(new SelectionViewpointClusterInfo
                {
                    Index = index + 1,
                    ItemCount = clusters[index].Items.Count,
                    ViewpointName = viewpointNames[index],
                    ViewpointPath = string.IsNullOrWhiteSpace(folderPath) ? viewpointNames[index] : folderPath + "/" + viewpointNames[index],
                    PreviewItemNames = names,
                    PreviewItemNamesTruncated = truncated,
                });
            }
        }

        private static string BuildClusterViewpointName(string name, int clusterIndex, int clusterCount)
        {
            return clusterCount <= 1
                ? name
                : name + " (" + clusterIndex.ToString() + ")";
        }

        private static double? NormalizeClusterMaxDistanceMm(double? value)
        {
            if (!value.HasValue)
                return null;
            if (double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value < 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "clusterMaxDistanceMm must be a finite value greater than or equal to 0.");
            return value.Value;
        }

        private static double NormalizeNonNegativeMarkupDistanceMm(double? value, double fallback, string parameterName)
        {
            var resolved = value.GetValueOrDefault(fallback);
            if (double.IsNaN(resolved) || double.IsInfinity(resolved) || resolved < 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, parameterName + " must be a finite value greater than or equal to 0.");
            return resolved;
        }

        private static double NormalizeMarkupFitMarginFactor(double? value)
        {
            var resolved = value.GetValueOrDefault(0.10);
            if (double.IsNaN(resolved) || double.IsInfinity(resolved) || resolved < 0 || resolved > 5)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "fitMarginFactor must be a finite value from 0 to 5.");
            return resolved;
        }

        private static BoundingBox3D ExpandBoundingBox(BoundingBox3D bounds, double offset)
        {
            if (bounds == null)
                throw new ArgumentNullException(nameof(bounds));

            return new BoundingBox3D(
                new Point3D(bounds.Min.X - offset, bounds.Min.Y - offset, bounds.Min.Z - offset),
                new Point3D(bounds.Max.X + offset, bounds.Max.Y + offset, bounds.Max.Z + offset));
        }

        private static void ApplyMarkupSelectionCamera(Document document, BoundingBox3D selectionBounds, bool autoTopView, bool fitToSelection, double fitMarginFactor)
        {
            var fitBounds = ExpandBoundingBoxByFactor(selectionBounds, fitMarginFactor);
            if (autoTopView)
            {
                // Persist a canonical square camera. A redline's camera-space
                // coordinates then remain tied to this viewpoint rather than to
                // the MCP host window aspect ratio.
                var viewpoint = document.CurrentViewpoint.CreateCopy();
                viewpoint.Rotation = new Rotation3D(0, 0, 0, -1);
                viewpoint.Projection = ViewpointProjection.Orthographic;
                viewpoint.AspectRatio = 1.0;
                if (fitToSelection)
                    viewpoint.ZoomBox(fitBounds);
                document.CurrentViewpoint.CopyFrom(viewpoint);
            }
            else if (fitToSelection)
            {
                var viewpoint = document.CurrentViewpoint.CreateCopy();
                viewpoint.ZoomBox(fitBounds);
                document.CurrentViewpoint.CopyFrom(viewpoint);
            }

            // Geometry is computed from CurrentViewpoint, so no redraw is required
            // as a synchronization barrier before redline projection.
        }

        private static BoundingBox3D ExpandBoundingBoxByFactor(BoundingBox3D bounds, double factor)
        {
            if (bounds == null)
                throw new ArgumentNullException(nameof(bounds));
            if (factor <= 0)
                return bounds;

            var x = (bounds.Max.X - bounds.Min.X) * factor / 2.0;
            var y = (bounds.Max.Y - bounds.Min.Y) * factor / 2.0;
            var z = (bounds.Max.Z - bounds.Min.Z) * factor / 2.0;
            return new BoundingBox3D(
                new Point3D(bounds.Min.X - x, bounds.Min.Y - y, bounds.Min.Z - z),
                new Point3D(bounds.Max.X + x, bounds.Max.Y + y, bounds.Max.Z + z));
        }

        private static string NormalizeMarkupSelectionSource(string source)
        {
            var normalized = string.IsNullOrWhiteSpace(source) ? MarkupSelectionCurrentSelectionSource : source.Trim();
            if (!string.Equals(normalized, MarkupSelectionCurrentSelectionSource, StringComparison.OrdinalIgnoreCase))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "markup_selection currently supports source=current_selection only.");
            return MarkupSelectionCurrentSelectionSource;
        }

        private static string NormalizeLiveMarkerStyle(string style)
        {
            try
            {
                return MarkupRedlineJsonBuilder.NormalizeStyle(string.IsNullOrWhiteSpace(style) ? MarkupRedlineJsonBuilder.TargetStyle : style);
            }
            catch (ArgumentException ex)
            {
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }
        }

        private static string NormalizeMarkupViewpointName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            return name.Trim().Replace('/', ' ').Replace('\\', ' ');
        }
    }
}
