using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Navisworks.Api;
using NavisHelper.Agent.Contracts;
using Newtonsoft.Json;

namespace NavisHelper.Agent.Services
{
    internal sealed partial class DocumentCommandService
    {
        private const string SavedViewpointsExportFormatCsv = "csv";
        private const string SavedViewpointsExportFormatJson = "json";
        private const string SavedViewpointsExportFormatMarkdown = "md";

        public SavedViewpointsExportResponse SavedViewpointsExport(Document document, SavedViewpointsExportRequest request)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (document.SavedViewpoints == null || document.SavedViewpoints.RootItem == null)
                throw new AgentCommandException(ErrorCodes.SavedViewpointNotFound, "The active document has no saved viewpoints.");
            if (string.IsNullOrWhiteSpace(request.OutputPath))
                throw new AgentCommandException(ErrorCodes.SchemaViolation, "OutputPath is required.");

            var includeItemIds = request.IncludeItemIds.GetValueOrDefault(true);
            var nodes = BuildSavedViewpointNodes(document.SavedViewpoints.RootItem);
            if (!includeItemIds)
            {
                foreach (var node in nodes)
                    node.Info.ItemId = null;
            }

            string outputPath;
            string format;
            try
            {
                outputPath = Path.GetFullPath(request.OutputPath);
                format = NormalizeSavedViewpointsExportFormat(request.Format, outputPath);
                var overwrite = request.Overwrite == true;
                if (File.Exists(outputPath) && !overwrite)
                    throw new AgentCommandException(ErrorCodes.CommandFailed, "Output file already exists. Pass overwrite=true or choose another outputPath.");

                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                if (string.Equals(format, SavedViewpointsExportFormatJson, StringComparison.OrdinalIgnoreCase))
                    File.WriteAllText(outputPath, JsonConvert.SerializeObject(nodes.Select(node => node.Info).ToList(), Formatting.Indented), Encoding.UTF8);
                else if (string.Equals(format, SavedViewpointsExportFormatMarkdown, StringComparison.OrdinalIgnoreCase))
                    File.WriteAllText(outputPath, BuildSavedViewpointsMarkdown(nodes), Encoding.UTF8);
                else
                    File.WriteAllText(outputPath, BuildSavedViewpointsCsv(nodes), Encoding.UTF8);
            }
            catch (AgentCommandException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                throw new AgentCommandException(ErrorCodes.CommandFailed, "Failed to write saved viewpoints export: " + ex.Message);
            }

            return new SavedViewpointsExportResponse
            {
                OutputPath = outputPath,
                Format = format,
                ExportedItemCount = nodes.Count,
                FolderCount = nodes.Count(node => node.Item is GroupItem),
                ViewpointCount = nodes.Count(node => node.Item is SavedViewpoint),
                Written = true,
            };
        }

        private static string NormalizeSavedViewpointsExportFormat(string format, string outputPath)
        {
            var value = (format ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(value))
                value = (Path.GetExtension(outputPath) ?? string.Empty).TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(value))
                value = SavedViewpointsExportFormatCsv;
            if (value == "markdown")
                value = SavedViewpointsExportFormatMarkdown;
            if (value == SavedViewpointsExportFormatCsv || value == SavedViewpointsExportFormatJson || value == SavedViewpointsExportFormatMarkdown)
                return value;

            throw new AgentCommandException(ErrorCodes.SchemaViolation, "Unsupported export format. Use csv, json, or md.");
        }

        private static string BuildSavedViewpointsCsv(IEnumerable<SavedViewpointNode> nodes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("itemId;type;name;path;parentPath;depth;index;childCount;containsVisibilityOverrides;containsAppearanceOverrides");
            foreach (var node in nodes)
            {
                var info = node.Info;
                sb.Append(EscapeSavedViewpointsCsv(info.ItemId)).Append(';');
                sb.Append(EscapeSavedViewpointsCsv(info.Type)).Append(';');
                sb.Append(EscapeSavedViewpointsCsv(info.Name)).Append(';');
                sb.Append(EscapeSavedViewpointsCsv(info.Path)).Append(';');
                sb.Append(EscapeSavedViewpointsCsv(info.ParentPath)).Append(';');
                sb.Append(info.Depth.ToString()).Append(';');
                sb.Append(info.Index.ToString()).Append(';');
                sb.Append(info.ChildCount.ToString()).Append(';');
                sb.Append(EscapeSavedViewpointsCsv(FormatNullableBool(info.ContainsVisibilityOverrides))).Append(';');
                sb.Append(EscapeSavedViewpointsCsv(FormatNullableBool(info.ContainsAppearanceOverrides))).AppendLine();
            }

            return sb.ToString();
        }

        private static string BuildSavedViewpointsMarkdown(IEnumerable<SavedViewpointNode> nodes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| itemId | type | name | path | parentPath | depth | index | childCount | visibilityOverrides | appearanceOverrides |");
            sb.AppendLine("| --- | --- | --- | --- | --- | ---: | ---: | ---: | --- | --- |");
            foreach (var node in nodes)
            {
                var info = node.Info;
                sb.Append("| ")
                    .Append(EscapeMarkdown(info.ItemId)).Append(" | ")
                    .Append(EscapeMarkdown(info.Type)).Append(" | ")
                    .Append(EscapeMarkdown(info.Name)).Append(" | ")
                    .Append(EscapeMarkdown(info.Path)).Append(" | ")
                    .Append(EscapeMarkdown(info.ParentPath)).Append(" | ")
                    .Append(info.Depth.ToString()).Append(" | ")
                    .Append(info.Index.ToString()).Append(" | ")
                    .Append(info.ChildCount.ToString()).Append(" | ")
                    .Append(EscapeMarkdown(FormatNullableBool(info.ContainsVisibilityOverrides))).Append(" | ")
                    .Append(EscapeMarkdown(FormatNullableBool(info.ContainsAppearanceOverrides))).AppendLine(" |");
            }

            return sb.ToString();
        }

        private static string EscapeSavedViewpointsCsv(string value)
        {
            value = value ?? string.Empty;
            if (value.IndexOfAny(new[] { ';', '"', '\r', '\n' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string EscapeMarkdown(string value)
        {
            return (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }

        private static string FormatNullableBool(bool? value)
        {
            return value.HasValue ? (value.Value ? "true" : "false") : string.Empty;
        }
    }
}
