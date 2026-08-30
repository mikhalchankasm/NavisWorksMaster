using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public static class WorldMarkerLifecyclePlanHelper
    {
        public const string Replace = "replace";
        public const string Delete = "delete";
        public const string Hide = "hide";
        public const string Show = "show";

        public static WorldMarkerLifecyclePlan PlanReplace(
            string markerId,
            IEnumerable<WorldMarkerModelDescriptor> currentModels,
            string managedRoot,
            string newArtifactPath)
        {
            ValidateMarkerId(markerId);
            if (!WorldMarkerArtifactPathPolicy.IsCleanupCandidate(managedRoot, newArtifactPath) ||
                !string.Equals(GetArtifactMarkerId(newArtifactPath), markerId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("newArtifactPath must be a generated DXF for markerId inside the managed root.", nameof(newArtifactPath));
            }
            var models = NormalizeModels(currentModels);
            var targets = models.Where(model => string.Equals(model.MarkerId, markerId, StringComparison.OrdinalIgnoreCase)).ToList();
            var plan = BuildPlan(Replace, markerId, targets, managedRoot, true);
            plan.NewArtifactPath = Path.GetFullPath(newArtifactPath.Trim());
            plan.CleanupArtifactPaths = plan.CleanupArtifactPaths
                .Where(path => !string.Equals(path, plan.NewArtifactPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return plan;
        }

        public static WorldMarkerLifecyclePlan PlanDelete(
            IEnumerable<string> markerIds,
            IEnumerable<WorldMarkerModelDescriptor> currentModels,
            string managedRoot)
        {
            var requested = NormalizeMarkerIds(markerIds);
            var models = NormalizeModels(currentModels);
            var requestedSet = new HashSet<string>(requested, StringComparer.OrdinalIgnoreCase);
            var targets = models.Where(model => requestedSet.Contains(model.MarkerId)).ToList();
            var plan = BuildPlan(Delete, string.Empty, targets, managedRoot, false);
            var found = new HashSet<string>(targets.Select(model => model.MarkerId), StringComparer.OrdinalIgnoreCase);
            plan.MissingMarkerIds = requested.Where(markerId => !found.Contains(markerId)).ToList();
            return plan;
        }

        public static WorldMarkerLifecyclePlan PlanVisibility(
            string operation,
            IEnumerable<string> markerIds,
            IEnumerable<WorldMarkerModelDescriptor> currentModels)
        {
            var normalizedOperation = (operation ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedOperation != Hide && normalizedOperation != Show)
                throw new ArgumentException("operation must be hide or show.", nameof(operation));

            var requested = NormalizeMarkerIds(markerIds);
            var models = NormalizeModels(currentModels);
            var requestedSet = new HashSet<string>(requested, StringComparer.OrdinalIgnoreCase);
            var targets = models.Where(model => requestedSet.Contains(model.MarkerId)).OrderBy(model => model.ModelIndex).ToList();
            var found = new HashSet<string>(targets.Select(model => model.MarkerId), StringComparer.OrdinalIgnoreCase);
            return new WorldMarkerLifecyclePlan
            {
                Operation = normalizedOperation,
                TargetHidden = normalizedOperation == Hide,
                TargetModels = targets,
                MissingMarkerIds = requested.Where(markerId => !found.Contains(markerId)).ToList(),
            };
        }

        private static WorldMarkerLifecyclePlan BuildPlan(
            string operation,
            string markerId,
            IList<WorldMarkerModelDescriptor> targets,
            string managedRoot,
            bool appendNewFirst)
        {
            var orderedTargets = targets.OrderBy(model => model.ModelIndex).ToList();
            var cleanup = orderedTargets
                .Select(model => model.ArtifactPath)
                .Where(path => WorldMarkerArtifactPathPolicy.IsCleanupCandidate(managedRoot, path))
                .Select(path => Path.GetFullPath(path.Trim()))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new WorldMarkerLifecyclePlan
            {
                Operation = operation,
                MarkerId = markerId,
                AppendNewBeforeDeletingExisting = appendNewFirst,
                TargetModels = orderedTargets,
                DeleteModelIndices = orderedTargets.Select(model => model.ModelIndex).OrderByDescending(index => index).ToList(),
                CleanupArtifactPaths = cleanup,
            };
        }

        private static List<WorldMarkerModelDescriptor> NormalizeModels(IEnumerable<WorldMarkerModelDescriptor> currentModels)
        {
            var models = currentModels == null
                ? new List<WorldMarkerModelDescriptor>()
                : currentModels.Where(model => model != null).ToList();
            var indices = new HashSet<int>();
            foreach (var model in models)
            {
                if (model.ModelIndex < 0)
                    throw new ArgumentException("Model indices must be non-negative.", nameof(currentModels));
                if (!indices.Add(model.ModelIndex))
                    throw new ArgumentException("Model indices must be unique.", nameof(currentModels));
                ValidateMarkerId(model.MarkerId);
            }
            return models;
        }

        private static List<string> NormalizeMarkerIds(IEnumerable<string> markerIds)
        {
            if (markerIds == null)
                throw new ArgumentNullException(nameof(markerIds));
            var result = markerIds.Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (result.Count == 0)
                throw new ArgumentException("At least one markerId is required.", nameof(markerIds));
            foreach (var markerId in result)
                ValidateMarkerId(markerId);
            return result;
        }

        private static void ValidateMarkerId(string markerId)
        {
            if (!WorldMarkerArtifactPathPolicy.IsMarkerId(markerId))
                throw new ArgumentException("markerId is not a generated world-marker ID.", nameof(markerId));
        }

        private static string GetArtifactMarkerId(string artifactPath)
        {
            var fileName = System.IO.Path.GetFileNameWithoutExtension(artifactPath) ?? string.Empty;
            var separator = fileName.IndexOf("--", StringComparison.Ordinal);
            return separator > 0 ? fileName.Substring(0, separator) : string.Empty;
        }
    }
}
