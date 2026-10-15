using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;

namespace NavisHelper.Agent.Services
{
    internal sealed class ClashZoneMarkerService
    {
        public ClashZoneMarkersResponse Execute(Document document, ClashZoneMarkersRequest request)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            request = request ?? new ClashZoneMarkersRequest();
            ClashZoneMarkerPlan plan;
            try
            {
                // An existing folder is a run folder even when its name has a dot ("run.1"), which
                // the pure planner would read as a file extension.
                var markerPath = request.MarkerPath;
                if (!string.IsNullOrWhiteSpace(markerPath) && Directory.Exists(markerPath.Trim()))
                    markerPath = Path.Combine(markerPath.Trim(), "zones.ifc");
                plan = ClashZoneMarkerModelPlanner.Plan(Snapshot(document), request.Operation,
                    markerPath, request.Transparency);
                if (plan.Append)
                {
                    // Only zones.ifc belongs to this service, including files being appended.
                    if (!string.Equals(Path.GetFileName(plan.MarkerFilePath), "zones.ifc", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("The marker file must be named zones.ifc.");
                    if (!File.Exists(plan.MarkerFilePath))
                        throw new ArgumentException("Marker file does not exist: " + plan.MarkerFilePath);
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is PathTooLongException || ex is NotSupportedException)
            {
                // Path.GetFullPath reports an over-long or unsupported path outside ArgumentException.
                throw new AgentCommandException(ErrorCodes.SchemaViolation, ex.Message);
            }
            var response = new ClashZoneMarkersResponse {
                Operation = plan.Operation, MarkerFilePath = plan.MarkerFilePath,
                LoadedMarkerModels = plan.LoadedMarkerModels, Warnings = plan.Warnings
            };
            if (request.Apply != true)
            {
                response.RemovedFiles = plan.RemoveIndices.Select(index =>
                    plan.LoadedMarkerModels.First(model => model.Index == index).FileName).ToList();
                response.StyledModelCount = plan.StyleIndices.Count;
                response.Message = "Dry-run only. Pass apply=true to change the document's models.";
                return response;
            }
            try
            {
                foreach (var index in plan.RemoveIndices)
                {
                    var fileName = document.Models[index].FileName;
                    if (!TryRemove(document, index))
                        throw new InvalidOperationException("Could not remove marker model: " + fileName);
                    response.RemovedFiles.Add(fileName);
                }
                if (plan.Append)
                {
                    var firstNewIndex = document.Models.Count;
                    if (!TryAppend(document, plan.MarkerFilePath))
                        throw new InvalidOperationException("Could not append marker file: " + plan.MarkerFilePath);
                    response.Appended = true;
                    var appended = ClashZoneMarkerModelPlanner.Plan(Snapshot(document), "style", plan.MarkerFilePath, 0);
                    response.AppendedModelIndex = appended.StyleIndices.Where(index => index >= firstNewIndex).DefaultIfEmpty(-1).First();
                    if (response.AppendedModelIndex < 0)
                        throw new InvalidOperationException("The appended marker model could not be identified.");
                }
                foreach (var index in plan.StyleIndices)
                {
                    document.Models.OverridePermanentTransparency(
                        new[] { document.Models[index].RootItem }, plan.Transparency.Value);
                    response.StyledModelCount++;
                }
                response.Applied = true;
                response.Message = "Clash-zone marker operation applied. The document has not been saved.";
                return response;
            }
            catch (Exception ex)
            {
                throw new AgentCommandException(ErrorCodes.CommandFailed,
                    "Marker operation failed; the document may contain partial changes and has not been saved. " + ex.Message);
            }
        }

        private static List<ClashZoneLoadedModel> Snapshot(Document document)
        {
            var models = new List<ClashZoneLoadedModel>();
            for (var index = 0; index < document.Models.Count; index++)
            {
                var model = document.Models[index];
                models.Add(new ClashZoneLoadedModel {
                    Index = index, FileName = model.FileName, SourceFileName = model.SourceFileName
                });
            }
            return models;
        }

        private static bool TryRemove(Document document, int index)
        {
            try { return document.TryRemoveFile(index); }
            catch (MissingMethodException) { document.RemoveFile(index); return true; }
        }

        private static bool TryAppend(Document document, string path)
        {
            try { return document.TryAppendFile(path); }
            catch (MissingMethodException) { document.AppendFile(path); return true; }
        }
    }
}
