using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NavisHelper.Agent.Contracts
{
    public sealed class ClashZoneLoadedModel
    {
        public int Index { get; set; }
        public string FileName { get; set; }
        public string SourceFileName { get; set; }
    }

    public sealed class ClashZoneMarkerPlan
    {
        public string Operation { get; set; }
        public string MarkerFilePath { get; set; }
        public bool Append { get; set; }
        public List<int> RemoveIndices { get; set; } = new List<int>();
        public List<int> StyleIndices { get; set; } = new List<int>();
        public double? Transparency { get; set; }
        public List<ClashZoneLoadedModel> LoadedMarkerModels { get; set; } = new List<ClashZoneLoadedModel>();
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public static class ClashZoneMarkerModelPlanner
    {
        public static ClashZoneMarkerPlan Plan(IList<ClashZoneLoadedModel> models, string operation,
            string markerPath, double? transparency)
        {
            operation = (operation ?? string.Empty).Trim().ToLowerInvariant();
            if (operation != "append" && operation != "replace" && operation != "remove" && operation != "style")
                throw new ArgumentException("Operation must be append, replace, remove or style.");
            var path = ResolveMarkerPath(markerPath);
            var plan = new ClashZoneMarkerPlan { Operation = operation, MarkerFilePath = path };
            plan.LoadedMarkerModels = (models ?? new List<ClashZoneLoadedModel>())
                .Where(model => model != null && (IsMarker(model.FileName) || IsMarker(model.SourceFileName)))
                .OrderBy(model => model.Index).ToList();
            var matching = plan.LoadedMarkerModels.Where(model => path == null ||
                MatchesPath(model.FileName, path) || MatchesPath(model.SourceFileName, path)).ToList();

            if (operation == "append" || operation == "replace")
            {
                if (path == null) throw new ArgumentException("A marker path is required for append or replace.");
                if (operation == "append" && (models ?? new List<ClashZoneLoadedModel>()).Any(model =>
                    model != null && (MatchesPath(model.FileName, path) || MatchesPath(model.SourceFileName, path))))
                    throw new ArgumentException("The marker file is already loaded; use replace instead.");
                plan.Append = true;
                if (operation == "replace")
                    plan.RemoveIndices = plan.LoadedMarkerModels.Select(model => model.Index).ToList();
            }
            else if (operation == "remove")
            {
                plan.RemoveIndices = matching.Select(model => model.Index).ToList();
                if (matching.Count == 0) plan.Warnings.Add("No clash-zone marker model is loaded.");
            }
            else
            {
                if (!transparency.HasValue || double.IsNaN(transparency.Value) ||
                    double.IsInfinity(transparency.Value) || transparency.Value < 0 || transparency.Value > 1)
                    throw new ArgumentException("Style requires a finite transparency between 0 and 1.");
                plan.StyleIndices = matching.Select(model => model.Index).ToList();
                plan.Transparency = transparency;
            }
            if (plan.RemoveIndices.RemoveAll(index => index == 0) > 0)
                plan.Warnings.Add("The first model of the document is never removed.");
            plan.RemoveIndices = plan.RemoveIndices.OrderByDescending(index => index).ToList();
            return plan;
        }

        private static string ResolveMarkerPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = path.Trim();
            if (!Path.HasExtension(path)) path = Path.Combine(path, "zones.ifc");
            else if (!string.Equals(Path.GetExtension(path), ".ifc", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The marker file must have the .ifc extension.");
            return Path.GetFullPath(path);
        }

        private static bool IsMarker(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try { return string.Equals(Path.GetFileName(path), "zones.ifc", StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
        }

        private static bool MatchesPath(string candidate, string path)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            try { return string.Equals(Path.GetFullPath(candidate), path, StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }
    }

    public sealed class ClashZoneMarkersRequest
    {
        public string Operation { get; set; }
        public string MarkerPath { get; set; }
        public double? Transparency { get; set; }
        public bool? Apply { get; set; }
    }

    public sealed class ClashZoneMarkersResponse
    {
        public string Operation { get; set; }
        public string MarkerFilePath { get; set; }
        public bool Applied { get; set; }
        public List<ClashZoneLoadedModel> LoadedMarkerModels { get; set; } = new List<ClashZoneLoadedModel>();
        public List<string> RemovedFiles { get; set; } = new List<string>();
        public bool Appended { get; set; }
        public int AppendedModelIndex { get; set; } = -1;
        public int StyledModelCount { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
        public string Message { get; set; }
    }
}
