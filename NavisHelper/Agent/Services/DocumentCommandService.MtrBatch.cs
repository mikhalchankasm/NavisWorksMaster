using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        private const int DefaultSelectionSetBatchMaxSetCount = 500;
        private const int DefaultLegacyBatchMaxClustersPerSet = 10;
        private const string DefaultViewpointNameTemplate = "{set} - {step}";

        public SelectionSetsBuildViewpointsResponse SelectionSetsBuildViewpoints(Document document, SelectionSetsBuildViewpointsRequest request)
        {
            ValidateSelectionSetBatchDocument(document);
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            var apply = request.Apply == true;
            var folderPrefix = NormalizeSelectionSetBatchFolderPrefix(request.FolderPrefix);
            var nameTemplate = NormalizeViewpointNameTemplate(request.NameTemplate);
            var steps = NormalizeSelectionSetViewpointSteps(request.Steps);
            var skipExisting = request.SkipExisting.GetValueOrDefault(true);
            var maxSetCount = NormalizeSelectionSetBatchMaxSetCount(request.MaxSetCount);
            var verbosity = NormalizeBatchVerbosity(request.Verbosity);
            var sourceSets = BuildSelectionSetNodes(document.SelectionSets.RootItem)
                .Where(node => node.Item is SelectionSet && IsSelectionSetBelowPrefix(node.Path, folderPrefix))
                .Take(maxSetCount + 1)
                .ToList();

            var response = new SelectionSetsBuildViewpointsResponse
            {
                Apply = apply,
                FolderPrefix = folderPrefix,
                NameTemplate = nameTemplate,
                Verbosity = verbosity,
                SelectionSetCount = Math.Min(sourceSets.Count, maxSetCount),
                Truncated = sourceSets.Count > maxSetCount,
            };
            if (response.Truncated)
            {
                sourceSets.RemoveAt(sourceSets.Count - 1);
                response.Warnings.Add("Processing was capped at " + maxSetCount.ToString() + " selection sets. Increase maxSetCount to process the remaining sets.");
            }

            var originalSelection = CopyCurrentSelection(document);
            try
            {
                foreach (var sourceSet in sourceSets)
                    ProcessSelectionSetViewpointSteps(document, sourceSet, nameTemplate, steps, skipExisting, apply, response);
            }
            finally
            {
                document.CurrentSelection.CopyFrom(originalSelection);
            }
            if (string.Equals(verbosity, "summary", StringComparison.Ordinal))
            {
                foreach (var item in response.Items)
                    foreach (var step in item.Steps)
                        foreach (var cluster in step.Clusters)
                        {
                            cluster.PreviewItemNames.Clear();
                            cluster.PreviewItemNamesTruncated = false;
                        }
            }
            return response;
        }

        public BuildMtrViewpointsResponse BuildMtrViewpoints(Document document, BuildMtrViewpointsRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var createPlan = request.CreatePlan.GetValueOrDefault(true);
            var createSectionBox = request.CreateSectionBox.GetValueOrDefault(true);
            if (!createPlan && !createSectionBox)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "At least one of createPlan or createSectionBox must be true.");

            var distanceMm = NormalizeClusterMaxDistanceMm(request.ClusterMaxDistanceMm) ?? 10000;
            var maxClusters = NormalizeLegacyMaxClusters(request.MaxClustersPerSet);
            var maxItems = request.MaxItemsForDistanceClustering.GetValueOrDefault(SelectionViewpointClusterService.DefaultMaxItemsForClustering);
            var steps = new List<SelectionSetViewpointStep>();
            if (createPlan)
            {
                steps.Add(new SelectionSetViewpointStep
                {
                    Step = SelectionSetViewpointStepKinds.Markup,
                    Label = "план",
                    EllipseColor = request.EllipseColor,
                    Thickness = request.Thickness,
                    PaddingFactor = request.PaddingFactor,
                    MarkStyle = request.MarkStyle,
                    ArrowCallout = request.ArrowCallout,
                    ArrowLengthMm = request.ArrowLengthMm,
                    TargetCrosshair = request.TargetCrosshair,
                    HatchAngleDeg = request.HatchAngleDeg,
                    HatchSpacingMm = request.HatchSpacingMm,
                    HatchSpacingPx = request.HatchSpacingPx,
                    HatchThickness = request.HatchThickness,
                    FitMarginFactor = request.FitMarginFactor,
                    MinMarkSizeMm = request.MinMarkSizeMm,
                    MarkSoloMinSizeMm = request.MarkSoloMinSizeMm,
                    MarkMergeGapMm = request.MarkMergeGapMm,
                    ClusterBy = SelectionClusterModes.Distance,
                    ClusterMaxDistanceMm = distanceMm,
                    MaxClusters = maxClusters,
                    MaxItemsForClustering = maxItems,
                });
            }
            if (createSectionBox)
            {
                steps.Add(new SelectionSetViewpointStep
                {
                    Step = SelectionSetViewpointStepKinds.SectionBox,
                    Label = "бокс",
                    BoxOffsetMm = request.BoxOffsetMm,
                    EllipseColor = request.EllipseColor,
                    Thickness = request.Thickness,
                    PaddingFactor = request.PaddingFactor,
                    MarkStyle = request.ArrowCallout.GetValueOrDefault(false) ? request.MarkStyle : null,
                    ArrowCallout = request.ArrowCallout,
                    ArrowLengthMm = request.ArrowLengthMm,
                    TargetCrosshair = request.TargetCrosshair,
                    HatchAngleDeg = request.HatchAngleDeg,
                    HatchSpacingMm = request.HatchSpacingMm,
                    HatchSpacingPx = request.HatchSpacingPx,
                    HatchThickness = request.HatchThickness,
                    MinMarkSizeMm = request.MinMarkSizeMm,
                    MarkSoloMinSizeMm = request.MarkSoloMinSizeMm,
                    MarkMergeGapMm = request.MarkMergeGapMm,
                    ClusterBy = SelectionClusterModes.Distance,
                    ClusterMaxDistanceMm = distanceMm,
                    MaxClusters = maxClusters,
                    MaxItemsForClustering = maxItems,
                });
            }

            var neutral = SelectionSetsBuildViewpoints(document, new SelectionSetsBuildViewpointsRequest
            {
                FolderPrefix = string.IsNullOrWhiteSpace(request.FolderPrefix) ? "MTR" : request.FolderPrefix,
                NameTemplate = "{set} — {step}",
                Steps = steps,
                SkipExisting = request.SkipExisting,
                MaxSetCount = request.MaxSetCount,
                Apply = request.Apply,
            });
            return ToLegacyBatchResponse(neutral);
        }

        private void ProcessSelectionSetViewpointSteps(
            Document document,
            SelectionSetNode sourceSet,
            string nameTemplate,
            IList<SelectionSetViewpointStep> steps,
            bool skipExisting,
            bool apply,
            SelectionSetsBuildViewpointsResponse response)
        {
            var itemResponse = new SelectionSetsBuildViewpointsItemResponse
            {
                SelectionSetPath = sourceSet.Path,
                FolderPath = sourceSet.ParentPath,
            };
            response.Items.Add(itemResponse);
            response.ProcessedSetCount++;
            try
            {
                var items = BuildSelectionSetItems(document, sourceSet.Item);
                itemResponse.SelectedItemCount = items.Count;
                if (items.Count == 0)
                {
                    itemResponse.SkippedEmpty = true;
                    itemResponse.Warnings.Add("Selection set resolved to zero model items; no viewpoints were created.");
                    response.SkippedEmptySetCount++;
                    return;
                }

                response.NonEmptySetCount++;
                document.CurrentSelection.CopyFrom(items);
                foreach (var step in steps)
                {
                    if (!StepMatchesItemCount(step, items.Count))
                    {
                        itemResponse.Steps.Add(new SelectionSetViewpointStepResponse
                        {
                            Step = step.Step,
                            Label = step.Label,
                            BaseName = ApplyViewpointNameTemplate(nameTemplate, sourceSet.Item.DisplayName, step.Label),
                            ClusterBy = step.ClusterBy,
                            SkippedByCondition = true,
                        });
                        response.SkippedConditionStepCount++;
                        continue;
                    }
                    ProcessSelectionSetViewpointStep(document, sourceSet, nameTemplate, step, skipExisting, apply, itemResponse, response);
                }
            }
            catch (Exception ex)
            {
                itemResponse.Warnings.Add("Set failed: " + ex.Message);
                response.Warnings.Add(sourceSet.Path + ": " + ex.Message);
            }
        }

        private void ProcessSelectionSetViewpointStep(
            Document document,
            SelectionSetNode sourceSet,
            string nameTemplate,
            SelectionSetViewpointStep step,
            bool skipExisting,
            bool apply,
            SelectionSetsBuildViewpointsItemResponse itemResponse,
            SelectionSetsBuildViewpointsResponse response)
        {
            var baseName = ApplyViewpointNameTemplate(nameTemplate, sourceSet.Item.DisplayName, step.Label);
            var probe = ExecuteSelectionSetViewpointStep(document, sourceSet.ParentPath, baseName, step, false);
            var stepResponse = new SelectionSetViewpointStepResponse
            {
                Step = step.Step,
                Label = step.Label,
                BaseName = baseName,
                ClusterBy = step.ClusterBy,
                ClusterCount = probe.Clusters.Count,
                DroppedClusterCount = probe.DroppedClusterCount,
                UncoveredItemCount = probe.UncoveredItemCount,
            };
            stepResponse.Clusters.AddRange(probe.Clusters);
            if (!apply)
                stepResponse.Warnings.AddRange(probe.Warnings);
            itemResponse.Steps.Add(stepResponse);
            if (!apply)
                return;

            if (skipExisting && step.Overwrite != true && AnyViewpointNameExists(document, sourceSet.ParentPath, probe.Clusters))
            {
                stepResponse.SkippedExisting = true;
                response.SkippedExistingStepCount++;
                return;
            }

            var created = ExecuteSelectionSetViewpointStep(document, sourceSet.ParentPath, baseName, step, true);
            stepResponse.Clusters.Clear();
            stepResponse.Clusters.AddRange(created.Clusters);
            stepResponse.ClusterCount = created.ClusterCount;
            stepResponse.DroppedClusterCount = created.DroppedClusterCount;
            stepResponse.UncoveredItemCount = created.UncoveredItemCount;
            stepResponse.CreatedViewpointCount = created.ClusterCount;
            stepResponse.MarkCount = created.MarkCount;
            stepResponse.SoloMarkCount = created.SoloMarkCount;
            stepResponse.MergedMarkCount = created.MergedMarkCount;
            stepResponse.EllipseCount = created.EllipseCount;
            stepResponse.ArrowCount = created.ArrowCount;
            stepResponse.SkippedItemCount = created.SkippedItemCount;
            stepResponse.Warnings.AddRange(created.Warnings);
            response.CreatedViewpointCount += created.ClusterCount;
        }

        private SelectionSetStepExecutionResult ExecuteSelectionSetViewpointStep(
            Document document,
            string folderPath,
            string baseName,
            SelectionSetViewpointStep step,
            bool apply)
        {
            if (string.Equals(step.Step, SelectionSetViewpointStepKinds.SectionBox, StringComparison.Ordinal))
            {
                var result = SectionBoxViewpoint(document, CreateSectionBoxStepRequest(baseName, folderPath, step, apply));
                return new SelectionSetStepExecutionResult(result.ClusterCount, result.Clusters, result.Warnings)
                {
                    DroppedClusterCount = result.DroppedClusterCount,
                    UncoveredItemCount = result.UncoveredItemCount,
                    MarkCount = result.MarkCount,
                    SoloMarkCount = result.SoloMarkCount,
                    MergedMarkCount = result.MergedMarkCount,
                    EllipseCount = result.EllipseCount,
                    ArrowCount = result.ArrowCount,
                    SkippedItemCount = result.SkippedItemCount,
                };
            }

            var markup = MarkupSelection(document, CreateMarkupStepRequest(baseName, folderPath, step, apply));
            return new SelectionSetStepExecutionResult(markup.ClusterCount, markup.Clusters, markup.Warnings)
            {
                DroppedClusterCount = markup.DroppedClusterCount,
                UncoveredItemCount = markup.UncoveredItemCount,
                MarkCount = markup.MarkCount,
                SoloMarkCount = markup.SoloMarkCount,
                MergedMarkCount = markup.MergedMarkCount,
                EllipseCount = markup.EllipseCount,
                ArrowCount = markup.ArrowCount,
                SkippedItemCount = markup.SkippedItemCount,
            };
        }

        private static MarkupSelectionRequest CreateMarkupStepRequest(string name, string folderPath, SelectionSetViewpointStep step, bool apply)
        {
            return new MarkupSelectionRequest
            {
                Name = name,
                FolderPath = folderPath,
                Source = MarkupSelectionCurrentSelectionSource,
                AutoTopView = true,
                FitToSelection = true,
                FitMarginFactor = step.FitMarginFactor,
                EllipseColor = step.EllipseColor,
                Thickness = step.Thickness,
                PaddingFactor = step.PaddingFactor,
                MarkStyle = step.MarkStyle,
                ArrowCallout = step.ArrowCallout,
                ArrowLengthMm = step.ArrowLengthMm,
                TargetCrosshair = step.TargetCrosshair,
                HatchAngleDeg = step.HatchAngleDeg,
                HatchSpacingMm = step.HatchSpacingMm,
                HatchSpacingPx = step.HatchSpacingPx,
                HatchThickness = step.HatchThickness,
                MinMarkSizeMm = step.MinMarkSizeMm,
                MinRadiusPixels = step.MinRadiusPixels,
                MarkSoloMinSizeMm = step.MarkSoloMinSizeMm,
                MarkMergeGapMm = step.MarkMergeGapMm,
                ClusterBy = string.Equals(step.Step, SelectionSetViewpointStepKinds.Overview, StringComparison.Ordinal) ? SelectionClusterModes.None : step.ClusterBy,
                ClusterMaxDistanceMm = step.ClusterMaxDistanceMm,
                ClusterTargetSize = step.ClusterTargetSize,
                ClusterCount = step.ClusterCount,
                ClusterGridSizeMm = step.ClusterGridSizeMm,
                MaxClusters = string.Equals(step.Step, SelectionSetViewpointStepKinds.Overview, StringComparison.Ordinal) ? 1 : step.MaxClusters,
                MaxItemsForClustering = step.MaxItemsForClustering,
                MaxItemsForDistanceClustering = step.MaxItemsForDistanceClustering,
                Overwrite = step.Overwrite,
                Apply = apply,
            };
        }

        private static SectionBoxViewpointRequest CreateSectionBoxStepRequest(string name, string folderPath, SelectionSetViewpointStep step, bool apply)
        {
            return new SectionBoxViewpointRequest
            {
                Name = name,
                FolderPath = folderPath,
                Source = MarkupSelectionCurrentSelectionSource,
                BoxOffsetMm = step.BoxOffsetMm,
                EllipseColor = step.EllipseColor,
                Thickness = step.Thickness,
                PaddingFactor = step.PaddingFactor,
                MarkStyle = step.MarkStyle,
                ArrowCallout = step.ArrowCallout,
                ArrowLengthMm = step.ArrowLengthMm,
                TargetCrosshair = step.TargetCrosshair,
                HatchAngleDeg = step.HatchAngleDeg,
                HatchSpacingMm = step.HatchSpacingMm,
                HatchSpacingPx = step.HatchSpacingPx,
                HatchThickness = step.HatchThickness,
                MinMarkSizeMm = step.MinMarkSizeMm,
                MinRadiusPixels = step.MinRadiusPixels,
                MarkSoloMinSizeMm = step.MarkSoloMinSizeMm,
                MarkMergeGapMm = step.MarkMergeGapMm,
                ClusterBy = step.ClusterBy,
                ClusterMaxDistanceMm = step.ClusterMaxDistanceMm,
                ClusterTargetSize = step.ClusterTargetSize,
                ClusterCount = step.ClusterCount,
                ClusterGridSizeMm = step.ClusterGridSizeMm,
                MaxClusters = step.MaxClusters,
                MaxItemsForClustering = step.MaxItemsForClustering,
                MaxItemsForDistanceClustering = step.MaxItemsForDistanceClustering,
                Overwrite = step.Overwrite,
                Apply = apply,
            };
        }

        private static List<SelectionSetViewpointStep> NormalizeSelectionSetViewpointSteps(IEnumerable<SelectionSetViewpointStep> source)
        {
            var steps = (source ?? Enumerable.Empty<SelectionSetViewpointStep>()).Where(step => step != null).ToList();
            if (steps.Count == 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "steps must contain at least one overview, markup, or sectionBox step.");
            foreach (var step in steps)
            {
                var kind = (step.Step ?? string.Empty).Trim().ToLowerInvariant();
                if (kind == "section_box") kind = SelectionSetViewpointStepKinds.SectionBox;
                if (kind != SelectionSetViewpointStepKinds.Overview && kind != SelectionSetViewpointStepKinds.Markup && kind != SelectionSetViewpointStepKinds.SectionBox)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "step must be overview, markup, or sectionBox.");
                step.Step = kind;
                step.Label = (step.Label ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(step.Label))
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "Every viewpoint step must have a non-empty label.");
                if (string.IsNullOrWhiteSpace(step.ClusterBy))
                    step.ClusterBy = SelectionClusterModes.None;
                if (kind == SelectionSetViewpointStepKinds.Overview)
                {
                    step.ClusterBy = SelectionClusterModes.None;
                    step.ClusterCount = null;
                    step.MaxClusters = 1;
                }
                if (step.WhenItemCountMin.HasValue && step.WhenItemCountMin.Value < 0)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "whenItemCountMin must be greater than or equal to 0.");
                if (step.WhenItemCountMax.HasValue && step.WhenItemCountMax.Value < 0)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "whenItemCountMax must be greater than or equal to 0.");
                if (step.WhenItemCountMin.HasValue && step.WhenItemCountMax.HasValue && step.WhenItemCountMin.Value > step.WhenItemCountMax.Value)
                    throw new AgentCommandException(ErrorCodes.SchemaViolation, "whenItemCountMin cannot be greater than whenItemCountMax.");
                if (kind == SelectionSetViewpointStepKinds.SectionBox && step.ArrowCallout.GetValueOrDefault(false) && string.IsNullOrWhiteSpace(step.MarkStyle))
                    step.MarkStyle = MarkupRedlineJsonBuilder.ArrowStyle;
                if (kind != SelectionSetViewpointStepKinds.SectionBox || HasSectionBoxMarkup(step))
                    step.MarkStyle = NormalizeNeutralMarkStyle(step.MarkStyle);
                SelectionViewpointClusterService.ValidateOptions(step);
            }
            var duplicateLabel = steps.GroupBy(step => step.Label, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
            if (duplicateLabel != null)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "Viewpoint step labels must be unique; duplicate label: " + duplicateLabel.Key + ".");
            return steps;
        }

        private static string NormalizeNeutralMarkStyle(string style)
        {
            try { return MarkupRedlineJsonBuilder.NormalizeStyle(style); }
            catch (ArgumentException ex) { throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message); }
        }

        private static bool HasSectionBoxMarkup(SelectionSetViewpointStep step)
        {
            return step != null && (step.ArrowCallout.GetValueOrDefault(false) || !string.IsNullOrWhiteSpace(step.MarkStyle));
        }

        private static string NormalizeSelectionSetBatchFolderPrefix(string value)
        {
            var normalized = NormalizeFolderPath(value);
            if (string.IsNullOrWhiteSpace(normalized))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "folderPrefix is required.");
            return normalized;
        }

        private static string NormalizeViewpointNameTemplate(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? DefaultViewpointNameTemplate : value.Trim();
            if (normalized.IndexOf("{set}", StringComparison.OrdinalIgnoreCase) < 0 || normalized.IndexOf("{step}", StringComparison.OrdinalIgnoreCase) < 0)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "nameTemplate must contain both {set} and {step}.");
            return normalized;
        }

        private static string ApplyViewpointNameTemplate(string template, string setName, string stepLabel)
        {
            return ReplaceTemplateToken(
                ReplaceTemplateToken(template, "{set}", (setName ?? string.Empty).Trim()),
                "{step}",
                stepLabel ?? string.Empty);
        }

        private static string ReplaceTemplateToken(string source, string token, string value)
        {
            var result = source ?? string.Empty;
            var start = result.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            while (start >= 0)
            {
                result = result.Substring(0, start) + value + result.Substring(start + token.Length);
                start = result.IndexOf(token, start + value.Length, StringComparison.OrdinalIgnoreCase);
            }
            return result;
        }

        private static int NormalizeSelectionSetBatchMaxSetCount(int? value)
        {
            var resolved = value.GetValueOrDefault(DefaultSelectionSetBatchMaxSetCount);
            if (resolved < 1 || resolved > 1000)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "maxSetCount must be from 1 to 1000.");
            return resolved;
        }

        private static string NormalizeBatchVerbosity(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "summary" : value.Trim().ToLowerInvariant();
            if (normalized != "summary" && normalized != "full")
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "verbosity must be summary or full.");
            return normalized;
        }

        private static bool StepMatchesItemCount(SelectionSetViewpointStep step, int itemCount)
        {
            return step != null &&
                (!step.WhenItemCountMin.HasValue || itemCount >= step.WhenItemCountMin.Value) &&
                (!step.WhenItemCountMax.HasValue || itemCount <= step.WhenItemCountMax.Value);
        }

        private static int NormalizeLegacyMaxClusters(int? value)
        {
            var resolved = value.GetValueOrDefault(DefaultLegacyBatchMaxClustersPerSet);
            if (resolved < 1 || resolved > 100)
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "maxClustersPerSet must be from 1 to 100.");
            return resolved;
        }

        private static void ValidateSelectionSetBatchDocument(Document document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (document.SelectionSets == null || document.SelectionSets.RootItem == null)
                throw new AgentCommandException(ErrorCodes.SelectionSetNotFound, "The active document has no selection sets collection.");
            if (document.ActiveView == null || document.CurrentViewpoint == null)
                throw new AgentCommandException(ErrorCodes.NoActiveView, "There is no active view.");
        }

        private static ModelItemCollection CopyCurrentSelection(Document document)
        {
            var result = new ModelItemCollection();
            if (document.CurrentSelection.SelectedItems != null)
            {
                foreach (ModelItem item in document.CurrentSelection.SelectedItems)
                    result.Add(item);
            }
            return result;
        }

        private static bool AnyViewpointNameExists(Document document, string folderPath, IList<SelectionViewpointClusterInfo> clusters)
        {
            if (document == null || clusters == null || clusters.Count == 0)
                return false;
            GroupItem folder;
            bool folderExists;
            int ignoredCreatedFolderCount;
            if (!TryResolveSavedViewpointFolder(document.SavedViewpoints, folderPath, false, out folder, out folderExists, out ignoredCreatedFolderCount) || !folderExists)
                return false;
            return clusters.Any(cluster => SavedItemExists(folder, cluster.ViewpointName));
        }

        private static bool IsSelectionSetBelowPrefix(string path, string folderPrefix)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                (string.Equals(path, folderPrefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(folderPrefix + "/", StringComparison.OrdinalIgnoreCase));
        }

        private static BuildMtrViewpointsResponse ToLegacyBatchResponse(SelectionSetsBuildViewpointsResponse source)
        {
            var result = new BuildMtrViewpointsResponse
            {
                Apply = source.Apply,
                FolderPrefix = source.FolderPrefix,
                SelectionSetCount = source.SelectionSetCount,
                ProcessedSetCount = source.ProcessedSetCount,
                NonEmptySetCount = source.NonEmptySetCount,
                SkippedEmptySetCount = source.SkippedEmptySetCount,
                Truncated = source.Truncated,
            };
            result.Warnings.AddRange(source.Warnings);
            foreach (var item in source.Items)
            {
                var legacy = new BuildMtrViewpointsItemResponse
                {
                    SelectionSetPath = item.SelectionSetPath,
                    FolderPath = item.FolderPath,
                    SelectedItemCount = item.SelectedItemCount,
                    SkippedEmpty = item.SkippedEmpty,
                };
                var markup = item.Steps.FirstOrDefault(step => step.Step == SelectionSetViewpointStepKinds.Markup);
                var section = item.Steps.FirstOrDefault(step => step.Step == SelectionSetViewpointStepKinds.SectionBox);
                var primary = markup ?? section;
                legacy.ClusterCount = primary == null ? 0 : primary.ClusterCount;
                if (primary != null) legacy.Clusters.AddRange(primary.Clusters);
                if (markup != null)
                {
                    legacy.PlanSkippedExisting = markup.SkippedExisting;
                    legacy.CreatedPlanViewpointCount = markup.CreatedViewpointCount;
                    result.CreatedPlanViewpointCount += markup.CreatedViewpointCount;
                    legacy.Warnings.AddRange(markup.Warnings);
                }
                if (section != null)
                {
                    legacy.SectionBoxSkippedExisting = section.SkippedExisting;
                    legacy.CreatedSectionBoxViewpointCount = section.CreatedViewpointCount;
                    result.CreatedSectionBoxViewpointCount += section.CreatedViewpointCount;
                    legacy.Warnings.AddRange(section.Warnings);
                }
                if (legacy.PlanSkippedExisting || legacy.SectionBoxSkippedExisting) result.SkippedExistingSetCount++;
                legacy.Warnings.AddRange(item.Warnings);
                result.Items.Add(legacy);
            }
            return result;
        }

        private sealed class SelectionSetStepExecutionResult
        {
            public SelectionSetStepExecutionResult(int clusterCount, IList<SelectionViewpointClusterInfo> clusters, IList<string> warnings)
            {
                ClusterCount = clusterCount;
                Clusters = clusters ?? new List<SelectionViewpointClusterInfo>();
                Warnings = warnings ?? new List<string>();
            }
            public int ClusterCount { get; private set; }
            public IList<SelectionViewpointClusterInfo> Clusters { get; private set; }
            public IList<string> Warnings { get; private set; }
            public int? MarkCount { get; set; }
            public int? SoloMarkCount { get; set; }
            public int? MergedMarkCount { get; set; }
            public int? EllipseCount { get; set; }
            public int? ArrowCount { get; set; }
            public int? SkippedItemCount { get; set; }
            public int DroppedClusterCount { get; set; }
            public int UncoveredItemCount { get; set; }
        }
    }
}
